using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private OptiScalerService? _optiScalerService;
    private OptiScalerService OptiScaler => _optiScalerService ??= new(_backend.DataDirectory);
    private string _optiScalerGame = "";
    private string _optiScalerPackage = "";
    private string _optiScalerProxy = "dxgi.dll";
    private IReadOnlyList<OptiScalerBackup> _optiScalerBackups = [];

    private void AddOptiScalerSection(StackPanel root)
    {
        root.Children.Add(SectionTitle("OptiScaler · pacote local", "Instalação com prévia e backup"));
        var content = Stack(); content.Children.Add(Text("Instale um pacote que você já possui.", 18, weight: FontWeights.SemiBold));
        content.Children.Add(Text("Selecione o executável real do jogo e a pasta descompactada com OptiScaler.dll e OptiScaler.ini. O VL mostra cada arquivo antes da cópia e preserva os arquivos substituídos. A compatibilidade gráfica depende do jogo e do pacote.", 13, Muted, margin: new Thickness(0, 10, 0, 16)));
        content.Children.Add(Text("Jogo: " + (_optiScalerGame.Length == 0 ? "nenhum executável escolhido" : _optiScalerGame), 12, Muted));
        content.Children.Add(Text("Pacote: " + (_optiScalerPackage.Length == 0 ? "nenhuma pasta escolhida" : _optiScalerPackage), 12, Muted, margin: new Thickness(0, 7, 0, 13)));
        var choices = new WrapPanel(); choices.Children.Add(FeatureButton("Escolher jogo…", PickOptiScalerGame)); choices.Children.Add(FeatureButton("Escolher pacote local…", PickOptiScalerPackage));
        var proxy = NewChoice("Nome da DLL de integração", OptiScalerService.ProxyNames.Select(n => (n, n)).ToArray(), _optiScalerProxy, value => _optiScalerProxy = value); proxy.Margin = new Thickness(0, 0, 12, 12); choices.Children.Add(proxy); content.Children.Add(choices);
        var preview = FeatureButton("Verificar pacote e revisar instalação", () => _ = ReviewOptiScalerAsync(), true); preview.IsEnabled = !_busy && _optiScalerGame.Length > 0 && _optiScalerPackage.Length > 0; content.Children.Add(preview);
        root.Children.Add(Card(content));
        foreach (var backup in _optiScalerBackups.OrderByDescending(b => b.CreatedAt))
        {
            var item = Stack(); item.Children.Add(Text(backup.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm") + (backup.Restored ? " · restaurado" : " · backup disponível"), 15, weight: FontWeights.SemiBold)); item.Children.Add(Text(backup.GameExecutable + $" · {backup.Files} arquivos", 12, Muted, margin: new Thickness(0, 8, 0, 12)));
            if (!backup.Restored) item.Children.Add(FeatureButton("Revisar restauração OptiScaler", () => _ = RestoreOptiScalerAsync(backup))); root.Children.Add(Card(item));
        }
    }

    private async Task LoadOptiScalerBackupsAsync(CancellationToken token) => _optiScalerBackups = await OptiScaler.GetBackupsAsync(token);
    private void PickOptiScalerGame()
    {
        if (_busy || _smoke) return; var picker = new OpenFileDialog { Title = "Selecionar o executável real do jogo", Filter = "Executável do jogo (*.exe)|*.exe", CheckFileExists = true };
        if (picker.ShowDialog(this) == true) { _optiScalerGame = picker.FileName; RenderPage(); }
    }
    private void PickOptiScalerPackage()
    {
        if (_busy || _smoke) return; var picker = new OpenFolderDialog { Title = "Pasta descompactada do pacote OptiScaler" };
        if (picker.ShowDialog(this) == true) { _optiScalerPackage = picker.FolderName; RenderPage(); }
    }
    private async Task ReviewOptiScalerAsync()
    {
        if (_busy || _smoke) return; OptiScalerPreview? preview = null;
        await RunFeatureAsync("Verificando pacote e arquivos do jogo…", async token => preview = await OptiScaler.PreviewInstallAsync(_optiScalerGame, _optiScalerPackage, _optiScalerProxy, token));
        if (preview == null) return;
        var window = Dialog("Revisar instalação OptiScaler", out var panel, out var footer); panel.Children.Add(Text("Confira cada arquivo antes da cópia.", 24, weight: FontWeights.Bold));
        panel.Children.Add(Text(preview.GameExecutable, 12, Muted, margin: new Thickness(0, 12, 0, 14)));
        if (!preview.CanApply) panel.Children.Add(Text(preview.BlockReason ?? "A instalação não está disponível para esta seleção.", 14, AccentSoft));
        foreach (var file in preview.Files) panel.Children.Add(Text($"{file.RelativePath} · {Bytes(file.Bytes)} · " + (file.BeforeSha256 == null ? "criar" : "substituir com backup"), 13, margin: new Thickness(0, 6, 0, 0)));
        foreach (var warning in preview.Warnings) panel.Children.Add(Text(warning, 12, AccentSoft, margin: new Thickness(0, 12, 0, 0)));
        var consent = Consent("Revisei os arquivos e confirmei a compatibilidade do pacote com este jogo."); consent.IsEnabled = preview.CanApply; panel.Children.Add(consent);
        var apply = Button("Instalar pacote e preservar backup", () => window.DialogResult = true, true); apply.IsEnabled = false;
        consent.Checked += (_, _) => apply.IsEnabled = preview.CanApply; consent.Unchecked += (_, _) => apply.IsEnabled = false;
        var cancel = Button("Voltar", () => window.DialogResult = false); cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 10, 0); footer.Children.Add(cancel); footer.Children.Add(apply);
        if (window.ShowDialog() != true) return;
        await RunFeatureAsync("Copiando e verificando pacote OptiScaler…", async token => { var result = await OptiScaler.ApplyAsync(preview, token); await RecordFeatureActivityAsync("optiscaler-install", "Instalação OptiScaler", result.Success ? "Instalado e verificado" : result.Partial ? "Instalação parcial" : result.Conflict ? "Conflito" : "Falhou", result.Message + " Cópia anterior disponível na página Scaling quando houver backup.", OptiScalerStatus(result)); await LoadOptiScalerBackupsAsync(CancellationToken.None); Notice(result.Message, !result.Success); });
    }
    private async Task RestoreOptiScalerAsync(OptiScalerBackup backup)
    {
        if (_busy || _smoke || !ConfirmFeature("Restaurar arquivos anteriores?", backup.GameExecutable + "\n\nO estado instalado será verificado antes de restaurar cada arquivo. Feche o jogo. Arquivos alterados após a instalação podem bloquear a restauração.", "Restaurar backup OptiScaler")) return;
        await RunFeatureAsync("Restaurando arquivos anteriores do jogo…", async token => { var result = await OptiScaler.RestoreAsync(backup.Id, token); await RecordFeatureActivityAsync("optiscaler-restore", "Restauração OptiScaler", result.Success ? "Restaurado" : "Restauração não concluída", result.Message, result.Success ? OperationStatus.Reverted : OptiScalerStatus(result)); await LoadOptiScalerBackupsAsync(CancellationToken.None); Notice(result.Message, !result.Success); });
    }
    private static OperationStatus OptiScalerStatus(OptiScalerResult result) => result.Success ? OperationStatus.Completed : result.Conflict ? OperationStatus.Conflict : result.Partial ? OperationStatus.PartiallyCompleted : OperationStatus.Failed;
}
