using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private HardwareInventoryReport? _hardwareInventory;
    private readonly HardwareInventoryService _hardwareService = new();
    private static readonly IReadOnlyDictionary<string, string> HardwareLabels = new Dictionary<string, string>
    {
        ["Name"] = "Nome", ["Manufacturer"] = "Fabricante", ["NumberOfCores"] = "Núcleos físicos", ["NumberOfLogicalProcessors"] = "Processadores lógicos",
        ["MaxClockSpeed"] = "Clock máximo informado (MHz)", ["CurrentClockSpeed"] = "Clock informado pelo WMI (MHz)", ["DeviceLocator"] = "Slot", ["BankLabel"] = "Banco",
        ["Capacity"] = "Capacidade", ["Speed"] = "Speed informado (MT/s)", ["ConfiguredClockSpeed"] = "Velocidade configurada (MT/s)", ["PartNumber"] = "Peça",
        ["SMBIOSMemoryType"] = "Tipo de memória", ["FormFactor"] = "Formato", ["SMBIOSBIOSVersion"] = "Versão da BIOS", ["ReleaseDate"] = "Data informada",
        ["SMBIOSMajorVersion"] = "SMBIOS · versão principal", ["SMBIOSMinorVersion"] = "SMBIOS · revisão", ["Product"] = "Produto", ["Version"] = "Versão",
        ["VideoProcessor"] = "Processador gráfico", ["DriverVersion"] = "Versão do driver", ["DriverDate"] = "Data do driver", ["CurrentHorizontalResolution"] = "Largura informada (px)",
        ["CurrentVerticalResolution"] = "Altura informada (px)", ["CurrentRefreshRate"] = "Atualização informada (Hz)", ["Model"] = "Modelo", ["InterfaceType"] = "Interface",
        ["MediaType"] = "Mídia informada", ["Size"] = "Tamanho", ["BytesPerSector"] = "Bytes por setor", ["FirmwareRevision"] = "Firmware", ["SystemType"] = "Arquitetura",
        ["TotalPhysicalMemory"] = "Memória física informada", ["AutomaticManagedPagefile"] = "Memória virtual automática"
    };

    private UIElement BuildHardwareTool()
    {
        var root = Stack(); root.Children.Add(InlineNote("Conheça as peças. Confira a origem.", "Inventário local de processador, BIOS, placa-mãe, módulos de RAM, GPU, drivers e discos. Cada grupo identifica a fonte; temperatura e XMP/EXPO não são deduzidos por heurísticas.", "\uE950"));
        var actions = new WrapPanel(); actions.Children.Add(FeatureButton("Ler hardware detalhado", () => _ = ReadDetailedHardwareAsync(), true));
        var export = FeatureButton("Exportar inventário…", ExportDetailedHardware); export.IsEnabled = _hardwareInventory is not null && !_busy; actions.Children.Add(export);
        actions.Children.Add(FeatureButton("Estimar eventos do mouse", OpenMouseEventEstimate)); root.Children.Add(actions);
        var firmware = Stack(); firmware.Children.Add(Text("O inventário mostra modelo e versão, não comprova correção de microcódigo, XMP/EXPO ou compatibilidade de um novo firmware. Identifique a placa-mãe e confira o suporte do fabricante. Instalação de BIOS, chipset e perfil de memória é uma decisão manual.", 13, Muted));
        var sources = new WrapPanel(); sources.Children.Add(FeatureButton("Suporte AMD", () => OpenExternal("https://www.amd.com/en/support/download/drivers.html"))); sources.Children.Add(FeatureButton("Suporte Intel", () => OpenExternal("https://www.intel.com/content/www/us/en/support.html"))); firmware.Children.Add(sources);
        root.Children.Add(new Expander { Header = "Ryzen, Intel, GPU e memória · interpretar o inventário", Content = firmware, Foreground = Muted, Margin = new Thickness(0, 6, 0, 16) });
        if (_hardwareInventory is null) root.Children.Add(Empty("\uE950", "Faça uma leitura detalhada.", "A consulta só começa ao escolher Ler hardware. Ela não aplica configurações nem envia dados."));
        else
        {
            root.Children.Add(Text("Lido em " + _hardwareInventory.CapturedAt.ToString("dd/MM/yyyy HH:mm:ss"), 12, Muted, margin: new Thickness(0, 4, 0, 16)));
            foreach (var section in _hardwareInventory.Sections)
            {
                var card = Stack(); card.Children.Add(Text(section.Title, 21, weight: FontWeights.Bold)); card.Children.Add(Text(section.Source, 11, AccentSoft, margin: new Thickness(0, 5, 0, 12)));
                if (section.Error is not null) card.Children.Add(Text("Fonte indisponível: " + section.Error, 13, Muted));
                if (section.Rows.Count == 0 && section.Error is null) card.Children.Add(Text("Nenhum item informado por esta fonte.", 13, Muted));
                foreach (var row in section.Rows)
                {
                    card.Children.Add(Text(row.Name, 16, weight: FontWeights.SemiBold, margin: new Thickness(0, 10, 0, 10)));
                    foreach (var property in row.Values)
                    {
                        string value = HardwareValue(property.Key, property.Value);
                        card.Children.Add(Text((HardwareLabels.GetValueOrDefault(property.Key) ?? property.Key) + ": " + value, 13, Muted, margin: new Thickness(0, 0, 0, 6)));
                    }
                }
                root.Children.Add(Card(card));
            }
            foreach (string finding in _hardwareInventory.Findings) root.Children.Add(InlineNote("Sobre esta leitura", finding, "\uE946"));
        }
        return root;
    }

    private async Task ReadDetailedHardwareAsync() => await RunFeatureAsync("Consultando hardware detalhado…", async token =>
    {
        var progress = new Progress<string>(value => _analysisStatus.Text = "◌  " + value);
        _hardwareInventory = await _hardwareService.ReadAsync(progress, token);
        Notice("Inventário concluído. Grupos sem resposta continuam identificados como indisponíveis.");
    });
    private void ExportDetailedHardware()
    {
        if (_smoke || _hardwareInventory is null) return;
        var dialog = new SaveFileDialog { FileName = "VL-Hardware-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json", Filter = "Inventário JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_hardwareInventory, new JsonSerializerOptions { WriteIndented = true })); Notice("Inventário exportado. Revise os modelos e dados do computador antes de compartilhar."); }
        catch (Exception ex) { Notice("Não foi possível exportar: " + ex.Message, true); }
    }
    private static string HardwareValue(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Não informado";
        if (key is "Capacity" or "Size" or "TotalPhysicalMemory" && ulong.TryParse(value, out var bytes)) return $"{bytes / 1073741824d:0.##} GiB";
        if (key is "DriverDate" or "ReleaseDate" && value.Length >= 8 && DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date.ToString("dd/MM/yyyy");
        if (key == "SMBIOSMemoryType") return value switch { "18" => "DDR", "19" => "DDR2", "24" => "DDR3", "26" => "DDR4", "34" => "DDR5", "35" => "LPDDR5", "30" => "LPDDR4", "0" => "Não informado", _ => value };
        if (key == "FormFactor") return value switch { "8" => "DIMM", "12" => "SODIMM", "0" => "Não informado", _ => value };
        return value == "True" ? "Sim" : value == "False" ? "Não" : value;
    }

    private void OpenMouseEventEstimate()
    {
        if (_busy || _smoke) return;
        var window = Dialog("Eventos do mouse", out var panel, out var footer);
        panel.Children.Add(Text("Movimento observado na janela", 25, weight: FontWeights.Bold));
        panel.Children.Add(Text("Mova o ponteiro dentro da área por dez segundos. O resultado estima eventos recebidos pelo aplicativo; não mede eletricamente a porta USB nem certifica o polling rate do mouse.", 14, Muted, margin: new Thickness(0, 12, 0, 18)));
        var area = new Border { Height = 220, Background = Brush(Palette.Panel), BorderBrush = AccentSoft, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Cursor = Cursors.Cross,
            Child = Text("Mova o mouse aqui após iniciar", 18, Muted, margin: new Thickness(20)) };
        panel.Children.Add(area); var status = Text("Pronto para observar", 15, margin: new Thickness(0, 16, 0, 12)); panel.Children.Add(status);
        var timestamps = new List<double>(); var watch = new Stopwatch(); bool running = false;
        area.MouseMove += (_, _) => { if (running && timestamps.Count < 100_001) timestamps.Add(watch.Elapsed.TotalMilliseconds); };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        var start = Button("Iniciar dez segundos", () => { timestamps.Clear(); watch.Restart(); running = true; timer.Start(); }, true);
        timer.Tick += (_, _) =>
        {
            status.Text = $"{Math.Max(0, 10 - watch.Elapsed.TotalSeconds):0.0} s · {timestamps.Count} eventos recebidos";
            if (watch.Elapsed.TotalSeconds < 10) return;
            running = false; timer.Stop(); watch.Stop();
            var result = MouseEventStatistics.Analyze(timestamps);
            status.Text = result is null ? "Amostra insuficiente. Inicie novamente e mova o ponteiro dentro da área." : $"{result.EstimatedEventsPerSecond:0} eventos/s pela mediana · intervalo {result.MedianIntervalMs:0.###} ms · p95 {result.P95IntervalMs:0.###} ms · {result.Samples} intervalos. Estimativa desta janela.";
        };
        var close = Button("Fechar", () => window.Close()); close.IsCancel = true; close.Margin = new Thickness(0, 0, 10, 0); footer.Children.Add(close); footer.Children.Add(start);
        try { window.ShowDialog(); } finally { timer.Stop(); running = false; watch.Stop(); }
    }
}
