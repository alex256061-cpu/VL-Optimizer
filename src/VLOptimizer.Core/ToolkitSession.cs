using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace VLOptimizer.Core;

public record ToolkitSessionOptions(IReadOnlyList<string> GameExecutables, IReadOnlyList<string> BackgroundExecutables,
    Guid? PowerPlanId = null, bool RaiseGamePriority = false);
public record ToolkitProcessIdentity(int Id, long StartedUtcTicks, string ExecutablePath);
public record ToolkitSessionProcess(ToolkitProcessIdentity Identity, ProcessPriorityClass Priority);
public enum ToolkitSessionStatus { Prepared, Active, Ending, Ended, Partial }
public enum ToolkitSessionChangeState { Saved, Applied, Unchanged, Restored, Exited, Conflict, Failed }
public record ToolkitSessionProcessChange(ToolkitProcessIdentity Identity, ProcessPriorityClass OriginalPriority, ProcessPriorityClass AppliedPriority,
    ToolkitSessionChangeState State, string Message);
public record ToolkitSessionPowerChange(Guid OriginalPlanId, Guid AppliedPlanId, ToolkitSessionChangeState State, string Message);
public record ToolkitSessionRecord(Guid Id, DateTimeOffset CreatedAt, ToolkitSessionOptions Options, ToolkitSessionStatus Status,
    IReadOnlyList<ToolkitSessionProcessChange> Processes, ToolkitSessionPowerChange? Power, IReadOnlyList<string> Notices, DateTimeOffset? EndedAt = null)
{
    public bool NeedsRecovery => Status != ToolkitSessionStatus.Ended;
}

/// <summary>Injectable boundary; implementations must compare PID, process start time AND full executable path.</summary>
public interface IToolkitSessionSystem
{
    bool IsSupportedOS { get; }
    IReadOnlyList<ToolkitSessionProcess> ReadProcesses();
    ProcessPriorityClass? ReadPriority(ToolkitProcessIdentity identity);
    void SetPriority(ToolkitProcessIdentity identity, ProcessPriorityClass priority);
    IReadOnlyList<PowerPlan> ReadPowerPlans();
    Guid ReadActivePowerPlan();
    void SetActivePowerPlan(Guid plan);
}

/// <summary>
/// Explicit sessions only. Persists each original state before applying it. Reading records never recovers
/// or changes the machine; after an interruption the user must explicitly end/recover the saved session.
/// </summary>
public sealed class ToolkitSessionService
{
    private readonly string _directory;
    private readonly IToolkitSessionSystem _system;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private int _watching;
    public bool IsWatching => Volatile.Read(ref _watching) != 0;
    public ToolkitSessionService(string dataDirectory, IToolkitSessionSystem? system = null)
    { _directory = ToolkitPaths.Full(dataDirectory); _system = system ?? new ToolkitNativeSessionSystem(); }

    public async Task<IReadOnlyList<ToolkitSessionRecord>> GetSessionsAsync(CancellationToken token = default)
        => (await ReadAsync(token).ConfigureAwait(false)).AsReadOnly();

    public Task<ToolkitSessionRecord> BeginAsync(ToolkitSessionOptions options, CancellationToken token = default)
        => BeginCoreAsync(options, Guid.NewGuid(), token);
    private async Task<ToolkitSessionRecord> BeginCoreAsync(ToolkitSessionOptions options, Guid sessionId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (!_system.IsSupportedOS) throw new PlatformNotSupportedException("Sessões que alteram prioridades/energia exigem Windows 10/11 x64 cliente.");
        options = Normalize(options); var mutex = Locks.GetOrAdd(_directory, _ => new(1, 1)); await mutex.WaitAsync(token).ConfigureAwait(false);
        try
        {
            EnsureStorage(); using var fileLock = await FileLockAsync(token).ConfigureAwait(false); var records = await ReadAsync(token).ConfigureAwait(false);
            if (records.Any(r => r.NeedsRecovery)) throw new InvalidOperationException("Encerre ou recupere a sessão anterior antes de iniciar outra.");
            var processes = _system.ReadProcesses();
            var games = processes.Where(p => options.GameExecutables.Contains(p.Identity.ExecutablePath, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (games.Length == 0) throw new InvalidOperationException("Nenhum dos executáveis de jogo selecionados está em execução com identidade verificável.");
            var changes = new List<ToolkitSessionProcessChange>();
            foreach (var process in processes)
            {
                ProcessPriorityClass? requested = options.BackgroundExecutables.Contains(process.Identity.ExecutablePath, StringComparer.OrdinalIgnoreCase) ? ProcessPriorityClass.BelowNormal :
                    options.RaiseGamePriority && games.Any(g => g.Identity == process.Identity) ? ProcessPriorityClass.High : null;
                if (requested is ProcessPriorityClass priority)
                    changes.Add(new(process.Identity, process.Priority, priority, process.Priority == priority ? ToolkitSessionChangeState.Unchanged : ToolkitSessionChangeState.Saved, "Estado original registrado."));
            }
            ToolkitSessionPowerChange? power = null;
            if (options.PowerPlanId is Guid selected)
            {
                if (!_system.ReadPowerPlans().Any(p => p.Id == selected)) throw new InvalidOperationException("O plano de energia selecionado não existe.");
                var current = _system.ReadActivePowerPlan(); power = new(current, selected, current == selected ? ToolkitSessionChangeState.Unchanged : ToolkitSessionChangeState.Saved, "Plano original registrado.");
            }
            var notices = new List<string> { "Somente os executáveis selecionados são alterados. Nenhum aplicativo é encerrado. Sessão precisa ser encerrada explicitamente ou pelo monitor iniciado pelo usuário." };
            foreach (var absent in options.BackgroundExecutables.Where(path => !processes.Any(p => p.Identity.ExecutablePath.Equals(path, StringComparison.OrdinalIgnoreCase)))) notices.Add($"Aplicativo não encontrado ou sem acesso ao caminho: {absent}");
            var record = new ToolkitSessionRecord(sessionId, DateTimeOffset.UtcNow, options, ToolkitSessionStatus.Prepared, changes.ToArray(), power, notices.ToArray());
            records.Add(record); await SaveAsync(records, token).ConfigureAwait(false);
            // Every step already has a durable original before the first system mutation.
            for (int index = 0; index < changes.Count; index++)
            {
                var change = changes[index]; if (change.State == ToolkitSessionChangeState.Unchanged) continue;
                token.ThrowIfCancellationRequested();
                try
                {
                    var current = _system.ReadPriority(change.Identity);
                    if (current is null) changes[index] = change with { State = ToolkitSessionChangeState.Exited, Message = "Processo encerrou ou sua identidade mudou; preservado." };
                    else if (current != change.OriginalPriority) changes[index] = change with { State = ToolkitSessionChangeState.Conflict, Message = "A prioridade mudou depois do snapshot; preservada." };
                    else
                    {
                        _system.SetPriority(change.Identity, change.AppliedPriority);
                        changes[index] = change with { State = _system.ReadPriority(change.Identity) == change.AppliedPriority ? ToolkitSessionChangeState.Applied : ToolkitSessionChangeState.Failed, Message = "Alteração executada; resultado conferido pela identidade do processo." };
                    }
                }
                catch (Exception ex) when (IsSystemFailure(ex)) { changes[index] = change with { State = ToolkitSessionChangeState.Failed, Message = ex.Message }; }
                record = record with { Processes = changes.ToArray() }; Replace(records, record); await SaveAsync(records, CancellationToken.None).ConfigureAwait(false);
            }
            if (power is { State: ToolkitSessionChangeState.Saved })
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (_system.ReadActivePowerPlan() != power.OriginalPlanId) power = power with { State = ToolkitSessionChangeState.Conflict, Message = "Outro programa alterou a energia; plano preservado." };
                    else
                    {
                        _system.SetActivePowerPlan(power.AppliedPlanId);
                        power = power with { State = _system.ReadActivePowerPlan() == power.AppliedPlanId ? ToolkitSessionChangeState.Applied : ToolkitSessionChangeState.Failed, Message = "Plano selecionado aplicado e conferido." };
                    }
                }
                catch (Exception ex) when (IsSystemFailure(ex)) { power = power with { State = ToolkitSessionChangeState.Failed, Message = ex.Message }; }
            }
            bool partial = changes.Any(c => c.State is ToolkitSessionChangeState.Conflict or ToolkitSessionChangeState.Failed) || power?.State is ToolkitSessionChangeState.Conflict or ToolkitSessionChangeState.Failed;
            record = record with { Processes = changes.ToArray(), Power = power, Status = partial ? ToolkitSessionStatus.Partial : ToolkitSessionStatus.Active };
            Replace(records, record); await SaveAsync(records, CancellationToken.None).ConfigureAwait(false); return record;
        }
        finally { mutex.Release(); }
    }

    public async Task<ToolkitSessionRecord> EndAsync(Guid sessionId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); if (!_system.IsSupportedOS) throw new PlatformNotSupportedException("Recuperação de sessão exige Windows 10/11 x64 cliente.");
        var mutex = Locks.GetOrAdd(_directory, _ => new(1, 1)); await mutex.WaitAsync(token).ConfigureAwait(false);
        try
        {
            EnsureStorage(); using var fileLock = await FileLockAsync(token).ConfigureAwait(false); var records = await ReadAsync(token).ConfigureAwait(false);
            var record = records.SingleOrDefault(r => r.Id == sessionId) ?? throw new ArgumentException("Sessão não encontrada.");
            if (!record.NeedsRecovery) return record;
            record = record with { Status = ToolkitSessionStatus.Ending }; Replace(records, record); await SaveAsync(records, token).ConfigureAwait(false);
            var changes = record.Processes.ToList(); var power = record.Power;
            for (int index = changes.Count - 1; index >= 0; index--)
            {
                token.ThrowIfCancellationRequested(); var change = changes[index];
                if (change.State is ToolkitSessionChangeState.Restored or ToolkitSessionChangeState.Exited or ToolkitSessionChangeState.Unchanged or ToolkitSessionChangeState.Conflict) continue;
                try
                {
                    var current = _system.ReadPriority(change.Identity);
                    if (current is null) changes[index] = change with { State = ToolkitSessionChangeState.Exited, Message = "Processo original já encerrou; nenhum novo PID foi alterado." };
                    else if (current == change.OriginalPriority) changes[index] = change with { State = ToolkitSessionChangeState.Restored, Message = "A prioridade original já está presente." };
                    else if (current != change.AppliedPriority) changes[index] = change with { State = ToolkitSessionChangeState.Conflict, Message = "Prioridade alterada externamente; preservada." };
                    else
                    {
                        _system.SetPriority(change.Identity, change.OriginalPriority);
                        changes[index] = change with { State = _system.ReadPriority(change.Identity) == change.OriginalPriority ? ToolkitSessionChangeState.Restored : ToolkitSessionChangeState.Failed, Message = "Prioridade original restaurada e conferida." };
                    }
                }
                catch (Exception ex) when (IsSystemFailure(ex)) { changes[index] = change with { State = ToolkitSessionChangeState.Failed, Message = ex.Message }; }
                record = record with { Processes = changes.ToArray() }; Replace(records, record); await SaveAsync(records, CancellationToken.None).ConfigureAwait(false);
            }
            if (power is not null && power.State is not (ToolkitSessionChangeState.Restored or ToolkitSessionChangeState.Unchanged or ToolkitSessionChangeState.Conflict))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var current = _system.ReadActivePowerPlan();
                    if (current == power.OriginalPlanId) power = power with { State = ToolkitSessionChangeState.Restored, Message = "O plano original já está ativo." };
                    else if (current != power.AppliedPlanId) power = power with { State = ToolkitSessionChangeState.Conflict, Message = "Energia alterada externamente; plano preservado." };
                    else if (!_system.ReadPowerPlans().Any(p => p.Id == power.OriginalPlanId)) power = power with { State = ToolkitSessionChangeState.Conflict, Message = "O plano original foi removido; escolha manualmente outro plano." };
                    else
                    {
                        _system.SetActivePowerPlan(power.OriginalPlanId);
                        power = power with { State = _system.ReadActivePowerPlan() == power.OriginalPlanId ? ToolkitSessionChangeState.Restored : ToolkitSessionChangeState.Failed, Message = "Plano original restaurado e conferido." };
                    }
                }
                catch (Exception ex) when (IsSystemFailure(ex)) { power = power with { State = ToolkitSessionChangeState.Failed, Message = ex.Message }; }
            }
            bool unresolved = changes.Any(c => c.State is ToolkitSessionChangeState.Saved or ToolkitSessionChangeState.Applied or ToolkitSessionChangeState.Failed) || power?.State is ToolkitSessionChangeState.Saved or ToolkitSessionChangeState.Applied or ToolkitSessionChangeState.Failed;
            // Conflicts are a completed conditional restore: keep the external decision, do not retry automatically.
            record = record with { Status = unresolved ? ToolkitSessionStatus.Partial : ToolkitSessionStatus.Ended, Processes = changes.ToArray(), Power = power, EndedAt = unresolved ? null : DateTimeOffset.UtcNow };
            Replace(records, record); await SaveAsync(records, CancellationToken.None).ConfigureAwait(false); return record;
        }
        finally { mutex.Release(); }
    }
    public Task<ToolkitSessionRecord> RecoverAsync(Guid sessionId, CancellationToken token = default) => EndAsync(sessionId, token);

    public async Task WatchAsync(ToolkitSessionOptions options, IProgress<ToolkitSessionRecord>? progress = null, CancellationToken token = default)
    {
        if (!_system.IsSupportedOS) throw new PlatformNotSupportedException("Monitor de sessões exige Windows 10/11 x64 cliente.");
        options = Normalize(options);
        if (Interlocked.CompareExchange(ref _watching, 1, 0) != 0) throw new InvalidOperationException("O monitor de sessão já está ativo.");
        ToolkitSessionRecord? ownSession = null; ToolkitProcessIdentity[] watchedGames = []; Guid? ownedId = null;
        try
        {
            if ((await GetSessionsAsync(token).ConfigureAwait(false)).Any(s => s.NeedsRecovery)) throw new InvalidOperationException("Recupere a sessão pendente antes de ativar o monitor.");
            while (!token.IsCancellationRequested)
            {
                var running = _system.ReadProcesses();
                if (ownSession is null)
                {
                    watchedGames = running.Where(p => options.GameExecutables.Contains(p.Identity.ExecutablePath, StringComparer.OrdinalIgnoreCase)).Select(p => p.Identity).ToArray();
                    if (watchedGames.Length > 0) { ownedId = Guid.NewGuid(); ownSession = await BeginCoreAsync(options, ownedId.Value, token).ConfigureAwait(false); progress?.Report(ownSession); }
                }
                else if (!running.Any(p => watchedGames.Contains(p.Identity)))
                { var ended = await EndAsync(ownSession.Id, CancellationToken.None).ConfigureAwait(false); progress?.Report(ended); ownSession = null; ownedId = null; }
                await Task.Delay(2000, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            try
            {
                if (ownSession is not null)
                { var ended = await EndAsync(ownSession.Id, CancellationToken.None).ConfigureAwait(false); progress?.Report(ended); }
                else if (ownedId is Guid pendingId && (await GetSessionsAsync(CancellationToken.None).ConfigureAwait(false)).Any(s => s.Id == pendingId && s.NeedsRecovery))
                { var ended = await EndAsync(pendingId, CancellationToken.None).ConfigureAwait(false); progress?.Report(ended); }
            }
            finally { Interlocked.Exchange(ref _watching, 0); }
        }
    }

    private static ToolkitSessionOptions Normalize(ToolkitSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string[] Paths(IReadOnlyList<string> paths) => paths.Select(ToolkitPaths.Full).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var games = Paths(options.GameExecutables); var background = Paths(options.BackgroundExecutables);
        if (games.Length is 0 or > 50 || background.Length > 100 || games.Concat(background).Any(p => !Path.GetExtension(p).Equals(".exe", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Selecione de 1 a 50 executáveis de jogo e até 100 aplicativos de fundo.");
        if (games.Intersect(background, StringComparer.OrdinalIgnoreCase).Any()) throw new ArgumentException("Um executável não pode ser jogo e aplicativo de fundo na mesma sessão.");
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (games.Concat(background).Any(p => ToolkitPaths.IsWithin(p, windows))) throw new ArgumentException("Executáveis da pasta Windows não podem ser alvos de sessão.");
        return options with { GameExecutables = Array.AsReadOnly(games), BackgroundExecutables = Array.AsReadOnly(background) };
    }
    private static bool IsSystemFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException;
    private void EnsureStorage() { ToolkitPaths.EnsureNoLinks(_directory); Directory.CreateDirectory(_directory); ToolkitPaths.EnsureNoLinks(_directory); }
    private async Task<FileStream> FileLockAsync(CancellationToken token)
    {
        string path = Path.Combine(_directory, "sessions.lock"); ToolkitPaths.EnsureNoLinks(path);
        for (int retry = 0; ; retry++)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (retry < 50) { await Task.Delay(100, token).ConfigureAwait(false); }
        }
    }
    private async Task<List<ToolkitSessionRecord>> ReadAsync(CancellationToken token)
    {
        string path = Path.Combine(_directory, "sessions.json"); if (!File.Exists(path)) return [];
        ToolkitPaths.EnsureNoLinks(path); if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Histórico de sessão excede o limite.");
        var records = JsonSerializer.Deserialize<List<ToolkitSessionRecord>>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false)) ?? throw new InvalidDataException("Histórico de sessão inválido.");
        if (records.Count > 5000 || records.Select(r => r.Id).Distinct().Count() != records.Count) throw new InvalidDataException("Histórico de sessão inválido ou duplicado.");
        return records;
    }
    private async Task SaveAsync(List<ToolkitSessionRecord> records, CancellationToken token)
    {
        string path = Path.Combine(_directory, "sessions.json"), temp = Path.Combine(_directory, "sessions-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            ToolkitPaths.EnsureNoLinks(path);
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
            { await JsonSerializer.SerializeAsync(stream, records, Json, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false); stream.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void Replace(List<ToolkitSessionRecord> records, ToolkitSessionRecord record) => records[records.FindIndex(r => r.Id == record.Id)] = record;
}

public sealed class ToolkitNativeSessionSystem : IToolkitSessionSystem
{
    private readonly IWindowsSystemAccess _power;
    public ToolkitNativeSessionSystem(IWindowsSystemAccess? power = null) => _power = power ?? new WindowsSystemAccess();
    public bool IsSupportedOS => _power.IsSupportedOS;
    public IReadOnlyList<ToolkitSessionProcess> ReadProcesses()
    {
        var result = new List<ToolkitSessionProcess>(); var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
                try
                {
                    if (process.Id <= 4 || process.Id == Environment.ProcessId || process.HasExited) continue;
                    string? path = process.MainModule?.FileName; if (path is null) continue;
                    result.Add(new(new(process.Id, process.StartTime.ToUniversalTime().Ticks, ToolkitPaths.Full(path)), process.PriorityClass));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { }
            return result;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    public ProcessPriorityClass? ReadPriority(ToolkitProcessIdentity identity)
    {
        try { using var process = OpenVerified(identity); return process?.PriorityClass; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return null; }
    }
    public void SetPriority(ToolkitProcessIdentity identity, ProcessPriorityClass priority)
    {
        if (!IsSupportedOS) throw new PlatformNotSupportedException();
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        using var process = OpenVerified(identity) ?? throw new InvalidOperationException("A identidade do processo mudou; prioridade preservada.");
        process.PriorityClass = priority;
    }
    private static Process? OpenVerified(ToolkitProcessIdentity identity)
    {
        if (identity.Id <= 4 || identity.Id == Environment.ProcessId) throw new ArgumentException("Processo protegido.");
        var process = Process.GetProcessById(identity.Id);
        try
        {
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != identity.StartedUtcTicks || !string.Equals(process.MainModule?.FileName, identity.ExecutablePath, StringComparison.OrdinalIgnoreCase)) { process.Dispose(); return null; }
            // .NET holds the opened process handle, so an exited PID cannot be retargeted to a new process.
            _ = process.SafeHandle; return process;
        }
        catch { process.Dispose(); throw; }
    }
    public IReadOnlyList<PowerPlan> ReadPowerPlans() => _power.ReadPowerPlans();
    public Guid ReadActivePowerPlan() => _power.ReadActivePowerPlan();
    public void SetActivePowerPlan(Guid plan) => _power.SetActivePowerPlan(plan);
}
