using System.Diagnostics;

namespace VLOptimizer.Core;

/// <summary>Live frame statistics for one process, matching the reference tool's shape: a rolling
/// 10-second window of presents, with average, 1% low, 0.1% low and mean frametime.</summary>
public sealed record LiveFrameStats(string Application, int ProcessId, double Fps, double Low1, double Low01, double FrameTimeMs, int Frames, bool IndependentFlip);

/// <summary>Rolling frame collector: a port of the reference implementation's fps service.
///
/// <para>Constants are the same on purpose — window 10 s, idle timeout 15 s, prune 3 s — because a
/// different window would report different numbers for the same scene and make comparisons across
/// tools meaningless.</para>
///
/// <para>Target selection prefers, in order: the pinned pid, a known game executable, an
/// independent-flip presenter, then the process with the most recent frames. Ignored executables
/// (the desktop compositor and browsers) never become the target.</para></summary>
public sealed class LiveFrameStatistics
{
    public const int WindowMs = 10000;
    public const int IdleMs = 15000;
    private const int PruneMs = 3000;

    /// <summary>Processes that always present frames but are never the game the user is measuring.</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm.exe", "explorer.exe", "chrome.exe", "msedge.exe", "firefox.exe",
        "opera.exe", "brave.exe", "firefox.exe", "SearchApp.exe",
    };

    private sealed class TrackedProcess
    {
        public TrackedProcess(string name) => Name = name;
        public string Name { get; }
        public List<(long At, double Milliseconds)> Frames { get; } = [];
        public long LastAt { get; set; }
        public bool IndependentFlip { get; set; }
    }

    private readonly object gate = new();
    private readonly Dictionary<int, TrackedProcess> processes = [];
    private readonly HashSet<int> knownGameProcessIds = [];
    private int? pinned;
    private int selfProcessId = Environment.ProcessId;

    /// <summary>Pins the measured process so target auto-selection does not drift.</summary>
    public void Pin(int? processId)
    {
        lock (gate) pinned = processId;
    }

    /// <summary>Marks an executable name as a game; such processes win target selection.</summary>
    public void RegisterGameProcess(int processId)
    {
        lock (gate) knownGameProcessIds.Add(processId);
    }

    /// <summary>Adds one accepted present. Frames outside the window are dropped as they age out.</summary>
    public void Add(int processId, string application, double milliseconds, bool independentFlip)
    {
        if (processId <= 0 || processId == selfProcessId) return;
        var name = application.Trim();
        if (name.Length == 0 || Ignored.Contains(name)) return;
        if (!(milliseconds > 0 && milliseconds < 1000)) return;

        long now = Environment.TickCount64;
        lock (gate)
        {
            if (!processes.TryGetValue(processId, out var tracked))
            {
                tracked = new TrackedProcess(name);
                processes[processId] = tracked;
            }
            tracked.LastAt = now;
            tracked.IndependentFlip = independentFlip;
            tracked.Frames.Add((now, milliseconds));

            long cutoff = now - WindowMs;
            int drop = 0;
            while (drop < tracked.Frames.Count && tracked.Frames[drop].At < cutoff) drop++;
            if (drop > 0) tracked.Frames.RemoveRange(0, drop);
        }
    }

    /// <summary>Drops processes that stopped presenting, then returns the current target.</summary>
    public int? SelectTarget()
    {
        long now = Environment.TickCount64;
        lock (gate)
        {
            foreach (var stale in processes.Where(p => p.Value.LastAt < now - PruneMs).Select(p => p.Key).ToArray())
            {
                processes.Remove(stale);
            }
            if (pinned is { } pin && processes.ContainsKey(pin)) return pin;

            int? best = null;
            long bestScore = -1;
            foreach (var (processId, tracked) in processes)
            {
                long score = (knownGameProcessIds.Contains(processId) ? 1_000_000 : 0)
                           + (tracked.IndependentFlip ? 100_000 : 0)
                           + tracked.Frames.Count;
                if (score > bestScore) { bestScore = score; best = processId; }
            }
            return best;
        }
    }

    /// <summary>Statistics for one process, or null when it has no frames in the window.</summary>
    public LiveFrameStats? Stats(int processId)
    {
        double[] frames;
        string name;
        bool flip;
        lock (gate)
        {
            if (!processes.TryGetValue(processId, out var tracked) || tracked.Frames.Count == 0) return null;
            frames = tracked.Frames.Select(f => f.Milliseconds).ToArray();
            name = tracked.Name;
            flip = tracked.IndependentFlip;
        }
        return Compute(name, processId, frames, flip);
    }

    /// <summary>Stats for the auto-selected target.</summary>
    public LiveFrameStats? TargetStats()
    {
        var target = SelectTarget();
        return target is null ? null : Stats(target.Value);
    }

    /// <summary>Average plus percentile lows over an arbitrary frame set. Shared with the recording
    /// path so a session and the live panel cannot disagree.</summary>
    public static LiveFrameStats Compute(string application, int processId, IReadOnlyList<double> frames, bool independentFlip = false)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0) throw new ArgumentException("Sem quadros.", nameof(frames));

        double total = 0;
        foreach (var value in frames) total += value;
        var slowest = frames.OrderByDescending(v => v).ToArray();
        double WorstFraction(double fraction)
        {
            int count = Math.Max(1, (int)Math.Floor(frames.Count * fraction));
            double sum = 0;
            for (int i = 0; i < count; i++) sum += slowest[i];
            return sum / count;
        }
        return new(
            application,
            processId,
            Math.Round(frames.Count * 1000.0 / total * 10) / 10,
            Math.Round(1000.0 / WorstFraction(0.01) * 10) / 10,
            Math.Round(1000.0 / WorstFraction(0.001) * 10) / 10,
            Math.Round(total / frames.Count * 100) / 100,
            frames.Count,
            independentFlip);
    }

    public void Clear()
    {
        lock (gate) { processes.Clear(); pinned = null; knownGameProcessIds.Clear(); }
    }

    /// <summary>A marked baseline the user can compare against, matching the reference tool: the
    /// average and 1% low over the last 5 seconds of the target, plus when it was taken.</summary>
    public sealed record Baseline(string Application, int ProcessId, double Fps, double Low1, long TakenAtMs);

    private Baseline? baseline;

    /// <summary>Snapshot of the last 5 seconds as a baseline. Returns null (with a reason) when the
    /// target has too few frames, so the user is told to play a bit longer instead of getting a
    /// number computed from two frames.</summary>
    public (Baseline? Baseline, string? Error) MarkBaseline()
    {
        int? target;
        double[] recent;
        string name;
        lock (gate)
        {
            target = SelectTarget();
            if (target is null) return (null, "Nenhum jogo com quadros para medir.");
            if (!processes.TryGetValue(target.Value, out var tracked)) return (null, "Nenhum jogo com quadros para medir.");
            long cutoff = Environment.TickCount64 - 5000;
            recent = tracked.Frames.Where(f => f.At >= cutoff).Select(f => f.Milliseconds).ToArray();
            name = tracked.Name;
            if (recent.Length < 30) return (null, "Poucos quadros ainda. Jogue alguns segundos e marque de novo.");
            var stats = Compute(name, target.Value, recent);
            baseline = new Baseline(name, target.Value, Math.Round(stats.Fps * 10) / 10, Math.Round(stats.Low1), Environment.TickCount64);
            return (baseline, null);
        }
    }

    public Baseline? CurrentBaseline { get { lock (gate) return baseline; } }

    public void ClearBaseline() { lock (gate) baseline = null; }
}
