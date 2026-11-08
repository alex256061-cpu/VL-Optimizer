using VLOptimizer.Core;

public static class MaintenanceTests
{
    public static void Register(Action<string, Func<Task>> test)
    {
        test("Recuperação: Defender e Proteção do Sistema possuem etapas fechadas", async () =>
        {
            foreach (var (kind, expected) in new[] { (MaintenanceKind.EnableDefender, MaintenanceStageKind.EnableDefender), (MaintenanceKind.EnableSystemRestore, MaintenanceStageKind.EnableSystemRestore) })
            {
                var fake = new FakeMaintenance(); var service = new MaintenanceService(fake); var preview = await service.PreviewAsync(kind);
                if (preview.Stages.Count != 1 || preview.Stages[0] != expected || fake.Executed.Count != 0) throw new InvalidOperationException("Plano ou prévia inválidos.");
                await service.ExecuteAsync(preview); if (fake.Executed.Count != 1 || fake.Executed[0] != expected) throw new InvalidOperationException("Etapa inesperada.");
            }
        });
        test("Recuperação: reativação exige administrador antes de executar", async () =>
        {
            var fake = new FakeMaintenance { Admin = false }; var service = new MaintenanceService(fake);
            if ((await service.PreviewAsync(MaintenanceKind.EnableDefender)).CanApply || (await service.PreviewAsync(MaintenanceKind.EnableSystemRestore)).CanApply || fake.Executed.Count != 0) throw new InvalidOperationException("Privilégio ignorado.");
        });
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        test("Manutenção prévia apenas consulta ambiente", async () => { var fake = new FakeMaintenance(); var service = new MaintenanceService(fake); var preview = await service.PreviewAsync(MaintenanceKind.ResetNetwork); Check(preview.CanApply && fake.Executed.Count == 0 && preview.Stages.Count == 2, "Prévia executou comandos"); });
        test("Manutenção bloqueia privilégio insuficiente", async () => { var fake = new FakeMaintenance { Admin = false }; var service = new MaintenanceService(fake); var preview = await service.PreviewAsync(MaintenanceKind.RepairWindows); Check(!preview.CanApply && (await service.ExecuteAsync(preview)).Status == OperationStatus.Conflict && fake.Executed.Count == 0, "Privilégio ignorado"); });
        test("Manutenção bloqueia plataforma incompatível", async () => { var service = new MaintenanceService(new FakeMaintenance { Supported = false }); Check(!(await service.PreviewAsync(MaintenanceKind.FlushDns)).CanApply, "Plataforma ignorada"); });
        test("Manutenção recusa enum fora do catálogo", async () => { try { await new MaintenanceService(new FakeMaintenance()).PreviewAsync((MaintenanceKind)99); throw new InvalidOperationException("Enum desconhecido aceito"); } catch (ArgumentOutOfRangeException) { } });
        test("Manutenção rejeita cópia e prévia de outra instância", async () => { var fake = new FakeMaintenance(); var service = new MaintenanceService(fake); var preview = await service.PreviewAsync(MaintenanceKind.FlushDns); Check((await service.ExecuteAsync(preview with { })).Status == OperationStatus.Conflict && (await new MaintenanceService(fake).ExecuteAsync(preview)).Status == OperationStatus.Conflict && fake.Executed.Count == 0, "Prévia fabricada autorizou comando"); });
        test("Manutenção consome aprovação apenas uma vez", async () => { var fake = new FakeMaintenance(); var service = new MaintenanceService(fake); var preview = await service.PreviewAsync(MaintenanceKind.FlushDns); Check((await service.ExecuteAsync(preview)).Status == OperationStatus.Completed && (await service.ExecuteAsync(preview)).Status == OperationStatus.Conflict && fake.Executed.Count == 1, "Replay permitiu comando"); });
        test("Manutenção expira aprovação após dez minutos", async () => { var now = DateTimeOffset.UtcNow; var fake = new FakeMaintenance(); var service = new MaintenanceService(fake, () => now); var preview = await service.PreviewAsync(MaintenanceKind.FlushDns); now = now.AddMinutes(11); Check((await service.ExecuteAsync(preview)).Status == OperationStatus.Conflict && fake.Executed.Count == 0, "Prévia vencida executou"); });
        test("Manutenção revalida privilégios no início", async () => { var fake = new FakeMaintenance(); var service = new MaintenanceService(fake); var preview = await service.PreviewAsync(MaintenanceKind.ResetNetwork); fake.Admin = false; Check((await service.ExecuteAsync(preview)).Status == OperationStatus.Failed && fake.Executed.Count == 0, "Privilégio antigo foi reutilizado"); });
        test("Manutenção reset exige reinício e respeita ordem", async () => { var fake = new FakeMaintenance(); var service = new MaintenanceService(fake); var result = await service.ExecuteAsync(await service.PreviewAsync(MaintenanceKind.ResetNetwork)); Check(result.Status == OperationStatus.RestartRequired && fake.Executed.SequenceEqual(new[] { MaintenanceStageKind.ResetWinsock, MaintenanceStageKind.ResetIp }), "Reset fora de ordem"); });
        test("Manutenção não chama SFC se DISM falhou", async () => { var fake = new FakeMaintenance { Failure = MaintenanceStageKind.DismRepair }; var service = new MaintenanceService(fake); var result = await service.ExecuteAsync(await service.PreviewAsync(MaintenanceKind.RepairWindows)); Check(result.Status == OperationStatus.Failed && fake.Executed.Count == 1, "Etapa posterior executada após falha"); });
        test("Manutenção preserva falha parcial por etapa", async () => { var fake = new FakeMaintenance { Failure = MaintenanceStageKind.ResetIp }; var service = new MaintenanceService(fake); var result = await service.ExecuteAsync(await service.PreviewAsync(MaintenanceKind.ResetNetwork)); Check(result.Status == OperationStatus.PartiallyCompleted && result.Stages.Count == 2 && result.Stages[1].ExitCode == 5, "Falha parcial escondida"); });
        test("Manutenção não conclui saúde somente por exitcode SFC", async () => { var fake = new FakeMaintenance { Verified = false }; var service = new MaintenanceService(fake); var result = await service.ExecuteAsync(await service.PreviewAsync(MaintenanceKind.RepairWindows)); Check(result.Status == OperationStatus.Unverified && result.Stages.Count == 2, "Código 0 foi tratado como saúde comprovada"); });
        test("Manutenção timeout não anuncia sucesso", async () => { var fake = new FakeMaintenance { Timeout = true }; var service = new MaintenanceService(fake); var result = await service.ExecuteAsync(await service.PreviewAsync(MaintenanceKind.RepairWindows)); Check(result.Status == OperationStatus.Unverified && fake.Executed.Count == 1 && result.Stages[0].TimedOut, "Timeout não foi preservado"); });
        test("Manutenção respeita cancelamento antes de iniciar", async () => { var fake = new FakeMaintenance(); var service = new MaintenanceService(fake); var preview = await service.PreviewAsync(MaintenanceKind.FlushDns); using var cancel = new CancellationTokenSource(); cancel.Cancel(); try { await service.ExecuteAsync(preview, token: cancel.Token); throw new InvalidOperationException("Cancelamento ignorado"); } catch (OperationCanceledException) { } Check(fake.Executed.Count == 0, "Cancelamento executou comando"); });
        test("Manutenção cancela entre etapas com resultado parcial", async () => { using var cancel = new CancellationTokenSource(); var fake = new FakeMaintenance { AfterStep = () => cancel.Cancel() }; var service = new MaintenanceService(fake); var preview = await service.PreviewAsync(MaintenanceKind.ResetNetwork); var result = await service.ExecuteAsync(preview, token: cancel.Token); Check(result.Status == OperationStatus.PartiallyCompleted && fake.Executed.Count == 1, "Cancelamento entre etapas perdido"); });
        test("Manutenção exceção da ferramenta vira resultado sem etapa seguinte", async () => { var fake = new FakeMaintenance { Throws = true }; var service = new MaintenanceService(fake); var result = await service.ExecuteAsync(await service.PreviewAsync(MaintenanceKind.RepairWindows)); Check(result.Status == OperationStatus.Failed && result.Stages[0].ExitCode == null && fake.Executed.Count == 1, "Exceção anunciou sucesso"); });
    }
    private sealed class FakeMaintenance : IMaintenanceSystemAccess
    {
        public bool Supported = true, Admin = true, Verified = true, Timeout, Throws;
        public MaintenanceStageKind? Failure;
        public Action? AfterStep;
        public List<MaintenanceStageKind> Executed = [];
        public MaintenanceEnvironment InspectEnvironment() => new(Supported, Admin);
        public Task<MaintenanceStage> ExecuteStageAsync(MaintenanceStageKind stage, IProgress<string>? progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Executed.Add(stage); if (Throws) throw new IOException("Falha isolada de fixture"); AfterStep?.Invoke();
            return Task.FromResult(new MaintenanceStage(stage, Failure == stage ? 5 : 0, "Fixture · sem execução de processo Windows", Verified, Timeout));
        }
    }
}
