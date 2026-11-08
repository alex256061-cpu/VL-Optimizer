using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private MaintenanceService? _maintenanceService;
    private MaintenanceService Maintenance => _maintenanceService ??= new();
    private MaintenanceResult? _maintenanceResult;
    private bool _maintenanceRunning;

    private UIElement BuildMaintenanceTool()
    {
        var root = Stack(); root.Children.Add(InlineNote("Manutenção com resultado por ferramenta.", "Reparos e resets são ações explícitas. Leia os efeitos, confira os privilégios e revise a saída do Windows. Estas ferramentas não possuem reversão automática no VL.", "\uE90F"));
        if (_snapshot?.IsAdministrator == false) root.Children.Add(FeatureButton("Reabrir como administrador", RelaunchAsAdministrator));
        foreach (var definition in Maintenance.GetDefinitions())
        {
            var content = Stack(); content.Children.Add(Text(definition.Title, 18, weight: FontWeights.SemiBold)); content.Children.Add(Text(definition.Description, 13, Muted, margin: new Thickness(0, 8, 0, 10)));
            content.Children.Add(Text(definition.Warning, 12, AccentSoft, margin: new Thickness(0, 0, 0, 13)));
            content.Children.Add(FeatureButton("Revisar " + definition.Title.ToLowerInvariant(), () => _ = ReviewMaintenanceAsync(definition.Kind)));
            if (definition.RequiresAdmin) content.Children.Add(Text("Requer administrador" + (definition.RequiresRestart ? " · reinicialização necessária" : ""), 11, Muted)); root.Children.Add(Card(content));
        }
        if (_maintenanceResult != null)
        {
            var result = Stack(); result.Children.Add(Text(StatusLabel(_maintenanceResult.Status), 22, weight: FontWeights.Bold)); result.Children.Add(Text(_maintenanceResult.Message, 13, Muted, margin: new Thickness(0, 9, 0, 14)));
            foreach (var stage in _maintenanceResult.Stages)
            {
                var output = new TextBox { Text = stage.Output, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 280, MinHeight = 90, FontSize = 12, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                result.Children.Add(new Expander { Header = StageTitle(stage.Kind) + " · código " + (stage.ExitCode?.ToString() ?? "indisponível") + (stage.TimedOut ? " · prazo de observação excedido" : ""), Content = output, Foreground = Muted, FontSize = 12, IsExpanded = !stage.Accepted, Margin = new Thickness(0, 0, 0, 12) });
            }
            root.Children.Add(Card(result, true));
        }
        return root;
    }
    private static string StageTitle(MaintenanceStageKind stage) => stage switch {
        MaintenanceStageKind.FlushDns => "Cache DNS", MaintenanceStageKind.ResetWinsock => "Reset Winsock", MaintenanceStageKind.ResetIp => "Reset TCP/IP", MaintenanceStageKind.DismRepair => "DISM · reparo da imagem", MaintenanceStageKind.SfcScan => "SFC · arquivos do sistema", MaintenanceStageKind.DismCleanup => "DISM · limpeza de componentes", MaintenanceStageKind.RestorePoint => "Ponto de restauração", _ => "Explorer do usuário atual" };

    private async Task ReviewMaintenanceAsync(MaintenanceKind kind)
    {
        if (_busy || _smoke) return; MaintenancePreview? preview = null;
        await RunFeatureAsync("Preparando prévia de manutenção…", async token => preview = await Maintenance.PreviewAsync(kind, token));
        if (preview == null) return;
        var window = Dialog("Revisar manutenção do Windows", out var panel, out var footer); panel.Children.Add(Text(preview.Definition.Title, 25, weight: FontWeights.Bold));
        panel.Children.Add(Text(preview.Definition.Description, 14, Muted, margin: new Thickness(0, 12, 0, 14))); panel.Children.Add(Text(preview.Definition.Warning, 13, AccentSoft));
        foreach (var stage in preview.Stages) panel.Children.Add(Text("• " + StageTitle(stage), 13, margin: new Thickness(0, 12, 0, 0)));
        if (!preview.CanApply) panel.Children.Add(Text(preview.BlockReason ?? "Esta manutenção não está disponível nesta sessão.", 13, AccentSoft, margin: new Thickness(0, 15, 0, 0)));
        var consent = Consent("Revisei os efeitos e aprovo executar esta manutenção agora."); consent.IsEnabled = preview.CanApply; panel.Children.Add(consent);
        var execute = Button("Executar manutenção revisada", () => window.DialogResult = true, true); execute.IsEnabled = false;
        consent.Checked += (_, _) => execute.IsEnabled = preview.CanApply; consent.Unchecked += (_, _) => execute.IsEnabled = false;
        var back = Button("Voltar", () => window.DialogResult = false); back.IsCancel = true; back.Margin = new Thickness(0, 0, 10, 0); footer.Children.Add(back); footer.Children.Add(execute);
        if (window.ShowDialog() != true) return;
        await RunFeatureAsync("Executando manutenção do Windows…", async token =>
        {
            _maintenanceRunning = true;
            try
            {
                await RecordFeatureActivityAsync("maintenance-start-" + preview.Id, preview.Definition.Title + " · início", "Execução autorizada", "Uma ferramenta Windows pode estar em andamento. Consulte o registro final e os logs do Windows antes de repetir se houver interrupção.", OperationStatus.Unverified);
                var progress = new Progress<string>(message => _analysisStatus.Text = "◌  " + message);
                _maintenanceResult = await Maintenance.ExecuteAsync(preview, progress, token);
                var output = string.Join("\n\n", _maintenanceResult.Stages.Select(s => StageTitle(s.Kind) + " · código " + s.ExitCode + "\n" + s.Output));
                if (output.Length > 30000) output = output[..30000] + "\n[Saída limitada no histórico; consulte os logs oficiais do Windows.]";
                await RecordFeatureActivityAsync("maintenance-" + preview.Id, preview.Definition.Title, _maintenanceResult.Message, output, _maintenanceResult.Status);
                Notice(_maintenanceResult.Message, _maintenanceResult.Status is OperationStatus.Failed or OperationStatus.PartiallyCompleted);
            }
            finally { _maintenanceRunning = false; }
        });
    }

    private void RelaunchAsAdministrator()
    {
        if (_busy || _smoke) return;
        if (_sessionMonitorTask is { IsCompleted: false }) { Notice("Pare o monitor de jogos antes de reabrir o aplicativo como administrador."); return; }
        try
        {
            var executable = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "VLOptimizer.exe"));
            if (!File.Exists(executable) || !System.IO.Path.IsPathFullyQualified(executable)) throw new FileNotFoundException("O executável próprio do VL não foi localizado.", executable);
            var process = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory });
            if (process != null) Close(); else Notice("A nova sessão não foi confirmada. A sessão atual permanece aberta.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { Notice("Elevação cancelada. A sessão atual permanece aberta."); }
        catch (Exception ex) { Notice("Não foi possível reabrir com privilégios: " + ex.Message, true); }
    }
}
