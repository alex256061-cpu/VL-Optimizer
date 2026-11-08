using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private ScalingProfileService? _scalingService;
    private ScalingProfileService Scaling => _scalingService ??= new(_backend.DataDirectory);
    private IReadOnlyList<ScalingProfile> _scalingProfiles = [];
    private ScalingCapabilities? _scalingCapabilities;

    private UIElement BuildScalingPage()
    {
        var root = Stack();
        root.Children.Add(InlineNote("Perfis locais, motor gráfico separado.", _scalingCapabilities?.Message ?? "O VL guarda e exporta opções de scaling. Um programa externo precisa ser selecionado para renderizar; os perfis não são aplicados automaticamente ao programa externo.", "\uE7F4"));
        var controls = new WrapPanel(); controls.Children.Add(FeatureButton("Novo perfil", () => _ = EditScalingProfileAsync(new ScalingProfile()), true)); controls.Children.Add(FeatureButton("Atualizar perfis", () => _ = RefreshScalingAsync())); controls.Children.Add(FeatureButton("Selecionar programa externo…", () => _ = SelectScalingEngineAsync()));
        if (_scalingCapabilities?.IsEngineAvailable == true) controls.Children.Add(FeatureButton("Abrir programa externo", () => _ = LaunchScalingEngineAsync())); root.Children.Add(controls);
        if (_scalingCapabilities?.EnginePath != null) root.Children.Add(Text("Programa selecionado: " + _scalingCapabilities.EnginePath, 12, Muted, margin: new Thickness(0, 0, 0, 16)));
        if (_scalingProfiles.Count == 0) root.Children.Add(Empty("\uE7F4", "Organize as opções de cada jogo.", "Crie um perfil com algoritmo, escala, captura, cursor e limites. Salvar apenas registra suas preferências locais; não ativa interpolação nem muda o jogo."));
        foreach (var profile in _scalingProfiles)
        {
            var content = Stack(); content.Children.Add(Text(profile.Name, 19, weight: FontWeights.Bold));
            content.Children.Add(Text(profile.ExecutablePath.Length == 0 ? "Jogo ainda não vinculado" : profile.ExecutablePath, 12, Muted, margin: new Thickness(0, 7, 0, 12)));
            content.Children.Add(Text($"Escala {profile.ScalingAlgorithm} · fator {profile.ScaleFactor:0.##} · captura {profile.CaptureApi} · geração {profile.FrameGeneration}", 13));
            content.Children.Add(Text("Preferências armazenadas · aplicação no motor externo é manual", 11, AccentSoft, margin: new Thickness(0, 8, 0, 14)));
            var actions = new WrapPanel(); actions.Children.Add(FeatureButton("Editar opções", () => _ = EditScalingProfileAsync(profile))); actions.Children.Add(FeatureButton("Exportar perfil…", () => _ = ExportScalingProfileAsync(profile))); actions.Children.Add(FeatureButton("Excluir perfil…", () => _ = DeleteScalingProfileAsync(profile))); content.Children.Add(actions); root.Children.Add(Card(content));
        }
        AddOptiScalerSection(root);
        return root;
    }

    private async Task RefreshScalingAsync() => await RunFeatureAsync("Lendo perfis e disponibilidade de scaling…", async token => { _scalingProfiles = await Scaling.GetProfilesAsync(token); _scalingCapabilities = await Scaling.GetCapabilitiesAsync(); await LoadOptiScalerBackupsAsync(token); });

    private async Task SelectScalingEngineAsync()
    {
        if (_busy || _smoke) return; var picker = new OpenFileDialog { Title = "Selecionar programa de scaling já instalado", Filter = "Executável (*.exe)|*.exe", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        await RunFeatureAsync("Guardando caminho do programa externo…", async token => { await Scaling.SetEnginePathAsync(picker.FileName, token); _scalingCapabilities = await Scaling.GetCapabilitiesAsync(); Notice("Caminho guardado. O programa só será executado quando você escolher Abrir programa externo."); });
    }
    private async Task LaunchScalingEngineAsync()
    {
        if (_busy || _smoke || !ConfirmFeature("Abrir programa externo?", (_scalingCapabilities?.EnginePath ?? "") + "\n\nO programa selecionado será iniciado. As opções dos perfis VL precisam ser ajustadas manualmente nele.", "Abrir programa")) return;
        await RunFeatureAsync("Abrindo programa de scaling…", async token => await Scaling.LaunchEngineAsync(token));
    }

    private static readonly Dictionary<string, string> ScalingLabels = new()
    {
        ["Name"] = "Nome do perfil", ["ExecutablePath"] = "Executável do jogo", ["AutoScale"] = "Automação desejada · não executada pelo VL", ["AutoScaleDelay"] = "Atraso desejado · segundos",
        ["ScalingMode"] = "Modo de escala", ["FitMode"] = "Ajuste da imagem", ["ScalingAlgorithm"] = "Algoritmo de escala", ["ScaleFactor"] = "Fator de escala", ["ResizeBeforeScale"] = "Redimensionar antes da escala", ["FsrOptimized"] = "FSR otimizado",
        ["Ls1Performance"] = "LS1 · modo de desempenho", ["Anime4KQuality"] = "Anime4K · qualidade", ["Sharpness"] = "Nitidez · 0 a 10", ["Ls1Sharpness"] = "LS1 · nitidez de 0 a 4", ["Vrs"] = "Sombreamento variável (VRS)",
        ["FrameGeneration"] = "Geração de quadros desejada", ["PerformanceMode"] = "Geração · modo de desempenho", ["FrameGenerationMode"] = "Modo de geração", ["FrameMultiplier"] = "Multiplicador de quadros", ["TargetFps"] = "FPS alvo", ["FlowScale"] = "Flow scale · 25 a 100",
        ["ClipCursor"] = "Confinar cursor", ["AdjustCursorSpeed"] = "Ajustar velocidade do cursor", ["HideCursor"] = "Ocultar cursor", ["ScaleCursor"] = "Aplicar escala ao cursor", ["SyncMode"] = "Sincronização", ["MaxFrameLatency"] = "Latência máxima · 1 a 15 frames", ["GsyncSupport"] = "Suporte a G-SYNC", ["HdrSupport"] = "Suporte a HDR",
        ["CaptureApi"] = "API de captura", ["QueueTarget"] = "Fila alvo · 0 a 2", ["DrawFps"] = "Mostrar FPS no motor externo", ["PreferredGpuIndex"] = "Índice da GPU preferida", ["OutputDisplayIndex"] = "Índice do monitor de saída", ["CropLeft"] = "Recorte esquerdo · pixels", ["CropTop"] = "Recorte superior · pixels", ["CropRight"] = "Recorte direito · pixels", ["CropBottom"] = "Recorte inferior · pixels", ["MultiDisplay"] = "Múltiplos monitores", ["FpsLimit"] = "Limite de FPS · 0 desativa"
    };
    private static readonly Dictionary<string, string[]> ScalingChoices = new()
    {
        ["ScalingAlgorithm"] = ScalingProfileService.Algorithms.ToArray(), ["ScalingMode"] = ["Auto", "Custom"], ["FitMode"] = ["AspectRatio", "Fullscreen"], ["Anime4KQuality"] = ["S", "M", "L", "VL", "UL"],
        ["FrameGeneration"] = ["Off", "LSFG1", "LSFG2", "LSFG3"], ["FrameGenerationMode"] = ["Fixed", "Adaptive", "X2", "X3", "X4"], ["CaptureApi"] = ["DXGI", "WGC"], ["SyncMode"] = ["Off", "Default", "VSync", "Half", "Third"]
    };

    private async Task EditScalingProfileAsync(ScalingProfile profile)
    {
        if (_busy || _smoke) return;
        var window = Dialog("Opções do perfil de scaling", out var panel, out var footer);
        panel.Children.Add(Text("Preferências do perfil", 25, weight: FontWeights.Bold));
        panel.Children.Add(Text("Estas opções ficam armazenadas no VL e podem ser exportadas. O programa externo controla captura e renderização; salvar aqui não aplica escala, HDR, interpolação ou limite de FPS.", 13, AccentSoft, margin: new Thickness(0, 12, 0, 18)));
        var fields = new Dictionary<string, FrameworkElement>();
        var properties = typeof(ScalingProfile).GetProperties().Where(p => p.Name != nameof(ScalingProfile.Id)).ToArray();
        var groups = new[] { ("Perfil e imagem", new[] { "Name", "ExecutablePath", "ScalingAlgorithm", "ScaleFactor", "ScalingMode", "FitMode", "ResizeBeforeScale", "FsrOptimized", "Ls1Performance", "Anime4KQuality", "Sharpness", "Ls1Sharpness", "Vrs" }),
            ("Quadros e sincronização", new[] { "FrameGeneration", "PerformanceMode", "FrameGenerationMode", "FrameMultiplier", "TargetFps", "FlowScale", "SyncMode", "MaxFrameLatency", "GsyncSupport", "FpsLimit" }),
            ("Captura, cursor e saída", new[] { "CaptureApi", "QueueTarget", "DrawFps", "ClipCursor", "AdjustCursorSpeed", "HideCursor", "ScaleCursor", "HdrSupport", "PreferredGpuIndex", "OutputDisplayIndex", "CropLeft", "CropTop", "CropRight", "CropBottom", "MultiDisplay" }),
            ("Automação desejada · sem execução no VL", new[] { "AutoScale", "AutoScaleDelay" }) };
        foreach (var (heading, names) in groups)
        {
            var section = Stack();
            foreach (var name in names)
            {
                var property = properties.Single(p => p.Name == name); var label = ScalingLabels.GetValueOrDefault(name, name); var value = property.GetValue(profile);
                FrameworkElement field;
                if (property.PropertyType == typeof(bool)) field = new CheckBox { Content = label, IsChecked = (bool)value! };
                else if (ScalingChoices.TryGetValue(name, out var choices)) field = new ComboBox { ItemsSource = choices, SelectedItem = value, MinHeight = 44 };
                else field = new TextBox { Text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", MinHeight = 44 };
                AutomationProperties.SetName(field, label); fields[name] = field;
                if (property.PropertyType != typeof(bool)) section.Children.Add(Text(label, 12, Muted, margin: new Thickness(0, 10, 0, 5)));
                section.Children.Add(field);
                if (name == "ExecutablePath")
                {
                    var select = Button("Selecionar executável…", () => { var picker = new OpenFileDialog { Filter = "Executável (*.exe)|*.exe", CheckFileExists = true }; if (picker.ShowDialog(window) == true) ((TextBox)field).Text = picker.FileName; }); select.HorizontalAlignment = HorizontalAlignment.Left; select.Margin = new Thickness(0, 7, 0, 0); section.Children.Add(select);
                }
            }
            panel.Children.Add(new Expander { Header = heading, Content = section, Foreground = Muted, FontSize = 14, IsExpanded = heading == "Perfil e imagem", Margin = new Thickness(0, 0, 0, 18) });
        }
        var validation = Text("", 12, AccentSoft); panel.Children.Add(validation); ScalingProfile? updated = null;
        var save = Button("Salvar preferências locais", () =>
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.SerializeToNode(profile)!.AsObject();
                foreach (var property in properties)
                {
                    var field = fields[property.Name];
                    if (field is CheckBox boolean) json[property.Name] = boolean.IsChecked == true;
                    else if (field is ComboBox choice) json[property.Name] = (string)choice.SelectedItem;
                    else if (field is TextBox text)
                    {
                        if (property.PropertyType == typeof(int)) json[property.Name] = int.Parse(text.Text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                        else if (property.PropertyType == typeof(double)) json[property.Name] = double.Parse(text.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
                        else json[property.Name] = text.Text;
                    }
                }
                updated = json.Deserialize<ScalingProfile>(); ScalingProfileService.ValidateProfile(updated!); window.DialogResult = true;
            }
            catch (Exception ex) { validation.Text = "Confira os campos: " + ex.Message; validation.BringIntoView(); }
        }, true);
        var cancel = Button("Voltar", () => window.DialogResult = false); cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 10, 0); footer.Children.Add(cancel); footer.Children.Add(save);
        if (window.ShowDialog() != true || updated == null) return;
        await RunFeatureAsync("Salvando preferências de scaling…", async token => { await Scaling.SaveProfileAsync(updated, token); _scalingProfiles = await Scaling.GetProfilesAsync(token); Notice("Preferências salvas localmente. Nenhuma opção gráfica foi aplicada ao jogo ou ao programa externo."); });
    }

    private async Task DeleteScalingProfileAsync(ScalingProfile profile)
    {
        if (_busy || _smoke || !ConfirmFeature("Excluir perfil local?", profile.Name + "\n\nApenas as preferências armazenadas no VL serão removidas. O jogo e o programa externo não serão alterados.", "Excluir perfil")) return;
        await RunFeatureAsync("Excluindo perfil local…", async token => { await Scaling.DeleteProfileAsync(profile.Id, token); _scalingProfiles = await Scaling.GetProfilesAsync(token); });
    }
    private async Task ExportScalingProfileAsync(ScalingProfile profile)
    {
        if (_busy || _smoke) return; var picker = new SaveFileDialog { FileName = "VL-Scaling-" + profile.Id.ToString("N") + ".json", Filter = "Perfil VL JSON (*.json)|*.json" }; if (picker.ShowDialog(this) != true) return;
        await RunFeatureAsync("Exportando perfil de scaling…", async token => { await Scaling.ExportProfileAsync(profile.Id, picker.FileName, token); Notice("Perfil exportado no formato VL. A importação por outro programa depende do suporte dele."); });
    }
}
