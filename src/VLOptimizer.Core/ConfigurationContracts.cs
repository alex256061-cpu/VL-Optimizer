using System.Text.Json;
using Microsoft.Win32;

namespace VLOptimizer.Core;

public enum ConfigurationTargetKind { Registry, Service, PowerPlan, Hibernation, SteamOverlay, NvidiaDrs, Pagefile, DnsIpv4, DisplayMode, AppPowerThrottling }
public enum ConfigurationRefresh { None, Mouse, Keyboard, Theme }
public enum ConfigurationResultStatus { Completed, Partial, Failed, Conflict, Blocked, Reverted, Unchanged }
public enum ConfigurationStepStatus { Pending, Applied, Unchanged, Failed, Conflict, Restored }
public enum ConfigurationPosition { Before, Desired, Diverged, Unreadable }

public sealed record ConfigurationTarget(ConfigurationTargetKind Kind, string Path, string Name = "")
{
    public string ResourceId => $"{Kind}:{Path.ToUpperInvariant()}:{Name.ToUpperInvariant()}";
    public override string ToString() => Kind == ConfigurationTargetKind.Registry ? $"{Path}\\{Name}" : $"{Kind}: {Path}";
}

/// <summary>Lossless registry representation. Binary is base64, multi-string is a JSON array;
/// integers use their signed registry bit representation. Missing values retain key existence.</summary>
public sealed record ConfigurationValue(bool Exists, int Kind, string Json, bool KeyExists = true)
{
    public static ConfigurationValue Missing(bool keyExists = false) => new(false, 0, "null", keyExists);
    public static ConfigurationValue DWord(int value) => new(true, (int)RegistryValueKind.DWord, JsonSerializer.Serialize(value));
    public static ConfigurationValue String(string value) => new(true, (int)RegistryValueKind.String, JsonSerializer.Serialize(value));
    public static ConfigurationValue Binary(byte[] value) => new(true, (int)RegistryValueKind.Binary, JsonSerializer.Serialize(value));
    public static ConfigurationValue State<T>(T value) => new(true, 0, JsonSerializer.Serialize(value));
    public string Display => !Exists ? "Ausente" : Kind == (int)RegistryValueKind.Binary ? $"Binário: {Json}" : Json;
}

public sealed record ConfigurationChange(ConfigurationTarget Target, ConfigurationValue On, ConfigurationValue Off);
public sealed record ConfigurationDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Category { get; init; } = "sistema";
    public string Description { get; init; } = "";
    public string Warning { get; init; } = "";
    public string Restart { get; init; } = "";
    public string SourceUrl { get; init; } = "https://learn.microsoft.com/windows/win32/sysinfo/registry";
    public string SourceEvidence { get; init; } = "";
    public int MinimumBuild { get; init; } = 19041;
    public string? Capability { get; init; }
    public string? UnsupportedReason { get; init; }
    public ConfigurationRefresh Refresh { get; init; }
    public bool AllowDisable { get; init; } = true;
    public IReadOnlyList<ConfigurationChange> Changes { get; init; } = [];
}

public sealed record ConfigurationEnvironment(bool IsSupportedOS, bool IsAdministrator, int WindowsBuild,
    bool IsLaptop = false, bool IsDualCcdX3d = false, string GpuName = "");
public sealed record ConfigurationServiceState(uint StartType, bool Running, bool DelayedAutoStart = false);
public sealed record ConfigurationHibernationState(bool Enabled, int? FileType, int? SizePercent);
public sealed record ConfigurationPagefileState(bool AutomaticManaged, ConfigurationValue? PagingFiles);
public sealed record ConfigurationDnsState(bool Automatic, IReadOnlyList<string> Servers);
public sealed record ConfigurationDisplayMode(uint Width, uint Height, uint Frequency, uint BitsPerPixel, uint Orientation, int PositionX, int PositionY, uint FixedOutput);

public sealed record ConfigurationStep
{
    public required ConfigurationTarget Target { get; init; }
    public required ConfigurationValue Before { get; init; }
    public required ConfigurationValue Desired { get; init; }
    public bool RequiresAdmin { get; init; }
    public ConfigurationValue? ObservedAfter { get; init; }
    public ConfigurationStepStatus Status { get; init; }
    public string Message { get; init; } = "";
}
public sealed record ConfigurationSnapshot
{
    public int Schema { get; init; } = 1;
    public required string DefinitionId { get; init; }
    public bool Enabled { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<ConfigurationStep> Steps { get; init; } = [];
}
public sealed record ConfigurationPreview
{
    public required ConfigurationDefinition Definition { get; init; }
    public required ConfigurationSnapshot Snapshot { get; init; }
    public bool Enabled => Snapshot.Enabled;
    public IReadOnlyList<ConfigurationStep> Steps => Snapshot.Steps;
    public bool CanApply { get; init; }
    public string? BlockReason { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
public sealed record ConfigurationApplyResult(ConfigurationResultStatus Status, string Message, ConfigurationSnapshot Snapshot)
{
    public IReadOnlyList<ConfigurationStep> Steps => Snapshot.Steps;
    public bool CanRestore => Steps.Any(x => x.Before != x.Desired && x.Status is ConfigurationStepStatus.Applied or ConfigurationStepStatus.Failed or ConfigurationStepStatus.Pending);
}
public sealed record ConfigurationInspection(ConfigurationStep Step, ConfigurationPosition Position, ConfigurationValue? Current, string? Error = null);

/// <summary>Closed operation boundary; no arbitrary commands or executable scripts are accepted.</summary>
public interface IConfigurationSystemAccess
{
    ConfigurationEnvironment InspectEnvironment();
    IReadOnlyList<ConfigurationChange> ResolveDynamicChanges(string capability);
    IReadOnlyList<ConfigurationChange> ResolveDynamicChangesForRestore(string capability) => ResolveDynamicChanges(capability);
    ConfigurationValue Read(ConfigurationTarget target);
    void Write(ConfigurationTarget target, ConfigurationValue value);
    void Refresh(ConfigurationRefresh refresh);
}

public interface IConfigurationEngine
{
    IReadOnlyList<ConfigurationDefinition> GetDefinitions();
    Task<ConfigurationPreview> PreviewAsync(string id, bool enabled, CancellationToken token);
    Task<ConfigurationApplyResult> ApplyAsync(ConfigurationPreview approvedPreview, CancellationToken token);
    Task<ConfigurationApplyResult> RestoreAsync(ConfigurationSnapshot snapshot, CancellationToken token);
    Task<IReadOnlyList<ConfigurationInspection>> CheckSnapshotAsync(ConfigurationSnapshot snapshot, CancellationToken token);
}
