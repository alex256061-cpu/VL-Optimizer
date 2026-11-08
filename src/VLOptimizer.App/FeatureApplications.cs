using System.Windows;
using System.Windows.Controls;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private IReadOnlyList<ToolkitUninstallResult> _uninstallHistory = [];
    private async Task RefreshInstalledAppsAsync() => await RunFeatureAsync("Lendo inventário e desinstalações…", async token => { _installedApps = await Toolkit.Inventory.ReadInstalledAppsAsync(token); _uninstallHistory = await Toolkit.Applications.GetHistoryAsync(token); });

    private async Task ReviewUninstallAsync(string id)
    {
        if (_busy || _smoke) return; ToolkitUninstallPreview? preview = null;
        await RunFeatureAsync("Consultando desinstalador registrado…", async token => preview = await Toolkit.Applications.PreviewUninstallAsync(id, token));
        if (preview == null) return;
        var window = Dialog("Revisar desinstalação", out var panel, out var footer); panel.Children.Add(Text(preview.Application.Name, 25, weight: FontWeights.Bold));
        panel.Children.Add(Text("O assistente registrado pelo programa será iniciado. Ele pode abrir outra janela, solicitar elevação e remover dados ou configurações conforme suas opções. O VL não possui desfazer dessa desinstalação.", 14, Muted, margin: new Thickness(0, 14, 0, 18)));
        panel.Children.Add(Text("Desinstalador: " + preview.Executable, 12)); panel.Children.Add(Text("Argumentos registrados: " + string.Join(" ", preview.Arguments), 12, Muted, margin: new Thickness(0, 8, 0, 12)));
        panel.Children.Add(Text("Cancelar a espera no VL não encerra o assistente do fabricante. A conclusão exige código de saída aceito e desaparecimento da entrada de instalação.", 12, AccentSoft));
        var consent = Consent("Quero iniciar o assistente de desinstalação deste programa."); panel.Children.Add(consent);
        var execute = Button("Abrir desinstalador revisado", () => window.DialogResult = true, true); execute.IsEnabled = false; consent.Checked += (_, _) => execute.IsEnabled = true; consent.Unchecked += (_, _) => execute.IsEnabled = false;
        var back = Button("Voltar", () => window.DialogResult = false); back.IsCancel = true; back.Margin = new Thickness(0, 0, 10, 0); footer.Children.Add(back); footer.Children.Add(execute);
        if (window.ShowDialog() != true) return;
        await RunFeatureAsync("Aguardando resultado do desinstalador…", async token =>
        {
            var result = await Toolkit.Applications.UninstallAsync(preview, token); var status = result.Status switch { ToolkitUninstallStatus.Completed => OperationStatus.Completed, ToolkitUninstallStatus.RestartRequired => OperationStatus.RestartRequired, ToolkitUninstallStatus.Failed => OperationStatus.Failed, _ => OperationStatus.Unverified };
            await RecordFeatureActivityAsync("uninstall", "Desinstalação · " + result.Name, result.Message, "Código: " + result.ExitCode, status); _installedApps = await Toolkit.Inventory.ReadInstalledAppsAsync(CancellationToken.None); _uninstallHistory = await Toolkit.Applications.GetHistoryAsync(CancellationToken.None); Notice(result.Message, status == OperationStatus.Failed);
        });
    }

    private void AddUninstallHistory(StackPanel root)
    {
        if (_uninstallHistory.Count == 0) return;
        var items = Stack(); foreach (var record in _uninstallHistory.OrderByDescending(h => h.CreatedAt))
        {
            var item = Stack(); item.Children.Add(Text(record.Name + " · " + UninstallLabel(record.Status), 15, weight: FontWeights.SemiBold)); item.Children.Add(Text(record.Message, 12, Muted, margin: new Thickness(0, 7, 0, 12)));
            if (record.Status is ToolkitUninstallStatus.Completed or ToolkitUninstallStatus.RestartRequired) item.Children.Add(FeatureButton("Verificar pastas restantes…", () => _ = ReviewLeftoversAsync(record.ApplicationId)));
            items.Children.Add(Card(item));
        }
        root.Children.Add(new Expander { Header = $"Desinstalações pelo VL e análise de pastas ({_uninstallHistory.Count})", Content = items, Foreground = Muted, FontSize = 13, IsExpanded = true, Margin = new Thickness(0, 0, 0, 16) });
    }
    private static string UninstallLabel(ToolkitUninstallStatus status) => status switch { ToolkitUninstallStatus.Completed => "Concluída", ToolkitUninstallStatus.RestartRequired => "Reinicialização necessária", ToolkitUninstallStatus.Failed => "Falhou", ToolkitUninstallStatus.Prepared => "Preparada", ToolkitUninstallStatus.Started => "Iniciada · conferir resultado", _ => "Não verificada" };

    private async Task ReviewLeftoversAsync(string id)
    {
        if (_busy || _smoke) return; ToolkitLeftoversPreview? preview = null;
        await RunFeatureAsync("Verificando pastas após desinstalação…", async token => preview = await Toolkit.Applications.PreviewLeftoversAsync(id, token));
        if (preview == null) return;
        var window = Dialog("Revisar arquivos restantes", out var panel, out var footer); panel.Children.Add(Text(preview.ApplicationName, 25, weight: FontWeights.Bold));
        panel.Children.Add(Text($"{preview.Files.FileCount:N0} arquivos · {Bytes(preview.Files.TotalBytes)} encontrados em pastas candidatas.", 14, Muted, margin: new Thickness(0, 12, 0, 14)));
        panel.Children.Add(Text("Uma pasta parecida com o nome do programa pode conter dados que você deseja guardar. Confira os caminhos e faça uma cópia se precisar. A exclusão é permanente e não possui restauração no VL.", 13, AccentSoft));
        foreach (var path in preview.CandidateRoots) panel.Children.Add(Text(path, 12, Muted, margin: new Thickness(0, 8, 0, 0)));
        var files = Stack(); foreach (var file in preview.Files.Files.Take(150)) files.Children.Add(Text(Bytes(file.Bytes) + " · " + file.Path, 11, Muted, margin: new Thickness(0, 5, 0, 0)));
        panel.Children.Add(new Expander { Header = "Primeiros 150 arquivos", Content = files, Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 14, 0, 12) }); panel.Children.Add(FeatureButton("Exportar lista completa", () => ExportCleanupList(preview.Files)));
        AddIssueDetails(panel, preview.Files.Issues);
        var consent = Consent("Revisei os arquivos e aprovo a exclusão permanente da lista encontrada."); panel.Children.Add(consent);
        var execute = Button("Excluir arquivos revisados", () => window.DialogResult = true, true); execute.IsEnabled = false; consent.Checked += (_, _) => execute.IsEnabled = preview.Files.FileCount > 0; consent.Unchecked += (_, _) => execute.IsEnabled = false;
        var back = Button("Voltar", () => window.DialogResult = false); back.IsCancel = true; back.Margin = new Thickness(0, 0, 10, 0); footer.Children.Add(back); footer.Children.Add(execute);
        if (window.ShowDialog() != true) return;
        await RunFeatureAsync("Excluindo arquivos restantes revisados…", async token => { var result = await Toolkit.Applications.ApplyLeftoversAsync(preview, token); await RecordFeatureActivityAsync("leftovers", "Arquivos após desinstalação · " + preview.ApplicationName, $"{result.DeletedFiles} arquivos · {Bytes(result.DeletedBytes)} removidos", $"{result.SkippedFiles} preservados, {result.FailedFiles} falharam.", result.FailedFiles > 0 || result.Canceled ? OperationStatus.PartiallyCompleted : OperationStatus.Completed); Notice($"{Bytes(result.DeletedBytes)} removidos. {result.SkippedFiles} arquivos preservados; {result.FailedFiles} falharam."); });
    }

    private async Task ReviewKillProcessAsync(int pid)
    {
        if (_busy || _smoke) return; ToolkitKillProcessPreview? preview = null;
        await RunFeatureAsync("Conferindo identidade do processo…", async token => preview = await Toolkit.Applications.PreviewKillProcessAsync(pid, token));
        if (preview == null) return;
        if (!ConfirmFeature("Encerrar processo escolhido?", preview.Process.Name + " · PID " + preview.Process.Identity.Id + "\n" + preview.Process.Identity.ExecutablePath + "\n\n" + preview.Consequence + "\n\nO caminho e o horário de início serão conferidos antes de encerrar; processos críticos são bloqueados.", "Encerrar somente este processo")) return;
        await RunFeatureAsync("Encerrando processo revisado…", async token => { var result = await Toolkit.Applications.KillAsync(preview, token); await RecordFeatureActivityAsync("process-end", "Encerramento · " + preview.Process.Name, result.Message, "PID " + preview.Process.Identity.Id, result.Success ? OperationStatus.Completed : result.Conflict ? OperationStatus.Conflict : OperationStatus.Failed); Notice(result.Message, !result.Success); });
    }
}
