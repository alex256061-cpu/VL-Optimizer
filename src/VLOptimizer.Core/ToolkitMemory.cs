using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace VLOptimizer.Core;

public sealed class ToolkitMemoryService
{
    private readonly SemaphoreSlim _purge = new(1, 1);
    public Task<ToolkitMemorySnapshot> ReadAsync(CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var performance = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        bool performanceAvailable = GetPerformanceInfo(ref performance, performance.Size);
        ulong? standby = null; string? notice = null;
        try
        {
            using var counters = new ToolkitPerformanceCounters();
            var handles = new[] { "Core", "Normal Priority", "Reserve" }.Select(name => counters.Add($@"\Memory\Standby Cache {name} Bytes")).ToArray();
            counters.Collect(); var values = handles.Select(counters.ReadLarge).ToArray();
            if (values.All(v => v is >= 0)) standby = (ulong)values.Sum(v => v!.Value);
            else notice = "Contadores standby indisponíveis; nenhum valor de cache foi estimado.";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { notice = "Standby não disponível: " + ex.Message; }
        return new ToolkitMemorySnapshot(DateTimeOffset.UtcNow, memory.TotalPhysical, memory.AvailablePhysical,
            performanceAvailable ? performance.CommitTotal.ToUInt64() * performance.PageSize.ToUInt64() : null,
            performanceAvailable ? performance.CommitLimit.ToUInt64() * performance.PageSize.ToUInt64() : null,
            standby, memory.TotalPhysical > 0 ? 100 * (1 - (double)memory.AvailablePhysical / memory.TotalPhysical) : 0, notice);
    }, token);

    /// <summary>Explicit system mutation. Purges standby only, never the working sets of arbitrary processes.</summary>
    public async Task<ToolkitMemoryPurgeResult> PurgeStandbyAsync(CancellationToken token = default)
    {
        if (!new WindowsSystemAccess().IsSupportedOS) throw new PlatformNotSupportedException("Alterações são suportadas no Windows 10/11 x64 cliente.");
        if (!await _purge.WaitAsync(0, token).ConfigureAwait(false)) return new(false, null, null, null, "Já existe uma purga em execução.");
        try
        {
            var before = await ReadAsync(token).ConfigureAwait(false); token.ThrowIfCancellationRequested();
            int privilegeStatus = RtlAdjustPrivilege(13, true, false, out bool previous);
            if (privilegeStatus != 0) return new(false, before.StandbyBytes, null, null, $"Privilégio recusado (NTSTATUS 0x{privilegeStatus:X8}); execute elevado se desejar realizar a purga.");
            int status;
            try { int command = 4; status = NtSetSystemInformation(80, ref command, 4); }
            finally { RtlAdjustPrivilege(13, previous, false, out _); }
            if (status != 0) return new(false, before.StandbyBytes, null, null, $"A purga foi recusada pelo Windows (NTSTATUS 0x{status:X8}).");
            ToolkitMemorySnapshot? after = null;
            try { after = await ReadAsync(CancellationToken.None).ConfigureAwait(false); } catch (Win32Exception) { }
            long? freed = before.StandbyBytes is ulong a && after?.StandbyBytes is ulong b ? (long)Math.Max(0, (decimal)a - b) : null;
            return new(true, before.StandbyBytes, after?.StandbyBytes, freed,
                freed is null ? "Windows aceitou a purga; a diferença de standby não pôde ser medida. Ação sem reversão." : "Purga aceita e diferença de standby medida. O cache pode voltar; isso não mede ganho de FPS. Ação sem reversão.");
        }
        finally { _purge.Release(); }
    }
    public ToolkitMemoryScheduler CreateScheduler(int minutes, IProgress<ToolkitMemoryPurgeResult>? results = null) => new(this, minutes, results);
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PerformanceInformation
    {
        public uint Size; public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
    [DllImport("psapi.dll", SetLastError = true)] private static extern bool GetPerformanceInfo(ref PerformanceInformation info, uint size);
    [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int informationClass, ref int information, uint length);
    [DllImport("ntdll.dll")] private static extern int RtlAdjustPrivilege(uint privilege, [MarshalAs(UnmanagedType.U1)] bool enable, [MarshalAs(UnmanagedType.U1)] bool currentThread, [MarshalAs(UnmanagedType.U1)] out bool previous);
}

/// <summary>Opt-in and lifetime-bound. Creating this object never purges RAM; call Start explicitly.</summary>
public sealed class ToolkitMemoryScheduler : IAsyncDisposable
{
    private readonly ToolkitMemoryService _service; private readonly TimeSpan _interval; private readonly IProgress<ToolkitMemoryPurgeResult>? _results;
    private CancellationTokenSource? _cancel; private Task? _loop;
    internal ToolkitMemoryScheduler(ToolkitMemoryService service, int minutes, IProgress<ToolkitMemoryPurgeResult>? results)
    { if (minutes is not (5 or 10 or 15 or 30 or 60)) throw new ArgumentOutOfRangeException(nameof(minutes)); _service = service; _interval = TimeSpan.FromMinutes(minutes); _results = results; }
    public bool IsRunning => _loop is { IsCompleted: false };
    public void Start()
    {
        if (IsRunning) return; _cancel?.Dispose(); _cancel = new(); var token = _cancel.Token;
        _loop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(_interval);
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    try { var result = await _service.PurgeStandbyAsync(token).ConfigureAwait(false); _results?.Report(result); }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException) { _results?.Report(new(false, null, null, null, ex.Message)); }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }, token);
    }
    public async ValueTask DisposeAsync()
    { if (_cancel is not null) await _cancel.CancelAsync().ConfigureAwait(false); if (_loop is not null) { try { await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { } } _cancel?.Dispose(); }
}

public sealed class ToolkitDiagnosticsService
{
    public async Task<ToolkitBottleneckReport> CollectAsync(int seconds, int? processId = null, IProgress<ToolkitDiagnosticSample>? progress = null, CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (seconds is < 10 or > 120 || processId is <= 4) throw new ArgumentOutOfRangeException(nameof(seconds), "Duração de 10 a 120 segundos; PID de jogo opcional.");
        var memory = new ToolkitMemoryService(); var samples = new List<ToolkitDiagnosticSample>(); var notices = new List<string>();
        using var counters = new ToolkitPerformanceCounters();
        var cpu = counters.Add(@"\Processor(_Total)\% Processor Time");
        var cores = counters.Add(@"\Processor(*)\% Processor Time");
        var gpu = counters.Add(@"\GPU Engine(*)\Utilization Percentage");
        counters.Collect(); var watch = Stopwatch.StartNew(); double active = 0, away = 0, last = 0; bool closed = false;
        DateTime? originalStarted = null;
        if (processId is int pid) { using var target = Process.GetProcessById(pid); originalStarted = target.StartTime; }
        while (active < seconds && watch.Elapsed.TotalSeconds < seconds * 4)
        {
            await Task.Delay(1000, token).ConfigureAwait(false); counters.Collect();
            double now = watch.Elapsed.TotalSeconds, dt = now - last; last = now;
            bool focused = true;
            if (processId is int targetPid)
            {
                try { using var target = Process.GetProcessById(targetPid); if (target.HasExited || target.StartTime != originalStarted) { closed = true; break; } }
                catch (ArgumentException) { closed = true; break; }
                GetWindowThreadProcessId(GetForegroundWindow(), out var foreground); focused = foreground == targetPid;
            }
            if (!focused) { away += dt; continue; }
            var ram = await memory.ReadAsync(token).ConfigureAwait(false);
            double? cpuValue = counters.ReadDouble(cpu);
            var coreValues = counters.ReadArray(cores).Where(v => !v.Name.EndsWith("_Total", StringComparison.OrdinalIgnoreCase)).Select(v => v.Value).ToArray();
            var gpuValues = counters.ReadArray(gpu).Where(v => v.Name.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase))
                .GroupBy(v => Regex.Replace(Regex.Replace(v.Name, @"^pid_\d+_", "", RegexOptions.IgnoreCase), @"_eng_\d+_engtype_3D$", "", RegexOptions.IgnoreCase))
                .Select(g => Math.Clamp(g.Sum(x => x.Value), 0, 100)).ToArray();
            var sample = new ToolkitDiagnosticSample(DateTimeOffset.UtcNow, cpuValue is double c ? Math.Clamp(c, 0, 100) : null,
                coreValues.Length > 0 ? Math.Clamp(coreValues.Max(), 0, 100) : null, gpuValues.Length > 0 ? gpuValues.Max() : null, ram.UsedPercent, true);
            samples.Add(sample); active += dt; progress?.Report(sample);
        }
        if (samples.Count < 6) throw new InvalidOperationException(closed ? "O jogo encerrou antes de seis amostras válidas." : "São necessárias seis amostras com o jogo em foco para avaliar a sessão.");
        if (samples.All(s => s.GpuPercent is null)) notices.Add("Contadores GPU 3D indisponíveis; não foi estimado uso de GPU.");
        if (samples.All(s => s.BusiestCorePercent is null)) notices.Add("Leitura por núcleo indisponível; a CPU total pode esconder saturação de uma thread.");
        notices.Add("CPU e GPU representam utilização do sistema/adaptador durante a sessão; temperaturas e VRAM não são inferidas.");
        if (Environment.ProcessorCount > 64) notices.Add("Em múltiplos grupos de processadores, os contadores podem não cobrir todos os grupos.");
        return AnalyzeSamples(samples, active, away, closed, active < seconds - 1, notices);
    }
    public static ToolkitBottleneckReport AnalyzeSamples(IEnumerable<ToolkitDiagnosticSample> input, double measuredSeconds, double awaySeconds = 0, bool processClosed = false, bool partial = false, IReadOnlyList<string>? notices = null)
    {
        var samples = input.Where(s => s.InFocus && double.IsFinite(s.RamPercent) && s.RamPercent is >= 0 and <= 100).ToArray();
        if (samples.Length < 6) throw new ArgumentException("São necessárias pelo menos seis amostras válidas.");
        double? Average(Func<ToolkitDiagnosticSample, double?> selector)
        { var values = samples.Select(selector).Where(v => v is double n && double.IsFinite(n) && n is >= 0 and <= 100).Select(v => v!.Value).ToArray(); return values.Length > 0 ? Math.Round(values.Average(), 1) : null; }
        var cpu = Average(s => s.CpuPercent); var core = Average(s => s.BusiestCorePercent); var gpu = Average(s => s.GpuPercent); var ram = Math.Round(samples.Max(s => s.RamPercent), 1);
        string assessment = ram >= 90 ? "Pressão de memória observada; confirme paginação e consumo por processo." :
            gpu is >= 95 ? "GPU próxima da saturação durante a coleta; compare resolução e qualidade na mesma cena." :
            core is >= 90 && gpu is < 90 ? "Um núcleo esteve próximo da saturação com folga de GPU; investigue limite de CPU na mesma cena." :
            cpu is >= 90 ? "CPU total elevada; confira tarefas concorrentes e comportamento no jogo." :
            gpu is null ? "Coleta parcial sem GPU; não é possível classificar gargalo gráfico." : "Não houve saturação sustentada suficiente para classificar um gargalo nesta sessão.";
        return new(Array.AsReadOnly(samples), cpu, core, gpu, ram, measuredSeconds, awaySeconds, processClosed, partial, assessment, notices ?? []);
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
}

internal sealed class ToolkitPerformanceCounters : IDisposable
{
    private IntPtr _query;
    internal ToolkitPerformanceCounters()
    { var status = PdhOpenQueryW(null, UIntPtr.Zero, out _query); if (status != 0) throw new Win32Exception((int)status, "Não foi possível abrir os contadores de desempenho."); }
    internal IntPtr Add(string path) => PdhAddEnglishCounterW(_query, path, UIntPtr.Zero, out var handle) == 0 ? handle : IntPtr.Zero;
    internal void Collect() { if (_query != IntPtr.Zero) PdhCollectQueryData(_query); }
    internal double? ReadDouble(IntPtr handle) => handle != IntPtr.Zero && PdhGetFormattedCounterValue(handle, 0x200, out _, out var value) == 0 && value.Status is 0 or 1 && double.IsFinite(value.Double) ? value.Double : null;
    internal long? ReadLarge(IntPtr handle) => handle != IntPtr.Zero && PdhGetFormattedCounterValue(handle, 0x400, out _, out var value) == 0 && value.Status is 0 or 1 ? value.Large : null;
    internal IReadOnlyList<(string Name, double Value)> ReadArray(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return [];
        uint bytes = 0, count = 0; uint status = PdhGetFormattedCounterArrayW(handle, 0x200, ref bytes, ref count, IntPtr.Zero);
        if (status != 0x800007D2 || bytes > 16 * 1024 * 1024 || bytes == 0) return [];
        var buffer = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (PdhGetFormattedCounterArrayW(handle, 0x200, ref bytes, ref count, buffer) != 0) return [];
            var result = new List<(string, double)>(); int size = Marshal.SizeOf<CounterArrayItem>();
            if ((ulong)count * (uint)size > bytes) return [];
            for (int i = 0; i < count; i++)
            { var value = Marshal.PtrToStructure<CounterArrayItem>(buffer + i * size); if (value.Value.Status is 0 or 1 && double.IsFinite(value.Value.Double)) result.Add((Marshal.PtrToStringUni(value.Name) ?? "", value.Value.Double)); }
            return result;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public void Dispose() { if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct CounterValue
    { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Double; [FieldOffset(8)] public long Large; }
    [StructLayout(LayoutKind.Sequential)] private struct CounterArrayItem { public IntPtr Name; public CounterValue Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, UIntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, UIntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bytes, ref uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
