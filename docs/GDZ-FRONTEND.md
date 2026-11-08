# GDZ Optimizer — análise estática do frontend

Data: 08/11/2026. Versão declarada no pacote: **1.1.27** (`package.json:3`).

**Precisão acrescentada pelo aprofundamento do backend:** o menu Scaling chama `open-scaling-window`, que inicia `GDZOptimizer.exe` WPF. A existência de `src/gdz-scaling.html` e seus controles não comprova carregamento desse HTML no fluxo principal. Consulte o [mapa atualizado](GDZ-MAPA-COMPLETO.md) para distinguir o auxiliar ativo, a ponte legada e o HTML empacotado.

## Escopo e grau de evidência

Este relatório examina somente textos extraídos de `app.asar`, guardados em `artifacts/private/gdz-analysis/extracted`. As referências abaixo usam **caminhos relativos à raiz do ASAR e números de linha dos arquivos extraídos**. O frontend, o preload e o aplicativo não foram executados. Nenhum canal IPC foi invocado; nenhum serviço de autenticação, arquivo de sessão, token ou dado pessoal armazenado foi consultado.

- **Observado:** declarações, seletores, parâmetros e decisões presentes no código legível.
- **Inferido:** intenção provável de um identificador ou de uma chamada; não comprova implementação nem efeito no Windows.
- Os rótulos comerciais e as promessas de desempenho são conteúdo do produto, não resultados desta análise.
- A presença de um canal na allowlist ou de uma chamada no renderer não prova a existência, validação ou eficácia do handler no processo principal.

## Organização e fluxo

O pacote declara `app-main.js` como entrada e `electron-updater` como dependência (`package.json:6`, `package.json:9`). O preload usa `contextBridge.exposeInMainWorld` para fornecer `window.electron` (`preload.js:37`). O dashboard utiliza essa ponte e contém um fallback de desenvolvimento, com hardware de exemplo e percentuais aleatórios, apenas quando `window.electron` está ausente (`src/dashboard.html:1519`). Esse fallback é evidência de um modo de demonstração no código; não demonstra que os valores da captura sejam simulados.

O dashboard concentra HTML, CSS inicial e lógica inline. Carrega, nesta ordem, quinze módulos: `otimizacoes.js`, `fps.js`, `pcinfo.js`, `gtarp.js`, `opti.js`, `dash-v3.js`, `memoria.js`, `limpeza.js`, `programas.js`, `fundo.js`, `busca.js`, `mouse-test.js`, `plano.js`, `sons.js` e `tour.js` (`src/dashboard.html:2802`). Vários módulos embrulham `window.go`, formando uma cadeia de atualização ao trocar de página, em vez de usar um roteador de framework.

### Navegação observada

`go(pg, el)` alterna `.active` em páginas e itens do menu, move o indicador, zera o scroll e coloca foco na página (`src/dashboard.html:1731`). Não altera URL, hash ou histórico nesse trecho. `goTo` pode destacar um componente específico; `scaling` abre uma janela separada (`src/dashboard.html:1787`).

| ID de página | Função apresentada | Referência |
|---|---|---|
| `dashboard` | Painel inicial, recomendações e hardware | `src/dashboard.html:711` |
| `jogos` | Jogos detectados e modo automático | `src/dashboard.html:806` |
| `scaling` | Configuração de escala; menu abre janela própria | `src/dashboard.html:832`, `src/dashboard.html:1697` |
| `gargalo` | Diagnóstico de gargalo | `src/dashboard.html:1200` |
| `fps` | Medidor, comparações e overlay | `src/dashboard.html:1271` |
| `opti` | GTA RP e DLSS/FSR | `src/dashboard.html:1277`, `src/plano.js:12` |
| `pcinfo` | Informações do PC | `src/dashboard.html:1283` |
| `hardware` | Verificações de configuração | `src/dashboard.html:1298` |
| `desempenho` | Energia, GPU e processos de fundo | `src/dashboard.html:1307` |
| `inputlag` | Mouse e teclado | `src/dashboard.html:1312` |
| `live` | Internet e live | `src/dashboard.html:1344` |
| `memoria` | RAM e memória virtual | `src/dashboard.html:1349` |
| `limpeza` | Limpeza e ocupação do disco | `src/dashboard.html:1381` |
| `programas` | Desinstalação e sobras | `src/dashboard.html:1409` |
| `tweaks` | Windows | `src/dashboard.html:1427` |
| `inicializacao` | Aplicativos que iniciam com Windows | `src/dashboard.html:1431` |
| `sobre` | Produto, plano, preferências e atualizações | `src/dashboard.html:1442` |

`plano.js` decora o menu e intercepta a navegação conforme `user.features`; aceita `*` ou um identificador de recurso mapeado (`src/plano.js:43`, `src/plano.js:390`). Exibe plano, expiração, estado offline e situação de outro dispositivo (`src/plano.js:56`). Isso descreve a apresentação e o fluxo de acesso na UI; não estabelece como a autorização é validada no backend.

## Superfície completa exposta pelo preload

Quatro métodos estão disponíveis em `window.electron`:

| Método | Comportamento estático observado |
|---|---|
| `send(channel, ...args)` | Encaminha somente os nove canais listados; fora deles não faz chamada (`preload.js:38`). |
| `invoke(channel, ...args)` | Encaminha somente os 87 canais listados; fora deles retorna Promise rejeitada (`preload.js:41`). |
| `on(channel, cb)` | Aceita quatro eventos e callback do tipo função; repassa argumentos sem o objeto de evento Electron (`preload.js:45`). Não expõe método de remoção de listener. |
| `openExternal(url)` | Chama `shell.openExternal` quando recebe string iniciada por `http://` ou `https://` (`preload.js:50`). |

Os comentários sobre `contextIsolation` e `nodeIntegration` estão em `preload.js:3`. A configuração efetiva das janelas deve ser conferida no processo principal; um comentário não a comprova.

### Nove canais `send`

`minimize-window`, `maximize-window`, `close-window`, `load-dashboard`, `logout`, `open-scaling-window`, `gs-minimize`, `gs-maximize`, `gs-close` (`preload.js:10`).

### 87 canais `invoke`

| Grupo descritivo | Canais completos | Referência |
|---|---|---|
| Conta e navegação externa | `login`, `register`, `check-session`, `get-user`, `open-site`, `claim-key` | `preload.js:16` |
| Atualização | `update-status`, `update-install` | `preload.js:16` |
| Preferências e ofertas | `telemetry-consent`, `telemetry-consent-set`, `offers`, `plan-event`, `autostart-get`, `autostart-set` | `preload.js:17` |
| Hardware e processos | `get-hardware`, `get-stats`, `get-mem-stats`, `get-processes`, `kill-process` | `preload.js:18` |
| Resolução e scaling | `gdz-scaling-get-res`, `gdz-scaling-apply`, `gdz-scaling-revert`, `ls-browse-exe`, `ls-find-exe`, `ls-launch`, `ls-stop`, `ls-config` | `preload.js:19` |
| Ações e diagnóstico | `run-action`, `revert-action`, `restore-all`, `get-backups`, `get-caps`, `run-check`, `measure-bottleneck`, `bn-find-game`, `bn-notify` | `preload.js:21` |
| Jogos | `games-scan`, `games-status`, `set-auto-mode`, `game-clean`, `shader-clean` | `preload.js:23` |
| RAM | `ram-clean`, `ram-status`, `ram-auto-set` | `preload.js:24` |
| Inicialização e recomendações | `startup-list`, `startup-set`, `get-recommendations` | `preload.js:25` |
| Limpeza e discos | `clean-scan`, `clean-run`, `disk-drives`, `disk-scan`, `disk-scan-status`, `disk-scan-cancel`, `disk-open` | `preload.js:26` |
| Aplicativos | `apps-list`, `apps-extra`, `apps-uninstall`, `apps-exists`, `apps-leftovers`, `apps-remove-leftovers` | `preload.js:27` |
| Ajustes | `tweaks-status`, `tweak-set`, `tweak-dns`, `tweak-tool`, `actions-status` | `preload.js:28` |
| FPS | `fps-status`, `fps-set-target`, `fps-record`, `fps-clear`, `fps-overlay-set`, `fps-base-mark`, `fps-base-clear` | `preload.js:29` |
| DLSS/FSR | `opti-status`, `opti-download`, `opti-browse`, `opti-analyze`, `opti-detect`, `opti-install`, `opti-uninstall`, `opti-config`, `opti-open-folder` | `preload.js:30` |
| FiveM e painel da GPU | `fivem-gfx-status`, `fivem-gfx-apply`, `fivem-gfx-restore`, `open-gpu-panel` | `preload.js:31` |
| Informações do PC | `pcinfo-get`, `pcinfo-live` | `preload.js:32` |

### Quatro eventos

`fps-update`, `overlay-update`, `entitlement-update`, `update-status` (`preload.js:35`). O overlay registra `overlay-update` (`src/fps-overlay.html:81`); a tela de plano registra `update-status` e `entitlement-update` (`src/plano.js:529`). A exposição de `fps-update` não significa que todos os módulos o consumam; o medidor principal consulta `fps-status` a cada segundo.

## Catálogo declarado na UI

`src/otimizacoes.js:13` define oito tipos: `T` (tweak), `A` (ação reversível), `O` (ação pontual), `C` (verificação), `TOOL` (ferramenta), `FIX` (reparo), `TIP` (dica) e `DNS`. A contagem abaixo resulta de inspeção das declarações em `PAGES`, sem executar JavaScript.

| Página | T | A | O | C | TOOL | FIX | TIP | DNS | Total declarado |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Desempenho | 8 | 16 | 3 | 0 | 0 | 0 | 2 | 0 | 29 |
| Mouse e teclado | 4 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 4 |
| Memória | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| Limpeza | 1 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 2 |
| Internet e live | 4 | 1 | 0 | 1 | 2 | 0 | 0 | 1 | 9 |
| Windows | 16 | 0 | 2 | 0 | 2 | 4 | 0 | 0 | 24 |
| Verificar hardware | 0 | 0 | 0 | 4 | 0 | 0 | 5 | 0 | 9 |
| **Total** | **33** | **18** | **5** | **5** | **5** | **4** | **7** | **1** | **78** |

São **51 declarações de interruptores** (`T + A`) antes dos filtros de aplicabilidade, disponibilidade, recomendação e bloqueio. As 78 entradas incluem dicas e verificações; não equivalem a 78 otimizações aplicáveis. Outros módulos têm recursos fora desse catálogo, como RAM, limpeza de arquivos, aplicações, FPS, scaling e FiveM.

### 33 IDs de tweaks

Para `T(id)`, **o título literal, descrição, recomendação, necessidade de reinício, bloqueio e estado chegam de `tweaks-status`** (`src/otimizacoes.js:290`). O frontend não permite recuperar sozinho todos os nomes oficiais. A coluna “assunto” abaixo é inferência dos IDs, sinônimos e contexto, não transcrição de uma resposta do backend.

| Página | IDs declarados | Assunto inferível e origem |
|---|---|---|
| Desempenho | `game-mode`, `steam-overlay`, `bg-apps`, `edge-bg` | Modo de Jogo, sobreposição Steam e execução em segundo plano (`src/otimizacoes.js:57`). |
| Desempenho | `dlss-rec`, `gpu-msi`, `mpo` | Modelo DLSS recomendado, interrupções da GPU e MPO (`src/otimizacoes.js:80`, `src/otimizacoes.js:236`). |
| Desempenho | `transparency` | Transparência do Windows (`src/otimizacoes.js:89`, `src/otimizacoes.js:235`). |
| Mouse e teclado | `mouse-accel`, `kbd-repeat`, `sticky`, `numlock` | Aceleração do mouse, repetição de teclas, teclas de aderência e Num Lock (`src/otimizacoes.js:123`). |
| Limpeza | `hibernate` | Hibernação (`src/otimizacoes.js:143`, `src/otimizacoes.js:243`). |
| Internet e live | `delivery`, `lso`, `eee`, `nic-intmod` | Delivery Optimization, Large Send Offload, Energy Efficient Ethernet e moderação de interrupções (`src/otimizacoes.js:151`, `src/otimizacoes.js:232`). |
| Windows | `telemetry`, `suggestions`, `bing`, `activity`, `widgets`, `location` | Telemetria, sugestões, busca web, atividade, widgets e localização (`src/otimizacoes.js:166`). |
| Windows | `end-task`, `file-ext`, `hidden-files`, `this-pc`, `classic-menu`, `dark`, `taskbar-left`, `task-view`, `search-icon`, `bsod-detail` | Opções de interface, Explorer, barra de tarefas e detalhe de erros (`src/otimizacoes.js:169`). |

### 18 ações reversíveis `A`

O identificador enviado é o texto de `act`; o nome exibido é o `title` abaixo. Chave interna da UI: prefixo `act:` seguido do identificador (`src/otimizacoes.js:14`).

| Página | Identificador enviado | Nome exibido | Linha em `src/otimizacoes.js` |
|---|---|---|---:|
| Desempenho | `Plano de Alta Performance` | Plano de energia Alto Desempenho | 46 |
| Desempenho | `Modo Máximo Desempenho` | Plano de energia Desempenho Máximo | 50 |
| Desempenho | `Modo X3D Correto` | Modo certo para Ryzen X3D | 53 |
| Desempenho | `DVR Desativado` | Sem gravação em segundo plano | 58 |
| Desempenho | `Game Bar Desativado` | Game Bar (Win+G) desligada | 60 |
| Desempenho | `Sem Reinício Durante o Jogo` | Windows não reinicia sozinho | 67 |
| Desempenho | `Desativar Notificações` | Sem notificações do Windows | 69 |
| Desempenho | `HAGS Ativo` | Agendamento de GPU por hardware (HAGS) | 76 |
| Desempenho | `Jogos em Janela Otimizados` | Jogos em janela e sem bordas otimizados | 78 |
| Desempenho | `Efeitos Visuais Off` | Sem efeitos visuais do Windows | 86 |
| Desempenho | `SysMain Desativado` | SysMain (Superfetch) desligado | 90 |
| Desempenho | `Indexação Off` | Indexação de arquivos desligada | 93 |
| Desempenho | `Serviços Desativados` | Serviços pouco usados desligados | 96 |
| Desempenho | `Serviços Xbox Desativados` | Serviços do Xbox desligados | 99 |
| Desempenho | `Game Bar Removido` | Xbox Game Bar desinstalado | 102 |
| Desempenho | `720p Boost Ativo` | Tela em 720p (emergência) | 104 |
| Memória | `Pagefile Automático` | Memória virtual gerenciada pelo Windows | 131 |
| Internet e live | `OBS Prioritário` | OBS com prioridade | 154 |

### Outras 27 entradas

| Tipo | Identificador | Nome ou assunto apresentado | Referência |
|---|---|---|---|
| O | `UWP Removidos` | Remover apps que vêm com o Windows | `src/otimizacoes.js:107` |
| O | `Chrome/Discord Limitados` | Chrome e Discord em prioridade baixa | `src/otimizacoes.js:113` |
| O | `Apps de Sincronização Fechados` | Fechar OneDrive, Dropbox e Google Drive | `src/otimizacoes.js:115` |
| O | `Explorer Mínimo` | Reiniciar a barra de tarefas | `src/otimizacoes.js:175` |
| O | `Padrões Restaurados` | Limpar ajustes antigos do GDZ | `src/otimizacoes.js:177` |
| C | `encoder` | Configuração ideal do OBS | `src/otimizacoes.js:157` |
| C | `amd` | Verificar Ryzen | `src/otimizacoes.js:194` |
| C | `intel` | Verificar processador Intel | `src/otimizacoes.js:195` |
| C | `gpu` | Verificar placa de vídeo | `src/otimizacoes.js:200` |
| C | `ram` | Verificar memória (XMP/EXPO) | `src/otimizacoes.js:206` |
| TOOL | `update-cleanup` | Limpar sobras do Windows Update | `src/otimizacoes.js:220` |
| TOOL | `flush-dns` | Limpar cache de DNS | `src/otimizacoes.js:214` |
| TOOL | `net-reset` | Resetar a rede | `src/otimizacoes.js:216` |
| TOOL | `restore-point` | Criar ponto de restauração | `src/otimizacoes.js:212` |
| TOOL | `repair` | Reparar arquivos do Windows | `src/otimizacoes.js:222` |
| FIX | `Defender Off` | Religar o Windows Defender | `src/otimizacoes.js:179` |
| FIX | `Desativar Atualizações Windows` | Religar o Windows Update | `src/otimizacoes.js:181` |
| FIX | `System Restore Off` | Religar a Restauração do Sistema | `src/otimizacoes.js:183` |
| FIX | `DPI Scaling Off` | Consertar a escala da tela | `src/otimizacoes.js:185` |
| TIP | Chave derivada do título | Desligar sobreposição da NVIDIA no próprio aplicativo | `src/otimizacoes.js:63` |
| TIP | Chave derivada do título | Desligar sobreposição do Discord no próprio aplicativo | `src/otimizacoes.js:65` |
| TIP | Chave derivada do título | Ryzen: ajustes na BIOS | `src/otimizacoes.js:196` |
| TIP | Chave derivada do título | Intel com P-cores e E-cores | `src/otimizacoes.js:197` |
| TIP | Chave derivada do título | NVIDIA RTX e DLSS 4.5 | `src/otimizacoes.js:201` |
| TIP | Chave derivada do título | NVIDIA: menor atraso e sincronização | `src/otimizacoes.js:202` |
| TIP | Chave derivada do título | AMD Radeon: menor atraso | `src/otimizacoes.js:203` |
| DNS | `dns` | Servidor DNS; `auto`, `cloudflare`, `google`, `quad9`, `adguard` | `src/otimizacoes.js:20`, `src/otimizacoes.js:262` |

`T` usa `tweak-set {id,on}`; `A` usa `run-action` ou `revert-action {action}`; `O` usa `run-action`; `FIX` usa `revert-action`; `TOOL` usa `tweak-tool {id}`; `C` usa `run-check {name}`; DNS usa `tweak-dns {provider}` (`src/otimizacoes.js:548`, `src/otimizacoes.js:579`, `src/otimizacoes.js:605`, `src/otimizacoes.js:622`). As dicas não têm operação de ajuste.

## Como são calculadas as contagens da captura

### Carregamento e aplicabilidade

O catálogo consulta em paralelo `tweaks-status`, `actions-status` e `get-caps`; guarda os dados em memória (`src/otimizacoes.js:304`). Capacidade de hardware pode esconder itens: AMD/Intel, X3D de dois CCDs, CPU híbrida, NVIDIA/AMD GPU/RTX, OBS e Discord (`src/otimizacoes.js:281`). Bloqueios de ações vêm de `caps.blocked`; os de tweaks chegam no próprio tweak (`src/otimizacoes.js:294`).

Há ainda recomendação condicional declarada no frontend: Alto Desempenho é recomendado quando não se trata de notebook nem X3D de dois CCDs; Modo X3D exige a capacidade correspondente; OBS exige `obsExe` (`src/otimizacoes.js:46`, `src/otimizacoes.js:53`, `src/otimizacoes.js:154`). As regras dos 33 tweaks dependem da resposta do backend e não podem ser completadas por esta leitura.

### Denominador do anel

`gdzOpt.recommended()` seleciona somente interruptores `T/A` visíveis, com informação disponível, marcados como recomendados e sem bloqueio; exclui a página de verificações (`src/otimizacoes.js:518`, `src/otimizacoes.js:706`). Portanto:

- **Total do anel** = quantidade de interruptores recomendados e aplicáveis.
- **Aplicados** = quantidade dessa seleção cujo estado `on` é verdadeiro.
- **Pendentes** = total menos aplicados.

O `14 de 19` da captura é compatível com essa seleção e com `5 pendentes`; não é uma nota de benchmark nem fração de todas as 78 entradas. Não foi possível reproduzir o número 19 sem consultar o estado e as capacidades reais do backend.

`dash-v3.js` escolhe “Nada aplicado ainda”, “Dá para melhorar” ou “Tudo no ponto” conforme os resultados (`src/dash-v3.js:145`). O texto lista a primeira recomendação pendente, o número de aplicativos da inicialização e temporários quando chegam a pelo menos 100 MiB (`src/dash-v3.js:171`). `Ver pendentes` abre a primeira recomendação desligada (`src/otimizacoes.js:709`).

Os cinco cartões iniciais excluem `checksOnly` e `noBar`; isso remove Hardware e Limpeza do conjunto de cartões. Cada cartão mostra ligados/recomendados, ou ligados/total se não há recomendados (`src/otimizacoes.js:673`). Os contadores dentro de uma página incluem todos os seus interruptores visíveis, enquanto o botão “Aplicar recomendados” considera recomendados desligados, não bloqueados e fora de operação (`src/otimizacoes.js:521`).

### Duas operações distintas

“Aplicar recomendados” de uma página confirma a lista e alterna os itens sequencialmente, computando sucessos e reinícios necessários (`src/otimizacoes.js:636`). **O botão global “Otimização Rápida” não executa esse loop do frontend:** envia `run-action {action: 'Otimização Rápida'}` e confia na resposta do processo principal (`src/dashboard.html:1946`). O subconjunto executado por essa ação não pode ser estabelecido por esse trecho do renderer.

Após sucesso, a UI atualiza recomendações, backups, histórico recente, evento de diagnóstico e o texto “agora mesmo” (`src/dashboard.html:1954`). Um `MutationObserver` converte esse texto em timestamp em `localStorage` (`src/dash-v3.js:224`). A data exibida é registro local de uma resposta de sucesso, não medição independente de todas as mudanças aplicadas.

O catálogo notifica `gdz-opt-change`, relê depois de mudanças e ao entrar em páginas se a leitura anterior tiver mais de quatro segundos (`src/otimizacoes.js:653`, `src/otimizacoes.js:699`, `src/otimizacoes.js:759`).

## Dados consumidos e recursos por módulo

| Área | Dados e parâmetros observados | Evidência |
|---|---|---|
| Conta | Formulários recebem e-mail/senha e, no cadastro, nome e aceite de termos. Enviam os campos por IPC. O dashboard recebe usuário; planos recebem recursos, expiração e condições da licença. | `src/login.html:574`, `src/login.html:606`, `src/dashboard.html:1556`, `src/plano.js:43` |
| Resumo de hardware | CPU, GPU, memória e detalhes textuais; percentuais CPU/RAM/GPU via `get-stats`, a cada 2,5 s. | `src/dashboard.html:1646`, `src/dashboard.html:2233`, `src/dashboard.html:2799` |
| Inicialização | Lista com nome, escopo e estado; alteração envia `{name,scope,enabled}`. | `src/dashboard.html:1863`, `src/dashboard.html:1882` |
| Recomendações | `startupOn` e `tempBytes`; o painel encaminha para Inicialização ou Limpeza. | `src/dashboard.html:1817`, `src/dashboard.html:1826` |
| Memória | RAM usada/total/cache, percentual e pentes: slots, capacidade, geração, velocidade, fabricante e part number quando fornecidos. Atualização visual a cada 2 s apenas com página ativa, documento visível e janela focada; estado da limpeza automática a cada 15 s na página. | `src/dashboard.html:2174`, `src/dashboard.html:2190`, `src/dashboard.html:2230`, `src/memoria.js:110`, `src/memoria.js:137` |
| Limpeza de RAM | `ram-status` informa intervalos, frequência, próxima/última execução e quantidade liberada; `ram-auto-set` recebe `{on,minutes}`; limpeza manual usa `ram-clean`. | `src/dashboard.html:2190`, `src/dashboard.html:2206`, `src/dashboard.html:2218` |
| Processos | Nome, PID e RAM; comando de encerramento recebe `{pid,name}`. | `src/dashboard.html:2581`, `src/dashboard.html:2595` |
| Gargalo | Procura jogo a cada 3 s por até 10 min, solicita medição de 40 s usando PID e apresenta resultado. O algoritmo que produz o diagnóstico está no processo principal. | `src/dashboard.html:2345`, `src/dashboard.html:2413`, `src/dashboard.html:2454` |
| Jogos | Faz scan de instalações/processos; consulta status a cada 5 s com página aberta; estado inclui `running`, `autoMode` e `autoGame`. Limpeza envia ID de jogo ou operação de shaders. | `src/dashboard.html:2725`, `src/dashboard.html:2752`, `src/dashboard.html:2762`, `src/dashboard.html:2790` |
| Limpeza de arquivos | Recebe categorias `cats` com IDs, bytes, estado aberto e seleção padrão; grupos Windows, Navegadores e Apps/jogos. Só seleciona itens com bytes e sem app aberto; envia IDs escolhidos, exibe bytes liberados e resultados por categoria. Confirma Lixeira separadamente. | `src/limpeza.js:43`, `src/limpeza.js:52`, `src/limpeza.js:138` |
| Ocupação do disco | Recebe unidades, progresso, pastas e arquivos; solicita scan por unidade, consulta status, permite cancelamento e abrir caminho. Não há comando de exclusão de arquivo nessa interface de exploração. | `src/limpeza.js:185`, `src/limpeza.js:213`, `src/limpeza.js:252`, `src/limpeza.js:389` |
| Programas | Lista instalações e dados extras de ícone/tamanho; apresenta nome, publicador, versão e data. Desinstala pelo `key`, acompanha existência a cada 2,5 s por até 30 min e oferece seleção de sobras por IDs. | `src/programas.js:31`, `src/programas.js:66`, `src/programas.js:124`, `src/programas.js:141`, `src/programas.js:212` |
| FPS | Recebe processos/PIDs, FPS, frametime, lows, baseline, overlay e sessões. Gravações de 15/30/60 s, dois slots de comparação; registra `{slot,secs,label}`. Consulta status a cada 1 s apenas na página. | `src/fps.js:6`, `src/fps.js:113`, `src/fps.js:162`, `src/fps.js:237`, `src/fps.js:255` |
| Overlay | Recebe evento com jogo, FPS/frametime/low, uso/temperatura CPU/GPU, RAM e baseline; permite quatro cantos e campos selecionados. | `src/fps.js:6`, `src/fps-overlay.html:50` |
| Informações do PC | Dados estáticos via `pcinfo-get`; sensores via `pcinfo-live` a cada 2 s na página. CPU, caches e instruções; placa-mãe/BIOS/TPM/Secure Boot; módulos RAM; GPUs e drivers; telas; discos, volumes e saúde; Windows; adaptadores de rede; bateria; sensores com mínimo/máximo da sessão. | `src/pcinfo.js:231`, `src/pcinfo.js:249`, `src/pcinfo.js:263`, `src/pcinfo.js:275`, `src/pcinfo.js:283`, `src/pcinfo.js:388` |
| Exportação do PC | Monta relatório textual a partir dos mesmos campos mostrados e escreve na área de transferência após clique. Não envia mensagem a um terceiro nesse código. | `src/pcinfo.js:336`, `src/pcinfo.js:425` |
| DLSS/FSR | Detecta jogo/executável, pacote e instalação; recebe análise; envia executável, nome de DLL, opções e aceite sobre anti-cheat. Opções incluem upscaler, geração de quadros, spoof, FPS e menu. O frontend não implementa o upscaler. | `src/opti.js:7`, `src/opti.js:12`, `src/opti.js:98`, `src/opti.js:174` |
| FiveM | Lê configuração, preset, SamplingMode e modo de janela; envia preset, escala e sem bordas; oferece restauração. Escalas visuais: nativo, 83%, 75%, 67% e 50%. | `src/gtarp.js:6`, `src/gtarp.js:74`, `src/gtarp.js:94` |
| Scaling | Janela separada reúne frame generation, alvo, flow scale, API de captura, fila, upscaler, nitidez, fator e sincronização; envia configuração a `ls-launch` ou pede `ls-stop`. | `src/gdz-scaling.html:558`, `src/gdz-scaling.html:594` |
| Plano e atualização | Consulta ofertas, registra eventos de plano, abre site por finalidade, atualiza sessão ou resgata chave via main; recebe status de update e pede instalação. Nenhuma dessas operações foi realizada nesta análise. | `src/plano.js:26`, `src/plano.js:234`, `src/plano.js:256`, `src/plano.js:272`, `src/plano.js:449` |

### Outros algoritmos visíveis

- **Comparação de FPS:** calcula variação percentual `(depois - antes) / antes × 100`; no frametime, redução é considerada melhora. Trata diferenças menores que 3% como variação e mostra aviso se os jogos são diferentes (`src/fps.js:10`, `src/fps.js:130`). O renderer consome os valores de FPS/lows; não mostra nesse trecho como os frames foram capturados.
- **Teste de mouse:** coleta timestamps de `pointerrawupdate`, ou eventos coalescidos como fallback; desconsidera intervalos menores que 0,05 ms ou a partir de 20 ms; exige pelo menos 50 intervalos e estima Hz pelo inverso da mediana. Aproxima taxas nominais de 125 a 8000 Hz se a diferença for até 15%; estabilidade é a fração dentro de ±25% da mediana (`src/mouse-test.js:8`, `src/mouse-test.js:23`, `src/mouse-test.js:44`). É uma estimativa baseada em eventos do renderer, não leitura direta da configuração USB.
- **Busca:** combina páginas, catálogo aplicável, tags, sinônimos e descrições, normalizando acentos. Exige cada palavra em algum campo; início de palavra no título pesa 30, título parcial 18, metadados 12, descrição 5 e página recebe bônus 3. Sem consulta mostra até cinco recomendações pendentes ou atalhos. Atalho Ctrl/Cmd+K e navegação por teclado (`src/busca.js:74`, `src/busca.js:90`, `src/busca.js:128`, `src/busca.js:261`).
- **Apresentação da RAM:** estima canais a partir dos rótulos de banco/slot quando disponíveis; capacidade e velocidade derivam dos módulos recebidos (`src/pcinfo.js:61`). A animação dos pentes varia com o percentual de RAM, com estados de atenção a partir de 85% e crítico a partir de 93% (`src/memoria.js:110`). Isso é sinalização visual, não um teste de estabilidade da memória.

## Persistência observada no renderer

Nenhum valor real das preferências abaixo foi lido; foram identificadas apenas as chaves declaradas no código.

| Chave/estado | Conteúdo e limite | Referência |
|---|---|---|
| `gdz-theme` | `light`/`dark` | `src/dashboard.html:8` |
| `gdz-efeitos` | Efeitos visuais `off`/`on` | `src/dashboard.html:10`, `src/fundo.js:278` |
| `gdz-sons` | Sons `off`/`on` | `src/sons.js:6`, `src/sons.js:189` |
| `gdz-last-quickopt` | Timestamp de resposta de sucesso da otimização rápida | `src/dash-v3.js:208` |
| `gdz-mouse-tests` | Resultados de até seis testes de mouse, com timestamp | `src/mouse-test.js:11`, `src/mouse-test.js:21` |
| `gdz-pcinfo-mode` | Modo simples/avançado | `src/pcinfo.js:14`, `src/pcinfo.js:383` |
| `gdz-tour` | Versão vista do tutorial, atualmente `1` | `src/tour.js:14`, `src/tour.js:471` |
| `gdz-nudge:<chave>` | Timestamp para limitar repetição de avisos; intervalo global de 10 min e prazo por aviso | `src/plano.js:288`, `src/plano.js:294` |
| `recentArr` | Histórico de até seis ações em memória, com hora e destino; sem escrita local nesse código | `src/dashboard.html:1763` |
| `st`, `cl`, `dk` e estados dos módulos | Cache de respostas, seleções, progresso, filtros e mínimos/máximos em memória | `src/otimizacoes.js:271`, `src/limpeza.js:48`, `src/limpeza.js:165`, `src/pcinfo.js:13` |

Os backups não são gravados pelo renderer analisado: a UI consulta `get-backups`, oferece reversão por ação e `restore-all` (`src/dashboard.html:1564`). Local, formato e confiabilidade desses backups devem ser tratados na análise do backend. O mesmo limite vale para sessão, consentimento, autostart, configurações de FPS/scaling, programas e tarefas automáticas: o renderer delega operações ao main e não determina sozinho onde são persistidas.

## Recursos visuais e dependências externas declaradas

- Fontes Inter, Outfit e JetBrains Mono via Google Fonts (`src/dashboard.html:17`). Há quinze folhas CSS locais referenciadas de `tweaks.css` a `tour.css` (`src/dashboard.html:581`); login usa seu próprio tema. A referência de um asset não prova que ele tenha sido incluído nesta extração parcial de textos.
- Marca `src/logo.png`, ícone `bin/GDZOptimizer.ico`, SVGs inline, gradientes e capas locais/embutidas; há JPEGs em data URI no catálogo de jogos (`src/dashboard.html:601`, `src/dashboard.html:755`, `src/dashboard.html:2632`, `src/gdz-scaling.html:237`).
- Doze jogos recebem nomes/capas especiais na UI: Counter-Strike 2, Valorant, Warzone, Fortnite, Minecraft, FiveM, Apex Legends, League of Legends, Free Fire, Roblox, Rainbow Six Siege e Overwatch 2 (`src/dashboard.html:2621`). Há associações adicionais para capas de GTA V, Red Dead Redemption 2, Rocket League e PUBG (`src/dashboard.html:2642`). Isso não é o inventário de jogos efetivamente instalado na máquina.
- Capas Steam podem ser carregadas de `cdn.cloudflare.steamstatic.com` e `shared.cloudflare.steamstatic.com` (`src/dashboard.html:2616`); links sociais são encaminhados ao navegador externo (`src/dashboard.html:1452`).
- A CSP declarada permite scripts e estilos inline, fontes Google, imagens HTTPS e conexão ao domínio `keyauth.win` (`src/dashboard.html:5`, `src/gdz-scaling.html:5`). Essa permissão não demonstra que um pedido a esse domínio ocorra, nem determina o serviço de autenticação efetivamente utilizado pelo main.
- Sons são produzidos por lógica Web Audio em `sons.js`; efeitos e movimento ficam em módulos próprios e CSS. Tutorial inicial tem versão persistente e coordena sua exibição com avisos de plano (`src/sons.js:29`, `src/tour.js:14`, `src/plano.js:41`).

## Limites e conclusões verificáveis

O frontend fornece evidência suficiente para reconstruir o mapa de páginas, catálogo declarado, contrato de chamadas e critérios das contagens. Confirma também que o painel central representa a cobertura de recomendações aplicáveis; não mede ganho de FPS.

Não foram estabelecidos por este relatório: comandos reais no Windows, regras completas de hardware, algoritmos internos de diagnóstico/FPS, eficácia e reversibilidade das otimizações, origem binária de scaling, integridade das dependências, destino real da autenticação/telemetria ou armazenamento de sessão. O texto da caixa de consentimento afirma que configuração de hardware é enviada sempre e que métricas de FPS dependem do consentimento (`src/plano.js:468`); isso é **declaração visível do produto**, cuja implementação exige análise separada do processo principal.

O trabalho realizado foi leitura estática e produção deste mapa técnico, sem alteração do programa instalado ou de `src/release`.
