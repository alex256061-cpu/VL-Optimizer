using Microsoft.Win32;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VLOptimizer.Core;

internal static class ConfigurationAdditionalAccess
{
    private const string MemoryPath = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    private static readonly ConfigurationTarget pagingFiles = new(ConfigurationTargetKind.Registry, MemoryPath, "PagingFiles");
    internal static ConfigurationValue ReadPagefile(ConfigurationWindowsAccess registry)
    {
        bool automatic = WithComputer(computer =>
        {
            dynamic properties = computer.Properties_; dynamic property = properties.Item("AutomaticManagedPagefile", 0);
            try { return Convert.ToBoolean(property.Value, System.Globalization.CultureInfo.InvariantCulture); }
            finally { Release(property); Release(properties); }
        });
        return ConfigurationValue.State(new ConfigurationPagefileState(automatic, automatic ? null : registry.Read(pagingFiles)));
    }
    internal static void WritePagefile(ConfigurationWindowsAccess registry, ConfigurationValue value)
    {
        var desired = JsonSerializer.Deserialize<ConfigurationPagefileState>(value.Json) ?? throw new InvalidDataException("Estado de pagefile ausente.");
        if (!desired.AutomaticManaged && desired.PagingFiles is null) throw new InvalidDataException("Configuração manual anterior ausente.");
        WithComputer(computer =>
        {
            dynamic properties = computer.Properties_; dynamic property = properties.Item("AutomaticManagedPagefile", 0);
            try { property.Value = desired.AutomaticManaged; dynamic saved = computer.Put_(0, null); Release(saved); }
            finally { Release(property); Release(properties); }
            return true;
        });
        if (!desired.AutomaticManaged) registry.Write(pagingFiles, desired.PagingFiles!);
    }
    private static T WithComputer<T>(Func<dynamic, T> action)
    {
        object? locator = null, services = null, collection = null, computer = null;
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator") ?? throw new PlatformNotSupportedException("WMI scripting indisponível.");
            locator = Activator.CreateInstance(type) ?? throw new IOException("WMI não iniciou.");
            services = ((dynamic)locator).ConnectServer(".", @"root\cimv2");
            collection = ((dynamic)services).ExecQuery("SELECT * FROM Win32_ComputerSystem", "WQL", 0);
            foreach (object item in (IEnumerable)collection) { computer = item; break; }
            if (computer is null) throw new IOException("Win32_ComputerSystem não respondeu.");
            return action(computer);
        }
        finally { Release(computer); Release(collection); Release(services); Release(locator); }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    internal static IReadOnlyList<ConfigurationChange> ResolveObs(ConfigurationWindowsAccess access)
    {
        var candidates = new List<string>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var app = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\obs64.exe");
            if (app?.GetValue("") is string path) candidates.Add(path);
        }
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            candidates.Add(Path.Combine(root, @"obs-studio\bin\64bit\obs64.exe"));
        string? obs = candidates.Select(x => x.Trim('"')).FirstOrDefault(File.Exists);
        if (obs is null) return [];
        obs = Path.GetFullPath(obs);
        var layers = new ConfigurationTarget(ConfigurationTargetKind.Registry, @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", obs);
        var original = access.Read(layers);
        if (original.Exists && original.Kind != (int)RegistryValueKind.String) throw new InvalidDataException("O valor Layers do OBS não é string; configuração preservada.");
        var tokens = (original.Exists ? JsonSerializer.Deserialize<string>(original.Json) ?? "" : "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => !x.Equals("RUNASADMIN", StringComparison.OrdinalIgnoreCase)).ToArray();
        string off = string.Join(' ', tokens); string on = (off.Length == 0 ? "~ " : off + " ") + "RUNASADMIN";
        return [new(layers, ConfigurationValue.String(on), off.Length == 0 ? ConfigurationValue.Missing(original.KeyExists) : ConfigurationValue.String(off)),
            new(new(ConfigurationTargetKind.AppPowerThrottling, obs), ConfigurationValue.State(true), ConfigurationValue.State(false))];
    }
    internal static ConfigurationValue ReadPowerThrottling(string path)
    {
        string output = RunPowerCfg("/powerthrottling", "list");
        bool disabled = Regex.IsMatch(output, "(?<![A-Za-z0-9_])" + Regex.Escape(path) + "(?=[\"'\\s]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return ConfigurationValue.State(disabled);
    }
    internal static void WritePowerThrottling(string path, ConfigurationValue value)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.GetFileName(path).Equals("obs64.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Executável fora do alvo OBS.");
        RunPowerCfg("/powerthrottling", JsonSerializer.Deserialize<bool>(value.Json) ? "disable" : "reset", "/path", path);
    }
    private static string RunPowerCfg(params string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("powercfg não iniciou.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000)) { process.Kill(true); throw new TimeoutException("powercfg não respondeu em 15 segundos."); }
        Task.WaitAll(output, error);
        if (process.ExitCode != 0) throw new IOException($"powercfg recusou a solicitação ({process.ExitCode}): {error.Result.Trim()} {output.Result.Trim()}");
        return output.Result;
    }
}
