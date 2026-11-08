using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using VLOptimizer.Core;
using Path = System.IO.Path;

namespace VLOptimizer.App;

public sealed partial class MainWindow : Window
{
    private readonly IOptimizerBackend _backend = new WindowsOptimizerBackend();
    private readonly string[] _args;
    private readonly Grid _shell = new();
    private readonly Border _sidebar = new();
    private readonly ColumnDefinition _sidebarColumn = new() { Width = new GridLength(226) };
    private readonly StackPanel _navigation = new();
    private readonly StackPanel _sidebarFooter = new();
    private readonly Grid _header = new();
    private readonly StackPanel _searchPanel = new();
    private readonly List<TextBlock> _navLabels = [];
    private readonly Dictionary<string, Button> _navButtons = [];
    private readonly ContentControl _pageHost = new();
    private readonly TextBlock _pageTitle = new();
    private readonly TextBlock _pageSubtitle = new();
    private readonly TextBlock _analysisStatus = new();
    private readonly TextBox _search = new();
    private readonly Border _noticeBox = new();
    private readonly TextBlock _noticeText = new();
    private readonly TextBlock _sidebarBrand = new();
    private readonly HashSet<string> _selectedStartup = [];
    private readonly List<RotateTransform> _spinners = [];
    private SystemSnapshot? _snapshot;
    private SystemSnapshot? _previousSnapshot;
    private IReadOnlyList<OperationRecord> _history = [];
    private CancellationTokenSource? _cts;
    private string _page = "overview";
    private string _stage = "Pronto para analisar";
    private bool _busy;
    private bool _smoke;
    private bool _motionSmoke;
    private bool _economy;
    private bool _reduceMotion;
    private bool _building;
    private double _lastLayoutWidth;
    private Button? _reviewStartup;
    private static readonly Brush Muted = Brush(Palette.Muted);
    private static readonly Brush Accent = Brush(Palette.Accent);
    private static readonly Brush AccentSoft = Brush(Palette.AccentBright);

    public MainWindow(string[] args)
    {
        _args = args;
        _motionSmoke = args.Contains("--motion-smoke");
        _smoke = _motionSmoke || args.Contains("--smoke-test");
        Title = Brand.Name;
        Width = Math.Min(1536, SystemParameters.WorkArea.Width); Height = Math.Min(884, SystemParameters.WorkArea.Height); MinWidth = 620; MinHeight = 360;
        Background = Brush(Palette.Canvas); Foreground = Brush(Palette.Text); FontFamily = new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Inter"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        LoadPreferences();
        TextOptions.SetTextFormattingMode(_shell, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(_shell, TextRenderingMode.Grayscale);
        BuildShell();
        SourceInitialized += (_, _) => SetDarkTitleBar();
        SizeChanged += (_, _) => AdaptLayout();
        StateChanged += (_, _) => UpdateSpinners();
        PreviewKeyDown += OnKeyDown;
        Closing += (_, _) => _cts?.Cancel();
        Closing += (_, e) => { if (_maintenanceRunning) { e.Cancel = true; Notice("Uma ferramenta de manutenção do Windows está em andamento. O cancelamento será atendido entre etapas; aguarde a conclusão da ferramenta antes de fechar."); } };
        Closing += OnClosingSessionMonitor;
        Closed += (_, _) => StopSceneMotion();
        Closed += (_, _) => { _fpsOverlayWindow?.Close(); _fpsOverlayWindow = null; };
        IsVisibleChanged += (_, _) => UpdateSceneMotion();
        SystemParameters.StaticPropertyChanged += OnMotionSystemPreferenceChanged;
        Loaded += async (_, _) =>
        {
            await LoadHistoryAsync();
            Navigate("overview", false);
            UpdateSceneMotion();
            if (_motionSmoke) await RunMotionSmokeAsync();
            else if (_smoke) await RunSmokeAsync();
            else await AnalyzeAsync();
        };
    }

    private static SolidColorBrush Brush(string color)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b;
    }

    private void Navigate(string page, bool animate = true)
    {
        var changed = _page != page;
        _page = page;
        foreach (var item in _navButtons)
        {
            item.Value.Background = item.Key == page ? Brush(Palette.Raised) : Brushes.Transparent;
            item.Value.BorderBrush = item.Key == page ? Brush(Palette.AccentDeep) : Brushes.Transparent;
            if (item.Value.Content is StackPanel s)
                foreach (var child in s.Children.OfType<TextBlock>()) child.Foreground = item.Key == page ? Brush(Palette.Text) : Muted;
        }
        RenderPage(animate && InputManager.Current.MostRecentInputDevice is not KeyboardDevice);
        if (changed) ScrollToPageStart();
        if (page == "history") _ = RefreshHistoryPageAsync();
        if (page == "scaling" && _scalingCapabilities == null && !_busy) _ = RefreshScalingAsync();
    }

    private void RenderPage(bool animate = false)
    {
        _building = true;
        foreach (var spinner in _spinners) spinner.BeginAnimation(RotateTransform.AngleProperty, null);
        _spinners.Clear();
        var (title, subtitle) = _page switch
        {
            "catalog" => ("Ajustes do Windows", "Escolha por categoria, consulte o estado e revise os efeitos antes de aplicar."),
            "games" => ("Jogos e FiveM", "Instalações locais, perfis e cópias de segurança vinculadas ao jogo."),
            "measurements" => ("Medições e diagnóstico", "Amostras reais, sessões de FPS e comparação com contexto."),
            "scaling" => ("Scaling e perfis", "Configure os seus perfis e confira a disponibilidade do motor externo."),
            "search" => ("Resultados da busca", "Ajustes, ferramentas, páginas e itens encontrados neste computador."),
            "startup" => ("Inicialização", "Escolha o que deve acompanhar o Windows. Você revisa cada alteração."),
            "power" => ("Energia", "Desempenho, autonomia e consumo: uma escolha para o seu contexto."),
            "storage" => ("Limpeza e armazenamento", "Verifique arquivos por categoria, revise a lista e confira o resultado da exclusão."),
            "processes" => ("Processos", "Observe o consumo e revise qualquer encerramento individual antes de executar."),
            "history" => ("Histórico e restauração", "Cada mudança tem estado anterior, verificação e resultado registrado."),
            "settings" => ("Do seu jeito", "Preferências de interface, privacidade e informações do aplicativo."),
            _ => (Greeting(), "O que já está no ponto e o que faz sentido ajustar no seu PC.")
        };
        if (ProductPages.Find(_page) is { } productPage && _page != "overview") { title = productPage.Title; subtitle = productPage.Description; }
        _pageTitle.Text = title; _pageSubtitle.Text = subtitle; UpdateDesignState();
        _pageHost.Content = _page switch
        {
            "catalog" => BuildConfigurationCatalog(), "games" => BuildGamesPage(), "measurements" => BuildMeasurementsPage(),
            "scaling" => BuildScalingPage(), "search" => BuildSearchResults(),
            "performance" or "input" or "memory" or "network" or "windows" or "applications" or "graphics" or "fps" or "hardware" => BuildProductArea(_page),
            "startup" => BuildStartupArea(), "power" => BuildPower(), "storage" => BuildProductArea("storage"),
            "processes" => BuildProcesses(), "history" => BuildHistory(), "settings" => BuildSettings(), _ => BuildOverview()
        };
        _building = false;
        UpdateSpinners();
        MotionTransitions.Show(_pageHost, animate && MotionAllowed && (!_smoke || _motionSmoke));
    }

    private void UpdateSelectionButton()
    {
        if (_reviewStartup == null) return;
        _reviewStartup.Content = $"Revisar seleção ({_selectedStartup.Count})";
        _reviewStartup.IsEnabled = _selectedStartup.Count > 0 && !_busy;
    }

    private UIElement BuildPower()
    {
        var root = Stack(); var snapshot = _snapshot;
        if (snapshot == null) { root.Children.Add(AnalyzeEmpty()); return root; }
        var context = snapshot.OnBattery switch { true => "Na bateria: considere a autonomia antes de escolher.", false => "Na tomada: equilibre desempenho, temperatura e consumo.", _ => "Alimentação não identificada: escolha conforme seu uso." };
        root.Children.Add(InlineNote("Energia para o seu momento.", context + " O aplicativo oferece somente os planos existentes, com revisão antes de qualquer mudança.", "\uE945"));
        var panel = Stack(); panel.Children.Add(SectionTitle("Planos disponíveis", "Estado confirmado na última análise"));
        if (snapshot.PowerPlans.Count == 0) panel.Children.Add(Empty("\uE946", "Nenhum plano foi identificado.", "O recurso pode estar limitado pelo hardware ou por uma política do Windows."));
        foreach (var plan in snapshot.PowerPlans)
        {
            var row = new Grid { Margin = new Thickness(0, 10, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = Stack(); info.Margin = new Thickness(0, 0, 20, 0);
            info.Children.Add(Text(plan.Name, 16, plan.IsActive ? AccentSoft : null, FontWeights.SemiBold));
            info.Children.Add(Text(plan.IsActive ? "Plano ativo" : "Disponível neste computador", 12, Muted, margin: new Thickness(0, 5, 0, 0))); row.Children.Add(info);
            var button = Button(plan.IsActive ? "Ativo" : "Revisar mudança", () => _ = ReviewRequestsAsync([new OperationRequest(OperationKind.SetPowerPlan, plan.Id.ToString())]));
            button.IsEnabled = !plan.IsActive && !_busy; button.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(button, 1); row.Children.Add(button); panel.Children.Add(row);
            panel.Children.Add(new Border { Height = 1, Background = Brush(Palette.Line) });
        }
        panel.Children.Add(Text("Mudar o plano pode afetar consumo, autonomia, ruído e temperatura. Não representa ganho de FPS medido.", 12, Muted, margin: new Thickness(0, 16, 0, 0)));
        var card = Card(panel); card.MaxWidth = 1060; card.HorizontalAlignment = HorizontalAlignment.Stretch; root.Children.Add(card);
        root.Children.Add(ActionFooter("Abrir energia no Windows", "ms-settings:powersleep")); return root;
    }

    private UIElement BuildProcesses()
    {
        var root = Stack();
        if (_snapshot == null) { root.Children.Add(AnalyzeEmpty()); return root; } root.Children.Add(InlineNote("Observação, não um diagnóstico de gargalo.", "Consumo de memória ou CPU depende do trabalho em andamento. Estes valores são uma amostra; não comprovam que um programa está prejudicando o PC. O encerramento individual é opcional e exige revisar a identidade e a perda de dados não salvos.", "\uE9D9"));
        root.Children.Add(SectionTitle("Consumo observado", $"{_snapshot.ObservationSeconds:0.#} segundos de coleta · ordenação do diagnóstico"));
        if (_snapshot.TopProcesses.Count == 0) root.Children.Add(Empty("\uE946", "Os processos não puderam ser observados.", "Permissões e processos que terminam durante a coleta podem limitar a leitura."));
        foreach (var process in _snapshot.TopProcesses.Where(p => Matches(p.Name + " " + p.Id)))
        {
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            var name = Stack(); name.Children.Add(Text(process.Name, 15, weight: FontWeights.SemiBold)); name.Children.Add(Text("PID " + process.Id, 11, Muted, margin: new Thickness(0, 5, 0, 0))); grid.Children.Add(name);
            var memory = Stack(); memory.Children.Add(Text(Bytes(process.MemoryBytes), 17)); memory.Children.Add(Text("MEMÓRIA RESIDENTE", 9, Muted, margin: new Thickness(0, 5, 0, 0))); Grid.SetColumn(memory, 1); grid.Children.Add(memory);
            var cpu = Stack(); cpu.Children.Add(Text(process.CpuPercent is double percent ? $"{percent:0.#}%" : "Indisponível", 17)); cpu.Children.Add(Text("CPU OBSERVADA", 9, Muted, margin: new Thickness(0, 5, 0, 0))); Grid.SetColumn(cpu, 2); grid.Children.Add(cpu);
            var content = Stack(); content.Children.Add(grid); content.Children.Add(new Expander { Header = "Controle individual do processo", Content = FeatureButton("Revisar encerramento…", () => _ = ReviewKillProcessAsync(process.Id)), Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 12, 0, 0) }); root.Children.Add(Card(content));
        }
        var task = Button("Abrir Gerenciador de Tarefas", () => OpenExternal(Path.Combine(Environment.SystemDirectory, "Taskmgr.exe"))); task.HorizontalAlignment = HorizontalAlignment.Left; root.Children.Add(task); return root;
    }

    private UIElement BuildHistory()
    {
        var root = Stack(); root.Children.Add(InlineNote("O que mudou. E como voltar.", "O resultado vem da verificação do estado final. A restauração detecta conflitos com mudanças posteriores antes de sobrescrever uma configuração.", "\uE81C"));
        var bar = new WrapPanel { Margin = new Thickness(0, 2, 0, 16) }; bar.Children.Add(Button("Atualizar histórico", () => _ = RefreshHistoryPageAsync()));
        var folder = Button("Abrir pasta de dados", () => OpenExternal(_backend.DataDirectory)); folder.Margin = new Thickness(10, 0, 0, 0); bar.Children.Add(folder); root.Children.Add(bar);
        var restoreAll = FeatureButton("Revisar restauração das configurações", () => _ = UndoAllConfigurationsAsync()); restoreAll.IsEnabled = !_busy && _history.Any(r => r.CanUndo); bar.Children.Add(restoreAll);
        root.Children.Add(Text("Restauração geral recupera configurações reversíveis, da mais recente para a mais antiga. Limpezas não possuem desfazer; backups FiveM e OptiScaler têm revisão nas páginas dos respectivos recursos.", 12, Muted, margin: new Thickness(0, 0, 0, 16)));
        if (_history.Count == 0) root.Children.Add(Empty("\uE81C", "Uma história que começa com controle.", "Nenhuma operação registrada. Após revisar e aplicar uma alteração, o resultado e a opção de reversão aparecerão aqui."));
        foreach (var record in _history.OrderByDescending(r => r.StartedAt))
        {
            var card = Stack(); var head = new Grid(); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Text(record.Title, 17, weight: FontWeights.SemiBold)); var status = Chip(StatusLabel(record.Status), StatusColor(record.Status)); Grid.SetColumn(status, 1); head.Children.Add(status); card.Children.Add(head);
            card.Children.Add(Text(record.StartedAt.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm:ss"), 11, Muted, margin: new Thickness(0, 7, 0, 13)));
            if (!string.IsNullOrWhiteSpace(record.Before)) card.Children.Add(Text("Antes: " + record.Before, 13, Muted));
            if (!string.IsNullOrWhiteSpace(record.After)) card.Children.Add(Text("Resultado esperado: " + record.After, 13, Muted, margin: new Thickness(0, 6, 0, 9)));
            card.Children.Add(Text(record.Message, 13));
            if (record.Request.Kind == OperationKind.Maintenance && record.Request.TargetId.StartsWith("fivem", StringComparison.OrdinalIgnoreCase)) card.Children.Add(FeatureButton("Ver backups FiveM", () => { _gamesTab = "FiveM"; Navigate("games"); _ = RefreshFiveMBackupsAsync(); }));
            if (record.Request.Kind == OperationKind.Maintenance && record.Request.TargetId.StartsWith("session", StringComparison.OrdinalIgnoreCase)) card.Children.Add(FeatureButton("Ver sessões de jogo", () => { _gamesTab = "Sessões"; Navigate("games"); _ = RefreshSessionsAsync(); }));
            if (record.CanUndo) { var undo = Button("Revisar restauração", () => _ = UndoAsync(record)); undo.IsEnabled = !_busy; undo.HorizontalAlignment = HorizontalAlignment.Left; undo.Margin = new Thickness(0, 16, 0, 0); card.Children.Add(undo); }
            root.Children.Add(Card(card));
        }
        return root;
    }

    private UIElement BuildSettings()
    {
        var root = Stack(); var title = Stack(); title.Children.Add(Chip("PREFERÊNCIAS LOCAIS", AccentSoft));
        title.Children.Add(Text("O acabamento também é eficiência.", 25, weight: FontWeights.SemiBold, margin: new Thickness(0, 14, 0, 9)));
        title.Children.Add(Text("Vermelho e preto, com movimento ajustável. O primeiro diagnóstico é somente leitura; novas coletas são iniciadas por você.", 14, Muted)); root.Children.Add(Card(title, true));
        var motion = Stack(); motion.Children.Add(Text("Movimento e consumo", 18, weight: FontWeights.SemiBold, margin: new Thickness(0, 0, 0, 12)));
        var reduced = new CheckBox { Content = "Reduzir movimento", IsChecked = _reduceMotion };
        reduced.Checked += (_, _) => { _reduceMotion = true; SavePreferences(); UpdateSpinners(); }; reduced.Unchecked += (_, _) => { _reduceMotion = false; SavePreferences(); UpdateSpinners(); }; motion.Children.Add(reduced);
        motion.Children.Add(Text("Pausa órbitas e luzes dos circuitos e remove deslocamentos. Os dados e as ações continuam disponíveis.", 12, Muted, margin: new Thickness(28, 0, 0, 10)));
        var economy = new CheckBox { Content = "Modo visual econômico", IsChecked = _economy };
        economy.Checked += (_, _) => { _economy = true; SavePreferences(); UpdateSpinners(); }; economy.Unchecked += (_, _) => { _economy = false; SavePreferences(); UpdateSpinners(); }; motion.Children.Add(economy);
        motion.Children.Add(Text("Pausa todos os efeitos contínuos. Minimizar também pausa as animações. Medições iniciadas por você seguem o prazo escolhido e podem ser canceladas com Esc.", 12, Muted, margin: new Thickness(28, 0, 0, 0))); root.Children.Add(Card(motion));
        var privacy = Stack(); privacy.Children.Add(Text("Privacidade por padrão", 18, weight: FontWeights.SemiBold));
        privacy.Children.Add(Text("Sem conta, anúncios ou telemetria externa. Histórico e backups de configuração ficam neste perfil do Windows. Relatórios exportados incluem informações deste PC; revise antes de compartilhar.", 14, Muted, margin: new Thickness(0, 10, 0, 12)));
        privacy.Children.Add(Text(_backend.DataDirectory, 12, AccentSoft)); root.Children.Add(Card(privacy));
        var about = Stack(); about.Children.Add(Text(Brand.Name, 24, weight: FontWeights.SemiBold)); about.Children.Add(Text(Brand.Version + "  ·  Windows x64  ·  .NET / WPF", 12, AccentSoft, margin: new Thickness(0, 9, 0, 13)));
        about.Children.Add(Text("Ajustes com prévia, verificação e reversão; limpeza por arquivos revisados; jogos, presets FiveM e medições locais. FPS usa capturas PresentMon; scaling armazena preferências e integra componentes externos. Nenhum recurso promete aceleração sem medição.", 14, Muted));
        if (_snapshot?.IsAdministrator == false) about.Children.Add(FeatureButton("Reabrir como administrador", RelaunchAsAdministrator));
        if (_snapshot != null) about.Children.Add(Text($"Sistema observado: {_snapshot.WindowsName} · build {_snapshot.WindowsBuild}\nSessão: {(_snapshot.IsAdministrator ? "administrador" : "usuário comum")} · {(_snapshot.SupportedOS ? "alvo compatível" : "compatibilidade adicional não validada")}", 13, Muted, margin: new Thickness(0, 13, 0, 0)));
        about.Children.Add(Text("Distribuição comercial ainda requer assinatura, validação em máquinas virtuais e profiling de consumo. Não há certificado ou atualização automática configurados nesta prévia.", 12, Muted, margin: new Thickness(0, 13, 0, 0))); var licenses = Button("Licença das fontes Inter", ShowFontLicense); licenses.HorizontalAlignment = HorizontalAlignment.Left; licenses.Margin = new Thickness(0, 16, 0, 0); about.Children.Add(licenses); root.Children.Add(Card(about));
        root.Children.Add(ActionFooter("Abrir Windows Update", "ms-settings:windowsupdate")); return root;
    }

    private async Task AnalyzeAsync()
    {
        if (_busy) return;
        _busy = true; _stage = "Preparando coleta somente leitura…";
        _noticeBox.Visibility = Visibility.Collapsed;
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(message => { _stage = message; _analysisStatus.Text = "◌  " + message; if (_page == "overview") RenderPage(); });
        _analysisStatus.Text = "◌  " + _stage; RenderPage();
        try
        {
            var newSnapshot = await _backend.AnalyzeAsync(progress, _cts.Token);
            _previousSnapshot = _snapshot;
            _snapshot = newSnapshot;
            _selectedStartup.Clear();
            _stage = "Análise concluída";
            _analysisStatus.Text = $"●  Análise de {_snapshot.CapturedAt:HH:mm:ss} · {_snapshot.ObservationSeconds:0.#} s observados";
            _noticeBox.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { _stage = "Análise cancelada"; _analysisStatus.Text = "○  Análise cancelada · dados anteriores preservados"; Notice("Análise cancelada. Nenhuma configuração foi alterada."); }
        catch (Exception ex) { _stage = "Não foi possível concluir a análise"; _analysisStatus.Text = "○  Análise não concluída"; Notice("Não foi possível concluir a análise: " + ex.Message, true); }
        finally { _busy = false; _cts.Dispose(); _cts = null; RenderPage(); }
    }

    private async Task ReviewStartupAsync()
    {
        if (_snapshot == null) return;
        var requests = _snapshot.StartupItems.Where(i => _selectedStartup.Contains(i.Id)).Select(i => new OperationRequest(OperationKind.DisableStartup, i.Id)).ToArray();
        await ReviewRequestsAsync(requests);
    }

    private async Task ReviewRequestsAsync(IReadOnlyList<OperationRequest> requests)
    {
        if (_busy || requests.Count == 0 || _smoke) return;
        _busy = true; _cts = new CancellationTokenSource(); RenderPage();
        var previews = new List<OperationPreview>();
        try
        {
            foreach (var request in requests)
            {
                _stage = "Consultando prévia " + (previews.Count + 1) + " de " + requests.Count;
                _analysisStatus.Text = "◌  " + _stage;
                previews.Add(await _backend.PreviewAsync(request, _cts.Token));
            }
        }
        catch (OperationCanceledException) { Notice("Prévia cancelada. Nenhuma alteração realizada."); return; }
        catch (Exception ex) { Notice("Não foi possível preparar a prévia: " + ex.Message, true); return; }
        finally { _busy = false; _cts.Dispose(); _cts = null; RenderPage(); }
        previews = OperationBatch.PreventOverlaps(previews).ToList();
        if (!ConfirmPreviews(previews)) return;
        _busy = true; _cts = new CancellationTokenSource();
        var results = new List<OperationRecord>();
        try
        {
            foreach (var preview in previews.Where(p => p.CanApply))
            {
                _cts.Token.ThrowIfCancellationRequested();
                _stage = "Aplicando e verificando: " + preview.Title;
                _analysisStatus.Text = "◌  " + _stage;
                RenderPage();
                results.Add(await _backend.ApplyAsync(preview, _cts.Token));
            }
            _selectedStartup.Clear();
            var completed = results.Count(r => r.Status is OperationStatus.Completed or OperationStatus.RestartRequired);
            Notice($"{completed} de {results.Count} operações concluídas e verificadas. Confira o resultado individual no histórico. Execute uma nova análise para atualizar o retrato do PC.");
            _analysisStatus.Text = "●  Operações registradas · confira o histórico";
        }
        catch (OperationCanceledException) { Notice("Execução cancelada entre etapas. As ações já registradas podem ser consultadas no histórico."); _analysisStatus.Text = "○  Execução interrompida"; }
        catch (Exception ex) { Notice("A execução encontrou um erro: " + ex.Message + ". Consulte o histórico antes de tentar novamente.", true); }
        finally { _busy = false; _cts.Dispose(); _cts = null; await LoadHistoryAsync(); Navigate("history"); }
    }

    private bool ConfirmPreviews(IReadOnlyList<OperationPreview> previews)
    {
        var eligible = previews.Count(p => p.CanApply);
        var window = Dialog("Revise antes de aplicar", out var panel, out var footer);
        panel.Children.Add(Text("Você decide o que muda.", 26, weight: FontWeights.SemiBold, margin: new Thickness(0, 0, 0, 12)));
        panel.Children.Add(Text("A prévia ainda não alterou o Windows. Confira o estado anterior, o resultado esperado e os possíveis efeitos de cada ação.", 14, Muted, margin: new Thickness(0, 0, 0, 20)));
        foreach (var preview in previews)
        {
            var details = Stack(); details.Children.Add(Text(preview.Title, 17, weight: FontWeights.SemiBold));
            details.Children.Add(Text(preview.Explanation, 13, Muted, margin: new Thickness(0, 9, 0, 12)));
            details.Children.Add(Text("Antes: " + preview.Before, 13)); details.Children.Add(Text("Depois: " + preview.After, 13, margin: new Thickness(0, 7, 0, 7)));
            details.Children.Add(Text("Risco: " + preview.Risk + (preview.CanApply ? " · Estado anterior será registrado para reversão." : " · Não será aplicado."), 12, preview.CanApply ? Brush("#D9C4A0") : AccentSoft));
            if (!preview.CanApply) details.Children.Add(Text(preview.BlockReason ?? "Esta ação está indisponível neste contexto.", 13, Brush("#FFB6AE"), margin: new Thickness(0, 9, 0, 0)));
            if (!string.IsNullOrWhiteSpace(preview.SourceUrl)) { var source = Button("Consultar fonte técnica", () => OpenExternal(preview.SourceUrl)); source.HorizontalAlignment = HorizontalAlignment.Left; source.Margin = new Thickness(0, 12, 0, 0); details.Children.Add(source); }
            panel.Children.Add(Card(details));
        }
        var consent = new CheckBox { Content = new TextBlock { Text = "Revisei os efeitos e aprovo as operações disponíveis acima.", TextWrapping = TextWrapping.Wrap }, IsEnabled = eligible > 0, Margin = new Thickness(0, 8, 0, 10) };
        panel.Children.Add(consent);
        var apply = Button($"Aprovar e aplicar ({eligible})", () => window.DialogResult = true, true); apply.IsEnabled = false;
        consent.Checked += (_, _) => apply.IsEnabled = eligible > 0; consent.Unchecked += (_, _) => apply.IsEnabled = false;
        var cancel = Button("Voltar sem alterar", () => window.DialogResult = false); cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 12, 0);
        footer.Children.Add(cancel); footer.Children.Add(apply);
        return window.ShowDialog() == true;
    }

    private async Task UndoAsync(OperationRecord record)
    {
        if (_busy || _smoke) return;
        var window = Dialog("Revisar restauração", out var panel, out var footer);
        panel.Children.Add(Text("Restaurar esta configuração?", 25, weight: FontWeights.SemiBold, margin: new Thickness(0, 0, 0, 16)));
        panel.Children.Add(Text(record.Title, 18, AccentSoft));
        panel.Children.Add(Text("Estado a restaurar: " + record.Before, 14, margin: new Thickness(0, 14, 0, 14)));
        panel.Children.Add(Text("A configuração atual será verificada. Se outro programa ou usuário a alterou depois, a restauração será bloqueada e o conflito ficará no histórico. Seus arquivos pessoais não são afetados por esta reversão de configuração.", 14, Muted));
        var cancel = Button("Voltar", () => window.DialogResult = false); cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 12, 0); footer.Children.Add(cancel);
        footer.Children.Add(Button("Aprovar restauração", () => window.DialogResult = true, true));
        if (window.ShowDialog() != true) return;
        _busy = true; _cts = new CancellationTokenSource(); RenderPage();
        try
        {
            var result = await _backend.UndoAsync(record.Id, _cts.Token);
            Notice(StatusLabel(result.Status) + ": " + result.Message, result.Status is OperationStatus.Failed or OperationStatus.Conflict);
        }
        catch (OperationCanceledException) { Notice("Restauração interrompida. Confira o estado registrado antes de tentar de novo."); }
        catch (Exception ex) { Notice("Não foi possível restaurar: " + ex.Message, true); }
        finally { _busy = false; _cts.Dispose(); _cts = null; await LoadHistoryAsync(); RenderPage(); }
    }

    private Window Dialog(string title, out StackPanel panel, out StackPanel footer)
    {
        var window = new Window { Title = title, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Width = Math.Max(560, Math.Min(760, ActualWidth - 40)), Height = Math.Max(340, Math.Min(760, ActualHeight - 40)), MinWidth = 540, MinHeight = 340, Background = Brush(Palette.Panel), ShowInTaskbar = false };
        window.SourceInitialized += (_, _) => { try { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref dark, sizeof(int)); } catch { } };
        var dock = new DockPanel { Margin = new Thickness(28) };
        var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(156, 0, 0, 0)), IsHitTestVisible = true };
        Grid.SetRowSpan(scrim, 2); Grid.SetColumnSpan(scrim, 2); Panel.SetZIndex(scrim, 100); _shell.Children.Add(scrim);
        window.Closed += (_, _) => _shell.Children.Remove(scrim);
        window.Loaded += (_, _) => MotionTransitions.Show(dock, MotionAllowed && InputManager.Current.MostRecentInputDevice is not KeyboardDevice);
        footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
        panel = Stack(); dock.Children.Add(new ScrollViewer { Content = panel }); window.Content = dock; return window;
    }

    private async Task LoadHistoryAsync()
    {
        try { _history = await _backend.GetHistoryAsync(); }
        catch (Exception ex) { Notice("Não foi possível ler o histórico: " + ex.Message, true); }
    }

    private async Task RefreshHistoryPageAsync()
    {
        await LoadHistoryAsync(); if (_page == "history") RenderPage();
    }

    private void ExportReport()
    {
        if (_snapshot == null || _busy || _smoke) return;
        var dialog = new SaveFileDialog { Title = "Exportar relatório · contém dados deste PC", FileName = $"VL-Optimizer-{DateTime.Now:yyyyMMdd-HHmmss}.json", Filter = "Relatório JSON (*.json)|*.json", DefaultExt = ".json", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var report = new
            {
                Application = Brand.Name, Version = Brand.Version, ExportedAt = DateTimeOffset.Now,
                Privacy = "Contém informações de hardware, nomes de processos e comandos de inicialização. Revise antes de compartilhar.",
                Snapshot = _snapshot, PreviousSnapshot = _previousSnapshot, Recommendations = _backend.GetRecommendations(_snapshot),
                Operations = _history.Select(r => new { r.Id, r.CatalogVersion, r.Request, r.Title, r.StartedAt, r.FinishedAt, Status = StatusLabel(r.Status), r.Before, r.After, r.Message, r.CanUndo }),
                MeasurementContext = "Amostra somente leitura. Não comprova ganho de desempenho. Comparações exigem carga e condições equivalentes."
            };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Notice("Relatório exportado. Ele contém informações deste PC, incluindo comandos de inicialização; revise antes de compartilhar.");
        }
        catch (Exception ex) { Notice("Não foi possível exportar: " + ex.Message, true); }
    }

    private void SearchNavigate()
    {
        if (_search.Text.Trim().Length == 0) return;
        Navigate("search", false);
    }

    private bool Matches(string value) => string.IsNullOrWhiteSpace(_search.Text) || Contains(value, _search.Text.Trim());
    private static bool Contains(string value, string query) => query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(term => CultureInfo.GetCultureInfo("pt-BR").CompareInfo.IndexOf(value, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
    private void ScrollToRecommendations()
    {
        if (_page != "overview") Navigate("overview");
        if (VisualTreeHelper.GetParent(_pageHost) is ScrollContentPresenter presenter && VisualTreeHelper.GetParent(presenter) is ScrollViewer scroll) scroll.ScrollToEnd();
        else { var parent = VisualTreeHelper.GetParent(_pageHost); while (parent != null && parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent); (parent as ScrollViewer)?.ScrollToEnd(); }
    }

    private void ScrollToPageStart()
    {
        var parent = VisualTreeHelper.GetParent(_pageHost);
        while (parent != null && parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
        (parent as ScrollViewer)?.ScrollToTop();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { e.Handled = true; if (!_busy) _ = AnalyzeAsync(); }
        else if (e.Key == Key.Escape) { if (_busy) _cts?.Cancel(); else if (_search.Text.Length > 0) _search.Clear(); e.Handled = true; }
        else if ((e.Key == Key.F || e.Key == Key.K) && Keyboard.Modifiers == ModifierKeys.Control) { _search.Focus(); _search.SelectAll(); e.Handled = true; }
    }

    private void Notice(string message, bool error = false)
    {
        _noticeText.Text = message; _noticeText.Foreground = error ? Brush("#FFCCC5") : Brush(Palette.Text);
        _noticeBox.BorderBrush = error ? Brush("#713C43") : Brush(Palette.Line); _noticeBox.Visibility = Visibility.Visible;
    }

    private void OpenExternal(string target)
    {
        if (_smoke) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Notice("Não foi possível abrir este recurso do Windows: " + ex.Message, true); }
    }

    private bool MotionAllowed => !_reduceMotion && !_economy && SystemParameters.ClientAreaAnimation && WindowState != WindowState.Minimized;
    private void UpdateSpinners()
    {
        foreach (var spinner in _spinners)
        {
            spinner.BeginAnimation(RotateTransform.AngleProperty, null);
            if (_busy && MotionAllowed) spinner.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(1400)) { RepeatBehavior = RepeatBehavior.Forever });
            else spinner.Angle = 0;
        }
        UpdateSceneMotion();
        if (!MotionAllowed) MotionTransitions.Show(_pageHost, false);
    }

    private string PreferencesFile => Path.Combine(_backend.DataDirectory, "interface.json");
    private void LoadPreferences()
    {
        try
        {
            if (!File.Exists(PreferencesFile)) return;
            var data = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(PreferencesFile));
            _economy = data?.GetValueOrDefault("economy") ?? false; _reduceMotion = data?.GetValueOrDefault("reduceMotion") ?? false;
        }
        catch { /* Preferences are optional; a damaged file cannot block diagnosis. */ }
    }
    private void SavePreferences()
    {
        if (_smoke) return;
        try { Directory.CreateDirectory(_backend.DataDirectory); File.WriteAllText(PreferencesFile, JsonSerializer.Serialize(new { economy = _economy, reduceMotion = _reduceMotion })); }
        catch (Exception ex) { Notice("A preferência vale nesta sessão, mas não pôde ser salva: " + ex.Message, true); }
    }

    private void SetDarkTitleBar()
    {
        try { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int)); } catch { }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private async Task RunSmokeAsync()
    {
        try
        {
            var index = Array.IndexOf(_args, "--screenshot-dir");
            var directory = index >= 0 && index + 1 < _args.Length ? Path.GetFullPath(_args[index + 1]) : Path.Combine(AppContext.BaseDirectory, "private", "smoke-screenshots");
            Directory.CreateDirectory(directory);
            _reduceMotion = true; Width = 1366; Height = 768; WindowState = WindowState.Normal;
            Navigate("overview", false); await CaptureAsync(Path.Combine(directory, "01-overview-before-analysis.png"));
            await AnalyzeAsync();
            await RefreshScalingAsync();
            foreach (var page in ProductPages.All.Select(p => p.Id).Concat(new[] { "catalog", "power", "measurements", "processes" }))
            {
                Navigate(page, false); await CaptureAsync(Path.Combine(directory, "02-" + page + "-1366.png"));
            }
            Navigate("measurements", false);
            foreach (var tab in new[] { "FPS", "Diagnóstico", "Memória", "Disco", "Programas", "Manutenção", "Hardware" })
            {
                _measurementTab = tab; RenderPage(); await CaptureAsync(Path.Combine(directory, "02-measurements-" + tab + ".png"));
            }
            _measurementTab = "FPS";
            Navigate("games", false);
            foreach (var tab in new[] { "Bibliotecas", "Sessões", "FiveM" })
            {
                _gamesTab = tab; RenderPage(); await CaptureAsync(Path.Combine(directory, "02-games-" + tab + ".png"));
            }
            _gamesTab = "Bibliotecas";
            foreach (var (area, tabs) in new[] {
                ("performance", new[] { "Ajustes", "Energia", "Apps em segundo plano", "Orientações" }),
                ("input", new[] { "Ajustes", "Eventos do mouse" }),
                ("memory", new[] { "RAM e standby", "Processos", "Memória virtual" }),
                ("storage", new[] { "Arquivos e Lixeira", "Componentes e hibernação", "Ocupação de pastas" }),
                ("network", new[] { "DNS e placa de rede", "Reparos de rede", "OBS e transmissão" }),
                ("windows", new[] { "Preferências", "Reparos e proteção", "Histórico e backups" }),
                ("graphics", new[] { "DLSS e tecnologias", "OptiScaler" }),
                ("fps", new[] { "FPS e frametimes", "Diagnóstico da sessão" }) })
            {
                Navigate(area, false);
                foreach (string tab in tabs) { _areaTabs[area] = tab; RenderPage(); await CaptureAsync(Path.Combine(directory, "area-" + area + "-" + tab + ".png")); }
                _areaTabs[area] = tabs[0];
            }
            var overlayProbe = new FpsOverlayWindow { Opacity = 0 }; overlayProbe.Show(); bool overlayStylesVerified = overlayProbe.ClickThroughVerified; overlayProbe.Close();
            if (!overlayStylesVerified) throw new InvalidOperationException("Overlay não confirmou seus estilos de entrada.");
            Width = 683; Height = 384; Navigate("overview", false); await CaptureAsync(Path.Combine(directory, "03-overview-compact.png"));
            Width = 1536; Height = 884; Navigate("overview", false); await CaptureAsync(Path.Combine(directory, "04-overview-wide.png"));
            _search.Text = "energia"; SearchNavigate(); await CaptureAsync(Path.Combine(directory, "05-search.png")); _search.Clear();
            await AnalyzeAsync(); Navigate("overview", false);
            await CaptureAsync(Path.Combine(directory, "06-overview-comparison.png"));
            _search.Clear(); Navigate("overview", false);
            var visibleProfile = await MeasureIdleAsync();
            WindowState = WindowState.Minimized;
            var minimizedProfile = await MeasureIdleAsync();
            WindowState = WindowState.Normal;
            File.WriteAllText(Path.Combine(directory, "smoke-result.json"), JsonSerializer.Serialize(new { Success = _snapshot != null, ReadOnly = true, NavigationSucceeded = true, Pages = _navButtons.Keys.ToArray(), OverlayStylesVerified = overlayStylesVerified, Snapshot = _snapshot, PreviousSnapshot = _previousSnapshot, HistoryCount = _history.Count, MotionReduced = true, VisibleIdle = visibleProfile, MinimizedIdle = minimizedProfile, ProfilingNote = "Amostra de repouso de 3 segundos por estado; não valida fluidez, FPS ou consumo em outras máquinas." }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(_snapshot == null ? 2 : 0);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-error.txt"), ex.ToString()); } catch { }
            Application.Current.Shutdown(3);
        }
    }

    private async Task<object> MeasureIdleAsync()
    {
        // Let resizing, PNG capture and first paint settle before measuring idle CPU.
        await Task.Delay(1500);
        using var process = Process.GetCurrentProcess(); process.Refresh();
        var cpuBefore = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
        await Task.Delay(3000); process.Refresh(); watch.Stop();
        return new { ObservationSeconds = watch.Elapsed.TotalSeconds, WorkingSetBytes = process.WorkingSet64, PrivateMemoryBytes = process.PrivateMemorySize64, NormalizedCpuPercent = (process.TotalProcessorTime - cpuBefore).TotalSeconds / watch.Elapsed.TotalSeconds / Environment.ProcessorCount * 100, LogicalProcessors = Environment.ProcessorCount };
    }

    private async Task CaptureAsync(string file)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        // Render at logical 96 DPI. Additional Windows DPI runs can use the same smoke entry point.
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(_shell.ActualWidth), (int)Math.Ceiling(_shell.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_shell); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(file); encoder.Save(stream);
    }

    private static StackPanel Stack() => new() { Orientation = Orientation.Vertical };
    private static TextBlock Text(string text, double size = 14, Brush? color = null, FontWeight? weight = null, Thickness? margin = null) => new() { Text = text, FontSize = size, FontFamily = new FontFamily(new Uri("pack://application:,,,/"), size >= 20 && weight == FontWeights.Bold ? "./Assets/Fonts/#Inter Display" : "./Assets/Fonts/#Inter"), Foreground = color ?? Brush(Palette.Text), FontWeight = weight ?? FontWeights.Normal, Margin = margin ?? new Thickness(0), TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.42 };
    private static TextBlock Glyph(string glyph, double size = 20, Brush? color = null) => new() { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size, Foreground = color ?? AccentSoft, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };

    private static Button Button(object content, Action action, bool primary = false)
    {
        var button = new Button { Content = content }; if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
        button.Click += (_, _) => action();
        MotionFeedback.Attach(button, () => {
            var window = Window.GetWindow(button);
            return (window as MainWindow ?? window?.Owner as MainWindow)?.MotionAllowed == true
                && !System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(button);
        });
        return button;
    }
    private static Button Button(string glyph, string text, Action action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(Glyph(glyph, 15, Muted)); row.Children.Add(Text(text, 12, margin: new Thickness(8, 0, 0, 0)));
        var button = Button(row, action); AutomationProperties.SetName(button, text); return button;
    }
    private static Border Card(UIElement content, bool highlight = false) => new()
    {
        Background = highlight ? new LinearGradientBrush((Color)ColorConverter.ConvertFromString(Palette.Raised), (Color)ColorConverter.ConvertFromString(Palette.Panel), 25) : Brush(Palette.Panel),
        BorderBrush = highlight ? Brush(Palette.AccentDeep) : Brush(Palette.Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(23), Margin = new Thickness(0, 0, 0, 12), Child = content
    };
    private static UIElement Chip(string text, Brush color)
    {
        var label = Text(text, 10, color, FontWeights.SemiBold); label.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Child = label, Padding = new Thickness(9, 5, 9, 5), Background = Brush(Palette.Raised), BorderBrush = Brush(Palette.Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left };
    }
    private static UIElement SectionTitle(string title, string subtitle)
    {
        var panel = new Grid { Margin = new Thickness(0, 16, 0, 15) }; panel.ColumnDefinitions.Add(new ColumnDefinition()); panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.Children.Add(Text(title, 18, weight: FontWeights.SemiBold)); var sub = Text(subtitle, 11, Muted, margin: new Thickness(16, 6, 0, 0)); Grid.SetColumn(sub, 1); panel.Children.Add(sub); return panel;
    }
    private static UIElement InlineNote(string title, string message, string glyph)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = Glyph(glyph, 22); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new Thickness(0, 3, 0, 0); grid.Children.Add(icon);
        var text = Stack(); text.Children.Add(Text(title, 16, weight: FontWeights.SemiBold)); text.Children.Add(Text(message, 13, Muted, margin: new Thickness(0, 8, 0, 0))); Grid.SetColumn(text, 1); grid.Children.Add(text); return Card(grid);
    }
    private static UIElement Empty(string glyph, string title, string message)
    {
        var grid = new Grid { Margin = new Thickness(3, 7, 3, 7) }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = Glyph(glyph, 27); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new Thickness(0, 4, 0, 0); grid.Children.Add(icon);
        var details = Stack(); details.Children.Add(Text(title, 17, weight: FontWeights.SemiBold)); details.Children.Add(Text(message, 13, Muted, margin: new Thickness(0, 8, 0, 0))); Grid.SetColumn(details, 1); grid.Children.Add(details); return Card(grid);
    }
    private UIElement AnalyzeEmpty()
    {
        var content = Stack();
        content.Children.Add(Chip(_busy ? "DIAGNÓSTICO EM ANDAMENTO" : "COMECE PELO DIAGNÓSTICO", AccentSoft));
        content.Children.Add(Text(_busy ? "Conhecendo seu computador." : "Uma leitura antes de escolher.", 23, weight: FontWeights.Bold, margin: new Thickness(0, 17, 0, 12)));
        content.Children.Add(Text(_busy ? _stage : "A coleta identifica hardware, consumo e recursos disponíveis. Ela não aplica ajustes e leva cerca de cinco segundos.", 14, Muted, margin: new Thickness(0, 0, 0, 20)));
        var action = Button(_busy ? "Cancelar análise" : "Analisar meu PC", () => { if (_busy) _cts?.Cancel(); else _ = AnalyzeAsync(); }, true);
        action.HorizontalAlignment = HorizontalAlignment.Left; content.Children.Add(action);
        var card = Card(content, true); card.MaxWidth = 760; card.HorizontalAlignment = HorizontalAlignment.Center;
        return card;
    }

    private UIElement Category(string glyph, string title, string subtitle, string page)
    {
        var content = Stack(); content.Children.Add(Glyph(glyph, 22)); content.Children.Add(Text(title, 16, weight: FontWeights.SemiBold, margin: new Thickness(0, 13, 0, 6))); content.Children.Add(Text(subtitle, 12, Muted));
        var button = Button(content, () => Navigate(page)); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Background = Brush(Palette.Raised); button.Padding = new Thickness(19); button.Margin = new Thickness(5, 0, 5, 8); AutomationProperties.SetName(button, $"{title}: {subtitle}"); return button;
    }
    private static UIElement Metric(string glyph, string title, string? value, string subtitle, double? percent = null, IReadOnlyList<double>? samples = null)
    {
        var content = Stack(); var label = new StackPanel { Orientation = Orientation.Horizontal }; label.Children.Add(Glyph(glyph, 16)); label.Children.Add(Text(title, 10, Muted, FontWeights.SemiBold, new Thickness(8, 0, 0, 0))); content.Children.Add(label);
        content.Children.Add(Text(value ?? "Aguardando análise", 15, value == null ? Muted : null, FontWeights.SemiBold, new Thickness(0, 14, 0, 8)));
        content.Children.Add(Text(subtitle, 11, Muted));
        if (samples?.Count > 1)
        {
            var canvas = new Canvas { Height = 26, ClipToBounds = true, Margin = new Thickness(0, 12, 0, 0) };
            canvas.SizeChanged += (_, _) => { canvas.Children.Clear(); var line = new Polyline { Stroke = AccentSoft, StrokeThickness = 1.5 }; for (int i = 0; i < samples.Count; i++) line.Points.Add(new Point(i * canvas.ActualWidth / (samples.Count - 1), 24 - Math.Clamp(samples[i], 0, 100) / 100 * 22)); canvas.Children.Add(line); };
            AutomationProperties.SetName(canvas, "Amostras reais de CPU. Uso médio descrito acima."); content.Children.Add(canvas);
        }
        else if (percent.HasValue) { var bar = Bar(percent.Value); ((FrameworkElement)bar).Margin = new Thickness(0, 15, 0, 0); content.Children.Add(bar); }
        var card = Card(content); card.Padding = new Thickness(18); card.Margin = new Thickness(5, 0, 5, 8); return card;
    }
    private static UIElement Bar(double value) => new ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Clamp(value, 0, 100), Height = 4, Foreground = AccentSoft, Background = Brush(Palette.Line), BorderThickness = new Thickness(0), IsHitTestVisible = false };
    private UIElement RecommendationCard(Recommendation recommendation)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = Stack(); text.Children.Add(Text(recommendation.Category.ToUpperInvariant() + "  /  " + recommendation.Priority.ToUpperInvariant(), 10, AccentSoft, FontWeights.SemiBold));
        text.Children.Add(Text(recommendation.Title, 16, weight: FontWeights.SemiBold, margin: new Thickness(0, 8, 20, 9)));
        var expander = new Expander { Header = "Entender a recomendação", Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 0, 18, 0) };
        var details = Stack(); details.Children.Add(Text(recommendation.Explanation, 13, Muted, margin: new Thickness(0, 9, 0, 10)));
        if (!string.IsNullOrWhiteSpace(recommendation.SourceUrl)) { var source = Button("Consultar fonte", () => OpenExternal(recommendation.SourceUrl)); source.HorizontalAlignment = HorizontalAlignment.Left; details.Children.Add(source); }
        expander.Content = details; text.Children.Add(expander); grid.Children.Add(text);
        if (recommendation.TargetPage != null)
        {
            var target = recommendation.TargetPage;
            var review = Button("Revisar  →", () => Navigate(NormalizePage(target))); review.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(review, 1); grid.Children.Add(review);
        }
        return Card(grid);
    }
    private static string NormalizePage(string value) => ProductPages.Find(value.ToLowerInvariant())?.Id ?? (value.ToLowerInvariant() switch { "energy" or "energia" => "power", "inicializacao" or "inicialização" or "initialization" => "startup", "armazenamento" or "cleanup" => "storage", "processos" => "processes", "historico" or "histórico" => "history", "configurações" or "configuracoes" => "settings", "ajustes" => "catalog", "fps" or "diagnostics" => "measurements", "power" or "startup" or "storage" or "processes" or "history" or "settings" or "catalog" or "games" or "scaling" or "measurements" => value.ToLowerInvariant(), _ => "overview" });
    private UIElement ActionFooter(string label, string target)
    {
        var panel = Stack(); var button = Button(label + "  ↗", () => OpenExternal(target)); button.HorizontalAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 8, 0, 6); panel.Children.Add(button);
        panel.Children.Add(Text("Abre um recurso do Windows. Alterações feitas lá ficam fora do histórico deste aplicativo.", 11, Muted)); return panel;
    }
    private static string Bytes(ulong value) => Bytes((double)value);
    private static string CpuLabel(double? value) => value is double percent ? $"{percent:0.#}%" : "indisponível";
    private static string MemoryAvailableLabel(SystemSnapshot snapshot) => snapshot.TotalMemoryBytes > 0 ? Bytes(snapshot.AvailableMemoryBytes) : "Não disponível";
    private static string Bytes(long value) => Bytes((double)value);
    private static string Bytes(double value)
    {
        var units = new[] { "B", "KB", "MB", "GB", "TB" }; int unit = 0; while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; } return $"{value:0.#} {units[unit]}";
    }
    private static string StatusLabel(OperationStatus status) => status switch
    {
        OperationStatus.Pending => "Pendente", OperationStatus.Completed => "Concluído", OperationStatus.PartiallyCompleted => "Concluído parcialmente", OperationStatus.Skipped => "Ignorado", OperationStatus.Failed => "Falhou", OperationStatus.RestartRequired => "Reinicialização necessária", OperationStatus.Unverified => "Resultado não verificado", OperationStatus.Reverted => "Restaurado", OperationStatus.Conflict => "Conflito detectado", _ => "Não disponível"
    };
    private static Brush StatusColor(OperationStatus status) => status switch { OperationStatus.Completed or OperationStatus.Reverted => Accent, OperationStatus.Failed or OperationStatus.Conflict => Brush("#FFB5AA"), _ => AccentSoft };
}

