using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace VLOptimizer.Core;

/// <summary>Streams live frame data from PresentMon the way the reference implementation does:
/// a long-running PresentMon writing CSV to stdout, read line by line, filtered per target process in
/// code. No <c>--timed</c>, no <c>--output_file</c> — that combination silently produces no file on
/// machines with many active ETW sessions, while the stdout stream keeps working.
///
/// <para><c>--exclude dwm.exe</c> drops the desktop compositor's presents, which otherwise dominate the
/// stream and drown the game.</para></summary>
public sealed class ToolkitFpsStreamReader : IAsyncDisposable
{
    private readonly string executablePath;
    private readonly string sessionName;
    private Process? process;
    private CancellationTokenSource? stop;
    private Task? pump;

    public ToolkitFpsStreamReader(string executablePath, string sessionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        this.executablePath = Path.GetFullPath(executablePath);
        this.sessionName = sessionName;
    }

    public bool IsRunning => process is { HasExited: false };
    public string? LastError { get; private set; }

    /// <summary>Starts the collector. <paramref name="onFrame"/> receives every accepted present for
    /// one process. Throws when the executable is missing, so the caller reports it instead of
    /// waiting for frames that will never come.</summary>
    public void Start(int targetProcessId, Action<ToolkitFrame> onFrame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        if (process is not null) throw new InvalidOperationException("O coletor já está em execução.");
        if (!File.Exists(executablePath)) throw new FileNotFoundException("PresentMon não encontrado.", executablePath);

        var start = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
        };
        // Same flag set as the reference implementation; --exclude keeps the compositor out.
        foreach (var argument in new[] { "--output_stdout", "--no_console_stats", "--stop_existing_session",
            "--session_name", sessionName, "--no_track_input", "--no_track_gpu", "--exclude", "dwm.exe" })
        {
            start.ArgumentList.Add(argument);
        }

        var processStart = new Process { StartInfo = start };
        if (!processStart.Start()) throw new IOException("Não foi possível iniciar o PresentMon.");
        process = processStart;
        stop = new CancellationTokenSource();
        var token = stop.Token;
        var stderr = new StringBuilder();

        // stderr carries the access-denied message; keep it bounded and surface it on exit.
        _ = Task.Run(async () =>
        {
            try
            {
                var buffer = new char[4096];
                int read;
                while ((read = await processStart.StandardError.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    if (stderr.Length < 8192) stderr.Append(buffer, 0, read);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        }, token);

        processStart.EnableRaisingEvents = true;
        processStart.Exited += (_, _) =>
        {
            if (processStart.ExitCode != 0)
            {
                var message = stderr.ToString();
                LastError = message.Contains("access", StringComparison.OrdinalIgnoreCase) ||
                            message.Contains("privilege", StringComparison.OrdinalIgnoreCase)
                    ? "O Windows bloqueou a medição: a captura de FPS exige o grupo Usuários de log de desempenho ou execução como administrador."
                    : "O medidor parou (código " + processStart.ExitCode + ").";
            }
        };

        pump = Task.Run(() => PumpAsync(processStart, targetProcessId, onFrame, token), token);
    }

    private static async Task PumpAsync(Process source, int targetProcessId, Action<ToolkitFrame> onFrame, CancellationToken token)
    {
        int applicationColumn = -1, processColumn = -1, frameColumn = -1, swapColumn = -1;
        bool headerRead = false;
        var buffer = new char[8192];
        var pending = new StringBuilder();

        try
        {
            int read;
            while ((read = await source.StandardOutput.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                pending.Append(buffer, 0, read);
                int newline;
                while ((newline = IndexOfNewline(pending)) >= 0)
                {
                    var line = pending.ToString(0, newline).TrimEnd('\r');
                    pending.Remove(0, newline + 1);
                    if (line.Length == 0) continue;

                    if (!headerRead)
                    {
                        var header = SplitCsv(line);
                        headerRead = true;
                        (applicationColumn, processColumn, frameColumn, swapColumn) = LocateColumns(header);
                        if (processColumn < 0 || frameColumn < 0) return;  // not a frame CSV
                        continue;
                    }

                    if (!TryParseRow(line, processColumn, frameColumn, out int pid, out double ms)) continue;
                    if (pid != targetProcessId) continue;
                    if (ms <= 0 || ms >= 1000) continue;

                    var cells = SplitCsv(line);
                    string application = applicationColumn >= 0 && applicationColumn < cells.Length ? cells[applicationColumn] : "?";
                    string? swap = swapColumn >= 0 && swapColumn < cells.Length && cells[swapColumn].Length > 0 ? cells[swapColumn] : null;
                    onFrame(new ToolkitFrame(pid, application, ms, null, swap, null));
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
    }

    private static int IndexOfNewline(StringBuilder builder)
    {
        for (int i = 0; i < builder.Length; i++) if (builder[i] == '\n') return i;
        return -1;
    }

    private static (int Application, int Process, int Frame, int Swap) LocateColumns(string[] header)
    {
        int application = -1, process = -1, frame = -1, swap = -1;
        for (int i = 0; i < header.Length; i++)
        {
            var name = header[i].TrimStart('\uFEFF');
            if (name.Equals("Application", StringComparison.OrdinalIgnoreCase)) application = i;
            else if (name.Equals("ProcessID", StringComparison.OrdinalIgnoreCase)) process = i;
            else if (name.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase)) frame = i;
            else if (name.Equals("SwapChainAddress", StringComparison.OrdinalIgnoreCase)) swap = i;
        }
        return (application, process, frame, swap);
    }

    private static bool TryParseRow(string line, int processColumn, int frameColumn, out int processId, out double milliseconds)
    {
        processId = 0;
        milliseconds = 0;
        var cells = SplitCsv(line);
        int needed = Math.Max(processColumn, frameColumn);
        if (cells.Length <= needed) return false;
        if (!int.TryParse(cells[processColumn], NumberStyles.Integer, CultureInfo.InvariantCulture, out processId)) return false;
        if (!double.TryParse(cells[frameColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out milliseconds)) return false;
        return true;
    }

    /// <summary>Minimal CSV split that honours double quotes, matching the format PresentMon emits.</summary>
    private static string[] SplitCsv(string line)
    {
        var cells = new List<string>(24);
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
            else if (c == '"' && cell.Length == 0) quoted = true;
            else cell.Append(c);
        }
        cells.Add(cell.ToString());
        return cells.ToArray();
    }

    public async Task StopAsync()
    {
        stop?.Cancel();
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception) { }
        if (pump is not null) { try { await pump.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        process?.Dispose();
        process = null;
        pump = null;
        stop?.Dispose();
        stop = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
