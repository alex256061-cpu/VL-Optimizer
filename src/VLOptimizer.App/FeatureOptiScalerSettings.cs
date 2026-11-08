using System.Windows;
using System.Windows.Controls;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private OptiScalerSettingsService? _optiSettingsService;
    private OptiScalerSettingsService OptiSettings => _optiSettingsService ??= new(_backend.DataDirectory);
    private IReadOnlyDictionary<string, string> _optiSettingsValues = new Dictionary<string, string>();
    private readonly Dictionary<string, string> _optiSettingsChanges = [];
    private string _optiSettingsGame = "";
    private IReadOnlyList<OptiSettingsBackup> _optiSettingsBackups = [];
    private void AddOptiScalerSettingsSection(StackPanel root)
    {
        root.Children.Add(SectionTitle("Opções do pacote instalado", "Prévia e recuperação do INI"));
        var panel = Stack(); panel.Children.Add(Text("Leia a versão que está no jogo.", 18, weight: FontWeights.SemiBold));
        panel.Children.Add(Text("O editor modifica somente chaves reconhecidas que já existem no OptiScaler.ini. Preserva comentários, codificação e opções desconhecidas. Feche o jogo. Restaure estas opções antes de remover a instalação OptiScaler.", 13, Muted, margin: new Thickness(0, 9, 0, 14)));
        var actions = new WrapPanel(); actions.Children.Add(FeatureButton("Escolher jogo…", PickOptiScalerGame));
        actions.Children.Add(FeatureButton("Ler opções e backups", () => _ = ReadOptiSettingsAsync(), true)); panel.Children.Add(actions);
        if (_optiSettingsGame == _optiScalerGame && _optiSettingsGame.Length > 0)
        {
            foreach (var field in OptiScalerSettingsService.Definitions)
            {
                if (!_optiSettingsValues.TryGetValue(field.Id, out var value)) { panel.Children.Add(Text(field.Title + " · chave não presente nesta versão", 12, Muted, margin: new Thickness(0, 6, 0, 6))); continue; }
                var choices = new[] { ("__keep", "Manter · " + OptiValueTitle(value)) }.Concat(field.Values.Select(v => (v, OptiValueTitle(v)))).ToArray();
                panel.Children.Add(NewChoice(field.Title, choices, _optiSettingsChanges.GetValueOrDefault(field.Id) ?? "__keep", selected => { if (selected == "__keep") _optiSettingsChanges.Remove(field.Id); else _optiSettingsChanges[field.Id] = selected; }));
            }
            panel.Children.Add(FeatureButton("Revisar alterações do INI…", () => _ = ReviewOptiSettingsAsync(), true));
        }
        root.Children.Add(Card(panel));
        foreach (var backup in _optiSettingsBackups)
        {
            var item = Stack(); item.Children.Add(Text(backup.At.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") + " · " + backup.Game, 12, Muted));
            if (!backup.Restored) item.Children.Add(FeatureButton("Revisar restauração das opções", () => _ = RestoreOptiSettingsAsync(backup)));
            else item.Children.Add(Text("Restaurado", 12, Muted)); root.Children.Add(Card(item));
        }
    }
    private static string OptiValueTitle(string value) => value.ToLowerInvariant() switch {
        "auto" => "Automático pelo pacote", "true" => "Ligado", "false" => "Desligado", "0x24" => "Home", "0x2d" => "Insert", "0x23" => "End", "-1" => "Sem atalho",
        "nofg" => "Sem geração", "ffx" => "FidelityFX · versão do pacote", "dlss" => "NVIDIA DLSS", "dlssg" => "DLSS Frame Generation", "xefg" => "XeSS Frame Generation", "fsrfg" => "FSR Frame Generation", _ => value };
    private async Task ReadOptiSettingsAsync() => await RunFeatureAsync("Lendo opções OptiScaler…", async token =>
    {
        _optiSettingsBackups = await OptiSettings.GetBackupsAsync(token);
        if (string.IsNullOrWhiteSpace(_optiScalerGame)) { Notice("Escolha o executável para ler suas opções. Os backups conhecidos continuam listados."); return; }
        _optiSettingsValues = await OptiSettings.ReadAsync(_optiScalerGame, token); _optiSettingsGame = _optiScalerGame; _optiSettingsChanges.Clear();
    });
    private async Task ReviewOptiSettingsAsync()
    {
        if (_busy || _smoke || _optiSettingsChanges.Count == 0) return; OptiSettingsPreview? preview = null;
        await RunFeatureAsync("Preparando prévia do INI…", async token => preview = await OptiSettings.PreviewAsync(_optiScalerGame, new Dictionary<string, string>(_optiSettingsChanges), token));
        if (preview == null || !ConfirmFeature("Modificar opções OptiScaler?", preview.Game + "\n\n" + string.Join("\n", preview.Changes.Select(c => c.Key + ": " + _optiSettingsValues.GetValueOrDefault(c.Key) + " → " + c.Value)) + "\n\nUm backup exato será gravado antes da escrita. Bibliotecas, hardware e jogo determinam o suporte; geração de quadros não garante menor latência.", "Gravar com backup")) return;
        await RunFeatureAsync("Gravando e verificando INI…", async token =>
        {
            var result = await OptiSettings.ApplyAsync(preview, token); await RecordFeatureActivityAsync("optiscaler-settings", "Opções OptiScaler", StatusLabel(result.Status), result.Message, result.Status);
            _optiSettingsBackups = await OptiSettings.GetBackupsAsync(CancellationToken.None); _optiSettingsChanges.Clear(); Notice(result.Message, result.Status != OperationStatus.Completed);
            if (result.Status == OperationStatus.Completed) _optiSettingsValues = await OptiSettings.ReadAsync(preview.Game, CancellationToken.None);
        });
    }
    private async Task RestoreOptiSettingsAsync(OptiSettingsBackup backup)
    {
        if (_busy || _smoke || !ConfirmFeature("Restaurar opções do pacote?", backup.Game + "\n\nEdições posteriores são preservadas; backups sobrepostos exigem ordem inversa. O jogo deve estar fechado.", "Restaurar INI anterior")) return;
        await RunFeatureAsync("Restaurando opções OptiScaler…", async token =>
        {
            var result = await OptiSettings.RestoreAsync(backup.Id, token); await RecordFeatureActivityAsync("optiscaler-settings-restore", "Restauração de opções OptiScaler", StatusLabel(result.Status), result.Message, result.Status);
            _optiSettingsBackups = await OptiSettings.GetBackupsAsync(CancellationToken.None); _optiSettingsGame = ""; Notice(result.Message, result.Status != OperationStatus.Reverted);
        });
    }
}
