using System.Windows;
using System.Windows.Controls;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private readonly UwpManagementService _storeApps = new();
    private IReadOnlyList<UwpInstalledPackage>? _storePackages;
    private UIElement BuildStoreAppsTool()
    {
        var content = Stack(); content.Children.Add(Text("Remoção apenas do usuário atual. Dependências, pacotes protegidos e aplicativos fora do catálogo permanecem bloqueados. Uma remoção não tem desfazer pelo VL.", 12, Muted, margin: new Thickness(0, 5, 0, 12)));
        content.Children.Add(FeatureButton("Buscar apps da Loja", () => _ = RunFeatureAsync("Lendo pacotes do usuário atual…", async token => _storePackages = await _storeApps.InventoryAsync(token))));
        if (_storePackages != null)
        {
            foreach (var definition in _storeApps.GetDefinitions())
            {
                foreach (var package in _storePackages.Where(p => p.Name.Equals(definition.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    var item = Stack(); item.Children.Add(Text(definition.Title, 16, weight: FontWeights.SemiBold)); item.Children.Add(Text(package.Version + " · " + package.Publisher, 11, Muted, margin: new Thickness(0, 6, 0, 9))); item.Children.Add(Text(definition.Impact, 12, Muted));
                    var actions = new WrapPanel(); actions.Children.Add(FeatureButton("Revisar remoção…", () => _ = ReviewStoreAppRemovalAsync(package.PackageFullName))); actions.Children.Add(FeatureButton("Procurar na Store", () => OpenExternal(definition.StoreSearchUrl))); item.Children.Add(actions); content.Children.Add(Card(item));
                }
            }
            if (!_storeApps.GetDefinitions().Any(d => _storePackages.Any(p => p.Name.Equals(d.Id, StringComparison.OrdinalIgnoreCase)))) content.Children.Add(Text("Nenhum dos pacotes removíveis do catálogo foi encontrado neste usuário.", 12, Muted, margin: new Thickness(0, 8, 0, 12)));
        }
        content.Children.Add(Text("Procurar na Store abre uma busca. A disponibilidade e a reinstalação dependem da loja e do pacote; o VL não promete recuperar dados removidos.", 11, Muted, margin: new Thickness(0, 6, 0, 0)));
        return new Expander { Header = "Apps da Loja · catálogo de remoção", Content = content, Foreground = Muted, FontSize = 14, Margin = new Thickness(0, 8, 0, 18), IsExpanded = _storePackages != null };
    }
    private async Task ReviewStoreAppRemovalAsync(string fullName)
    {
        if (_busy || _smoke) return; UwpRemovalPreview? preview = null;
        await RunFeatureAsync("Consultando pacote e proteção do Windows…", async token => preview = await _storeApps.PreviewRemoveAsync(fullName, token));
        if (preview == null) return;
        var window = Dialog("Revisar remoção do pacote", out var panel, out var footer); panel.Children.Add(Text(preview.Definition.Title, 25, weight: FontWeights.Bold)); panel.Children.Add(Text(preview.Package.PackageFullName, 12, Muted, margin: new Thickness(0, 12, 0, 14))); panel.Children.Add(Text(preview.Consequence, 14, AccentSoft));
        if (!preview.CanApply) panel.Children.Add(Text(preview.BlockReason ?? "Pacote indisponível para remoção.", 13, Muted, margin: new Thickness(0, 12, 0, 0)));
        var consent = Consent("Aprovo remover este pacote do meu usuário e entendo que não há desfazer pelo VL."); consent.IsEnabled = preview.CanApply; panel.Children.Add(consent);
        var apply = Button("Remover pacote revisado", () => window.DialogResult = true, true); apply.IsEnabled = false; consent.Checked += (_, _) => apply.IsEnabled = preview.CanApply; consent.Unchecked += (_, _) => apply.IsEnabled = false;
        var back = Button("Voltar", () => window.DialogResult = false); back.IsCancel = true; back.Margin = new Thickness(0, 0, 10, 0); footer.Children.Add(back); footer.Children.Add(apply); if (window.ShowDialog() != true) return;
        await RunFeatureAsync("Removendo e conferindo pacote do usuário…", async token => { var result = await _storeApps.RemoveAsync(preview, token); await RecordFeatureActivityAsync("store-remove", "App da Loja · " + preview.Definition.Title, result.Message, "Código: " + result.ExitCode, result.Status); _storePackages = await _storeApps.InventoryAsync(CancellationToken.None); Notice(result.Message, result.Status is OperationStatus.Failed or OperationStatus.PartiallyCompleted); });
    }
}
