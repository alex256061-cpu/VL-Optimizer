using System.Text.Json;
using VLOptimizer.Core;

var tests = new List<(string Name, Func<Task> Run)>();
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/private/tests", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
Directory.CreateDirectory(root);
var index = 0;
FakeSystem NewSystem() => new();
(WindowsOptimizerBackend Backend, FakeSystem System, string Path) NewCase(FakeSystem? system = null)
{
    var path = Path.Combine(root, (++index).ToString("D3"));
    var fake = system ?? NewSystem();
    return (new WindowsOptimizerBackend(path, fake), fake, path);
}
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
void Test(string name, Func<Task> run) => tests.Add((name, run));
async Task<OperationRecord> Disable(WindowsOptimizerBackend backend) => await backend.ApplyAsync(await backend.PreviewAsync(new(OperationKind.DisableStartup, FakeSystem.StartupId), default), default);
async Task<OperationRecord> SetPower(WindowsOptimizerBackend backend) => await backend.ApplyAsync(await backend.PreviewAsync(new(OperationKind.SetPowerPlan, FakeSystem.Fast.ToString()), default), default);
bool Denied(OperationRecord r) => r.Status is not (OperationStatus.Completed or OperationStatus.RestartRequired);

Test("A análise apenas lê e preserva startup e energia", async () => {
    var (b, f, _) = NewCase(); var snap = await b.AnalyzeAsync(null, default);
    Check(snap.SupportedOS && f.Writes == 0 && f.Values.ContainsKey("Discord") && f.Active == FakeSystem.Balanced, "A análise alterou estado");
});
Test("A prévia não modifica configurações", async () => {
    var (b, f, _) = NewCase(); var p = await b.PreviewAsync(new(OperationKind.DisableStartup, FakeSystem.StartupId), default);
    Check(p.CanApply && f.Writes == 0, "Prévia deve ser apenas leitura");
});
Test("Desativação salva estado e verifica ausência", async () => {
    var (b, f, _) = NewCase(); var r = await Disable(b);
    Check(r.Status == OperationStatus.Completed && r.CanUndo && !f.Values.ContainsKey("Discord"), $"Resultado inesperado: {r.Status} {r.Message}");
    Check((await b.GetHistoryAsync()).Any(x => x.Id == r.Id && x.Status == r.Status), "Histórico não persistido");
});
Test("Restauração reconstitui startup com tipo e valor", async () => {
    var (b, f, _) = NewCase(); var before = f.Values["Discord"]; var r = await Disable(b); var restored = await b.UndoAsync(r.Id, default);
    Check(restored.Status == OperationStatus.Reverted && f.Values["Discord"] == before, "Valor/tipo não restaurados");
});
Test("Reabrir backend preserva histórico e permite restauração", async () => {
    var (b, f, path) = NewCase(); var r = await Disable(b);
    var reopened = new WindowsOptimizerBackend(path, f); var found = (await reopened.GetHistoryAsync()).Single(x => x.Id == r.Id);
    Check(found.CanUndo && (await reopened.UndoAsync(r.Id, default)).Status == OperationStatus.Reverted, "Reabertura perdeu reversão");
});
Test("Startup alterado depois da prévia não é removido", async () => {
    var (b, f, _) = NewCase(); var p = await b.PreviewAsync(new(OperationKind.DisableStartup, FakeSystem.StartupId), default);
    var changed = new StartupValueState(1, JsonSerializer.Serialize("outro.exe")); f.Values["Discord"] = changed;
    var r = await b.ApplyAsync(p, default); Check(Denied(r) && f.Values["Discord"] == changed, "Sobrescreveu mudança posterior");
});
Test("Restauração não sobrescreve valor recriado por outro app", async () => {
    var (b, f, _) = NewCase(); var r = await Disable(b); var replacement = new StartupValueState(1, JsonSerializer.Serialize("novo.exe"));
    f.Values["Discord"] = replacement; var undo = await b.UndoAsync(r.Id, default);
    Check(undo.Status == OperationStatus.Conflict && f.Values["Discord"] == replacement, "Conflito não preservado");
});
Test("Falha ao remover startup não é sucesso", async () => {
    var (b, f, _) = NewCase(); f.ThrowDelete = true; var r = await Disable(b);
    Check(Denied(r) && f.Values.ContainsKey("Discord"), "Falha relatada como sucesso");
});
Test("Escrita startup sem efeito falha na verificação", async () => {
    var (b, f, _) = NewCase(); f.IgnoreDelete = true; var r = await Disable(b);
    Check(r.Status is OperationStatus.Failed or OperationStatus.Unverified, "Ausência não foi verificada");
});
Test("Falha depois da remoção mantém registro recuperável", async () => {
    var (b, f, _) = NewCase(); f.ThrowAfterDelete = true; var r = await Disable(b);
    Check(Denied(r) && !f.Values.ContainsKey("Discord"), "Falha parcial não reconhecida");
    Check(r.CanUndo || (await b.GetHistoryAsync()).Any(x => x.Id == r.Id && x.CanUndo), "Backup da falha parcial indisponível");
});
Test("Alteração de energia usa plano existente e verifica", async () => {
    var (b, f, _) = NewCase(); var r = await SetPower(b);
    Check(r.Status == OperationStatus.Completed && f.Active == FakeSystem.Fast, $"Plano não aplicado: {r.Message}");
});
Test("Energia reverte ao plano exato anterior", async () => {
    var (b, f, _) = NewCase(); var r = await SetPower(b); var u = await b.UndoAsync(r.Id, default);
    Check(u.Status == OperationStatus.Reverted && f.Active == FakeSystem.Balanced, "Energia não restaurada");
});
Test("Plano ativo já correto é ignorado sem escrita", async () => {
    var (b, f, _) = NewCase(); f.Active = FakeSystem.Fast;
    var p = await b.PreviewAsync(new(OperationKind.SetPowerPlan, FakeSystem.Fast.ToString()), default);
    Check(!p.CanApply && f.Writes == 0 && p.Title.Contains("configurado", StringComparison.OrdinalIgnoreCase), "Plano idêntico oferecido como alteração");
});
Test("Plano removido entre prévia e aplicação não é escolhido", async () => {
    var (b, f, _) = NewCase(); var p = await b.PreviewAsync(new(OperationKind.SetPowerPlan, FakeSystem.Fast.ToString()), default);
    f.IncludeFast = false; var r = await b.ApplyAsync(p, default); Check(Denied(r) && f.Active == FakeSystem.Balanced, "Plano inexistente aplicado");
});
Test("Política de energia recusada retorna falha e preserva plano", async () => {
    var (b, f, _) = NewCase(); f.ThrowPower = true; var r = await SetPower(b);
    Check(Denied(r) && f.Active == FakeSystem.Balanced, "Falha de política ignorada");
});
Test("Energia sem efeito não é marcada concluída", async () => {
    var (b, f, _) = NewCase(); f.IgnorePower = true; var r = await SetPower(b);
    Check(r.Status is OperationStatus.Failed or OperationStatus.Unverified, "Plano final não verificado");
});
Test("Energia modificada por outro programa não é sobrescrita no undo", async () => {
    var (b, f, _) = NewCase(); var r = await SetPower(b); f.Active = FakeSystem.Other;
    var undo = await b.UndoAsync(r.Id, default); Check(undo.Status == OperationStatus.Conflict && f.Active == FakeSystem.Other, "Conflito energia perdido");
});
Test("Sistema fora do alvo bloqueia mutações", async () => {
    var (b, f, _) = NewCase(); f.IsSupportedOS = false;
    var p = await b.PreviewAsync(new(OperationKind.DisableStartup, FakeSystem.StartupId), default);
    Check(!p.CanApply, "Prévia permitida em OS não suportado");
    try { var r = await b.ApplyAsync(p, default); Check(Denied(r), "Alteração em OS não suportado"); } catch (InvalidOperationException) { }
    Check(f.Writes == 0, "Alteração em OS não suportado");
});
Test("Origem HKLM não é aceita para modificação", async () => {
    var (b, f, _) = NewCase(); var p = await b.PreviewAsync(new(OperationKind.DisableStartup, "hklm-run:Discord"), default);
    Check(!p.CanApply && f.Writes == 0, "Origem privilegiada aceita");
});
Test("Plano com GUID inválido é recusado", async () => {
    var (b, f, _) = NewCase(); var p = await b.PreviewAsync(new(OperationKind.SetPowerPlan, "not-a-guid"), default);
    Check(!p.CanApply && f.Writes == 0, "GUID inválido aceito");
});
Test("Prévia fabricada não autoriza ação", async () => {
    var (b, f, _) = NewCase(); var forged = new OperationPreview { Request = new(OperationKind.DisableStartup, FakeSystem.StartupId), CanApply = true, Before = "forged", After = "forged" };
    try { var r = await b.ApplyAsync(forged, default); Check(Denied(r) && f.Writes == 0, "Prévia forjada aplicada"); }
    catch (ArgumentException) { Check(f.Writes == 0, "Escrita antes de rejeitar"); }
    catch (InvalidOperationException) { Check(f.Writes == 0, "Escrita antes de rejeitar"); }
});
Test("Prévia aprovada de outra instância não autoriza execução", async () => {
    var (b, f, path) = NewCase(); var p = await b.PreviewAsync(new(OperationKind.DisableStartup, FakeSystem.StartupId), default);
    var other = new WindowsOptimizerBackend(path, f);
    try { var r = await other.ApplyAsync(p, default); Check(Denied(r) && f.Writes == 0, "Prévia externa executada"); }
    catch (ArgumentException) { Check(f.Writes == 0, "Mutação inesperada"); }
    catch (InvalidOperationException) { Check(f.Writes == 0, "Mutação inesperada"); }
});
Test("Cancelamento anterior à aplicação preserva estado", async () => {
    var (b, f, _) = NewCase(); var p = await b.PreviewAsync(new(OperationKind.DisableStartup, FakeSystem.StartupId), default);
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    try { var r = await b.ApplyAsync(p, cancel.Token); Check(Denied(r), "Cancelamento ignorado"); } catch (OperationCanceledException) { }
    Check(f.Writes == 0, "Cancelamento fez escrita");
});
Test("Nome de startup com espaços e acentos é preservado", async () => {
    var (b, f, _) = NewCase(); const string name = "Aplicativo de áudio"; f.Values[name] = new StartupValueState(2, JsonSerializer.Serialize("%LOCALAPPDATA%\\Áudio App\\app.exe"));
    var p = await b.PreviewAsync(new(OperationKind.DisableStartup, "hkcu-run:" + Uri.EscapeDataString(name)), default);
    var r = await b.ApplyAsync(p, default); Check(r.Status == OperationStatus.Completed, $"Não aplicou: {r.Message}");
    var u = await b.UndoAsync(r.Id, default); Check(u.Status == OperationStatus.Reverted && f.Values.ContainsKey(name), "Não restaurou nome Unicode");
});
Test("Item desconhecido sem seleção não sofre alteração", async () => {
    var (b, f, _) = NewCase(); f.Values["Aplicativo corporativo"] = new StartupValueState(1, JsonSerializer.Serialize("corp.exe"));
    await Disable(b); Check(f.Values.ContainsKey("Aplicativo corporativo"), "Mutação fora da seleção");
});
Test("Catálogo não promete ganho de FPS nem limpeza de RAM", async () => {
    var (b, f, _) = NewCase(); var recommendations = b.GetRecommendations(await f.AnalyzeAsync(null, default));
    Check(!recommendations.Any(x => x.Title.Contains("FPS", StringComparison.OrdinalIgnoreCase) || x.Title.Contains("limpar RAM", StringComparison.OrdinalIgnoreCase)), "Promessa não medida");
});

Test("Journal interrompido fica não verificado na próxima abertura", async () => {
    var (b, f, path) = NewCase(); Directory.CreateDirectory(path);
    var pending = new OperationRecord { Request = new(OperationKind.DisableStartup, FakeSystem.StartupId), Status = OperationStatus.Pending, Title = "Interrupção isolada", Before = "original", After = "ausente" };
    await File.WriteAllTextAsync(Path.Combine(path, "history.json"), JsonSerializer.Serialize(new { Version = 1, Records = new[] { pending } }));
    var reopened = new WindowsOptimizerBackend(path, f); var history = await reopened.GetHistoryAsync();
    Check(history.Single(x => x.Id == pending.Id).Status == OperationStatus.Unverified && f.Writes == 0, "Interrupção marcada como sucesso");
});
Test("Journal inválido impede escrita no sistema", async () => {
    var (b, f, path) = NewCase(); Directory.CreateDirectory(path); await File.WriteAllTextAsync(Path.Combine(path, "history.json"), "{corrompido");
    try { await Disable(b); } catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException) { }
    Check(f.Writes == 0, "Executou sem histórico íntegro");
});
Test("Prévia não pode ser reaplicada para executar novamente", async () => {
    var (b, f, _) = NewCase(); var p = await b.PreviewAsync(new(OperationKind.DisableStartup, FakeSystem.StartupId), default);
    await b.ApplyAsync(p, default); var before = f.Writes;
    try { var r = await b.ApplyAsync(p, default); Check(Denied(r), "Prévia reutilizada como concluída"); } catch (InvalidOperationException) { }
    Check(f.Writes == before, "Executou a mesma aprovação duas vezes");
});
Test("Inicialização essencial conhecida permanece protegida", async () => {
    var (b, f, _) = NewCase(); f.Values["SecurityHealth"] = new StartupValueState(1, JsonSerializer.Serialize("C:\\Windows\\System32\\SecurityHealthSystray.exe"));
    var p = await b.PreviewAsync(new(OperationKind.DisableStartup, "hkcu-run:SecurityHealth"), default);
    Check(!p.CanApply && f.Writes == 0, "Proteção essencial oferecida como alteração");
});
Test("Aprovação consumida não pode reaplicar após restauração", async () => {
    var (b, f, _) = NewCase(); var p = await b.PreviewAsync(new(OperationKind.DisableStartup, FakeSystem.StartupId), default);
    var r = await b.ApplyAsync(p, default); await b.UndoAsync(r.Id, default); var before = f.Writes;
    try { var next = await b.ApplyAsync(p, default); Check(Denied(next), "Aprovação consumida reutilizada"); } catch (InvalidOperationException) { }
    Check(f.Writes == before && f.Values.ContainsKey("Discord"), "Nova alteração sem nova aprovação");
});
Test("Histórico separado não interfere entre máquinas de teste", async () => {
    var first = NewCase(); var second = NewCase(); await Disable(first.Backend);
    Check((await second.Backend.GetHistoryAsync()).Count == 0 && second.System.Writes == 0, "Histórico vazou entre perfis");
});
Test("Dois executores preservam registros no mesmo journal", async () => {
    var (b, f, path) = NewCase(); var other = new WindowsOptimizerBackend(path, f);
    var p1 = await b.PreviewAsync(new(OperationKind.DisableStartup, FakeSystem.StartupId), default);
    var p2 = await other.PreviewAsync(new(OperationKind.SetPowerPlan, FakeSystem.Fast.ToString()), default);
    await Task.WhenAll(b.ApplyAsync(p1, default), other.ApplyAsync(p2, default));
    var history = await b.GetHistoryAsync();
    Check(history.Count >= 2 && history.Select(x => x.Id).Distinct().Count() == history.Count, "Atualização concorrente perdeu registros");
});

ConfigurationTests.Register(Test);
ToolkitTests.Register(Test, Path.Combine(root, "toolkit"));
ToolkitSessionTests.Register(Test, Path.Combine(root, "sessions"));
ApplicationManagementTests.Register(Test, Path.Combine(root, "applications"));
MaintenanceTests.Register(Test);
UwpTests.Register(Test);
HardwareInventoryTests.Register(Test);
GameGraphicsTests.Register(Test, Path.Combine(root, "graphics"));
ProductIntegrationTests.Register(Test, Path.Combine(root, "integration"));
ProductAreasTests.Register(Test, Path.Combine(root, "areas"));
ScalingEngineTests.Register(Test, Path.Combine(root, "scaling-engine"));
ScalingAbiTests.Register(Test, Path.Combine(root, "scaling-abi"));
EnginePayloadTests.Register(Test, Path.Combine(root, "engine-payload"));

var failed = 0; var results = new List<object>();
foreach (var (name, run) in tests) {
    try { await run(); Console.WriteLine($"PASS {name}"); results.Add(new { name, passed = true, error = (string?)null }); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.GetType().Name} {ex.Message}"); results.Add(new { name, passed = false, error = ex.Message }); }
}
var report = new { executedAt = DateTimeOffset.Now, passed = tests.Count - failed, failed, total = tests.Count, isolatedFakesOnly = true, results };
var reportPath = Path.GetFullPath(Path.Combine(root, "../../..", "test-results.json"));
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{tests.Count - failed}/{tests.Count} passaram. Relatório: {reportPath}");
return failed == 0 ? 0 : 1;

sealed class FakeSystem : IWindowsSystemAccess
{
    public const string StartupId = "hkcu-run:Discord";
    public static readonly Guid Balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid Fast = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid Other = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");
    public bool IsSupportedOS { get; set; } = true;
    public Dictionary<string, StartupValueState> Values { get; } = new(StringComparer.OrdinalIgnoreCase) { ["Discord"] = new(1, JsonSerializer.Serialize("C:\\Users\\Teste\\Discord.exe --start")) };
    public int Writes { get; private set; }
    public bool ThrowDelete { get; set; }
    public bool IgnoreDelete { get; set; }
    public bool ThrowAfterDelete { get; set; }
    public bool ThrowPower { get; set; }
    public bool IgnorePower { get; set; }
    public bool IncludeFast { get; set; } = true;
    public Guid Active { get; set; } = Balanced;
    public Task<SystemSnapshot> AnalyzeAsync(IProgress<string>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new SystemSnapshot { SupportedOS = IsSupportedOS, WindowsName = "Windows 11 TESTE ISOLADO", WindowsBuild = 26100,
            StartupItems = Values.Select(x => new StartupItem("hkcu-run:" + Uri.EscapeDataString(x.Key), x.Key, x.Value.SerializedValue, "HKCU Run", true, "Revisão manual")).ToList(),
            PowerPlans = ReadPowerPlans().ToList(), TotalMemoryBytes = 16UL * 1024 * 1024 * 1024, AvailableMemoryBytes = 8UL * 1024 * 1024 * 1024, ObservationSeconds = 5 });
    }
    public StartupValueState? ReadUserStartup(string name) => Values.GetValueOrDefault(name);
    public void DeleteUserStartup(string name) { if (ThrowDelete) throw new UnauthorizedAccessException("Recusado no teste"); Writes++; if (!IgnoreDelete) Values.Remove(name); if (ThrowAfterDelete) throw new IOException("Falha parcial simulada"); }
    public void WriteUserStartup(string name, StartupValueState value) { Writes++; Values[name] = value; }
    public IReadOnlyList<PowerPlan> ReadPowerPlans() => (IncludeFast ? new[] { new PowerPlan(Balanced, "Equilibrado", Active == Balanced), new PowerPlan(Fast, "Alto desempenho", Active == Fast), new PowerPlan(Other, "Economia", Active == Other) } : new[] { new PowerPlan(Balanced, "Equilibrado", Active == Balanced), new PowerPlan(Other, "Economia", Active == Other) });
    public Guid ReadActivePowerPlan() => Active;
    public void SetActivePowerPlan(Guid id) { if (ThrowPower) throw new UnauthorizedAccessException("Política bloqueou"); Writes++; if (!IgnorePower) Active = id; }
}
