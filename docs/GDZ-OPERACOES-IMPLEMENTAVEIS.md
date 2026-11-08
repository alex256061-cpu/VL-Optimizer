# GDZ Optimizer 1.1.27 — catálogo de operações para implementação

Esta etapa transforma os nomes da interface em alvos concretos: comandos, valores de registro, pré-condições, snapshot, reversão e leitura do resultado. A interface do VL Optimizer não foi alterada e nenhum recurso do aplicativo novo foi construído.

A fonte é o backend decodificado com SHA-256 `52ca9c4279b739ec674637dae36a361b62eaf06b94aaa6db2a9caba6d4baaef0`. Todas as linhas abaixo apontam para `artifacts/private/gdz-analysis/deep/backend-focused.js`. A recuperação usa AST e um interpretador restrito de expressões puras; não carrega o programa, não dispara IPC, não executa os comandos produzidos e não consulta arquivos de sessão.

**O que é novo:** 36 ações base + 33 tweaks registrados no mesmo dicionário = **69 nomes aplicáveis**, além de **2 padrões dinâmicos**. Destes 36, **13 não aparecem em PAGES**; vários são aliases/combinações antigas. Existem ainda **8 nomes apenas de reversão legada**: não devem ser anunciados como capacidades atuais de aplicar. As contagens se sobrepõem aos 177 itens de interface já catalogados e não podem ser somadas para anunciar um total de recursos.

Os JSONs privados `operations-declarative.json` e `operation-specs.json` preservam os comandos completos e campos individuais. Expansões dinâmicas usam **capacidades sintéticas**, incluindo GPU/NIC e caminhos `C:\Example`; nenhum desses exemplos foi descoberto neste computador.

## 1. Nomes aceitos pelo executor de ações

| Nome exato | Mecanismo recuperado | Itens no snapshot de exemplo | Detector | Em PAGES | Fonte |
|---|---|---:|---|---|---|
| `Otimização Rápida` | Combina plano HIGH (omitido em x3dDual), Modo Jogo, DVR desligado, aceleração do mouse desligada e temporários com mais de 1 dia. O backup não recupera arquivos apagados. | 8 | sim | não | `deep/backend-focused.js:1862` |
| `Plano de Alta Performance` | Seleciona HIGH; se falhar, tenta duplicar o mesmo GUID e selecionar novamente. Só avisa erro se a segunda seleção falhar. | 1 | sim | sim | `deep/backend-focused.js:1863` |
| `Modo Máximo Desempenho` | Mesmo procedimento com ULTIMATE. Não cria ajustes específicos para cada jogo. | 1 | sim | sim | `deep/backend-focused.js:1864` |
| `SysMain Desativado` | Para SysMain e troca o tipo de inicialização para Disabled. | 1 | sim | sim | `deep/backend-focused.js:1865` |
| `Desativar Telemetria` | Desativa/para DiagTrack e define AllowTelemetry=0; este comando não altera a telemetria própria do GDZ. | 2 | sim | não | `deep/backend-focused.js:1866` |
| `Game Bar Desativado` | Desliga captura/DVR e UseNexusForGameBarEnabled, e cria política AllowGameDVR=0. | 4 | sim | sim | `deep/backend-focused.js:1867` |
| `Game Bar Removido` | Aplica a desativação, encerra processos da Game Bar, remove XboxGamingOverlay do usuário e cria protocolos substitutos apontando para systray.exe se o pacote sumiu. Pode abrir a Store na reversão. | 4 | sim | sim | `deep/backend-focused.js:1868` |
| `Serviços Xbox Desativados` | Para e desativa XblAuthManager, XblGameSave e XboxNetApiSvc; não inclui todo serviço que contenha Xbox. | 3 | sim | sim | `deep/backend-focused.js:1869` |
| `HAGS Ativo` | Define GraphicsDrivers\HwSchMode DWord=2. A reversão legada grava 1. | 1 | sim | sim | `deep/backend-focused.js:1870` |
| `Desativar Notificações` | Define ToastEnabled DWord=0 no usuário atual. | 1 | sim | sim | `deep/backend-focused.js:1871` |
| `Efeitos Visuais Off` | Define VisualFXSetting=2 e máscara binária 90 12 03 80 10 00 00 00. A volta legada usa máscara fixa 9E 1E 07 80 12 00 00 00. | 2 | sim | sim | `deep/backend-focused.js:1872` |
| `Serviços Desativados` | Para/desativa Spooler, Fax, RemoteRegistry, MapsBroker e WbioSrvc. A volta legada seleciona Manual, não o estado anterior. | 5 | sim | sim | `deep/backend-focused.js:1873` |
| `Indexação Off` | Para e desativa WSearch. | 1 | sim | sim | `deep/backend-focused.js:1874` |
| `UWP Removidos` | Remove os 10 pacotes da lista UWP_APPS do usuário atual. Em x3dDual preserva Xbox.TCUI/XboxGamingOverlay. Não existe snapshot dos pacotes nem comando reverso correspondente. | nenhum spec | não | sim | `deep/backend-focused.js:1875` |
| `Temporários Removidos` | Apaga filhos de TEMP e C:\Windows\Temp com LastWriteTime anterior a 1 dia; pode remover uma pasta inteira cujo filho seja recente. | nenhum spec | não | não | `deep/backend-focused.js:1879` |
| `Temporários Limpos` | Alias com script idêntico a Temporários Removidos. | nenhum spec | não | não | `deep/backend-focused.js:1880` |
| `DNS Otimizado` | Ação antiga: DNS IPv4 1.1.1.1 e 8.8.8.8 em todos os adaptadores Up. Distingue-se do seletor atual com quatro provedores. | 1 | sim | não | `deep/backend-focused.js:1881` |
| `Apps de Sincronização Fechados` | Encerra OneDrive, Dropbox, GoogleDriveFS e BackgroundTransferHost; não salva nem reabre o trabalho desses processos. | nenhum spec | não | sim | `deep/backend-focused.js:1882` |
| `Desativar Mouse Aceleração` | Grava MouseSpeed/Threshold1/Threshold2 como strings 0 e aplica SystemParametersInfo SPI_SETMOUSE (4). | 3 | sim | não | `deep/backend-focused.js:1883` |
| `Limpar Inicialização` | Desabilita somente Discord, Spotify, OneDrive, Steam e EpicGamesLauncher existentes em HKCU Run, por bytes StartupApproved iniciados por 3. | 5 | sim | não | `deep/backend-focused.js:1884` |
| `Reduzir Input Lag` | Combinação antiga de aceleração do mouse desligada e flags de atalhos de acessibilidade desligadas. | 6 | sim | não | `deep/backend-focused.js:1885` |
| `Modo Jogo` | Define AllowAutoGameMode e AutoGameModeEnabled como DWord=1. | 2 | sim | não | `deep/backend-focused.js:1886` |
| `Chrome/Discord Limitados` | Seleciona BelowNormal para os processos chrome e Discord que já estão abertos; não aplica limite de RAM, CPU ou rede. | nenhum spec | não | sim | `deep/backend-focused.js:1887` |
| `Explorer Mínimo` | Encerra explorer, espera 600 ms e o reabre. Não mantém o shell parado nem configura um modo persistente. | nenhum spec | não | sim | `deep/backend-focused.js:1888` |
| `DVR Desativado` | Define AppCaptureEnabled e GameDVR_Enabled como DWord=0. | 2 | sim | sim | `deep/backend-focused.js:1889` |
| `720p Boost Ativo` | Salva somente largura/altura primárias em HKCU\Software\GDZ e chama ChangeDisplaySettings para 1280×720, dmFields=0x180000. Não registra frequência/rotação/monitor; ignora retorno Win32. | nenhum spec | não | sim | `deep/backend-focused.js:1890` |
| `Todos Tweaks Aplicados` | Nome histórico: aplica apenas aceleração do mouse, acessibilidade e Modo Jogo; adiciona otimização de janela se build≥22000. Não aplica os 33 TWEAKS. | 9 | sim | não | `deep/backend-focused.js:1891` |
| `Jogos em Janela Otimizados` | Preserva outras opções DirectXUserGlobalSettings, remove tokens SwapEffectUpgradeEnable e acrescenta SwapEffectUpgradeEnable=1;. | 1 | sim | sim | `deep/backend-focused.js:1892` |
| `Sem Upload P2P` | Define DODownloadMode DWord=0 na política DeliveryOptimization. | 1 | sim | não | `deep/backend-focused.js:1893` |
| `Sem Reinício Durante o Jogo` | Define NoAutoRebootWithLoggedOnUsers DWord=1; não observa se um jogo está aberto. | 1 | sim | sim | `deep/backend-focused.js:1894` |
| `Correção de Flicker (MPO)` | Ação antiga define somente Dwm\OverlayTestMode=5. O tweak mpo atual também define GraphicsDrivers\DisableOverlays=1. | 1 | sim | não | `deep/backend-focused.js:1895` |
| `Sem Atalhos de Acessibilidade` | Flags String: StickyKeys=506, ToggleKeys=58, Keyboard Response=122. | 3 | sim | não | `deep/backend-focused.js:1896` |
| `Pagefile Automático` | Set-CimInstance Win32_ComputerSystem AutomaticManagedPagefile=true; avisa e sai se já estiver ativo. O spec registra PagingFiles, não o valor CIM diretamente. | 1 | sim | sim | `deep/backend-focused.js:1897` |
| `Modo X3D Correto` | Seleciona Balanced, liga Modo Jogo/Nexus e remove política de bloqueio DVR. Se faltar Game Bar abre Store; se faltar amd3dvcacheSvc avisa instalar chipset. Não instala esses componentes. | 5 | sim | sim | `deep/backend-focused.js:1898` |
| `OBS Prioritário` | Acrescenta RUNASADMIN ao AppCompatFlags Layers do OBS e desativa powerthrottling para seu executável. Não muda PriorityClass do OBS. Snapshot só Layers; extra de volta reseta powerthrottling ao padrão. | 1 | sim | sim | `deep/backend-focused.js:1899` |
| `Padrões Restaurados` | Restaura valores escolhidos pelo autor para ajustes antigos de GPU/prioridade/HID/TCP/BCD/mouse/fontes. Não equivale a restaurar uma imagem do Windows nem todos os ajustes do GDZ. | 19 | não | sim | `deep/backend-focused.js:1900` |

As 13 ausentes de PAGES são alcançáveis estaticamente pelo `run-action`, sujeito à autenticação e regras do executor. Isso não prova que exista um botão ativo para cada uma. “Otimização Rápida” é usada pelo dashboard, apesar de não ser uma entrada PAGES.

**Bloqueios do executor:** em `x3dDual`, impede HIGH, ULTIMATE e desativar/remover Game Bar; fora de `x3dDual`, impede Modo X3D Correto. Otimização em janela exige build conhecido ≥22000, e OBS exige executável encontrado. Fonte: `backend-focused.js:2077`. A detecção X3D usa modelos específicos, incluindo 7945HX3D, apesar de parte do texto exibido mencionar só modelos desktop.

## 2. Os 33 tweaks e todos os seus valores

Cada linha de registro tem `[caminho, nome, tipo, valor ligado, padrão desligado]`. `remover valor` corresponde a `null`: desativar sem snapshot remove a propriedade. **Com snapshot, o fluxo tenta restaurar o valor anterior primeiro; depois pode aplicar o padrão desligado caso o probe ainda indique ligado.** Portanto o padrão da tabela não substitui o estado salvo e desligar não é sempre uma restauração exata.

A amostra sintética expande **59 linhas de registro**, **1 linha de serviço** e **2 ajustes DRS**. O número de linhas reais varia com as NICs/GPU encontradas. A raiz GPU `...PCI\EXAMPLE\0...` e NIC `0001` são marcadores explicativos.

### `game-mode` — Modo de Jogo do Windows

Requisição: `tweak-set` com `id=game-mode` e `on=true/false`. Fonte: `deep/backend-focused.js:3264`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\SOFTWARE\Microsoft\GameBar` | `AllowAutoGameMode` | `DWord` | 1 | 0 |
| `HKCU:\SOFTWARE\Microsoft\GameBar` | `AutoGameModeEnabled` | `DWord` | 1 | 0 |

Probe exato da amostra:

```powershell
[bool](((GdzReg 'HKCU:\SOFTWARE\Microsoft\GameBar' 'AutoGameModeEnabled') -ne '0'))
```

### `delivery` — Sem upload de atualizações para outros PCs

Requisição: `tweak-set` com `id=delivery` e `on=true/false`. Fonte: `deep/backend-focused.js:3273`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization` | `DODownloadMode` | `DWord` | 0 | remover valor |

Probe exato da amostra:

```powershell
(((GdzReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization' 'DODownloadMode') -eq '0'))
```

### `edge-bg` — Edge fechado de verdade

Requisição: `tweak-set` com `id=edge-bg` e `on=true/false`. Fonte: `deep/backend-focused.js:3281`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKLM:\SOFTWARE\Policies\Microsoft\Edge` | `StartupBoostEnabled` | `DWord` | 0 | remover valor |
| `HKLM:\SOFTWARE\Policies\Microsoft\Edge` | `BackgroundModeEnabled` | `DWord` | 0 | remover valor |

Probe exato da amostra:

```powershell
(((GdzReg 'HKLM:\SOFTWARE\Policies\Microsoft\Edge' 'StartupBoostEnabled') -eq '0') -and ((GdzReg 'HKLM:\SOFTWARE\Policies\Microsoft\Edge' 'BackgroundModeEnabled') -eq '0'))
```

### `bg-apps` — Sem apps da Loja rodando escondidos

Requisição: `tweak-set` com `id=bg-apps` e `on=true/false`. Fonte: `deep/backend-focused.js:3290`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications` | `GlobalUserDisabled` | `DWord` | 1 | 0 |
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\Search` | `BackgroundAppGlobalToggle` | `DWord` | 0 | 1 |

Probe exato da amostra:

```powershell
(((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications' 'GlobalUserDisabled') -eq '1') -and ((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search' 'BackgroundAppGlobalToggle') -eq '0'))
```

### `hibernate` — Desligar de verdade (sem hibernação)

Requisição: `tweak-set` com `id=hibernate` e `on=true/false`. Fonte: `deep/backend-focused.js:3298`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKLM:\SYSTEM\CurrentControlSet\Control\Power` | `HibernateEnabled` | `DWord` | 0 | 1 |

Campos dependentes de capacidade: `rec`. As funções originais e exemplo expandido constam no JSON.

Probe exato da amostra:

```powershell
(((GdzReg 'HKLM:\SYSTEM\CurrentControlSet\Control\Power' 'HibernateEnabled') -eq '0'))
```

### `mpo` — Correção de tela piscando (MPO)

Requisição: `tweak-set` com `id=mpo` e `on=true/false`. Fonte: `deep/backend-focused.js:3309`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKLM:\SOFTWARE\Microsoft\Windows\Dwm` | `OverlayTestMode` | `DWord` | 5 | remover valor |
| `HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers` | `DisableOverlays` | `DWord` | 1 | remover valor |

Aplicação adicional declarada: `reboot`.

Probe exato da amostra:

```powershell
(((GdzReg 'HKLM:\SOFTWARE\Microsoft\Windows\Dwm' 'OverlayTestMode') -eq '5') -and ((GdzReg 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' 'DisableOverlays') -eq '1'))
```

### `gpu-msi` — Placa de vídeo com interrupção MSI e prioridade alta

Requisição: `tweak-set` com `id=gpu-msi` e `on=true/false`. Fonte: `deep/backend-focused.js:3318`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKLM:\SYSTEM\CurrentControlSet\Enum\PCI\EXAMPLE\0\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties` | `MSISupported` | `DWord` | 1 | remover valor |
| `HKLM:\SYSTEM\CurrentControlSet\Enum\PCI\EXAMPLE\0\Device Parameters\Interrupt Management\Affinity Policy` | `DevicePriority` | `DWord` | 3 | remover valor |

Campos dependentes de capacidade: `reg`, `block`. As funções originais e exemplo expandido constam no JSON.

Aplicação adicional declarada: `reboot`.

O caminho é da primeira GPU dedicada NVIDIA/AMD selecionada em buildCaps; não de todas as GPUs. Deve ser resolvido e validado por PNPDeviceID antes de usar o modelo de chave acima.

Probe exato da amostra:

```powershell
(((GdzReg 'HKLM:\SYSTEM\CurrentControlSet\Enum\PCI\EXAMPLE\0\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties' 'MSISupported') -eq '1') -and ((GdzReg 'HKLM:\SYSTEM\CurrentControlSet\Enum\PCI\EXAMPLE\0\Device Parameters\Interrupt Management\Affinity Policy' 'DevicePriority') -eq '3'))
```

### `dlss-rec` — DLSS no modelo recomendado pela NVIDIA

Requisição: `tweak-set` com `id=dlss-rec` e `on=true/false`. Fonte: `deep/backend-focused.js:3328`.

NVIDIA DRS: ID `283385345` (`0x10E41E01`) → `1` (`0x00000001`); ID `283385331` (`0x10E41DF3`) → `16777215` (`0x00FFFFFF`). Sem snapshot, desligar remove os overrides; com snapshot restaura os overrides anteriores.

Campos dependentes de capacidade: `note`, `drs`, `block`. As funções originais e exemplo expandido constam no JSON.

Probe exato da amostra:

```powershell
(((GdzDrsGet 283385345) -eq '1') -and ((GdzDrsGet 283385331) -eq '16777215'))
```

### `steam-overlay` — Sobreposição do Steam (Shift+Tab) desligada

Requisição: `tweak-set` com `id=steam-overlay` e `on=true/false`. Fonte: `deep/backend-focused.js:3337`.

Campos dependentes de capacidade: `probe`, `on`, `off`, `block`. As funções originais e exemplo expandido constam no JSON.

Edita EnableGameOverlay no bloco system de cada `Steam\userdata\*\config\localconfig.vdf`. Exige conta existente e nenhum RunningAppID>0. Solicita Steam `-shutdown`, espera no máximo 40×500 ms, grava UTF-8 sem BOM e o reabre somente se estava aberto. Não há snapshot dos VDF; desligar escreve 1.

Probe exato da amostra:

```powershell
[bool]($(
    $gdzVdf = @(Get-ChildItem -LiteralPath 'C:\Example\Steam\userdata' -Directory -ErrorAction SilentlyContinue |
      ForEach-Object { Join-Path $_.FullName 'config\localconfig.vdf' } | Where-Object { Test-Path -LiteralPath $_ })
    ($gdzVdf.Count -gt 0) -and (@($gdzVdf | Where-Object { -not (Select-String -LiteralPath $_ -Pattern '"EnableGameOverlay"\s+"0"' -Quiet) }).Count -eq 0)
  ))
```

### `lso` — LSO da placa de rede desligado

Requisição: `tweak-set` com `id=lso` e `on=true/false`. Fonte: `deep/backend-focused.js:3348`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\0001` | `*LsoV2IPv4` | `String` | "0" | "1" |
| `HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\0001` | `*LsoV2IPv6` | `String` | "0" | "1" |

Campos dependentes de capacidade: `reg`, `on`, `off`, `block`. As funções originais e exemplo expandido constam no JSON.

Aplicação adicional declarada: `net`.

Reinicia adaptadores correspondentes ao NetCfgInstanceId depois da escrita; ignora adaptadores Disabled. `eee` e `nic-intmod` voltam para `Ndi\Params\<keyword>\default` quando disponível, senão string 1. No LSO, o padrão desligado fixo é string 1; IPv6 só aparece se suportado.

Probe exato da amostra:

```powershell
(((GdzReg 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\0001' '*LsoV2IPv4') -eq '0') -and ((GdzReg 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\0001' '*LsoV2IPv6') -eq '0'))
```

### `eee` — Economia de energia da placa de rede desligada (EEE)

Requisição: `tweak-set` com `id=eee` e `on=true/false`. Fonte: `deep/backend-focused.js:3360`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\0001` | `*EEE` | `String` | "0" | "1" |

Campos dependentes de capacidade: `reg`, `on`, `off`, `block`. As funções originais e exemplo expandido constam no JSON.

Aplicação adicional declarada: `net`.

Reinicia adaptadores correspondentes ao NetCfgInstanceId depois da escrita; ignora adaptadores Disabled. `eee` e `nic-intmod` voltam para `Ndi\Params\<keyword>\default` quando disponível, senão string 1. No LSO, o padrão desligado fixo é string 1; IPv6 só aparece se suportado.

Probe exato da amostra:

```powershell
(((GdzReg 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\0001' '*EEE') -eq '0'))
```

### `nic-intmod` — Placa de rede sem moderação de interrupção

Requisição: `tweak-set` com `id=nic-intmod` e `on=true/false`. Fonte: `deep/backend-focused.js:3372`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\0001` | `*InterruptModeration` | `String` | "0" | "1" |

Campos dependentes de capacidade: `reg`, `on`, `off`, `block`. As funções originais e exemplo expandido constam no JSON.

Aplicação adicional declarada: `net`.

Reinicia adaptadores correspondentes ao NetCfgInstanceId depois da escrita; ignora adaptadores Disabled. `eee` e `nic-intmod` voltam para `Ndi\Params\<keyword>\default` quando disponível, senão string 1. No LSO, o padrão desligado fixo é string 1; IPv6 só aparece se suportado.

Probe exato da amostra:

```powershell
(((GdzReg 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\0001' '*InterruptModeration') -eq '0'))
```

### `mouse-accel` — Mira sem aceleração do mouse

Requisição: `tweak-set` com `id=mouse-accel` e `on=true/false`. Fonte: `deep/backend-focused.js:3384`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Control Panel\Mouse` | `MouseSpeed` | `String` | "0" | "1" |
| `HKCU:\Control Panel\Mouse` | `MouseThreshold1` | `String` | "0" | "6" |
| `HKCU:\Control Panel\Mouse` | `MouseThreshold2` | `String` | "0" | "10" |

Probe exato da amostra:

```powershell
(((GdzReg 'HKCU:\Control Panel\Mouse' 'MouseSpeed') -eq '0') -and ((GdzReg 'HKCU:\Control Panel\Mouse' 'MouseThreshold1') -eq '0') -and ((GdzReg 'HKCU:\Control Panel\Mouse' 'MouseThreshold2') -eq '0'))
```

### `sticky` — Sem janelas de acessibilidade no meio do jogo

Requisição: `tweak-set` com `id=sticky` e `on=true/false`. Fonte: `deep/backend-focused.js:3394`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Control Panel\Accessibility\StickyKeys` | `Flags` | `String` | "506" | "510" |
| `HKCU:\Control Panel\Accessibility\ToggleKeys` | `Flags` | `String` | "58" | "62" |
| `HKCU:\Control Panel\Accessibility\Keyboard Response` | `Flags` | `String` | "122" | "126" |

Probe exato da amostra:

```powershell
[bool]((@('StickyKeys','ToggleKeys','Keyboard Response') | Where-Object { ([int](GdzReg "HKCU:\Control Panel\Accessibility\$_" 'Flags')) -band 4 }).Count -eq 0)
```

### `numlock` — Num Lock ligado ao iniciar

Requisição: `tweak-set` com `id=numlock` e `on=true/false`. Fonte: `deep/backend-focused.js:3403`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Control Panel\Keyboard` | `InitialKeyboardIndicators` | `String` | "2" | "0" |
| `Registry::HKEY_USERS\.DEFAULT\Control Panel\Keyboard` | `InitialKeyboardIndicators` | `String` | "2" | "2147483648" |

Aplicação adicional declarada: `reboot`.

Probe exato da amostra:

```powershell
(((GdzReg 'HKCU:\Control Panel\Keyboard' 'InitialKeyboardIndicators') -eq '2') -and ((GdzReg 'Registry::HKEY_USERS\.DEFAULT\Control Panel\Keyboard' 'InitialKeyboardIndicators') -eq '2'))
```

### `kbd-repeat` — Teclado repetindo na velocidade máxima

Requisição: `tweak-set` com `id=kbd-repeat` e `on=true/false`. Fonte: `deep/backend-focused.js:3411`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Control Panel\Keyboard` | `KeyboardDelay` | `String` | "0" | "1" |
| `HKCU:\Control Panel\Keyboard` | `KeyboardSpeed` | `String` | "31" | "31" |

Probe exato da amostra:

```powershell
(((GdzReg 'HKCU:\Control Panel\Keyboard' 'KeyboardDelay') -eq '0') -and ((GdzReg 'HKCU:\Control Panel\Keyboard' 'KeyboardSpeed') -eq '31'))
```

### `telemetry` — Telemetria e anúncios personalizados

Requisição: `tweak-set` com `id=telemetry` e `on=true/false`. Fonte: `deep/backend-focused.js:3422`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection` | `AllowTelemetry` | `DWord` | 0 | remover valor |
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo` | `Enabled` | `DWord` | 0 | 1 |
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy` | `TailoredExperiencesWithDiagnosticDataEnabled` | `DWord` | 0 | 1 |
| `HKCU:\Software\Microsoft\Siuf\Rules` | `NumberOfSIUFInPeriod` | `DWord` | 0 | remover valor |

Serviço: `DiagTrack`: ligado `Disabled`, padrão desligado `Automatic`.

Probe exato da amostra:

```powershell
(((GdzReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection' 'AllowTelemetry') -eq '0') -and ((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo' 'Enabled') -eq '0') -and ((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy' 'TailoredExperiencesWithDiagnosticDataEnabled') -eq '0') -and ((GdzReg 'HKCU:\Software\Microsoft\Siuf\Rules' 'NumberOfSIUFInPeriod') -eq '0') -and ((GdzSvc 'DiagTrack') -eq 'Disabled'))
```

### `suggestions` — Sem sugestões, dicas e apps instalados sozinhos

Requisição: `tweak-set` com `id=suggestions` e `on=true/false`. Fonte: `deep/backend-focused.js:3433`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` | `SilentInstalledAppsEnabled` | `DWord` | 0 | 1 |
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` | `SystemPaneSuggestionsEnabled` | `DWord` | 0 | 1 |
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` | `SoftLandingEnabled` | `DWord` | 0 | 1 |
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` | `SubscribedContent-338388Enabled` | `DWord` | 0 | 1 |
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` | `SubscribedContent-338389Enabled` | `DWord` | 0 | 1 |
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` | `SubscribedContent-353694Enabled` | `DWord` | 0 | 1 |
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` | `SubscribedContent-353696Enabled` | `DWord` | 0 | 1 |
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` | `SubscribedContent-310093Enabled` | `DWord` | 0 | 1 |
| `HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent` | `DisableWindowsConsumerFeatures` | `DWord` | 1 | remover valor |

Probe exato da amostra:

```powershell
(((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SilentInstalledAppsEnabled') -eq '0') -and ((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SystemPaneSuggestionsEnabled') -eq '0') -and ((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SoftLandingEnabled') -eq '0') -and ((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-338388Enabled') -eq '0') -and ((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-338389Enabled') -eq '0') -and ((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-353694Enabled') -eq '0') -and ((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-353696Enabled') -eq '0') -and ((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-310093Enabled') -eq '0') -and ((GdzReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableWindowsConsumerFeatures') -eq '1'))
```

### `bing` — Pesquisa do Iniciar sem Bing

Requisição: `tweak-set` com `id=bing` e `on=true/false`. Fonte: `deep/backend-focused.js:3441`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\Search` | `BingSearchEnabled` | `DWord` | 0 | remover valor |
| `HKCU:\Software\Policies\Microsoft\Windows\Explorer` | `DisableSearchBoxSuggestions` | `DWord` | 1 | remover valor |

Aplicação adicional declarada: `explorer`.

Probe exato da amostra:

```powershell
(((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search' 'BingSearchEnabled') -eq '0') -and ((GdzReg 'HKCU:\Software\Policies\Microsoft\Windows\Explorer' 'DisableSearchBoxSuggestions') -eq '1'))
```

### `activity` — Sem histórico de atividades

Requisição: `tweak-set` com `id=activity` e `on=true/false`. Fonte: `deep/backend-focused.js:3452`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKLM:\SOFTWARE\Policies\Microsoft\Windows\System` | `EnableActivityFeed` | `DWord` | 0 | remover valor |
| `HKLM:\SOFTWARE\Policies\Microsoft\Windows\System` | `PublishUserActivities` | `DWord` | 0 | remover valor |
| `HKLM:\SOFTWARE\Policies\Microsoft\Windows\System` | `UploadUserActivities` | `DWord` | 0 | remover valor |

Probe exato da amostra:

```powershell
(((GdzReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'EnableActivityFeed') -eq '0') -and ((GdzReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'PublishUserActivities') -eq '0') -and ((GdzReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'UploadUserActivities') -eq '0'))
```

### `widgets` — Sem Widgets (notícias e clima)

Requisição: `tweak-set` com `id=widgets` e `on=true/false`. Fonte: `deep/backend-focused.js:3460`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKLM:\SOFTWARE\Policies\Microsoft\Dsh` | `AllowNewsAndInterests` | `DWord` | 0 | remover valor |
| `HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds` | `EnableFeeds` | `DWord` | 0 | remover valor |

Aplicação adicional declarada: `reboot`.

Probe exato da amostra:

```powershell
(((GdzReg 'HKLM:\SOFTWARE\Policies\Microsoft\Dsh' 'AllowNewsAndInterests') -eq '0') -and ((GdzReg 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds' 'EnableFeeds') -eq '0'))
```

### `location` — Localização desligada

Requisição: `tweak-set` com `id=location` e `on=true/false`. Fonte: `deep/backend-focused.js:3470`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location` | `Value` | `String` | "Deny" | "Allow" |

Probe exato da amostra:

```powershell
(((GdzReg 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location' 'Value') -eq 'Deny'))
```

### `end-task` — Finalizar tarefa pela barra de tarefas

Requisição: `tweak-set` com `id=end-task` e `on=true/false`. Fonte: `deep/backend-focused.js:3478`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings` | `TaskbarEndTask` | `DWord` | 1 | remover valor |

Campos dependentes de capacidade: `block`. As funções originais e exemplo expandido constam no JSON.

Bloqueado se build conhecido <22631 (Windows 11 23H2); não basta o requisito Windows 11 genérico.

Probe exato da amostra:

```powershell
(((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings' 'TaskbarEndTask') -eq '1'))
```

### `file-ext` — Mostrar a extensão dos arquivos

Requisição: `tweak-set` com `id=file-ext` e `on=true/false`. Fonte: `deep/backend-focused.js:3487`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` | `HideFileExt` | `DWord` | 0 | 1 |

Probe exato da amostra:

```powershell
(((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'HideFileExt') -eq '0'))
```

### `hidden-files` — Mostrar arquivos ocultos

Requisição: `tweak-set` com `id=hidden-files` e `on=true/false`. Fonte: `deep/backend-focused.js:3495`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` | `Hidden` | `DWord` | 1 | 2 |

Probe exato da amostra:

```powershell
(((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'Hidden') -eq '1'))
```

### `this-pc` — Explorador abre em "Este Computador"

Requisição: `tweak-set` com `id=this-pc` e `on=true/false`. Fonte: `deep/backend-focused.js:3502`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` | `LaunchTo` | `DWord` | 1 | remover valor |

Probe exato da amostra:

```powershell
(((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'LaunchTo') -eq '1'))
```

### `classic-menu` — Menu do botão direito completo

Requisição: `tweak-set` com `id=classic-menu` e `on=true/false`. Fonte: `deep/backend-focused.js:3509`.

Campos dependentes de capacidade: `block`. As funções originais e exemplo expandido constam no JSON.

Aplicação adicional declarada: `explorer`.

Cria a subchave `InprocServer32` de `HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` e seu valor padrão vazio. Desligar remove recursivamente a raiz desse CLSID. Não há snapshot dessa chave na declaração; reinicia Explorer.

Probe exato da amostra:

```powershell
[bool]((Test-Path -LiteralPath 'HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32'))
```

### `dark` — Tema escuro

Requisição: `tweak-set` com `id=dark` e `on=true/false`. Fonte: `deep/backend-focused.js:3520`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` | `AppsUseLightTheme` | `DWord` | 0 | 1 |
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` | `SystemUsesLightTheme` | `DWord` | 0 | 1 |

Probe exato da amostra:

```powershell
(((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'AppsUseLightTheme') -eq '0') -and ((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'SystemUsesLightTheme') -eq '0'))
```

### `transparency` — Sem transparência nas janelas

Requisição: `tweak-set` com `id=transparency` e `on=true/false`. Fonte: `deep/backend-focused.js:3527`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` | `EnableTransparency` | `DWord` | 0 | 1 |

Probe exato da amostra:

```powershell
(((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'EnableTransparency') -eq '0'))
```

### `taskbar-left` — Barra de tarefas à esquerda

Requisição: `tweak-set` com `id=taskbar-left` e `on=true/false`. Fonte: `deep/backend-focused.js:3537`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` | `TaskbarAl` | `DWord` | 0 | remover valor |

Campos dependentes de capacidade: `block`. As funções originais e exemplo expandido constam no JSON.

Probe exato da amostra:

```powershell
(((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'TaskbarAl') -eq '0'))
```

### `task-view` — Esconder o botão Visão de Tarefas

Requisição: `tweak-set` com `id=task-view` e `on=true/false`. Fonte: `deep/backend-focused.js:3545`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` | `ShowTaskViewButton` | `DWord` | 0 | 1 |

Probe exato da amostra:

```powershell
(((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'ShowTaskViewButton') -eq '0'))
```

### `search-icon` — Pesquisa da barra só como ícone

Requisição: `tweak-set` com `id=search-icon` e `on=true/false`. Fonte: `deep/backend-focused.js:3552`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKCU:\Software\Microsoft\Windows\CurrentVersion\Search` | `SearchboxTaskbarMode` | `DWord` | 1 | 2 |

Probe exato da amostra:

```powershell
(((GdzReg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search' 'SearchboxTaskbarMode') -eq '1'))
```

### `bsod-detail` — Tela azul com detalhes do erro

Requisição: `tweak-set` com `id=bsod-detail` e `on=true/false`. Fonte: `deep/backend-focused.js:3559`.

| Caminho | Nome | Tipo | Ligado | Padrão desligado |
|---|---|---|---|---|
| `HKLM:\SYSTEM\CurrentControlSet\Control\CrashControl` | `DisplayParameters` | `DWord` | 1 | 0 |
| `HKLM:\SYSTEM\CurrentControlSet\Control\CrashControl` | `DisableEmoticon` | `DWord` | 1 | 0 |

Probe exato da amostra:

```powershell
(((GdzReg 'HKLM:\SYSTEM\CurrentControlSet\Control\CrashControl' 'DisplayParameters') -eq '1') -and ((GdzReg 'HKLM:\SYSTEM\CurrentControlSet\Control\CrashControl' 'DisableEmoticon') -eq '1'))
```

### Pré-condições e matriz de capacidades

Foram expandidos **198 casos estáticos** (33 tweaks × 6 conjuntos sintéticos de capacidades). Isso testa a interpretação das declarações; não é um ensaio dos comandos nem da compatibilidade no Windows.

| Tweaks com bloqueio | Regra exata |
|---|---|
| gpu-msi | Requer caps.gpuIrq não vazio. |
| dlss-rec | Requer caps.rtx e caps.nvapi. |
| steam-overlay | Requer caps.steamPath não vazio. |
| lso | Requer caps.nicLso.length > 0. |
| eee | Requer pelo menos uma nicOpt cujo nome esteja em *EEE, EEE, AdvancedEEE, EEELinkAdvertisement, EnableGreenEthernet. |
| nic-intmod | Requer nicOpt com nome *InterruptModeration. |
| end-task | Bloqueia build conhecido <22631. |
| classic-menu / taskbar-left | Bloqueia build conhecido <22000. |

Build igual a 0/desconhecido passa pelas três regras de versão acima; isso é comportamento do código, não garantia de suporte. Os demais 24 tweaks não têm função block declarada, embora Windows/driver ainda possam recusar a alteração. Hibernate é recomendado apenas quando laptop=false. As demais recomendações constam dos campos rec do inventário.

| Perfil sintético | Tweaks bloqueados | Tweaks marcados recomendados |
|---|---|---:|
| `desktopRtxWin11` | nenhum | 14 |
| `notebookWin10NoSupportedExtras` | gpu-msi, dlss-rec, steam-overlay, lso, eee, nic-intmod, end-task, classic-menu, taskbar-left | 13 |
| `dualX3DOldRtxWin11` | nenhum | 14 |
| `win1122H2` | end-task | 14 |
| `win1123H2` | nenhum | 14 |
| `missingWindowsBuild` | nenhum | 14 |

## 3. Reversões sem ação atual de aplicação

Há 31 propriedades na declaração inicial REVERT. Uma atribuição posterior acrescenta Reduzir Input Lag e outra complementa Efeitos Visuais Off: **32 nomes finais**, dos quais os 8 abaixo não têm ACTIONS correspondente. A API `revert-action` pode recebê-los; isso não torna suas antigas ações de aplicar disponíveis.

| Nome legado | O que a volta tenta fazer | Fonte |
|---|---|---|
| `DPI Scaling Off` | Remover Win8DpiScaling do Desktop do usuário. | `deep/backend-focused.js:3062` |
| `Defender Off` | Reativar monitoramento e remover a política DisableAntiSpyware. | `deep/backend-focused.js:3047` |
| `Defender Pausado` | Reativar monitoramento em tempo real via Set-MpPreference. | `deep/backend-focused.js:3046` |
| `Desativar Atualizações Windows` | Selecionar wuauserv Manual e iniciar o serviço. | `deep/backend-focused.js:3048` |
| `Fonte Baixa Qualidade` | FontSmoothing string 2 e FontSmoothingType DWord 2. | `deep/backend-focused.js:3063` |
| `Pagefile Ajustado` | Mesma redefinição da paginação automática. | `deep/backend-focused.js:3055` |
| `Paginação Desativada` | Win32_ComputerSystem.AutomaticManagedPagefile=true via WMI. | `deep/backend-focused.js:3054` |
| `System Restore Off` | Enable-ComputerRestore no volume do sistema. | `deep/backend-focused.js:3053` |

## 4. Padrões dinâmicos e jogos

`run-action` aceita **qualquer texto** que corresponda a `^Otimizar .{1,60}$` quando não existe ação nominal: o sufixo não precisa ser um jogo instalado. O efeito é sempre HIGH (exceto X3D dual), Modo Jogo e DVR desligado, com snapshot desses estados. O programa não gera um perfil técnico diferente para CS2, Valorant ou outro nome nesse caminho.

`GPU Dedicada: <id>` exige id reconhecido, GPU híbrida e executáveis encontrados. Grava `GpuPreference=2;` em `HKCU:\Software\Microsoft\DirectX\UserGpuPreferences`, um valor por caminho de executável, com snapshot. A escrita substitui o conteúdo do valor; não mescla outros tokens existentes.

As 12 entradas conhecidas fornecem descoberta e identificação de processo. Além delas, o scanner forma uma biblioteca dinâmica de jogos Steam/Epic. Estas entradas não são 12 algoritmos exclusivos de otimização.

| ID | Nome | Descoberta declarada | Executáveis relativos | Processos | Fonte |
|---|---|---|---|---|---|
| `cs2` | Counter-Strike 2 | {"steam":[730]} | ["game\\bin\\win64\\cs2.exe"] | ["cs2.exe"] | `deep/backend-focused.js:2394` |
| `valorant` | Valorant | {"riot":"valorant.live"} | ["ShooterGame\\Binaries\\Win64\\VALORANT-Win64-Shipping.exe"] | ["VALORANT-Win64-Shipping.exe"] | `deep/backend-focused.js:2401` |
| `warzone` | Call of Duty: Warzone | {"steam":[1962663,1938090],"uninstall":"^Call of Duty"} | ["cod.exe"] | ["cod.exe"] | `deep/backend-focused.js:2408` |
| `fortnite` | Fortnite | {"epic":"Fortnite"} | ["FortniteGame\\Binaries\\Win64\\FortniteClient-Win64-Shipping.exe"] | ["FortniteClient-Win64-Shipping.exe"] | `deep/backend-focused.js:2416` |
| `minecraft` | Minecraft | {"marker":["%APPDATA%\\.minecraft"],"appx":["Microsoft.MinecraftUWP","Microsoft.4297127D64EC6"]} | [] | ["Minecraft.Windows.exe","javaw.exe"] | `deep/backend-focused.js:2423` |
| `fivem` | FiveM (GTA RP) | {"paths":["%LOCALAPPDATA%\\FiveM"]} | ["FiveM.exe","FiveM.app\\data\\cache\\subprocess\\*GTAProcess.exe"] | ["FiveM.exe","FiveM_*GTAProcess.exe"] | `deep/backend-focused.js:2431` |
| `apex` | Apex Legends | {"steam":[1172470],"uninstall":"^Apex Legends"} | ["r5apex.exe","r5apex_dx12.exe"] | ["r5apex.exe","r5apex_dx12.exe"] | `deep/backend-focused.js:2439` |
| `lol` | League of Legends | {"riot":"league_of_legends.live"} | ["Game\\League of Legends.exe"] | ["League of Legends.exe"] | `deep/backend-focused.js:2447` |
| `freefire` | Free Fire (BlueStacks) | {"uninstall":"^(BlueStacks\|MSI App Player)"} | ["HD-Player.exe"] | ["HD-Player.exe"] | `deep/backend-focused.js:2454` |
| `roblox` | Roblox | {"paths":["%LOCALAPPDATA%\\Roblox\\Versions","%ProgramFiles%\\Roblox\\Versions","%ProgramFiles(x86)%\\Roblox\\Versions","%LOCALAPPDATA%\\Bloxstrap\\Versions"]} | ["*\\RobloxPlayerBeta.exe"] | ["RobloxPlayerBeta.exe"] | `deep/backend-focused.js:2461` |
| `r6` | Rainbow Six Siege | {"steam":[359550],"uninstall":"^Tom Clancy.s Rainbow Six"} | ["RainbowSix.exe","RainbowSix_Vulkan.exe","RainbowSix_BE.exe"] | ["RainbowSix.exe","RainbowSix_Vulkan.exe","RainbowSix_BE.exe"] | `deep/backend-focused.js:2468` |
| `ow2` | Overwatch 2 | {"steam":[2357570],"uninstall":"^Overwatch"} | ["Overwatch.exe","_retail_\\Overwatch.exe"] | ["Overwatch.exe"] | `deep/backend-focused.js:2476` |

FiveM possui limpeza específica: `data\cache`, `data\server-cache`, `data\server-cache-priv`; preserva somente o item `data\cache\game` na lista de exclusão. O caminho de limpeza está separado da limpeza geral de logs/crashes. O código verifica processos antes de limpar cache de jogo. Fonte: `backend-focused.js:2828`.

## 5. As 16 categorias completas de limpeza

O scanner calcula alvos existentes e únicos, sem apagar. Na execução, a seleção é filtrada pela lista de IDs conhecida. Processos abertos são consultados novamente e aquela categoria é pulada. A idade de temporários vale para itens de primeiro nível; nas categorias normais vale por arquivo. Lixeira e DeliveryOptimization têm caminhos especiais. O relatório de algoritmos detalha a travessia e as diferenças entre medir e excluir.

| ID / nome | Seleção padrão | Idade em dias | Processo que bloqueia | Expressão exata de alvos | Fonte |
|---|---|---:|---|---|---|
| `temp` — Arquivos temporários | sim | 1 | nenhum | `@($env:TEMP, "$env:SystemRoot\Temp") \| Where-Object { $_ -match '\\(Temp\|Tmp)(\\\d+)?\\?$' }` | `deep/backend-focused.js:5031` |
| `recycle` — Lixeira | não | especial | nenhum | `Lixeira: $Recycle.Bin\<SID atual> em discos Fixed/IsReady` | `deep/backend-focused.js:5040` |
| `crash` — Relatórios de erro e despejos de memória | sim | 0 | nenhum | `"$env:ProgramData\Microsoft\Windows\WER\ReportArchive"; "$env:ProgramData\Microsoft\Windows\WER\ReportQueue"; "$env:ProgramData\Microsoft\Windows\WER\Temp"; "$L\Microsoft\Windows\WER"; "$L\CrashDumps"; "$env:SystemRoot\Minidump"; "$env:SystemRoot\MEMORY.DMP"; "$env:SystemRoot\LiveKernelReports"` | `deep/backend-focused.js:5047` |
| `logs` — Logs antigos do Windows | sim | 7 | nenhum | `Sub "$env:SystemRoot\Logs" @('CBS', 'DISM', 'MoSetup', 'WindowsUpdate', 'NetSetup', 'SIH', 'waasmedic', 'dosvc')` | `deep/backend-focused.js:5056` |
| `delivery` — Cache de entrega de atualizações | sim | especial | nenhum | `"$env:SystemRoot\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"` | `deep/backend-focused.js:5064` |
| `drivers` — Instaladores de driver já usados | sim | 0 | nenhum | `"$env:SystemDrive\AMD"; "$env:SystemDrive\NVIDIA"` | `deep/backend-focused.js:5072` |
| `chrome` — Google Chrome | sim | 0 | chrome | `Chromium "$L\Google\Chrome\User Data"` | `deep/backend-focused.js:5080` |
| `edge` — Microsoft Edge | sim | 0 | msedge | `Chromium "$L\Microsoft\Edge\User Data"` | `deep/backend-focused.js:5090` |
| `brave` — Brave | sim | 0 | brave | `Chromium "$L\BraveSoftware\Brave-Browser\User Data"` | `deep/backend-focused.js:5101` |
| `opera` — Opera | sim | 0 | opera | `Chromium "$R\Opera Software\Opera Stable"; Chromium "$L\Opera Software\Opera Stable"` | `deep/backend-focused.js:5111` |
| `operagx` — Opera GX | sim | 0 | opera | `Chromium "$R\Opera Software\Opera GX Stable"; Chromium "$L\Opera Software\Opera GX Stable"` | `deep/backend-focused.js:5121` |
| `firefox` — Mozilla Firefox | sim | 0 | firefox | `Get-ChildItem -LiteralPath "$L\Mozilla\Firefox\Profiles" -Directory -Force \| ForEach-Object { Sub $_.FullName @('cache2', 'startupCache', 'thumbnails', 'jumpListCache') }` | `deep/backend-focused.js:5131` |
| `discord` — Discord | sim | 0 | Discord, DiscordPTB, DiscordCanary | `foreach ($d in 'discord', 'discordptb', 'discordcanary') { Sub "$R\$d" $CR }` | `deep/backend-focused.js:5141` |
| `steam` — Steam | sim | 0 | steam, steamwebhelper | `$sp = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam').SteamPath; if ($sp) { Sub ($sp -replace '/', '\') @('appcache\httpcache', 'logs', 'dumps') }; "$L\Steam\htmlcache"` | `deep/backend-focused.js:5151` |
| `epic` — Epic Games Launcher | sim | 0 | EpicGamesLauncher | `Get-ChildItem -LiteralPath "$L\EpicGamesLauncher\Saved" -Directory -Filter 'webcache*' -Force \| ForEach-Object { $_.FullName }; "$L\EpicGamesLauncher\Saved\Logs"` | `deep/backend-focused.js:5161` |
| `fivem` — FiveM: logs e relatórios de crash | sim | 0 | FiveM* | `"$L\FiveM\FiveM.app\logs"; "$L\FiveM\FiveM.app\crashes"` | `deep/backend-focused.js:5171` |

Detalhes das expressões: `$L=LOCALAPPDATA`, `$R=APPDATA`. `Sub` combina a raiz com cada nome; `Chromium` inclui a raiz e cada subpasta com Preferences, usando Cache/Code Cache/GPUCache/DawnCache/DawnGraphiteCache/DawnWebGPUCache, além de ShaderCache/GrShaderCache/GraphiteDawnCache da raiz. Firefox inclui cache2/startupCache/thumbnails/jumpListCache. Não há rotina de reversão para esses arquivos.

A lista de processos é um retrato no início do script, não um bloqueio de arquivo nem monitoramento contínuo. Arquivos inacessíveis/abertos podem ficar. `freed` vem da rotina de exclusão; o resultado deve conservar também `files`, `failed` e `open`, não transformar sucesso do pedido em todos os arquivos removidos.

## 6. Provedores DNS e ferramentas pontuais

| Provedor | Servidores IPv4 |
|---|---|
| `cloudflare` | `1.1.1.1, 1.0.0.1` |
| `google` | `8.8.8.8, 8.8.4.4` |
| `quad9` | `9.9.9.9, 149.112.112.112` |
| `adguard` | `94.140.14.14, 94.140.15.15` |

`auto` é um quinto modo de seleção, não um quinto provedor: usa os servidores automáticos ou restaura o snapshot conforme fluxo. A ação antiga DNS Otimizado usa 1.1.1.1 + 8.8.8.8, portanto não deve ser confundida com Cloudflare do seletor.

| ID ferramenta | Timeout | Sequência principal | Verificação / limite | Fonte |
|---|---:|---|---|---|
| `restore-point` | 300 s | Enable-ComputerRestore + Checkpoint-Computer MODIFY_SETTINGS; frequência temporária 0. | Salva/restaura SystemRestorePointCreationFrequency em finally; pode deixar proteção habilitada. | `deep/backend-focused.js:3662` |
| `flush-dns` | 30 s | Clear-DnsClientCache + ipconfig /flushdns. | Não verifica código de saída dos dois comandos. | `deep/backend-focused.js:3679` |
| `net-reset` | 120 s | ipconfig /flushdns; netsh winsock reset; netsh int ip reset. | Anuncia necessidade de reiniciar; não restaura ajustes anteriores e não verifica cada exit code. | `deep/backend-focused.js:3683` |
| `update-cleanup` | 2400 s | DISM /Online /Cleanup-Image /StartComponentCleanup /NoRestart. | Exige exit code 0; mede delta de espaço livre; diferencia >0,05 GB. | `deep/backend-focused.js:3687` |
| `repair` | 3600 s | DISM /RestoreHealth /NoRestart; depois sfc /scannow. | Guarda/valida exit code DISM; não valida exit code SFC. | `deep/backend-focused.js:3691` |

## 7. FiveM: valores completos dos presets

Procura `CitizenFX\gta5_settings.xml` primeiro em APPDATA e depois LOCALAPPDATA. Só escreve com FiveM fechado. O preset FPS tenta 18 campos; Balanced tenta 16. `xmlSet` troca somente a primeira tag existente com `value="..."`; **não cria tags ausentes** e não faz parse XML completo. Assim o número de tentativas não prova 18/16 alterações em todo arquivo.

| Tag XML | FPS | Balanced |
|---|---|---|
| `MSAA` | 0 | 0 |
| `MSAAFragments` | 0 | 0 |
| `MSAAQuality` | 0 | 0 |
| `ReflectionMSAA` | 0 | 0 |
| `GrassQuality` | 0 | 1 |
| `ShadowQuality` | 1 | 2 |
| `Shadow_SoftShadows` | 1 | 2 |
| `ReflectionQuality` | 0 | 1 |
| `PostFX` | 0 | 2 |
| `SSAO` | 0 | 1 |
| `Tessellation` | 0 | 1 |
| `MaxLodScale` | "0.000000" | "0.000000" |
| `Shadow_LongShadows` | "false" | "false" |
| `UltraShadows_Enabled` | "false" | "false" |
| `HdStreamingInFlight` | "false" | não altera |
| `DoF` | "false" | "false" |
| `MotionBlurStrength` | "0.000000" | "0.000000" |
| `TXAA_Enabled` | "false" | não altera |

Escala separada: índices de entrada 0,1,2,3,4 escrevem respectivamente `SamplingMode=0,4,3,2,1`. `borderless=true` escreve Windowed=2; `false` escreve Windowed=0. O código não altera resolução explicitamente nesse fluxo.

O backup é criado uma única vez antes da primeira alteração e vale para o arquivo inteiro, não por preset. A gravação usa arquivo temporário e rename. Restaurar copia o backup integral, apaga esse backup e remove fivemPreset das configurações. Não compara o conteúdo final nem recria tags ausentes. Fontes: `backend-focused.js:8798`, `8899`, `8928`.

## 8. Critérios concretos para reimplementar com equivalência

| Área | Entrada e efeito já especificados | Verificação que a futura implementação precisa ter |
|---|---|---|
| Ações e tweaks | Nomes/payloads, chaves e tipos, comandos on/off, regras de capacidade, detectores e specs. | Capturar estado tipado antes; rejeitar operação bloqueada; reler cada valor; manter erro parcial; restaurar o estado capturado. |
| Composições antigas | Lista exata de componentes; aliases e efeitos genéricos por jogo. | Confirmar que nenhum nome publicitário seja interpretado como algoritmo adicional. |
| Limpeza | Todos os 16 seletores, idade, processos, paths e resultados. | Rodar primeiro sobre árvore de arquivos de teste; confirmar seleção e bytes sem atingir dados reais; ensaiar links, cloud, idade e erros. |
| Jogos / GPU | IDs conhecidos, origens, exe/proc e preferência GPU. | Validar o executável descoberto e conservar valor de registro anterior; separar descoberta de otimização. |
| DNS | Quatro pares, modo auto e preservação por GUID de adaptador. | Relê servidores efetivos de cada interface; não anuncia sucesso global se alguma recusou. |
| FiveM | 2 presets, todas as tags/valores, escala, janela, backup integral. | Verificar tags efetivamente existentes/alteradas e permitir recuperar o XML integral. |
| Gráficos nativos / OptiScaler | Apenas contratos de integração neste catálogo. | Usar os relatórios específicos; não tratar tabelas de opções como reimplementação de captura, upscale e frame generation. |

O executor original `run-action` retorna `on=true` depois de ausência de GDZ_ERRO e eventualmente salva snapshot posterior, **sem comparar esse snapshot nem executar DETECT nesse retorno**. `runTweak` faz probe e pode retornar `success=true,on=false`. Para reconstruir comportamento visível, esses casos precisam ser distinguidos; para um app confiável, a equivalência técnica deve exigir o efeito relido.

Lacunas práticas que continuam: resultado real e compatibilidade não foram ensaiados no Windows; políticas podem ser recusadas, serviços/pacotes podem faltar, semântica de drivers varia. Reinstalação UWP, lixo excluído, encerramento de processo e resets de rede não são cobertos por um snapshot universal. Há dados suficientes para especificar e implementar separadamente os recursos acima, mas isso não demonstra uma cópia funcional dos motores gráficos nativos.

## 9. Arquivos e reprodução

- [Extrator de declarações](../scripts/extract-gdz-operations.cjs).
- [Gerador deste catálogo e especificação](../scripts/build-gdz-operation-specs.py).
- [Inventário declarativo privado](../artifacts/private/gdz-analysis/deep/operations-declarative.json).
- [Especificação normalizada privada](../artifacts/private/gdz-analysis/deep/operation-specs.json).

```powershell
node scripts/extract-gdz-operations.cjs
python scripts/build-gdz-operation-specs.py
```

Esses comandos executam somente ferramentas próprias de análise, sobre textos já extraídos. A pasta privada contém trechos do software analisado e caminhos locais, por isso não deve integrar o código do produto nem ser publicada automaticamente.
