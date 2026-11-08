using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;

namespace VLOptimizer.Core;

public enum HardwareQuery { Processor, MemoryModules, Bios, Baseboard, Graphics, Storage, Computer }
public record HardwareRow(string Name, IReadOnlyDictionary<string, string?> Values);
public record HardwareSection(string Id, string Title, string Source, IReadOnlyList<HardwareRow> Rows, string? Error);
public record HardwareInventoryReport(DateTimeOffset CapturedAt, IReadOnlyList<HardwareSection> Sections, IReadOnlyList<string> Findings);
public interface IHardwareInventoryAccess { IReadOnlyList<HardwareRow> Read(HardwareQuery query); }

/// <summary>Read-only inventory with explicit field provenance. No inferred temperatures, XMP state or VRAM.</summary>
public sealed class HardwareInventoryService(IHardwareInventoryAccess? access = null)
{
    private readonly IHardwareInventoryAccess system = access ?? new HardwareInventoryWindowsAccess();
    private static readonly (HardwareQuery Query, string Title, string Source)[] Queries = [
        (HardwareQuery.Processor, "Processador", "WMI Win32_Processor"),
        (HardwareQuery.MemoryModules, "Módulos de memória", "WMI Win32_PhysicalMemory"),
        (HardwareQuery.Bios, "BIOS / firmware", "WMI Win32_BIOS"),
        (HardwareQuery.Baseboard, "Placa-mãe", "WMI Win32_BaseBoard"),
        (HardwareQuery.Graphics, "GPU e driver", "WMI Win32_VideoController"),
        (HardwareQuery.Storage, "Discos físicos", "WMI Win32_DiskDrive"),
        (HardwareQuery.Computer, "Sistema e paginação", "WMI Win32_ComputerSystem") ];

    public Task<HardwareInventoryReport> ReadAsync(IProgress<string>? progress = null, CancellationToken token = default) => Task.Run(() =>
    {
        var sections = new List<HardwareSection>();
        foreach (var query in Queries)
        {
            token.ThrowIfCancellationRequested(); progress?.Report("Consultando " + query.Title.ToLowerInvariant() + "…");
            try { sections.Add(new(query.Query.ToString(), query.Title, query.Source, system.Read(query.Query), null)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { sections.Add(new(query.Query.ToString(), query.Title, query.Source, [], ex.Message)); }
        }
        return new HardwareInventoryReport(DateTimeOffset.Now, sections, Analyze(sections));
    }, token);

    public static IReadOnlyList<string> Analyze(IEnumerable<HardwareSection> sections)
    {
        var data = sections.ToArray(); var findings = new List<string>();
        foreach (var row in data.Where(s => s.Id == HardwareQuery.MemoryModules.ToString()).SelectMany(s => s.Rows))
        {
            if (row.Values.TryGetValue("ConfiguredClockSpeed", out var configured) && row.Values.TryGetValue("Speed", out var advertised)
                && uint.TryParse(configured, out var current) && uint.TryParse(advertised, out var rated) && current > 0 && rated > current)
                findings.Add($"{row.Name}: configuração {current} MT/s, valor Speed informado pelo firmware {rated} MT/s. Confira o perfil de memória e a compatibilidade na BIOS; esta comparação não comprova XMP/EXPO desligado.");
        }
        findings.Add("XMP/EXPO, Resizable BAR e temperatura do processador não são inferidos por nome de peça ou tamanho de BAR. Fontes indisponíveis aparecem sem valor.");
        findings.Add("Data/versão do driver e da BIOS são inventário, não prova de que são atuais. Confira atualizações no fabricante antes de concluir que uma correção foi instalada.");
        if (data.Any(x => x.Error is not null)) findings.Add("Inventário parcial: uma ou mais fontes não responderam. Os demais grupos continuam disponíveis.");
        return findings;
    }
}

public sealed class HardwareInventoryWindowsAccess : IHardwareInventoryAccess
{
    // The caller selects an enum. Arbitrary WQL, namespace, credentials and scripts are never accepted.
    private static readonly IReadOnlyDictionary<HardwareQuery, (string Class, string[] Fields)> Definitions = new Dictionary<HardwareQuery, (string, string[])>
    {
        [HardwareQuery.Processor] = ("Win32_Processor", ["Name", "Manufacturer", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed", "CurrentClockSpeed"]),
        [HardwareQuery.MemoryModules] = ("Win32_PhysicalMemory", ["DeviceLocator", "BankLabel", "Capacity", "Speed", "ConfiguredClockSpeed", "Manufacturer", "PartNumber", "SMBIOSMemoryType", "FormFactor"]),
        [HardwareQuery.Bios] = ("Win32_BIOS", ["Manufacturer", "SMBIOSBIOSVersion", "ReleaseDate", "SMBIOSMajorVersion", "SMBIOSMinorVersion"]),
        [HardwareQuery.Baseboard] = ("Win32_BaseBoard", ["Manufacturer", "Product", "Version"]),
        [HardwareQuery.Graphics] = ("Win32_VideoController", ["Name", "VideoProcessor", "DriverVersion", "DriverDate", "CurrentHorizontalResolution", "CurrentVerticalResolution", "CurrentRefreshRate"]),
        [HardwareQuery.Storage] = ("Win32_DiskDrive", ["Model", "InterfaceType", "MediaType", "Size", "BytesPerSector", "FirmwareRevision"]),
        [HardwareQuery.Computer] = ("Win32_ComputerSystem", ["Manufacturer", "Model", "SystemType", "TotalPhysicalMemory", "AutomaticManagedPagefile"])
    };

    public IReadOnlyList<HardwareRow> Read(HardwareQuery query)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Inventário disponível no Windows.");
        if (!Definitions.TryGetValue(query, out var definition)) throw new ArgumentOutOfRangeException(nameof(query));
        object? locator = null, services = null, collection = null;
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator") ?? throw new PlatformNotSupportedException("Consulta WMI indisponível.");
            locator = Activator.CreateInstance(type) ?? throw new IOException("Não foi possível iniciar o serviço de consulta.");
            services = ((dynamic)locator).ConnectServer(".", @"root\cimv2");
            // The scripting collection's .NET enumerator needs a bidirectional result set.
            collection = ((dynamic)services).ExecQuery("SELECT " + string.Join(',', definition.Fields) + " FROM " + definition.Class, "WQL", 0);
            var rows = new List<HardwareRow>();
            foreach (object item in (IEnumerable)collection)
            {
                try
                {
                    if (rows.Count >= 128) throw new InvalidDataException("Fonte ultrapassou o limite de itens.");
                    var values = new Dictionary<string, string?>(); object? properties = null;
                    try
                    {
                        properties = ((dynamic)item).Properties_;
                        foreach (string field in definition.Fields)
                        {
                            object? property = null;
                            try { property = ((dynamic)properties).Item(field, 0); object? value = ((dynamic)property).Value; values[field] = Format(value); }
                            catch (COMException) { values[field] = null; }
                            finally { Release(property); }
                        }
                    }
                    finally { Release(properties); }
                    string name = values.GetValueOrDefault("Name") ?? values.GetValueOrDefault("DeviceLocator") ?? values.GetValueOrDefault("Model") ?? values.GetValueOrDefault("Product") ?? (definition.Class + " " + (rows.Count + 1));
                    rows.Add(new(name.Trim(), values));
                }
                finally { Release(item); }
            }
            return rows;
        }
        finally { Release(collection); Release(services); Release(locator); }
    }
    private static string? Format(object? value) => value switch
    {
        null or DBNull => null, Array array => string.Join("; ", array.Cast<object?>().Select(Format)),
        IFormattable formattable => Limit(formattable.ToString(null, CultureInfo.InvariantCulture)), _ => Limit(value.ToString())
    };
    private static string? Limit(string? value) => value is { Length: > 4096 } ? value[..4096] : value;
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
}

public record MouseEventEstimate(int Samples, double MedianIntervalMs, double EstimatedEventsPerSecond, double P95IntervalMs);
public static class MouseEventStatistics
{
    public static MouseEventEstimate? Analyze(IEnumerable<double> timestampsMs)
    {
        var timestamps = timestampsMs.Where(double.IsFinite).Take(100_001).ToArray();
        var intervals = timestamps.Zip(timestamps.Skip(1), (a, b) => b - a).Where(x => x > 0 && x < 1000).Order().ToArray();
        if (intervals.Length < 10) return null;
        int half = intervals.Length / 2; double median = intervals.Length % 2 == 0 ? (intervals[half - 1] + intervals[half]) / 2 : intervals[half];
        return new(intervals.Length, median, 1000 / median, intervals[Math.Clamp((int)Math.Ceiling(intervals.Length * .95) - 1, 0, intervals.Length - 1)]);
    }
}
