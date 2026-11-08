using System.IO;
using System.Windows;
using System.Windows.Controls;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, string> _areaTabs = [];
    private void NavigateToConfiguration(string id)
    {
        string page = ProductPages.ForConfiguration(id);
        _areaTabs[page] = page switch { "memory" => "Memória virtual", "storage" => "Componentes e hibernação", "network" => id == "obs-priority" ? "OBS e transmissão" : "DNS e placa de rede", "windows" => "Preferências", "graphics" => "DLSS e tecnologias", _ => "Ajustes" };
        _configurationCategory = "Todas"; Navigate(page);
    }
    private UIElement BuildProductArea(string id)
    {
        var page = ProductPages.Find(id) ?? throw new ArgumentException("Área desconhecida.", nameof(id));
        return id switch
        {
            "performance" => AreaTabs(id, ("Ajustes", () => BuildConfigurationCatalog(page.ConfigurationIds)), ("Energia", BuildPower),
                ("Apps em segundo plano", BuildSessionTool), ("Orientações", BuildPerformanceGuidance)),
            "input" => AreaTabs(id, ("Ajustes", () => BuildConfigurationCatalog(page.ConfigurationIds)), ("Eventos do mouse", BuildMouseEventTool)),
            "memory" => AreaTabs(id, ("RAM e standby", BuildMemoryTool), ("Processos", BuildProcesses), ("Memória virtual", () => BuildConfigurationCatalog(page.ConfigurationIds))),
            "storage" => AreaTabs(id, ("Arquivos e Lixeira", BuildCleanupPage), ("Componentes e hibernação", () =>
            {
                var panel = Stack(); panel.Children.Add(BuildConfigurationCatalog(page.ConfigurationIds));
                panel.Children.Add(BuildMaintenanceTool([MaintenanceKind.CleanupWindowsUpdate])); return panel;
            }), ("Ocupação de pastas", BuildDiskTool)),
            "applications" => BuildInstalledAppsTool(),
            "network" => AreaTabs(id, ("DNS e placa de rede", () => BuildConfigurationCatalog(page.ConfigurationIds.Where(x => x != "obs-priority").ToArray())),
                ("Reparos de rede", () => BuildMaintenanceTool([MaintenanceKind.FlushDns, MaintenanceKind.ResetNetwork])), ("OBS e transmissão", BuildStreamingGuide)),
            "windows" => AreaTabs(id, ("Preferências", () => BuildConfigurationCatalog(page.ConfigurationIds)), ("Reparos e proteção", BuildWindowsRecovery),
                ("Histórico e backups", BuildHistory)),
            "graphics" => AreaTabs(id, ("DLSS e tecnologias", BuildGraphicsGuide), ("OptiScaler", () => { var root = Stack(); AddOptiScalerSection(root); AddOptiScalerSettingsSection(root); return root; })),
            "fps" => AreaTabs(id, ("FPS e frametimes", BuildFpsTool), ("Diagnóstico da sessão", BuildDiagnosticTool)),
            "hardware" => BuildHardwareTool(),
            _ => throw new ArgumentException("Área sem implementação.", nameof(id))
        };
    }
    private UIElement AreaTabs(string area, params (string Label, Func<UIElement> Build)[] tabs)
    {
        string selected = _areaTabs.GetValueOrDefault(area) ?? tabs[0].Label;
        if (!tabs.Any(t => t.Label == selected)) selected = tabs[0].Label;
        var root = Stack(); root.Children.Add(FeatureTabBar(tabs.Select(t => t.Label).ToArray(), selected, value => _areaTabs[area] = value));
        root.Children.Add(tabs.First(t => t.Label == selected).Build()); return root;
    }
    private UIElement BuildMouseEventTool()
    {
        var root = Stack(); root.Children.Add(InlineNote("Observe a entrega de eventos.", "Mova o mouse durante dez segundos. A mediana e o p95 dos intervalos descrevem os eventos recebidos nesta janela; coalescência e carga do sistema impedem tratar isso como medição física da taxa USB.", "\uE962"));
        root.Children.Add(FeatureButton("Iniciar teste de eventos do mouse", OpenMouseEventEstimate, true)); return root;
    }
    private UIElement BuildPerformanceGuidance()
    {
        var root = Stack(); root.Children.Add(InlineNote("Escolha pelo comportamento observado.", "Planos de energia mudam consumo e políticas. HAGS, MPO e interrupções dependem do driver e do jogo; desligar serviços pode prejudicar recursos. O nome de um ajuste não comprova ganho de FPS.", "\uE945"));
        AddGuide(root, "Sobreposições NVIDIA e Discord", "Desative a sobreposição pela configuração do próprio aplicativo, se esse for o diagnóstico desejado. O VL não encerra esses aplicativos nem altera arquivos internos para forçar o estado.", "https://support.discord.com/hc/en-us/articles/217659737-Games-Overlay-101");
        var actions = new WrapPanel(); actions.Children.Add(FeatureButton("Revisar pacotes Xbox e apps do Windows", () => Navigate("applications")));
        actions.Children.Add(FeatureButton("Revisar encerramento de sincronizadores", () => Navigate("processes")));
        actions.Children.Add(FeatureButton("Medir antes e depois", () => Navigate("fps"))); root.Children.Add(actions); return root;
    }
    private UIElement BuildStreamingGuide()
    {
        var root = Stack(); root.Children.Add(BuildConfigurationCatalog(["obs-priority"]));
        AddGuide(root, "Configuração do OBS para este computador", "No OBS, abra Ferramentas → Assistente de configuração automática. Ele considera o uso pretendido, o hardware e a conexão. Depois faça uma transmissão de teste e confira frames perdidos. O VL não troca bitrate, resolução ou chave de transmissão sem esse fluxo.", "https://obsproject.com/kb/quick-start-guide/");
        AddGuide(root, "Bitrate e codificador da plataforma", "No YouTube, escolha a tabela conforme codec, resolução e FPS; use CBR e intervalo de keyframe de 2 segundos conforme o guia. O orçamento de upload e a carga de codificação precisam de um teste real. O ajuste OBS acima altera elevação e power throttling; não limita RAM nem muda PriorityClass.", "https://support.google.com/youtube/answer/2853702?hl=pt-BR");
        return root;
    }
    private UIElement BuildWindowsRecovery()
    {
        var root = Stack(); var shortcuts = new WrapPanel();
        shortcuts.Children.Add(FeatureButton("Segurança do Windows", () => OpenExternal("windowsdefender:")));
        shortcuts.Children.Add(FeatureButton("Windows Update", () => OpenExternal("ms-settings:windowsupdate")));
        shortcuts.Children.Add(FeatureButton("Proteção do Sistema", () => OpenExternal(Path.Combine(Environment.SystemDirectory, "SystemPropertiesProtection.exe"))));
        shortcuts.Children.Add(FeatureButton("Escala e monitores", () => OpenExternal("ms-settings:display"))); root.Children.Add(shortcuts);
        root.Children.Add(InlineNote("Recuperação com escopo conhecido.", "A escala correta depende do monitor; ajuste-a na tela oficial do Windows. O histórico recupera valores que o VL guardou. Ele não impõe padrões universais de BCD, rede ou mouse nem apaga ajustes de outro programa sem snapshot.", "\uE81C"));
        root.Children.Add(BuildMaintenanceTool([MaintenanceKind.CreateRestorePoint, MaintenanceKind.RepairWindows, MaintenanceKind.RestartExplorer, MaintenanceKind.EnableDefender, MaintenanceKind.EnableSystemRestore])); return root;
    }
    private UIElement BuildGraphicsGuide()
    {
        var root = Stack(); root.Children.Add(InlineNote("Upscaling e geração são etapas diferentes.", "Upscaling reconstrói a imagem a partir de uma resolução de entrada menor. Geração de quadros intercala imagens; um FPS apresentado maior não comprova simulação mais rápida ou menor atraso. O jogo, a GPU, o driver e as bibliotecas determinam o suporte.", "\uE7F4"));
        root.Children.Add(BuildConfigurationCatalog(["dlss-rec"]));
        AddGuide(root, "DLSS", "O ajuste NVIDIA usa dois parâmetros DRS quando o driver os anuncia. DLSS Super Resolution, Ray Reconstruction e Frame Generation têm requisitos distintos; selecionar um preset não acrescenta suporte a um jogo que não o possui.", "https://www.nvidia.com/en-us/geforce/technologies/dlss/");
        AddGuide(root, "FSR e XeSS", "Dependem da integração e da versão usada pelo jogo ou pelo pacote local. Não é possível concluir a versão ou a compatibilidade apenas pelo modelo da GPU. A aba OptiScaler instala o pacote escolhido e edita somente chaves reconhecidas no INI.", "https://github.com/optiscaler/OptiScaler/wiki");
        AddGuide(root, "Latência NVIDIA", "Reflex depende de suporte no jogo e no hardware. Sincronização, limite de FPS e taxa variável interagem; não há uma configuração universal que maximize FPS e minimize latência em qualquer cenário. Faça o ajuste no jogo e no aplicativo do fabricante.", "https://www.nvidia.com/en-us/geforce/technologies/reflex/");
        AddGuide(root, "Latência AMD", "Anti-Lag e Anti-Lag 2 possuem condições de suporte diferentes; Anti-Lag 2 exige integração no jogo. O VL não injeta esse recurso nem muda o driver ao abrir esta página.", "https://www.amd.com/en/products/software/adrenalin/radeon-software-anti-lag.html");
        AddGuide(root, "Geração pelo driver AMD", "AFMF é um recurso do AMD Software para hardware e modos de apresentação compatíveis. Ative e teste pelo perfil do jogo no aplicativo do fabricante. Ele não equivale a ligar a geração integrada no jogo ou o motor de scaling do VL.", "https://www.amd.com/en/products/software/adrenalin/afmf.html");
        root.Children.Add(FeatureButton("Preferência de GPU do Windows", () => OpenExternal("ms-settings:display-advancedgraphics"))); return root;
    }
    private void AddGuide(StackPanel root, string title, string body, string source)
    {
        var panel = Stack(); panel.Children.Add(Text(title, 18, weight: FontWeights.SemiBold)); panel.Children.Add(Text(body, 13, Muted, margin: new Thickness(0, 9, 0, 14)));
        panel.Children.Add(FeatureButton("Consultar orientação oficial", () => OpenExternal(source))); root.Children.Add(Card(panel));
    }
    private UIElement BuildStartupArea()
    {
        var root = Stack(); var recoverable = _history.Where(r => r.Request.Kind == OperationKind.DisableStartup && r.CanUndo).OrderByDescending(r => r.StartedAt).ToArray();
        if (recoverable.Length > 0)
        {
            var panel = Stack(); panel.Children.Add(Text("Reativar entradas removidas pelo VL", 17, weight: FontWeights.SemiBold));
            foreach (var record in recoverable) panel.Children.Add(FeatureButton("Revisar reativação · " + record.Title, () => _ = UndoAsync(record)));
            root.Children.Add(Card(panel));
        }
        root.Children.Add(BuildStartup()); return root;
    }
}
