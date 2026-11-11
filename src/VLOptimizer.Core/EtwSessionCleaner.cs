using System.ComponentModel;
using System.Diagnostics;

namespace VLOptimizer.Core;

public sealed record EtwCleanupResult(int Removed, string Message);

/// <summary>Removes stale Event Tracing sessions left behind by this tool.
///
/// <para>When PresentMon is killed before it can close its session, the session survives the process
/// and a few of them block every later capture. The reference tool has the same defect, and its FPS
/// meter then silently stops working until the machine reboots.</para>
///
/// <para><b>Safety rules:</b> only names starting with this tool's own prefixes are candidates — Steam,
/// Discord, NVIDIA and Windows sessions are never listed, let alone stopped; the caller's live session
/// is never touched; every external call has a timeout; and no failure escapes as an exception,
/// because a stuck trace session must never take the app down.</para></summary>
public sealed class EtwSessionCleaner
{
    /// <summary>Prefixes owned by this tool. A stray <c>PresentMon</c> session is also ours, since it
    /// can only exist because our own capture launched the process.</summary>
    private static readonly string[] OwnedPrefixes = ["VL_", "VLR", "VLS", "VLC", "VLX", "VLd", "VLe"];

    private const string PresentMonSession = "PresentMon";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    /// <summary>Stops sessions this tool left behind. <paramref name="keepSession"/> survives, so this
    /// may be called while the caller's own meter is running.</summary>
    public EtwCleanupResult CleanStaleSessions(string? keepSession = null)
    {
        List<string> running;
        try { running = ListSessions(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(0, "Não foi possível listar as sessões de rastreamento; nada foi alterado.");
        }

        var candidates = new List<string>();
        foreach (var name in running)
        {
            if (!IsOwned(name)) continue;
            if (!string.IsNullOrEmpty(keepSession) && name.Equals(keepSession, StringComparison.OrdinalIgnoreCase)) continue;
            candidates.Add(name);
        }
        if (candidates.Count == 0) return new(0, "Nenhuma sessão antiga do VL para limpar.");

        int removed = 0;
        foreach (var session in candidates)
        {
            if (StopSession(session)) removed++;
        }
        return removed == 0
            ? new(0, "Havia " + candidates.Count + " sessão(ões) antiga(s) que não puderam ser encerradas; o medidor segue mesmo assim.")
            : new(removed, "Encerradas " + removed + " sessão(ões) de rastreamento antigas do VL.");
    }

    private static bool IsOwned(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Equals(PresentMonSession, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var prefix in OwnedPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Reads the session list from <c>logman query -ets</c>. Output is parsed, not trusted:
    /// only the first column of data rows is used.</summary>
    private static List<string> ListSessions()
    {
        var sessions = new List<string>();
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "logman.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("query");
        start.ArgumentList.Add("-ets");

        using var process = Process.Start(start) ?? throw new InvalidOperationException("logman não iniciou.");
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            try { process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            return sessions;
        }
        var text = output.GetAwaiter().GetResult();
        bool inTable = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) { if (inTable) break; continue; }
            // The table starts under the header row that names the first column.
            if (!inTable)
            {
                if (line.Contains("Coletores", StringComparison.OrdinalIgnoreCase) || line.Contains("Data Collector", StringComparison.OrdinalIgnoreCase)) inTable = true;
                continue;
            }
            var name = line.Split(' ', '\t')[0].Trim();
            if (name.Length == 0 || name.StartsWith('-')) continue;
            if (name.Equals("O", StringComparison.OrdinalIgnoreCase)) continue;
            sessions.Add(name);
        }
        return sessions;
    }

    private static bool StopSession(string name)
    {
        if (!IsOwned(name)) return false;
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "logman.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("stop");
            start.ArgumentList.Add(name);
            start.ArgumentList.Add("-ets");

            using var process = Process.Start(start);
            if (process is null) return false;
            // Drain both pipes so a chatty child cannot deadlock on a full buffer.
            var drained = Task.WhenAll(process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
            if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                try { process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
                return false;
            }
            try { drained.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
