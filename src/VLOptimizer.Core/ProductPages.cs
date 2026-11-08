namespace VLOptimizer.Core;

public sealed record ProductPage(string Id, string Title, string Section, string Glyph, string Description, IReadOnlyList<string> ConfigurationIds);
public static class ProductPages
{
    private static IReadOnlyList<string> Ids(params string[] ids) => Array.AsReadOnly(ids);
    public static IReadOnlyList<ProductPage> All { get; } = Array.AsReadOnly(new[] {
        new ProductPage("overview", "Painel", "PRINCIPAL", "\uE80F", "Diagnóstico, recomendações e últimas atividades.", Ids()),
        new ProductPage("performance", "Desempenho", "OTIMIZAR", "\uE945", "Energia, gráficos e tarefas de fundo, com efeitos e limites de cada ajuste.", Ids("power-balanced", "power-high", "power-ultimate", "x3d-mode", "game-mode", "dvr-off", "gamebar-off", "steam-overlay", "updates-no-reboot", "notifications-off", "bg-apps", "edge-bg", "hags", "windowed-games", "dlss-rec", "gpu-msi", "mpo", "visual-effects-off", "transparency", "sysmain-off", "indexing-off", "unused-services-off", "xbox-services-off", "resolution-720p")),
        new ProductPage("input", "Mouse e teclado", "OTIMIZAR", "\uE962", "Aceleração, repetição, atalhos de acessibilidade, Num Lock e eventos do mouse.", Ids("mouse-accel", "kbd-repeat", "sticky", "numlock")),
        new ProductPage("memory", "Memória", "OTIMIZAR", "\uE950", "RAM, commit, standby, processos e paginação automática.", Ids("pagefile-auto")),
        new ProductPage("storage", "Limpeza", "OTIMIZAR", "\uEDA2", "Caches, temporários, Lixeira, componentes e espaço ocupado.", Ids("hibernate")),
        new ProductPage("applications", "Programas", "OTIMIZAR", "\uE8A5", "Inventário, desinstalação, sobras e pacotes opcionais do Windows.", Ids()),
        new ProductPage("network", "Internet e live", "OTIMIZAR", "\uE774", "DNS, propriedades do driver, reparo de rede e preparação do OBS.", Ids("dns-auto", "dns-cloudflare", "dns-google", "dns-quad9", "dns-adguard", "delivery", "lso", "eee", "nic-intmod", "obs-priority")),
        new ProductPage("windows", "Windows", "OTIMIZAR", "\uE713", "Privacidade, Explorador, aparência, manutenção e recuperação.", Ids("telemetry", "suggestions", "bing", "activity", "widgets", "location", "end-task", "file-ext", "hidden-files", "this-pc", "classic-menu", "dark", "taskbar-left", "task-view", "search-icon", "bsod-detail", "windows-update-services")),
        new ProductPage("startup", "Inicialização", "OTIMIZAR", "\uE7B5", "Entradas de inicialização e reativação pelo backup do VL.", Ids()),
        new ProductPage("games", "Meus jogos", "JOGOS", "\uE7FC", "Bibliotecas, perfis, GPU, sessões e FiveM.", Ids()),
        new ProductPage("graphics", "DLSS / FSR", "JOGOS", "\uE7F4", "Perfil NVIDIA, tecnologias de imagem e integração local OptiScaler.", Ids("dlss-rec")),
        new ProductPage("scaling", "VL Scaling", "JOGOS", "\uE9D9", "Perfis de escala, captura, cursor e geração de quadros desejada. Motor gráfico externo.", Ids()),
        new ProductPage("fps", "Medidor de FPS", "DIAGNÓSTICO", "\uE9D9", "Frametimes, captura, comparação e sobreposição externa opcional.", Ids()),
        new ProductPage("hardware", "Verificar hardware", "DIAGNÓSTICO", "\uE964", "CPU, RAM, BIOS, GPUs e discos com origem dos dados.", Ids()),
        new ProductPage("history", "Histórico e restauração", "FERRAMENTAS", "\uE81C", "Operações, estado anterior e restauração condicional.", Ids()),
        new ProductPage("settings", "Configurações", "FERRAMENTAS", "\uE713", "Movimento, economia, privacidade e versão.", Ids())
    });
    public static ProductPage? Find(string id) => All.FirstOrDefault(p => p.Id == id);
    public static string ForConfiguration(string id) => id == "dlss-rec" ? "graphics" : All.FirstOrDefault(p => p.ConfigurationIds.Contains(id))?.Id ?? "catalog";
}
