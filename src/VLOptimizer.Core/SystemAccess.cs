using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace VLOptimizer.Core;

/// <summary>Small, injectable boundary around the explicitly supported Windows operations.</summary>
public interface IWindowsSystemAccess
{
    bool IsSupportedOS { get; }
    Task<SystemSnapshot> AnalyzeAsync(IProgress<string>? progress, CancellationToken token);
    StartupValueState? ReadUserStartup(string name);
    void DeleteUserStartup(string name);
    void WriteUserStartup(string name, StartupValueState value);
    IReadOnlyList<PowerPlan> ReadPowerPlans();
    Guid ReadActivePowerPlan();
    void SetActivePowerPlan(Guid id);
}

/// <summary>Registry type and lossless JSON representation of its original value.</summary>
public record StartupValueState(int Kind, string SerializedValue);

public sealed class WindowsSystemAccess : IWindowsSystemAccess
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public bool IsSupportedOS => OperatingSystem.IsWindows() && NativeVersion() is { Major: 10, Build: >= 19041 } &&
                                 RuntimeInformation.OSArchitecture == Architecture.X64 &&
                                 string.Equals(ReadString(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\ProductOptions", "ProductType"), "WinNT", StringComparison.OrdinalIgnoreCase);

    public async Task<SystemSnapshot> AnalyzeAsync(IProgress<string>? progress, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Este diagnóstico usa APIs do Windows.");
        return await Task.Run(async () =>
        {
            var notices = new List<string>();
            var version = NativeVersion();
            progress?.Report("Identificando Windows, processador e memória…");
            token.ThrowIfCancellationRequested();
            string windows = ReadString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName") ?? "Windows";
            if (version.Build >= 22000) windows = windows.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            var memoryAvailable = GlobalMemoryStatusEx(ref memory);
            if (!memoryAvailable) notices.Add("A API de memória física não respondeu; a memória disponível não foi determinada.");
            ulong installed = GetPhysicallyInstalledSystemMemory(out var installedKb) ? installedKb * 1024 : 0;
            var performance = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
            ulong? committed = GetPerformanceInfo(ref performance, performance.Size)
                ? performance.CommitTotal.ToUInt64() * performance.PageSize.ToUInt64() : null;
            var disks = new List<DiskInfo>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                        disks.Add(new DiskInfo(drive.Name, drive.TotalSize, drive.AvailableFreeSpace));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { notices.Add($"Não foi possível consultar o disco {drive.Name}: {ex.Message}"); }
            }
            progress?.Report("Lendo inicialização e planos de energia…");
            var startup = EnumerateStartup(notices);
            var plans = new List<PowerPlan>();
            try { plans.AddRange(ReadPowerPlans()); }
            catch (Exception ex) { notices.Add($"Planos de energia indisponíveis: {ex.Message}"); }
            bool? battery = GetSystemPowerStatus(out var power) && power.AcLineStatus != 255 ? power.AcLineStatus == 0 : null;
            bool reboot = IsRebootPending(notices);
            bool admin;
            using (var identity = WindowsIdentity.GetCurrent()) admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            if (!IsSupportedOS) notices.Add("Alterações requerem Windows 10/11 x64 cliente, build 19041 ou superior. Nesta instalação incompatível a leitura permanece disponível. Cada ajuste verifica sua versão mínima e as permissões.");
            notices.Add("A utilização de CPU foi observada por aproximadamente 5 segundos. Ela descreve esta sessão e não confirma gargalo nem ganho em jogos.");
            notices.Add("Este retrato inicial não mede temperatura nem atividade de disco. A página Medições oferece coleta de CPU/núcleos/GPU 3D quando o contador estiver disponível; valores ausentes não são estimados.");
            notices.Add("Inicialização: são consultados Registro e pastas de inicialização. Tarefas agendadas e todas as fontes de aplicativos empacotados ainda não são inventariadas.");
            notices.Add("Uma entrada Run encontrada não comprova que o aplicativo foi executado: o Windows, políticas ou o próprio programa podem impedir sua inicialização. Revise também Aplicativos de inicialização no Windows.");
            if (Environment.ProcessorCount > 64) notices.Add("Em máquinas com múltiplos grupos de processadores, a amostragem GetSystemTimes representa o grupo do processo; não interprete a leitura como cobertura de todos os grupos.");
            progress?.Report("Observando CPU e consumo dos processos por 5 segundos…");
            var processBefore = CaptureProcesses();
            var sampleWatch = Stopwatch.StartNew();
            var samples = new List<double>();
            var previous = ReadTimes();
            for (var index = 0; index < 5; index++)
            {
                await Task.Delay(1000, token).ConfigureAwait(false);
                var current = ReadTimes();
                if (previous is { } start && current is { } end && end.Total > start.Total && end.Idle >= start.Idle)
                {
                    var elapsed = end.Total - start.Total;
                    var idle = end.Idle - start.Idle;
                    samples.Add(Math.Clamp(100.0 * (1.0 - (double)idle / elapsed), 0, 100));
                }
                previous = current;
            }
            double observed = sampleWatch.Elapsed.TotalSeconds;
            var processAfter = CaptureProcesses();
            var processes = processAfter.Select(item =>
            {
                double? cpu = processBefore.TryGetValue(item.Key, out var old) && old.StartTicks == item.Value.StartTicks
                    ? Math.Clamp((item.Value.CpuTicks - old.CpuTicks) / (double)TimeSpan.TicksPerSecond / observed / Environment.ProcessorCount * 100, 0, 100)
                    : null;
                return new ProcessInfo(item.Key, item.Value.Name, item.Value.Memory, cpu);
            }).OrderByDescending(x => x.CpuPercent ?? 0).ThenByDescending(x => x.MemoryBytes).Take(12).ToList();
            // Physical memory is read again after observation, so its timestamp matches the result.
            GlobalMemoryStatusEx(ref memory);
            committed = GetPerformanceInfo(ref performance, performance.Size)
                ? performance.CommitTotal.ToUInt64() * performance.PageSize.ToUInt64() : null;
            progress?.Report("Consolidando o diagnóstico…");
            return new SystemSnapshot
            {
                CapturedAt = DateTimeOffset.Now, WindowsName = windows, WindowsBuild = version.Build,
                SupportedOS = IsSupportedOS, IsAdministrator = admin,
                CpuName = ReadString(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString")?.Trim() ?? "Não disponível",
                GpuName = ReadGpuName(), InstalledMemoryBytes = installed,
                TotalMemoryBytes = memory.TotalPhysical, AvailableMemoryBytes = memory.AvailablePhysical,
                CommittedMemoryBytes = committed, AverageCpuPercent = samples.Count > 0 ? samples.Average() : null,
                CpuSamples = samples, ObservationSeconds = observed, Disks = disks, StartupItems = startup,
                PowerPlans = plans, OnBattery = battery, RebootPending = reboot, TopProcesses = processes, Notices = notices
            };
        }, token).ConfigureAwait(false);
    }

    public StartupValueState? ReadUserStartup(string name)
    {
        ValidateName(name);
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        if (key is null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
        var kind = key.GetValueKind(name);
        var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null) throw new InvalidDataException("O valor de inicialização não pôde ser lido.");
        return new StartupValueState((int)kind, JsonSerializer.Serialize(value, value.GetType()));
    }

    public void DeleteUserStartup(string name)
    {
        ValidateName(name);
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? throw new IOException("A chave de inicialização não está mais disponível.");
        key.DeleteValue(name, false);
        key.Flush();
    }

    public void WriteUserStartup(string name, StartupValueState value)
    {
        ValidateName(name);
        var kind = (RegistryValueKind)value.Kind;
        object decoded = kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString => JsonSerializer.Deserialize<string>(value.SerializedValue)!,
            RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(value.SerializedValue)!,
            RegistryValueKind.DWord => JsonSerializer.Deserialize<int>(value.SerializedValue),
            RegistryValueKind.QWord => JsonSerializer.Deserialize<long>(value.SerializedValue),
            RegistryValueKind.Binary or RegistryValueKind.None => JsonSerializer.Deserialize<byte[]>(value.SerializedValue)!,
            _ => throw new InvalidDataException("Tipo de Registro não suportado para restauração.")
        };
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        key.SetValue(name, decoded, kind);
        key.Flush();
    }

    public IReadOnlyList<PowerPlan> ReadPowerPlans()
    {
        var active = ReadActivePowerPlan();
        var plans = new List<PowerPlan>();
        for (uint index = 0; index < 128; index++)
        {
            uint size = 16;
            var data = new byte[16];
            uint result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, index, data, ref size);
            if (result == 259) break;
            if (result != 0) throw new Win32Exception((int)result);
            var id = new Guid(data);
            plans.Add(new PowerPlan(id, ReadPowerName(id), id == active));
        }
        return plans;
    }

    public Guid ReadActivePowerPlan()
    {
        uint result = PowerGetActiveScheme(IntPtr.Zero, out var pointer);
        if (result != 0) throw new Win32Exception((int)result);
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    public void SetActivePowerPlan(Guid id)
    {
        uint result = PowerSetActiveScheme(IntPtr.Zero, ref id);
        if (result != 0) throw new Win32Exception((int)result, "O Windows recusou a alteração do plano. Permissões e políticas corporativas são respeitadas.");
    }

    private static string ReadPowerName(Guid id)
    {
        uint size = 0;
        uint result = PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if (result is not (0 or 234) || size == 0 || size > 65536) return id.ToString();
        var bytes = new byte[size];
        result = PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, bytes, ref size);
        return result == 0 ? Encoding.Unicode.GetString(bytes).TrimEnd('\0') : id.ToString();
    }

    internal static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 16383 || name.Contains('\0'))
            throw new ArgumentException("Nome de inicialização inválido.", nameof(name));
    }

    internal static bool IsProtectedStartup(string name, string command)
    {
        string combined = name + " " + command;
        string[] protectedTerms = ["defender", "securityhealth", "antivirus", "antimalware", "avast", "avgui", "kaspersky", "bitdefender", "eset", "mcafee", "norton", "sophos", "crowdstrike", "sentinel", "forticlient", "anyconnect", "globalprotect", "intune", "company portal", "narrator", "nvda", "jaws", "magnify", "screenreader", "accessibility", "credential", "vpn", "zscaler", "tanium", "carbonblack", "cylance", "trend micro", "webroot", "f-secure", "malwarebytes"];
        return protectedTerms.Any(term => combined.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static List<StartupItem> EnumerateStartup(List<string> notices)
    {
        var items = new List<StartupItem>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var suffix in new[] { "Run", "RunOnce" })
        {
            // HKCU\Software is shared by WOW64; avoid displaying it twice.
            if (hive == RegistryHive.CurrentUser && view == RegistryView.Registry32) continue;
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\" + suffix);
                if (key is null) continue;
                foreach (var name in key.GetValueNames())
                {
                    try
                    {
                        string command = Convert.ToString(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)) ?? "";
                        bool writable = hive == RegistryHive.CurrentUser && suffix == "Run" &&
                            key.GetValueKind(name) is RegistryValueKind.String or RegistryValueKind.ExpandString &&
                            !IsProtectedStartup(name, command) && !string.IsNullOrWhiteSpace(name);
                        string source = $"{(hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")} · {suffix}{(hive == RegistryHive.LocalMachine ? view == RegistryView.Registry64 ? " · 64 bits" : " · 32 bits" : "")}";
                        string id = hive == RegistryHive.CurrentUser && suffix == "Run" ? "hkcu-run:" + Uri.EscapeDataString(name) : $"readonly:{hive}:{view}:{suffix}:{Uri.EscapeDataString(name)}";
                        items.Add(new StartupItem(id, string.IsNullOrWhiteSpace(name) ? "(Padrão)" : name, command, source, writable,
                            writable ? "Revise o comando e a função. A seleção remove apenas a entrada de inicialização; não fecha nem desinstala o aplicativo. Não selecione software corporativo ou de acessibilidade." : "Somente leitura nesta versão. Entradas sensíveis, de máquina e de execução única não são alteradas."));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    { notices.Add($"Uma entrada de inicialização não pôde ser consultada: {ex.Message}"); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { notices.Add($"Parte da inicialização está protegida: {ex.Message}"); }
        }
        foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
        {
            string path = Environment.GetFolderPath(folder);
            try
            {
                if (Directory.Exists(path))
                    items.AddRange(Directory.EnumerateFiles(path).Where(x => !Path.GetFileName(x).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                        .Select(x => new StartupItem("readonly:folder:" + x, Path.GetFileNameWithoutExtension(x), x,
                            folder == Environment.SpecialFolder.Startup ? "Pasta do usuário" : "Pasta de todos os usuários", false,
                            "Somente leitura. Use Aplicativos de inicialização do Windows para revisar este item.")));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { notices.Add($"Pasta de inicialização indisponível: {ex.Message}"); }
        }
        return items.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private record ProcessReading(string Name, long Memory, long CpuTicks, long StartTicks);
    private static Dictionary<int, ProcessReading> CaptureProcesses()
    {
        var result = new Dictionary<int, ProcessReading>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { result[process.Id] = new(process.ProcessName, process.WorkingSet64, process.TotalProcessorTime.Ticks, process.StartTime.ToUniversalTime().Ticks); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        return result;
    }
    private static string? ReadString(RegistryKey root, string path, string name)
    {
        try { using var key = root.OpenSubKey(path); return Convert.ToString(key?.GetValue(name)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }
    private static bool IsRebootPending(List<string> notices)
    {
        try
        {
            using var servicing = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            using var updates = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            using var session = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            return servicing is not null || updates is not null || session?.GetValue("PendingFileRenameOperations") is string[] { Length: > 0 };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            notices.Add("Não foi possível consultar todos os sinais de reinicialização pendente. Confirme também no Windows Update. " + ex.Message);
            return false;
        }
    }
    private static string ReadGpuName()
    {
        var names = new List<string>();
        for (uint index = 0; index < 32; index++)
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>(), DeviceName = "", DeviceString = "", DeviceId = "", DeviceKey = "" };
            if (!EnumDisplayDevices(null, index, ref device, 0)) break;
            if ((device.StateFlags & 8) == 0 && !string.IsNullOrWhiteSpace(device.DeviceString)) names.Add(device.DeviceString);
        }
        return names.Count > 0 ? string.Join(" · ", names.Distinct()) : "Não disponível";
    }
    private static (ulong Idle, ulong Total)? ReadTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        return (idle, kernel + user);
    }
    private static (int Major, int Build) NativeVersion()
    {
        if (!OperatingSystem.IsWindows()) return (0, 0);
        var version = new OsVersion { Size = (uint)Marshal.SizeOf<OsVersion>(), ServicePack = "" };
        return RtlGetVersion(ref version) == 0 ? ((int)version.Major, (int)version.Build) : (0, 0);
    }

    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual; }
    [StructLayout(LayoutKind.Sequential)] private struct PerformanceInformation { public uint Size; public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize; public uint HandleCount, ProcessCount, ThreadCount; }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { public byte AcLineStatus, BatteryFlag, BatteryPercent, SystemStatusFlag; public uint BatteryLifeTime, BatteryFullLifeTime; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct OsVersion { public uint Size, Major, Minor, Build, Platform; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string ServicePack; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice { public int Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString; public uint StateFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey; }
    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)] private static extern int RtlGetVersion(ref OsVersion version);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalKb);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    [DllImport("psapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPerformanceInfo(ref PerformanceInformation information, uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice display, uint flags);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid guid);
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup, uint access, uint index, byte[] buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[]? buffer, ref uint size);
}
