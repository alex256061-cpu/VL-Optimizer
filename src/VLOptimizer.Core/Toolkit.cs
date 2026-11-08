namespace VLOptimizer.Core;

/// <summary>Additional, independently implemented tools. Construction never starts collectors or changes Windows.</summary>
public sealed class OptimizerToolkit
{
    public ToolkitCleanupService Cleanup { get; }
    public ToolkitFpsService Fps { get; } = new();
    public ToolkitGameService Games { get; } = new();
    public ToolkitFiveMService FiveM { get; }
    public ToolkitInventoryService Inventory { get; } = new();
    public ToolkitMemoryService Memory { get; } = new();
    public ToolkitDiagnosticsService Diagnostics { get; } = new();
    public ToolkitDiskService Disk { get; } = new();
    public ToolkitSessionService Sessions { get; }
    public ApplicationManagementService Applications { get; }

    public OptimizerToolkit(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        Cleanup = new ToolkitCleanupService();
        FiveM = new ToolkitFiveMService(Path.Combine(Path.GetFullPath(dataDirectory), "fivem"));
        Sessions = new ToolkitSessionService(Path.Combine(Path.GetFullPath(dataDirectory), "sessions"));
        Applications = new ApplicationManagementService(Path.Combine(Path.GetFullPath(dataDirectory), "applications"));
    }
}

public record ToolkitIssue(string Path, string Message);
public record ToolkitCleanupCategory(string Id, string Title, string Description, IReadOnlyList<string> Roots,
    int MinimumAgeDays = 0, IReadOnlyList<string>? BlockingProcesses = null, bool SelectedByDefault = false);
public record ToolkitCleanupFile(string Path, string CategoryId, long Bytes, DateTime LastWriteUtc, DateTime CreationUtc, uint Attributes, string Identity);
public sealed record ToolkitCleanupPreview(Guid Id, DateTimeOffset CreatedAt, IReadOnlyList<ToolkitCleanupFile> Files,
    IReadOnlyList<string> CategoryIds, IReadOnlyList<ToolkitIssue> Issues, bool Complete)
{
    public long TotalBytes => Files.Sum(x => x.Bytes);
    public int FileCount => Files.Count;
    public string Consequence => "Exclusão permanente dos arquivos listados; sem reversão. Arquivos alterados, em uso, cloud ou links serão preservados.";
}
public record ToolkitCleanupResult(long DeletedBytes, int DeletedFiles, int SkippedFiles, int FailedFiles,
    IReadOnlyList<ToolkitIssue> Issues, bool Canceled);
public sealed record ToolkitRecycleBinPreview(Guid Id, string? DriveRoot, long Bytes, long Items, DateTimeOffset CreatedAt);
public record ToolkitRecycleBinResult(bool Success, string Message, long? RemainingBytes, long? RemainingItems);
public record ToolkitDiskNode(string Path, long OwnBytes, long TotalBytes, long FileCount);
public record ToolkitDiskScan(string Root, long TotalBytes, long FileCount, IReadOnlyList<ToolkitDiskNode> Directories,
    IReadOnlyList<ToolkitCleanupFile> LargestFiles, IReadOnlyList<ToolkitIssue> Issues, bool Complete);
public record ToolkitScanProgress(int Directories, long Files, long Bytes, string CurrentPath);

public record ToolkitFrame(int ProcessId, string Application, double Milliseconds, double? TimestampSeconds, string? SwapChain, string? PresentMode);
public record ToolkitFpsStatistics(double Fps, double Low1, double Low01, double FrameTimeMs, int Frames, double FrameDurationSeconds);
public record ToolkitFpsProcess(int ProcessId, string Application, ToolkitFpsStatistics Statistics);
public record ToolkitFpsReport(string Source, IReadOnlyList<ToolkitFpsProcess> Processes, int RejectedRows, IReadOnlyList<string> Notices);
public record ToolkitFpsComparison(ToolkitFpsStatistics Before, ToolkitFpsStatistics After, double FpsChangePercent, double Low1ChangePercent);
public record ToolkitCaptureResult(ToolkitFpsReport Report, int ExitCode, string StandardError, bool CompletedDuration);

public record ToolkitGame(string Id, string Name, string Source, string InstallDirectory, IReadOnlyList<string> Executables, bool IsRunning);
public record ToolkitGameDiscovery(IReadOnlyList<ToolkitGame> Games, IReadOnlyList<ToolkitIssue> Issues);
public record ToolkitInstalledApp(string Id, string Name, string? Version, string? Publisher, string? InstallLocation, string Source, long? EstimatedBytes);
public record ToolkitProcess(int Id, string Name, string? ExecutablePath, long WorkingSetBytes, DateTimeOffset? StartedAt, bool HasWindow);

public record ToolkitFiveMChange(string Element, string? Before, string After, bool WillCreate);
public sealed record ToolkitFiveMPreview(Guid Id, string Path, string Preset, IReadOnlyList<ToolkitFiveMChange> Changes,
    string OriginalSha256, string PlannedSha256, DateTimeOffset CreatedAt);
public record ToolkitFiveMBackup(Guid Id, string Path, string OriginalSha256, string AppliedSha256, DateTimeOffset CreatedAt, bool Restored);
public record ToolkitFiveMResult(bool Success, bool Conflict, string Message, ToolkitFiveMBackup? Backup);
public record ToolkitMemorySnapshot(DateTimeOffset CapturedAt, ulong TotalBytes, ulong AvailableBytes, ulong? CommittedBytes,
    ulong? CommitLimitBytes, ulong? StandbyBytes, double UsedPercent, string? StandbyNotice);
public record ToolkitMemoryPurgeResult(bool Success, ulong? BeforeStandbyBytes, ulong? AfterStandbyBytes, long? FreedBytes, string Message);
public record ToolkitDiagnosticSample(DateTimeOffset At, double? CpuPercent, double? BusiestCorePercent, double? GpuPercent, double RamPercent, bool InFocus);
public record ToolkitBottleneckReport(IReadOnlyList<ToolkitDiagnosticSample> Samples, double? CpuPercent, double? BusiestCorePercent,
    double? GpuPercent, double PeakRamPercent, double MeasuredSeconds, double AwaySeconds, bool ProcessClosed, bool Partial,
    string Assessment, IReadOnlyList<string> Notices);
