using Microsoft.Win32;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VLOptimizer.Core;

/// <summary>Local, reversible operation executor. No shell, command execution, or automatic elevation.</summary>
public sealed partial class WindowsOptimizerBackend : IOptimizerBackend
{
    private const string StartupSource = "https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys";
    private const string PowerSource = "https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme";
    private readonly IWindowsSystemAccess system;
    private readonly IConfigurationEngine configurations;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConditionalWeakTable<OperationPreview, ApprovedState> issued = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string DataDirectory { get; }

    public WindowsOptimizerBackend(string? dataDirectory = null, IWindowsSystemAccess? systemAccess = null, IConfigurationEngine? configurationEngine = null)
    {
        DataDirectory = Path.GetFullPath(dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VLOptimizer"));
        system = systemAccess ?? new WindowsSystemAccess();
        configurations = configurationEngine ?? new ConfigurationEngine();
    }

    public Task<SystemSnapshot> AnalyzeAsync(IProgress<string>? progress, CancellationToken token) => system.AnalyzeAsync(progress, token);

    public async Task<OperationPreview> PreviewAsync(OperationRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var lease = await AcquireAsync(token).ConfigureAwait(false);
        _ = LoadAndRecover();
        token.ThrowIfCancellationRequested();
        if (!system.IsSupportedOS)
            return Blocked(request, "Sistema em modo de diagnóstico", "As alterações desta versão são limitadas ao Windows 10/11 x64 cliente, build 19041 ou superior. O diagnóstico continua disponível.");
        try
        {
            if (request.Kind == OperationKind.Configure)
                return await PreviewConfigurationAsync(request, token).ConfigureAwait(false);
            if (request.Kind == OperationKind.DisableStartup)
            {
                string name = StartupName(request.TargetId);
                var value = system.ReadUserStartup(name);
                if (value is null)
                    return Blocked(request, "Já configurado", "Esta entrada não está presente em HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run.");
                if ((RegistryValueKind)value.Kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
                    return Blocked(request, "Tipo de inicialização não suportado", "Apenas valores de texto da chave Run do usuário podem ser desativados.");
                string command = JsonSerializer.Deserialize<string>(value.SerializedValue) ?? throw new InvalidDataException("Comando de inicialização vazio.");
                if (WindowsSystemAccess.IsProtectedStartup(name, command))
                    return Blocked(request, "Entrada sensível protegida", "O nome ou comando está associado a segurança, acessibilidade ou gestão corporativa. Revise pelo Windows ou com o responsável de TI.");
                var preview = new OperationPreview
                {
                    Request = request, Title = $"Desativar inicialização de {name}",
                    Explanation = "Remove somente esta entrada Run do usuário. O aplicativo continua instalado e processos em execução não são encerrados. O efeito será percebido no próximo logon. A função do programa não foi verificada automaticamente; revise o comando e selecione apenas se reconhecer o aplicativo.",
                    Before = command, After = "Entrada removida da inicialização do usuário", Risk = "Moderado · revisão individual", SourceUrl = StartupSource, CanApply = true,
                    Resources = [new ConfigurationTarget(ConfigurationTargetKind.Registry, @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", name).ResourceId]
                };
                issued.Add(preview, new ApprovedState(new BackupState { StartupName = name, StartupValue = value }, DateTimeOffset.UtcNow));
                return preview;
            }
            if (request.Kind == OperationKind.SetPowerPlan)
            {
                if (!Guid.TryParse(request.TargetId, out var target)) return Blocked(request, "Plano inválido", "Escolha um plano existente na lista obtida do Windows.");
                var plans = system.ReadPowerPlans();
                var selected = plans.FirstOrDefault(x => x.Id == target);
                if (selected is null) return Blocked(request, "Plano indisponível", "O plano não existe mais. Analise novamente.");
                var active = system.ReadActivePowerPlan();
                if (active == target) return Blocked(request, "Já configurado", "Este plano já está ativo.");
                var preview = new OperationPreview
                {
                    Request = request, Title = $"Ativar plano {selected.Name}",
                    Explanation = "Solicita ao Windows a ativação de um plano já existente. O resultado depende das definições do plano e das políticas da máquina. Pode afetar consumo, autonomia, ruído e temperatura; não há promessa de ganho de desempenho. Notebooks na bateria devem priorizar a autonomia.",
                    Before = plans.FirstOrDefault(x => x.Id == active)?.Name ?? active.ToString(), After = selected.Name,
                    Risk = "Moderado · energia e temperatura", SourceUrl = PowerSource, CanApply = true,
                    Resources = [new ConfigurationTarget(ConfigurationTargetKind.PowerPlan, "active").ResourceId]
                };
                issued.Add(preview, new ApprovedState(new BackupState { PowerBefore = active, PowerAfter = target }, DateTimeOffset.UtcNow));
                return preview;
            }
            return Blocked(request, "Operação não disponível", "Esta operação não faz parte do catálogo permitido.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { return Blocked(request, "Não foi possível preparar a operação", ex.Message); }
    }

    public async Task<OperationRecord> ApplyAsync(OperationPreview approvedPreview, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(approvedPreview);
        if (!approvedPreview.CanApply)
            throw new InvalidOperationException("A operação precisa de uma prévia válida emitida por esta sessão. Revise e aprove novamente.");
        using var lease = await AcquireAsync(token).ConfigureAwait(false);
        var journal = LoadAndRecover();
        token.ThrowIfCancellationRequested();
        // Single-use approval, consumed under the same gate as execution. Concurrent calls and
        // Apply → Undo → Apply cannot replay an earlier approval even if its inputs still match.
        if (!issued.TryGetValue(approvedPreview, out var approved) || !issued.Remove(approvedPreview))
            throw new InvalidOperationException("A prévia já foi utilizada ou não pertence a esta sessão. Revise e aprove uma nova prévia.");
        var record = new OperationRecord
        {
            Request = approvedPreview.Request, Title = approvedPreview.Title, Status = OperationStatus.Pending,
            Before = approvedPreview.Before, After = approvedPreview.After,
            BackupJson = JsonSerializer.Serialize(approved.Backup, JsonOptions), CanUndo = true,
            Message = "Prévia aprovada. Estado anterior salvo; aguardando alteração."
        };
        if (!system.IsSupportedOS)
            return AppendFinal(journal, record, OperationStatus.Skipped, "Sistema não suportado para alterações. Nenhuma alteração foi solicitada.", false);
        if (DateTimeOffset.UtcNow - approved.CreatedAt > TimeSpan.FromMinutes(15))
            return AppendFinal(journal, record, OperationStatus.Skipped, "A prévia expirou. Gere uma nova antes de aplicar.", false);

        if (record.Request.Kind == OperationKind.Configure)
            return await ApplyConfigurationAsync(journal, record, approved, token).ConfigureAwait(false);

        var backup = approved.Backup;
        bool unchanged;
        bool alreadyApplied;
        try
        {
            if (record.Request.Kind == OperationKind.SetPowerPlan && !system.ReadPowerPlans().Any(x => x.Id == backup.PowerAfter))
                return AppendFinal(journal, record, OperationStatus.Skipped, "O plano selecionado foi removido após a prévia. Nenhuma alteração foi realizada.", false);
            (unchanged, alreadyApplied) = StateForApply(record.Request, backup);
            if (alreadyApplied)
                return AppendFinal(journal, record, OperationStatus.Skipped, "Já configurado. Nenhuma alteração foi realizada por esta ação.", false);
            if (!unchanged)
                return AppendFinal(journal, record, OperationStatus.Conflict, "O estado mudou após a prévia. Analise e aprove novamente; nenhum valor foi sobrescrito.", false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { return AppendFinal(journal, record, OperationStatus.Failed, "A pré-condição não pôde ser verificada: " + ex.Message, false); }

        // Durably persist Pending BEFORE invoking any Windows mutation. Persistence failure aborts here.
        journal.Records.Add(record);
        Save(journal);
        if (token.IsCancellationRequested)
        {
            UpdateFinal(journal, record, OperationStatus.Skipped, "Cancelado antes da alteração. Nenhuma configuração foi modificada.", false);
            token.ThrowIfCancellationRequested();
        }
        try
        {
            // Recheck immediately before touching the system, including after writing the journal.
            (unchanged, alreadyApplied) = StateForApply(record.Request, backup);
            if (!unchanged)
                return UpdateFinal(journal, record, alreadyApplied ? OperationStatus.Skipped : OperationStatus.Conflict,
                    "O estado mudou antes da execução. Nenhuma alteração foi realizada por esta ação.", false);
            if (record.Request.Kind == OperationKind.SetPowerPlan && !system.ReadPowerPlans().Any(x => x.Id == backup.PowerAfter))
                return UpdateFinal(journal, record, OperationStatus.Skipped, "O plano selecionado não está mais disponível. Nenhuma alteração foi realizada.", false);
            if (record.Request.Kind == OperationKind.DisableStartup) system.DeleteUserStartup(backup.StartupName!);
            else system.SetActivePowerPlan(backup.PowerAfter!.Value);
            // Cancellation is deliberately deferred through verification and durable final journaling.
            (_, alreadyApplied) = StateForApply(record.Request, backup);
            return UpdateFinal(journal, record, alreadyApplied ? OperationStatus.Completed : OperationStatus.Unverified,
                alreadyApplied ? "Estado final confirmado por nova leitura. A reversão do estado anterior está disponível." : "A solicitação terminou, mas o estado final não corresponde ao esperado. Revise o histórico antes de continuar.", true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            bool before = false, after = false;
            try { (before, after) = StateForApply(record.Request, backup); } catch { }
            return UpdateFinal(journal, record, before ? OperationStatus.Failed : OperationStatus.Unverified,
                (after ? "O estado solicitado foi observado, mas a operação relatou erro. " : "A operação não pôde ser concluída com verificação integral. ") + ex.Message,
                !before);
        }
    }

    public async Task<OperationRecord> UndoAsync(Guid recordId, CancellationToken token)
    {
        using var lease = await AcquireAsync(token).ConfigureAwait(false);
        var journal = LoadAndRecover();
        token.ThrowIfCancellationRequested();
        var record = journal.Records.FirstOrDefault(x => x.Id == recordId) ?? throw new KeyNotFoundException("Ação não encontrada no histórico local.");
        if (!record.CanUndo) return record;
        if (!system.IsSupportedOS)
            return UpdateFinal(journal, record, OperationStatus.Conflict, "Restauração bloqueada neste sistema. Apenas Windows 10/11 x64 cliente compatível é suportado para alterações.", true);
        if (record.Request.Kind == OperationKind.Configure)
            return await UndoConfigurationAsync(journal, record, token).ConfigureAwait(false);
        if (record.Request.Kind == OperationKind.SetPowerPlan && journal.Records.SkipWhile(x => x.Id != record.Id).Skip(1)
            .Any(x => x.CanUndo && x.Request.Kind == OperationKind.Configure && ReadBackup(x).Configuration?.Steps.Any(s => s.Target.Kind == ConfigurationTargetKind.PowerPlan) == true))
            return UpdateFinal(journal, record, OperationStatus.Conflict, "Uma configuração posterior também altera o plano de energia. Restaure essa configuração primeiro.", true);
        var backup = ReadBackup(record);
        var (before, after) = StateForApply(record.Request, backup);
        if (before) return UpdateFinal(journal, record, OperationStatus.Reverted, "O estado original já foi encontrado. Nenhuma escrita foi necessária.", false);
        if (!after)
            return UpdateFinal(journal, record, OperationStatus.Conflict, "Uma configuração posterior foi detectada. A restauração não sobrescreveu esse estado. Revise manualmente pelo Windows.", true);
        if (record.Request.Kind == OperationKind.SetPowerPlan && !system.ReadPowerPlans().Any(x => x.Id == backup.PowerBefore))
            return UpdateFinal(journal, record, OperationStatus.Conflict, "O plano original foi removido do Windows. Nenhum plano foi criado e nenhuma alteração foi aplicada.", true);

        var pending = record with { Status = OperationStatus.Pending, FinishedAt = null, Message = "Restauração iniciada; estado anterior preservado no backup." };
        Replace(journal, pending);
        Save(journal);
        if (token.IsCancellationRequested)
        {
            Replace(journal, record with { Message = "Restauração cancelada antes de modificar a configuração. O backup continua disponível." });
            Save(journal);
            token.ThrowIfCancellationRequested();
        }
        try
        {
            (before, after) = StateForApply(record.Request, backup);
            if (before) return UpdateFinal(journal, pending, OperationStatus.Reverted, "O estado original já foi restaurado por outro componente.", false);
            if (!after) return UpdateFinal(journal, pending, OperationStatus.Conflict, "A configuração mudou durante a preparação. Nada foi sobrescrito.", true);
            if (record.Request.Kind == OperationKind.SetPowerPlan && !system.ReadPowerPlans().Any(x => x.Id == backup.PowerBefore))
                return UpdateFinal(journal, pending, OperationStatus.Conflict, "O plano original foi removido durante a preparação. Nenhuma alteração foi realizada.", true);
            if (record.Request.Kind == OperationKind.DisableStartup) system.WriteUserStartup(backup.StartupName!, backup.StartupValue!);
            else system.SetActivePowerPlan(backup.PowerBefore!.Value);
            (before, _) = StateForApply(record.Request, backup);
            return UpdateFinal(journal, pending, before ? OperationStatus.Reverted : OperationStatus.Unverified,
                before ? "Estado original restaurado e confirmado por nova leitura." : "A restauração foi solicitada, mas não pôde ser confirmada. O backup foi preservado.", !before);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try { (before, _) = StateForApply(record.Request, backup); } catch { before = false; }
            return UpdateFinal(journal, pending, OperationStatus.Unverified,
                "A restauração relatou erro. " + (before ? "O estado original foi observado. " : "O estado final exige revisão. ") + ex.Message, !before);
        }
    }

    public async Task<IReadOnlyList<OperationRecord>> GetHistoryAsync()
    {
        using var lease = await AcquireAsync(CancellationToken.None).ConfigureAwait(false);
        return LoadAndRecover().Records.OrderByDescending(x => x.StartedAt).ToList();
    }

    public IReadOnlyList<Recommendation> GetRecommendations(SystemSnapshot snapshot)
    {
        var recommendations = new List<Recommendation>();
        if (!snapshot.SupportedOS)
            recommendations.Add(new("compatibility", "Diagnóstico", "Sistema em modo de diagnóstico", "As alterações requerem Windows 10/11 x64 cliente e build 19041 ou superior. Cada recurso possui validações adicionais de versão, dispositivo e privilégio.", "Alta", "https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information", "Visão geral"));
        if (snapshot.RebootPending)
            recommendations.Add(new("reboot", "Diagnóstico", "O Windows sinalizou uma reinicialização pendente", "Salve seu trabalho e consulte o Windows Update. O sinal vem de chaves de manutenção do Windows; uma reinicialização pode ser necessária para concluir instalações. O aplicativo não reinicia o computador.", "Alta", "https://support.microsoft.com/windows/windows-update", "Diagnóstico"));
        int manageable = snapshot.StartupItems.Count(x => x.CanDisable);
        if (manageable > 0)
            recommendations.Add(new("startup", "Desempenho potencial", "Revise os aplicativos que iniciam com você", $"Foram encontradas {manageable} entradas Run do usuário que podem ser revisadas individualmente. Desative apenas aplicativos conhecidos que não precisa ao entrar. O efeito precisa de nova medição após outro logon; quantidade de entradas não indica ganho de desempenho.", "Revisar", StartupSource, "Inicialização"));
        if (snapshot.TotalMemoryBytes > 0 && snapshot.AvailableMemoryBytes < snapshot.TotalMemoryBytes / 10)
            recommendations.Add(new("memory-pressure", "Diagnóstico", "Pouca memória disponível durante esta análise", "A memória disponível ficou abaixo de 10% no momento da coleta. Isso é um sinal para investigar os aplicativos em uso, não uma confirmação de gargalo. Salve o trabalho e revise os maiores consumidores; o aplicativo não limpa RAM nem encerra processos.", "Revisar", "https://learn.microsoft.com/en-us/windows/win32/memory/memory-performance-information", "Diagnóstico"));
        if (snapshot.AverageCpuPercent is >= 80)
            recommendations.Add(new("cpu-observation", "Diagnóstico", "CPU ocupada durante a observação", $"A média desta janela de {snapshot.ObservationSeconds:0.0} segundos ficou acima de 80%. Verifique se havia uma tarefa esperada em andamento e repita em contexto equivalente. Uma janela curta não confirma gargalo permanente.", "Revisar", "https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes", "Diagnóstico"));
        foreach (var disk in snapshot.Disks.Where(x => x.TotalBytes > 0 && x.FreeBytes < x.TotalBytes / 10))
            recommendations.Add(new("disk-" + disk.Name, "Armazenamento", $"Revise o espaço disponível em {disk.Name}", "Menos de 10% do volume está disponível. Consulte as categorias de armazenamento do Windows antes de excluir arquivos. A limpeza automática foi adiada até que os limites de exclusão sejam validados; nenhum arquivo será removido nesta versão.", "Revisar", "https://support.microsoft.com/windows/free-up-drive-space-in-windows", "Armazenamento"));
        if (snapshot.PowerPlans.Count > 1)
            recommendations.Add(new("power", "Energia", snapshot.OnBattery == true ? "Priorize autonomia enquanto estiver na bateria" : "Conheça o plano de energia atual",
                snapshot.OnBattery == true ? "O equipamento está na bateria. Planos de maior consumo podem reduzir autonomia e aumentar a temperatura. Escolha somente um plano existente e revise o efeito para seu uso." : "O plano ativo já foi identificado. Trocar de plano é uma escolha de uso; não há recomendação universal de Desempenho Máximo nem ganho comprovado nesta análise.", "Opcional", PowerSource, "Energia"));
        if (recommendations.Count == 0)
            recommendations.Add(new("no-action", "Diagnóstico", "Nenhuma ação prioritária nesta observação", "Os sinais coletados não indicaram uma ação prioritária pelo catálogo atual. Isso não é um benchmark completo: uso de GPU, temperaturas e comportamento em jogos exigem instrumentação própria.", "Informativo", "https://learn.microsoft.com/en-us/windows-hardware/test/wpt/", "Diagnóstico"));
        return recommendations;
    }

    private (bool Before, bool After) StateForApply(OperationRequest request, BackupState backup)
    {
        if (request.Kind == OperationKind.DisableStartup)
        {
            if (backup.StartupName is null || backup.StartupValue is null || StartupName(request.TargetId) != backup.StartupName)
                throw new InvalidDataException("Backup de inicialização inconsistente.");
            var current = system.ReadUserStartup(backup.StartupName);
            return (current == backup.StartupValue, current is null);
        }
        if (request.Kind == OperationKind.SetPowerPlan)
        {
            if (backup.PowerBefore is null || backup.PowerAfter is null || !Guid.TryParse(request.TargetId, out var target) || target != backup.PowerAfter)
                throw new InvalidDataException("Backup de energia inconsistente.");
            var current = system.ReadActivePowerPlan();
            return (current == backup.PowerBefore, current == backup.PowerAfter);
        }
        throw new InvalidDataException("Operação fora do catálogo permitido.");
    }

    private static string StartupName(string id)
    {
        const string prefix = "hkcu-run:";
        if (id is null || !id.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException("Somente a chave Run do usuário atual pode ser alterada.");
        string name = Uri.UnescapeDataString(id[prefix.Length..]);
        WindowsSystemAccess.ValidateName(name);
        return name;
    }
    private static OperationPreview Blocked(OperationRequest request, string title, string reason) =>
        new() { Request = request, Title = title, Explanation = reason, BlockReason = reason, CanApply = false };
    private static BackupState ReadBackup(OperationRecord record) =>
        JsonSerializer.Deserialize<BackupState>(record.BackupJson ?? throw new InvalidDataException("Backup ausente."), JsonOptions) ?? throw new InvalidDataException("Backup inválido.");

    private Journal LoadAndRecover()
    {
        string path = Path.Combine(DataDirectory, "history.json");
        Journal journal;
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Histórico excede o limite de leitura. Preserve o arquivo antes de manutenção manual.");
            string contents = File.ReadAllText(path);
            using var document = JsonDocument.Parse(contents);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("Version", out var version) || !version.TryGetInt32(out int schema) || schema != 1 ||
                !document.RootElement.TryGetProperty("Records", out var records) || records.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Estrutura do histórico local inválida; nenhuma operação foi executada.");
            journal = JsonSerializer.Deserialize<Journal>(contents, JsonOptions) ?? throw new InvalidDataException("Histórico local inválido; nenhuma operação foi executada.");
            if (journal.Version != 1 || journal.Records is null ||
                journal.Records.Any(x => x is null || x.Id == Guid.Empty || x.Request is null || !Enum.IsDefined(x.Status) ||
                    !Enum.IsDefined(x.Request.Kind) || string.IsNullOrWhiteSpace(x.Request.TargetId) || (x.CanUndo && string.IsNullOrWhiteSpace(x.BackupJson))) ||
                journal.Records.Select(x => x.Id).Distinct().Count() != journal.Records.Count)
                throw new InvalidDataException("Formato de histórico não suportado ou inconsistente. Nenhuma operação foi executada.");
        }
        else journal = new();
        bool recovered = false;
        for (int index = 0; index < journal.Records.Count; index++)
        {
            if (journal.Records[index].Status != OperationStatus.Pending) continue;
            journal.Records[index] = journal.Records[index] with
            {
                Status = OperationStatus.Unverified, FinishedAt = DateTimeOffset.Now,
                Message = "A sessão anterior terminou com uma operação pendente. O resultado não foi presumido. O backup foi preservado e a restauração verificará possíveis conflitos."
            };
            recovered = true;
        }
        if (recovered) Save(journal);
        return journal;
    }

    private void Save(Journal journal)
    {
        string destination = Path.Combine(DataDirectory, "history.json");
        string temporary = Path.Combine(DataDirectory, ".history-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, journal, JsonOptions);
                stream.Flush(true);
            }
            if (File.Exists(destination)) File.Replace(temporary, destination, Path.Combine(DataDirectory, "history.previous.json"), true);
            else File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private OperationRecord AppendFinal(Journal journal, OperationRecord record, OperationStatus status, string message, bool canUndo)
    {
        var final = record with { Status = status, Message = message, CanUndo = canUndo, FinishedAt = DateTimeOffset.Now };
        journal.Records.Add(final);
        Save(journal);
        return final;
    }
    private OperationRecord UpdateFinal(Journal journal, OperationRecord record, OperationStatus status, string message, bool canUndo)
    {
        var final = record with { Status = status, Message = message, CanUndo = canUndo, FinishedAt = DateTimeOffset.Now };
        Replace(journal, final);
        Save(journal);
        return final;
    }
    private static void Replace(Journal journal, OperationRecord record)
    {
        int index = journal.Records.FindIndex(x => x.Id == record.Id);
        if (index < 0) throw new InvalidDataException("Registro de operação ausente.");
        journal.Records[index] = record;
    }
    private async Task<Lease> AcquireAsync(CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var started = DateTimeOffset.UtcNow;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var file = new FileStream(Path.Combine(DataDirectory, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    return new Lease(file, gate);
                }
                catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
                {
                    if (DateTimeOffset.UtcNow - started > TimeSpan.FromSeconds(10)) throw new IOException("Outra instância está executando uma operação. Aguarde e tente novamente.", ex);
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
            }
        }
        catch { gate.Release(); throw; }
    }
    private sealed class Lease(FileStream file, SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() { file.Dispose(); semaphore.Release(); }
    }
    private sealed record ApprovedState(BackupState Backup, DateTimeOffset CreatedAt, ConfigurationPreview? Configuration = null);
    private sealed class BackupState
    {
        public string? StartupName { get; set; }
        public StartupValueState? StartupValue { get; set; }
        public Guid? PowerBefore { get; set; }
        public Guid? PowerAfter { get; set; }
        public ConfigurationSnapshot? Configuration { get; set; }
    }
    private sealed class Journal
    {
        public int Version { get; set; } = 1;
        public List<OperationRecord> Records { get; set; } = [];
    }
}
