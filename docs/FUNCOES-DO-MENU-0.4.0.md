# Funções do menu — VL Optimizer 0.4.0

A imagem orientou a navegação. O nome GDZ Scaling da referência corresponde a VL Scaling, preservando a identidade deste projeto. Bloqueios comerciais do GDZ não foram copiados; cada função confere sistema, hardware e permissão.

## Áreas solicitadas

| Menu | Função disponível |
| --- | --- |
| Painel | Diagnóstico, recomendações e últimas atividades. |
| Desempenho | Energia, gráficos e tarefas de fundo, com efeitos e limites de cada ajuste. |
| Mouse e teclado | Aceleração, repetição, atalhos de acessibilidade, Num Lock e eventos do mouse. |
| Memória | RAM, commit, standby, processos e paginação automática. |
| Limpeza | Caches, temporários, Lixeira, componentes e espaço ocupado. |
| Programas | Inventário, desinstalação, sobras e pacotes opcionais do Windows. |
| Internet e live | DNS, propriedades do driver, reparo de rede e preparação do OBS. |
| Windows | Privacidade, Explorador, aparência, manutenção e recuperação. |
| Inicialização | Entradas de inicialização e reativação pelo backup do VL. |
| Meus jogos | Bibliotecas, perfis, GPU, sessões e FiveM. |
| DLSS / FSR | Perfil NVIDIA, tecnologias de imagem e integração local OptiScaler. |
| VL Scaling | Perfis de escala, captura, cursor e geração de quadros desejada. Motor gráfico externo. |
| Medidor de FPS | Frametimes, captura, comparação e sobreposição externa opcional. |
| Verificar hardware | CPU, RAM, BIOS, GPUs e discos com origem dos dados. |
| Histórico e restauração | Operações, estado anterior e restauração condicional. |
| Configurações | Movimento, economia, privacidade e versão. |

São 13 entradas principais e três atalhos inferiores: hardware, histórico e configurações. Em janela larga o menu fica aberto. A seleção do catálogo vale para a área atual; prévias sobrepostas são bloqueadas antes de escrever.

## Pesquisa e critérios

Foram cruzados os 78 itens de PAGES, ações/handlers recuperados e contratos já implementados, com documentação primária atual. O INI oficial OptiScaler mudou em relação à UI antiga: geração tem origem/saída separadas; contador e atalho pertencem a Menu. O editor modifica dez chaves fechadas somente quando já existem, preserva codificação/comentários e salva backup antes de escrever. Presença de chave não comprova suporte a todo valor por um binário antigo.

O overlay FPS novo é uma janela externa com passagem de cliques e sem ativação, nos quatro cantos da tela principal. Mostra média acumulada, frametime e frames do CSV da captura própria. Não injeta biblioteca nem garante aparecer em tela cheia exclusiva. Lows ficam no relatório final.

As dicas são identificadas como orientação manual: OBS, sincronização/latência, BIOS, chipset e perfis de memória. Não há medições ou correções fictícias por modelo de hardware.

## Mapa das 78 entradas

### 01. Plano de energia Alto Desempenho

Mecanismo recuperado da amostra: powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c; se falha tenta /duplicatescheme com esse GUID, depois /setactive novamente. Emite GDZ_ERRO se rejeitado.

No VL: Seleciona o plano Alto Desempenho somente quando está instalado e disponível. ID: power-high

### 02. Plano de energia Desempenho Máximo

Mecanismo recuperado da amostra: Mesmo procedimento com o GUID e9a42b02-d5df-448d-aa00-03f14749eb61 (Ultimate Performance).

No VL: Seleciona o plano Desempenho Máximo somente quando está instalado e disponível. ID: power-ultimate

### 03. Modo certo para Ryzen X3D

Mecanismo recuperado da amostra: Ativa plano Equilibrado 381b4222-f694-41f0-9685-ff5bb260df2e; liga AllowAutoGameMode/AutoGameModeEnabled/UseNexusForGameBarEnabled; remove política AllowGameDVR. Se Game Bar falta, abre sua página na Store; se amd3dvcacheSvc falta, apenas avisa instalar chipset AMD.

No VL: Seleciona Equilibrado, liga Modo Jogo e Nexus e remove a política de bloqueio Game DVR. Não instala drivers AMD nem pacotes Xbox. ID: x3d-mode

### 04. Modo de Jogo do Windows

Mecanismo recuperado da amostra: Grava AllowAutoGameMode=1 e AutoGameModeEnabled=1 em HKCU\SOFTWARE\Microsoft\GameBar.

No VL: Configura os dois sinalizadores de Modo Jogo do Windows. O efeito depende do jogo e da versão do sistema. ID: game-mode

### 05. Sem gravação em segundo plano

Mecanismo recuperado da amostra: DVR_OFF: AppCaptureEnabled=0 em GameDVR e GameDVR_Enabled=0 em GameConfigStore (HKCU).

No VL: Desativa captura de aplicativos e Game DVR; não remove a Xbox Game Bar. ID: dvr-off

### 06. Game Bar (Win+G) desligada

Mecanismo recuperado da amostra: Executa o bloco GAMEBAR_OFF: políticas/valores do Game Bar e DVR, parada de processos; difere da remoção do pacote Appx.

No VL: Desativa captura/DVR, atalho Nexus e política Game DVR. ID: gamebar-off

### 07. Sobreposição do Steam (Shift+Tab) desligada

Mecanismo recuperado da amostra: Usa helper de leitura/escrita VDF para a opção da sobreposição: valor 0 ao aplicar e 1 ao desfazer. Recusa se há jogo Steam aberto; pede shutdown da Steam, espera até 40×500 ms, altera VDF e reabre se estava aberta.

No VL: Configuração EnableGameOverlay no localconfig.vdf de cada conta Steam. ID: steam-overlay

### 08. Dica: a sobreposição da NVIDIA se desliga na NVIDIA App

Mecanismo recuperado da amostra: Apresenta a recomendação escrita pelo produto; desempenho e compatibilidade não foram confirmados nesta análise.

No VL: Guia da sobreposição NVIDIA; configuração manual no aplicativo do fabricante.

### 09. Dica: a sobreposição do Discord se desliga dentro do Discord

Mecanismo recuperado da amostra: Apresenta a recomendação escrita pelo produto; desempenho e compatibilidade não foram confirmados nesta análise.

No VL: Guia da sobreposição Discord; sem encerrar ou editar arquivos internos do aplicativo.

### 10. Windows não reinicia sozinho

Mecanismo recuperado da amostra: Grava NoAutoRebootWithLoggedOnUsers=1 em HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU. A existência de uma política não prova impedir todos os tipos de reinício.

No VL: Define a política NoAutoRebootWithLoggedOnUsers. A política é interpretada pelo Windows Update e pode variar com a política da organização. ID: updates-no-reboot

### 11. Sem notificações do Windows

Mecanismo recuperado da amostra: Grava ToastEnabled=0 em HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\PushNotifications.

No VL: Configura ToastEnabled no usuário atual; pode afetar alertas importantes. ID: notifications-off

### 12. Sem apps da Loja rodando escondidos

Mecanismo recuperado da amostra: Grava GlobalUserDisabled=1 em BackgroundAccessApplications e BackgroundAppGlobalToggle=0 em Search (HKCU).

No VL: Configura as permissões globais de execução em segundo plano para aplicativos compatíveis da Store. ID: bg-apps

### 13. Edge fechado de verdade

Mecanismo recuperado da amostra: Grava StartupBoostEnabled=0 e BackgroundModeEnabled=0 nas políticas do Microsoft Edge no HKLM.

No VL: Desativa por política o Startup Boost e a permanência do Edge em segundo plano. ID: edge-bg

### 14. Agendamento de GPU por hardware (HAGS)

Mecanismo recuperado da amostra: Grava HwSchMode=2 em HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers; reinício indicado na UI.

No VL: Define HwSchMode=2. O efeito depende do suporte da GPU e do driver. ID: hags

### 15. Jogos em janela e sem bordas otimizados

Mecanismo recuperado da amostra: No valor String DirectXUserGlobalSettings de HKCU\Software\Microsoft\DirectX\UserGpuPreferences, remove SwapEffectUpgradeEnable=\d; existente e acrescenta SwapEffectUpgradeEnable=1;.

No VL: Atualiza somente o token SwapEffectUpgradeEnable na configuração DirectX global, preservando outras opções. ID: windowed-games

### 16. DLSS no modelo recomendado pela NVIDIA

Mecanismo recuperado da amostra: Usa NVAPI DRS: IDs 283385345 (DLSS_SR_OVERRIDE)=1 e 283385331 (DLSS_SR_PRESET)=16777215 (recomendado). Não modifica DLLs do jogo neste ajuste.

No VL: Ativa a substituição DLSS-SR e seleciona o preset mais recente anunciado pelo driver NVIDIA por dois ajustes DRS oficiais. Não altera arquivos do jogo. ID: dlss-rec

### 17. Placa de vídeo com interrupção MSI e prioridade alta

Mecanismo recuperado da amostra: Para a GPU detectada em caps.gpuIrq, grava MSISupported=1 em MessageSignaledInterruptProperties e DevicePriority=3 em Affinity Policy. Caminho depende da GPU; exige reinício.

No VL: Configura MSI e prioridade de interrupção apenas em dispositivos de vídeo PCI NVIDIA/AMD detectados. ID: gpu-msi

### 18. Correção de tela piscando (MPO)

Mecanismo recuperado da amostra: Grava OverlayTestMode=5 no Dwm e DisableOverlays=1 em GraphicsDrivers (HKLM); exige reinício segundo o contrato.

No VL: Desativa overlays por configuração gráfica, como diagnóstico de flicker. Não é uma recomendação universal. ID: mpo

### 19. Sem efeitos visuais do Windows

Mecanismo recuperado da amostra: Grava VisualFXSetting=2 em Explorer\VisualEffects e UserPreferencesMask=bytes [90,12,03,80,10,00,00,00] hex em HKCU\Control Panel\Desktop.

No VL: Define VisualFXSetting=2 e a máscara de preferências visuais indicada na prévia. ID: visual-effects-off

### 20. Sem transparência nas janelas

Mecanismo recuperado da amostra: Grava EnableTransparency=0 em Themes\Personalize (HKCU) e transmite WM_SETTINGCHANGE/ImmersiveColorSet por SendMessageTimeout para atualizar aparência.

No VL: Desativa transparência em componentes compatíveis e notifica a sessão. ID: transparency

### 21. SysMain (Superfetch) desligado

Mecanismo recuperado da amostra: Stop-Service SysMain e Set-Service SysMain -StartupType Disabled.

No VL: Para o serviço SysMain e altera sua inicialização para Desativado. ID: sysmain-off

### 22. Indexação de arquivos desligada

Mecanismo recuperado da amostra: Stop-Service WSearch e Set-Service WSearch -StartupType Disabled.

No VL: Para o serviço WSearch e desativa sua inicialização. ID: indexing-off

### 23. Serviços pouco usados desligados

Mecanismo recuperado da amostra: Para e define Disabled para Spooler, Fax, RemoteRegistry, MapsBroker e WbioSrvc: impressão, fax, registro remoto, mapas e biometria.

No VL: Para e desativa somente Spooler, Fax, RemoteRegistry, MapsBroker e WbioSrvc que estiverem instalados; cada serviço tem estado anterior próprio. ID: unused-services-off

### 24. Serviços do Xbox desligados

Mecanismo recuperado da amostra: Para e define Disabled para XblAuthManager, XblGameSave e XboxNetApiSvc.

No VL: Desativa XblAuthManager, XblGameSave e XboxNetApiSvc quando presentes. ID: xbox-services-off

### 25. Xbox Game Bar desinstalado

Mecanismo recuperado da amostra: Desliga DVR/Game Bar, encerra GameBar/GameBarFTServer/GameBarPresenceWriter e remove Microsoft.XboxGamingOverlay com Remove-AppxPackage. Sem o pacote, cria handlers ms-gamebar/ms-gamebarservices/ms-gamingoverlay apontando para systray.exe. A reversão extra usa GAMEBAR_REINSTALL.

No VL: Catálogo UWP: remoção individual XboxGamingOverlay, protegida em X3D; sem protocolos substitutos.

### 26. Tela em 720p (emergência)

Mecanismo recuperado da amostra: Lê PrimaryScreen.Bounds e grava PrevResW/PrevResH em HKCU\Software\GDZ; DEVMODE recebe 1280×720 e dmFields=0x180000; user32 ChangeDisplaySettings(ref m,0), sem CDS_UPDATEREGISTRY neste script.

No VL: Seleciona 1280×720 no monitor primário somente quando o driver anuncia o modo com a frequência e profundidade atuais. O histórico salva frequência, rotação, posição e resolução para restauração. ID: resolution-720p

### 27. Remover apps que vêm com o Windows

Mecanismo recuperado da amostra: Remove por Get-AppxPackage/Remove-AppxPackage uma lista fixa: Xbox.TCUI, XboxApp, XboxGamingOverlay, Cortana, BingWeather, GetHelp, People, WindowsMaps, ZuneMusic e ZuneVideo; em X3D de dois CCDs preserva TCUI e XboxGamingOverlay. A descrição menciona outros apps, mas a lista é que delimita a ação.

No VL: Catálogo fechado de dez pacotes UWP; prévia individual, verificação e sem undo fictício.

### 28. Chrome e Discord em prioridade baixa

Mecanismo recuperado da amostra: Define PriorityClass=BelowNormal para processos chrome/Discord em execução; não fixa porcentagem de CPU/RAM.

No VL: Sessões opt-in com prioridades originais e identidade dos processos. BelowNormal não é limite de RAM/CPU.

### 29. Fechar OneDrive, Dropbox e Google Drive

Mecanismo recuperado da amostra: Stop-Process -Force para OneDrive, Dropbox, GoogleDriveFS e BackgroundTransferHost; não remove autostart.

No VL: Processos: encerramento individual revalidando PID, início e caminho; sem fechar sincronizadores automaticamente.

### 30. Mira sem aceleração do mouse

Mecanismo recuperado da amostra: Grava MouseSpeed, MouseThreshold1 e MouseThreshold2 como String 0 em HKCU\Control Panel\Mouse e aplica SystemParametersInfo(4,0,[0,0,0],2). Desfazer reaplica os valores do Registro.

No VL: Configura a aceleração do mouse e solicita atualização à sessão por SystemParametersInfo. ID: mouse-accel

### 31. Teclado repetindo na velocidade máxima

Mecanismo recuperado da amostra: Grava KeyboardDelay=String 0 e KeyboardSpeed=String 31 em HKCU\Control Panel\Keyboard; usa KBD_APPLY_FROM_REG para aplicar e desfazer.

No VL: Configura atraso e velocidade de repetição do teclado e solicita atualização da sessão. ID: kbd-repeat

### 32. Sem janelas de acessibilidade no meio do jogo

Mecanismo recuperado da amostra: Grava Flags=String 506 em StickyKeys, 58 em ToggleKeys e 122 em Keyboard Response (HKCU\Control Panel\Accessibility). O probe verifica o bit 4; desativa atalhos, sem remover os recursos.

No VL: Desativa os atalhos de ativação de Teclas de Aderência, Alternância e Filtragem por seus sinalizadores. ID: sticky

### 33. Num Lock ligado ao iniciar

Mecanismo recuperado da amostra: Grava InitialKeyboardIndicators=String 2 em HKCU e HKEY_USERS\.DEFAULT\Control Panel\Keyboard; os padrões de fallback são 0 e 2147483648.

No VL: Configura Num Lock no usuário atual e na área de login padrão. ID: numlock

### 34. Memória virtual gerenciada pelo Windows

Mecanismo recuperado da amostra: Lê Win32_ComputerSystem; se já automático emite aviso; caso contrário Set-CimInstance -Property @{AutomaticManagedPagefile=$true}. Não escreve diretamente um tamanho fixo.

No VL: Ativa AutomaticManagedPagefile por WMI. O estado manual anterior e PagingFiles com tipo/ausência são preservados no histórico. ID: pagefile-auto

### 35. Limpar sobras do Windows Update

Mecanismo recuperado da amostra: Mede espaço livre do disco do sistema; Dism.exe /Online /Cleanup-Image /StartComponentCleanup /NoRestart. Confere LASTEXITCODE; mede diferença e informa GB se >0,05. Timeout 2400000 ms (40 min). Não usa ResetBase neste script.

No VL: CleanupWindowsUpdate: DISM StartComponentCleanup, sem ResetBase.

### 36. Desligar de verdade (sem hibernação)

Mecanismo recuperado da amostra: Grava HibernateEnabled=0 e chama powercfg /hibernate off. Ao desligar, powercfg /hibernate on só é chamado se o Registro restaurado indicar 1.

No VL: Desativa hibernação pelo powercfg e preserva tipo e tamanho configurados para restauração. ID: hibernate

### 37. Servidor DNS

Mecanismo recuperado da amostra: Nos adaptadores com Status Up, Set-DnsClientServerAddress recebe os dois IPv4 do provedor e Clear-DnsClientCache limpa cache. Cria snapshot sob Tweak: dns antes da primeira troca. Automático tenta reverter snapshot e, se ainda não reconhecido como auto, usa ResetServerAddresses.

No VL: Cinco opções DNS: DHCP e quatro provedores, snapshot por GUID de NIC física.

### 38. Sem upload de atualizações para outros PCs

Mecanismo recuperado da amostra: Grava DODownloadMode=0 em política DeliveryOptimization do HKLM.

No VL: Define a política de Delivery Optimization para evitar compartilhamento de atualizações entre PCs. ID: delivery

### 39. LSO da placa de rede desligado

Mecanismo recuperado da amostra: Nos adaptadores físicos detectados, grava *LsoV2IPv4=String 0 e, quando existe, *LsoV2IPv6=String 0 sob a classe de rede {4d36e972-e325-11ce-bfc1-08002be10318}; reinicia adaptador. Valor padrão listado é 1, sujeito ao backup.

No VL: Desativa as propriedades LSO IPv4/IPv6 anunciadas pelo driver de rede. Pode elevar utilização de CPU. ID: lso

### 40. Economia de energia da placa de rede desligada (EEE)

Mecanismo recuperado da amostra: Para palavras de driver detectadas (*EEE, EEE, AdvancedEEE, EEELinkAdvertisement, EnableGreenEthernet), grava String 0 sob a classe de rede. O fallback de reversão vem do default declarado pelo driver em Ndi\Params; reinicia adaptador.

No VL: Desativa as propriedades EEE e Green Ethernet anunciadas pelo driver. Use para investigar instabilidade de Ethernet. ID: eee

### 41. Placa de rede sem moderação de interrupção

Mecanismo recuperado da amostra: Para *InterruptModeration detectado no driver, grava String 0 sob a classe de rede, depois reinicia adaptador; fallback de reversão é o default obtido no driver.

No VL: Desativa a propriedade de moderação de interrupções da placa de rede; pode elevar uso de CPU. ID: nic-intmod

### 42. Limpar cache de DNS

Mecanismo recuperado da amostra: Clear-DnsClientCache e ipconfig /flushdns. Timeout 30000 ms. Saída do ipconfig é descartada e a mensagem de sucesso é escrita pelo script.

No VL: FlushDns: API local, sem mudar o provedor DNS.

### 43. Resetar a rede

Mecanismo recuperado da amostra: ipconfig /flushdns; netsh winsock reset; netsh int ip reset. Timeout 120000 ms. Escreve aviso de reiniciar; não há verificação explícita de todos os códigos de saída nesta string.

No VL: ResetNetwork: Winsock/TCP-IP com revisão, saída por etapa e aviso de reinício.

### 44. OBS com prioridade

Mecanismo recuperado da amostra: Para caps.obsExe, preserva flags existentes e acrescenta RUNASADMIN em AppCompatFlags\Layers (HKCU); executa powercfg /powerthrottling disable /path exe. Desfazer extra usa /powerthrottling reset; pode provocar UAC toda abertura.

No VL: Acrescenta RUNASADMIN ao Layers do OBS e cria a exceção de power throttling para obs64.exe. Não altera a classe de prioridade do processo. ID: obs-priority

### 45. Configuração ideal do OBS

Mecanismo recuperado da amostra: Retorna recomendações fixas por capacidade: NVIDIA→NVENC H.264 Twitch/AV1 ou HEVC YouTube, P5/P6; AMD→AMF e AV1 citado RX7000+; Intel→QSV; nenhum→x264 veryfast 720p. CBR/keyframe 2 s; Twitch 6000 kbps 1080p60 ou 4500 kbps 720p60. Informa se OBS foi detectado. Não configura OBS nem mede upload.

No VL: Assistente oficial do OBS e guia da plataforma. Não grava bitrate ou chave de transmissão arbitrariamente.

### 46. Telemetria e anúncios personalizados

Mecanismo recuperado da amostra: Grava AllowTelemetry=0, AdvertisingInfo\Enabled=0, TailoredExperiencesWithDiagnosticDataEnabled=0 e NumberOfSIUFInPeriod=0; desativa e para DiagTrack. Desfazer tenta iniciar o serviço se ele não estiver Disabled.

No VL: Configura políticas de diagnóstico/publicidade e interrompe o serviço DiagTrack; políticas variam por edição do Windows. ID: telemetry

### 47. Sem sugestões, dicas e apps instalados sozinhos

Mecanismo recuperado da amostra: Grava 0 nos oito indicadores do ContentDeliveryManager (SilentInstalledAppsEnabled, SystemPaneSuggestionsEnabled, SoftLandingEnabled e cinco SubscribedContent-*Enabled) e DisableWindowsConsumerFeatures=1 em CloudContent.

No VL: Desativa os sinalizadores de sugestões e conteúdo promocional listados na prévia. ID: suggestions

### 48. Pesquisa do Iniciar sem Bing

Mecanismo recuperado da amostra: Grava BingSearchEnabled=0 e DisableSearchBoxSuggestions=1; encerra SearchHost/SearchApp e reinicia Explorer ao aplicar e ao desfazer.

No VL: Configura pesquisa local e política de sugestões na caixa de busca. A sessão deve ser renovada para o Explorer atualizar. ID: bing

### 49. Sem histórico de atividades

Mecanismo recuperado da amostra: Grava EnableActivityFeed=0, PublishUserActivities=0 e UploadUserActivities=0 em política HKLM\SOFTWARE\Policies\Microsoft\Windows\System.

No VL: Configura três políticas relacionadas ao histórico e publicação de atividades. ID: activity

### 50. Sem Widgets (notícias e clima)

Mecanismo recuperado da amostra: Grava AllowNewsAndInterests=0 em política Dsh e EnableFeeds=0 em Windows Feeds; encerra Widgets/WidgetService. Reinício indicado.

No VL: Configura as políticas de Widgets e Windows Feeds. Mudança de sessão pode ser necessária. ID: widgets

### 51. Localização desligada

Mecanismo recuperado da amostra: Grava Value=String Deny em ConsentStore\location (HKLM); fallback desligado Allow. Não é prova de impedir toda inferência de localização por qualquer programa.

No VL: Configura a permissão global de localização para Deny. Aplicativos e funções dependentes podem parar de receber localização. ID: location

### 52. Finalizar tarefa pela barra de tarefas

Mecanismo recuperado da amostra: Grava TaskbarEndTask=1 em Explorer\Advanced\TaskbarDeveloperSettings (HKCU); bloqueado abaixo do build 22631 quando build é conhecido.

No VL: Exibe a opção Finalizar tarefa no menu da barra de tarefas em versões compatíveis. ID: end-task

### 53. Mostrar a extensão dos arquivos

Mecanismo recuperado da amostra: Grava HideFileExt=0 em Explorer\Advanced (HKCU).

No VL: Exibe extensões de arquivos no Explorer. ID: file-ext

### 54. Mostrar arquivos ocultos

Mecanismo recuperado da amostra: Grava Hidden=1 em Explorer\Advanced (HKCU). Não altera ShowSuperHidden nesta declaração.

No VL: Exibe arquivos ocultos no Explorer; arquivos protegidos do sistema mantêm configuração independente. ID: hidden-files

### 55. Explorador abre em "Este Computador"

Mecanismo recuperado da amostra: Grava LaunchTo=1 em Explorer\Advanced (HKCU), abrindo em Este Computador.

No VL: Configura o Explorer para abrir em Este Computador. ID: this-pc

### 56. Menu do botão direito completo

Mecanismo recuperado da amostra: Cria HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32 com valor padrão vazio e reinicia Explorer. Ao desfazer remove recursivamente a chave CLSID deste truque; só Windows 11.

No VL: Configura o registro de compatibilidade do menu de contexto; versões novas do Windows podem ignorá-lo. ID: classic-menu

### 57. Tema escuro

Mecanismo recuperado da amostra: Grava AppsUseLightTheme=0 e SystemUsesLightTheme=0 em Themes\Personalize (HKCU).

No VL: Configura o tema escuro de aplicativos compatíveis e do sistema. ID: dark

### 58. Barra de tarefas à esquerda

Mecanismo recuperado da amostra: Grava TaskbarAl=0 em Explorer\Advanced (HKCU), bloqueado fora do Windows 11.

No VL: Alinha os ícones da barra de tarefas à esquerda. ID: taskbar-left

### 59. Esconder o botão Visão de Tarefas

Mecanismo recuperado da amostra: Grava ShowTaskViewButton=0 em Explorer\Advanced; Win+Tab continua disponível segundo a descrição.

No VL: Oculta o botão Visão de Tarefas, preservando a função e seus atalhos. ID: task-view

### 60. Pesquisa da barra só como ícone

Mecanismo recuperado da amostra: Grava SearchboxTaskbarMode=1 em HKCU\...\Search; o fallback é 2.

No VL: Configura a apresentação da pesquisa como ícone na barra de tarefas. ID: search-icon

### 61. Tela azul com detalhes do erro

Mecanismo recuperado da amostra: Grava DisplayParameters=1 e DisableEmoticon=1 em HKLM\SYSTEM\CurrentControlSet\Control\CrashControl.

No VL: Configura apresentação dos parâmetros de parada e ocultação do emoticon na tela de erro. ID: bsod-detail

### 62. Criar ponto de restauração

Mecanismo recuperado da amostra: Temporariamente grava SystemRestorePointCreationFrequency=0 em HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore; Enable-ComputerRestore no disco do sistema e Checkpoint-Computer -Description GDZ Optimizer -RestorePointType MODIFY_SETTINGS; finally restaura/remove o valor anterior. Timeout 300000 ms (5 min).

No VL: CreateRestorePoint: confere nova sequência; depende da Proteção do Sistema.

### 63. Reparar arquivos do Windows

Mecanismo recuperado da amostra: Dism.exe /Online /Cleanup-Image /RestoreHealth /NoRestart; guarda código DISM; depois sfc.exe /scannow. Reporta erro se DISM falhou e aviso final se não. Timeout 3600000 ms (60 min). O código de saída do SFC não é avaliado explicitamente nesse script.

No VL: RepairWindows: DISM/SFC, códigos e saída por etapa; exit code não prova saúde.

### 64. Reiniciar a barra de tarefas

Mecanismo recuperado da amostra: Stop-Process explorer -Force; espera 600 ms; Start-Process explorer. Não substitui o shell.

No VL: RestartExplorer: valida caminho, usuário e sessão do shell e verifica o novo processo.

### 65. Limpar ajustes antigos do GDZ

Mecanismo recuperado da amostra: LEGACY_DEFAULTS remove TdrDelay, IRQ8Priority, PollInterval100us, MaxFrameLatency, ajustes NVTweak, TcpAckFrequency/TCPNoDelay/TcpMaxDataRetransmissions e opções BCD useplatformtick/useplatformclock; restaura Win32PrioritySeparation=2, MouseDataQueueSize=100, SystemResponsiveness=20, autotuning TCP normal, DoubleClickSpeed=500 e suavização de fontes.

No VL: Substituição deliberada: recuperar snapshots próprios no histórico. Não impõe padrões universais de BCD/rede/HID de outro produto.

### 66. Religar o Windows Defender

Mecanismo recuperado da amostra: Set-MpPreference -DisableRealtimeMonitoring $false e Remove-ItemProperty DisableAntiSpyware da política Windows Defender. Exceções/erros são suprimidos nesse fallback, portanto a declaração não garante que Tamper Protection/políticas permitam religar.

No VL: EnableDefender: solicita proteção e verifica estado; não remove políticas nem contorna Tamper Protection.

### 67. Religar o Windows Update

Mecanismo recuperado da amostra: Set-Service wuauserv -StartupType Manual e Start-Service wuauserv. Erros são silenciosos nesse fallback.

No VL: Configura wuauserv e BITS para início manual e solicita sua execução. O tipo e o estado anteriores ficam no histórico. ID: windows-update-services

### 68. Religar a Restauração do Sistema

Mecanismo recuperado da amostra: Enable-ComputerRestore -Drive $env:SystemDrive com ErrorAction SilentlyContinue. Não cria por si só um novo ponto neste reparo.

No VL: EnableSystemRestore: solicita ativação na unidade Windows. Confirmação de ponto é outra etapa.

### 69. Consertar a escala da tela

Mecanismo recuperado da amostra: Remove-ItemProperty Win8DpiScaling de HKCU\Control Panel\Desktop. Não define explicitamente uma escala percentual nova nem corrige toda incompatibilidade DPI.

No VL: Abre Escala e monitores no Windows, em vez de impor 96 DPI para todo monitor.

### 70. Verificar Ryzen

Mecanismo recuperado da amostra: Lê nome CPU, procura dispositivos PnP AMD 3D V-Cache/PPM/GPIO/PSP/SMBus/I2C. Para 7900X3D/7950X3D/9900X3D/9950X3D/7945HX3D confere amd3dvcacheSvc, pacote XboxGamingOverlay, política AllowGameDVR e GUID Equilibrado ativo. Fora desses modelos só orienta EXPO/PBO; não altera BIOS.

No VL: Inventário com fonte WMI e orientação manual de firmware/driver. Não comprova correções, XMP/EXPO ou suporte por heurísticas de nome. Guias GPU em DLSS / FSR.

### 71. Verificar processador Intel

Mecanismo recuperado da amostra: Lê Update Revision no Registro e extrai UInt32 a partir do byte 4. Para desktop Core i5/i7/i9 13ª/14ª gerações avisa se microcode conhecido<0x12B; para Core Ultra 200 confere build>=26100.2314 e microcode conhecido>=0x114. Em híbridos olha AutoGameModeEnabled. Não consulta versão mais recente online.

No VL: Inventário com fonte WMI e orientação manual de firmware/driver. Não comprova correções, XMP/EXPO ou suporte por heurísticas de nome. Guias GPU em DLSS / FSR.

### 72. Dica: o maior ganho do Ryzen está na BIOS

Mecanismo recuperado da amostra: Apresenta a recomendação escrita pelo produto; desempenho e compatibilidade não foram confirmados nesta análise.

No VL: Inventário com fonte WMI e orientação manual de firmware/driver. Não comprova correções, XMP/EXPO ou suporte por heurísticas de nome. Guias GPU em DLSS / FSR.

### 73. Dica: Intel com P-cores e E-cores

Mecanismo recuperado da amostra: Apresenta a recomendação escrita pelo produto; desempenho e compatibilidade não foram confirmados nesta análise.

No VL: Inventário com fonte WMI e orientação manual de firmware/driver. Não comprova correções, XMP/EXPO ou suporte por heurísticas de nome. Guias GPU em DLSS / FSR.

### 74. Verificar placa de vídeo

Mecanismo recuperado da amostra: Lê Win32_VideoController de VEN_10DE/1002/8086. Marca driver NVIDIA/AMD não integrado com data>180 dias; lê HwSchMode. Se encontra nvidia-smi, usa -q -d MEMORY e heurística BAR1 Total>256 MiB como ReBAR ligado. Não muda driver/BIOS.

No VL: Inventário com fonte WMI e orientação manual de firmware/driver. Não comprova correções, XMP/EXPO ou suporte por heurísticas de nome. Guias GPU em DLSS / FSR.

### 75. Dica NVIDIA RTX: mais FPS com o DLSS 4.5

Mecanismo recuperado da amostra: Apresenta a recomendação escrita pelo produto; desempenho e compatibilidade não foram confirmados nesta análise.

No VL: Inventário com fonte WMI e orientação manual de firmware/driver. Não comprova correções, XMP/EXPO ou suporte por heurísticas de nome. Guias GPU em DLSS / FSR.

### 76. Dica NVIDIA: menor atraso sem cortes na imagem

Mecanismo recuperado da amostra: Apresenta a recomendação escrita pelo produto; desempenho e compatibilidade não foram confirmados nesta análise.

No VL: Inventário com fonte WMI e orientação manual de firmware/driver. Não comprova correções, XMP/EXPO ou suporte por heurísticas de nome. Guias GPU em DLSS / FSR.

### 77. Dica AMD Radeon: menor atraso

Mecanismo recuperado da amostra: Apresenta a recomendação escrita pelo produto; desempenho e compatibilidade não foram confirmados nesta análise.

No VL: Inventário com fonte WMI e orientação manual de firmware/driver. Não comprova correções, XMP/EXPO ou suporte por heurísticas de nome. Guias GPU em DLSS / FSR.

### 78. Verificar memória (XMP/EXPO)

Mecanismo recuperado da amostra: Lê Win32_PhysicalMemory, maior ConfiguredClockSpeed, tipo SMBIOS, capacidade e PartNumber. Extrai números de quatro dígitos 2400..9000 do modelo como velocidade presumida do kit. Avisa se kit>config+100; ou DDR4<=2666/DDR5<=4800; ou só um módulo. Menos de 16 GB acrescenta orientação. Não lê diretamente XMP/EXPO na BIOS.

No VL: Inventário com fonte WMI e orientação manual de firmware/driver. Não comprova correções, XMP/EXPO ou suporte por heurísticas de nome. Guias GPU em DLSS / FSR.

## Detalhamento das configurações atuais

As definições abaixo têm prévia, aprovação consumível, snapshot anterior, releitura e restauração com conflito. O JSON público preserva os valores técnicos e as fontes.

### `game-mode` — Modo de Jogo do Windows

Configura os dois sinalizadores de Modo Jogo do Windows. O efeito depende do jogo e da versão do sistema.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `delivery` — Sem upload de atualizações para outros PCs

Define a política de Delivery Optimization para evitar compartilhamento de atualizações entre PCs.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `edge-bg` — Edge fechado de verdade

Desativa por política o Startup Boost e a permanência do Edge em segundo plano.
Impacto: O Edge pode exibir a indicação Gerenciado pela sua organização enquanto esta política estiver definida.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `bg-apps` — Sem apps da Loja rodando escondidos

Configura as permissões globais de execução em segundo plano para aplicativos compatíveis da Store.
Impacto: Aplicativos podem deixar de receber notificações em segundo plano.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `hibernate` — Desligar de verdade (sem hibernação)

Desativa hibernação pelo powercfg e preserva tipo e tamanho configurados para restauração.
Impacto: O arquivo de hibernação pode ser removido; seu conteúdo não é recuperável. O tipo e o tamanho configurados são preservados.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `mpo` — Correção de tela piscando (MPO)

Desativa overlays por configuração gráfica, como diagnóstico de flicker. Não é uma recomendação universal.
Impacto: Pode afetar reprodução de vídeo, consumo de energia e composição gráfica. Use somente para diagnóstico.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `gpu-msi` — Placa de vídeo com interrupção MSI e prioridade alta

Configura MSI e prioridade de interrupção apenas em dispositivos de vídeo PCI NVIDIA/AMD detectados.
Impacto: Mudanças de interrupção podem causar instabilidade de driver; salve o trabalho e mantenha uma forma de restaurar a configuração.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `dlss-rec` — DLSS no modelo recomendado pela NVIDIA

Ativa a substituição DLSS-SR e seleciona o preset mais recente anunciado pelo driver NVIDIA por dois ajustes DRS oficiais. Não altera arquivos do jogo.
Impacto: O suporte depende de GPU, driver, jogo e perfil NVIDIA.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `steam-overlay` — Sobreposição do Steam (Shift+Tab) desligada

Configuração EnableGameOverlay no localconfig.vdf de cada conta Steam.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `lso` — LSO da placa de rede desligado

Desativa as propriedades LSO IPv4/IPv6 anunciadas pelo driver de rede. Pode elevar utilização de CPU.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `eee` — Economia de energia da placa de rede desligada (EEE)

Desativa as propriedades EEE e Green Ethernet anunciadas pelo driver. Use para investigar instabilidade de Ethernet.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `nic-intmod` — Placa de rede sem moderação de interrupção

Desativa a propriedade de moderação de interrupções da placa de rede; pode elevar uso de CPU.
Impacto: Pode aumentar a carga de CPU durante tráfego intenso.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `mouse-accel` — Mira sem aceleração do mouse

Configura a aceleração do mouse e solicita atualização à sessão por SystemParametersInfo.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `sticky` — Sem janelas de acessibilidade no meio do jogo

Desativa os atalhos de ativação de Teclas de Aderência, Alternância e Filtragem por seus sinalizadores.
Impacto: Usuários que dependem de recursos de acessibilidade devem revisar os valores antes de aplicar.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `numlock` — Num Lock ligado ao iniciar

Configura Num Lock no usuário atual e na área de login padrão.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `kbd-repeat` — Teclado repetindo na velocidade máxima

Configura atraso e velocidade de repetição do teclado e solicita atualização da sessão.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `telemetry` — Telemetria e anúncios personalizados

Configura políticas de diagnóstico/publicidade e interrompe o serviço DiagTrack; políticas variam por edição do Windows.
Impacto: AllowTelemetry=0 não elimina todos os dados de diagnóstico em todas as edições do Windows.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `suggestions` — Sem sugestões, dicas e apps instalados sozinhos

Desativa os sinalizadores de sugestões e conteúdo promocional listados na prévia.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `bing` — Pesquisa do Iniciar sem Bing

Configura pesquisa local e política de sugestões na caixa de busca. A sessão deve ser renovada para o Explorer atualizar.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `activity` — Sem histórico de atividades

Configura três políticas relacionadas ao histórico e publicação de atividades.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `widgets` — Sem Widgets (notícias e clima)

Configura as políticas de Widgets e Windows Feeds. Mudança de sessão pode ser necessária.
Build mínima: 22000; dependências/privilégios são consultados na prévia.

### `location` — Localização desligada

Configura a permissão global de localização para Deny. Aplicativos e funções dependentes podem parar de receber localização.
Impacto: Pode afetar localização do dispositivo, aplicativos de clima e ajuste automático de fuso horário.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `end-task` — Finalizar tarefa pela barra de tarefas

Exibe a opção Finalizar tarefa no menu da barra de tarefas em versões compatíveis.
Build mínima: 22631; dependências/privilégios são consultados na prévia.

### `file-ext` — Mostrar a extensão dos arquivos

Exibe extensões de arquivos no Explorer.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `hidden-files` — Mostrar arquivos ocultos

Exibe arquivos ocultos no Explorer; arquivos protegidos do sistema mantêm configuração independente.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `this-pc` — Explorador abre em "Este Computador"

Configura o Explorer para abrir em Este Computador.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `classic-menu` — Menu do botão direito completo

Configura o registro de compatibilidade do menu de contexto; versões novas do Windows podem ignorá-lo.
Build mínima: 22000; dependências/privilégios são consultados na prévia.

### `dark` — Tema escuro

Configura o tema escuro de aplicativos compatíveis e do sistema.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `transparency` — Sem transparência nas janelas

Desativa transparência em componentes compatíveis e notifica a sessão.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `taskbar-left` — Barra de tarefas à esquerda

Alinha os ícones da barra de tarefas à esquerda.
Build mínima: 22000; dependências/privilégios são consultados na prévia.

### `task-view` — Esconder o botão Visão de Tarefas

Oculta o botão Visão de Tarefas, preservando a função e seus atalhos.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `search-icon` — Pesquisa da barra só como ícone

Configura a apresentação da pesquisa como ícone na barra de tarefas.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `bsod-detail` — Tela azul com detalhes do erro

Configura apresentação dos parâmetros de parada e ocultação do emoticon na tela de erro.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `dvr-off` — Desativar captura e DVR

Desativa captura de aplicativos e Game DVR; não remove a Xbox Game Bar.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `gamebar-off` — Desativar Game Bar e DVR

Desativa captura/DVR, atalho Nexus e política Game DVR.
Impacto: Pode prejudicar recursos Xbox. Bloqueado em processadores X3D com dois CCDs.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `notifications-off` — Desativar notificações do usuário

Configura ToastEnabled no usuário atual; pode afetar alertas importantes.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `updates-no-reboot` — Evitar reinício automático com sessão aberta

Define a política NoAutoRebootWithLoggedOnUsers. A política é interpretada pelo Windows Update e pode variar com a política da organização.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `sysmain-off` — Desativar SysMain

Para o serviço SysMain e altera sua inicialização para Desativado.
Impacto: Pode piorar abertura de programas; não é um ganho universal.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `indexing-off` — Desativar indexação do Windows

Para o serviço WSearch e desativa sua inicialização.
Impacto: A pesquisa de arquivos e e-mails pode ficar mais lenta ou incompleta.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `xbox-services-off` — Desativar os três serviços Xbox

Desativa XblAuthManager, XblGameSave e XboxNetApiSvc quando presentes.
Impacto: Login, salvamento na nuvem e funções Xbox podem deixar de funcionar.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `hags` — Agendamento de GPU por hardware

Define HwSchMode=2. O efeito depende do suporte da GPU e do driver.
Impacto: O registro não comprova que o recurso esteja ativo no driver; compare estabilidade e latência.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `visual-effects-off` — Reduzir efeitos visuais

Define VisualFXSetting=2 e a máscara de preferências visuais indicada na prévia.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `windowed-games` — Otimizações para jogos em janela

Atualiza somente o token SwapEffectUpgradeEnable na configuração DirectX global, preservando outras opções.
Build mínima: 22000; dependências/privilégios são consultados na prévia.

### `power-balanced` — Plano Equilibrado

Seleciona o plano Equilibrado já instalado; o plano anterior é salvo para restauração.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `power-high` — Plano Alto Desempenho

Seleciona o plano Alto Desempenho somente quando está instalado e disponível.
Impacto: Pode aumentar consumo de energia, ruído e temperatura.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `power-ultimate` — Plano Desempenho Máximo

Seleciona o plano Desempenho Máximo somente quando está instalado e disponível.
Impacto: Pode aumentar consumo de energia, ruído e temperatura. Equipamentos com Modern Standby podem não oferecer esse plano.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `x3d-mode` — Configuração para X3D com dois CCDs

Seleciona Equilibrado, liga Modo Jogo e Nexus e remove a política de bloqueio Game DVR. Não instala drivers AMD nem pacotes Xbox.
Impacto: Requer Xbox Game Bar e driver de chipset AMD com suporte a 3D V-Cache para o comportamento esperado.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `windows-update-services` — Reativar serviços do Windows Update

Configura wuauserv e BITS para início manual e solicita sua execução. O tipo e o estado anteriores ficam no histórico.
Impacto: Não remove políticas da organização nem garante o download de atualizações. O Windows pode iniciar ou parar estes serviços sob demanda.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `pagefile-auto` — Arquivo de paginação automático

Ativa AutomaticManagedPagefile por WMI. O estado manual anterior e PagingFiles com tipo/ausência são preservados no histórico.
Impacto: A alocação em uso só muda após reiniciar. A prévia não estima o tamanho futuro nem promete ganho de FPS.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `unused-services-off` — Desativar cinco serviços opcionais

Para e desativa somente Spooler, Fax, RemoteRegistry, MapsBroker e WbioSrvc que estiverem instalados; cada serviço tem estado anterior próprio.
Impacto: Pode interromper impressão, fax, Registro remoto, mapas offline e biometria/Windows Hello. Revise os cinco alvos e mantenha outra forma de login.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `obs-priority` — OBS com elevação e sem power throttling

Acrescenta RUNASADMIN ao Layers do OBS e cria a exceção de power throttling para obs64.exe. Não altera a classe de prioridade do processo.
Impacto: O próximo início do OBS pode solicitar UAC. O histórico preserva os tokens Layers anteriores e se já existia exceção de limitação de energia.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `dns-cloudflare` — DNS IPv4: Cloudflare

Configura DNS IPv4 por GUID nas interfaces Ethernet/Wi-Fi físicas detectadas. O histórico preserva o modo automático ou a lista estática de cada interface.
Impacto: Pode afetar nomes internos de empresas e acesso à rede. Não altera IPv6, proxies, NRPT nem políticas DoH; nenhum ganho de ping é presumido. Desativar seleciona modo automático; Restaurar recupera a configuração anterior.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `dns-google` — DNS IPv4: Google

Configura DNS IPv4 por GUID nas interfaces Ethernet/Wi-Fi físicas detectadas. O histórico preserva o modo automático ou a lista estática de cada interface.
Impacto: Pode afetar nomes internos de empresas e acesso à rede. Não altera IPv6, proxies, NRPT nem políticas DoH; nenhum ganho de ping é presumido. Desativar seleciona modo automático; Restaurar recupera a configuração anterior.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `dns-quad9` — DNS IPv4: Quad9

Configura DNS IPv4 por GUID nas interfaces Ethernet/Wi-Fi físicas detectadas. O histórico preserva o modo automático ou a lista estática de cada interface.
Impacto: Pode afetar nomes internos de empresas e acesso à rede. Não altera IPv6, proxies, NRPT nem políticas DoH; nenhum ganho de ping é presumido. Desativar seleciona modo automático; Restaurar recupera a configuração anterior.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `dns-adguard` — DNS IPv4: AdGuard

Configura DNS IPv4 por GUID nas interfaces Ethernet/Wi-Fi físicas detectadas. O histórico preserva o modo automático ou a lista estática de cada interface.
Impacto: Pode afetar nomes internos de empresas e acesso à rede. Não altera IPv6, proxies, NRPT nem políticas DoH; nenhum ganho de ping é presumido. Desativar seleciona modo automático; Restaurar recupera a configuração anterior.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `dns-auto` — DNS IPv4: Automático (DHCP)

Configura DNS IPv4 por GUID nas interfaces Ethernet/Wi-Fi físicas detectadas. O histórico preserva o modo automático ou a lista estática de cada interface.
Impacto: Pode afetar nomes internos de empresas e acesso à rede. Não altera IPv6, proxies, NRPT nem políticas DoH; nenhum ganho de ping é presumido. Desativar seleciona modo automático; Restaurar recupera a configuração anterior.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

### `resolution-720p` — Resolução 1280 × 720 na sessão

Seleciona 1280×720 no monitor primário somente quando o driver anuncia o modo com a frequência e profundidade atuais. O histórico salva frequência, rotação, posição e resolução para restauração.
Impacto: A imagem e o tamanho das janelas podem mudar imediatamente. A alteração usa a sessão atual e não grava um novo modo permanente no perfil do Windows.
Build mínima: 19041; dependências/privilégios são consultados na prévia.

## Fontes primárias conferidas em 08/11/2026

- [INI OptiScaler](https://github.com/optiscaler/OptiScaler/blob/master/OptiScaler.ini).
- [Assistente OBS](https://obsproject.com/kb/quick-start-guide/) e [codificador YouTube](https://support.google.com/youtube/answer/2853702?hl=pt-BR).
- [Defender](https://learn.microsoft.com/powershell/module/defender/set-mppreference) e [Proteção do Sistema](https://learn.microsoft.com/powershell/module/microsoft.powershell.management/enable-computerrestore?view=powershell-5.1).
- [Atalhos Windows Settings](https://learn.microsoft.com/windows/apps/develop/launch/launch-settings).
- [DLSS](https://www.nvidia.com/en-us/geforce/technologies/dlss/) e [Reflex](https://www.nvidia.com/en-us/geforce/technologies/reflex/).
- [Anti-Lag](https://www.amd.com/en/products/software/adrenalin/radeon-software-anti-lag.html) e [AFMF](https://www.amd.com/en/products/software/adrenalin/afmf.html).

## Limites

O motor gráfico proprietário GDZ/Lossless não foi reconstruído ou incluído. Os 40 campos VL Scaling continuam armazenamento/exportação e lançamento manual de programa externo. Nenhum shader/assembly do alvo foi incorporado. Não há bypass de anticheat, backend remoto ou promessa de FPS.

218 cenários isolados passaram, sem alterações reais de Windows/Defender/serviços/Proteção do Sistema no PC de desenvolvimento. O smoke percorre 16 destinos, quatro páginas legadas e 22 subabas novas, além das abas existentes. Estilos Win32 de passagem de cliques/não ativação do overlay foram conferidos em leitura do próprio HWND. Captura em jogo, mutações e homologação de drivers/VMs continuam pendentes.
