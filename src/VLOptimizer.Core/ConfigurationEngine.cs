namespace VLOptimizer.Core;

/// <summary>Serializes the reversible configuration workflow; persistence is owned by the existing journal.</summary>
public sealed class ConfigurationEngine : IConfigurationEngine
{
    private readonly IConfigurationSystemAccess system;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<ConfigurationPreview, DateTimeOffset> issued = new(ReferenceEqualityComparer.Instance);
    private readonly object approvalLock = new();
    public ConfigurationEngine(IConfigurationSystemAccess? system = null) => this.system = system ?? new ConfigurationWindowsAccess();
    public IReadOnlyList<ConfigurationDefinition> GetDefinitions() => OptimizationCatalog.All;

    public Task<ConfigurationPreview> PreviewAsync(string id, bool enabled, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var definition = OptimizationCatalog.Find(id);
        var empty = new ConfigurationSnapshot { DefinitionId = id, Enabled = enabled };
        if (definition is null)
            return new ConfigurationPreview { Definition = new() { Id = id, Title = "Configuração desconhecida" }, Snapshot = empty, BlockReason = "Identificador fora do catálogo." };
        try
        {
            var env = system.InspectEnvironment();
            var blocked = BlockReason(definition, env);
            if (!enabled && !definition.AllowDisable) blocked = "Esta ação tem apenas aplicação. Para recuperar o estado anterior exato, use Restaurar no histórico.";
            if (blocked is not null) return new ConfigurationPreview { Definition = definition, Snapshot = empty, BlockReason = blocked };
            var changes = Resolve(definition);
            if (changes.Count == 0) return new ConfigurationPreview { Definition = definition, Snapshot = empty, BlockReason = "Nenhum dispositivo ou alvo compatível foi encontrado." };
            var steps = changes.Select(change =>
            {
                token.ThrowIfCancellationRequested();
                var before = system.Read(change.Target);
                var desired = enabled ? change.On : change.Off;
                // Deleting a value does not remove a key that was already present.
                if (!desired.Exists) desired = desired with { KeyExists = before.KeyExists };
                // Preserve whether a stopped service was stopped; enabling telemetry is an explicit start.
                return new ConfigurationStep { Target = change.Target, Before = before, Desired = desired, RequiresAdmin = RequiresAdmin(change.Target),
                    Status = Equal(before, desired) ? ConfigurationStepStatus.Unchanged : ConfigurationStepStatus.Pending };
            }).ToArray();
            string? reason = steps.Any(x => x.RequiresAdmin && !env.IsAdministrator) ? "Execute o VL Optimizer como administrador para alterar estes alvos." : null;
            if (reason is null && steps.All(x => Equal(x.Before, x.Desired))) reason = "Todos os valores consultados já correspondem à opção selecionada.";
            var warnings = new List<string>();
            if (!string.IsNullOrWhiteSpace(definition.Warning)) warnings.Add(definition.Warning);
            if (!string.IsNullOrWhiteSpace(definition.Restart)) warnings.Add(definition.Restart == "rede" ? "A alteração será usada após reiniciar o adaptador ou o Windows; a conexão não será interrompida automaticamente." : definition.Restart == "explorer" ? "Encerre a sessão e entre novamente para atualizar o Explorer; janelas abertas não serão encerradas automaticamente." : "Pode ser necessário reiniciar o Windows para que a configuração entre em vigor.");
            if (env.IsLaptop && id == "hibernate") warnings.Add("Desativar hibernação remove uma opção de suspensão útil em notebooks.");
            var preview = new ConfigurationPreview { Definition = definition, Snapshot = empty with { Steps = steps }, CanApply = reason is null, BlockReason = reason, Warnings = warnings };
            if (preview.CanApply) lock (approvalLock)
            {
                foreach (var old in issued.Where(x => DateTimeOffset.UtcNow - x.Value > TimeSpan.FromMinutes(10)).Select(x => x.Key).ToArray()) issued.Remove(old);
                issued[preview] = DateTimeOffset.UtcNow;
            }
            return preview;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new ConfigurationPreview { Definition = definition, Snapshot = empty, BlockReason = $"Consulta indisponível: {ex.Message}" }; }
    }, token);

    public async Task<ConfigurationApplyResult> ApplyAsync(ConfigurationPreview approvedPreview, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            bool authorized;
            lock (approvalLock) authorized = issued.Remove(approvedPreview, out var when) && DateTimeOffset.UtcNow - when <= TimeSpan.FromMinutes(10);
            if (!authorized || !approvedPreview.CanApply) return new(ConfigurationResultStatus.Blocked, "A prévia não foi emitida por este motor, expirou ou já foi utilizada. Consulte os valores novamente.", approvedPreview.Snapshot);
            var validation = Validate(approvedPreview.Snapshot, false);
            if (validation is not null) return new(ConfigurationResultStatus.Blocked, validation, approvedPreview.Snapshot);
            // Validate every resource before the first write, preventing partial work after a known stale preview.
            foreach (var step in approvedPreview.Steps)
            {
                token.ThrowIfCancellationRequested();
                if (!Equal(system.Read(step.Target), step.Before)) return new(ConfigurationResultStatus.Conflict, "Um alvo mudou depois da prévia. Nenhuma alteração foi iniciada; consulte novamente.", approvedPreview.Snapshot);
            }
            return await Task.Run(() => Execute(approvedPreview.Snapshot, false, token), CancellationToken.None).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task<ConfigurationApplyResult> RestoreAsync(ConfigurationSnapshot snapshot, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var validation = Validate(snapshot, true);
            if (validation is not null) return new(ConfigurationResultStatus.Blocked, validation, snapshot);
            return await Task.Run(() => Execute(snapshot, true, token), CancellationToken.None).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public Task<IReadOnlyList<ConfigurationInspection>> CheckSnapshotAsync(ConfigurationSnapshot snapshot, CancellationToken token) => Task.Run<IReadOnlyList<ConfigurationInspection>>(() =>
    {
        var error = Validate(snapshot, true, requireAdmin: false);
        if (error is not null) return (snapshot.Steps ?? []).Where(x => x is not null).Select(x => new ConfigurationInspection(x, ConfigurationPosition.Unreadable, null, error)).ToArray();
        return snapshot.Steps.Select(x =>
        {
            token.ThrowIfCancellationRequested();
            try { var current = system.Read(x.Target); return new ConfigurationInspection(x, Equal(current, x.Before) ? ConfigurationPosition.Before : Equal(current, x.ObservedAfter ?? x.Desired) ? ConfigurationPosition.Desired : ConfigurationPosition.Diverged, current); }
            catch (Exception ex) { return new ConfigurationInspection(x, ConfigurationPosition.Unreadable, null, ex.Message); }
        }).ToArray();
    }, token);

    private ConfigurationApplyResult Execute(ConfigurationSnapshot snapshot, bool restoring, CancellationToken token)
    {
        var results = new List<ConfigurationStep>();
        bool cancelled = false;
        foreach (var step in snapshot.Steps)
        {
            if (token.IsCancellationRequested) { cancelled = true; results.Add(step with { Message = "Operação cancelada antes deste alvo." }); continue; }
            bool attemptedWrite = false;
            try
            {
                var current = system.Read(step.Target);
                var expected = restoring ? step.ObservedAfter ?? step.Desired : step.Before;
                var desired = restoring ? step.Before : step.Desired;
                bool needsEmptyKeyCleanup = restoring && !desired.Exists && !desired.KeyExists && current.KeyExists;
                if (Equal(current, desired) && !needsEmptyKeyCleanup) { results.Add(step with { Status = restoring ? ConfigurationStepStatus.Restored : ConfigurationStepStatus.Unchanged, Message = "Estado já corresponde ao solicitado." }); continue; }
                if (needsEmptyKeyCleanup && !current.Exists)
                {
                    system.Write(step.Target, desired);
                    results.Add(step with { Status = ConfigurationStepStatus.Restored, Message = "Valor ausente; chave vazia criada pela operação removida quando possível." });
                    continue;
                }
                if (!Equal(current, expected)) { results.Add(step with { Status = ConfigurationStepStatus.Conflict, Message = "O valor atual foi alterado por outra ação. Ele foi preservado." }); continue; }
                attemptedWrite = true;
                system.Write(step.Target, desired);
                if (!Equal(system.Read(step.Target), desired)) throw new IOException("A verificação após a escrita não confirmou o estado solicitado.");
                results.Add(step with { Status = restoring ? ConfigurationStepStatus.Restored : ConfigurationStepStatus.Applied, ObservedAfter = restoring ? step.ObservedAfter : desired, Message = "Estado relido e confirmado." });
            }
            catch (Exception ex)
            {
                ConfigurationValue? observed = step.ObservedAfter;
                if (attemptedWrite) try { observed = system.Read(step.Target); } catch { }
                results.Add(step with { Status = ConfigurationStepStatus.Failed, ObservedAfter = observed, Message = ex.Message });
            }
        }
        var updated = snapshot with { Steps = results };
        var success = results.Count(x => x.Status is ConfigurationStepStatus.Applied or ConfigurationStepStatus.Restored or ConfigurationStepStatus.Unchanged);
        var failed = results.Count - success;
        string? refreshFailure = null;
        if (success > 0 && OptimizationCatalog.Find(snapshot.DefinitionId) is { Refresh: not ConfigurationRefresh.None } def)
        {
            try { system.Refresh(def.Refresh); } catch (Exception ex) { refreshFailure = ex.Message; }
        }
        var status = failed == 0 && refreshFailure is null ? restoring ? ConfigurationResultStatus.Reverted : results.All(x => x.Status == ConfigurationStepStatus.Unchanged) ? ConfigurationResultStatus.Unchanged : ConfigurationResultStatus.Completed
            : success > 0 ? ConfigurationResultStatus.Partial : results.Any(x => x.Status == ConfigurationStepStatus.Conflict) ? ConfigurationResultStatus.Conflict : ConfigurationResultStatus.Failed;
        string message = $"{success} de {results.Count} alvos confirmados{(restoring ? " na restauração" : "")}.";
        if (failed > 0) message += $" {failed} alvo(s) com falha, conflito ou sem execução; o snapshot foi preservado.";
        if (cancelled) message += " Cancelado; alterações anteriores continuam disponíveis para restauração.";
        if (refreshFailure is not null) message += $" Valores gravados, atualização da sessão pendente: {refreshFailure}";
        return new(status, message, updated);
    }

    private string? Validate(ConfigurationSnapshot snapshot, bool restoring, bool requireAdmin = true)
    {
        if (snapshot.Schema != 1 || snapshot.Steps is null || snapshot.Steps.Count is 0 or > 512) return "Snapshot inválido ou sem alvos.";
        var def = OptimizationCatalog.Find(snapshot.DefinitionId);
        if (def is null) return "Configuração fora do catálogo.";
        if (!snapshot.Enabled && !def.AllowDisable) return "O catálogo não permite esta variante da operação.";
        var env = system.InspectEnvironment();
        var block = BlockReason(def, env);
        if (block is not null) return block;
        var changes = Resolve(def, restoring).ToDictionary(x => x.Target.ResourceId, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in snapshot.Steps)
        {
            if (step is null || step.Target is null || step.Before is null || step.Desired is null || !Enum.IsDefined(step.Status)) return "Snapshot contém um alvo ou estado inválido.";
            if (!seen.Add(step.Target.ResourceId) || !changes.TryGetValue(step.Target.ResourceId, out var permitted)) return "Snapshot contém um alvo duplicado ou fora desta configuração.";
            var desired = snapshot.Enabled ? permitted.On : permitted.Off;
            if (!desired.Exists) desired = desired with { KeyExists = step.Before.KeyExists };
            if (!Equal(step.Desired, desired) || !ValidValue(step.Target, step.Before) || step.ObservedAfter is { } observed && !ValidValue(step.Target, observed)) return "Os valores do snapshot não correspondem ao contrato do catálogo.";
            if (step.ObservedAfter is not null && (step.Status is ConfigurationStepStatus.Pending or ConfigurationStepStatus.Unchanged || step.Status == ConfigurationStepStatus.Applied && !Equal(step.ObservedAfter, step.Desired))) return "Estado observado não corresponde à etapa registrada.";
            if (requireAdmin && RequiresAdmin(step.Target) && !env.IsAdministrator) return "Restauração/aplicação requer administrador para estes alvos.";
        }
        if (!restoring && changes.Count != snapshot.Steps.Count) return "O conjunto de dispositivos mudou depois da prévia.";
        return null;
    }
    private IReadOnlyList<ConfigurationChange> Resolve(ConfigurationDefinition def, bool restoring = false) => def.Changes.Concat(def.Capability is null ? [] : restoring ? system.ResolveDynamicChangesForRestore(def.Capability) : system.ResolveDynamicChanges(def.Capability)).DistinctBy(x => x.Target.ResourceId).ToArray();
    private static string? BlockReason(ConfigurationDefinition def, ConfigurationEnvironment env)
    {
        if (!env.IsSupportedOS) return "Alterações disponíveis somente no Windows 10/11 x64 cliente compatível.";
        if (env.WindowsBuild < def.MinimumBuild) return $"Esta opção exige Windows build {def.MinimumBuild} ou superior.";
        if (def.UnsupportedReason is not null) return def.UnsupportedReason;
        if (env.IsDualCcdX3d && def.Id is "power-high" or "power-ultimate" or "gamebar-off") return "Processador X3D com dois CCDs: mantenha plano Equilibrado, Modo Jogo e Xbox Game Bar para o escalonamento AMD.";
        if (!env.IsDualCcdX3d && def.Id == "x3d-mode") return "Esta combinação se destina a processadores X3D com dois CCDs.";
        return null;
    }
    private static bool RequiresAdmin(ConfigurationTarget target) => target.Kind is not (ConfigurationTargetKind.SteamOverlay or ConfigurationTargetKind.DisplayMode) && (target.Kind != ConfigurationTargetKind.Registry || !target.Path.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase));
    private static bool ValidValue(ConfigurationTarget target, ConfigurationValue value)
    {
        try
        {
            if (value.Json.Length > 1_048_576) return false;
            if (!value.Exists) return target.Kind is ConfigurationTargetKind.Registry or ConfigurationTargetKind.SteamOverlay or ConfigurationTargetKind.NvidiaDrs;
            using var json = System.Text.Json.JsonDocument.Parse(value.Json);
            if (target.Kind == ConfigurationTargetKind.SteamOverlay) return value.Kind == (int)Microsoft.Win32.RegistryValueKind.String && json.RootElement.ValueKind == System.Text.Json.JsonValueKind.String;
            if (target.Kind == ConfigurationTargetKind.NvidiaDrs) return value.Kind == (int)Microsoft.Win32.RegistryValueKind.DWord && json.RootElement.TryGetInt32(out _);
            if (target.Kind != ConfigurationTargetKind.Registry)
            {
                if (value.Kind != 0) return false;
                return target.Kind switch
                {
                    ConfigurationTargetKind.Service => System.Text.Json.JsonSerializer.Deserialize<ConfigurationServiceState>(value.Json) is { StartType: >= 2 and <= 4 },
                    ConfigurationTargetKind.Hibernation => System.Text.Json.JsonSerializer.Deserialize<ConfigurationHibernationState>(value.Json) is { } h && (!h.Enabled && h.FileType is null && h.SizePercent is null || h.Enabled && h.FileType is 1 or 2 && h.SizePercent is >= 0 and <= 100),
                    ConfigurationTargetKind.PowerPlan => json.RootElement.TryGetGuid(out var plan) && plan != Guid.Empty,
                    ConfigurationTargetKind.Pagefile => System.Text.Json.JsonSerializer.Deserialize<ConfigurationPagefileState>(value.Json) is { } p && (p.AutomaticManaged && p.PagingFiles is null || !p.AutomaticManaged && p.PagingFiles is not null && ValidValue(new(ConfigurationTargetKind.Registry, @"HKLM\PagefileValidation", "PagingFiles"), p.PagingFiles)),
                    ConfigurationTargetKind.AppPowerThrottling => json.RootElement.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False,
                    ConfigurationTargetKind.DnsIpv4 => System.Text.Json.JsonSerializer.Deserialize<ConfigurationDnsState>(value.Json) is { Servers: not null } dns && dns.Servers.Count <= 16 && dns.Automatic == (dns.Servers.Count == 0) && dns.Servers.All(x => System.Net.IPAddress.TryParse(x, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork),
                    ConfigurationTargetKind.DisplayMode => System.Text.Json.JsonSerializer.Deserialize<ConfigurationDisplayMode>(value.Json) is { } mode && ConfigurationDisplayCodec.Encode(mode).Length == ConfigurationDisplayCodec.Size,
                    _ => false
                };
            }
            return value.Kind switch
            {
                (int)Microsoft.Win32.RegistryValueKind.String or (int)Microsoft.Win32.RegistryValueKind.ExpandString => json.RootElement.ValueKind == System.Text.Json.JsonValueKind.String,
                (int)Microsoft.Win32.RegistryValueKind.DWord => json.RootElement.TryGetInt32(out _),
                (int)Microsoft.Win32.RegistryValueKind.QWord => json.RootElement.TryGetInt64(out _),
                (int)Microsoft.Win32.RegistryValueKind.Binary or (int)Microsoft.Win32.RegistryValueKind.None => json.RootElement.TryGetBytesFromBase64(out _),
                (int)Microsoft.Win32.RegistryValueKind.MultiString => json.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array && json.RootElement.EnumerateArray().All(x => x.ValueKind == System.Text.Json.JsonValueKind.String),
                _ => false
            };
        }
        catch { return false; }
    }
    // Key creation by an earlier step in the same operation is not a change to another value.
    // KeyExists is retained for conservative cleanup, while resource identity is the individual value.
    public static bool Equal(ConfigurationValue a, ConfigurationValue b) => !a.Exists && !b.Exists || a == b;
}
