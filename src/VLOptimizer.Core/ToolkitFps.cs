using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace VLOptimizer.Core;

/// <summary>Measured frametimes only. Capture requires a separately supplied PresentMon executable.</summary>
public sealed class ToolkitFpsService
{
    private readonly SemaphoreSlim _capture = new(1, 1);
    public static ToolkitFpsStatistics? AnalyzeFrametimes(IEnumerable<double> values)
    {
        var frames = values.Where(v => double.IsFinite(v) && v > 0 && v < 1000).ToArray();
        if (frames.Length == 0) return null;
        double sum = frames.Sum(); var slowest = frames.OrderByDescending(v => v).ToArray();
        double Low(double fraction) => Round(1000 / slowest.Take(Math.Max(1, (int)Math.Floor(frames.Length * fraction))).Average(), 1);
        return new(Round(frames.Length * 1000 / sum, 1), Low(.01), Low(.001), Round(sum / frames.Length, 2), frames.Length, sum / 1000);
    }
    private static double Round(double value, int digits) => Math.Round(value, digits, MidpointRounding.AwayFromZero);
    public static ToolkitFpsComparison Compare(ToolkitFpsStatistics before, ToolkitFpsStatistics after)
    {
        if (before.Fps <= 0 || before.Low1 <= 0) throw new ArgumentException("A medição anterior deve ser maior que zero.");
        return new(before, after, Round((after.Fps - before.Fps) / before.Fps * 100, 1), Round((after.Low1 - before.Low1) / before.Low1 * 100, 1));
    }
    public async Task<ToolkitFpsReport> ImportCsvAsync(string path, CancellationToken token = default)
    {
        path = ToolkitPaths.Full(path); ToolkitPaths.EnsureNoLinks(path);
        if (new FileInfo(path).Length > 512L * 1024 * 1024) throw new InvalidDataException("O CSV excede o limite de 512 MiB.");
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        return await ImportCsvAsync(reader, path, token).ConfigureAwait(false);
    }
    public async Task<ToolkitFpsReport> ImportCsvAsync(TextReader reader, string source = "CSV", CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var frames = new Dictionary<(int Pid, string App), List<double>>();
        var chains = new Dictionary<(int Pid, string App), HashSet<string>>();
        var notices = new List<string>(); int rejected = 0, rowCount = 0;
        var lines = new LimitedLineReader(reader);
        string? line;
        do { line = await lines.ReadAsync(token).ConfigureAwait(false); } while (line is not null && string.IsNullOrWhiteSpace(line));
        if (line is null) throw new InvalidDataException("O CSV está vazio.");
        if (lines.Overlong) throw new InvalidDataException("Cabeçalho CSV excede 64 KiB.");
        var header = ParseCsvLine(line).Select(s => s.Trim().TrimStart('\uFEFF')).ToArray();
        int Find(params string[] names) => Array.FindIndex(header, h => names.Any(n => n.Equals(h, StringComparison.OrdinalIgnoreCase)));
        int appIndex = Find("Application"), pidIndex = Find("ProcessID"), msIndex = Find("MsBetweenPresents", "CPUFrameTime"), chainIndex = Find("SwapChainAddress");
        if (appIndex < 0 || pidIndex < 0 || msIndex < 0)
            throw new InvalidDataException("CSV PresentMon requer Application, ProcessID e MsBetweenPresents ou CPUFrameTime.");
        notices.Add($"Métrica: {header[msIndex]} em milissegundos. FPS corresponde ao ritmo dos frames medidos, sem inferir frames exibidos pelo monitor.");
        while ((line = await lines.ReadAsync(token).ConfigureAwait(false)) is not null)
        {
            token.ThrowIfCancellationRequested();
            if (++rowCount > 2_000_000) throw new InvalidDataException("O CSV excede dois milhões de linhas; divida a captura.");
            if (lines.Overlong) { rejected++; continue; }
            string[] cells;
            try { cells = ParseCsvLine(line); } catch (FormatException) { rejected++; continue; }
            if (cells.Length <= Math.Max(appIndex, Math.Max(pidIndex, msIndex)) || string.IsNullOrWhiteSpace(cells[appIndex]) ||
                !int.TryParse(cells[pidIndex], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid <= 0 ||
                !double.TryParse(cells[msIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) || !double.IsFinite(ms) || ms <= 0 || ms >= 1000)
            { rejected++; continue; }
            var key = (pid, cells[appIndex].Trim());
            if (!frames.TryGetValue(key, out var list)) { list = []; frames.Add(key, list); chains.Add(key, new(StringComparer.OrdinalIgnoreCase)); }
            list.Add(ms);
            if (chainIndex >= 0 && cells.Length > chainIndex && !string.IsNullOrWhiteSpace(cells[chainIndex])) chains[key].Add(cells[chainIndex]);
        }
        if (chains.Any(c => c.Value.Count > 1)) notices.Add("Há processos com múltiplas swapchains; o resultado por processo agrega essas cadeias e deve ser interpretado com esse limite.");
        notices.Add("Comparações Antes/Depois exigem a mesma cena, resolução e condições; uma diferença isolada não comprova efeito de ajuste.");
        var processes = frames.Select(f => new ToolkitFpsProcess(f.Key.Pid, f.Key.App, AnalyzeFrametimes(f.Value)!)).OrderByDescending(p => p.Statistics.Frames).ToArray();
        return new(source, Array.AsReadOnly(processes), rejected, notices.AsReadOnly());
    }
    internal static string[] ParseCsvLine(string line)
    {
        var result = new List<string>(); var cell = new StringBuilder(); bool quoted = false, endedQuote = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') { quoted = false; endedQuote = true; }
                else cell.Append(c);
            }
            else if (c == ',') { result.Add(cell.ToString()); cell.Clear(); endedQuote = false; }
            else if (c == '"' && cell.Length == 0 && !endedQuote) quoted = true;
            else if (endedQuote && !char.IsWhiteSpace(c)) throw new FormatException("CSV com aspas inválidas.");
            else if (!endedQuote) cell.Append(c);
        }
        if (quoted) throw new FormatException("Campo multilinha ou aspas não terminadas; linha descartada.");
        result.Add(cell.ToString()); return result.ToArray();
    }
    private sealed class LimitedLineReader(TextReader reader)
    {
        private readonly char[] _buffer = new char[4096]; private int _position, _length; private long _read;
        public bool Overlong { get; private set; }
        public async Task<string?> ReadAsync(CancellationToken token)
        {
            var value = new StringBuilder(); Overlong = false; bool any = false;
            while (true)
            {
                if (_position == _length)
                {
                    _length = await reader.ReadAsync(_buffer.AsMemory(), token).ConfigureAwait(false); _position = 0;
                    _read += _length; if (_read > 512L * 1024 * 1024) throw new InvalidDataException("CSV excede o limite de leitura.");
                    if (_length == 0) return any ? value.ToString().TrimEnd('\r') : null;
                }
                char c = _buffer[_position++]; any = true;
                if (c == '\n') return value.ToString().TrimEnd('\r');
                if (value.Length < 65536) value.Append(c); else Overlong = true;
            }
        }
    }

    public Task<ToolkitCaptureResult> CaptureAsync(string presentMonExe, int processId, int seconds, CancellationToken token = default)
        => CaptureAsync(presentMonExe, processId, seconds, null, token);
    public async Task<ToolkitCaptureResult> CaptureAsync(string presentMonExe, int processId, int seconds,
        IProgress<ToolkitFpsLiveSample>? progress, CancellationToken token = default)
    {
        if (!new WindowsSystemAccess().IsSupportedOS) throw new PlatformNotSupportedException("Captura disponível em Windows 10/11 x64 cliente.");
        if (processId <= 4 || processId == Environment.ProcessId || seconds is not (15 or 30 or 60 or 120)) throw new ArgumentException("Selecione um PID de jogo e duração de 15, 30, 60 ou 120 segundos.");
        presentMonExe = ToolkitPaths.Full(presentMonExe); ToolkitPaths.EnsureNoLinks(presentMonExe);
        if (!Path.GetExtension(presentMonExe).Equals(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(presentMonExe)) throw new FileNotFoundException("Selecione o executável externo PresentMon.", presentMonExe);
        using (var target = Process.GetProcessById(processId)) if (target.HasExited) throw new InvalidOperationException("O processo selecionado já encerrou.");
        if (!await _capture.WaitAsync(0, token).ConfigureAwait(false)) throw new InvalidOperationException("Já existe uma captura nesta sessão.");
        var session = "VL_" + Guid.NewGuid().ToString("N");
        var csvPath = Path.Combine(Path.GetTempPath(), session + ".csv");
        try
        {
            var start = new ProcessStartInfo(presentMonExe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(presentMonExe)! };
            foreach (var argument in new[] { "--process_id", processId.ToString(CultureInfo.InvariantCulture), "--output_file", csvPath,
                "--timed", seconds.ToString(CultureInfo.InvariantCulture), "--terminate_after_timed", "--no_console_stats", "--session_name", session, "--no_track_input", "--no_track_gpu" }) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            if (!process.Start()) throw new IOException("Não foi possível iniciar PresentMon.");
            var stderr = ReadBoundedAsync(process.StandardError, 64 * 1024); var stdout = ReadBoundedAsync(process.StandardOutput, 64 * 1024);
            var watch = Stopwatch.StartNew();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(seconds + 20));
            using var monitorCancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            // Only this unpredictable, per-capture path is polled. No scan of the temp folder or another session.
            var monitor = ToolkitFpsCsvTail.FollowAsync(csvPath, processId, progress, watch, monitorCancel.Token);
            try
            {
                var exited = process.WaitForExitAsync(timeout.Token);
                var completed = await Task.WhenAny(exited, monitor).ConfigureAwait(false);
                if (completed == monitor) { await monitor.ConfigureAwait(false); token.ThrowIfCancellationRequested(); }
                await exited.ConfigureAwait(false);
            }
            catch
            {
                // A malformed/oversized CSV, cancellation or timeout stops only our collector/session.
                if (!process.HasExited) { try { process.Kill(); } catch (InvalidOperationException) { } }
                await StopOwnedSessionAsync(presentMonExe, session).ConfigureAwait(false);
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await process.WaitForExitAsync(stopTimeout.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
                token.ThrowIfCancellationRequested();
                if (timeout.IsCancellationRequested) throw new TimeoutException("O coletor excedeu o prazo; somente a sessão própria foi encerrada.");
                throw;
            }
            finally
            {
                await monitorCancel.CancelAsync().ConfigureAwait(false);
                await monitor.ConfigureAwait(false);
            }
            string error = await stderr.ConfigureAwait(false); await stdout.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException($"PresentMon encerrou com código {process.ExitCode}: {error}");
            if (!File.Exists(csvPath)) throw new IOException("PresentMon não produziu CSV; confira a versão, permissões e atividade gráfica do processo.");
            var report = await ImportCsvAsync(csvPath, token).ConfigureAwait(false);
            if (report.Processes.Count == 0) throw new InvalidDataException("Não houve frames válidos na captura. Nenhum FPS foi estimado.");
            progress?.Report(new ToolkitFpsLiveSample(DateTimeOffset.UtcNow, watch.Elapsed.TotalSeconds,
                report.Processes.Where(p => p.ProcessId == processId).Select(p => new ToolkitFpsLiveProcess(p.ProcessId, p.Application,
                    p.Statistics.Fps, p.Statistics.FrameTimeMs, p.Statistics.Frames, p.Statistics.FrameDurationSeconds)).ToArray(),
                report.RejectedRows, report.Notices.Concat(new[] { "Captura encerrada; lows disponíveis no relatório final." }).ToArray()));
            return new(report with { Source = "PresentMon — sessão " + session }, process.ExitCode, error, watch.Elapsed.TotalSeconds >= seconds - 1);
        }
        finally
        {
            try { if (File.Exists(csvPath)) File.Delete(csvPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            _capture.Release();
        }
    }
    private static async Task StopOwnedSessionAsync(string executable, string session)
    {
        try
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "--terminate_existing_session", "--session_name", session }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info); if (process is null) return;
            var output = ReadBoundedAsync(process.StandardOutput, 16 * 1024); var error = ReadBoundedAsync(process.StandardError, 16 * 1024);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); } catch (OperationCanceledException) { if (!process.HasExited) process.Kill(); }
            await Task.WhenAll(output, error).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
    internal static async Task<string> ReadBoundedAsync(StreamReader reader, int maximum)
    {
        var result = new StringBuilder(); var buffer = new char[4096]; int n;
        while ((n = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0) if (result.Length < maximum) result.Append(buffer, 0, Math.Min(n, maximum - result.Length));
        return result.ToString();
    }
}
