using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private string _gamesTab = "Bibliotecas";
    private readonly HashSet<string> _sessionGames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sessionBackground = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ToolkitSessionRecord> _gameSessions = [];
    private Guid? _sessionPower;
    private bool _sessionRaisePriority;
    private CancellationTokenSource? _sessionMonitorCancellation;
    private Task? _sessionMonitorTask;
    private bool _sessionCloseAllowed;

    private UIElement BuildSessionTool()
    {
        var root = Stack(); root.Children.Add(InlineNote("Sessões só começam quando você decide.", "Escolha os executáveis. Apps de fundo selecionados passam para prioridade Abaixo do normal; nenhum é encerrado. O plano e as prioridades anteriores ficam registrados antes da mudança e são restaurados se continuarem no estado aplicado.", "\uE7FC"));
        var watching = _sessionMonitorTask is { IsCompleted: false };
        if (watching) root.Children.Add(InlineNote("Monitor de jogos ativo", "A cada dois segundos, os executáveis selecionados são procurados. O monitor inicia a sessão ao encontrar o jogo e tenta restaurar ao fechar o jogo, parar o monitor ou encerrar o VL.", "\uE768"));
        var actions = new WrapPanel(); actions.Children.Add(FeatureButton("Escolher executável do jogo…", PickSessionGame)); actions.Children.Add(FeatureButton("Atualizar aplicativos e sessões", () => _ = RefreshSessionsAsync()));
        if (watching) actions.Children.Add(FeatureButton("Parar monitor e restaurar", () => _ = StopSessionMonitorAsync(), true)); root.Children.Add(actions);
        var selection = Stack(); selection.Children.Add(Text("Jogos selecionados", 16, weight: FontWeights.SemiBold));
        if (_sessionGames.Count == 0) selection.Children.Add(Text("Selecione ao menos um executável. A sessão manual exige que o jogo já esteja aberto.", 12, Muted, margin: new Thickness(0, 8, 0, 8)));
        foreach (var path in _sessionGames.ToArray())
        {
            var item = new CheckBox { Content = Text(path, 12), IsChecked = true, IsEnabled = !_busy && !watching }; item.Checked += (_, _) => _sessionGames.Add(path); item.Unchecked += (_, _) => _sessionGames.Remove(path); selection.Children.Add(item);
        }
        var powerChoices = new List<(string Id, string Title)> { ("unchanged", "Manter plano atual") }; powerChoices.AddRange((_snapshot?.PowerPlans ?? []).Select(p => (p.Id.ToString(), p.Name)));
        var power = NewChoice("Plano durante a sessão", powerChoices, _sessionPower?.ToString() ?? "unchanged", value => _sessionPower = Guid.TryParse(value, out var id) ? id : null); power.IsEnabled = !watching && !_busy; power.Margin = new Thickness(0, 12, 0, 12); selection.Children.Add(power);
        var raise = new CheckBox { Content = Text("Elevar prioridade do jogo para Alta durante a sessão", 13), IsChecked = _sessionRaisePriority, IsEnabled = !_busy && !watching }; raise.Checked += (_, _) => _sessionRaisePriority = true; raise.Unchecked += (_, _) => _sessionRaisePriority = false; selection.Children.Add(raise);
        selection.Children.Add(Text("Prioridade Alta pode reduzir a resposta de outros aplicativos. Não usa Tempo real e não comprova ganho no jogo.", 11, Muted, margin: new Thickness(29, 0, 0, 12)));
        var background = Stack();
        foreach (var process in _observedProcesses.Where(p => p.ExecutablePath != null && p.HasWindow).DistinctBy(p => p.ExecutablePath, StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Name))
        {
            var path = process.ExecutablePath!; var select = new CheckBox { Content = Text(process.Name + " · " + path, 12), IsChecked = _sessionBackground.Contains(path), IsEnabled = !_busy && !watching }; select.Checked += (_, _) => _sessionBackground.Add(path); select.Unchecked += (_, _) => _sessionBackground.Remove(path); background.Children.Add(select);
        }
        if (background.Children.Count == 0) background.Children.Add(Text("Atualize os aplicativos para escolher os que podem ficar em Abaixo do normal. Nenhum aplicativo é selecionado por padrão.", 12, Muted, margin: new Thickness(0, 8, 0, 0)));
        selection.Children.Add(new Expander { Header = $"Aplicativos de fundo · {_sessionBackground.Count} selecionados", Content = background, Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 16) });
        var start = new WrapPanel(); var manual = FeatureButton("Revisar sessão manual", () => _ = BeginManualSessionAsync(), true); manual.IsEnabled = !_busy && !watching; start.Children.Add(manual);
        var monitor = FeatureButton("Revisar monitor de jogos", () => _ = StartSessionMonitorAsync()); monitor.IsEnabled = !_busy && !watching; start.Children.Add(monitor); selection.Children.Add(start);
        selection.Children.Add(Text("Sessão manual permanece ativa até você encerrá-la nesta tela. Se o VL fechar inesperadamente, consulte as sessões pendentes na próxima abertura; nenhuma recuperação escreve no Windows sem a sua ação.", 12, Muted)); root.Children.Add(Card(selection));
        foreach (var session in _gameSessions.OrderByDescending(s => s.CreatedAt).Take(30))
        {
            var card = Stack(); card.Children.Add(Text(session.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") + " · " + SessionLabel(session.Status), 16, weight: FontWeights.SemiBold));
            foreach (var process in session.Processes) card.Children.Add(Text($"PID {process.Identity.Id} · {System.IO.Path.GetFileName(process.Identity.ExecutablePath)} · {process.OriginalPriority} → {process.AppliedPriority} · {process.State}", 12, Muted, margin: new Thickness(0, 8, 0, 0)));
            if (session.Power != null) card.Children.Add(Text("Energia: " + session.Power.State + " · " + session.Power.Message, 12, Muted, margin: new Thickness(0, 8, 0, 0)));
            foreach (var notice in session.Notices) card.Children.Add(Text(notice, 11, Muted, margin: new Thickness(0, 7, 0, 0)));
            if (session.NeedsRecovery) card.Children.Add(FeatureButton("Revisar encerramento e restauração", () => _ = EndManualSessionAsync(session))); root.Children.Add(Card(card));
        }
        return root;
    }

    private static string SessionLabel(ToolkitSessionStatus status) => status switch { ToolkitSessionStatus.Active => "Ativa", ToolkitSessionStatus.Prepared => "Preparada · conferir recuperação", ToolkitSessionStatus.Ending => "Restauração pendente", ToolkitSessionStatus.Ended => "Encerrada", _ => "Parcial · conferir etapas" };
    private void PickSessionGame()
    {
        if (_busy || _smoke || _sessionMonitorTask is { IsCompleted: false }) return; var picker = new OpenFileDialog { Filter = "Executável do jogo (*.exe)|*.exe", CheckFileExists = true, Title = "Selecionar jogo para a sessão" }; if (picker.ShowDialog(this) == true) { _sessionGames.Add(picker.FileName); RenderPage(); }
    }
    private async Task RefreshSessionsAsync() => await RunFeatureAsync("Lendo aplicativos e sessões de jogo…", async token => { _observedProcesses = await Toolkit.Inventory.ReadProcessesAsync(token); _gameSessions = await Toolkit.Sessions.GetSessionsAsync(token); });
    private ToolkitSessionOptions SessionOptions() => new(_sessionGames.ToArray(), _sessionBackground.ToArray(), _sessionPower, _sessionRaisePriority);
    private bool ConfirmSession(ToolkitSessionOptions options, bool monitor)
    {
        if (options.GameExecutables.Count == 0) { Notice("Selecione ao menos um executável de jogo."); return false; }
        return ConfirmFeature(monitor ? "Ativar monitor de jogos?" : "Iniciar sessão manual?", "Jogos:\n" + string.Join("\n", options.GameExecutables) + "\n\nApps que passam para Abaixo do normal:\n" + (options.BackgroundExecutables.Count == 0 ? "Nenhum" : string.Join("\n", options.BackgroundExecutables)) + "\n\n" + (options.RaiseGamePriority ? "Prioridade do jogo: Alta." : "Prioridade do jogo: preservada.") + "\nEnergia: " + (options.PowerPlanId.HasValue ? _snapshot?.PowerPlans.FirstOrDefault(p => p.Id == options.PowerPlanId.Value)?.Name ?? options.PowerPlanId.Value.ToString() : "manter plano atual") + "\n\n" + (monitor ? "O monitor fica ligado até você pará-lo ou encerrar o VL. Ele tenta restaurar quando o jogo fecha." : "O jogo precisa estar aberto. Encerre a sessão nesta tela para restaurar os valores anteriores."), monitor ? "Ativar monitor escolhido" : "Iniciar sessão revisada");
    }
    private async Task BeginManualSessionAsync()
    {
        if (_busy || _smoke) return; var options = SessionOptions(); if (!ConfirmSession(options, false)) return;
        await RunFeatureAsync("Iniciando e verificando sessão de jogo…", async token => { var record = await Toolkit.Sessions.BeginAsync(options, token); await JournalSessionAsync(record); _gameSessions = await Toolkit.Sessions.GetSessionsAsync(CancellationToken.None); Notice("Sessão registrada. Encerre nesta tela para recuperar prioridades e energia anteriores."); });
    }
    private async Task EndManualSessionAsync(ToolkitSessionRecord record)
    {
        if (_busy || _smoke || !ConfirmFeature("Encerrar e restaurar a sessão?", "Prioridades e energia serão comparadas com o último estado aplicado. Mudanças externas e processos com nova identidade serão preservados.", "Encerrar sessão revisada")) return;
        await RunFeatureAsync("Restaurando estado anterior da sessão…", async token => { var result = await Toolkit.Sessions.EndAsync(record.Id, token); await JournalSessionAsync(result); _gameSessions = await Toolkit.Sessions.GetSessionsAsync(CancellationToken.None); Notice(SessionLabel(result.Status) + ". Confira o resultado de cada etapa."); });
    }
    private async Task JournalSessionAsync(ToolkitSessionRecord record) => await RecordFeatureActivityAsync("session", "Sessão de jogo", SessionLabel(record.Status), string.Join("; ", record.Notices) + " " + string.Join("; ", record.Processes.Select(p => $"PID {p.Identity.Id}: {p.State} · {p.Message}")), record.Status == ToolkitSessionStatus.Ended ? OperationStatus.Reverted : record.Status == ToolkitSessionStatus.Active ? OperationStatus.Completed : OperationStatus.PartiallyCompleted);
    private async Task StartSessionMonitorAsync()
    {
        if (_busy || _smoke || _sessionMonitorTask is { IsCompleted: false }) return; var options = SessionOptions(); if (!ConfirmSession(options, true)) return;
        _sessionMonitorCancellation = new CancellationTokenSource(); _sessionMonitorTask = MonitorSessionsAsync(options, _sessionMonitorCancellation.Token); RenderPage();
        await Task.Yield();
    }
    private async Task MonitorSessionsAsync(ToolkitSessionOptions options, CancellationToken token)
    {
        try
        {
            var progress = new Progress<ToolkitSessionRecord>(record => { _ = ObserveSessionRecordAsync(record); });
            await Toolkit.Sessions.WatchAsync(options, progress, token);
        }
        catch (Exception ex) { Notice("Monitor encerrado: " + ex.Message + ". Consulte as sessões pendentes antes de reiniciar.", true); }
        finally { if (IsLoaded && !_sessionCloseAllowed) _ = Dispatcher.BeginInvoke(() => { if (_page == "games") RenderPage(); }); }
    }
    private async Task ObserveSessionRecordAsync(ToolkitSessionRecord record)
    {
        await JournalSessionAsync(record);
        try { _gameSessions = await Toolkit.Sessions.GetSessionsAsync(); if (_page == "games" && !_busy) RenderPage(); }
        catch (Exception ex) { Notice("A sessão mudou, mas sua lista não pôde ser atualizada: " + ex.Message, true); }
    }
    private async Task StopSessionMonitorAsync()
    {
        if (_sessionMonitorTask == null) return; _sessionMonitorCancellation?.Cancel();
        await _sessionMonitorTask; _sessionMonitorCancellation?.Dispose(); _sessionMonitorCancellation = null; _sessionMonitorTask = null;
        _gameSessions = await Toolkit.Sessions.GetSessionsAsync(); RenderPage(); Notice("Monitor parado. Confira a restauração da última sessão na lista.");
    }
    private async void OnClosingSessionMonitor(object? sender, CancelEventArgs e)
    {
        if (_sessionCloseAllowed || _sessionMonitorTask is not { IsCompleted: false } && _memoryScheduler?.IsRunning != true) return;
        e.Cancel = true; _sessionMonitorCancellation?.Cancel(); _analysisStatus.Text = "◌  Encerrando monitor e restaurando sessão antes de fechar…";
        try { if (_sessionMonitorTask != null) await _sessionMonitorTask; await StopMemorySchedulerAsync(false); }
        catch (Exception ex) { Notice("Ao encerrar automações: " + ex.Message + ". Consulte as sessões pendentes ao reabrir.", true); }
        finally { _sessionCloseAllowed = true; Close(); }
    }
}
