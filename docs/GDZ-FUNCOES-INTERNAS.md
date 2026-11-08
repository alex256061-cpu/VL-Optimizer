# GDZ: 159 funções declaradas do processo principal

Cada linha foi escrita a partir do corpo recuperado, com interpretação de entradas, saídas e efeitos. Os parâmetros abaixo têm nomes descritivos; os identificadores originais estão no JSON de evidência. Nenhuma função do produto foi executada.

A contagem anterior de 175 era lexical: incluía 16 funções PowerShell dentro de strings. A análise sintática separa **159 declarações JavaScript**, mais **70 funções ligadas a variáveis** descritas no [catálogo complementar](GDZ-HELPERS-EXPRESSOES.md), além dos callbacks IPC e estruturas de ofuscação.

As referências B:L apontam para a linha L de `artifacts/private/gdz-analysis/deep/backend-focused.js`; offsets e intervalos do original ficam em `functions-explained.json`. A view é um instrumento de leitura estática, não um programa reconstruído para execução.

SHA-256 da view utilizada: `52ca9c4279b739ec674637dae36a361b62eaf06b94aaa6db2a9caba6d4baaef0`.


## Caminhos, autenticação e comunicação

| Função | Entrada | Saída | O que faz e efeito | Evidência |
|---|---|---|---|---|

| `getBinPath` | Segmentos de caminho | Caminho em bin | No pacote troca app.asar por app.asar.unpacked; em desenvolvimento usa bin ao lado da entrada. Não inicia o arquivo. | B:143 |

| `getSrcPath` | Segmentos de caminho | Caminho em src | Resolve recursos HTML/CSS/JS dentro da distribuição, sem abrir janelas por si só. | B:150 |

| `verifyEntitlement` | Documento de autorização assinado | Claims válidas ou null | Separa payload e assinatura base64url, verifica assinatura criptográfica e formato v/uid; rejeita conteúdo inválido. Não concede acesso por nome de plano presumido. | B:165 |

| `loadAuth` | Arquivo de sessão do app | Objeto com token ou null | Lê e descriptografa pelo safeStorage do Electron, aceita objeto com token textual; falhas resultam em null. O arquivo real não foi aberto nesta análise. | B:186 |

| `saveAuth` | Objeto de sessão | Booleano de gravação | Serializa, criptografa com safeStorage e grava authPath; retorna false se criptografia indisponível ou houver erro. | B:197 |

| `clearAuth` | Nenhuma | Sem retorno útil | Apaga o arquivo de sessão se existir; ignora falhas. Não foi chamada na análise. | B:207 |

| `removeLegacyAuthFiles` | Nenhuma | Sem retorno útil | Remove accounts.json, session.json e license.bin antigos do diretório userData; não representa limpeza geral do perfil. | B:212 |

| `apiCall` | Método, rota, body/token/timeout opcionais | Objeto offline/status/data | Faz requisição JSON com net.fetch, versão no user-agent, Bearer quando fornecido e AbortSignal; erros de transporte viram offline=true. | B:222 |

| `userFromClaims` | Claims verificadas e flag offline | Modelo de usuário | Monta IDs, nome derivado do e-mail, plano, recursos, expiração e condição offline. Transformação de dados, sem escrita direta no Windows. | B:262 |

| `acceptEntitlement` | Token, autorização assinada e offline | Usuário ou null | Verifica documento, persiste sessão criptografada com maior horário observado e atualiza currentUser. Não foi executada. | B:280 |

| `hasFeature` | Identificador de recurso | Booleano | Consulta a lista features do usuário e o curinga declarado; não avalia hardware ou eficácia de otimização. | B:292 |

| `refreshEntitlement` | Sessão persistida | Resultado ok/offline/revoked/message | Atualiza autorização pelo servidor; trata revogação e fallback offline com documento assinado, prazo e verificação de relógio. | B:296 |

| `channelAllowed` | Canal IPC e argumentos | Booleano | Decide acesso por lista de canais sempre disponíveis, usuário e recursos contratados; run-action tem regra específica para otimização rápida. | B:353 |

| `appAuth` | Rota de autenticação, formulário e mensagem de erro | success/user ou erro | Obtém identificação do PC, acrescenta board/hostname/versão/Windows, envia POST e aceita autorização assinada; agenda envio de informações após sucesso. | B:899 |

| `forceLogout` | Motivo | Sem retorno útil | Limpa currentUser e sessão persistida, fecha overlay, guarda motivo e retorna a janela para login. | B:953 |

| `loadOffers` | Flag de atualização forçada | Planos/ofertas ou null | Busca ofertas autenticadas e usa cache por usuário e TTL; se a rede falhar pode devolver cache anterior. | B:1059 |

| `flushPlanEvents` | Fila em memória | Promise sem dados de UI | Retira até 30 eventos e envia para /app/events se houver sessão. O lote é removido da fila antes da resposta; não há reencaminhamento nesse corpo. | B:1109 |

| `syncPlan` | Estado atual | Promise sem retorno de UI | Evita chamadas concorrentes, atualiza autorização, força logout se revogada e avisa renderer quando plano/recursos mudam. | B:1147 |

| `syncPlanOnFocus` | Evento de foco | Sem retorno útil | Solicita sincronização somente depois do intervalo mínimo; não faz ajuste de desempenho ao receber foco. | B:1173 |

| `sendUpdateState` | updateState em memória | Evento update-status | Envia estado de atualização à janela existente; não baixa nem instala por si só. | B:1183 |

| `verifyInstaller` | Arquivo baixado e versão | null ou mensagem de erro | Obtém assinatura da versão, calcula SHA-512 do instalador e verifica com chave pública de release. É verificação própria, distinta de Authenticode. | B:1186 |

| `startAutoUpdate` | Ambiente de distribuição | Eventos e registro de handler | Em instalação suportada configura electron-updater, baixa atualizações, verifica assinatura e só disponibiliza instalação quando pronta; não instala automaticamente ao fechar. | B:1202 |

| `requireAuth` | Função de handler | Nova função assíncrona | Wrapper que rejeita chamada sem currentUser e encaminha argumentos quando autenticado. Não substitui as outras validações do handler. | B:1289 |


## Janelas e scaling

| Função | Entrada | Saída | O que faz e efeito | Evidência |
|---|---|---|---|---|

| `openScalingWindow` | Estado lsProcess e caminho do auxiliar | Inicia ou tenta focar processo WPF | Inicia GDZOptimizer.exe separado com spawn detached/unref; se já ativo tenta focar a janela por PowerShell/Win32. Depois tenta renomear o título para GDZ Scaling. Não carrega gdz-scaling.html; abrir UI não ativa por si só a geração de quadros. | B:393 |

| `splitProfile` | Texto XML | head/body/tail ou null | Separa o primeiro bloco Profile e preserva o restante do XML; não é um parser XML geral. | B:488 |

| `scalingRead` | Settings.xml do auxiliar | Dicionário de opções ou null | Lê tags permitidas do perfil e configurações globais. Apenas leitura; exige arquivo e perfil no formato esperado. | B:498 |

| `scalingWrite` | Dicionário de opções | written/skipped ou erro | Valida enums, limita números, aceita booleanos, altera tags e tenta salvar cópia .gdz-original na primeira escrita; grava Settings.xml. | B:519 |

| `scalingRecoverIfCorrupted` | Arquivos de configuração do auxiliar | Booleano | Se houver marcador de corrupção e cópia .gdz-original, repõe configuração e remove CorruptedSettingsBackup.xml. | B:579 |

| `scalingFromUi` | Objeto da interface | Opções em nomes do XML | Converte FG, API, escala, fila, sharpness e flags para tags esperadas; flowScale vira percentual e sharpness é dividido por dez. | B:619 |

| `scalingRecommended` | Capacidades e tela principal | Preset de scaling | Recomenda LSFG3 fixo 2x, alvo limitado a 30–480 Hz, flowScale por resolução, DXGI e fila 1; não mede qual preset é mais rápido no jogo. | B:637 |

| `readHWIDCached` | Nenhuma | Promise de identificação | Compartilha uma leitura de identificação entre chamadas; limpa cache da Promise quando não obtém resultado para permitir nova tentativa. | B:714 |

| `readHWID` | Quantidade máxima de tentativas, padrão 3 | id/board ou null | Executa script de identificação com timeout e intervalo entre tentativas; valida formato mínimo. Não se leu a identificação real nesta análise. | B:727 |

| `isRealHWID` | Texto de identificação | Booleano | Verifica tipo, comprimento mínimo e ausência de prefixo de fallback; não confirma unicidade física do identificador. | B:742 |

| `execPS` | Texto PowerShell e timeout | Promise de stdout ou erro | Codifica UTF-16LE/base64 e usa powershell oculto, sem perfil e não interativo; limite de stdout de 8 MiB. Esta função do produto não foi chamada. | B:747 |

| `runPS` | Texto PowerShell e timeout | Promise de stdout | Enfileira execPS pela Promise compartilhada; continua aceitando tarefas após falha da anterior. Serializa comandos que usam essa fila. | B:785 |

| `initialWindowSize` | Área útil da tela principal | width/height | Calcula 80% da largura e 85% da altura, com mínimos desejados e limite à área disponível. | B:793 |

| `applyUiZoom` | Tamanho do conteúdo da janela | Efeito na escala da UI | Calcula zoom a partir das dimensões-base, limitado entre 1 e 2,5 e quantizado em passos de 0,05. | B:800 |

| `createWindow` | Ambiente Electron | Janela principal e listeners | Cria janela sem moldura, instala preload, inicia em login, ajusta zoom, sincroniza plano no foco e restringe navegação externa. | B:806 |


## Inventário, memória e capacidades

| Função | Entrada | Saída | O que faz e efeito | Evidência |
|---|---|---|---|---|

| `readStats` | Nenhuma | cpu/ram/gpu | Consulta CPU e RAM via CIM/WMI e soma engines GPU 3D; limita GPU a 99. Em erro devolve zeros, que portanto podem significar falha de leitura. | B:1446 |

| `smbiosMemory` | Buffer SMBIOS | Lista de dispositivos de memória | Percorre estruturas e strings, decodifica tipo 17, tamanho estendido, slots, fabricante e velocidade; interrompe ao encontrar fim/estrutura inválida. | B:1466 |

| `memMaker` | Identificador de fabricante e part number | Nome de fabricante | Consulta tabelas de IDs e padrões; retorna vazio para certos códigos não reconhecidos. É normalização, não identificação por SPD direto. | B:1548 |

| `memSticksFrom` | Módulos brutos e quantidade de slots | Módulos/slots para UI | Formata rótulos, capacidade, geração e velocidade, inclui vagas vazias e limita apresentação; não mede canais físicos. | B:1556 |

| `memSticks` | Cache/estado interno | Última lista disponível ou null | Inicia coleta assíncrona SMBIOS, cai para CIM se necessário, tenta no máximo três vezes e devolve o cache sem aguardar a leitura nova. | B:1630 |

| `standbyBytes` | Cache interno de standby | Último número de bytes ou null | Atualiza em segundo plano a cada pelo menos 8 s, evita concorrência e interrompe novas leituras após três falhas; não limpa RAM. | B:1662 |

| `safeRes` | Valor e fallback | Inteiro válido ou fallback | Aceita resolução inteira de 320 a 16000; valida dimensão, não verifica se o monitor suporta o modo. | B:1697 |

| `buildCaps` | Inventário bruto | Objeto de capacidades | Classifica CPU, X3D de dois CCDs, Intel híbrido, GPUs por IDs PCI, OBS/Steam/NICs e build; normaliza caminhos e propriedades aceitas. | B:1993 |

| `getCaps` | Cache de capacidades | Promise de CAPS | Executa CAPS_PS uma vez, analisa marcador JSON e normaliza com buildCaps; falha permite tentativa posterior. | B:2053 |

| `blockedActions` | Capacidades | Mapa ação → motivo | Bloqueia opções incompatíveis segundo regras do app, incluindo X3D de dois CCDs, Windows antigo e ausência de OBS; acrescenta bloqueios dos tweaks. | B:2078 |

| `encoderCheck` | Capacidades | level/title/lines | Produz instruções textuais de encoder OBS por fabricante da GPU. Não altera configuração OBS e não mede upload. | B:2180 |

| `runCheck` | ID de verificação | level/title/lines | Executa script de verificação permitido ou gera dicas de encoder; limita títulos/linhas da resposta e não aceita nomes fora do catálogo. | B:2201 |

| `measureBottleneck` | Segundos e PID opcional | Amostras agregadas ou erro | Limita duração a 10–120 s, coleta com foco, exige seis amostras; devolve médias de CPU/coreMax/GPU e máximo de RAM, além de duração e interrupções. Diagnóstico textual final fica no renderer. | B:2279 |

| `findGameProcess` | Lista de PIDs a ignorar | Candidato a jogo ou null | Lista janelas com GPU, prioriza executáveis conhecidos e depois desconhecidos com uso de GPU de pelo menos 10; é heurística de seleção. | B:2362 |


## Jogos e automação

| Função | Entrada | Saída | O que faz e efeito | Evidência |
|---|---|---|---|---|

| `libGame` | Registro bruto de biblioteca | Objeto de jogo ou null | Aceita Steam/Epic, limpa nome/chave/caminhos e descarta executáveis genéricos; prepara IDs para catálogo dinâmico. | B:2489 |

| `scanGames` | Flag force | Promise do mapa de instalações | Executa descoberta de lojas/caminhos, valida executáveis, incorpora biblioteca e resolve nomes de processos ambíguos; mantém cache de varredura. | B:2588 |

| `steamCoverFile` | Steam App ID | Arquivo e formato da capa ou null | Procura capas no cache local Steam, inclusive layouts antigos. Não baixa capas nessa função. | B:2688 |

| `gameCover` | Jogo e resultado do scan | Imagem data URI ou null | Usa capa Steam redimensionada ou ícone do executável, converte a imagem e guarda cache. Apenas apresentação. | B:2721 |

| `runningProcesses` | Nenhuma | Promise de Set de nomes | Executa tasklist em CSV, normaliza nomes para minúsculas; erro resulta em conjunto vazio. | B:2756 |

| `runningGameIds` | Set de processos | Lista de IDs de jogos | Cruza nomes em execução com padrões dos jogos conhecidos e da biblioteca. | B:2796 |

| `gpuActionGame` | Nome da ação | Objeto de jogo ou null | Reconhece ação específica de preferência de GPU por jogo; não aplica a preferência. | B:2802 |

| `gpuBlocked` | Jogo e capacidades | Motivo de bloqueio ou null | Exige GPU integrada mais dedicada e executável detectado antes de oferecer preferência de alto desempenho por jogo. | B:2809 |

| `cleanScript` | Objeto de jogo | Texto PowerShell ou null | Gera limpeza de caches FiveM ou shadercache Steam; FiveM tenta preservar cache/game. Geração de texto não é execução nem backup de arquivos. | B:2827 |

| `loadSettings` | Arquivo de preferências | Objeto ou vazio | Lê JSON e aceita objeto; falhas retornam vazio. Não valida individualmente todas as preferências nessa função. | B:2838 |

| `saveSettings` | settings em memória | Sem retorno útil | Grava JSON em .tmp e renomeia para o arquivo final; erros são ignorados pelo corpo. | B:2849 |

| `cleanAutoState` | Estado persistido bruto | game/scheme/killed ou null | Filtra ID do jogo, GUID de plano e até dez caminhos de executáveis; prepara estado para restauração do modo automático. | B:2861 |

| `autoTick` | Processos e settings | Promise; altera estado automático | Quando jogo aparece gera/aplica autoStartScript e persiste estado anterior; quando sai ou modo desliga chama autoStopScript. Protege contra execução concorrente. | B:2870 |

| `cleanRam` | Flag de execução automática | success/freedMB/message ou erro | Compila helper C# em PowerShell, solicita privilégio e purga standby via NtSetSystemInformation; compara bytes antes/depois. Não instala RAM nem comprova FPS maior. | B:2964 |

| `ramStatus` | Settings e ramLast | Estado da limpeza automática | Entrega intervalos aceitos, última execução, bytes/MB e próxima data prevista; não realiza a limpeza. | B:3030 |


## Tweaks, snapshots e restauração

| Função | Entrada | Saída | O que faz e efeito | Evidência |
|---|---|---|---|---|

| `detectActions` | Lista de ações, padrão todas detectáveis | Mapa de booleanos | Monta sondas PowerShell de leitura a partir de DETECT; extrai GDZ_DETECT e falha se resposta vazia. | B:3128 |

| `tweakRegScript` | Tweak, ligar/desligar e capacidades | Texto PowerShell | Gera criação/alteração/remoção de valores do Registro com tipos e valores on/off declarados; por si só não executa. | B:3574 |

| `tweakSvcScript` | Tweak, ligar/desligar e capacidades | Texto PowerShell | Gera Set-Service StartupType segundo estados on/off. Não significa que cada serviço esteja instalado ou aceite a alteração. | B:3583 |

| `tweakDrsScript` | Tweak, ligar/desligar e capacidades | Texto PowerShell | Gera Set/Del de opções NVIDIA DRS, com inicialização NVAPI e mensagem de falha; não implementa um driver gráfico. | B:3586 |

| `tweakBlocks` | Capacidades | Mapa de ações bloqueadas | Percorre os predicados block do catálogo e converte IDs em nomes de ação usados pelo backend. | B:3599 |

| `tweakProbeExpr` | Tweak e capacidades | Expressão PowerShell booleana | Usa probe explícito ou compara valores de Registro, modo de serviço e DRS; combina condições com AND. | B:3613 |

| `tweakStates` | Subconjunto de tweaks | states e DNS lido | Executa sondas em lote e lê NameServer de adaptadores ativos; inclui código DRS somente se necessário. | B:3618 |

| `dnsProviderOf` | Lista de servidores DNS | auto/custom ou provedor conhecido | Compara o primeiro servidor não vazio com a primeira entrada dos provedores; não mede latência nem compara todos os adaptadores. | B:3653 |

| `runTweak` | Tweak e flag on | error/message/on | Executa script do tweak e depois lê o estado com sua probe; retorna booleano observado. Efeitos dependem da definição de cada tweak. | B:3730 |

| `specFor` | Nome da ação | Especificação de snapshot ou null | Seleciona SPECS, preferência GPU por jogo ou especificação genérica de otimização; não captura estado por si só. | B:3942 |

| `sanitize` | Item bruto e especificação permitida | Item validado ou null | Confere identidade, tipos/faixas e formatos antes de permitir usar dados de backup em operações de Registro/serviços/energia/rede/DRS. | B:4070 |

| `itemId` | Item de snapshot | Identificador normalizado | Produz chave por tipo e destino para identificar sobreposição entre ações e comparar snapshots. | B:4142 |

| `loadBackups` | Arquivo de backups | seq/entries | Aceita estrutura e ações reconhecidas com sequência e lista de itens; validações item a item também ocorrem depois em sanitize. | B:4163 |

| `saveBackups` | backups em memória | Sem retorno útil ou exceção | Grava .tmp e renomeia; diferentemente de saveSettings, não esconde a exceção nesse corpo. | B:4183 |

| `takeSnapshot` | Lista de especificações | Itens validados | Executa captura do estado atual, analisa marcador JSON e filtra cada item por sanitize; ausência de marcador causa erro. | B:4190 |

| `restoreItems` | Itens de snapshot | error/message | Gera script RESTORE_PS com dados base64/JSON e código DRS quando necessário; aplica estados antigos, não recupera arquivos apagados pela limpeza. | B:4197 |

| `runExtra` | Nome da ação | error/message | Executa REVERT_EXTRA quando a ação requer complemento à restauração, por exemplo ajustes fora do snapshot padrão. | B:4204 |

| `revertAction` | Nome da ação | success/message ou erro | Usa backup ou fallback REVERT. Se uma ação posterior compartilha itens, transfere o estado anterior para seu backup em vez de sobrescrever imediatamente; restaura itens restantes e salva histórico. | B:4213 |

| `restoreAll` | Backups conhecidos | success/count/message ou erro | Ordena backups por sequência, deduplica itens para recuperar estado mais antigo e restaura em lote; limpa registros após sucesso e executa complementos. O campo error retornado por runExtra é ignorado nesse fluxo; exceções/rejeições ainda podem propagar. | B:4276 |

| `withActionLock` | Callback de operação | Promise da operação | Serializa operações mutáveis por fila de Promises e mantém fila utilizável depois de erro; não é bloqueio entre processos independentes. | B:4312 |

| `psResult` | Stdout textual | error/message | Reconhece marcadores GDZ_ERRO e GDZ_AVISO. Ausência de marcador não constitui verificação universal de todas as escritas. | B:4316 |

| `sameItem` | Dois itens de snapshot | Booleano | Compara existência, tipo e valor conforme reg/svc/scheme/pwr/bcd/tcp/dns/drs; para serviço compara modo/delayed, sem comparar running. | B:4439 |

| `actionStates` | Backups e sondas do Windows | Mapa ação → ativo | Combina detecção e comparação antes/depois para distinguir ajustes revertidos; verifica 720p pela dimensão física da tela principal. | B:4491 |


## Inicialização, arquivos e programas

| Função | Entrada | Saída | O que faz e efeito | Evidência |
|---|---|---|---|---|

| `autostartRegister` | Flag de preservar tarefa compatível | Promise booleana | Cria tarefa de logon do usuário para abrir o GDZ com RunLevel Highest, permitindo bateria e sem limite de três dias; não é o mesmo mecanismo HKCU Run do auxiliar Scaling. | B:4605 |

| `autostartRemove` | Nenhuma | Promise booleana | Remove tarefa de inicialização do GDZ e confere se desapareceu; não altera entradas de outros apps. | B:4608 |

| `autostartIsOn` | Nenhuma | Promise booleana | Consulta presença/estado não Disabled da tarefa de inicialização; não confirma que a próxima abertura ocorrerá sem falhas. | B:4611 |

| `autostartSync` | Distribuição e preferência autoStart | Promise sem retorno útil | Registra/reconcilia inicialização quando suportada, exceto se usuário desativou explicitamente; salva preferência após sucesso. | B:4615 |

| `listStartup` | Fontes de Registro permitidas | Lista nome/escopo/comando/enabled | Lê Run e StartupApproved; interpreta primeiro byte ímpar como desativado, filtra comandos e ordena por nome. Não cobre todas as tarefas/serviços de inicialização do Windows. | B:4656 |

| `tempCleanableBytes` | TEMP e C:\Windows\Temp | Número de bytes | Soma itens cujo LastWriteTime de topo tem mais de um dia; enumera conteúdo de diretórios selecionados. Estimativa de armazenamento, sem remoção. | B:4669 |

| `diskPS` | Código DISK_CS do app | Trecho PowerShell de carregamento | Cria fonte C# com nome derivado de hash em userData e gera compilação/carregamento da DLL de medição/limpeza. A chamada real pode escrever arquivos auxiliares. | B:4929 |

| `execPSFile` | Script e timeout | Promise de stdout | Grava .ps1 temporário com BOM, inicia PowerShell oculto com -File, limita stdout a 16 MiB e agenda remoção do temporário no callback. | B:4939 |

| `runPSFile` | Script e timeout | Promise de stdout | Enfileira execPSFile na mesma fila usada por runPS; evita concorrência entre essas operações. | B:5006 |

| `cleanScanScript` | Catálogo de categorias | Texto PowerShell de varredura | Monta medição de bytes/arquivos, existência, idade e aplicativos abertos por categoria; trata Lixeira separadamente. | B:5259 |

| `cleanRunScript` | Categorias selecionadas | Texto PowerShell de limpeza | Monta limpeza com bloqueio de apps abertos, idade e contagem de falhas; Lixeira usa Clear-RecycleBin e Delivery Optimization tem operação específica. | B:5280 |

| `diskScanStop` | Processo de varredura atual | Sem retorno útil | Encerra worker de ocupação de disco e limpa referência; não encerra processos arbitrários do usuário. | B:5373 |

| `listDrives` | Nenhuma | Lista de discos fixos prontos | Enumera letra, rótulo, total e espaço livre por DriveInfo, valida letras e normaliza números; somente leitura. | B:5380 |

| `parseUninstall` | UninstallString e metadados MSI | Executável e argumentos | Expande variáveis, reconhece MSI e converte /I para /X; separa caminho citado ou tenta localizar .exe em comando sem aspas. | B:5599 |

| `uninstallExe` | Registro de aplicativo | Caminho do desinstalador ou vazio | Extrai executável absoluto pelo parser, excluindo executores genéricos msiexec/rundll32 para não tratar pasta de sistema como pasta do app. | B:5655 |

| `protectedDirs` | Ambiente do processo | Lista de pastas protegidas | Lista raízes de Windows/programas/dados/usuário e pasta do próprio GDZ; auxilia o filtro de sobras, sem apagar nada. | B:5664 |

| `safeAppDir` | Pasta candidata e pastas de outros apps | Booleano | Rejeita caminhos não absolutos/curtos, raízes/ancestrais protegidos, árvores de sistema e sobreposição com outros apps. É filtro de caminho, não prova de propriedade de todos os arquivos. | B:5674 |

| `listApps` | Registro Uninstall | Lista normalizada e cache | Executa inventário, valida chave, elimina nome/versão duplicados e formata data/tamanho/local; não desinstala por si só. | B:5684 |

| `nameVariants` | Nome de aplicativo | Variações de nome | Remove versão/arquitetura, produz versão sem espaços e descarta nomes genéricos/inválidos; base para procurar sobras, não prova de autoria de pasta. | B:5877 |

| `pubVariants` | Publicador | Variações de publicador | Remove sufixos empresariais comuns e rejeita caracteres de caminho; auxilia heurística de sobras. | B:5881 |


## Sensores e informações ao vivo

| Função | Entrada | Saída | O que faz e efeito | Evidência |
|---|---|---|---|---|

| `cpuTimes` | os.cpus | idle/total acumulados | Soma tempos de todos os processadores lógicos; diferença entre amostras permite calcular utilização. | B:6293 |

| `lineReader` | Processo filho e callback | Callbacks por linha completa | Acumula stdout parcial e separa quebras de linha; preserva fragmento incompleto para próximo chunk. | B:6304 |

| `startNvidiaSmi` | Caminhos conhecidos | Booleano de início | Inicia consulta contínua de uso/temperatura GPU a cada 1000 ms, atualiza sensors e limpa campos quando processo sai. | B:6314 |

| `startSensorPs` | Necessidade de GPU fallback | Worker e atualizações de sensors | Inicia PowerShell de sensores; converte zona térmica de décimos de Kelvin e armazena como cpuTemp se na faixa. Origem de zona térmica não prova temperatura do die da CPU. | B:6343 |

| `sensorsStart` | Campos do overlay | Timer e workers sob demanda | Calcula CPU por deltas a cada segundo, inicia NVIDIA/fallback somente quando dados são necessários e limita tentativas de respawn. | B:6386 |

| `sensorsStop` | Estado sensors | Sem retorno útil | Limpa timer, encerra apenas workers próprios e zera sensores; não interrompe outras aplicações. | B:6448 |

| `sensorsSnapshot` | Últimos sensores e memória do SO | cpu/gpu/temp/ramUsed/ramTotal | Monta objeto usado pelo overlay; valores ausentes continuam null. RAM é calculada por totalmem/freemem. | B:6460 |

| `nvidiaSmiExe` | Lista de caminhos conhecidos | Caminho existente ou null | Seleciona nvidia-smi disponível no disco; não o inicia nem atualiza driver. | B:6619 |

| `buildPcInfo` | JSON bruto de inventário e dados NVIDIA | Modelo completo para renderer | Limpa textos, classifica GPU/discos, cruza monitores, normaliza hardware/Windows/bateria e calcula bootAt. Não mede tudo diretamente nessa função. | B:6622 |

| `nvidiaStatic` | nvidia-smi disponível | Promise de lista ou vazia | Consulta nome/driver/VBIOS/PCIe/power limit com timeout de seis segundos; não altera limite de potência. | B:6786 |

| `pcLiveSpawn` | Estado pcLive | Workers e cache ao vivo | Inicia nvidia-smi de 2 s e PowerShell de 1,5 s; interpreta clocks, VRAM/engines por LUID e temperatura de disco; conversão térmica tem o limite descrito acima. | B:6893 |

| `pcLiveStart` | Uso recente de pcinfo-live | Timer periódico | Calcula uso total/por núcleo por deltas, reinicia workers necessários e para após período de inatividade do consumidor. | B:7172 |

| `pcLiveStop` | Workers pcLive | Sem retorno útil | Cancela timer, encerra processos próprios e apaga caches temporários de CPU/GPU/disco. | B:7197 |


## FPS, overlay e telemetria

| Função | Entrada | Saída | O que faz e efeito | Evidência |
|---|---|---|---|---|

| `fpsHandleLine` | Uma linha CSV do PresentMon | Atualização de buffers por PID | Reconhece cabeçalho, aceita intervalos entre presents estritamente maiores que 0 e menores que 1000 ms, ignora processos configurados e mantém janela móvel/gravação; não mede input lag. | B:7248 |

| `fpsStats` | Array de frametimes em ms | fps/low1/low01/frametime/frames | FPS médio = N*1000/soma(ms). Lows são inverso da média dos 1% e 0,1% frametimes mais lentos, com pelo menos um frame; não são percentis simples. | B:7310 |

| `fpsPrune` | Mapa de processos medidos | Mapa modificado | Remove processos sem frames recentes por mais de três segundos. | B:7328 |

| `fpsTarget` | PID fixado e processos medidos | PID escolhido ou null | Prefere PID fixado; senão pontua jogo conhecido, independent flip e número de frames. Não prova que o escolhido esteja em primeiro plano. | B:7332 |

| `fpsLive` | PID | Métricas atuais ou null | Usa aproximadamente 1 s para FPS atual, janela inteira para low e 3 s para comparar com baseline do mesmo nome; devolve ganho percentual sem comprovar causalidade. | B:7345 |

| `fpsMarkBase` | Alvo atual e últimos cinco segundos | success/base ou erro | Exige pelo menos 30 frames e guarda média/low do alvo como baseline em memória. | B:7375 |

| `fpsRegisterHotkey` | Atalho declarado | Estado de registro | Registra atalho global: inicia medidor se parado, ou marca baseline se já ativo; não envia tecla a jogos. | B:7402 |

| `fpsStart` | Estado do medidor | Promise booleana | Inicia PresentMon com stdout CSV, sessão própria, sem rastrear input/GPU e excluindo dwm; interpreta erros e reutiliza início em andamento. | B:7426 |

| `fpsStop` | Captura ativa | Sem retorno útil | Encerra processo próprio, limpa buffers e solicita término da sessão PresentMon nomeada. Não altera gráficos do jogo. | B:7498 |

| `overlayBounds` | Canto 0–3 | Retângulo de posição/tamanho | Calcula posição na tela principal com margens; não segue automaticamente o monitor em que o jogo está. | B:7524 |

| `overlayOpen` | Preferência de overlay | Janela transparente e timer | Cria janela sem foco, sempre no topo e com clique atravessando; atualiza métricas a cada 500 ms e mantém medição/sensores ativos. | B:7535 |

| `overlayClose` | Overlay existente | Sem retorno útil | Cancela timer, interrompe sensores próprios e fecha a janela; fpsStop é uma operação separada. | B:7586 |

| `loadTelemetryQueue` | Arquivo de fila | Array ou vazio | Lê eventos JSON persistidos; não envia nada e não foi executada nesta análise. | B:7638 |

| `saveTelemetryQueue` | Array de eventos | Gravação local | Mantém somente os 500 últimos eventos no arquivo; erros de escrita são ignorados nesse corpo. | B:7646 |

| `fpsTelemetryItem` | Slot a/b e sessão gravada | Evento de métricas | Classifica before/after/manual, associa pairId quando elegível e inclui métricas, jogo, versão e nomes das otimizações registradas. | B:7653 |

| `enqueueFpsTelemetry` | Slot e sessão | Fila eventualmente gravada | Gera evento e só enfileira FPS quando consentimento está ativo, média ≥1 e pelo menos 30 frames. | B:7680 |

| `hardwareDetails` | Capacidades do Windows | Promise de detalhes normalizados | Consulta CPU/VRAM/driver/RAM/disco/placa-mãe/Windows/plano por PowerShell e armazena Promise de cache; estes dados compõem snapshot de hardware. | B:7688 |

| `displayDetails` | Telas Electron | Resolução/Hz/quantidade | Lê tela principal em pixels físicos e número de monitores; erro retorna objeto vazio. | B:7777 |

| `hardwareSnapshot` | Capacidades, hardware e tela | Objeto de hardware ou null | Combina fabricante/modelos, flags, RAM, build, monitores e detalhes. Não lê histórico de navegação ou conteúdo de documentos nesse corpo. | B:7794 |

| `flushTelemetry` | Sessão, consentimento e filas | POSTs e atualização local | Quando autenticado online envia FPS consentido e hardware se hash mudou. A condição de hardware independe de telemetryOn; só marca hash após HTTP 200, e descarta lote inválido em HTTP 400. | B:7817 |


## OptiScaler e FiveM

| Função | Entrada | Saída | O que faz e efeito | Evidência |
|---|---|---|---|---|

| `httpGetTo` | URL, destino/callback opcionais, contador redirect | Promise de texto ou arquivo | Baixa HTTPS com timeout, até cinco redirecionamentos e progresso; sem destino acumula texto. Não valida assinatura por si só. | B:8090 |

| `walkFiles` | Raiz, subcaminho e acumulador | Lista de caminhos relativos | Enumera recursivamente arquivos do pacote local; não copia arquivos nem executa DLLs. | B:8189 |

| `optiLocalPkg` | Cache OptiScaler | tag/dir ou null | Seleciona versão em pasta com marcador .ok, ordenando tags numericamente; marcador significa extração concluída pelo app, não verificação criptográfica. | B:8198 |

| `optiDownload` | Estado global opti | Progresso/erro e pacote em cache | Consulta release, baixa .7z, extrai com 7za e verifica presença de DLL/INI; usa fallback se consulta falhar. Não instala no jogo nessa função. | B:8214 |

| `optiManifest` | Pasta do jogo | Manifesto JSON ou null | Lê registro de arquivos/backups criado pela instalação GDZ para permitir atualização/remoção. | B:8270 |

| `exeRunning` | Caminho do executável | Promise booleana | Consulta processos por caminho; falha devolve false, portanto não é garantia absoluta de que o jogo esteja fechado. | B:8279 |

| `optiAnalyze` | Caminho .exe | Diagnóstico de instalação | Valida arquivo, procura executável real Unreal, upscalers e sinais de anti-cheat com profundidade/itens limitados; informa instalação prévia ou externa, sem escrever no jogo. | B:8289 |

| `optiIniValues` | Opções, capacidades e executável | Seções/chaves de INI | Converte UI para upscalers DX11/DX12/Vulkan, FG, spoofing e menu; leva peculiaridades do executável em conta. Não implementa o algoritmo DLSS/FSR. | B:8372 |

| `optiWriteIni` | Arquivo INI e valores por seção | Arquivo atualizado | Substitui chaves existentes e acrescenta faltantes preservando outras linhas; grava texto com CRLF. | B:8406 |

| `optiSanitizeOpts` | Opções recebidas | Opções aceitas com defaults | Aceita apenas enums de upscaler/FG/menuKey e flags booleanas; descarta valores arbitrários fora do contrato. | B:8460 |

| `optiRemoveFiles` | Diretório e manifesto | Lista de falhas | Remove arquivos instalados/logs, repõe backups e tenta apagar diretórios criados; só remove pasta de controle se não houver falhas. Pode alterar arquivos reais do jogo. | B:8470 |

| `optiInstall` | exe, dll, opts, acceptAntiCheat | success/manifest ou erro | Analisa destino, bloqueia jogos conhecidos, exige fechamento/aceite quando aplicável, baixa pacote se necessário, faz backups, copia arquivos, grava INI e tenta rollback em erro. | B:8527 |

| `optiUninstall` | Diretório do jogo | success ou erro | Lê manifesto, recusa jogo em execução, remove arquivos e repõe backups; limpa registro da instalação após concluir. | B:8629 |

| `xmlGet` | Texto XML e tag | Valor de atributo ou null | Lê o atributo value de tag via expressão regular; não suporta XML arbitrário ou validação de schema. | B:8851 |

| `xmlSet` | Texto XML, tag e novo valor | Texto alterado ou original | Substitui atributo value de tag existente; não cria tag ausente e não grava arquivo por si só. | B:8855 |

| `fivemRunning` | Processos em execução | Promise booleana | Reconhece FiveM.exe e processos GTAProcess do FiveM para evitar alteração enquanto o jogo está aberto. | B:8859 |


## Como ler os limites

Ver uma rotina no código confirma sua definição e dependências, mas não demonstra que foi chamada nesta sessão, que Windows/drivers aceitam todos os comandos ou que ela melhora FPS. As operações que geram scripts só os executam quando outro fluxo chama runPS/execPS/runPSFile. A análise não fez essas chamadas.

O inventário sintático completo está em `artifacts/private/gdz-analysis/deep/function-index.json`, incluindo todos os callbacks e wrappers das 21 fontes próprias. Funções de bibliotecas de terceiros em node_modules não são recursos autorais do produto e não compõem este catálogo. O motor gráfico nativo tem limites separados no [catálogo de Scaling](GDZ-SCALING-FUNCOES.md).
