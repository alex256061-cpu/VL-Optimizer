using System.Diagnostics;

namespace VLOptimizer.Core;

public sealed record LiveFpsStatus(
    bool Running,
    string? Error,
    IReadOnlyList<LiveFrameStats> Processes,
    LiveFrameStats? Target,
    int? TargetProcessId,
    string? Notice = null);

/// <summary>Owns the live FPS meter: one long-running PresentMon streaming CSV to stdout, filtered and
/// aggregated in code, exactly like the reference tool.
///
/// <para>No <c>--timed</c> and no <c>--output_file</c>: that combination returns success while writing
/// nothing on machines with a busy ETW pool. The stream keeps delivering, and errors are translated to
/// a message the user can act on instead of a raw exit code.</para></summary>
public sealed class LiveFpsService : IAsyncDisposable
{
    public const string SessionName = "VL_FPS";

    private readonly object gate = new();
    private ToolkitFpsStreamReader? reader;
    private LiveFrameStatistics statistics = new();
    private string? error;
    private string? notice;
    private int? targetProcessId;

    public bool IsRunning { get { lock (gate) return reader?.IsRunning == true; } }
    public string? Error { get { lock (gate) return error; } }

    /// <summary>Non-fatal information from the last start, such as how many stale sessions were
    /// cleaned. Shown as a notice, never as a failure.</summary>
    public string? Notice { get { lock (gate) return notice; } }
    public LiveFrameStatistics Statistics { get { lock (gate) return statistics; } }

    /// <summary>Starts the meter. Returns false with a message when PresentMon is missing, so the
    /// caller shows the reason instead of polling an empty sensor forever.
    /// <para>Stale trace sessions from earlier runs are cleared first. That step is best-effort: when
    /// it fails the meter still starts, because a broken cleanup must not block measurement.</para></summary>
    public (bool Ok, string Message) Start(string presentMonPath, int? pinProcessId = null)
    {
        lock (gate)
        {
            if (reader?.IsRunning == true) return (true, "Medidor já em execução.");
            if (!File.Exists(presentMonPath)) return (false, "PresentMon não encontrado: " + presentMonPath);

            notice = null;
            try
            {
                var cleanup = new EtwSessionCleaner().CleanStaleSessions(keepSession: null);
                if (cleanup.Removed > 0) notice = cleanup.Message;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Never let cleanup decide whether measuring is possible.
                notice = "A limpeza de sessões anteriores falhou, seguindo sem ela.";
            }

            statistics = new LiveFrameStatistics();
            if (pinProcessId is > 0) { statistics.Pin(pinProcessId); statistics.RegisterGameProcess(pinProcessId.Value); }
            error = null;
            var created = new ToolkitFpsStreamReader(presentMonPath, SessionName);
            try
            {
                created.Start(pinProcessId ?? 0, OnFrame);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or FileNotFoundException)
            {
                created.DisposeAsync().AsTask().GetAwaiter().GetResult();
                error = ex.Message;
                return (false, ex.Message);
            }
            reader = created;
            return (true, "Medidor iniciado. Os números aparecem assim que o jogo apresentar quadros.");
        }
    }

    private void OnFrame(ToolkitFrame frame)
    {
        lock (gate)
        {
            statistics.Add(frame.ProcessId, frame.Application, frame.Milliseconds,
                independentFlip: frame.PresentMode is { } mode &&
                    (mode.Contains("Independent Flip", StringComparison.OrdinalIgnoreCase) ||
                     mode.Contains("Hardware Composed", StringComparison.OrdinalIgnoreCase)));
        }
    }

    /// <summary>A snapshot for the UI poll. Target selection and pruning happen here so a page refresh
    /// always reflects the same instant.</summary>
    public LiveFpsStatus Snapshot()
    {
        lock (gate)
        {
            if (reader is null) return new(false, error, [], null, null, notice);
            var running = reader.IsRunning;
            // The stream sets its own message when PresentMon exits non-zero; surface it once.
            if (!running && reader.LastError is { } reported && error is null) error = reported;
            targetProcessId = statistics.SelectTarget();
            var target = targetProcessId is null ? null : statistics.Stats(targetProcessId.Value);
            var all = targetProcessId is null ? [] : new[] { target }.OfType<LiveFrameStats>().ToArray();
            return new(running, error, all, target, targetProcessId, notice);
        }
    }

    public async Task StopAsync()
    {
        ToolkitFpsStreamReader? stopping;
        lock (gate) { stopping = reader; reader = null; }
        if (stopping is not null)
        {
            try { await stopping.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
        }
        // Best-effort: close the trace session this meter owned, so the next start does not inherit it.
        try
        {
            var cleanup = new EtwSessionCleaner().CleanStaleSessions();
            if (cleanup.Removed > 0) lock (gate) notice = cleanup.Message;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
