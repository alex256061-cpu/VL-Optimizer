using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace VLOptimizer.App;

public partial class App : Application
{
    /// <summary>True when this process already runs elevated. The manifest asks for it; this covers the
    /// cases where the manifest is bypassed (a host that starts the exe without honouring it), so the
    /// app never reaches the FPS capture with a token that cannot open an ETW session.</summary>
    internal static bool IsElevated { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        IsElevated = CheckElevated();

        if (!IsElevated)
        {
            // Relaunch elevated once. If the user declines the UAC prompt we still start, so the
            // parts that do not need elevation keep working and the FPS page explains the limit.
            if (TryRelaunchElevated(e.Args))
            {
                Shutdown();
                return;
            }
        }

        var window = new MainWindow(e.Args);
        MainWindow = window;
        window.Show();
    }

    private static bool CheckElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException) { return false; }
    }

    /// <summary>Starts another copy of this executable with the runas verb. Returns true only when the
    /// new process actually launched, so a refused UAC prompt falls through to a normal start instead
    /// of leaving the user with nothing.</summary>
    private static bool TryRelaunchElevated(string[] arguments)
    {
        try
        {
            string executable = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? "";
            if (executable.Length == 0) return false;

            var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var elevated = Process.Start(start);
            return elevated is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or System.IO.FileNotFoundException)
        {
            // UAC declined (Win32Exception 1223) or the relaunch is impossible: keep running unelevated.
            return false;
        }
    }
}
