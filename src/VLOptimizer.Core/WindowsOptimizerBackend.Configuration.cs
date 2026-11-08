using System.Text.Json;

namespace VLOptimizer.Core;

public sealed partial class WindowsOptimizerBackend
{
    private async Task<OperationPreview> PreviewConfigurationAsync(OperationRequest request, CancellationToken token)
    {
        if (request.Enable is null) return Blocked(request, "Escolha uma opção", "Indique ativar ou desativar esta configuração.");
        var configuration = await configurations.PreviewAsync(request.TargetId, request.Enable.Value, token).ConfigureAwait(false);
        var preview = new OperationPreview
        {
            Request = request, Title = (request.Enable.Value ? "Ativar: " : "Desativar: ") + configuration.Definition.Title,
            Explanation = configuration.Definition.Description + " " + string.Join(" ", configuration.Warnings),
            Before = Describe(configuration.Steps, false), After = Describe(configuration.Steps, true),
            Risk = configuration.Steps.Any(x => x.RequiresAdmin) ? "Avançado · exige administrador" : "Moderado · preferência do usuário",
            SourceUrl = configuration.Definition.SourceUrl, CanApply = configuration.CanApply, BlockReason = configuration.BlockReason,
            Resources = configuration.Steps.Select(x => x.Target.ResourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
        if (preview.CanApply)
            issued.Add(preview, new ApprovedState(new BackupState { Configuration = configuration.Snapshot }, DateTimeOffset.UtcNow, configuration));
        return preview;
    }

    private async Task<OperationRecord> ApplyConfigurationAsync(Journal journal, OperationRecord record, ApprovedState approved, CancellationToken token)
    {
        if (approved.Configuration is null || approved.Backup.Configuration is not { } snapshot ||
            snapshot.DefinitionId != record.Request.TargetId || snapshot.Enabled != record.Request.Enable)
            return AppendFinal(journal, record, OperationStatus.Failed, "Prévia de configuração inconsistente; nada foi aplicado.", false);
        var states = await configurations.CheckSnapshotAsync(snapshot, token).ConfigureAwait(false);
        if (states.Count == 0 || states.Any(x => x.Current is null))
            return AppendFinal(journal, record, OperationStatus.Failed, "Um alvo da configuração não pôde ser relido. Gere uma nova prévia.", false);
        if (states.All(x => ConfigurationEngine.Equal(x.Current!, x.Step.Desired)))
            return AppendFinal(journal, record, OperationStatus.Skipped, "Já configurado. Nenhuma alteração foi iniciada.", false);
        if (states.Any(x => !ConfigurationEngine.Equal(x.Current!, x.Step.Before)))
            return AppendFinal(journal, record, OperationStatus.Conflict, "O estado mudou após a prévia. Nenhum valor foi sobrescrito.", false);

        journal.Records.Add(record);
        Save(journal); // The complete typed BEFORE snapshot is durable before the first write.
        try
        {
            var result = await configurations.ApplyAsync(approved.Configuration, token).ConfigureAwait(false);
            var updated = record with { BackupJson = JsonSerializer.Serialize(new BackupState { Configuration = result.Snapshot }, JsonOptions) };
            var status = Map(result.Status);
            if (status == OperationStatus.Completed && !string.IsNullOrEmpty(approved.Configuration.Definition.Restart)) status = OperationStatus.RestartRequired;
            return UpdateFinal(journal, updated, status, result.Message,
                result.Status is not (ConfigurationResultStatus.Blocked or ConfigurationResultStatus.Conflict or ConfigurationResultStatus.Unchanged) && result.CanRestore);
        }
        catch (Exception ex)
        {
            // Even cancellation or failure after a write retains the pre-write snapshot.
            return UpdateFinal(journal, record, OperationStatus.Unverified,
                "A execução foi interrompida; o snapshot anterior foi preservado. " + ex.Message, true);
        }
    }

    private async Task<OperationRecord> UndoConfigurationAsync(Journal journal, OperationRecord record, CancellationToken token)
    {
        var backup = ReadBackup(record);
        var snapshot = backup.Configuration ?? throw new InvalidDataException("Snapshot tipado da configuração ausente.");
        if (snapshot.DefinitionId != record.Request.TargetId || snapshot.Enabled != record.Request.Enable)
            throw new InvalidDataException("O snapshot não corresponde à operação registrada.");
        // Preserve intent of subsequent compound actions, even when both wrote the same value.
        int index = journal.Records.FindIndex(x => x.Id == record.Id);
        var resources = snapshot.Steps.Where(x => !ConfigurationEngine.Equal(x.Before, x.Desired)).Select(x => x.Target.ResourceId).ToHashSet(StringComparer.Ordinal);
        if (snapshot.Steps.Any(x => x.Target.Kind == ConfigurationTargetKind.PowerPlan) && journal.Records.Skip(index + 1).Any(x => x.CanUndo && x.Request.Kind == OperationKind.SetPowerPlan))
            return UpdateFinal(journal, record, OperationStatus.Conflict, "Uma alteração posterior de energia compartilha este alvo. Restaure o plano posterior primeiro.", true);
        foreach (var later in journal.Records.Skip(index + 1).Where(x => x.CanUndo && x.Request.Kind == OperationKind.Configure))
        {
            var laterSnapshot = ReadBackup(later).Configuration;
            if (laterSnapshot?.Steps.Any(x => resources.Contains(x.Target.ResourceId)) == true)
                return UpdateFinal(journal, record, OperationStatus.Conflict,
                    $"A ação posterior ‘{later.Title}’ também usa estes alvos. Restaure essa ação primeiro para preservar sua configuração.", true);
        }
        var pending = record with { Status = OperationStatus.Pending, FinishedAt = null, Message = "Restauração dos alvos em andamento; snapshot preservado." };
        Replace(journal, pending); Save(journal);
        try
        {
            var result = await configurations.RestoreAsync(snapshot, token).ConfigureAwait(false);
            // Keep original before/desired values and updated per-step outcomes for a retry.
            var updated = pending with { BackupJson = JsonSerializer.Serialize(new BackupState { Configuration = result.Snapshot }, JsonOptions) };
            bool restored = result.Status is ConfigurationResultStatus.Reverted or ConfigurationResultStatus.Unchanged;
            return UpdateFinal(journal, updated, restored ? OperationStatus.Reverted : Map(result.Status), result.Message, !restored);
        }
        catch (Exception ex)
        { return UpdateFinal(journal, pending, OperationStatus.Unverified, "Restauração interrompida; backup preservado. " + ex.Message, true); }
    }

    public async Task<OperationRecord> RecordActivityAsync(string id, string title, string message, OperationStatus status, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 160 || title.Length > 300 || message.Length > 40_000 || !Enum.IsDefined(status) || status == OperationStatus.Pending)
            throw new ArgumentException("Registro de atividade inválido.");
        using var lease = await AcquireAsync(token).ConfigureAwait(false);
        var journal = LoadAndRecover();
        var record = new OperationRecord { Request = new(OperationKind.Maintenance, id), Title = title, Status = status,
            Before = "Conforme prévia específica da ferramenta", After = "Consulte o resultado da atividade", CanUndo = false };
        return AppendFinal(journal, record, status, message, false);
    }

    private static string Describe(IReadOnlyList<ConfigurationStep> steps, bool desired) => string.Join(Environment.NewLine,
        steps.Select(x => $"{x.Target}: {(desired ? x.Desired : x.Before).Display}"));

    private static OperationStatus Map(ConfigurationResultStatus status) => status switch
    {
        ConfigurationResultStatus.Completed => OperationStatus.Completed,
        ConfigurationResultStatus.Reverted => OperationStatus.Reverted,
        ConfigurationResultStatus.Partial => OperationStatus.PartiallyCompleted,
        ConfigurationResultStatus.Conflict => OperationStatus.Conflict,
        ConfigurationResultStatus.Unchanged or ConfigurationResultStatus.Blocked => OperationStatus.Skipped,
        _ => OperationStatus.Failed
    };
}
