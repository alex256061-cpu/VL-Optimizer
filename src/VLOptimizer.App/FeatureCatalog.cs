using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private readonly HashSet<string> _selectedConfigurations = [];
    private string _configurationCategory = "Todas";
    private Button? _reviewConfigurations;
    private readonly Dictionary<string, Button> _configurationCategoryButtons = [];
    private HashSet<string>? _activeConfigurationScope;

    private UIElement BuildConfigurationCatalog(IReadOnlyList<string>? scope = null)
    {
        var root = Stack();
        root.Children.Add(InlineNote("Uma mudança por motivo.", "A seleção prepara uma revisão. O estado atual, os efeitos, as permissões e a reversão são conferidos antes de escrever. Uma opção de ajuste não representa um ganho de FPS medido.", "\uE8F1"));
        if (_snapshot?.IsAdministrator == false) root.Children.Add(FeatureButton("Reabrir como administrador", RelaunchAsAdministrator));
        var definitions = OptimizationCatalog.All.Where(d => scope == null || scope.Contains(d.Id)).ToArray();
        _activeConfigurationScope = scope?.ToHashSet(StringComparer.Ordinal);
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        _configurationCategoryButtons.Clear();
        foreach (var category in new[] { "Todas" }.Concat(definitions.Select(d => d.Category).Distinct().OrderBy(x => x)))
        {
            var selected = category == _configurationCategory;
            var button = Button(category, () => { var byKeyboard = InputManager.Current.MostRecentInputDevice is KeyboardDevice; _configurationCategory = category; RenderPage(); if (byKeyboard && _configurationCategoryButtons.TryGetValue(category, out var focus)) focus.Focus(); });
            button.Background = selected ? Brush(Palette.AccentDeep) : Brush(Palette.Panel);
            button.BorderBrush = selected ? AccentSoft : Brush(Palette.Line);
            button.FontSize = 12; button.Padding = new Thickness(12, 8, 12, 8); button.Margin = new Thickness(0, 0, 8, 8);
            AutomationProperties.SetHelpText(button, selected ? "Categoria selecionada" : "Filtrar categoria");
            toolbar.Children.Add(button);
            _configurationCategoryButtons[category] = button;
        }
        if (scope == null) root.Children.Add(toolbar);
        var visible = definitions.Where(d => (scope != null || _configurationCategory == "Todas" || d.Category == _configurationCategory) && Matches(d.Title + " " + d.Description + " " + d.Category + " " + d.Id)).ToArray();
        var selection = new Grid { Margin = new Thickness(0, 0, 0, 14) }; selection.ColumnDefinitions.Add(new ColumnDefinition()); selection.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selection.Children.Add(Text($"{visible.Length} ajustes encontrados · {definitions.Length} nesta área", 12, Muted, margin: new Thickness(0, 12, 15, 0)));
        _reviewConfigurations = Button("Revisar seleção", () => _ = ReviewConfigurationSelectionAsync(), true);
        UpdateConfigurationSelection(); Grid.SetColumn(_reviewConfigurations, 1); selection.Children.Add(_reviewConfigurations); root.Children.Add(selection);
        if (visible.Length == 0) root.Children.Add(Empty("\uE721", "Nenhum ajuste para esta busca.", "Experimente outro termo ou escolha Todas. A busca considera título, categoria e descrição."));
        foreach (var definition in visible)
        {
            var card = Stack();
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var copy = Stack(); copy.Children.Add(Text(definition.Category.ToUpperInvariant(), 10, AccentSoft, FontWeights.SemiBold));
            copy.Children.Add(Text(definition.Title, 17, weight: FontWeights.SemiBold, margin: new Thickness(0, 5, 14, 7)));
            copy.Children.Add(Text(definition.Description, 13, Muted, margin: new Thickness(0, 0, 14, 0))); header.Children.Add(copy);
            var toggle = new CheckBox { Content = _selectedConfigurations.Contains(definition.Id) ? "Selecionado para revisão" : "Selecionar para revisão", IsChecked = _selectedConfigurations.Contains(definition.Id), IsEnabled = !_busy, VerticalAlignment = VerticalAlignment.Top, FontSize = 11, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Esta seleção não representa o estado atual do Windows" };
            toggle.SetResourceReference(StyleProperty, "ReviewSelection"); AutomationProperties.SetName(toggle, "Selecionar " + definition.Title + " para revisão");
            toggle.Checked += (_, _) => { _selectedConfigurations.Add(definition.Id); toggle.Content = "Selecionado para revisão"; UpdateConfigurationSelection(); };
            toggle.Unchecked += (_, _) => { _selectedConfigurations.Remove(definition.Id); toggle.Content = "Selecionar para revisão"; UpdateConfigurationSelection(); };
            Grid.SetColumn(toggle, 1); header.Children.Add(toggle); card.Children.Add(header);
            var last = _history.Where(h => h.Request.Kind == OperationKind.Configure && h.Request.TargetId == definition.Id).OrderByDescending(h => h.StartedAt).FirstOrDefault();
            if (last != null) card.Children.Add(Text($"Última operação: {StatusLabel(last.Status)} · {last.StartedAt.ToLocalTime():dd/MM HH:mm}. Consulte o estado para conferir alterações externas.", 11, Muted, margin: new Thickness(0, 10, 0, 0)));
            var details = Stack();
            if (!string.IsNullOrWhiteSpace(definition.Warning)) details.Children.Add(Text(definition.Warning, 12, AccentSoft, margin: new Thickness(0, 9, 0, 9)));
            if (!string.IsNullOrWhiteSpace(definition.Restart)) details.Children.Add(Text("A alteração pode exigir reiniciar o Windows, a sessão ou o adaptador para produzir efeito. Consulte a prévia.", 12, Muted, margin: new Thickness(0, 0, 0, 9)));
            if (!string.IsNullOrWhiteSpace(definition.UnsupportedReason)) details.Children.Add(Text("Dependência indisponível: " + definition.UnsupportedReason, 12, AccentSoft, margin: new Thickness(0, 0, 0, 9)));
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            actions.Children.Add(FeatureButton("Consultar estado", () => _ = InspectConfigurationAsync(definition.Id)));
            actions.Children.Add(FeatureButton("Revisar ativação", () => _ = ReviewRequestsAsync([new OperationRequest(OperationKind.Configure, definition.Id, true)])));
            if (definition.AllowDisable) actions.Children.Add(FeatureButton("Revisar desativação", () => _ = ReviewRequestsAsync([new OperationRequest(OperationKind.Configure, definition.Id, false)])));
            details.Children.Add(actions);
            details.Children.Add(Text("Desativar aplica os valores previstos pelo catálogo. Para recuperar exatamente o valor anterior, use Restaurar no histórico.", 11, Muted, margin: new Thickness(0, 4, 0, 0)));
            var expander = new Expander { Header = "Efeitos, consulta e opções", Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 12, 0, 0), Content = details };
            AutomationProperties.SetName(expander, "Opções de " + definition.Title); card.Children.Add(expander);
            root.Children.Add(Card(card));
        }
        return root;
    }

    private void UpdateConfigurationSelection()
    {
        if (_reviewConfigurations == null) return;
        int count = _selectedConfigurations.Count(id => _activeConfigurationScope == null || _activeConfigurationScope.Contains(id));
        _reviewConfigurations.Content = $"Revisar seleção ({count})";
        _reviewConfigurations.IsEnabled = count > 0 && !_busy;
    }

    private async Task ReviewConfigurationSelectionAsync()
    {
        var selected = _selectedConfigurations.Where(id => _activeConfigurationScope == null || _activeConfigurationScope.Contains(id)).ToArray();
        await ReviewRequestsAsync(selected.Select(id => new OperationRequest(OperationKind.Configure, id, true)).ToArray());
        foreach (string id in selected) _selectedConfigurations.Remove(id);
    }

    private async Task InspectConfigurationAsync(string id)
    {
        OperationPreview? preview = null;
        await RunFeatureAsync("Consultando configuração…", async token => preview = await _backend.PreviewAsync(new OperationRequest(OperationKind.Configure, id, true), token));
        if (preview == null) return;
        var window = Dialog("Estado atual · " + preview.Title, out var panel, out var footer);
        panel.Children.Add(Text(preview.Title, 24, weight: FontWeights.Bold));
        panel.Children.Add(Text(preview.Explanation, 14, Muted, margin: new Thickness(0, 12, 0, 18)));
        panel.Children.Add(Text("Estado consultado", 12, AccentSoft, FontWeights.SemiBold));
        panel.Children.Add(Text(preview.Before, 13, margin: new Thickness(0, 8, 0, 18)));
        panel.Children.Add(Text("Efeito previsto ao ativar", 12, AccentSoft, FontWeights.SemiBold));
        panel.Children.Add(Text(preview.After, 13, margin: new Thickness(0, 8, 0, 18)));
        panel.Children.Add(Text(preview.CanApply ? "Disponível para revisão nesta sessão." : preview.BlockReason ?? "Indisponível nesta sessão.", 13, Muted));
        footer.Children.Add(Button("Fechar consulta", () => window.DialogResult = true)); window.ShowDialog();
    }

    private Button FeatureButton(string label, Action action, bool primary = false)
    {
        var button = Button(label, action, primary); button.HorizontalAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 0, 9, 9); button.IsEnabled = !_busy; button.FontSize = 12; button.Padding = new Thickness(13, 9, 13, 9); AutomationProperties.SetName(button, label); return button;
    }
    private readonly Dictionary<string, Button> _featureTabs = [];
    private UIElement FeatureTabBar(string[] tabs, string selected, Action<string> changed)
    {
        _featureTabs.Clear(); var panel = new WrapPanel();
        foreach (var tab in tabs)
        {
            var button = FeatureButton(tab, () => { bool byKeyboard = InputManager.Current.MostRecentInputDevice is KeyboardDevice; changed(tab); RenderPage(); if (byKeyboard && _featureTabs.TryGetValue(tab, out var focus)) focus.Focus(); });
            if (tab == selected) { button.BorderBrush = AccentSoft; button.BorderThickness = new Thickness(2); button.Background = Brush(Palette.AccentDeep); AutomationProperties.SetItemStatus(button, "Selecionada"); }
            _featureTabs[tab] = button; panel.Children.Add(button);
        }
        return panel;
    }

    private UIElement BuildSearchResults()
    {
        var root = Stack(); var query = _search.Text.Trim();
        if (query.Length == 0) { root.Children.Add(Empty("\uE721", "O que você quer ajustar?", "Digite um ajuste, jogo ou ferramenta na busca. Ctrl K leva o foco ao campo.")); return root; }
        var results = 0;
        var pages = new[] {
            ("catalog", "Ajustes do Windows", "desempenho mouse teclado rede privacidade serviços aparência jogos registro"),
            ("power", "Energia", "planos bateria tomada consumo autonomia"),
            ("startup", "Inicialização", "aplicativos iniciar windows startup"),
            ("storage", "Limpeza e armazenamento", "temporários cache disco espaço arquivos varredura"),
            ("games", "Jogos e FiveM", "steam epic gta perfis presets backup sessão prioridade automático monitor energia"),
            ("measurements", "Medições e diagnóstico", "fps frametime lows gargalo cpu gpu memória presentmon comparar"),
            ("scaling", "Scaling e perfis", "upscaler optiscaler resolução escala jogo motor externo"),
            ("processes", "Memória e processos", "ram consumo pid aplicativos"),
            ("history", "Histórico e restauração", "operações desfazer reversão recuperar backup"),
            ("settings", "Preferências", "movimento tema animação privacidade versão licença") };
        var navigation = ProductPages.All.Select(p => (p.Id, p.Title, p.Description)).Concat(pages.Where(p => ProductPages.Find(p.Item1) == null));
        foreach (var (page, title, terms) in navigation.Where(p => Contains(p.Item2 + " " + p.Item3, query)))
        { root.Children.Add(SearchResult("PÁGINA", title, terms, () => Navigate(page))); results++; }
        foreach (var definition in OptimizationCatalog.All.Where(d => Contains(d.Id + " " + d.Title + " " + d.Category + " " + d.Description, query)))
        { root.Children.Add(SearchResult("AJUSTE · " + definition.Category.ToUpperInvariant(), definition.Title, definition.Description, () => NavigateToConfiguration(definition.Id))); results++; }
        foreach (var definition in Maintenance.GetDefinitions().Where(d => Contains(d.Title + " " + d.Description, query)))
        { root.Children.Add(SearchResult("MANUTENÇÃO", definition.Title, definition.Description, () => { _measurementTab = "Manutenção"; Navigate("measurements"); })); results++; }
        foreach (var (tab, title, terms) in new[] { ("Hardware", "Inventário de hardware", "BIOS placa mãe memória slots GPU driver processador vídeo"), ("Memória", "Memória e standby", "ram cache standby purga comprometida"), ("Disco", "Tamanho das pastas", "varredura arquivos grandes tamanhos"), ("Programas", "Programas e desinstalação", "aplicativos uninstall desinstalar sobras") }.Where(t => Contains(t.Item2 + " " + t.Item3, query)))
        { root.Children.Add(SearchResult("FERRAMENTA", title, terms, () => { _measurementTab = tab; Navigate("measurements"); })); results++; }
        foreach (var app in _installedApps?.Where(a => Contains(a.Name + " " + a.Publisher, query)) ?? [])
        { root.Children.Add(SearchResult("PROGRAMA INSTALADO", app.Name, app.Publisher ?? "Fabricante não informado", () => { _measurementTab = "Programas"; Navigate("measurements"); })); results++; }
        if (_snapshot != null)
        {
            foreach (var item in _snapshot.StartupItems.Where(i => Contains(i.Name + " " + i.Source + " " + i.Command, query)))
            { root.Children.Add(SearchResult("INICIALIZAÇÃO", item.Name, item.Source, () => Navigate("startup"))); results++; }
            foreach (var process in _snapshot.TopProcesses.Where(p => Contains(p.Name + " " + p.Id, query)))
            { root.Children.Add(SearchResult("PROCESSO OBSERVADO", process.Name, $"PID {process.Id} · {Bytes(process.MemoryBytes)} de memória residente", () => Navigate("processes"))); results++; }
        }
        foreach (var game in _gameDiscovery?.Games.Where(g => Contains(g.Name + " " + g.Source, query)) ?? [])
        { root.Children.Add(SearchResult("JOGO INSTALADO", game.Name, game.InstallDirectory, () => Navigate("games"))); results++; }
        if (results == 0) root.Children.Add(Empty("\uE721", "Nenhum resultado encontrado.", "Experimente termos como mouse, energia, cache, FiveM ou FPS. Jogos aparecem após a busca de instalações locais."));
        else root.Children.Insert(0, Text($"{results} resultados para “{query}”", 13, Muted, margin: new Thickness(0, 0, 0, 15)));
        return root;
    }

    private UIElement SearchResult(string category, string title, string description, Action action)
    {
        var panel = Stack(); panel.Children.Add(Text(category, 10, AccentSoft, FontWeights.SemiBold));
        panel.Children.Add(Text(title, 17, weight: FontWeights.SemiBold, margin: new Thickness(0, 6, 0, 6)));
        panel.Children.Add(Text(description, 12, Muted));
        var button = Button(panel, action); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Padding = new Thickness(19); button.Background = Brush(Palette.Panel); button.Margin = new Thickness(0, 0, 0, 10);
        AutomationProperties.SetName(button, title + ". " + description); return button;
    }
}
