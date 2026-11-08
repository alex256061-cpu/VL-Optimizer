using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("VLOptimizer.Tests")]

namespace VLOptimizer.Core;

public record ToolkitFpsLiveProcess(int ProcessId, string Application, double AverageFps, double AverageFrameTimeMs, int Frames, double FrameDurationSeconds);
public record ToolkitFpsLiveSample(DateTimeOffset CapturedAt, double ElapsedSeconds, IReadOnlyList<ToolkitFpsLiveProcess> Processes,
    int RejectedRows, IReadOnlyList<string> Notices);

/// <summary>Incremental PresentMon CSV parser. Only complete lines contribute to live measurements.</summary>
public sealed class ToolkitFpsIncrementalParser
{
    private readonly int? _targetPid;
    private readonly StringBuilder _pending = new();
    private readonly Dictionary<(int Pid, string App), (int Count, double Sum)> _measurements = new();
    private readonly List<string> _notices = [];
    private string[]? _header;
    private int _application = -1, _pid = -1, _frame = -1, _rows, _rejected;
    private bool _overlong, _completed;
    private long _characters;
    public bool HasHeader => _header is not null;
    public int RejectedRows => _rejected;
    public ToolkitFpsIncrementalParser(int? targetProcessId = null)
    { if (targetProcessId is <= 0) throw new ArgumentOutOfRangeException(nameof(targetProcessId)); _targetPid = targetProcessId; }
    public void Append(ReadOnlySpan<char> fragment, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (_completed) throw new InvalidOperationException("O fluxo CSV já foi concluído.");
        if (_characters + fragment.Length > 512L * 1024 * 1024) throw new InvalidDataException("CSV excede o limite de leitura.");
        _characters += fragment.Length;
        foreach (char c in fragment)
        {
            if (c == '\n') ConsumeLine();
            else if (_pending.Length < 65536) _pending.Append(c);
            else _overlong = true;
        }
    }
    /// <summary>Call only after the writer has finished; accepts an unterminated final CSV row.</summary>
    public void Complete(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); if (_completed) return;
        if (_pending.Length > 0 || _overlong) ConsumeLine();
        _completed = true;
    }
    public ToolkitFpsLiveSample Snapshot(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        var processes = _measurements.Select(m => new ToolkitFpsLiveProcess(m.Key.Pid, m.Key.App,
            Math.Round(m.Value.Count * 1000 / m.Value.Sum, 1, MidpointRounding.AwayFromZero),
            Math.Round(m.Value.Sum / m.Value.Count, 2, MidpointRounding.AwayFromZero), m.Value.Count, m.Value.Sum / 1000)).OrderByDescending(p => p.Frames).ToArray();
        return new(DateTimeOffset.UtcNow, elapsedSeconds, Array.AsReadOnly(processes), _rejected,
            _header is null ? new[] { "Aguardando cabeçalho e frames do CSV da sessão própria." } : _notices.AsReadOnly());
    }
    private void ConsumeLine()
    {
        string line = _pending.ToString().TrimEnd('\r'); _pending.Clear(); bool longLine = _overlong; _overlong = false;
        if (_header is null)
        {
            if (longLine) throw new InvalidDataException("Cabeçalho CSV excede 64 KiB.");
            if (string.IsNullOrWhiteSpace(line)) return;
            try { _header = ToolkitFpsService.ParseCsvLine(line).Select(v => v.Trim().TrimStart('\uFEFF')).ToArray(); }
            catch (FormatException ex) { throw new InvalidDataException("Cabeçalho CSV inválido.", ex); }
            int Find(params string[] names) => Array.FindIndex(_header, value => names.Contains(value, StringComparer.OrdinalIgnoreCase));
            _application = Find("Application"); _pid = Find("ProcessID"); _frame = Find("MsBetweenPresents", "CPUFrameTime");
            if (_application < 0 || _pid < 0 || _frame < 0) throw new InvalidDataException("CSV PresentMon requer Application, ProcessID e MsBetweenPresents ou CPUFrameTime.");
            _notices.Add($"Métrica acumulada: {_header[_frame]} em ms. Frames de múltiplas swapchains do mesmo processo são agregados.");
            return;
        }
        if (++_rows > 2_000_000) throw new InvalidDataException("O CSV excede dois milhões de linhas.");
        if (longLine) { _rejected++; return; }
        string[] values;
        try { values = ToolkitFpsService.ParseCsvLine(line); } catch (FormatException) { _rejected++; return; }
        if (values.Length <= Math.Max(_application, Math.Max(_pid, _frame)) || string.IsNullOrWhiteSpace(values[_application]) ||
            !int.TryParse(values[_pid], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid <= 0 ||
            !double.TryParse(values[_frame], NumberStyles.Float, CultureInfo.InvariantCulture, out double frame) || !double.IsFinite(frame) || frame <= 0 || frame >= 1000)
        { _rejected++; return; }
        if (_targetPid is int selected && pid != selected) return;
        var key = (pid, values[_application].Trim());
        if (!_measurements.TryGetValue(key, out var state) && _measurements.Count >= 256) throw new InvalidDataException("CSV contém mais de 256 identidades de processo.");
        _measurements[key] = (state.Count + 1, state.Sum + frame);
    }
}

/// <summary>Capture calls this reader only with its own newly generated CSV path. It never enumerates temp files.</summary>
internal static class ToolkitFpsCsvTail
{
    internal static async Task FollowAsync(string csvPath, int? processId, IProgress<ToolkitFpsLiveSample>? progress,
        Stopwatch elapsed, CancellationToken token, TimeSpan? pollInterval = null)
    {
        csvPath = ToolkitPaths.Full(csvPath);
        var parser = new ToolkitFpsIncrementalParser(processId);
        var raw = new byte[16384]; var chars = new char[32768]; var firstBytes = new List<byte>(4);
        Decoder? decoder = null; FileStream? stream = null; long position = 0, total = 0; string? readNotice = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (stream is null && File.Exists(csvPath))
                    {
                        ToolkitPaths.EnsureNoLinks(csvPath);
                        stream = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16384, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        if (stream.Length < position) throw new InvalidDataException("O CSV da sessão foi truncado durante a captura.");
                        stream.Position = position;
                    }
                    if (stream is not null)
                    {
                        if (stream.Length > 512L * 1024 * 1024) throw new InvalidDataException("O CSV da sessão excede 512 MiB.");
                        if (stream.Length < position) throw new InvalidDataException("O CSV da sessão foi truncado durante a captura.");
                        int count;
                        while ((count = await stream.ReadAsync(raw, token).ConfigureAwait(false)) > 0)
                        {
                            position += count; total += count;
                            if (total > 512L * 1024 * 1024) throw new InvalidDataException("O CSV da sessão excede 512 MiB.");
                            int start = 0;
                            if (decoder is null)
                            {
                                while (firstBytes.Count < 4 && start < count) firstBytes.Add(raw[start++]);
                                if (firstBytes.Count < 4) continue;
                                var prefix = firstBytes.ToArray(); int skip;
                                Encoding encoding;
                                if (prefix.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) { encoding = Encoding.UTF8; skip = 3; }
                                else if (prefix.AsSpan().SequenceEqual(new byte[] { 0xff, 0xfe, 0, 0 })) { encoding = Encoding.UTF32; skip = 4; }
                                else if (prefix.AsSpan().SequenceEqual(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, true); skip = 4; }
                                else if (prefix[0] == 0xff && prefix[1] == 0xfe) { encoding = Encoding.Unicode; skip = 2; }
                                else if (prefix[0] == 0xfe && prefix[1] == 0xff) { encoding = Encoding.BigEndianUnicode; skip = 2; }
                                else { encoding = Encoding.UTF8; skip = 0; }
                                decoder = encoding.GetDecoder();
                                int initialChars = decoder.GetChars(prefix, skip, prefix.Length - skip, chars, 0, flush: false);
                                parser.Append(chars.AsSpan(0, initialChars), token); firstBytes.Clear();
                            }
                            int decoded = decoder.GetChars(raw, start, count - start, chars, 0, flush: false);
                            parser.Append(chars.AsSpan(0, decoded), token);
                            token.ThrowIfCancellationRequested();
                        }
                        readNotice = null;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                {
                    // A writer that temporarily denies sharing does not invent a measurement or abort a valid capture.
                    stream?.Dispose(); stream = null;
                    readNotice = "CSV temporariamente indisponível para leitura: " + ex.Message;
                }
                token.ThrowIfCancellationRequested();
                var sample = parser.Snapshot(elapsed.Elapsed.TotalSeconds);
                if (readNotice is not null) sample = sample with { Notices = sample.Notices.Concat(new[] { readNotice }).ToArray() };
                progress?.Report(sample);
                await Task.Delay(pollInterval ?? TimeSpan.FromMilliseconds(500), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { if (stream is not null) await stream.DisposeAsync().ConfigureAwait(false); }
    }
}
