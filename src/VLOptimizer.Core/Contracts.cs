namespace VLOptimizer.Core;

public record DiskInfo(string Name, long TotalBytes, long FreeBytes);
public record ProcessInfo(int Id, string Name, long MemoryBytes, double? CpuPercent);
public record StartupItem(string Id, string Name, string Command, string Source, bool CanDisable, string Caution);
public record PowerPlan(Guid Id, string Name, bool IsActive);
public record SystemSnapshot
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;
    public string WindowsName { get; init; } = "Windows";
    public int WindowsBuild { get; init; }
    public bool SupportedOS { get; init; }
    public bool IsAdministrator { get; init; }
    public string CpuName { get; init; } = "Não disponível";
    public string GpuName { get; init; } = "Não disponível";
    public ulong InstalledMemoryBytes { get; init; }
    public ulong TotalMemoryBytes { get; init; }
    public ulong AvailableMemoryBytes { get; init; }
    public ulong? CommittedMemoryBytes { get; init; }
    public double? AverageCpuPercent { get; init; }
    public List<double> CpuSamples { get; init; } = [];
    public double ObservationSeconds { get; init; }
    public List<DiskInfo> Disks { get; init; } = [];
    public List<StartupItem> StartupItems { get; init; } = [];
    public List<PowerPlan> PowerPlans { get; init; } = [];
    public bool? OnBattery { get; init; }
    public bool RebootPending { get; init; }
    public List<ProcessInfo> TopProcesses { get; init; } = [];
    public List<string> Notices { get; init; } = [];
}
public enum OperationKind { DisableStartup, SetPowerPlan, Configure, Maintenance }
public enum OperationStatus { Pending, Completed, Skipped, Failed, RestartRequired, Unverified, Reverted, Conflict, PartiallyCompleted }
public record OperationRequest(OperationKind Kind, string TargetId, bool? Enable = null);
public record OperationPreview
{
    public required OperationRequest Request { get; init; }
    public string Title { get; init; } = "";
    public string Explanation { get; init; } = "";
    public string Before { get; init; } = "";
    public string After { get; init; } = "";
    public string Risk { get; init; } = "Baixo";
    public string SourceUrl { get; init; } = "";
    public bool CanApply { get; init; }
    public string? BlockReason { get; init; }
    public IReadOnlyList<string> Resources { get; init; } = [];
}
public record OperationRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string CatalogVersion { get; init; } = "1.0";
    public required OperationRequest Request { get; init; }
    public string Title { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? FinishedAt { get; init; }
    public OperationStatus Status { get; init; }
    public string Before { get; init; } = "";
    public string After { get; init; } = "";
    public string Message { get; init; } = "";
    public string? BackupJson { get; init; }
    public bool CanUndo { get; init; }
}
public record Recommendation(string Id, string Category, string Title, string Explanation, string Priority, string SourceUrl, string? TargetPage = null);
public interface IOptimizerBackend
{
    Task<SystemSnapshot> AnalyzeAsync(IProgress<string>? progress, CancellationToken token);
    Task<OperationPreview> PreviewAsync(OperationRequest request, CancellationToken token);
    Task<OperationRecord> ApplyAsync(OperationPreview approvedPreview, CancellationToken token);
    Task<OperationRecord> UndoAsync(Guid recordId, CancellationToken token);
    Task<IReadOnlyList<OperationRecord>> GetHistoryAsync();
    Task<OperationRecord> RecordActivityAsync(string id, string title, string message, OperationStatus status, CancellationToken token);
    IReadOnlyList<Recommendation> GetRecommendations(SystemSnapshot snapshot);
    string DataDirectory { get; }
}
