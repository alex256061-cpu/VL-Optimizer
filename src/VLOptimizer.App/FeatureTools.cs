using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using VLOptimizer.Core;
using Path = System.IO.Path;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private OptimizerToolkit? _toolkit;
    private OptimizerToolkit Toolkit => _toolkit ??= new OptimizerToolkit(_backend.DataDirectory);
    private readonly HashSet<string> _cleanupCategories = [];
    private ToolkitCleanupPreview? _cleanupPreview;
    private ToolkitCleanupResult? _cleanupResult;
    private Button? _cleanupReviewButton;
    private ToolkitGameDiscovery? _gameDiscovery;
    private IReadOnlyList<ToolkitFiveMBackup> _fiveMBackups = [];
    private ToolkitBottleneckReport? _bottleneckReport;
    private ToolkitFpsReport? _fpsReport;
    private ToolkitFpsReport? _baselineFps;
    private ToolkitMemorySnapshot? _memoryRead;
    private string _presentMonPath = "";
    private int _capturePid;
    private int _captureSeconds = 30;
    private string _fiveMPath = "";

    private async Task RunFeatureAsync(string title, Func<CancellationToken, Task> work)
    {
        if (_busy) return;
        _busy = true; _stage = title; _analysisStatus.Text = "◌  " + title; _cts = new CancellationTokenSource(); RenderPage();
        try { await work(_cts.Token); _analysisStatus.Text = "●  " + title.TrimEnd('…', '.') + " · concluído"; }
        catch (OperationCanceledException) { Notice("Operação interrompida. Confira o último resultado; etapas já concluídas permanecem registradas."); _analysisStatus.Text = "○  Operação interrompida"; }
        catch (Exception ex) { Notice(title.TrimEnd('…', '.') + ": " + ex.Message, true); _analysisStatus.Text = "○  Operação não concluída"; }
        finally { _busy = false; _cts.Dispose(); _cts = null; RenderPage(); }
    }

    private async Task RecordFeatureActivityAsync(string id, string title, string outcome, string details, OperationStatus status = OperationStatus.Completed)
    {
        if (_smoke) return;
        try
        {
            await _backend.RecordActivityAsync(id, title, outcome + ". " + details, status, CancellationToken.None);
            await LoadHistoryAsync();
        }
        catch (Exception ex) { Notice("Resultado disponível nesta sessão, mas não foi possível registrá-lo no histórico: " + ex.Message, true); }
    }

    private UIElement BuildCleanupPage()
    {
        var root = Stack();
        root.Children.Add(InlineNote("O tamanho vem dos arquivos encontrados.", "Escolha as categorias e faça uma varredura. A exclusão exige revisar os arquivos encontrados e confirmar. Arquivos alterados, em uso, cloud ou links são preservados; a remoção é permanente.", "\uEDA2"));
        var actions = new WrapPanel(); actions.Children.Add(FeatureButton(_busy ? "Varredura em andamento…" : "Verificar seleção", () => _ = ScanCleanupAsync(), true));
        actions.Children.Add(FeatureButton("Revisar Lixeira…", () => _ = ReviewRecycleBinAsync()));
        actions.Children.Add(FeatureButton("Armazenamento do Windows", () => OpenExternal("ms-settings:storagesense"))); root.Children.Add(actions);
        foreach (var category in Toolkit.Cleanup.GetCategories())
        {
            var panel = Stack();
            var select = new CheckBox { Content = Text(category.Title, 15, weight: FontWeights.SemiBold), IsChecked = _cleanupCategories.Contains(category.Id), IsEnabled = !_busy };
            AutomationProperties.SetName(select, "Incluir " + category.Title + " na varredura");
            select.Checked += (_, _) => { _cleanupCategories.Add(category.Id); InvalidateCleanupPreview(); };
            select.Unchecked += (_, _) => { _cleanupCategories.Remove(category.Id); InvalidateCleanupPreview(); };
            panel.Children.Add(select); panel.Children.Add(Text(category.Description, 12, Muted, margin: new Thickness(30, 0, 0, 5)));
            var detail = new Expander { Header = category.MinimumAgeDays > 0 ? $"Locais · apenas arquivos com idade mínima de {category.MinimumAgeDays} dias" : "Locais considerados", Foreground = Muted, FontSize = 11, Margin = new Thickness(30, 5, 0, 0) };
            var paths = Stack(); foreach (var path in category.Roots) paths.Children.Add(Text(path, 11, Muted, margin: new Thickness(0, 5, 0, 0)));
            if (category.BlockingProcesses?.Count > 0) paths.Children.Add(Text("Feche estes aplicativos antes de limpar: " + string.Join(", ", category.BlockingProcesses), 11, AccentSoft, margin: new Thickness(0, 8, 0, 0)));
            detail.Content = paths; panel.Children.Add(detail); var card = Card(panel); card.Padding = new Thickness(18, 10, 18, 14); root.Children.Add(card);
        }
        if (_cleanupPreview != null)
        {
            var preview = _cleanupPreview; var results = Stack();
            results.Children.Add(Chip(preview.Complete ? "VARREDURA CONCLUÍDA" : "VARREDURA PARCIAL", AccentSoft));
            results.Children.Add(Text($"{Bytes(preview.TotalBytes)} em {preview.FileCount:N0} arquivos", 26, weight: FontWeights.Bold, margin: new Thickness(0, 14, 0, 8)));
            results.Children.Add(Text($"Verificação de {preview.CreatedAt.ToLocalTime():dd/MM HH:mm:ss}. A lista pode mudar até a exclusão; cada arquivo será validado novamente.", 12, Muted));
            _cleanupReviewButton = FeatureButton("Revisar arquivos e excluir…", () => _ = ReviewCleanupAsync(), true); results.Children.Add(_cleanupReviewButton);
            AddIssueDetails(results, preview.Issues); root.Children.Add(Card(results, true));
        }
        if (_cleanupResult != null)
        {
            var result = _cleanupResult;
            root.Children.Add(InlineNote($"{Bytes(result.DeletedBytes)} removidos", $"{result.DeletedFiles:N0} arquivos excluídos · {result.SkippedFiles:N0} preservados · {result.FailedFiles:N0} falharam" + (result.Canceled ? " · execução interrompida" : "") + ". O valor considera apenas exclusões confirmadas.", "\uE73E"));
        }
        if (_snapshot?.Disks.Count > 0)
        {
            var volumes = new Expander { Header = "Capacidade dos volumes · última análise", Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 15, 0, 0) };
            var content = Stack(); foreach (var disk in _snapshot.Disks) content.Children.Add(Text($"{disk.Name} · {Bytes(disk.FreeBytes)} livres de {Bytes(disk.TotalBytes)}", 12, Muted, margin: new Thickness(0, 10, 0, 0)));
            volumes.Content = content; root.Children.Add(volumes);
        }
        return root;
    }

    private async Task ScanCleanupAsync()
    {
        if (_cleanupCategories.Count == 0) { Notice("Selecione ao menos uma categoria para verificar."); return; }
        var ids = _cleanupCategories.ToArray();
        await RunFeatureAsync("Verificando arquivos de limpeza…", async token => { _cleanupPreview = await Toolkit.Cleanup.PreviewAsync(ids, token); _cleanupResult = null; });
    }

    private async Task ReviewRecycleBinAsync()
    {
        if (_busy || _smoke) return; ToolkitRecycleBinPreview? preview = null;
        await RunFeatureAsync("Consultando Lixeira do Windows…", async token => preview = await Toolkit.Cleanup.PreviewRecycleBinAsync(token: token));
        if (preview == null) return;
        if (!ConfirmFeature("Revisar esvaziamento da Lixeira?", $"O Windows informou {preview.Items:N0} itens e {Bytes(preview.Bytes)} na Lixeira. Esta consulta usa os totais do Shell, sem listar cada arquivo.\n\nA exclusão é permanente. A confirmação final continua com o Windows. Abra a Lixeira pelo Explorer se precisar revisar os itens individualmente.", "Prosseguir para confirmação do Windows")) return;
        var owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        await RunFeatureAsync("Solicitando esvaziamento da Lixeira…", async token => { var result = await Toolkit.Cleanup.EmptyRecycleBinAsync(preview, owner, token); await RecordFeatureActivityAsync("recyclebin", "Lixeira do Windows", result.Message, $"Restantes: {result.RemainingItems?.ToString() ?? "indisponível"} itens · {result.RemainingBytes?.ToString() ?? "indisponível"} bytes.", result.Success ? OperationStatus.Completed : OperationStatus.Unverified); Notice(result.Message, !result.Success); });
    }

    private void InvalidateCleanupPreview()
    {
        if (_cleanupPreview != null) Notice("A seleção mudou. Faça uma nova verificação para revisar os arquivos das categorias escolhidas.");
        _cleanupPreview = null;
        if (_cleanupReviewButton != null) _cleanupReviewButton.IsEnabled = false;
    }

    private async Task ReviewCleanupAsync()
    {
        if (_cleanupPreview == null || _busy || _smoke) return;
        var preview = _cleanupPreview; var window = Dialog("Revisar exclusão permanente", out var panel, out var footer);
        panel.Children.Add(Text($"Excluir {Bytes(preview.TotalBytes)}?", 25, weight: FontWeights.Bold));
        panel.Children.Add(Text(preview.Consequence, 14, AccentSoft, margin: new Thickness(0, 12, 0, 16)));
        panel.Children.Add(Text($"{preview.FileCount:N0} arquivos encontrados. Os primeiros 150 são apresentados abaixo; a lista completa pode ser exportada antes de confirmar.", 12, Muted));
        foreach (var group in preview.Files.GroupBy(f => f.CategoryId)) panel.Children.Add(Text($"{group.Key}: {group.Count():N0} arquivos · {Bytes(group.Sum(f => f.Bytes))}", 12, margin: new Thickness(0, 9, 0, 0)));
        var files = Stack(); foreach (var file in preview.Files.Take(150)) files.Children.Add(Text($"{Bytes(file.Bytes)} · {file.Path}", 11, Muted, margin: new Thickness(0, 5, 0, 0)));
        panel.Children.Add(new Expander { Header = "Mostrar arquivos", Content = files, Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 14, 0, 10) });
        panel.Children.Add(FeatureButton("Exportar lista completa", () => ExportCleanupList(preview)));
        AddIssueDetails(panel, preview.Issues);
        var consent = Consent("Revisei os locais e entendo que estes arquivos não terão restauração pelo aplicativo."); panel.Children.Add(consent);
        var apply = Button("Excluir arquivos revisados", () => window.DialogResult = true, true); apply.IsEnabled = false;
        consent.Checked += (_, _) => apply.IsEnabled = preview.FileCount > 0; consent.Unchecked += (_, _) => apply.IsEnabled = false;
        var cancel = Button("Voltar", () => window.DialogResult = false); cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 10, 0); footer.Children.Add(cancel); footer.Children.Add(apply);
        if (window.ShowDialog() != true) return;
        await RunFeatureAsync("Excluindo e conferindo arquivos…", async token =>
        {
            _cleanupResult = await Toolkit.Cleanup.ApplyAsync(preview, token); _cleanupPreview = null;
            var result = _cleanupResult;
            var details = $"{result.DeletedFiles} excluídos, {result.SkippedFiles} preservados, {result.FailedFiles} falharam. " + string.Join("; ", result.Issues.Take(8).Select(i => i.Path + ": " + i.Message));
            await RecordFeatureActivityAsync("cleanup", "Limpeza de arquivos", $"{Bytes(result.DeletedBytes)} removidos" + (result.Canceled ? " · interrompida" : ""), details, result.Canceled || result.FailedFiles > 0 ? OperationStatus.PartiallyCompleted : OperationStatus.Completed);
            Notice("Limpeza registrada. Apenas arquivos efetivamente removidos entram no total; consulte as falhas e os itens preservados.");
        });
    }

    private void ExportCleanupList(ToolkitCleanupPreview preview)
    {
        var picker = new SaveFileDialog { FileName = "VL-Limpeza-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json", Filter = "Lista JSON (*.json)|*.json" };
        if (picker.ShowDialog(this) != true) return;
        try { File.WriteAllText(picker.FileName, JsonSerializer.Serialize(preview, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { Notice("Não foi possível exportar a lista: " + ex.Message, true); }
    }

    private static CheckBox Consent(string label) => new() { Content = Text(label, 13), Margin = new Thickness(0, 14, 0, 10) };

    private static void AddIssueDetails(StackPanel panel, IReadOnlyList<ToolkitIssue> issues)
    {
        if (issues.Count == 0) return;
        var details = Stack(); foreach (var issue in issues.Take(100)) details.Children.Add(Text(issue.Path + " · " + issue.Message, 11, Muted, margin: new Thickness(0, 6, 0, 0)));
        if (issues.Count > 100) details.Children.Add(Text($"Mais {issues.Count - 100} observações no resultado completo.", 11, Muted));
        panel.Children.Add(new Expander { Header = $"Locais ignorados e limitações ({issues.Count})", Content = details, Foreground = AccentSoft, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
    }

    private UIElement BuildGamesPage()
    {
        var root = Stack();
        root.Children.Add(FeatureTabBar(["Bibliotecas", "Sessões", "FiveM"], _gamesTab, tab => _gamesTab = tab));
        if (_gamesTab == "Sessões") { root.Children.Add(BuildSessionTool()); return root; }
        root.Children.Add(InlineNote("O perfil acompanha o jogo certo.", "A busca lê bibliotecas locais. Presets FiveM mostram as tags alteradas, registram uma cópia do arquivo original e bloqueiam restauração quando houver conflito.", "\uE7FC"));
        var actions = new WrapPanel(); actions.Children.Add(FeatureButton("Buscar jogos instalados", () => _ = DiscoverGamesAsync(), true)); actions.Children.Add(FeatureButton("Atualizar backups FiveM", () => _ = RefreshFiveMBackupsAsync())); root.Children.Add(actions);
        if (_gamesTab == "Bibliotecas")
        {
        root.Children.Add(Text("Perfil básico: Modo Jogo ligado, DVR desligado e aceleração do mouse desativada. São configurações globais do usuário; o mesmo perfil vale para todos os jogos. Cada ajuste tem prévia e reversão.", 12, Muted, margin: new Thickness(0, 0, 0, 12)));
        root.Children.Add(FeatureButton("Revisar perfil básico de jogos", () => _ = ReviewRequestsAsync(new[] { "game-mode", "dvr-off", "mouse-accel" }.Select(id => new OperationRequest(OperationKind.Configure, id, true)).ToArray())));
        root.Children.Add(BuildGameGraphicsHistory());
        if (_gameDiscovery == null) root.Children.Add(Empty("\uE721", "Busque as instalações deste PC.", "Bibliotecas Steam, Epic e locais FiveM são verificados sob demanda. Nenhum jogo é iniciado durante a busca."));
        else
        {
            root.Children.Add(SectionTitle("Instalações encontradas", _gameDiscovery.Games.Count + " jogos"));
            if (_gameDiscovery.Games.Count == 0) root.Children.Add(Empty("\uE7FC", "Nenhuma instalação reconhecida.", "O jogo pode estar em uma biblioteca não registrada. Na aba FiveM, você também pode escolher manualmente o settings.xml da sua instalação."));
            foreach (var game in _gameDiscovery.Games)
            {
                var content = Stack(); content.Children.Add(Text(game.Name, 17, weight: FontWeights.SemiBold));
                content.Children.Add(Text(game.Source + (game.IsRunning ? " · em execução" : " · instalação local"), 11, AccentSoft, margin: new Thickness(0, 5, 0, 8)));
                content.Children.Add(Text(game.InstallDirectory, 12, Muted));
                BuildGameGraphicsActions(content, game);
                var detail = Stack(); foreach (var executable in game.Executables) detail.Children.Add(Text(executable, 11, Muted, margin: new Thickness(0, 5, 0, 0)));
                detail.Children.Add(FeatureButton("Abrir pasta do jogo", () => OpenExternal(game.InstallDirectory)));
                content.Children.Add(new Expander { Header = "Executáveis e pasta", Content = detail, Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) }); root.Children.Add(Card(content));
            }
            AddIssueDetails(root, _gameDiscovery.Issues);
        }
        return root;
        }
        root.Children.Add(SectionTitle("Presets FiveM", "Cópia de segurança antes da escrita"));
        var fivem = Stack(); fivem.Children.Add(Text("Arquivo de configuração", 13, weight: FontWeights.SemiBold));
        if (_fiveMPath.Length == 0) _fiveMPath = Toolkit.FiveM.FindSettingsPaths().FirstOrDefault() ?? "";
        fivem.Children.Add(Text(_fiveMPath.Length == 0 ? "Nenhum settings.xml localizado. Selecione o arquivo da sua instalação." : _fiveMPath, 12, Muted, margin: new Thickness(0, 8, 0, 12)));
        var presets = new WrapPanel(); presets.Children.Add(FeatureButton("Selecionar settings.xml…", PickFiveMPath));
        foreach (var preset in Toolkit.FiveM.GetPresets())
        {
            var button = FeatureButton(preset == "fps" ? "Revisar preset de FPS" : "Revisar preset equilibrado", () => _ = ReviewFiveMAsync(preset));
            button.IsEnabled = !_busy && _fiveMPath.Length > 0; presets.Children.Add(button);
        }
        fivem.Children.Add(presets); fivem.Children.Add(Text("Feche o jogo antes de aplicar. Ajustar qualidade e resolução pode mudar a imagem e o uso de recursos; o resultado de FPS precisa ser medido.", 12, Muted)); root.Children.Add(Card(fivem));
        if (_fiveMBackups.Count > 0)
        {
            root.Children.Add(SectionTitle("Backups FiveM", "Vinculados ao arquivo original"));
            foreach (var backup in _fiveMBackups.OrderByDescending(b => b.CreatedAt))
            {
                var content = Stack(); content.Children.Add(Text(backup.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") + (backup.Restored ? " · restaurado" : " · disponível"), 14, weight: FontWeights.SemiBold)); content.Children.Add(Text(backup.Path, 12, Muted, margin: new Thickness(0, 6, 0, 12)));
                if (!backup.Restored) content.Children.Add(FeatureButton("Revisar restauração FiveM", () => _ = RestoreFiveMAsync(backup))); root.Children.Add(Card(content));
            }
        }
        return root;
    }

    private void PickFiveMPath()
    {
        if (_smoke) return; var picker = new OpenFileDialog { Title = "Selecionar configuração FiveM/GTA", Filter = "Configuração XML (*.xml)|*.xml", CheckFileExists = true };
        if (picker.ShowDialog(this) == true) { _fiveMPath = picker.FileName; RenderPage(); }
    }

    private async Task DiscoverGamesAsync() => await RunFeatureAsync("Buscando jogos locais…", async token => { _gameDiscovery = await Toolkit.Games.DiscoverAsync(token); _fiveMBackups = await Toolkit.FiveM.GetBackupsAsync(token); });
    private async Task RefreshFiveMBackupsAsync() => await RunFeatureAsync("Lendo cópias de segurança FiveM…", async token => _fiveMBackups = await Toolkit.FiveM.GetBackupsAsync(token));

    private async Task ReviewFiveMAsync(string preset)
    {
        if (_busy || _smoke) return; ToolkitFiveMPreview? preview = null;
        await RunFeatureAsync("Preparando prévia FiveM…", async token => preview = await Toolkit.FiveM.PreviewPresetAsync(_fiveMPath, preset, null, null, token));
        if (preview == null) return;
        var window = Dialog("Revisar preset FiveM", out var panel, out var footer);
        panel.Children.Add(Text(preset == "fps" ? "Preset de FPS" : "Preset equilibrado", 25, weight: FontWeights.Bold));
        panel.Children.Add(Text(preview.Path, 12, Muted, margin: new Thickness(0, 10, 0, 18)));
        foreach (var change in preview.Changes) panel.Children.Add(Text($"{change.Element}: {change.Before ?? "ausente"} → {change.After}" + (change.WillCreate ? " · criar tag" : ""), 13, margin: new Thickness(0, 7, 0, 0)));
        panel.Children.Add(Text("O arquivo será verificado novamente e uma cópia anterior ficará vinculada a este caminho. Mudanças externas interrompem a aplicação. Feche o jogo primeiro.", 13, Muted, margin: new Thickness(0, 18, 0, 14)));
        var consent = Consent("Revisei as alterações e fechei o jogo."); panel.Children.Add(consent);
        var apply = Button("Aplicar preset e criar backup", () => window.DialogResult = true, true); apply.IsEnabled = false;
        consent.Checked += (_, _) => apply.IsEnabled = preview.Changes.Count > 0; consent.Unchecked += (_, _) => apply.IsEnabled = false;
        var back = Button("Voltar", () => window.DialogResult = false); back.IsCancel = true; back.Margin = new Thickness(0, 0, 10, 0); footer.Children.Add(back); footer.Children.Add(apply);
        if (window.ShowDialog() != true) return;
        await RunFeatureAsync("Aplicando e verificando preset FiveM…", async token => { var result = await Toolkit.FiveM.ApplyAsync(preview, token); await RecordFeatureActivityAsync("fivem", "Preset FiveM · " + preset, result.Success ? "Aplicado e verificado" : result.Conflict ? "Conflito" : "Não aplicado", result.Message + " Restauração disponível na página Jogos e FiveM quando houver backup.", result.Success ? OperationStatus.Completed : result.Conflict ? OperationStatus.Conflict : OperationStatus.Failed); Notice(result.Message, !result.Success); _fiveMBackups = await Toolkit.FiveM.GetBackupsAsync(CancellationToken.None); });
    }

    private async Task RestoreFiveMAsync(ToolkitFiveMBackup backup)
    {
        if (_busy || _smoke || !ConfirmFeature("Restaurar configuração FiveM?", backup.Path + "\n\nO estado atual será comparado com a última aplicação. Se o jogo ou outro programa alterou o arquivo, a restauração será bloqueada.", "Restaurar cópia anterior")) return;
        await RunFeatureAsync("Restaurando configuração FiveM…", async token => { var result = await Toolkit.FiveM.RestoreAsync(backup.Id, token); await RecordFeatureActivityAsync("fivem-restore", "Restauração FiveM", result.Success ? "Restaurado" : result.Conflict ? "Conflito" : "Falhou", result.Message, result.Success ? OperationStatus.Reverted : result.Conflict ? OperationStatus.Conflict : OperationStatus.Failed); Notice(result.Message, !result.Success); _fiveMBackups = await Toolkit.FiveM.GetBackupsAsync(CancellationToken.None); });
    }

    private bool ConfirmFeature(string title, string description, string action)
    {
        var window = Dialog(title, out var panel, out var footer); panel.Children.Add(Text(title, 24, weight: FontWeights.Bold)); panel.Children.Add(Text(description, 14, Muted, margin: new Thickness(0, 16, 0, 10)));
        var back = Button("Voltar", () => window.DialogResult = false); back.IsCancel = true; back.Margin = new Thickness(0, 0, 10, 0); footer.Children.Add(back); footer.Children.Add(Button(action, () => window.DialogResult = true, true)); return window.ShowDialog() == true;
    }
}
