using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private readonly TextBlock _compatibility = new();
    private readonly Dictionary<string, Border> _navMarkers = [];
    private Button? _exportTitleButton;
    private bool _railExpanded;
    private bool _railKeyboardNavigation;
    private const double RailWidth = 72;
    private const double ExpandedRailWidth = 264;
    private readonly RectangleGeometry _railClip = new(new Rect(0, 0, RailWidth, 10000));

    private static string Greeting()
    {
        var name = Environment.UserName.Split(' ', '.', '_')[0]; name = CultureInfo.GetCultureInfo("pt-BR").TextInfo.ToTitleCase(name);
        return (DateTime.Now.Hour < 12 ? "Bom dia, " : DateTime.Now.Hour < 18 ? "Boa tarde, " : "Boa noite, ") + name;
    }

    private void ShowHowTo()
    {
        var window = Dialog("Como usar o " + Brand.Name, out var panel, out var footer);
        panel.Children.Add(Text("Clareza em cada etapa.", 25, weight: FontWeights.Bold, margin: new Thickness(0, 0, 0, 20)));
        var steps = new[] { ("01  Analise", "O diagnóstico observa hardware, consumo, inicialização e energia. Não altera configurações."), ("02  Entenda e selecione", "Leia o motivo de cada recomendação e escolha os itens que conhece. Selecionar ainda não aplica nada."), ("03  Revise e aprove", "A prévia mostra o estado anterior e o efeito esperado. Uma confirmação explícita libera as operações disponíveis."), ("04  Verifique e restaure", "O histórico mostra o resultado verificado. Quando possível, restaure o estado anterior; conflitos são bloqueados.") };
        foreach (var (title, description) in steps) { panel.Children.Add(Text(title, 16, Brush(Palette.AccentBright), FontWeights.SemiBold, new Thickness(0, 0, 0, 8))); panel.Children.Add(Text(description, 13, Muted, margin: new Thickness(0, 0, 0, 22))); }
        panel.Children.Add(Text("F5 inicia uma análise. Esc solicita cancelamento entre etapas seguras. Ctrl K abre a busca. Operações indisponíveis neste Windows continuam bloqueadas.", 12, Muted));
        var close = Button("Entendi", () => window.DialogResult = true, true); close.IsCancel = true; footer.Children.Add(close); window.ShowDialog();
    }

    private void ShowFontLicense()
    {
        var window = Dialog("Licença das fontes · Inter", out var panel, out var footer);
        try { var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Fonts/OFL.txt")); if (resource == null) throw new InvalidOperationException("Recurso de licença não encontrado."); using var reader = new System.IO.StreamReader(resource.Stream); panel.Children.Add(Text(reader.ReadToEnd(), 12, Muted)); }
        catch (Exception ex) { panel.Children.Add(Text("Não foi possível carregar a licença: " + ex.Message, 13, Muted)); }
        var close = Button("Fechar", () => window.DialogResult = true); close.IsCancel = true; footer.Children.Add(close); window.ShowDialog();
    }

    private Image Logo(double size)
    {
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
        try { image.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")); } catch { }
        AutomationProperties.SetName(image, "Monograma VL vermelho"); return image;
    }

    private void BuildShell()
    {
        WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 36, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        _shell.Background = Brush(Palette.Canvas);
        _shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        _shell.RowDefinitions.Add(new RowDefinition());
        _sidebarColumn.Width = new GridLength(RailWidth);
        _shell.ColumnDefinitions.Add(_sidebarColumn); _shell.ColumnDefinitions.Add(new ColumnDefinition()); Content = _shell;
        BuildTitleBar();

        _sidebar.Background = Brush(Palette.Canvas); _sidebar.BorderBrush = Brush(Palette.Line); _sidebar.BorderThickness = new Thickness(0, 0, 1, 0);
        // The drawer has its own full allocation across the shell. Only its clip changes;
        // allocating 72px to a 264px child makes WPF introduce an unwanted layout clip.
        _sidebar.Width = ExpandedRailWidth; _sidebar.HorizontalAlignment = HorizontalAlignment.Left; _sidebar.Padding = new Thickness(12, 18, 12, 12); _sidebar.Clip = _railClip;
        Grid.SetRow(_sidebar, 1); Grid.SetColumnSpan(_sidebar, 2); Panel.SetZIndex(_sidebar, 20);
        var sidebarDock = new DockPanel(); _sidebar.Child = sidebarDock;
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 0, 0, 29), Height = 38 };
        brand.Children.Add(Logo(28)); _sidebarBrand.Text = Brand.Name; _sidebarBrand.FontSize = 14; _sidebarBrand.FontWeight = FontWeights.SemiBold; _sidebarBrand.Margin = new Thickness(13, 0, 0, 0); _sidebarBrand.VerticalAlignment = VerticalAlignment.Center; _sidebarBrand.Opacity = 0; _sidebarBrand.RenderTransform = new TranslateTransform(-6, 0); brand.Children.Add(_sidebarBrand);
        DockPanel.SetDock(brand, Dock.Top); sidebarDock.Children.Add(brand);
        var profile = Button(Glyph("\uE77B", 18, Brush(Palette.AccentBright)), () => Navigate("settings")); profile.Width = 48; profile.HorizontalAlignment = HorizontalAlignment.Left; profile.Padding = new Thickness(12); profile.ToolTip = "Preferências"; AutomationProperties.SetName(profile, "Preferências"); _sidebarFooter.Children.Add(profile); _sidebarFooter.Margin = new Thickness(0, 14, 0, 0); DockPanel.SetDock(_sidebarFooter, Dock.Bottom); sidebarDock.Children.Add(_sidebarFooter);
        AddNav("overview", "\uE80F", "Visão geral");
        _navigation.Children.Add(RailDivider());
        AddNav("catalog", "\uE8F1", "Ajustes do Windows"); AddNav("power", "\uE945", "Energia"); AddNav("startup", "\uE7B5", "Inicialização"); AddNav("storage", "\uEDA2", "Limpeza e armazenamento");
        AddNav("games", "\uE7FC", "Jogos e FiveM"); AddNav("measurements", "\uE9D9", "Medições e diagnóstico"); AddNav("scaling", "\uE7F4", "Scaling e perfis"); AddNav("processes", "\uE964", "Memória e processos");
        _navigation.Children.Add(RailDivider()); AddNav("history", "\uE81C", "Histórico e restauração"); AddNav("settings", "\uE713", "Configurações e sobre");
        sidebarDock.Children.Add(new ScrollViewer { Content = _navigation, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        _sidebar.MouseEnter += (_, _) => ExpandRail(true); _sidebar.MouseLeave += (_, _) => { if (!_railKeyboardNavigation || !_sidebar.IsKeyboardFocusWithin) ExpandRail(false); };
        _sidebar.PreviewMouseDown += (_, _) => _railKeyboardNavigation = false;
        _sidebar.PreviewKeyDown += (_, _) => { _railKeyboardNavigation = true; ExpandRail(true); };
        _sidebar.GotKeyboardFocus += (_, _) => { if (Keyboard.IsKeyDown(Key.Tab)) { _railKeyboardNavigation = true; ExpandRail(true); } };
        _sidebar.IsKeyboardFocusWithinChanged += (_, _) => ExpandRail((_railKeyboardNavigation && _sidebar.IsKeyboardFocusWithin) || _sidebar.IsMouseOver);
        _shell.Children.Add(_sidebar);

        var main = new Grid(); Grid.SetColumn(main, 1); Grid.SetRow(main, 1); _shell.Children.Add(main);
        var circuit = new AnimatedCircuitVisual { IsHitTestVisible = false }; RegisterSceneMotion(circuit); main.Children.Add(circuit);
        var dock = new DockPanel(); main.Children.Add(dock);
        BuildHeader(dock);
        var bottom = new Grid { Margin = new Thickness(28, 0, 28, 9) }; bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _analysisStatus.FontSize = 10; _analysisStatus.Foreground = Brush(Palette.Muted); _analysisStatus.Text = "○  Diagnóstico local · pronto para analisar";
        AutomationProperties.SetLiveSetting(_analysisStatus, AutomationLiveSetting.Polite); bottom.Children.Add(_analysisStatus);
        var shortcut = Text("F5 analisar   /   Esc cancelar", 10, Brush(Palette.Muted)); Grid.SetColumn(shortcut, 1); bottom.Children.Add(shortcut); DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
        _noticeBox.Background = Brush(Palette.Raised); _noticeBox.BorderBrush = Brush(Palette.Line); _noticeBox.BorderThickness = new Thickness(1); _noticeBox.CornerRadius = new CornerRadius(8); _noticeBox.Padding = new Thickness(12, 8, 12, 8); _noticeBox.Margin = new Thickness(28, 10, 28, 0); _noticeBox.Visibility = Visibility.Collapsed; _noticeText.FontSize = 12; _noticeBox.Child = _noticeText; AutomationProperties.SetLiveSetting(_noticeText, AutomationLiveSetting.Assertive); DockPanel.SetDock(_noticeBox, Dock.Top); dock.Children.Add(_noticeBox);
        _pageHost.Margin = new Thickness(28, 22, 27, 18);
        dock.Children.Add(new ScrollViewer { Content = _pageHost, Focusable = false });
    }

    private void BuildTitleBar()
    {
        var bar = new Border { Background = Brush(Palette.Canvas), BorderBrush = Brush(Palette.Line), BorderThickness = new Thickness(0, 0, 0, 1) }; Grid.SetColumnSpan(bar, 2); _shell.Children.Add(bar);
        var dock = new DockPanel { LastChildFill = false }; bar.Child = dock;
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }; brand.Children.Add(Logo(18)); brand.Children.Add(Text(Brand.Name.ToUpperInvariant(), 11, weight: FontWeights.Bold, margin: new Thickness(9, 0, 0, 0))); DockPanel.SetDock(brand, Dock.Left); dock.Children.Add(brand);
        var controls = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(controls, Dock.Right); dock.Children.Add(controls);
        controls.Children.Add(ChromeButton("\uE921", "Minimizar", () => SystemCommands.MinimizeWindow(this)));
        controls.Children.Add(ChromeButton("\uE922", "Maximizar ou restaurar", () => { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }));
        controls.Children.Add(ChromeButton("\uE8BB", "Fechar", () => SystemCommands.CloseWindow(this)));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0) }; DockPanel.SetDock(tools, Dock.Right); dock.Children.Add(tools);
        _exportTitleButton = ChromeButton("\uE74E", "Exportar relatório JSON com dados deste PC", ExportReport); _exportTitleButton.IsEnabled = false; tools.Children.Add(_exportTitleButton);
        tools.Children.Add(ChromeButton("\uE946", "Como usar", ShowHowTo));
        tools.Children.Add(ChromeButton("\uE713", "Preferências de movimento e privacidade", () => Navigate("settings")));
    }

    private static Button ChromeButton(string glyph, string label, Action action)
    {
        var button = Button(Glyph(glyph, 12, Muted), action); button.Padding = new Thickness(12, 0, 12, 0); button.MinHeight = 34; button.Height = 34; button.MinWidth = 38; button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.ToolTip = label; AutomationProperties.SetName(button, label); WindowChrome.SetIsHitTestVisibleInChrome(button, true); return button;
    }

    private void BuildHeader(DockPanel dock)
    {
        var border = new Border { Background = Brush(Palette.Canvas), BorderBrush = Brush(Palette.Line), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(28, 15, 28, 16) };
        _header.ColumnDefinitions.Add(new ColumnDefinition()); _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });
        _header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); _header.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) }); border.Child = _header;
        var heading = Stack(); var titleLine = new StackPanel { Orientation = Orientation.Horizontal };
        _pageTitle.FontSize = 21; _pageTitle.FontWeight = FontWeights.Bold; _pageTitle.LineHeight = 27; titleLine.Children.Add(_pageTitle);
        _compatibility.FontSize = 9; _compatibility.Foreground = Brush(Palette.Muted); _compatibility.Margin = new Thickness(12, 5, 0, 0); _compatibility.VerticalAlignment = VerticalAlignment.Center; titleLine.Children.Add(_compatibility); heading.Children.Add(titleLine);
        _pageSubtitle.FontSize = 12; _pageSubtitle.Foreground = Brush(Palette.Muted); _pageSubtitle.Margin = new Thickness(0, 3, 12, 0); heading.Children.Add(_pageSubtitle); _header.Children.Add(heading);
        Grid.SetColumn(_searchPanel, 1); _searchPanel.VerticalAlignment = VerticalAlignment.Center;
        var searchBox = new Grid(); _search.MinHeight = 40; _search.Height = 40; _search.Padding = new Thickness(37, 8, 57, 8); _search.Background = Brush(Palette.Panel); _search.BorderBrush = Brush(Palette.Line); _search.FontSize = 12; _search.ToolTip = "Pesquisar ajuste, página ou problema · Ctrl K"; AutomationProperties.SetName(_search, "Pesquisar ajustes, páginas e recomendações");
        var placeholder = Text("Buscar ajuste, página ou problema...", 12, Brush(Palette.Muted), margin: new Thickness(38, 0, 40, 0)); placeholder.VerticalAlignment = VerticalAlignment.Center; placeholder.IsHitTestVisible = false;
        _search.TextChanged += (_, _) => { placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; if (!_building && (_page is "search" or "overview" or "startup" or "catalog" or "measurements")) RenderPage(false); };
        _search.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SearchNavigate(); e.Handled = true; } }; searchBox.Children.Add(_search); searchBox.Children.Add(placeholder);
        var searchGlyph = Glyph("\uE721", 13, Brush(Palette.Muted)); searchGlyph.Margin = new Thickness(15, 0, 0, 0); searchGlyph.HorizontalAlignment = HorizontalAlignment.Left; searchGlyph.IsHitTestVisible = false; searchBox.Children.Add(searchGlyph);
        var key = Text("Ctrl K", 9, Brush(Palette.Muted)); key.HorizontalAlignment = HorizontalAlignment.Right; key.VerticalAlignment = VerticalAlignment.Center; key.Margin = new Thickness(0, 0, 12, 0); key.IsHitTestVisible = false; searchBox.Children.Add(key); _searchPanel.Children.Add(searchBox); _header.Children.Add(_searchPanel);
        DockPanel.SetDock(border, Dock.Top); dock.Children.Add(border);
    }

    private static UIElement RailDivider() => new Border { Height = 1, Width = 25, Background = Brush(Palette.Line), Margin = new Thickness(0, 17, 0, 17), HorizontalAlignment = HorizontalAlignment.Left };
    private void AddNav(string key, string glyph, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(Glyph(glyph, 18, Muted));
        var text = Text(label, 12, Muted, FontWeights.SemiBold, new Thickness(15, 0, 0, 0)); text.VerticalAlignment = VerticalAlignment.Center; text.TextWrapping = TextWrapping.NoWrap; text.Opacity = 0; text.RenderTransform = new TranslateTransform(-6, 0); row.Children.Add(text); _navLabels.Add(text);
        var button = Button(row, () => Navigate(key)); button.Width = 48; button.HorizontalAlignment = HorizontalAlignment.Left; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Padding = new Thickness(12, 10, 8, 10); button.Margin = new Thickness(0, 0, 0, 5); button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; button.ToolTip = label; AutomationProperties.SetName(button, label);
        var grid = new Grid(); grid.Children.Add(button); var marker = new Border { Width = 2, Height = 18, Background = Brush(Palette.Accent), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, IsHitTestVisible = false }; grid.Children.Add(marker); _navMarkers[key] = marker;
        _navButtons[key] = button; _navigation.Children.Add(grid);
    }

    private void ExpandRail(bool expanded)
    {
        if (_railExpanded == expanded) return; _railExpanded = expanded;
        var target = new Rect(0, 0, expanded ? ExpandedRailWidth : RailWidth, 10000);
        foreach (var button in _navButtons.Values) button.Width = expanded ? ExpandedRailWidth - 24 : 48;
        var current = _railClip.Rect;
        _railClip.BeginAnimation(RectangleGeometry.RectProperty, null); _railClip.Rect = target;
        // Keyboard focus reveals navigation immediately. Pointer transitions are short,
        // interruptible and preserve the fixed content position beneath the overlay.
        var animate = MotionAllowed && !_railKeyboardNavigation;
        if (animate)
        {
            var transition = new RectAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(200), FillBehavior = FillBehavior.Stop };
            transition.KeyFrames.Add(new DiscreteRectKeyFrame(current, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            transition.KeyFrames.Add(new SplineRectKeyFrame(target, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), new KeySpline(.23, 1, .32, 1)));
            _railClip.BeginAnimation(RectangleGeometry.RectProperty, transition, HandoffBehavior.SnapshotAndReplace);
        }
        foreach (var label in _navLabels.Prepend(_sidebarBrand))
        {
            var opacity = label.Opacity;
            label.BeginAnimation(OpacityProperty, null); label.Opacity = expanded ? 1 : 0;
            if (animate) MotionTransitions.Animate(label, OpacityProperty, opacity, label.Opacity, 160);
            if (label.RenderTransform is TranslateTransform shift)
            {
                var from = shift.X; shift.BeginAnimation(TranslateTransform.XProperty, null); shift.X = expanded ? 0 : -6;
                if (animate) MotionTransitions.Animate(shift, TranslateTransform.XProperty, from, shift.X, 200);
            }
        }
    }

    private void UpdateDesignState()
    {
        foreach (var marker in _navMarkers) marker.Value.Visibility = marker.Key == _page ? Visibility.Visible : Visibility.Collapsed;
        if (_exportTitleButton != null) _exportTitleButton.IsEnabled = _snapshot != null && !_busy;
        _compatibility.Text = _snapshot == null ? "" : _snapshot.SupportedOS ? "●  Diagnóstico local" : "●  Somente diagnóstico";
        _compatibility.ToolTip = _snapshot == null ? "" : $"{_snapshot.WindowsName} · build {_snapshot.WindowsBuild}. " + (_snapshot.SupportedOS ? "Plataforma alvo do aplicativo. Cada ajuste verifica permissões e disponibilidade." : "As alterações de configuração exigem um Windows compatível.");
    }

    private void AdaptLayout()
    {
        if (_header.ColumnDefinitions.Count < 2) return;
        var narrow = ActualWidth < 950;
        _header.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : ActualWidth < 1200 ? 330 : 420);
        _header.RowDefinitions[1].Height = narrow ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(_searchPanel, narrow ? 0 : 1); Grid.SetRow(_searchPanel, narrow ? 1 : 0); _searchPanel.Margin = narrow ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        _compatibility.Visibility = ActualWidth < 1050 ? Visibility.Collapsed : Visibility.Visible;
        _pageSubtitle.Visibility = ActualHeight < 480 ? Visibility.Collapsed : Visibility.Visible;
        if (Math.Abs(ActualWidth - _lastLayoutWidth) > 80 && _pageHost.Content != null) { _lastLayoutWidth = ActualWidth; RenderPage(); }
    }

    private UIElement BuildOverview()
    {
        var root = Stack();
        var wide = ActualWidth >= 1050;
        var dashboard = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        dashboard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(wide ? 1.84 : 1, GridUnitType.Star) }); dashboard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(wide ? 1 : 0, GridUnitType.Star) });
        var hero = BuildHero(); dashboard.Children.Add(hero);
        if (wide) { var hardware = BuildHardware(); Grid.SetColumn(hardware, 1); dashboard.Children.Add(hardware); }
        root.Children.Add(dashboard);
        root.Children.Add(SmallSection("OTIMIZAÇÕES", "Escolha uma área para explorar"));
        var categories = new UniformGrid { Columns = wide ? 5 : ActualWidth >= 780 ? 3 : 2, Margin = new Thickness(-5, 0, -5, 0) };
        categories.Children.Add(DashboardCategory("\uE8F1", "Ajustes", "Desempenho, entrada, rede e Windows.", "Catálogo com prévia e reversão", "catalog", false));
        categories.Children.Add(DashboardCategory("\uE7B5", "Inicialização", "Controle o que abre junto com o Windows.", _snapshot == null ? "Aguardando análise" : _snapshot.StartupItems.Count + " itens identificados", "startup", false));
        categories.Children.Add(DashboardCategory("\uE7FC", "Jogos", "Instalações locais e presets FiveM.", "Perfis com cópia de segurança", "games", true));
        var free = _snapshot?.Disks.Sum(d => d.FreeBytes) ?? 0;
        categories.Children.Add(DashboardCategory("\uEDA2", "Limpeza", "Verifique categorias antes de excluir.", _snapshot?.Disks.Count > 0 ? Bytes(free) + " livres em disco" : "Varredura sob demanda", "storage", true));
        categories.Children.Add(DashboardCategory("\uE9D9", "Medições", "FPS, frametime e consumo observado.", "Dados reais e dependências visíveis", "measurements", false)); root.Children.Add(categories);
        root.Children.Add(BuildRecommendations());
        if (!wide) { root.Children.Add(SmallSection("SEU HARDWARE", "Dados observados")); root.Children.Add(BuildHardware()); }
        if (_previousSnapshot != null && _snapshot != null)
        {
            var comparison = new Expander { Header = "Comparar observações · antes e depois", Foreground = Muted, Margin = new Thickness(0, 16, 0, 0), FontSize = 12 };
            var details = Stack(); details.Children.Add(Text("Observações de sessões diferentes não comprovam ganho. Repita sob carga e condições equivalentes.", 12, Brush(Palette.AccentBright), margin: new Thickness(0, 12, 0, 12)));
            details.Children.Add(Text($"Anterior · {_previousSnapshot.CapturedAt:dd/MM HH:mm:ss} · {_previousSnapshot.ObservationSeconds:0.#} s · CPU {CpuLabel(_previousSnapshot.AverageCpuPercent)} · RAM disponível {MemoryAvailableLabel(_previousSnapshot)}", 12, Muted));
            details.Children.Add(Text($"Atual · {_snapshot.CapturedAt:dd/MM HH:mm:ss} · {_snapshot.ObservationSeconds:0.#} s · CPU {CpuLabel(_snapshot.AverageCpuPercent)} · RAM disponível {MemoryAvailableLabel(_snapshot)}", 12, Muted, margin: new Thickness(0, 8, 0, 8)));
            details.Children.Add(Text("Observações diferentes não comprovam ganho. Consulte o resultado individual da limpeza no histórico.", 11, Muted)); comparison.Content = details; root.Children.Add(comparison);
        }
        if (_snapshot?.Notices.Count > 0)
        {
            var observations = new Expander { Header = "Detalhes e limites da coleta", Foreground = Brush(Palette.Muted), FontSize = 11, Margin = new Thickness(0, 12, 0, 0) }; var details = Stack(); foreach (var notice in _snapshot.Notices) details.Children.Add(Text("• " + notice, 12, Muted, margin: new Thickness(0, 9, 0, 0))); observations.Content = details; root.Children.Add(observations);
        }
        return root;
    }

    private UIElement BuildHero()
    {
        var height = ActualHeight < 830 ? 286 : 326;
        var frame = new Grid { MinHeight = height, ClipToBounds = true };
        var fill = new Border { CornerRadius = new CornerRadius(20), BorderBrush = Brush(Palette.Line), BorderThickness = new Thickness(1), Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop((Color)ColorConverter.ConvertFromString(Palette.Raised), 0), new GradientStop((Color)ColorConverter.ConvertFromString(Palette.Panel), .38), new GradientStop((Color)ColorConverter.ConvertFromString(Palette.Panel), 1) }, new Point(0, 0), new Point(1, 1)) }; frame.Children.Add(fill);
        var content = new Grid { Margin = new Thickness(32, 25, 14, 22) }; content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.18, GridUnitType.Star) }); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); frame.Children.Add(content);
        var copy = Stack(); copy.VerticalAlignment = VerticalAlignment.Center;
        copy.Children.Add(Eyebrow(_busy ? "ANÁLISE EM ANDAMENTO" : "RECOMENDAÇÕES PARA O SEU PC"));
        var count = _snapshot == null ? 0 : _backend.GetRecommendations(_snapshot).Count;
        var headline = new WrapPanel { Margin = new Thickness(0, 12, 0, 10), VerticalAlignment = VerticalAlignment.Center };
        headline.Children.Add(Text(_busy ? "Conhecendo seu PC." : _snapshot == null ? "Entenda seu PC." : "Seu PC, em foco.", 30, weight: FontWeights.Bold));
        if (_snapshot != null && !_busy) headline.Children.Add(new Border { Child = Text($"{count} para revisar", 9, Brush(Palette.AccentBright), FontWeights.Bold), Background = Brush(Palette.AccentDeep), BorderBrush = Brush(Palette.AccentDeep), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(10, 10, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        copy.Children.Add(headline);
        copy.Children.Add(Text(_busy ? _stage : _snapshot == null ? "Entenda seu hardware e o que pode ser ajustado. Diagnóstico real, sem alterações automáticas." : $"{count} recomendações para revisar. Inicialização, energia e uso de recursos, com uma explicação para cada escolha.", 12, Brush(Palette.Text), margin: new Thickness(0, 0, 5, 20)));
        var actions = new WrapPanel(); var action = Button(_busy ? "Cancelar coleta" : _snapshot == null ? "Analisar meu PC" : "Analisar novamente", () => { if (_busy) _cts?.Cancel(); else _ = AnalyzeAsync(); }, true); action.Padding = new Thickness(19, 10, 19, 10); action.MinHeight = 46; action.Margin = new Thickness(0, 0, 13, 8);
        action.SizeChanged += (_, _) => { var cut = new StreamGeometry(); using (var c = cut.Open()) { var w = action.ActualWidth; var h = action.ActualHeight; c.BeginFigure(new Point(0, 0), true, true); c.LineTo(new Point(w - 12, 0), true, false); c.LineTo(new Point(w, 12), true, false); c.LineTo(new Point(w, h), true, false); c.LineTo(new Point(12, h), true, false); c.LineTo(new Point(0, h - 12), true, false); } action.Clip = cut; };
        var actionContent = new StackPanel { Orientation = Orientation.Horizontal }; actionContent.Children.Add(Glyph("\uE945", 15, Brush(Palette.Text))); actionContent.Children.Add(Text(_busy ? "Cancelar coleta" : _snapshot == null ? "Analisar meu PC" : "Analisar novamente", 12, Brush(Palette.Text), FontWeights.Bold, new Thickness(9, 0, 0, 0))); action.Content = actionContent; AttachMotionFeedback(action); actions.Children.Add(action);
        if (_snapshot != null && !_busy) { var link = Button("Ver recomendações  ›", ScrollToRecommendations); link.Background = Brushes.Transparent; link.BorderThickness = new Thickness(0); link.Foreground = Brush(Palette.Accent); link.FontSize = 11; link.Padding = new Thickness(0, 10, 0, 10); actions.Children.Add(link); }
        copy.Children.Add(actions);
        var capture = Text(_snapshot == null ? "somente leitura · nenhuma alteração automática" : $"coleta de {_snapshot.ObservationSeconds:0.#} s · {_snapshot.CapturedAt:HH:mm:ss} · sob demanda", 9, Brush(Palette.Muted), margin: new Thickness(0, 4, 0, 0)); capture.FontFamily = new FontFamily("Cascadia Mono, Consolas"); copy.Children.Add(capture); content.Children.Add(copy);
        var orbit = BuildOrbit(); Grid.SetColumn(orbit, 1); content.Children.Add(orbit);
        var brackets = new BracketOverlay { IsHitTestVisible = false, Margin = new Thickness(11) }; frame.Children.Add(brackets); return frame;
    }

    private UIElement BuildOrbit()
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = 282, Height = 282, ClipToBounds = false };
        var field = new AnimatedOrbitVisual { VerticalAlignment = VerticalAlignment.Top }; RegisterSceneMotion(field); grid.Children.Add(field);
        if (_busy)
        {
            var spinner = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 141,13 A 112,112 0 0 1 253,125"), Stroke = Brush(Palette.Accent), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }; var transform = new RotateTransform(0, 141, 125); spinner.RenderTransform = transform; _spinners.Add(transform); grid.Children.Add(spinner);
        }
        var count = Stack(); count.VerticalAlignment = VerticalAlignment.Bottom; count.Margin = new Thickness(0, 0, 0, 1);
        var number = Text(_busy ? "…" : _snapshot == null ? "—" : _backend.GetRecommendations(_snapshot).Count.ToString(), 32, weight: FontWeights.Bold); number.TextAlignment = TextAlignment.Center; count.Children.Add(number);
        var label = Text(_busy ? "OBSERVANDO" : _snapshot == null ? "AGUARDANDO ANÁLISE" : "RECOMENDAÇÕES", 8, Brush(Palette.Muted)); label.TextAlignment = TextAlignment.Center; count.Children.Add(label); grid.Children.Add(count);
        AutomationProperties.SetName(grid, _busy ? "Análise indeterminada em andamento" : _snapshot == null ? "Aguardando análise" : $"{number.Text} recomendações. Não é uma pontuação de desempenho.");
        return new Viewbox { Child = grid, Stretch = Stretch.Uniform, MaxHeight = ActualHeight < 830 ? 249 : 282, MaxWidth = 310, Margin = new Thickness(-3, -5, -10, -7) };
    }

    private UIElement BuildHardware()
    {
        var content = Stack(); var title = new Grid(); title.ColumnDefinitions.Add(new ColumnDefinition()); title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); title.Children.Add(Eyebrow("SEU HARDWARE"));
        var inspect = Button("Ver coleta  ›", () => Navigate("processes")); inspect.Padding = new Thickness(0); inspect.MinHeight = 20; inspect.FontSize = 10; inspect.Foreground = Brush(Palette.Accent); inspect.Background = Brushes.Transparent; inspect.BorderThickness = new Thickness(0); Grid.SetColumn(inspect, 1); title.Children.Add(inspect); content.Children.Add(title);
        var cpuName = _snapshot?.CpuName;
        var cpuSummary = cpuName == null ? "Aguardando análise" : System.Text.RegularExpressions.Regex.Replace(cpuName.Replace("AMD ", "").Replace("Intel(R) ", "").Replace("(R)", ""), @"\s+\d+-Core\s+Processor\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        var gpuNames = _snapshot?.GpuName.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        var gpuSummary = gpuNames.Length == 0 ? "Aguardando análise" : gpuNames[0].Replace("NVIDIA GeForce ", "").Replace("NVIDIA ", "").Replace("AMD ", "") + (gpuNames.Length > 1 ? $" (+{gpuNames.Length - 1} GPU)" : "");
        content.Children.Add(HardwareRow("\uE950", "PROCESSADOR", cpuSummary, _snapshot?.AverageCpuPercent, _snapshot == null ? "" : _snapshot.AverageCpuPercent is double cpu ? $"{cpu:0.#}%" : "n/d", false, cpuName));
        content.Children.Add(HardwareRow("\uE7F4", "PLACA DE VÍDEO", gpuSummary, null, _snapshot == null ? "" : "n/d", true, _snapshot?.GpuName + " · Adpatadores identificados; uso e GPU ativa não são medidos."));
        double? memory = _snapshot?.TotalMemoryBytes > 0 ? 100.0 * (_snapshot.TotalMemoryBytes - _snapshot.AvailableMemoryBytes) / _snapshot.TotalMemoryBytes : null;
        content.Children.Add(HardwareRow("\uE964", "MEMÓRIA RAM", _snapshot == null ? "Aguardando análise" : _snapshot.InstalledMemoryBytes > 0 ? Bytes(_snapshot.InstalledMemoryBytes) : "Não disponível", memory, memory is double percent ? $"{percent:0.#}%" : "", true));
        var card = Card(content); card.CornerRadius = new CornerRadius(20); card.Padding = new Thickness(21, 22, 21, 17); card.Margin = new Thickness(15, 0, 0, 0); card.Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(Palette.Panel), (Color)ColorConverter.ConvertFromString(Palette.Canvas), 90); return card;
    }

    private static UIElement HardwareRow(string glyph, string label, string name, double? value, string trailing, bool divider, string? tooltip = null)
    {
        var row = new Grid { Margin = new Thickness(0, divider ? 0 : 14, 0, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        if (divider) { row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) }); row.RowDefinitions.Add(new RowDefinition()); var line = new Border { Background = Brush(Palette.Line), Height = 1 }; Grid.SetColumnSpan(line, 2); row.Children.Add(line); }
        var icon = IconBox(glyph, 35); icon.VerticalAlignment = VerticalAlignment.Center; icon.Margin = new Thickness(0, 8, 0, 4); if (divider) Grid.SetRow(icon, 1); row.Children.Add(icon);
        var details = Stack(); details.Margin = new Thickness(0, 12, 0, 14); if (divider) Grid.SetRow(details, 1); Grid.SetColumn(details, 1);
        var meta = new Grid(); meta.ColumnDefinitions.Add(new ColumnDefinition()); meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); var caption = Text(label, 9, Brush(Palette.Muted), FontWeights.SemiBold); caption.FontFamily = new FontFamily("Cascadia Mono, Consolas"); meta.Children.Add(caption); var percent = Text(trailing, 11, Brush(Palette.Text), FontWeights.SemiBold); Grid.SetColumn(percent, 1); meta.Children.Add(percent); details.Children.Add(meta);
        var model = Text(name, 14, weight: FontWeights.Bold, margin: new Thickness(0, 5, 0, 8)); model.TextWrapping = TextWrapping.NoWrap; model.TextTrimming = TextTrimming.CharacterEllipsis; model.ToolTip = tooltip ?? name; details.Children.Add(model);
        var track = new Grid { Height = 4, Background = Brush(Palette.Line) }; if (value is double v) track.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Left, Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(Palette.Accent), (Color)ColorConverter.ConvertFromString(Palette.AccentBright), 0), CornerRadius = new CornerRadius(2), Width = 1 });
        track.SizeChanged += (_, _) => { if (value is double v && track.Children.Count > 0) ((Border)track.Children[0]).Width = Math.Max(1, track.ActualWidth * Math.Clamp(v, 0, 100) / 100); }; AutomationProperties.SetName(track, value is double measured ? $"Uso observado {measured:0.#} por cento" : "Uso não disponível"); details.Children.Add(track); row.Children.Add(details); return row;
    }

    private UIElement DashboardCategory(string glyph, string title, string description, string status, string page, bool positive)
    {
        var layout = new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); layout.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = IconBox(glyph, 33); icon.VerticalAlignment = VerticalAlignment.Top; layout.Children.Add(icon);
        var copy = Stack(); Grid.SetColumn(copy, 1); copy.Children.Add(Text(title, 13, weight: FontWeights.Bold)); copy.Children.Add(Text(description, 11, Brush(Palette.Muted), margin: new Thickness(0, 5, 0, 12))); copy.Children.Add(Text(status, 11, _snapshot == null ? Brush(Palette.Muted) : positive ? Brush(Palette.AccentBright) : Brush(Palette.AccentBright), FontWeights.SemiBold)); layout.Children.Add(copy);
        var button = Button(layout, () => Navigate(page)); button.Padding = new Thickness(15, 16, 12, 13); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Margin = new Thickness(5, 0, 5, 0); button.Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(Palette.Panel), (Color)ColorConverter.ConvertFromString(Palette.Canvas), 90); button.BorderBrush = Brush(Palette.Line); button.MinHeight = ActualHeight < 830 ? 108 : 124; AutomationProperties.SetName(button, title + ": " + status); AttachMotionFeedback(button); return button;
    }

    private UIElement BuildRecommendations()
    {
        var outer = Stack(); var heading = new StackPanel { Orientation = Orientation.Horizontal }; heading.Children.Add(Glyph("\uE945", 13, Brush(Palette.AccentBright))); heading.Children.Add(Text("Recomendações", 13, weight: FontWeights.Bold, margin: new Thickness(8, 0, 0, 0))); if (_snapshot != null && !_snapshot.SupportedOS) heading.Children.Add(Text("·  somente diagnóstico neste Windows", 10, Brush(Palette.Muted), margin: new Thickness(10, 3, 0, 0))); outer.Children.Add(heading);
        var recs = _snapshot == null ? [] : _backend.GetRecommendations(_snapshot).Where(r => Matches(r.Title + " " + r.Explanation + " " + r.Category)).OrderBy(r => NormalizePage(r.TargetPage ?? "") is "startup" or "power" ? 0 : 1).ToArray();
        var grid = new UniformGrid { Columns = ActualWidth >= 950 ? 2 : 1, Margin = new Thickness(0, 14, 0, 0) };
        if (recs.Length == 0)
        {
            var empty = Text(_snapshot == null ? "Faça a primeira análise para descobrir o que faz sentido nesta máquina." : "Nenhuma recomendação encontrada para esta busca.", 12, Muted); grid.Children.Add(empty);
        }
        else foreach (var rec in recs.Take(2)) grid.Children.Add(CompactRecommendation(rec));
        outer.Children.Add(grid);
        if (recs.Length > 0)
        {
            var more = new Expander { Header = $"Ver detalhes e fontes · {recs.Length} orientações", Foreground = Brush(Palette.Muted), FontSize = 10, Margin = new Thickness(0, 7, 0, 0) }; var list = Stack(); foreach (var rec in recs) { list.Children.Add(Text(rec.Title, 13, weight: FontWeights.SemiBold, margin: new Thickness(0, 12, 0, 5))); list.Children.Add(Text(rec.Explanation, 12, Muted)); if (!string.IsNullOrWhiteSpace(rec.SourceUrl)) { var source = Button("Consultar fonte técnica", () => OpenExternal(rec.SourceUrl)); source.HorizontalAlignment = HorizontalAlignment.Left; source.Margin = new Thickness(0, 8, 0, 8); list.Children.Add(source); } } more.Content = list; outer.Children.Add(more);
        }
        var card = Card(outer); card.Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(Palette.Panel), (Color)ColorConverter.ConvertFromString(Palette.Canvas), 90); card.Padding = new Thickness(18, 15, 18, 13); card.Margin = new Thickness(0, 16, 0, 0); card.CornerRadius = new CornerRadius(17); return card;
    }

    private UIElement CompactRecommendation(Recommendation recommendation)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 16, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
        var icon = IconBox(recommendation.TargetPage?.Contains("nicial") == true ? "\uE768" : "\uE946", 26); icon.VerticalAlignment = VerticalAlignment.Top; row.Children.Add(icon);
        var details = Stack(); Grid.SetColumn(details, 1); details.Children.Add(Text(recommendation.Title, 12, weight: FontWeights.SemiBold));
        var explanation = Text(recommendation.Explanation, 11, Brush(Palette.Muted), margin: new Thickness(0, 4, 0, 0)); explanation.TextWrapping = TextWrapping.NoWrap; explanation.TextTrimming = TextTrimming.CharacterEllipsis; explanation.ToolTip = recommendation.Explanation; details.Children.Add(explanation); row.Children.Add(details);
        var navigate = Button("›", () => Navigate(NormalizePage(recommendation.TargetPage ?? "overview"))); navigate.Padding = new Thickness(0); navigate.MinWidth = 25; navigate.Background = Brushes.Transparent; navigate.BorderThickness = new Thickness(0); navigate.VerticalAlignment = VerticalAlignment.Top; navigate.MinHeight = 32; navigate.ToolTip = "Revisar " + recommendation.Title; AutomationProperties.SetName(navigate, "Revisar " + recommendation.Title); Grid.SetColumn(navigate, 2); row.Children.Add(navigate); return row;
    }

    private UIElement BuildStartup()
    {
        var root = Stack(); if (_snapshot == null) { root.Children.Add(AnalyzeEmpty()); return root; }
        var cardContent = Stack(); var heading = new Grid { Margin = new Thickness(0, 0, 0, 16) }; heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); heading.Children.Add(Text("Aplicativos que iniciam com o Windows", 16, weight: FontWeights.SemiBold));
        _reviewStartup = Button("Revisar seleção", () => _ = ReviewStartupAsync(), true); _reviewStartup.FontSize = 11; _reviewStartup.Padding = new Thickness(12, 8, 12, 8); UpdateSelectionButton(); Grid.SetColumn(_reviewStartup, 1); heading.Children.Add(_reviewStartup); cardContent.Children.Add(heading);
        cardContent.Children.Add(Text("Selecione apenas o que você conhece. A seleção prepara a revisão; não modifica o Windows.", 11, Muted, margin: new Thickness(0, 0, 0, 14)));
        foreach (var item in _snapshot.StartupItems.Where(i => Matches(i.Name + " " + i.Command + " " + i.Source)))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(39) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            row.Children.Add(IconBox("\uE8A5", 28)); var copy = Stack(); copy.Margin = new Thickness(0, 0, 9, 0); Grid.SetColumn(copy, 1);
            var header = Stack(); header.Children.Add(Text(item.Name + (item.CanDisable ? "" : " · protegido"), 12, item.CanDisable ? null : Brush(Palette.AccentBright), FontWeights.SemiBold));
            var summary = Text(item.Source + " · " + item.Command, 9, Brush(Palette.Muted), margin: new Thickness(0, 3, 0, 0)); summary.FontFamily = new FontFamily("Cascadia Mono, Consolas"); summary.TextWrapping = TextWrapping.NoWrap; summary.TextTrimming = TextTrimming.CharacterEllipsis; summary.ToolTip = item.Command; header.Children.Add(summary);
            var expander = new Expander { Header = header, Foreground = item.CanDisable ? Brush(Palette.Muted) : Brush(Palette.AccentBright), FontSize = 9, Margin = new Thickness(0, 6, 0, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = "Expandir origem, comando completo e cautelas" }; AutomationProperties.SetName(expander, "Detalhes de " + item.Name); var full = Stack(); full.Children.Add(Text(item.Command, 11, Muted, margin: new Thickness(0, 7, 0, 7))); full.Children.Add(Text(item.Caution, 11, Brush(Palette.AccentBright))); expander.Content = full; copy.Children.Add(expander); row.Children.Add(copy);
            var select = new CheckBox { IsChecked = _selectedStartup.Contains(item.Id), IsEnabled = item.CanDisable && !_busy, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Selecionar " + item.Name + " para revisão" }; select.SetResourceReference(StyleProperty, "SelectionToggle"); AutomationProperties.SetName(select, "Selecionar " + item.Name + " para revisar desativação"); select.Checked += (_, _) => { _selectedStartup.Add(item.Id); UpdateSelectionButton(); }; select.Unchecked += (_, _) => { _selectedStartup.Remove(item.Id); UpdateSelectionButton(); }; Grid.SetColumn(select, 2); row.Children.Add(select); cardContent.Children.Add(row); cardContent.Children.Add(new Border { Height = 1, Background = Brush(Palette.Line) });
        }
        var card = Card(cardContent); card.MaxWidth = 624; card.HorizontalAlignment = HorizontalAlignment.Center; card.Margin = new Thickness(0, 8, 0, 18); root.Children.Add(card); var settings = ActionFooter("Abrir inicialização do Windows", "ms-settings:startupapps"); ((FrameworkElement)settings).HorizontalAlignment = HorizontalAlignment.Center; root.Children.Add(settings); return root;
    }

    private static UIElement Eyebrow(string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Add(new Border { Width = 14, Height = 2, Background = Brush(Palette.Accent), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) }); var text = Text(label, 10, Brush(Palette.Muted), FontWeights.SemiBold); text.FontFamily = new FontFamily("Cascadia Mono, Consolas"); panel.Children.Add(text); return panel;
    }
    private static UIElement SmallSection(string label, string hint)
    {
        var grid = new Grid { Margin = new Thickness(0, 22, 0, 12) }; grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.Children.Add(Eyebrow(label)); var text = Text(hint, 10, Brush(Palette.Muted)); Grid.SetColumn(text, 1); grid.Children.Add(text); return grid;
    }
    private static Border IconBox(string glyph, double size)
    {
        var icon = Glyph(glyph, size > 30 ? 16 : 13, Brush(Palette.Text)); icon.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(9), BorderBrush = Brush(Palette.Line), BorderThickness = new Thickness(1), Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(Palette.Raised), (Color)ColorConverter.ConvertFromString(Palette.Panel), 90), Child = icon };
    }
}

internal sealed class BracketOverlay : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        var pen = new Pen(new SolidColorBrush(Palette.ColorOf(Palette.Accent)), 1.2); double w = ActualWidth, h = ActualHeight, n = 13;
        foreach (var p in new[] { new Point(0, 0), new Point(w, 0), new Point(0, h), new Point(w, h) }) { dc.DrawLine(pen, p, new Point(p.X + (p.X == 0 ? n : -n), p.Y)); dc.DrawLine(pen, p, new Point(p.X, p.Y + (p.Y == 0 ? n : -n))); }
    }
}

