# Catálogo e evidências — VL Optimizer 0.3.0

O catálogo abaixo vem das definições compiladas do Core. Ele contém 56 opções, incluindo os 33 tweaks recuperados. A ausência de uma dependência pode bloquear uma opção nesta máquina; estar listado não significa estar aplicado ou recomendado.

A prévia explica antes/depois, privilégio, riscos e reinício. Desativar usa os valores declarados; Restaurar recupera o snapshot real. Recursos não restauráveis, como limpeza e ferramentas de manutenção, têm fluxos separados. A matriz GDZ → VL está em [EVOLUCAO-0.3.0.md](EVOLUCAO-0.3.0.md).

| ID | Função | Grupo | Build mínima |
| --- | --- | --- | ---: |
| `game-mode` | Modo de Jogo do Windows | jogo | 19041 |
| `delivery` | Sem upload de atualizações para outros PCs | jogo | 19041 |
| `edge-bg` | Edge fechado de verdade | jogo | 19041 |
| `bg-apps` | Sem apps da Loja rodando escondidos | jogo | 19041 |
| `hibernate` | Desligar de verdade (sem hibernação) | jogo | 19041 |
| `mpo` | Correção de tela piscando (MPO) | jogo | 19041 |
| `gpu-msi` | Placa de vídeo com interrupção MSI e prioridade alta | jogo | 19041 |
| `dlss-rec` | DLSS no modelo recomendado pela NVIDIA | jogo | 19041 |
| `steam-overlay` | Sobreposição do Steam (Shift+Tab) desligada | jogo | 19041 |
| `lso` | LSO da placa de rede desligado | rede | 19041 |
| `eee` | Economia de energia da placa de rede desligada (EEE) | rede | 19041 |
| `nic-intmod` | Placa de rede sem moderação de interrupção | rede | 19041 |
| `mouse-accel` | Mira sem aceleração do mouse | entrada | 19041 |
| `sticky` | Sem janelas de acessibilidade no meio do jogo | entrada | 19041 |
| `numlock` | Num Lock ligado ao iniciar | entrada | 19041 |
| `kbd-repeat` | Teclado repetindo na velocidade máxima | entrada | 19041 |
| `telemetry` | Telemetria e anúncios personalizados | privacidade | 19041 |
| `suggestions` | Sem sugestões, dicas e apps instalados sozinhos | privacidade | 19041 |
| `bing` | Pesquisa do Iniciar sem Bing | privacidade | 19041 |
| `activity` | Sem histórico de atividades | privacidade | 19041 |
| `widgets` | Sem Widgets (notícias e clima) | privacidade | 22000 |
| `location` | Localização desligada | privacidade | 19041 |
| `end-task` | Finalizar tarefa pela barra de tarefas | aparencia | 22631 |
| `file-ext` | Mostrar a extensão dos arquivos | aparencia | 19041 |
| `hidden-files` | Mostrar arquivos ocultos | aparencia | 19041 |
| `this-pc` | Explorador abre em "Este Computador" | aparencia | 19041 |
| `classic-menu` | Menu do botão direito completo | aparencia | 22000 |
| `dark` | Tema escuro | aparencia | 19041 |
| `transparency` | Sem transparência nas janelas | aparencia | 19041 |
| `taskbar-left` | Barra de tarefas à esquerda | aparencia | 22000 |
| `task-view` | Esconder o botão Visão de Tarefas | aparencia | 19041 |
| `search-icon` | Pesquisa da barra só como ícone | aparencia | 19041 |
| `bsod-detail` | Tela azul com detalhes do erro | aparencia | 19041 |
| `dvr-off` | Desativar captura e DVR | jogo | 19041 |
| `gamebar-off` | Desativar Game Bar e DVR | jogo | 19041 |
| `notifications-off` | Desativar notificações do usuário | sistema | 19041 |
| `updates-no-reboot` | Evitar reinício automático com sessão aberta | sistema | 19041 |
| `sysmain-off` | Desativar SysMain | desempenho | 19041 |
| `indexing-off` | Desativar indexação do Windows | sistema | 19041 |
| `xbox-services-off` | Desativar os três serviços Xbox | jogo | 19041 |
| `hags` | Agendamento de GPU por hardware | jogo | 19041 |
| `visual-effects-off` | Reduzir efeitos visuais | aparencia | 19041 |
| `windowed-games` | Otimizações para jogos em janela | jogo | 22000 |
| `power-balanced` | Plano Equilibrado | desempenho | 19041 |
| `power-high` | Plano Alto Desempenho | desempenho | 19041 |
| `power-ultimate` | Plano Desempenho Máximo | desempenho | 19041 |
| `x3d-mode` | Configuração para X3D com dois CCDs | jogo | 19041 |
| `pagefile-auto` | Arquivo de paginação automático | memoria | 19041 |
| `unused-services-off` | Desativar cinco serviços opcionais | sistema | 19041 |
| `obs-priority` | OBS com elevação e sem power throttling | jogo | 19041 |
| `dns-cloudflare` | DNS IPv4: Cloudflare | rede | 19041 |
| `dns-google` | DNS IPv4: Google | rede | 19041 |
| `dns-quad9` | DNS IPv4: Quad9 | rede | 19041 |
| `dns-adguard` | DNS IPv4: AdGuard | rede | 19041 |
| `dns-auto` | DNS IPv4: Automático (DHCP) | rede | 19041 |
| `resolution-720p` | Resolução 1280 × 720 na sessão | jogo | 19041 |

## Fontes e fronteiras

Os valores/combinações recuperados são descritos em GDZ-OPERACOES-IMPLEMENTAVEIS.md. O código da implementação é próprio. Cada definição contém SourceUrl/SourceEvidence quando disponível; o JSON público `artifacts/catalogo-0.3.0.json` conserva esses campos, riscos e alvos estáticos.

APIs oficiais usadas: Registro Microsoft.Win32; Service Control Manager; PowerGet/SetActiveScheme; SystemParametersInfo; WMI Win32_ComputerSystem; IP Helper Get/SetInterfaceDnsSettings; DEVMODEW/ChangeDisplaySettingsEx; NVAPI DRS. A configuração Steam usa parser próprio KeyValues/VDF. Resolver dispositivos não cria chaves com exemplos sintéticos.

- [NVIDIA NVAPI SDK](https://github.com/NVIDIA/nvapi)
- [DNS por interface](https://learn.microsoft.com/windows/win32/api/netioapi/nf-netioapi-setinterfacednssettings)
- [Modo de vídeo](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-changedisplaysettingsexw)
- [Planos de energia](https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powersetactivescheme)
- [Memória virtual WMI](https://learn.microsoft.com/windows/win32/cimwin32prov/win32-computersystem)
- [PresentMon](https://github.com/GameTechDev/PresentMon)
- [Instalação manual OptiScaler](https://github.com/optiscaler/OptiScaler/wiki/Manual-Installation)

Compatibilidade de uma API não comprova benefício de desempenho. HAGS, MSI, MPO, LSO, EEE e serviços têm consequências dependentes de driver/carga. Preferências devem ser avaliadas com medições comparáveis e possibilidade de restaurar o estado original.