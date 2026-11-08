using System.Diagnostics;
using System.Text.Json;
using VLOptimizer.Core;

public static class ToolkitSessionTests
{
    public static void Register(Action<string, Func<Task>> test, string root)
    {
        int number = 0; Directory.CreateDirectory(root);
        (ToolkitSessionService Service, SessionFake Fake, string Directory, ToolkitSessionOptions Options) Case()
        {
            var dir = Path.Combine(root, (++number).ToString("D3")); var fake = new SessionFake();
            return (new(dir, fake), fake, dir, new([fake.Game.ExecutablePath], [fake.Background.ExecutablePath], SessionFake.Fast, true));
        }
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        async Task Expect(Func<Task> work)
        { try { await work(); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or PlatformNotSupportedException or JsonException or OperationCanceledException) { return; } throw new InvalidOperationException("A operação deveria ter sido recusada"); }
        test("Sessão inicia desligada e leitura não altera prioridades/energia", async () =>
        { var c = Case(); Check(!c.Service.IsWatching && (await c.Service.GetSessionsAsync()).Count == 0 && c.Fake.Writes == 0, "Sessão alterou estado ao criar/ler"); });
        test("Sessão persiste originais antes da primeira alteração e verifica aplicação", async () =>
        {
            var c = Case(); c.Fake.BeforeWrite = () => { var json = File.ReadAllText(Path.Combine(c.Directory, "sessions.json")); Check(json.Contains("OriginalPriority") && json.Contains("OriginalPlanId"), "Escreveu sem journal original"); };
            var r = await c.Service.BeginAsync(c.Options); Check(r.Status == ToolkitSessionStatus.Active && c.Fake.Priorities[c.Fake.Game] == ProcessPriorityClass.High && c.Fake.Priorities[c.Fake.Background] == ProcessPriorityClass.BelowNormal && c.Fake.ActivePlan == SessionFake.Fast, "Sessão não aplicou seleção");
            Check(c.Fake.Priorities[c.Fake.OtherProcess] == ProcessPriorityClass.Normal, "Aplicativo não selecionado alterado");
        });
        test("Sessão restaura prioridade ORIGINAL e plano exato após reabrir serviço", async () =>
        {
            var c = Case(); var r = await c.Service.BeginAsync(c.Options); var reopened = new ToolkitSessionService(c.Directory, c.Fake); var saved = await reopened.GetSessionsAsync();
            Check(saved.Single().NeedsRecovery, "Sessão ativa perdeu recuperação"); var end = await reopened.EndAsync(r.Id);
            Check(end.Status == ToolkitSessionStatus.Ended && c.Fake.Priorities[c.Fake.Background] == ProcessPriorityClass.AboveNormal && c.Fake.Priorities[c.Fake.Game] == ProcessPriorityClass.Normal && c.Fake.ActivePlan == SessionFake.Balanced, "Originais não restaurados");
        });
        test("Sessão preserva prioridade e energia alteradas por outro programa", async () =>
        {
            var c = Case(); var r = await c.Service.BeginAsync(c.Options); c.Fake.Priorities[c.Fake.Background] = ProcessPriorityClass.Idle; c.Fake.ActivePlan = SessionFake.Other;
            var end = await c.Service.EndAsync(r.Id); Check(end.Status == ToolkitSessionStatus.Ended && end.Processes.Any(p => p.State == ToolkitSessionChangeState.Conflict) && end.Power!.State == ToolkitSessionChangeState.Conflict && c.Fake.ActivePlan == SessionFake.Other && c.Fake.Priorities[c.Fake.Background] == ProcessPriorityClass.Idle, "Conflito foi sobrescrito");
        });
        test("Sessão não altera PID reutilizado com outro starttime", async () =>
        {
            var c = Case(); var r = await c.Service.BeginAsync(c.Options); c.Fake.Priorities.Remove(c.Fake.Background); var replacement = c.Fake.Background with { StartedUtcTicks = 9999999 }; c.Fake.Priorities[replacement] = ProcessPriorityClass.Idle;
            var end = await c.Service.EndAsync(r.Id); Check(end.Processes.Single(p => p.Identity == c.Fake.Background).State == ToolkitSessionChangeState.Exited && c.Fake.Priorities[replacement] == ProcessPriorityClass.Idle, "PID novo recebeu estado do antigo");
        });
        test("Sessão preserva decisão externa tomada antes da primeira aplicação", async () =>
        {
            var c = Case(); c.Fake.BeforePriorityRead = identity => { c.Fake.Priorities[identity] = ProcessPriorityClass.BelowNormal; c.Fake.BeforePriorityRead = null; };
            var r = await c.Service.BeginAsync(c.Options); var conflict = r.Processes.Single(p => p.State == ToolkitSessionChangeState.Conflict); Check(conflict.Identity == c.Fake.Game, "Fixture não criou conflito inicial");
            await c.Service.EndAsync(r.Id); Check(c.Fake.Priorities[c.Fake.Game] == ProcessPriorityClass.BelowNormal, "Undo sobrescreveu prioridade que a sessão nunca aplicou");
        });
        test("Sessão sem jogo conhecido e sistema incompatível não modifica Windows", async () =>
        {
            var c = Case(); c.Fake.Priorities.Remove(c.Fake.Game); await Expect(async () => await c.Service.BeginAsync(c.Options)); Check(c.Fake.Writes == 0, "Sem jogo houve mutação");
            c = Case(); c.Fake.IsSupportedOS = false; await Expect(async () => await c.Service.BeginAsync(c.Options)); Check(c.Fake.Writes == 0, "Sistema incompatível alterado");
        });
        test("Sessão interrompida depois de mudança mantém backup e restaura", async () =>
        {
            var c = Case(); c.Fake.ThrowAfterFirstPriorityWrite = true; var r = await c.Service.BeginAsync(c.Options); Check(r.Status == ToolkitSessionStatus.Partial, "Falha parcial marcada sucesso");
            c.Fake.ThrowAfterFirstPriorityWrite = false; var end = await new ToolkitSessionService(c.Directory, c.Fake).RecoverAsync(r.Id);
            Check(end.Status == ToolkitSessionStatus.Ended && c.Fake.Priorities[c.Fake.Background] == ProcessPriorityClass.AboveNormal && c.Fake.Priorities[c.Fake.Game] == ProcessPriorityClass.Normal, "Backup de falha parcial perdido");
        });
        test("Sessão duplicada é bloqueada e encerramento repetido não escreve", async () =>
        {
            var c = Case(); var r = await c.Service.BeginAsync(c.Options); int writes = c.Fake.Writes; await Expect(async () => await c.Service.BeginAsync(c.Options)); Check(c.Fake.Writes == writes, "Sessão duplicada aplicou");
            await c.Service.EndAsync(r.Id); writes = c.Fake.Writes; await c.Service.EndAsync(r.Id); Check(c.Fake.Writes == writes, "End repetido alterou sistema");
        });
        test("Sessão não sobrescreve plano original que foi removido", async () =>
        {
            var c = Case(); var r = await c.Service.BeginAsync(c.Options); c.Fake.IncludeBalanced = false; var end = await c.Service.EndAsync(r.Id);
            Check(end.Power!.State == ToolkitSessionChangeState.Conflict && c.Fake.ActivePlan == SessionFake.Fast, "Plano removido foi escolhido");
        });
        test("Sessão cancelada antes de Begin não altera sistema", async () =>
        {
            var c = Case(); using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Expect(async () => await c.Service.BeginAsync(c.Options, cancel.Token)); Check(c.Fake.Writes == 0, "Cancelamento prévio ignorado");
        });
        test("Sessão com journal corrompido bloqueia primeira alteração", async () =>
        {
            var c = Case(); Directory.CreateDirectory(c.Directory); File.WriteAllText(Path.Combine(c.Directory, "sessions.json"), "{invalid"); await Expect(async () => await c.Service.BeginAsync(c.Options)); Check(c.Fake.Writes == 0, "Journal inválido não bloqueou");
        });
        test("Sessão exige caminhos distintos para jogo e app reduzido", async () =>
        {
            var c = Case(); await Expect(async () => await c.Service.BeginAsync(c.Options with { BackgroundExecutables = c.Options.GameExecutables })); Check(c.Fake.Writes == 0, "Alvo ambíguo foi alterado");
        });
        test("Sessão watcher explícito restaura em cancelamento sem processos reais", async () =>
        {
            var c = Case(); using var cancel = new CancellationTokenSource(); var active = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var progress = new ImmediateProgress<ToolkitSessionRecord>(r => { if (r.Status == ToolkitSessionStatus.Active) { active.TrySetResult(true); cancel.Cancel(); } });
            var watch = c.Service.WatchAsync(c.Options, progress, cancel.Token); await active.Task.WaitAsync(TimeSpan.FromSeconds(5)); await watch;
            Check(!c.Service.IsWatching && c.Fake.Priorities[c.Fake.Background] == ProcessPriorityClass.AboveNormal && c.Fake.ActivePlan == SessionFake.Balanced && (await c.Service.GetSessionsAsync()).Single().Status == ToolkitSessionStatus.Ended, "Watcher não restaurou ao cancelar");
        });
        test("Sessão watcher restaura se cancelada durante Begin com journal preparado", async () =>
        {
            var c = Case(); using var cancel = new CancellationTokenSource(); c.Fake.BeforeWrite = () => cancel.Cancel();
            await c.Service.WatchAsync(c.Options, null, cancel.Token);
            Check(!c.Service.IsWatching && c.Fake.Priorities[c.Fake.Game] == ProcessPriorityClass.Normal && c.Fake.Priorities[c.Fake.Background] == ProcessPriorityClass.AboveNormal && c.Fake.ActivePlan == SessionFake.Balanced && (await c.Service.GetSessionsAsync()).Single().Status == ToolkitSessionStatus.Ended, "Cancelamento durante Begin deixou alteração aplicada");
        });
    }
    private sealed class ImmediateProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
    private sealed class SessionFake : IToolkitSessionSystem
    {
        public static readonly Guid Balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        public static readonly Guid Fast = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public static readonly Guid Other = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");
        public ToolkitProcessIdentity Game = new(100, 100000, @"C:\Fixtures\Game.exe");
        public ToolkitProcessIdentity Background = new(200, 200000, @"C:\Fixtures\Background.exe");
        public ToolkitProcessIdentity OtherProcess = new(300, 300000, @"C:\Fixtures\Other.exe");
        public bool IsSupportedOS { get; set; } = true;
        public bool IncludeBalanced = true, ThrowAfterFirstPriorityWrite;
        public int Writes;
        public Action? BeforeWrite;
        public Action<ToolkitProcessIdentity>? BeforePriorityRead;
        public Guid ActivePlan = Balanced;
        public Dictionary<ToolkitProcessIdentity, ProcessPriorityClass> Priorities;
        public SessionFake() => Priorities = new() { [Game] = ProcessPriorityClass.Normal, [Background] = ProcessPriorityClass.AboveNormal, [OtherProcess] = ProcessPriorityClass.Normal };
        public IReadOnlyList<ToolkitSessionProcess> ReadProcesses() => Priorities.Select(p => new ToolkitSessionProcess(p.Key, p.Value)).ToArray();
        public ProcessPriorityClass? ReadPriority(ToolkitProcessIdentity identity) { BeforePriorityRead?.Invoke(identity); return Priorities.TryGetValue(identity, out var value) ? value : null; }
        public void SetPriority(ToolkitProcessIdentity identity, ProcessPriorityClass priority)
        {
            BeforeWrite?.Invoke(); if (!Priorities.ContainsKey(identity)) throw new InvalidOperationException("Identidade ausente"); Priorities[identity] = priority; Writes++;
            if (ThrowAfterFirstPriorityWrite && Writes == 1) throw new IOException("Falha parcial de fixture depois da alteração");
        }
        public IReadOnlyList<PowerPlan> ReadPowerPlans() => (IncludeBalanced ? new[] { new PowerPlan(Balanced, "Original", ActivePlan == Balanced) } : Array.Empty<PowerPlan>()).Concat(new[] { new PowerPlan(Fast, "Sessão", ActivePlan == Fast), new PowerPlan(Other, "Outro", ActivePlan == Other) }).ToArray();
        public Guid ReadActivePowerPlan() => ActivePlan;
        public void SetActivePowerPlan(Guid plan) { BeforeWrite?.Invoke(); ActivePlan = plan; Writes++; }
    }
}
