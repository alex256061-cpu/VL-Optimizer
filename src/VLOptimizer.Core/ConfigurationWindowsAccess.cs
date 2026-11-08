using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace VLOptimizer.Core;

/// <summary>Native registry/service/power adapter. No GDZ executable, library or script is used.</summary>
public sealed class ConfigurationWindowsAccess : IConfigurationSystemAccess
{
    private readonly WindowsSystemAccess power = new();
    private const string NicClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Power";
    public ConfigurationEnvironment InspectEnvironment()
    {
        if (!OperatingSystem.IsWindows()) return new(false, false, 0);
        var version = new OsVersion { Size = (uint)Marshal.SizeOf<OsVersion>() };
        if (RtlGetVersion(ref version) != 0) throw new Win32Exception("Não foi possível identificar a versão real do Windows.");
        using var identity = WindowsIdentity.GetCurrent();
        bool admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        string cpuName = cpu?.GetValue("ProcessorNameString") as string ?? "";
        bool dual = new[] { "7900X3D", "7950X3D", "9900X3D", "9950X3D", "7945HX3D" }.Any(x => cpuName.Contains(x, StringComparison.OrdinalIgnoreCase));
        string gpuName = string.Join("; ", EnumerateDisplayDevices().Select(x => x.Name));
        // System power status reports whether a battery exists without relying on model-name guesses.
        bool laptop = GetSystemPowerStatus(out var status) && status.BatteryFlag != 128 && status.BatteryFlag != 255;
        return new(power.IsSupportedOS, admin, (int)version.Build, laptop, dual, gpuName);
    }

    public IReadOnlyList<ConfigurationChange> ResolveDynamicChanges(string capability)
    {
        RequireWindows();
        var rows = new List<ConfigurationChange>();
        switch (capability)
        {
            case "steam-overlay": rows.AddRange(ConfigurationSteamAccess.Resolve()); break;
            case "dlss-rec": rows.AddRange(ConfigurationNvidiaAccess.Resolve()); break;
            case "obs-priority": rows.AddRange(ConfigurationAdditionalAccess.ResolveObs(this)); break;
            case "pagefile-auto":
                var automaticPagefile = ConfigurationValue.State(new ConfigurationPagefileState(true, null));
                rows.Add(new(new(ConfigurationTargetKind.Pagefile, "system"), automaticPagefile, automaticPagefile)); break;
            case "resolution-720p": rows.AddRange(ConfigurationDisplayAccess.Resolve()); break;
            case "dns-cloudflare": case "dns-google": case "dns-quad9": case "dns-adguard": case "dns-auto": rows.AddRange(ConfigurationDnsAccess.Resolve(capability[4..])); break;
            case "gpu-msi":
                foreach (var gpu in EnumerateDisplayDevices().Where(x => x.DedicatedVendor))
                {
                    using var interrupts = Registry.LocalMachine.OpenSubKey(gpu.Path + @"\Device Parameters\Interrupt Management");
                    if (interrupts is null) continue;
                    rows.Add(Reg(@"HKLM\" + gpu.Path + @"\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties", "MSISupported", 1, null));
                    rows.Add(Reg(@"HKLM\" + gpu.Path + @"\Device Parameters\Interrupt Management\Affinity Policy", "DevicePriority", 3, null));
                }
                break;
            case "lso": case "eee": case "nic-intmod":
                var interfaces = NetworkInterface.GetAllNetworkInterfaces().ToDictionary(x => x.Id.Trim('{', '}'), StringComparer.OrdinalIgnoreCase);
                using (var nicClass = Registry.LocalMachine.OpenSubKey(NicClass))
                {
                    if (nicClass is null) break;
                    foreach (var name in nicClass.GetSubKeyNames().Where(x => x.Length == 4 && x.All(char.IsDigit)))
                    {
                        using var nic = nicClass.OpenSubKey(name);
                        if (nic is null || nic.GetValue("NetCfgInstanceId") is not string instance || !interfaces.TryGetValue(instance.Trim('{', '}'), out var net)) continue;
                        if (net.NetworkInterfaceType == NetworkInterfaceType.Loopback || net.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;
                        if (capability == "eee" && net.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT)) continue;
                        string[] properties = capability == "lso" ? ["*LsoV2IPv4", "*LsoV2IPv6"] : capability == "eee" ? ["*EEE", "EEE", "AdvancedEEE", "EEELinkAdvertisement", "EnableGreenEthernet"] : ["*InterruptModeration"];
                        foreach (var property in properties)
                        {
                            using var param = nic.OpenSubKey(@"Ndi\Params\" + property);
                            if (param is null && !nic.GetValueNames().Contains(property, StringComparer.OrdinalIgnoreCase)) continue;
                            string defaultValue = param?.GetValue("default") as string ?? "1";
                            rows.Add(Reg(@"HKLM\" + NicClass + "\\" + name, property, "0", defaultValue));
                        }
                    }
                }
                break;
            case "xbox-services":
                foreach (var name in new[] { "XblAuthManager", "XblGameSave", "XboxNetApiSvc" })
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
                    if (key is not null) rows.Add(new(new(ConfigurationTargetKind.Service, name), ConfigurationValue.State(new ConfigurationServiceState(4, false)), ConfigurationValue.State(new ConfigurationServiceState(3, false))));
                }
                break;
            case "hags":
                if (EnumerateDisplayDevices().Count > 0) rows.Add(Reg(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, 1));
                break;
            case "unused-services":
                foreach (var name in new[] { "Spooler", "Fax", "RemoteRegistry", "MapsBroker", "WbioSrvc" })
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
                    if (key is not null) rows.Add(new(new(ConfigurationTargetKind.Service, name), ConfigurationValue.State(new ConfigurationServiceState(4, false)), ConfigurationValue.State(new ConfigurationServiceState(3, false))));
                }
                break;
            case "windowed-games":
                var target = new ConfigurationTarget(ConfigurationTargetKind.Registry, @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings");
                var current = Read(target);
                if (current.Exists && current.Kind != (int)RegistryValueKind.String) throw new InvalidDataException("DirectXUserGlobalSettings não é uma string; o tipo existente foi preservado.");
                var options = current.Exists ? JsonSerializer.Deserialize<string>(current.Json) ?? "" : "";
                string prefix = string.Join(';', options.Split(';', StringSplitOptions.RemoveEmptyEntries).Where(x => !x.TrimStart().StartsWith("SwapEffectUpgradeEnable=", StringComparison.OrdinalIgnoreCase)));
                if (prefix.Length > 0) prefix += ";";
                rows.Add(new(target, ConfigurationValue.String(prefix + "SwapEffectUpgradeEnable=1;"), ConfigurationValue.String(prefix + "SwapEffectUpgradeEnable=0;")));
                break;
            case "hibernate":
                var old = ReadHibernation();
                rows.Add(new(new(ConfigurationTargetKind.Hibernation, "system"), ConfigurationValue.State(new ConfigurationHibernationState(false, null, null)),
                    ConfigurationValue.State(new ConfigurationHibernationState(true, old.FileType ?? 2, old.SizePercent ?? 0))));
                break;
            case "power-balanced": case "power-high": case "power-ultimate":
                Guid id = Guid.Parse(capability == "power-balanced" ? "381b4222-f694-41f0-9685-ff5bb260df2e" : capability == "power-high" ? "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" : "e9a42b02-d5df-448d-aa00-03f14749eb61");
                var plans = power.ReadPowerPlans();
                if (plans.Any(x => x.Id == id)) rows.Add(new(new(ConfigurationTargetKind.PowerPlan, "active"), ConfigurationValue.State(id), ConfigurationValue.State(Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"))));
                break;
            default: throw new InvalidOperationException("Capacidade fora do catálogo fechado.");
        }
        return rows;
    }
    public IReadOnlyList<ConfigurationChange> ResolveDynamicChangesForRestore(string capability) => capability.StartsWith("dns-", StringComparison.Ordinal) ? ConfigurationDnsAccess.Resolve(capability[4..], true) : ResolveDynamicChanges(capability);

    public ConfigurationValue Read(ConfigurationTarget target)
    {
        RequireWindows();
        return target.Kind switch
        {
            ConfigurationTargetKind.Registry => ReadRegistry(target),
            ConfigurationTargetKind.Service => ConfigurationValue.State(ReadService(target.Path)),
            ConfigurationTargetKind.PowerPlan => ConfigurationValue.State(power.ReadActivePowerPlan()),
            ConfigurationTargetKind.Hibernation => ConfigurationValue.State(ReadHibernation()),
            ConfigurationTargetKind.SteamOverlay => ConfigurationSteamAccess.Read(target.Path),
            ConfigurationTargetKind.NvidiaDrs => ConfigurationNvidiaAccess.Read(target),
            ConfigurationTargetKind.Pagefile => ConfigurationAdditionalAccess.ReadPagefile(this),
            ConfigurationTargetKind.AppPowerThrottling => ConfigurationAdditionalAccess.ReadPowerThrottling(target.Path),
            ConfigurationTargetKind.DnsIpv4 => ConfigurationDnsAccess.Read(target.Path),
            ConfigurationTargetKind.DisplayMode => ConfigurationDisplayAccess.Read(target.Path),
            _ => throw new InvalidOperationException("Tipo de alvo desconhecido.")
        };
    }
    public void Write(ConfigurationTarget target, ConfigurationValue value)
    {
        RequireWindows();
        switch (target.Kind)
        {
            case ConfigurationTargetKind.Registry: WriteRegistry(target, value); break;
            case ConfigurationTargetKind.Service: WriteService(target.Path, JsonSerializer.Deserialize<ConfigurationServiceState>(value.Json) ?? throw new InvalidDataException("Estado de serviço ausente.")); break;
            case ConfigurationTargetKind.PowerPlan:
                var id = JsonSerializer.Deserialize<Guid>(value.Json);
                if (!power.ReadPowerPlans().Any(x => x.Id == id)) throw new InvalidOperationException("O plano de energia não está mais instalado.");
                power.SetActivePowerPlan(id); break;
            case ConfigurationTargetKind.Hibernation: WriteHibernation(JsonSerializer.Deserialize<ConfigurationHibernationState>(value.Json) ?? throw new InvalidDataException("Estado de hibernação ausente.")); break;
            case ConfigurationTargetKind.SteamOverlay: ConfigurationSteamAccess.Write(target.Path, value); break;
            case ConfigurationTargetKind.NvidiaDrs: ConfigurationNvidiaAccess.Write(target, value); break;
            case ConfigurationTargetKind.Pagefile: ConfigurationAdditionalAccess.WritePagefile(this, value); break;
            case ConfigurationTargetKind.AppPowerThrottling: ConfigurationAdditionalAccess.WritePowerThrottling(target.Path, value); break;
            case ConfigurationTargetKind.DnsIpv4: ConfigurationDnsAccess.Write(target.Path, value); break;
            case ConfigurationTargetKind.DisplayMode: ConfigurationDisplayAccess.Write(target.Path, value); break;
            default: throw new InvalidOperationException("Tipo de alvo desconhecido.");
        }
    }

    private static ConfigurationValue ReadRegistry(ConfigurationTarget target)
    {
        var (hive, path) = ParsePath(target.Path);
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(path, false); // Access-denied exceptions propagate; denied is never interpreted as missing.
        if (key is null) return ConfigurationValue.Missing();
        if (!key.GetValueNames().Contains(target.Name, StringComparer.OrdinalIgnoreCase)) return ConfigurationValue.Missing(true);
        var kind = key.GetValueKind(target.Name);
        var value = key.GetValue(target.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) ?? throw new IOException("O valor desapareceu durante a consulta.");
        return new(true, (int)kind, JsonSerializer.Serialize(value, value.GetType()));
    }
    private static void WriteRegistry(ConfigurationTarget target, ConfigurationValue value)
    {
        var (hive, path) = ParsePath(target.Path);
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        if (!value.Exists)
        {
            using (var key = root.OpenSubKey(path, true)) key?.DeleteValue(target.Name, false);
            if (!value.KeyExists)
            {
                bool empty;
                using (var remaining = root.OpenSubKey(path, false)) empty = remaining is not null && remaining.ValueCount == 0 && remaining.SubKeyCount == 0;
                if (empty) root.DeleteSubKey(path, false); // Never recursively remove unrelated values or subkeys.
            }
            return;
        }
        var kind = (RegistryValueKind)value.Kind;
        object data = kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString => JsonSerializer.Deserialize<string>(value.Json)!,
            RegistryValueKind.DWord => JsonSerializer.Deserialize<int>(value.Json),
            RegistryValueKind.QWord => JsonSerializer.Deserialize<long>(value.Json),
            RegistryValueKind.Binary or RegistryValueKind.None => JsonSerializer.Deserialize<byte[]>(value.Json)!,
            RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(value.Json)!,
            _ => throw new InvalidDataException("Tipo do registro não suportado.")
        };
        using var writable = root.CreateSubKey(path, true) ?? throw new IOException("O Windows não abriu a chave para escrita.");
        writable.SetValue(target.Name, data, kind);
        writable.Flush();
    }
    private static (RegistryHive Hive, string Path) ParsePath(string value)
    {
        var parts = value.Split('\\', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]) || parts[1].Split('\\').Any(x => x is "." or ".." || x.Length == 0)) throw new InvalidDataException("Caminho de registro inválido.");
        return (parts[0] switch { "HKCU" => RegistryHive.CurrentUser, "HKLM" => RegistryHive.LocalMachine, "HKU" => RegistryHive.Users, _ => throw new InvalidDataException("Raiz de registro fora do catálogo.") }, parts[1]);
    }
    private static ConfigurationChange Reg(string path, string name, object? on, object? off)
    {
        static ConfigurationValue V(object? x) => x is null ? ConfigurationValue.Missing() : x is int number ? ConfigurationValue.DWord(number) : ConfigurationValue.String((string)x);
        return new(new(ConfigurationTargetKind.Registry, path, name), V(on), V(off));
    }
    private static IReadOnlyList<(string Path, string Name, bool DedicatedVendor)> EnumerateDisplayDevices()
    {
        var devices = new List<(string, string, bool)>();
        using var pci = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI");
        if (pci is null) return devices;
        foreach (var model in pci.GetSubKeyNames())
        {
            using var modelKey = pci.OpenSubKey(model);
            if (modelKey is null) continue;
            foreach (var instance in modelKey.GetSubKeyNames())
            {
                using var device = modelKey.OpenSubKey(instance);
                if (!string.Equals(device?.GetValue("ClassGUID") as string, "{4d36e968-e325-11ce-bfc1-08002be10318}", StringComparison.OrdinalIgnoreCase)) continue;
                string path = @"SYSTEM\CurrentControlSet\Enum\PCI\" + model + "\\" + instance;
                string name = device?.GetValue("FriendlyName") as string ?? device?.GetValue("DeviceDesc") as string ?? model;
                devices.Add((path, name, model.StartsWith("VEN_10DE", StringComparison.OrdinalIgnoreCase) || model.StartsWith("VEN_1002", StringComparison.OrdinalIgnoreCase)));
            }
        }
        return devices;
    }

    private static ConfigurationHibernationState ReadHibernation()
    {
        using var key = Registry.LocalMachine.OpenSubKey(PowerKey);
        if (key?.GetValue("HibernateEnabled") is not int enabled) throw new IOException("O estado de hibernação não pôde ser determinado.");
        int type = key.GetValue("HiberFileType") as int? ?? 2;
        return enabled == 0 ? new(false, null, null) : new(true, type, type == 1 ? 0 : key.GetValue("HiberFileSizePercent") as int? ?? 0);
    }
    private static void WriteHibernation(ConfigurationHibernationState value)
    {
        if (value.Enabled && (value.FileType is not (1 or 2) || value.SizePercent is null or < 0 or > 100)) throw new InvalidDataException("Tipo/tamanho de hibernação inválido.");
        RunPowerCfg("/hibernate", value.Enabled ? "on" : "off");
        if (value.Enabled)
        {
            if (value.FileType == 1)
            {
                RunPowerCfg("/hibernate", "/size", "0");
                RunPowerCfg("/hibernate", "/type", "reduced");
            }
            else
            {
                RunPowerCfg("/hibernate", "/type", "full");
                RunPowerCfg("/hibernate", "/size", value.SizePercent!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
    private static void RunPowerCfg(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var item in arguments) start.ArgumentList.Add(item);
        using var process = Process.Start(start) ?? throw new IOException("powercfg não iniciou.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000)) { process.Kill(true); throw new TimeoutException("powercfg não respondeu em 15 segundos."); }
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0) throw new IOException($"powercfg recusou a operação ({process.ExitCode}): {stderr.Result.Trim()} {stdout.Result.Trim()}");
    }

    public void Refresh(ConfigurationRefresh refresh)
    {
        RequireWindows();
        switch (refresh)
        {
            case ConfigurationRefresh.None: break;
            case ConfigurationRefresh.Mouse:
                using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse"))
                {
                    int ReadInt(string name) => int.TryParse(key?.GetValue(name)?.ToString(), out var value) ? value : throw new IOException($"Parâmetro do mouse {name} não pôde ser relido.");
                    int[] parameters = [ReadInt("MouseThreshold1"), ReadInt("MouseThreshold2"), ReadInt("MouseSpeed")];
                    if (!SystemParametersInfoArray(0x0004, 0, parameters, 2)) throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                break;
            case ConfigurationRefresh.Keyboard:
                using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard"))
                {
                    if (!uint.TryParse(key?.GetValue("KeyboardDelay")?.ToString(), out var delay) || !uint.TryParse(key.GetValue("KeyboardSpeed")?.ToString(), out var speed)) throw new IOException("Parâmetros do teclado não puderam ser relidos.");
                    if (!SystemParametersInfoScalar(0x0017, delay, IntPtr.Zero, 2) || !SystemParametersInfoScalar(0x000b, speed, IntPtr.Zero, 2)) throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                break;
            case ConfigurationRefresh.Theme:
                if (SendMessageTimeout(new IntPtr(0xffff), 0x001a, IntPtr.Zero, "ImmersiveColorSet", 2, 1000, out _) == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "A notificação de tema não foi confirmada; renove a sessão.");
                break;
        }
    }

    private static ConfigurationServiceState ReadService(string name)
    {
        using var handles = OpenServiceHandles(name, 0x0001 | 0x0004);
        QueryServiceConfig(handles.Service, IntPtr.Zero, 0, out uint bytes);
        int error = Marshal.GetLastWin32Error();
        if (bytes == 0 || error != 122) throw new Win32Exception(error, $"Não foi possível consultar {name}.");
        IntPtr memory = Marshal.AllocHGlobal(checked((int)bytes));
        try
        {
            if (!QueryServiceConfig(handles.Service, memory, bytes, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var config = Marshal.PtrToStructure<ServiceConfig>(memory);
            var status = ReadServiceStatus(handles.Service);
            if (status.State is not (1 or 4)) throw new IOException($"O serviço {name} está em transição ou pausado. Aguarde e consulte novamente.");
            bool delayed = false;
            if (!QueryServiceConfig2(handles.Service, 3, out delayed, 4, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new(config.StartType, status.State == 4, delayed);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private static void WriteService(string name, ConfigurationServiceState value)
    {
        if (value.StartType is < 2 or > 4) throw new InvalidDataException("Modo do serviço não é suportado para esta ação.");
        using var handles = OpenServiceHandles(name, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
        // A service may legitimately have been disabled while still running. Temporarily allow
        // demand-start in that rare restoration case, then put back Disabled after it starts.
        uint initialStart = value.Running && value.StartType == 4 ? 3U : value.StartType;
        if (!ChangeServiceConfig(handles.Service, uint.MaxValue, initialStart, uint.MaxValue, null, null, IntPtr.Zero, null, null, null, null)) throw new Win32Exception(Marshal.GetLastWin32Error());
        bool delayed = value.DelayedAutoStart;
        if (!ChangeServiceConfig2(handles.Service, 3, ref delayed)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var current = ReadServiceStatus(handles.Service);
        if (value.Running && current.State != 4)
        {
            if (!StartService(handles.Service, 0, IntPtr.Zero) && Marshal.GetLastWin32Error() != 1056) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        if (!value.Running && current.State != 1)
        {
            if (!ControlService(handles.Service, 1, out _) && Marshal.GetLastWin32Error() != 1062) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        var clock = Stopwatch.StartNew(); uint expected = value.Running ? 4U : 1U;
        while (ReadServiceStatus(handles.Service).State != expected)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException($"O serviço {name} não confirmou o estado final em 10 segundos.");
            Thread.Sleep(100);
        }
        if (initialStart != value.StartType && !ChangeServiceConfig(handles.Service, uint.MaxValue, value.StartType, uint.MaxValue, null, null, IntPtr.Zero, null, null, null, null)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private static ServiceStatus ReadServiceStatus(IntPtr service)
    {
        if (!QueryServiceStatusEx(service, 0, out ServiceStatus status, (uint)Marshal.SizeOf<ServiceStatus>(), out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return status;
    }
    private static ServiceHandles OpenServiceHandles(string name, uint access)
    {
        if (name.Length is 0 or > 256 || name.Any(c => !char.IsLetterOrDigit(c) && c != '_')) throw new InvalidDataException("Nome de serviço inválido.");
        var manager = OpenSCManager(null, null, 1);
        if (manager == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        var service = OpenService(manager, name, access);
        if (service == IntPtr.Zero) { int error = Marshal.GetLastWin32Error(); CloseServiceHandle(manager); throw new Win32Exception(error, $"Não foi possível abrir o serviço {name}."); }
        return new(manager, service);
    }
    private sealed class ServiceHandles(IntPtr manager, IntPtr service) : IDisposable
    {
        public IntPtr Service { get; } = service;
        public void Dispose() { CloseServiceHandle(Service); CloseServiceHandle(manager); }
    }
    private static void RequireWindows() { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Recurso disponível no Windows."); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct OsVersion { public uint Size, Major, Minor, Build, Platform; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? ServicePack; }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public uint BatteryLifeTime, BatteryFullLifeTime; }
    [StructLayout(LayoutKind.Sequential)] private struct ServiceConfig { public uint Type, StartType, ErrorControl; public IntPtr BinaryPath, LoadOrderGroup; public uint TagId; public IntPtr Dependencies, StartName, DisplayName; }
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus { public uint Type, State, ControlsAccepted, Win32ExitCode, SpecificExitCode, CheckPoint, WaitHint, ProcessId, Flags; }
    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)] private static extern int RtlGetVersion(ref OsVersion version);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SystemParametersInfoArray(uint action, uint param, [In] int[] data, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SystemParametersInfoScalar(uint action, uint param, IntPtr data, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wparam, string lparam, uint flags, uint timeout, out IntPtr result);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(IntPtr handle);
    [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceConfig(IntPtr service, IntPtr config, uint bytes, out uint needed);
    [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfig2W", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceConfig2(IntPtr service, uint level, [MarshalAs(UnmanagedType.Bool)] out bool delayed, uint bytes, out uint needed);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceStatusEx(IntPtr service, uint level, out ServiceStatus status, uint bytes, out uint needed);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ChangeServiceConfig(IntPtr service, uint type, uint startType, uint errorControl, string? binary, string? group, IntPtr tag, string? dependencies, string? user, string? password, string? displayName);
    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ChangeServiceConfig2(IntPtr service, uint level, [MarshalAs(UnmanagedType.Bool)] ref bool delayed);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool StartService(IntPtr service, uint argc, IntPtr argv);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ControlService(IntPtr service, uint control, out ServiceStatus status);
}
