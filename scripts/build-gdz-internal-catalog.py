#!/usr/bin/env python3
"""Build the human-reviewed GDZ function catalog; never execute target code."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEEP = ROOT / "artifacts/private/gdz-analysis/deep"

# Human interpretation of the inspected bodies. Inputs have descriptive names;
# the original obfuscated parameter names remain available in the JSON evidence.
NOTES = r"""
# Caminhos, autenticação e comunicação
getBinPath|Segmentos de caminho|Caminho em bin|No pacote troca app.asar por app.asar.unpacked; em desenvolvimento usa bin ao lado da entrada. Não inicia o arquivo.
getSrcPath|Segmentos de caminho|Caminho em src|Resolve recursos HTML/CSS/JS dentro da distribuição, sem abrir janelas por si só.
verifyEntitlement|Documento de autorização assinado|Claims válidas ou null|Separa payload e assinatura base64url, verifica assinatura criptográfica e formato v/uid; rejeita conteúdo inválido. Não concede acesso por nome de plano presumido.
loadAuth|Arquivo de sessão do app|Objeto com token ou null|Lê e descriptografa pelo safeStorage do Electron, aceita objeto com token textual; falhas resultam em null. O arquivo real não foi aberto nesta análise.
saveAuth|Objeto de sessão|Booleano de gravação|Serializa, criptografa com safeStorage e grava authPath; retorna false se criptografia indisponível ou houver erro.
clearAuth|Nenhuma|Sem retorno útil|Apaga o arquivo de sessão se existir; ignora falhas. Não foi chamada na análise.
removeLegacyAuthFiles|Nenhuma|Sem retorno útil|Remove accounts.json, session.json e license.bin antigos do diretório userData; não representa limpeza geral do perfil.
apiCall|Método, rota, body/token/timeout opcionais|Objeto offline/status/data|Faz requisição JSON com net.fetch, versão no user-agent, Bearer quando fornecido e AbortSignal; erros de transporte viram offline=true.
userFromClaims|Claims verificadas e flag offline|Modelo de usuário|Monta IDs, nome derivado do e-mail, plano, recursos, expiração e condição offline. Transformação de dados, sem escrita direta no Windows.
acceptEntitlement|Token, autorização assinada e offline|Usuário ou null|Verifica documento, persiste sessão criptografada com maior horário observado e atualiza currentUser. Não foi executada.
hasFeature|Identificador de recurso|Booleano|Consulta a lista features do usuário e o curinga declarado; não avalia hardware ou eficácia de otimização.
refreshEntitlement|Sessão persistida|Resultado ok/offline/revoked/message|Atualiza autorização pelo servidor; trata revogação e fallback offline com documento assinado, prazo e verificação de relógio.
channelAllowed|Canal IPC e argumentos|Booleano|Decide acesso por lista de canais sempre disponíveis, usuário e recursos contratados; run-action tem regra específica para otimização rápida.
appAuth|Rota de autenticação, formulário e mensagem de erro|success/user ou erro|Obtém identificação do PC, acrescenta board/hostname/versão/Windows, envia POST e aceita autorização assinada; agenda envio de informações após sucesso.
forceLogout|Motivo|Sem retorno útil|Limpa currentUser e sessão persistida, fecha overlay, guarda motivo e retorna a janela para login.
loadOffers|Flag de atualização forçada|Planos/ofertas ou null|Busca ofertas autenticadas e usa cache por usuário e TTL; se a rede falhar pode devolver cache anterior.
flushPlanEvents|Fila em memória|Promise sem dados de UI|Retira até 30 eventos e envia para /app/events se houver sessão. O lote é removido da fila antes da resposta; não há reencaminhamento nesse corpo.
syncPlan|Estado atual|Promise sem retorno de UI|Evita chamadas concorrentes, atualiza autorização, força logout se revogada e avisa renderer quando plano/recursos mudam.
syncPlanOnFocus|Evento de foco|Sem retorno útil|Solicita sincronização somente depois do intervalo mínimo; não faz ajuste de desempenho ao receber foco.
sendUpdateState|updateState em memória|Evento update-status|Envia estado de atualização à janela existente; não baixa nem instala por si só.
verifyInstaller|Arquivo baixado e versão|null ou mensagem de erro|Obtém assinatura da versão, calcula SHA-512 do instalador e verifica com chave pública de release. É verificação própria, distinta de Authenticode.
startAutoUpdate|Ambiente de distribuição|Eventos e registro de handler|Em instalação suportada configura electron-updater, baixa atualizações, verifica assinatura e só disponibiliza instalação quando pronta; não instala automaticamente ao fechar.
requireAuth|Função de handler|Nova função assíncrona|Wrapper que rejeita chamada sem currentUser e encaminha argumentos quando autenticado. Não substitui as outras validações do handler.
# Janelas e scaling
openScalingWindow|Estado lsProcess e caminho do auxiliar|Inicia ou tenta focar processo WPF|Inicia GDZOptimizer.exe separado com spawn detached/unref; se já ativo tenta focar a janela por PowerShell/Win32. Depois tenta renomear o título para GDZ Scaling. Não carrega gdz-scaling.html; abrir UI não ativa por si só a geração de quadros.
splitProfile|Texto XML|head/body/tail ou null|Separa o primeiro bloco Profile e preserva o restante do XML; não é um parser XML geral.
scalingRead|Settings.xml do auxiliar|Dicionário de opções ou null|Lê tags permitidas do perfil e configurações globais. Apenas leitura; exige arquivo e perfil no formato esperado.
scalingWrite|Dicionário de opções|written/skipped ou erro|Valida enums, limita números, aceita booleanos, altera tags e tenta salvar cópia .gdz-original na primeira escrita; grava Settings.xml.
scalingRecoverIfCorrupted|Arquivos de configuração do auxiliar|Booleano|Se houver marcador de corrupção e cópia .gdz-original, repõe configuração e remove CorruptedSettingsBackup.xml.
scalingFromUi|Objeto da interface|Opções em nomes do XML|Converte FG, API, escala, fila, sharpness e flags para tags esperadas; flowScale vira percentual e sharpness é dividido por dez.
scalingRecommended|Capacidades e tela principal|Preset de scaling|Recomenda LSFG3 fixo 2x, alvo limitado a 30–480 Hz, flowScale por resolução, DXGI e fila 1; não mede qual preset é mais rápido no jogo.
readHWIDCached|Nenhuma|Promise de identificação|Compartilha uma leitura de identificação entre chamadas; limpa cache da Promise quando não obtém resultado para permitir nova tentativa.
readHWID|Quantidade máxima de tentativas, padrão 3|id/board ou null|Executa script de identificação com timeout e intervalo entre tentativas; valida formato mínimo. Não se leu a identificação real nesta análise.
isRealHWID|Texto de identificação|Booleano|Verifica tipo, comprimento mínimo e ausência de prefixo de fallback; não confirma unicidade física do identificador.
execPS|Texto PowerShell e timeout|Promise de stdout ou erro|Codifica UTF-16LE/base64 e usa powershell oculto, sem perfil e não interativo; limite de stdout de 8 MiB. Esta função do produto não foi chamada.
runPS|Texto PowerShell e timeout|Promise de stdout|Enfileira execPS pela Promise compartilhada; continua aceitando tarefas após falha da anterior. Serializa comandos que usam essa fila.
initialWindowSize|Área útil da tela principal|width/height|Calcula 80% da largura e 85% da altura, com mínimos desejados e limite à área disponível.
applyUiZoom|Tamanho do conteúdo da janela|Efeito na escala da UI|Calcula zoom a partir das dimensões-base, limitado entre 1 e 2,5 e quantizado em passos de 0,05.
createWindow|Ambiente Electron|Janela principal e listeners|Cria janela sem moldura, instala preload, inicia em login, ajusta zoom, sincroniza plano no foco e restringe navegação externa.
# Inventário, memória e capacidades
readStats|Nenhuma|cpu/ram/gpu|Consulta CPU e RAM via CIM/WMI e soma engines GPU 3D; limita GPU a 99. Em erro devolve zeros, que portanto podem significar falha de leitura.
smbiosMemory|Buffer SMBIOS|Lista de dispositivos de memória|Percorre estruturas e strings, decodifica tipo 17, tamanho estendido, slots, fabricante e velocidade; interrompe ao encontrar fim/estrutura inválida.
memMaker|Identificador de fabricante e part number|Nome de fabricante|Consulta tabelas de IDs e padrões; retorna vazio para certos códigos não reconhecidos. É normalização, não identificação por SPD direto.
memSticksFrom|Módulos brutos e quantidade de slots|Módulos/slots para UI|Formata rótulos, capacidade, geração e velocidade, inclui vagas vazias e limita apresentação; não mede canais físicos.
memSticks|Cache/estado interno|Última lista disponível ou null|Inicia coleta assíncrona SMBIOS, cai para CIM se necessário, tenta no máximo três vezes e devolve o cache sem aguardar a leitura nova.
standbyBytes|Cache interno de standby|Último número de bytes ou null|Atualiza em segundo plano a cada pelo menos 8 s, evita concorrência e interrompe novas leituras após três falhas; não limpa RAM.
safeRes|Valor e fallback|Inteiro válido ou fallback|Aceita resolução inteira de 320 a 16000; valida dimensão, não verifica se o monitor suporta o modo.
buildCaps|Inventário bruto|Objeto de capacidades|Classifica CPU, X3D de dois CCDs, Intel híbrido, GPUs por IDs PCI, OBS/Steam/NICs e build; normaliza caminhos e propriedades aceitas.
getCaps|Cache de capacidades|Promise de CAPS|Executa CAPS_PS uma vez, analisa marcador JSON e normaliza com buildCaps; falha permite tentativa posterior.
blockedActions|Capacidades|Mapa ação → motivo|Bloqueia opções incompatíveis segundo regras do app, incluindo X3D de dois CCDs, Windows antigo e ausência de OBS; acrescenta bloqueios dos tweaks.
encoderCheck|Capacidades|level/title/lines|Produz instruções textuais de encoder OBS por fabricante da GPU. Não altera configuração OBS e não mede upload.
runCheck|ID de verificação|level/title/lines|Executa script de verificação permitido ou gera dicas de encoder; limita títulos/linhas da resposta e não aceita nomes fora do catálogo.
measureBottleneck|Segundos e PID opcional|Amostras agregadas ou erro|Limita duração a 10–120 s, coleta com foco, exige seis amostras; devolve médias de CPU/coreMax/GPU e máximo de RAM, além de duração e interrupções. Diagnóstico textual final fica no renderer.
findGameProcess|Lista de PIDs a ignorar|Candidato a jogo ou null|Lista janelas com GPU, prioriza executáveis conhecidos e depois desconhecidos com uso de GPU de pelo menos 10; é heurística de seleção.
# Jogos e automação
libGame|Registro bruto de biblioteca|Objeto de jogo ou null|Aceita Steam/Epic, limpa nome/chave/caminhos e descarta executáveis genéricos; prepara IDs para catálogo dinâmico.
scanGames|Flag force|Promise do mapa de instalações|Executa descoberta de lojas/caminhos, valida executáveis, incorpora biblioteca e resolve nomes de processos ambíguos; mantém cache de varredura.
steamCoverFile|Steam App ID|Arquivo e formato da capa ou null|Procura capas no cache local Steam, inclusive layouts antigos. Não baixa capas nessa função.
gameCover|Jogo e resultado do scan|Imagem data URI ou null|Usa capa Steam redimensionada ou ícone do executável, converte a imagem e guarda cache. Apenas apresentação.
runningProcesses|Nenhuma|Promise de Set de nomes|Executa tasklist em CSV, normaliza nomes para minúsculas; erro resulta em conjunto vazio.
runningGameIds|Set de processos|Lista de IDs de jogos|Cruza nomes em execução com padrões dos jogos conhecidos e da biblioteca.
gpuActionGame|Nome da ação|Objeto de jogo ou null|Reconhece ação específica de preferência de GPU por jogo; não aplica a preferência.
gpuBlocked|Jogo e capacidades|Motivo de bloqueio ou null|Exige GPU integrada mais dedicada e executável detectado antes de oferecer preferência de alto desempenho por jogo.
cleanScript|Objeto de jogo|Texto PowerShell ou null|Gera limpeza de caches FiveM ou shadercache Steam; FiveM tenta preservar cache/game. Geração de texto não é execução nem backup de arquivos.
loadSettings|Arquivo de preferências|Objeto ou vazio|Lê JSON e aceita objeto; falhas retornam vazio. Não valida individualmente todas as preferências nessa função.
saveSettings|settings em memória|Sem retorno útil|Grava JSON em .tmp e renomeia para o arquivo final; erros são ignorados pelo corpo.
cleanAutoState|Estado persistido bruto|game/scheme/killed ou null|Filtra ID do jogo, GUID de plano e até dez caminhos de executáveis; prepara estado para restauração do modo automático.
autoTick|Processos e settings|Promise; altera estado automático|Quando jogo aparece gera/aplica autoStartScript e persiste estado anterior; quando sai ou modo desliga chama autoStopScript. Protege contra execução concorrente.
cleanRam|Flag de execução automática|success/freedMB/message ou erro|Compila helper C# em PowerShell, solicita privilégio e purga standby via NtSetSystemInformation; compara bytes antes/depois. Não instala RAM nem comprova FPS maior.
ramStatus|Settings e ramLast|Estado da limpeza automática|Entrega intervalos aceitos, última execução, bytes/MB e próxima data prevista; não realiza a limpeza.
# Tweaks, snapshots e restauração
detectActions|Lista de ações, padrão todas detectáveis|Mapa de booleanos|Monta sondas PowerShell de leitura a partir de DETECT; extrai GDZ_DETECT e falha se resposta vazia.
tweakRegScript|Tweak, ligar/desligar e capacidades|Texto PowerShell|Gera criação/alteração/remoção de valores do Registro com tipos e valores on/off declarados; por si só não executa.
tweakSvcScript|Tweak, ligar/desligar e capacidades|Texto PowerShell|Gera Set-Service StartupType segundo estados on/off. Não significa que cada serviço esteja instalado ou aceite a alteração.
tweakDrsScript|Tweak, ligar/desligar e capacidades|Texto PowerShell|Gera Set/Del de opções NVIDIA DRS, com inicialização NVAPI e mensagem de falha; não implementa um driver gráfico.
tweakBlocks|Capacidades|Mapa de ações bloqueadas|Percorre os predicados block do catálogo e converte IDs em nomes de ação usados pelo backend.
tweakProbeExpr|Tweak e capacidades|Expressão PowerShell booleana|Usa probe explícito ou compara valores de Registro, modo de serviço e DRS; combina condições com AND.
tweakStates|Subconjunto de tweaks|states e DNS lido|Executa sondas em lote e lê NameServer de adaptadores ativos; inclui código DRS somente se necessário.
dnsProviderOf|Lista de servidores DNS|auto/custom ou provedor conhecido|Compara o primeiro servidor não vazio com a primeira entrada dos provedores; não mede latência nem compara todos os adaptadores.
runTweak|Tweak e flag on|error/message/on|Executa script do tweak e depois lê o estado com sua probe; retorna booleano observado. Efeitos dependem da definição de cada tweak.
specFor|Nome da ação|Especificação de snapshot ou null|Seleciona SPECS, preferência GPU por jogo ou especificação genérica de otimização; não captura estado por si só.
sanitize|Item bruto e especificação permitida|Item validado ou null|Confere identidade, tipos/faixas e formatos antes de permitir usar dados de backup em operações de Registro/serviços/energia/rede/DRS.
itemId|Item de snapshot|Identificador normalizado|Produz chave por tipo e destino para identificar sobreposição entre ações e comparar snapshots.
loadBackups|Arquivo de backups|seq/entries|Aceita estrutura e ações reconhecidas com sequência e lista de itens; validações item a item também ocorrem depois em sanitize.
saveBackups|backups em memória|Sem retorno útil ou exceção|Grava .tmp e renomeia; diferentemente de saveSettings, não esconde a exceção nesse corpo.
takeSnapshot|Lista de especificações|Itens validados|Executa captura do estado atual, analisa marcador JSON e filtra cada item por sanitize; ausência de marcador causa erro.
restoreItems|Itens de snapshot|error/message|Gera script RESTORE_PS com dados base64/JSON e código DRS quando necessário; aplica estados antigos, não recupera arquivos apagados pela limpeza.
runExtra|Nome da ação|error/message|Executa REVERT_EXTRA quando a ação requer complemento à restauração, por exemplo ajustes fora do snapshot padrão.
revertAction|Nome da ação|success/message ou erro|Usa backup ou fallback REVERT. Se uma ação posterior compartilha itens, transfere o estado anterior para seu backup em vez de sobrescrever imediatamente; restaura itens restantes e salva histórico.
restoreAll|Backups conhecidos|success/count/message ou erro|Ordena backups por sequência, deduplica itens para recuperar estado mais antigo e restaura em lote; limpa registros após sucesso e executa complementos. O campo error retornado por runExtra é ignorado nesse fluxo; exceções/rejeições ainda podem propagar.
withActionLock|Callback de operação|Promise da operação|Serializa operações mutáveis por fila de Promises e mantém fila utilizável depois de erro; não é bloqueio entre processos independentes.
psResult|Stdout textual|error/message|Reconhece marcadores GDZ_ERRO e GDZ_AVISO. Ausência de marcador não constitui verificação universal de todas as escritas.
sameItem|Dois itens de snapshot|Booleano|Compara existência, tipo e valor conforme reg/svc/scheme/pwr/bcd/tcp/dns/drs; para serviço compara modo/delayed, sem comparar running.
actionStates|Backups e sondas do Windows|Mapa ação → ativo|Combina detecção e comparação antes/depois para distinguir ajustes revertidos; verifica 720p pela dimensão física da tela principal.
# Inicialização, arquivos e programas
autostartRegister|Flag de preservar tarefa compatível|Promise booleana|Cria tarefa de logon do usuário para abrir o GDZ com RunLevel Highest, permitindo bateria e sem limite de três dias; não é o mesmo mecanismo HKCU Run do auxiliar Scaling.
autostartRemove|Nenhuma|Promise booleana|Remove tarefa de inicialização do GDZ e confere se desapareceu; não altera entradas de outros apps.
autostartIsOn|Nenhuma|Promise booleana|Consulta presença/estado não Disabled da tarefa de inicialização; não confirma que a próxima abertura ocorrerá sem falhas.
autostartSync|Distribuição e preferência autoStart|Promise sem retorno útil|Registra/reconcilia inicialização quando suportada, exceto se usuário desativou explicitamente; salva preferência após sucesso.
listStartup|Fontes de Registro permitidas|Lista nome/escopo/comando/enabled|Lê Run e StartupApproved; interpreta primeiro byte ímpar como desativado, filtra comandos e ordena por nome. Não cobre todas as tarefas/serviços de inicialização do Windows.
tempCleanableBytes|TEMP e C:\Windows\Temp|Número de bytes|Soma itens cujo LastWriteTime de topo tem mais de um dia; enumera conteúdo de diretórios selecionados. Estimativa de armazenamento, sem remoção.
diskPS|Código DISK_CS do app|Trecho PowerShell de carregamento|Cria fonte C# com nome derivado de hash em userData e gera compilação/carregamento da DLL de medição/limpeza. A chamada real pode escrever arquivos auxiliares.
execPSFile|Script e timeout|Promise de stdout|Grava .ps1 temporário com BOM, inicia PowerShell oculto com -File, limita stdout a 16 MiB e agenda remoção do temporário no callback.
runPSFile|Script e timeout|Promise de stdout|Enfileira execPSFile na mesma fila usada por runPS; evita concorrência entre essas operações.
cleanScanScript|Catálogo de categorias|Texto PowerShell de varredura|Monta medição de bytes/arquivos, existência, idade e aplicativos abertos por categoria; trata Lixeira separadamente.
cleanRunScript|Categorias selecionadas|Texto PowerShell de limpeza|Monta limpeza com bloqueio de apps abertos, idade e contagem de falhas; Lixeira usa Clear-RecycleBin e Delivery Optimization tem operação específica.
diskScanStop|Processo de varredura atual|Sem retorno útil|Encerra worker de ocupação de disco e limpa referência; não encerra processos arbitrários do usuário.
listDrives|Nenhuma|Lista de discos fixos prontos|Enumera letra, rótulo, total e espaço livre por DriveInfo, valida letras e normaliza números; somente leitura.
parseUninstall|UninstallString e metadados MSI|Executável e argumentos|Expande variáveis, reconhece MSI e converte /I para /X; separa caminho citado ou tenta localizar .exe em comando sem aspas.
uninstallExe|Registro de aplicativo|Caminho do desinstalador ou vazio|Extrai executável absoluto pelo parser, excluindo executores genéricos msiexec/rundll32 para não tratar pasta de sistema como pasta do app.
protectedDirs|Ambiente do processo|Lista de pastas protegidas|Lista raízes de Windows/programas/dados/usuário e pasta do próprio GDZ; auxilia o filtro de sobras, sem apagar nada.
safeAppDir|Pasta candidata e pastas de outros apps|Booleano|Rejeita caminhos não absolutos/curtos, raízes/ancestrais protegidos, árvores de sistema e sobreposição com outros apps. É filtro de caminho, não prova de propriedade de todos os arquivos.
listApps|Registro Uninstall|Lista normalizada e cache|Executa inventário, valida chave, elimina nome/versão duplicados e formata data/tamanho/local; não desinstala por si só.
nameVariants|Nome de aplicativo|Variações de nome|Remove versão/arquitetura, produz versão sem espaços e descarta nomes genéricos/inválidos; base para procurar sobras, não prova de autoria de pasta.
pubVariants|Publicador|Variações de publicador|Remove sufixos empresariais comuns e rejeita caracteres de caminho; auxilia heurística de sobras.
# Sensores e informações ao vivo
cpuTimes|os.cpus|idle/total acumulados|Soma tempos de todos os processadores lógicos; diferença entre amostras permite calcular utilização.
lineReader|Processo filho e callback|Callbacks por linha completa|Acumula stdout parcial e separa quebras de linha; preserva fragmento incompleto para próximo chunk.
startNvidiaSmi|Caminhos conhecidos|Booleano de início|Inicia consulta contínua de uso/temperatura GPU a cada 1000 ms, atualiza sensors e limpa campos quando processo sai.
startSensorPs|Necessidade de GPU fallback|Worker e atualizações de sensors|Inicia PowerShell de sensores; converte zona térmica de décimos de Kelvin e armazena como cpuTemp se na faixa. Origem de zona térmica não prova temperatura do die da CPU.
sensorsStart|Campos do overlay|Timer e workers sob demanda|Calcula CPU por deltas a cada segundo, inicia NVIDIA/fallback somente quando dados são necessários e limita tentativas de respawn.
sensorsStop|Estado sensors|Sem retorno útil|Limpa timer, encerra apenas workers próprios e zera sensores; não interrompe outras aplicações.
sensorsSnapshot|Últimos sensores e memória do SO|cpu/gpu/temp/ramUsed/ramTotal|Monta objeto usado pelo overlay; valores ausentes continuam null. RAM é calculada por totalmem/freemem.
nvidiaSmiExe|Lista de caminhos conhecidos|Caminho existente ou null|Seleciona nvidia-smi disponível no disco; não o inicia nem atualiza driver.
buildPcInfo|JSON bruto de inventário e dados NVIDIA|Modelo completo para renderer|Limpa textos, classifica GPU/discos, cruza monitores, normaliza hardware/Windows/bateria e calcula bootAt. Não mede tudo diretamente nessa função.
nvidiaStatic|nvidia-smi disponível|Promise de lista ou vazia|Consulta nome/driver/VBIOS/PCIe/power limit com timeout de seis segundos; não altera limite de potência.
pcLiveSpawn|Estado pcLive|Workers e cache ao vivo|Inicia nvidia-smi de 2 s e PowerShell de 1,5 s; interpreta clocks, VRAM/engines por LUID e temperatura de disco; conversão térmica tem o limite descrito acima.
pcLiveStart|Uso recente de pcinfo-live|Timer periódico|Calcula uso total/por núcleo por deltas, reinicia workers necessários e para após período de inatividade do consumidor.
pcLiveStop|Workers pcLive|Sem retorno útil|Cancela timer, encerra processos próprios e apaga caches temporários de CPU/GPU/disco.
# FPS, overlay e telemetria
fpsHandleLine|Uma linha CSV do PresentMon|Atualização de buffers por PID|Reconhece cabeçalho, aceita intervalos entre presents estritamente maiores que 0 e menores que 1000 ms, ignora processos configurados e mantém janela móvel/gravação; não mede input lag.
fpsStats|Array de frametimes em ms|fps/low1/low01/frametime/frames|FPS médio = N*1000/soma(ms). Lows são inverso da média dos 1% e 0,1% frametimes mais lentos, com pelo menos um frame; não são percentis simples.
fpsPrune|Mapa de processos medidos|Mapa modificado|Remove processos sem frames recentes por mais de três segundos.
fpsTarget|PID fixado e processos medidos|PID escolhido ou null|Prefere PID fixado; senão pontua jogo conhecido, independent flip e número de frames. Não prova que o escolhido esteja em primeiro plano.
fpsLive|PID|Métricas atuais ou null|Usa aproximadamente 1 s para FPS atual, janela inteira para low e 3 s para comparar com baseline do mesmo nome; devolve ganho percentual sem comprovar causalidade.
fpsMarkBase|Alvo atual e últimos cinco segundos|success/base ou erro|Exige pelo menos 30 frames e guarda média/low do alvo como baseline em memória.
fpsRegisterHotkey|Atalho declarado|Estado de registro|Registra atalho global: inicia medidor se parado, ou marca baseline se já ativo; não envia tecla a jogos.
fpsStart|Estado do medidor|Promise booleana|Inicia PresentMon com stdout CSV, sessão própria, sem rastrear input/GPU e excluindo dwm; interpreta erros e reutiliza início em andamento.
fpsStop|Captura ativa|Sem retorno útil|Encerra processo próprio, limpa buffers e solicita término da sessão PresentMon nomeada. Não altera gráficos do jogo.
overlayBounds|Canto 0–3|Retângulo de posição/tamanho|Calcula posição na tela principal com margens; não segue automaticamente o monitor em que o jogo está.
overlayOpen|Preferência de overlay|Janela transparente e timer|Cria janela sem foco, sempre no topo e com clique atravessando; atualiza métricas a cada 500 ms e mantém medição/sensores ativos.
overlayClose|Overlay existente|Sem retorno útil|Cancela timer, interrompe sensores próprios e fecha a janela; fpsStop é uma operação separada.
loadTelemetryQueue|Arquivo de fila|Array ou vazio|Lê eventos JSON persistidos; não envia nada e não foi executada nesta análise.
saveTelemetryQueue|Array de eventos|Gravação local|Mantém somente os 500 últimos eventos no arquivo; erros de escrita são ignorados nesse corpo.
fpsTelemetryItem|Slot a/b e sessão gravada|Evento de métricas|Classifica before/after/manual, associa pairId quando elegível e inclui métricas, jogo, versão e nomes das otimizações registradas.
enqueueFpsTelemetry|Slot e sessão|Fila eventualmente gravada|Gera evento e só enfileira FPS quando consentimento está ativo, média ≥1 e pelo menos 30 frames.
hardwareDetails|Capacidades do Windows|Promise de detalhes normalizados|Consulta CPU/VRAM/driver/RAM/disco/placa-mãe/Windows/plano por PowerShell e armazena Promise de cache; estes dados compõem snapshot de hardware.
displayDetails|Telas Electron|Resolução/Hz/quantidade|Lê tela principal em pixels físicos e número de monitores; erro retorna objeto vazio.
hardwareSnapshot|Capacidades, hardware e tela|Objeto de hardware ou null|Combina fabricante/modelos, flags, RAM, build, monitores e detalhes. Não lê histórico de navegação ou conteúdo de documentos nesse corpo.
flushTelemetry|Sessão, consentimento e filas|POSTs e atualização local|Quando autenticado online envia FPS consentido e hardware se hash mudou. A condição de hardware independe de telemetryOn; só marca hash após HTTP 200, e descarta lote inválido em HTTP 400.
# OptiScaler e FiveM
httpGetTo|URL, destino/callback opcionais, contador redirect|Promise de texto ou arquivo|Baixa HTTPS com timeout, até cinco redirecionamentos e progresso; sem destino acumula texto. Não valida assinatura por si só.
walkFiles|Raiz, subcaminho e acumulador|Lista de caminhos relativos|Enumera recursivamente arquivos do pacote local; não copia arquivos nem executa DLLs.
optiLocalPkg|Cache OptiScaler|tag/dir ou null|Seleciona versão em pasta com marcador .ok, ordenando tags numericamente; marcador significa extração concluída pelo app, não verificação criptográfica.
optiDownload|Estado global opti|Progresso/erro e pacote em cache|Consulta release, baixa .7z, extrai com 7za e verifica presença de DLL/INI; usa fallback se consulta falhar. Não instala no jogo nessa função.
optiManifest|Pasta do jogo|Manifesto JSON ou null|Lê registro de arquivos/backups criado pela instalação GDZ para permitir atualização/remoção.
exeRunning|Caminho do executável|Promise booleana|Consulta processos por caminho; falha devolve false, portanto não é garantia absoluta de que o jogo esteja fechado.
optiAnalyze|Caminho .exe|Diagnóstico de instalação|Valida arquivo, procura executável real Unreal, upscalers e sinais de anti-cheat com profundidade/itens limitados; informa instalação prévia ou externa, sem escrever no jogo.
optiIniValues|Opções, capacidades e executável|Seções/chaves de INI|Converte UI para upscalers DX11/DX12/Vulkan, FG, spoofing e menu; leva peculiaridades do executável em conta. Não implementa o algoritmo DLSS/FSR.
optiWriteIni|Arquivo INI e valores por seção|Arquivo atualizado|Substitui chaves existentes e acrescenta faltantes preservando outras linhas; grava texto com CRLF.
optiSanitizeOpts|Opções recebidas|Opções aceitas com defaults|Aceita apenas enums de upscaler/FG/menuKey e flags booleanas; descarta valores arbitrários fora do contrato.
optiRemoveFiles|Diretório e manifesto|Lista de falhas|Remove arquivos instalados/logs, repõe backups e tenta apagar diretórios criados; só remove pasta de controle se não houver falhas. Pode alterar arquivos reais do jogo.
optiInstall|exe, dll, opts, acceptAntiCheat|success/manifest ou erro|Analisa destino, bloqueia jogos conhecidos, exige fechamento/aceite quando aplicável, baixa pacote se necessário, faz backups, copia arquivos, grava INI e tenta rollback em erro.
optiUninstall|Diretório do jogo|success ou erro|Lê manifesto, recusa jogo em execução, remove arquivos e repõe backups; limpa registro da instalação após concluir.
xmlGet|Texto XML e tag|Valor de atributo ou null|Lê o atributo value de tag via expressão regular; não suporta XML arbitrário ou validação de schema.
xmlSet|Texto XML, tag e novo valor|Texto alterado ou original|Substitui atributo value de tag existente; não cria tag ausente e não grava arquivo por si só.
fivemRunning|Processos em execução|Promise booleana|Reconhece FiveM.exe e processos GTAProcess do FiveM para evitar alteração enquanto o jogo está aberto.
"""


def main() -> None:
    original_index = json.loads((DEEP / "backend-function-index.json").read_text(encoding="utf-8"))
    declarations = [{"name": f["name"], "line": f["originalLine"], "offset": f["originalStart"]}
                    for f in original_index if f["kind"] == "FunctionDeclaration"
                    and f.get("name") and not f["name"].startswith("_0x")]
    independent = DEEP / "main-functions-original.json"
    if independent.exists():
        other = json.loads(independent.read_text(encoding="utf-8"))
        if {(f["name"], f["offset"]) for f in declarations} != {(f["name"], f["start"]) for f in other}:
            raise ValueError("Independent original AST inventory disagrees")
    evidence = {f["name"]: f for f in json.loads((DEEP / "backend-functions-readable.json").read_text(encoding="utf-8"))}
    names = {f["name"] for f in declarations}
    records = []
    section = ""
    for raw in NOTES.splitlines():
        if not raw.strip():
            continue
        if raw.startswith("# "):
            section = raw[2:]
            continue
        name, inputs, outputs, behavior = raw.split("|", 3)
        original = next((f for f in declarations if f["name"] == name), None)
        if name not in names or original is None:
            raise ValueError(f"Unknown declaration: {name}")
        function = evidence[name]
        records.append({
            "name": name, "section": section, "inputs": inputs, "outputs": outputs,
            "behavior": behavior, "confidence": "static_body_reviewed_not_dynamically_tested",
            "source": {"archivePath": "app-main.js", "originalLine": original["line"],
                       "originalOffset": function["originalStart"], "originalEnd": function["originalEnd"],
                       "focusedLine": function["focusedLine"], "focusedEndLine": function["focusedEndLine"]},
            "originalParameterNames": function["parameters"], "directCalls": function["directCalls"],
        })
    actual = [r["name"] for r in records]
    if len(actual) != len(set(actual)) or set(actual) != names:
        raise ValueError({"missing": sorted(names - set(actual)), "extra": sorted(set(actual) - names)})
    focused = DEEP / "backend-focused.js"
    source_hash = hashlib.sha256(focused.read_bytes()).hexdigest()
    output = ROOT / "docs/GDZ-FUNCOES-INTERNAS.md"
    parts = [
        "# GDZ: 159 funções declaradas do processo principal\n",
        "Cada linha foi escrita a partir do corpo recuperado, com interpretação de entradas, saídas e efeitos. Os parâmetros abaixo têm nomes descritivos; os identificadores originais estão no JSON de evidência. Nenhuma função do produto foi executada.\n",
        "A contagem anterior de 175 era lexical: incluía 16 funções PowerShell dentro de strings. A análise sintática separa **159 declarações JavaScript**, mais **70 funções ligadas a variáveis** descritas no [catálogo complementar](GDZ-HELPERS-EXPRESSOES.md), além dos callbacks IPC e estruturas de ofuscação.\n",
        "As referências B:L apontam para a linha L de `artifacts/private/gdz-analysis/deep/backend-focused.js`; offsets e intervalos do original ficam em `functions-explained.json`. A view é um instrumento de leitura estática, não um programa reconstruído para execução.\n",
        f"SHA-256 da view utilizada: `{source_hash}`.\n",
    ]
    current_section = None
    for record in records:
        if record["section"] != current_section:
            current_section = record["section"]
            parts.extend([f"\n## {current_section}\n", "| Função | Entrada | Saída | O que faz e efeito | Evidência |\n|---|---|---|---|---|\n"])
        cells = [f"`{record['name']}`", record["inputs"], record["outputs"], record["behavior"], f"B:{record['source']['focusedLine']}"]
        parts.append("| " + " | ".join(c.replace("|", "\\|").replace("\n", " ") for c in cells) + " |\n")
    parts.extend([
        "\n## Como ler os limites\n",
        "Ver uma rotina no código confirma sua definição e dependências, mas não demonstra que foi chamada nesta sessão, que Windows/drivers aceitam todos os comandos ou que ela melhora FPS. As operações que geram scripts só os executam quando outro fluxo chama runPS/execPS/runPSFile. A análise não fez essas chamadas.\n",
        "O inventário sintático completo está em `artifacts/private/gdz-analysis/deep/function-index.json`, incluindo todos os callbacks e wrappers das 21 fontes próprias. Funções de bibliotecas de terceiros em node_modules não são recursos autorais do produto e não compõem este catálogo. O motor gráfico nativo tem limites separados no [catálogo de Scaling](GDZ-SCALING-FUNCOES.md).\n",
    ])
    output.write_text("\n".join(parts), encoding="utf-8")
    (DEEP / "functions-explained.json").write_text(json.dumps({
        "schemaVersion": 1, "coverage": {"expectedDeclarations": len(names), "explainedDeclarations": len(records), "missing": []},
        "focusedSha256": source_hash, "functions": records,
    }, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"report": str(output), "coveredDeclarations": len(records), "missing": 0}, ensure_ascii=False))


if __name__ == "__main__":
    main()
