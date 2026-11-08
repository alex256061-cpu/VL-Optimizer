# GDZ Optimizer 1.1.27 — algoritmos e contratos para implementação

Análise estática complementar, 08/11/2026. Este capítulo converte os fluxos internos em regras implementáveis: dados de entrada, unidades, algoritmos, formatos persistidos, limites e critérios verificáveis. Não implementa o aplicativo e não executa comandos do otimizador.

Foram revisados **34 contratos**, extraídas **32 constantes literais** sem lacunas e identificados **54 payloads embutidos distintos** por SHA-256. As constantes incluem seis grupos com assinaturas/interoperabilidade C#. Payload é um bloco textual encontrado, não uma função exclusiva nem uma ação adicional do produto.

Os critérios de verificação abaixo são especificações de aceitação para a implementação futura; não são alegação de que os scripts do GDZ foram executados ou validados no Windows. Fatos de código, limitações observadas e melhorias sugeridas estão separados em cada contrato.

Fonte de referência: `artifacts/private/gdz-analysis/deep/backend-focused.js`, SHA-256 `52ca9c4279b739ec674637dae36a361b62eaf06b94aaa6db2a9caba6d4baaef0`. As linhas apontam para essa versão estável, e as funções também guardam offsets do main original na evidência JSON.

O inventário completo de comandos/valores por ação está no catálogo de operações; este capítulo aprofunda os mecanismos compartilhados. Para endpoints e payloads IPC, consulte [GDZ-IPC.md](GDZ-IPC.md). Para os 533 métodos do wrapper gerenciado, consulte [GDZ-SCALING-FUNCOES.md](GDZ-SCALING-FUNCOES.md).

## Índice dos contratos

| ID | Domínio | Contrato |
| --- | --- | --- |
| PS-01 | Execução e estado | [Transporte PowerShell e filas](#ps-01) |
| MEM-01 | Memória | [Métricas rápidas, standby e cache de leituras](#mem-01) |
| MEM-02 | Memória | [Parser SMBIOS e normalização dos módulos](#mem-02) |
| MEM-03 | Memória | [Limpeza de standby e agendamento da RAM](#mem-03) |
| DISK-01 | Disco e limpeza | [Medição normal e regra dos temporários](#disk-01) |
| DISK-02 | Disco e limpeza | [Limpeza C# e diferenças em relação à prévia](#disk-02) |
| DISK-03 | Disco e limpeza | [Scanner de disco, compressão da árvore e stdout](#disk-03) |
| DISK-04 | Disco e limpeza | [Ciclo do scanner e compilação auxiliar](#disk-04) |
| DISK-05 | Disco e limpeza | [Categorias: seleção, processos abertos e casos especiais](#disk-05) |
| HW-01 | Hardware e sensores | [Inventário detalhado e procedência dos campos](#hw-01) |
| HW-02 | Hardware e sensores | [Sensores do overlay: CPU, NVIDIA e fallback](#hw-02) |
| HW-03 | Hardware e sensores | [PCInfo ao vivo: núcleos, LUID, frequência e watchdog](#hw-03) |
| FPS-01 | FPS e gargalo | [Entrada CSV do PresentMon e retenção](#fps-01) |
| FPS-02 | FPS e gargalo | [Fórmulas de média, 1% low e 0,1% low](#fps-02) |
| FPS-03 | FPS e gargalo | [Escolha de alvo, janelas ao vivo e baseline](#fps-03) |
| FPS-04 | FPS e gargalo | [Ciclo PresentMon e gravações Antes/Depois](#fps-04) |
| BN-01 | FPS e gargalo | [Amostragem de gargalo com pausa por foco](#bn-01) |
| BN-02 | FPS e gargalo | [Descoberta de processo para o teste de gargalo](#bn-02) |
| GAME-01 | Jogos e modo automático | [Descoberta de jogos fixos e biblioteca dinâmica](#game-01) |
| GAME-02 | Jogos e modo automático | [Reconhecimento de processos e capas locais](#game-02) |
| AUTO-01 | Jogos e modo automático | [Máquina de estados do modo automático](#auto-01) |
| BACKUP-01 | Backup e reversão | [Formato de arquivo e tipos de snapshot](#backup-01) |
| BACKUP-02 | Backup e reversão | [Sanitização e identidade estável dos recursos](#backup-02) |
| BACKUP-03 | Backup e reversão | [Restauração por tipo e verificações de retorno](#backup-03) |
| BACKUP-04 | Backup e reversão | [Sobreposição de ações e restaurar tudo](#backup-04) |
| BACKUP-05 | Backup e reversão | [Detecção do estado atual e igualdade por tipo](#backup-05) |
| FPS-05 | FPS e gargalo | [Overlay: janela, posição e atualização](#fps-05) |
| DRS-01 | Backup e reversão | [Ponte NVIDIA DRS embutida e valores default](#drs-01) |
| FIVEM-01 | FiveM e OptiScaler | [Localização, leitura e atualização do XML FiveM](#fivem-01) |
| FIVEM-02 | FiveM e OptiScaler | [Presets exatos e restauração FiveM](#fivem-02) |
| OPTI-01 | FiveM e OptiScaler | [Análise de compatibilidade de diretório OptiScaler](#opti-01) |
| OPTI-02 | FiveM e OptiScaler | [Opções normalizadas e contrato INI](#opti-02) |
| OPTI-03 | FiveM e OptiScaler | [Manifesto transacional de instalação e remoção](#opti-03) |
| TELEM-01 | Telemetria e arquivos | [Fila FPS, pares e envio de hardware](#telem-01) |

## Descobertas que mudam a implementação

- Prévia e limpeza usam filtros diferentes para arquivos cloud; não há equivalência garantida entre os bytes mostrados e tentados na exclusão.
- Temporários usam idade do item de primeiro nível: uma pasta antiga pode conter arquivos recentes que entram na limpeza inteira.
- FPS de gravação é calculado por PID e frametimes; o código não verifica primeiro plano, apesar de a mensagem mencionar foco.
- `secs` de gravação vem da soma dos frametimes, enquanto o prazo de coleta vem de relógio. São grandezas diferentes.
- Métrica GPU diverge por tela: soma global, máximo por adaptador ou lista por placa, conforme o pipeline.
- Reversão de serviço não para automaticamente um serviço que antes estava parado; backup de energia restaura só AC, sem DC.
- Modo automático restaura prioridade para Normal, sem preservar a prioridade original.
- FiveM usa regex, não cria tags ausentes e tem backup único; balanced não desfaz todos os campos de fps.
- Editor INI do OptiScaler preserva duplicatas/linhas indentadas e pode inserir uma segunda chave.
- Hardware alterado pode ser enviado mesmo com consentimento FPS desligado; isso é contrato do cliente observado estaticamente.

<a id="ps-01"></a>
## PS-01 — Transporte PowerShell e filas

**Entradas.** Texto de script e timeout; ações já escolhidas pelos handlers. Estes contratos descrevem o alvo, não mandam executar os scripts.

**Saídas e unidades.** Promise com stdout.trim() ou rejeição do processo. psResult retorna {error,message}, cada campo string ou null.

**Regras extraídas.**

- execPS codifica UTF-16LE em Base64 e chama powershell com -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand. Timeout padrão 20.000 ms e maxBuffer 8.388.608 bytes.
- execPSFile cria %TEMP%/gdz-<UUID>.ps1 em UTF-8 com BOM e usa -File. Timeout padrão 20.000 ms, maxBuffer 16.777.216 bytes; tenta unlink no callback, inclusive após erro.
- runPS e runPSFile usam a mesma _psQueue: uma rejeição não bloqueia o próximo trabalho. Chamadas diretas execPS/execPSFile e processos spawn não entram nessa fila.
- withActionLock tem uma segunda cadeia Promise independente; serializa apenas handlers que o invocam. O sistema inteiro não é uma única transação.
- psResult procura a primeira linha ^GDZ_ERRO: e ^GDZ_AVISO:. Ausência desses marcadores não prova que cada comando interno deu certo; depende também de exit code/throw que o script efetivamente verifica.

**Critérios para verificar uma implementação futura.**

- Script com stdout "GDZ_ERRO: falhou" e saída 0 deve ser falha semântica.
- Rejeitar A e enfileirar B deve permitir que B comece; execPS de leitura pode ocorrer enquanto runPS aguarda.

**Limites observados.**

- Os binários e os scripts do GDZ não foram executados nesta análise. Para uma nova implementação, o contrato de sucesso precisa de pós-verificação por operação.

**Evidência.** `execPS` L747, `runPS` L785, `execPSFile` L4939, `runPSFile` L5006, `withActionLock` L4312, `psResult` L4316.

<a id="mem-01"></a>
## MEM-01 — Métricas rápidas, standby e cache de leituras

**Entradas.** Sem parâmetros. Dados Win32/OS e estado em memória do processo.

**Saídas e unidades.** readStats: cpu/ram/gpu em %. get-mem-stats: used/total/cache em GiB arredondados a 1 decimal; pct inteiro; sticks null ou array.

**Regras extraídas.**

- readStats usa média Win32_Processor.LoadPercentage; RAM=(TotalVisibleMemorySize-FreePhysicalMemory)/TotalVisibleMemorySize; GPU=soma de todos os engines 3D arredondada e limitada a 99. Erro geral retorna três zeros.
- standby soma StandbyCacheCoreBytes+StandbyCacheNormalPriorityBytes+StandbyCacheReserveBytes. Atualiza em segundo plano quando não busy, fails<3 e passaram 8 s desde a conclusão anterior. Timeout 10 s; resultado vazio, negativo ou não finito conta como falha.
- standbyBytes devolve imediatamente a última leitura, inicialmente null. Sucesso zera fails; três falhas seguidas suspendem novas tentativas até o estado ser alterado no processo.
- Cache exposto é min(standby,freemem), não toda a memória de cache do Windows. Used=totalmem-freemem. Divisor GiB=1.073.741.824.
- memSticks tenta até três vezes, não duplica requisição em voo e retorna o cache imediatamente. Consulta SMBIOS bruto em Base64; usa Win32_PhysicalMemory/MemoryDevices somente se a interpretação SMBIOS não produzir módulos. Timeout 20 s.

**Critérios para verificar uma implementação futura.**

- Primeira consulta pode ter sticks/cache null e segunda ter dados; a UI deve aceitar esse estado.
- Standby maior que memória livre deve mostrar cache limitado à memória livre. Três falhas consecutivas devem interromper reconsulta automática.

**Limites observados.**

- Zero em readStats pode representar erro. GiB são rotulados como GB pela interface do alvo.

**Evidência.** `readStats` L1446, `standbyBytes` L1662, `memSticks` L1630, `STANDBY_PS` L1655, `MEM_LAYOUT_PS` L1626.

<a id="mem-02"></a>
## MEM-02 — Parser SMBIOS e normalização dos módulos

**Entradas.** Buffer SMBIOS com estruturas e seção de strings terminada por NUL duplo; depois array de módulos e número de slots.

**Saídas e unidades.** Módulo ocupado: {empty:false,label,gb,gen,form,mts,maker,part}; vazio: {empty:true,label}.

**Regras extraídas.**

- Percorre estruturas por type/length; rejeita length<4 ou além do buffer; para em tipo 127. Lê tipo 17 com length>=21 e strings Latin-1 indexadas a partir de 1.
- Size em offset 12: 65535→0; 32767→extendedSize UInt32LE offset 28 mascarado com 0x7fffffff quando length>=32; bit 15 ligado→KiB convertidos em MiB por round((size&0x7fff)/1024). Caso comum já é MiB.
- slot/bank são índices de strings em 16/17; memType em 18; form em 14: 13=sodimm,11=soldada,outros=dimm. mts prefere UInt16LE offset 32 e recua ao offset 21 quando indisponível/sentinela 65535.
- Fabricante e part number vêm dos índices 23 e 26. Remove tipos de memória 8 a 12. memSticksFrom mantém até 16 módulos ocupados e adiciona vazios até completar no máximo 8 posições quando há menos de 8 ocupados.
- memText remove caracteres fora de ASCII imprimível, colapsa whitespace e elimina strings genéricas, zeros, ffff etc.; labels/part/maker ficam em até 32 caracteres. Conflitos de slot usam bank · slot; remove ControllerN- e traduz channel para Canal.
- gb=round(mb/102.4)/10. mts fora de 100..20.000 vira 0. Fabricante usa tabela hexadecimal, padrões de nome, depois padrões do part number; ID desconhecido de quatro hexadecimais vira vazio.

**Critérios para verificar uma implementação futura.**

- Size 0x8000|1024 gera 1 MiB; size 0x7fff com extended 16384 gera 16 GiB.
- Dois módulos de 8192 MiB devem gerar 8,0 GiB cada; slots duplicados com banks distintos devem ser distinguíveis.
- Módulos vazios não devem ganhar fabricante/frequência inventados.

**Limites observados.**

- O parser não cobre toda a especificação SMBIOS moderna; offsets estendidos de frequência não são usados. O fallback WMI só distingue FormFactor=12 como sodimm.

**Evidência.** `smbiosMemory` L1466, `memText` L1536, `memMaker` L1548, `memSticksFrom` L1556, `MEM_GEN` L1507, `MEM_MAKER_ID` L1519.

<a id="mem-03"></a>
## MEM-03 — Limpeza de standby e agendamento da RAM

**Entradas.** cleanRam(auto:boolean). Configurações ramAuto e ramMinutes.

**Saídas e unidades.** {success,freedMB,message} ou {success:false,error}; freedMB pode ser null. ramStatus retorna auto/minutes/intervals/lastAt/lastFreedMB/lastAuto/nextAt.

**Regras extraídas.**

- C# embutido chama RtlAdjustPrivilege(13,true,false,out old) e NtSetSystemInformation(80,ref cmd=4,4). Só o segundo NTSTATUS é usado para decidir êxito.
- Não esvazia working set de cada processo. Mede standby antes/depois com os três contadores de MEM-01; freedMB=max(0,Round((before-after)/1MiB)).
- NTSTATUS!=0 imprime GDZ_ERRO com hexadecimal de oito dígitos. Se a purga for aceita mas falhar a medição, a ação ainda pode retornar success:true com freedMB:null.
- Lock _ramBusy devolve erro se já houver limpeza. Timeout do script 30 s. Em sucesso atualiza ramLast.at/cleanedAt e cache standby quando há GDZ_RAM.
- Intervalos permitidos são 5,10,15,30,60 minutos; valor inválido volta a 10. ramAuto é true salvo se explicitamente false. Tick a cada 15 s exige currentUser e ramAuto. ramLast.at começa na abertura do app.
- Falha não avança ramLast.at; se o vencimento persistir, o próximo tick pode tentar novamente. fmtMB usa divisor 1024 para exibir GB com vírgula decimal.

**Critérios para verificar uma implementação futura.**

- Purga aceita sem contadores deve preservar a distinção freedMB=null e freedMB=0.
- A chamada concorrente deve falhar imediatamente; falha de purga não deve produzir lastFreedMB novo.

**Limites observados.**

- A diferença de standby não mede ganho de FPS nem memória permanentemente liberada. O privilégio anterior não é restaurado pelo C# embutido.

**Evidência.** `cleanRam` L2964, `ramStatus` L3030, `fmtMB` L2962, `RAM_CLEAN_PS` L2952, `RAM_INTERVALS` L2953.

<a id="disk-01"></a>
## DISK-01 — Medição normal e regra dos temporários

**Entradas.** Measure(path,minAgeDays) ou MeasureTop(path,minAgeDays); tempos UTC e tamanhos lógicos de arquivo.

**Saídas e unidades.** long[2]=[bytes,arquivos]. Erros de acesso são ignorados; sem contador explícito de diretórios inacessíveis.

**Regras extraídas.**

- Cutoff=DateTime.UtcNow.AddDays(-minAgeDays), inclusivo: LastWriteTimeUtc<=cut. Measure percorre cada subdiretório e filtra idade de cada arquivo; idade da pasta não barra a descida.
- Sum usa pilha iterativa, pula ReparsePoint e arquivos com atributos CLOUD=0x400000|0x40000|0x1000. Conta FileInfo.Length e um arquivo por entrada, sem deduplicar hardlinks nem medir allocation size.
- Measure(path) quando path é arquivo verifica IsLink e idade, mas não IsCloud; é uma diferença do caminho recursivo Sum.
- MeasureTop aplica a idade só aos itens diretamente dentro da pasta. Se uma subpasta estiver antiga, mede todo o seu conteúdo com DateTime.MaxValue, inclusive arquivos recém-modificados.
- Uma pasta de temporários recente preserva todos os seus descendentes na regra Top. A pasta raiz não é contada como arquivo.

**Critérios para verificar uma implementação futura.**

- Pasta top antiga com arquivo interno recente deve entrar inteira; pasta top recente com arquivo antigo deve ficar inteira de fora.
- O mesmo arquivo com duas hardlinks aparece duas vezes na soma lógica; bytes não devem ser apresentados como espaço físico garantido.

**Limites observados.**

- Medição parcial por permissão parece uma soma válida. Sum não possui o limite de profundidade 100 usado pela limpeza recursiva.

**Evidência.** `DISK_CS` L4768.

<a id="disk-02"></a>
## DISK-02 — Limpeza C# e diferenças em relação à prévia

**Entradas.** Clean(path,minAgeDays) ou CleanTop(path,minAgeDays).

**Saídas e unidades.** long[3]=[bytes de arquivos apagados,arquivos apagados,falhas ao apagar arquivo].

**Regras extraídas.**

- DelFile pula ReparsePoint e arquivos mais novos que cut; remove flag ReadOnly antes de FileInfo.Delete. Incrementa bytes/files somente após Delete sem exceção; falha incrementa apenas o terceiro campo.
- Clean não apaga a raiz; percorre descendentes e remove pastas vazias se a data da pasta, capturada antes de limpar, era <=cut. Só desce enquanto depth<100.
- CleanTop filtra idade dos itens de primeiro nível, depois chama CleanDir/DelFile com DateTime.MaxValue. Isso pode apagar arquivo recente dentro de pasta top antiga.
- DelFile NÃO chama IsCloud. Arquivos offline/recall sem ReparsePoint podem ficar fora da prévia recursiva e ainda entrar na tentativa de limpeza.
- Erros de enumeração e de remoção de diretório não são somados ao contador failed de arquivos. Uma pasta com reparse point é considerada não vazia e permanece.
- A rotina não faz comparação pós-ação com inventário prévio; alterações concorrentes no sistema podem mudar tamanho, idade e conteúdo.

**Critérios para verificar uma implementação futura.**

- Arquivo ReadOnly antigo pode ser apagado; arquivo em uso deve incrementar failed e não freed.
- Falha de enumeração não deve ser confundida com pasta comprovadamente vazia.
- Na implementação futura, usar os mesmos filtros de cloud na prévia e ação corrige uma divergência observada, sem imitar o defeito.

**Limites observados.**

- A análise não apagou arquivos. Há condições de corrida normais de filesystem; não tratar esta implementação como sandbox de diretórios.

**Evidência.** `DISK_CS` L4768.

<a id="disk-03"></a>
## DISK-03 — Scanner de disco, compressão da árvore e stdout

**Entradas.** Scan(root,topFiles); o handler do produto fixa topFiles=40.

**Saídas e unidades.** Linhas TAB: P	files	bytes; D	id	parent	totalBytes	totalFiles	ownBytes	name; F	bytes	fullPath.

**Regras extraídas.**

- DFS com duas pilhas e IDs crescentes; cada filho tem parent menor. Pula ReparsePoint e arquivos CLOUD, ignora exceções. own soma arquivos diretos; cnt conta arquivos diretos.
- Progresso P sai a cada >=250 ms após concluir a enumeração de uma pasta. Uma pasta individual grande/inacessível pode retardar o próximo evento.
- Arquivos maiores são mantidos ordenados por tamanho; conserva no máximo topFiles, incluindo empatados enquanto houver vaga; quando cheio só substitui mínimo se tamanho novo for maior.
- Agrega bytes/arquivos de baixo para cima. Threshold inicial max(8MiB,total/20000). Dobra threshold até haver no máximo 30.000 nós acima do limiar. Sempre emite raiz.
- Emite D em ordem de ID e F do maior para o menor. IDs podem conter lacunas depois do filtro; nó listado continua tendo os ancestrais listados porque total do pai>=total do filho.
- D inclui número total de arquivos descendentes, apesar do comentário curto no código poder sugerir arquivos soltos. ownBytes é o campo de arquivos diretos; não existe ownFiles no protocolo final.

**Critérios para verificar uma implementação futura.**

- Árvore A/B com um arquivo de 10MiB deve permitir reconstruir soma pai/filho e ownBytes sem contar duas vezes.
- Cliente deve usar ID explícito em Map; índice da linha pode ser diferente do ID por causa dos nós filtrados.
- Nomes e caminhos contendo TAB devem ser recompostos a partir do campo de nome até o fim da linha.

**Limites observados.**

- scanned não equivale a total-free do volume: permissões, cloud, reparse, metadados e hardlinks geram divergências. Não há cancelamento cooperativo dentro do C#.

**Evidência.** `DISK_CS` L4768.

<a id="disk-04"></a>
## DISK-04 — Ciclo do scanner e compilação auxiliar

**Entradas.** drive em formato X:, escolhido entre volumes Fixed e IsReady.

**Saídas e unidades.** disk-scan responde sucesso ao iniciar; disk-scan-status retorna state idle/running/done/error e resultado posterior.

**Regras extraídas.**

- diskPS calcula SHA-1 do texto DISK_CS e usa seus primeiros dez caracteres em gdz-disk-<hash>.cs/.dll no userData. Escreve .cs se ausente; tenta Add-Type -OutputAssembly, depois carrega DLL; em falha compila em memória.
- O scanner é processo PowerShell dedicado fora de _psQueue. Usa EncodedCommand UTF-16LE, stdout UTF-8, stderr ignorado. Só um estado running é permitido.
- Ao fechar, considera leitura concluída se houver qualquer nó D, independentemente de exit code. Sem D retorna erro genérico. Armazena total/free obtidos antes da varredura, scanned/files da raiz e at=Date.now().
- diskScanStop mata o processo e limpa referência; o callback de fechamento só atua se ainda for o processo atual. O handler de cancelamento é descrito no catálogo IPC.
- listDrives retorna name de duas letras X:, label,total/free em bytes. O drive do sistema vem de SystemDrive com fallback C:.

**Critérios para verificar uma implementação futura.**

- Fechamento sem D deve ser error; D válido seguido de exit code diferente de zero ainda vira done no alvo.
- O arquivo fonte auxiliar muda de nome quando muda o conteúdo, por causa do hash do texto.

**Limites observados.**

- Não tomar state=done como prova de leitura integral. Nenhuma compilação do C# do alvo foi realizada para esta análise.

**Evidência.** `diskPS` L4929, `diskScanStop` L5373, `listDrives` L5380.

<a id="disk-05"></a>
## DISK-05 — Categorias: seleção, processos abertos e casos especiais

**Entradas.** clean-run {ids:string[]}; IDs reconhecidos em CLEAN_CATS. O catálogo de operações contém os caminhos e todas as categorias.

**Saídas e unidades.** Scan: cats com exists/bytes/files/open. Run: results[{id,open,freed,files,failed}],freed total.

**Regras extraídas.**

- CLEAN_PS_HEAD fotografa nomes dos processos uma vez por execução e usa wildcard -like; não acompanha abertura/fechamento durante a limpeza.
- Categorias normais com processo detectado aberto são puladas inteiras e retornam open:true,freed/files/failed=0. Duplicatas de IDs são eliminadas por Set; IDs desconhecidos não entram.
- Alvos resolve os caminhos existentes, elimina vazios e Select-Object -Unique. Chromium inclui raiz e pastas-filhas que contêm Preferences; caches exatos CR, depois shaders da raiz. Não usa uma lista fixa só Default/Profile 1.
- Lixeira mede as pastas por SID do usuário em todos os drives fixos prontos, chama Clear-RecycleBin -Force e calcula diferença não negativa; failed é fixado em 0.
- DeliveryOptimization chama Delete-DeliveryOptimizationCache quando disponível; caso contrário usa Clean. Resultado é diferença entre medição antes/depois, failed fixado em 0.
- clean-scan usa execPSFile com 120 s; clean-run usa runPSFile com 600 s e withActionLock. Leitura JSON exige linha GDZ_JSON; campo único é normalizado para array.

**Critérios para verificar uma implementação futura.**

- Categoria de navegador aberto deve retornar open:true sem ser interpretada como limpeza concluída.
- Lixeira vazia ainda aparece como exists:true; diferencia de categoria inexistente.
- Resultados especiais podem ter failed=0 mesmo se parte da operação externa falhar silenciosamente.

**Limites observados.**

- Prévia e execução coletam processos e caminhos em momentos diferentes. Não há confirmação externa nesta análise de todos os comandos de limpeza.

**Evidência.** `cleanScanScript` L5259, `cleanRunScript` L5280, `psJson` L5302, `CLEAN_PS_HEAD` L5011.

<a id="hw-01"></a>
## HW-01 — Inventário detalhado e procedência dos campos

**Entradas.** Sem alteração de configurações. WMI/CIM/registro, Electron screen e nvidia-smi quando encontrado.

**Saídas e unidades.** Objeto CPU/board/mem/gpus/adapters/disks/vols/nets/displays/win/battery/bootAt; falhas parciais usam vazio/null conforme conversor.

**Regras extraídas.**

- CPU é o primeiro Win32_Processor; cache soma InstalledSize por Level=3/4/5 para L1/L2/L3 em KiB. L2/L3 podem recuar a L2CacheSize/L3CacheSize.
- Microcode usa bytes 4..7 de Update Revision quando length>=8, senão bytes 0..3 quando length>=4. A lista de features consulta IsProcessorFeaturePresent com IDs 3,6,10,13,36,37,38,39,40,41.
- Virtualização é OR de VirtualizationFirmwareEnabled e HypervisorPresent, sem distinguir hypervisor do host de suporte habilitado.
- Secure Boot é null se a consulta falhar; UEFI é true se firmware_type=UEFI OU Secure Boot retornar qualquer booleano. TPM pega primeira SpecVersion antes da vírgula.
- VRAM prefere HardwareInformation.qwMemorySize de chave cuja DriverDesc=Name da GPU; fallback AdapterRAM. Ignora GPUs sem PNPDeviceID começando PCI.
- Discos: Get-PhysicalDisk e Get-StorageReliabilityCounter fornecem capacidade, saúde, firmware, temperatura, desgaste, horas e erros; volumes vêm de Win32_LogicalDisk DriveType=3. Nomes de unidade via associações DiskDrive→DiskPartition→LogicalDisk.
- Rede só Physical com Status Up. Tamanho/nome físico dos monitores vêm de WMI; resolução/Hz e escala atuais vêm de Electron. Bateria consulta design/full/cycles de classes root/wmi.
- nvidiaStatic consulta name,driver_version,vbios_version,pcie.link.gen.max,pcie.link.width.max,power.limit com timeout 6 s; não é o mesmo streaming do overlay.

**Critérios para verificar uma implementação futura.**

- Secure Boot desativado mas consultável deve produzir uefi:true,secureBoot:false.
- RAM e disco ausentes devem permanecer distinguíveis de capacidade zero; GPU PCI com VRAM>4GiB deve preferir qwMemorySize.

**Limites observados.**

- Informações de firmware e WMI podem estar ausentes ou erradas. Não inferir temperatura real do die nem estado de XMP apenas do nome do part number.

**Evidência.** `buildPcInfo` L6622, `nvidiaStatic` L6786, `nvidiaSmiExe` L6619, `PCINFO_PS` L6471.

<a id="hw-02"></a>
## HW-02 — Sensores do overlay: CPU, NVIDIA e fallback

**Entradas.** Flags de exibição fpsOverlay.show; estado sensors e contadores cumulativos os.cpus().times.

**Saídas e unidades.** {cpu,cpuTemp,gpu,gpuTemp,ramUsed,ramTotal}; % e °C, RAM GiB com used a 1 decimal e total inteiro.

**Regras extraídas.**

- CPU a cada 1 s: total=sum(user+nice+sys+idle+irq) em todos os processadores; uso=round(100*(1-Δidle/Δtotal)) se Δtotal>0.
- NVIDIA: busca nvidia-smi em System32 ou Program Files/NVIDIA Corporation/NVSMI. Executa query utilization.gpu,temperature.gpu, csv,noheader,nounits, -lms 1000. Uso limitado 0..100; temperatura aceita apenas 0<t<130.
- Cada linha NVIDIA sobrescreve os mesmos campos; em multi-GPU o último resultado recebido ocupa o resumo, sem agregação por placa.
- PowerShell fallback tem prioridade BelowNormal, lê GPU 3D global se necessário e ThermalZoneInformation.HighPrecisionTemperature máxima a cada 3 s. Temperatura=T/10-273,15; aceita só 30<t<115 e arredonda.
- O nome cpuTemp deriva de zona térmica do Windows, não de associação comprovada ao sensor da CPU. Valor fora da faixa vira null.
- Inicialização tenta novamente a cada 60 s somente quando não há smi nem ps. GDZS:none põe respawnAt=Infinity. Alterar show dispara sensorsStop pelo handler, permitindo novo ciclo.
- lineReader acumula pedaços até newline; a última linha sem newline não é entregue no exit. sensorsStop mata processos próprios e zera campos/respawnAt.

**Critérios para verificar uma implementação futura.**

- Δtotal=4000 e Δidle=1000 deve produzir cpu=75.
- ThermalZone 3200 deve produzir 47 °C; 2900 fica null.
- Duas linhas NVIDIA de duas GPUs não equivalem ao máximo nem à média; o resumo será a última linha válida.

**Limites observados.**

- Métricas do dashboard, overlay, gargalo e pcinfo usam pipelines diferentes; não exigir igualdade exata entre elas.

**Evidência.** `cpuTimes` L6293, `lineReader` L6304, `startNvidiaSmi` L6314, `startSensorPs` L6343, `sensorsStart` L6386, `sensorsStop` L6448, `sensorsSnapshot` L6460, `SENSOR_PS` L6281.

<a id="hw-03"></a>
## HW-03 — PCInfo ao vivo: núcleos, LUID, frequência e watchdog

**Entradas.** Polling pcinfo-live atualiza lastUse; caches do inventário fornecem mapeamento LUID→nome.

**Saídas e unidades.** {cpu:{usage,cores,mhz,coreMhz,temp},ram:{used,total},gpus:[...],disks:{id:temp}}. RAM aqui é bytes; VRAM em MiB; clock em MHz.

**Regras extraídas.**

- pcLiveStart usa timer de 1 s; se lastUse ficar >8 s sem renovação, para tudo. Reinicia subprocessos faltantes no máximo a cada 30 s.
- CPU por núcleo usa deltas acumulados de user/nice/sys/idle/irq. Só publica e avança lastCpu quando Δtotal agregado>=400*quantidade de CPUs, acumulando deltas quando a amostra é curta. Clampa % a 0..100.
- PCLIVE_PS verifica se o PID pai ainda existe, coleta a cada 1500 ms e emite GDZPI:<JSON>. perf contém Name=PercentProcessorPerformance=ProcessorFrequency; ignora _Total e fatores<=0; MHz=round(performance*frequency/100), ordenados por grupo,núcleo.
- GPU Windows interpreta LUID de luid_0xHI_0xLO concatenando HI+LO preenchido a 8 hex dígitos e converte para string decimal BigInt; soma engines 3D por adaptador e limita a 100. DedicatedUsage vira MiB por /1.048.576.
- Fallback GPU Windows só aparece se LUID existe no mapa de adapters já lido. Se pcinfo ainda não preencheu o mapa, pode haver dados brutos sem placa exibida.
- NVIDIA vivo usa query name,utilization.gpu,temperature.gpu,memory.used,memory.total,power.draw,fan.speed,clocks.gr,clocks.mem a cada 2000 ms; agrupa linhas num lote depois de 150 ms sem nova linha. Usa lista NVIDIA quando há qualquer dado, senão wingpu.
- NVIDIA aceita campos finitos não negativos limitados: usage/fan100,temp130,memUsed/memTotal1.000.000,power2000,clocks100.000. Discos são consultados a cada quinto loop (~7,5 s) e temperaturas aceitas só 0<t<120.

**Critérios para verificar uma implementação futura.**

- LUID 0x00000000_0x0000002A corresponde à chave decimal 42.
- Polling interrompido por mais de 8 s deve encerrar os coletores próprios; nova consulta pode reiniciá-los.
- PercentProcessorPerformance=125 e ProcessorFrequency=3200 deve gerar 4000 MHz, sujeito à procedência do contador.

**Limites observados.**

- CPU MHz é estimativa pelo contador, não leitura física instantânea de PLL. O decoder preserva condicionais de runtime; não houve teste dos sensores em hardware.

**Evidência.** `pcLiveSpawn` L6893, `pcLiveStart` L7172, `pcLiveStop` L7197, `PCLIVE_PS` L6597.

<a id="fps-01"></a>
## FPS-01 — Entrada CSV do PresentMon e retenção

**Entradas.** Linhas CSV sem suporte a quoting complexo; colunas Application,ProcessID,MsBetweenPresents e PresentMode.

**Saídas e unidades.** Map por PID: {name,frames:[[atMs,frameMs]],last,flip}; frames adicionais na gravação ativa do mesmo PID.

**Regras extraídas.**

- A primeira linha não vazia vira mapa nome→índice após trim; não valida se os nomes esperados existem. Faz split(","), não um parser CSV completo.
- Descarta Application vazio, ProcessID convertido em zero, frameMs<=0 ou >=1000, executáveis FPS_IGNORE e PID do próprio processo.
- Timestamp é Date.now() de recebimento, não tempo do evento ETW. flip indica se PresentMode corresponde a Independent Flip ou Hardware Composed, case-insensitive.
- Cada PID mantém somente amostras com timestamp>=agora-10.000 ms. Isso é poda por tempo de recepção; múltiplas linhas no mesmo chunk podem ter timestamp idêntico.
- Gravação recebe frameMs somente se PID coincide e Date.now()<=until. Não exige janela em primeiro plano nesse caminho.

**Critérios para verificar uma implementação futura.**

- FrameMs=0,1000,NaN devem ser descartados; 999,9 passa.
- Sem coluna MsBetweenPresents, as linhas não devem virar valores inventados.
- Processo em segundo plano ainda pode fornecer frames se PresentMon continuar emitindo para o PID.

**Limites observados.**

- Múltiplas swapchains do mesmo PID são misturadas, pois não existe chave de swapchain. Não é garantia de FPS apresentado ao monitor.

**Evidência.** `fpsHandleLine` L7248.

<a id="fps-02"></a>
## FPS-02 — Fórmulas de média, 1% low e 0,1% low

**Entradas.** Lista de durações de frame válidas em milissegundos, como filtradas por FPS-01.

**Saídas e unidades.** null se lista vazia; senão {fps,low1,low01,frametime,frames}.

**Regras extraídas.**

- S=sum(frameMs),N=length; fps=round(N*1000/S*10)/10 e frametime=round(S/N*100)/100.
- Ordena durações da maior para a menor. Para fração q usa k=max(1,floor(N*q)); low(q)=round(1000/mean(k maiores durações)*10)/10.
- low1 usa q=0,01; low01 usa q=0,001. São inversos da média dos frames mais lentos, não o percentil direto nem a média aritmética dos FPS por frame.
- Com menos de 100 frames low1 usa um frame; com menos de 1000 frames low01 usa um frame.

**Critérios para verificar uma implementação futura.**

- [10,10,20,40]→fps=50,low1=25,low01=25,frametime=20,frames=4.
- 100 frames todos 10 ms exceto um de 100 ms→fps=91,7,low1=10,low01=10,frametime=10,9.
- 1000 frames todos 10 ms→100 FPS e lows=100.

**Limites observados.**

- Critérios calculados independentemente para uma futura implementação. Não foram obtidos executando fpsStats do alvo.

**Evidência.** `fpsStats` L7310.

<a id="fps-03"></a>
## FPS-03 — Escolha de alvo, janelas ao vivo e baseline

**Entradas.** Processos com frames; pinned opcional; catálogo de padrões de jogos.

**Saídas e unidades.** PID selecionado ou null; live com pid/name/fps/frametime/low1 e base opcional.

**Regras extraídas.**

- Remove processos cujo last<agora-3000 ms. pinned vale se ainda houver PID no Map. Caso contrário score=1.000.000 se jogo conhecido +100.000 se flip +quantidade de frames retidos; maior vence, empate preserva primeira inserção.
- FPS/frametime instantâneos usam último 1 s quando há pelo menos 2 frames; caso contrário usa a janela retida de até 10 s. Low1 ao vivo sempre vem da janela retida de até 10 s.
- Baseline usa últimos 5 s e exige >=30 frames. Salva pid,name,fps a 1 decimal,low1 inteiro,label Base,at.
- Comparação com baseline exige apenas name igual sem diferenciar maiúsculas; pode continuar depois que o jogo reiniciar em outro PID. Usa últimos 3 s para now/gain; fresh só enquanto agora-base.at<2500 ms.
- gain=round((currentFPS-baseFPS)/baseFPS*1000)/10, em %. Atalho Ctrl/Command+Alt+B registra uma vez; se medidor não existe, primeiro atalho apenas chama fpsStart e não marca base.

**Critérios para verificar uma implementação futura.**

- Jogo conhecido com 5 frames vence processo desconhecido com independent flip e 1000 frames.
- Baseline 100 FPS e amostra corrente 125 FPS→gain=25,0%.
- 29 frames em 5 s deve rejeitar marcação; 30 aceita.

**Limites observados.**

- Não há normalização por mesma cena/resolução/foco na comparação. gain visual não comprova causalidade da otimização.

**Evidência.** `fpsPrune` L7328, `fpsTarget` L7332, `fpsLive` L7345, `fpsMarkBase` L7375, `fpsRegisterHotkey` L7402.

<a id="fps-04"></a>
## FPS-04 — Ciclo PresentMon e gravações Antes/Depois

**Entradas.** fps-record: slot a/b, secs em {15,30,60}, label opcional. Polling/overlay mantém o medidor ativo.

**Saídas e unidades.** Sessão salva {fps,low1,low01,frametime,frames,name,label,secs,at,pairId?}.

**Regras extraídas.**

- Inicia PresentMon.exe com --output_stdout --no_console_stats --stop_existing_session --session_name GDZ_FPS --no_track_input --no_track_gpu --exclude dwm.exe. Deduplica Promise de startup.
- Startup resolve após 400 ms conforme existência do processo, sem aguardar primeiro frame. Erro de saída com stderr access/privilege/administrador ganha mensagem de administração.
- Só uma gravação simultânea; fixa PID/nome selecionado no início e deadline Date.now()+secs*1000. Poll de 250 ms encerra no deadline, processo coletor ausente ou PID não mais no Map.
- Exige >=30 frames; label remove controles ASCII e <> e corta em 40 caracteres. Label default Antes/Depois. secs salvo=round(sum(frameMs)/1000), que pode diferir de tempo de parede e duração pedida.
- Enfileira telemetria antes de guardar settings.fpsSessions[slot]. Timer de idle a cada 5 s para medidor se sem overlay/rec e lastUse>15 s.
- fpsStop mata o subprocesso próprio e também chama --terminate_existing_session --session_name GDZ_FPS com timeout5 s; limpa procs/cols/buffer. No encerramento do app também chama sensorsStop.

**Critérios para verificar uma implementação futura.**

- Pedido secs=20 deve cair no default30; nova gravação concorrente deve falhar.
- Uma gravação com 40 frames de 20 ms pode ser aceita e salvar secs=1 se terminar cedo; distinguir isso de duração pedida.
- Nome da sessão deve ser estável para encerramento; UI deve esperar dados após startup nominal.

**Limites observados.**

- A mensagem de erro recomenda primeiro plano, mas o código desta gravação não checa foreground. O medidor não implementa a captura em si: depende de PresentMon externo.

**Evidência.** `fpsStart` L7426, `fpsStop` L7498, `FPS_REC_SECS` L6253.

<a id="bn-01"></a>
## BN-01 — Amostragem de gargalo com pausa por foco

**Entradas.** seconds por parseInt com default30 e clamp10..120; pid inteiro>4 ou 0 para medição geral.

**Saídas e unidades.** {success,samples,seconds,away,closed,partial,cpu,coreMax,gpu,ram,ramGb} ou erro.

**Regras extraídas.**

- Script pode durar até seconds*4 por relógio; timeout externo=(seconds*4+60)*1000. Esquenta contador CPU antes do loop, dorme1000 ms entre iterações e mede dt real.
- Se PID fecha, closed=true e encerra. Se foreground PID difere, soma dt a away e não coleta nem soma inGame. Sem PID não exige foco.
- CPU usa PerfOS_Processor._Total; core é máximo de PercentProcessorTime dentre instâncias não _Total. GPU agrupa engines3D removendo prefixo pid_N_ e sufixo _eng_... e escolhe a maior soma de grupo, limitada a100.
- RAM por amostra=(TotalVisibleMemorySize-FreePhysicalMemory)/TotalVisibleMemorySize em %, arredondado. stdout GDZ_BN: cpu,core,gpu,ram;... e GDZ_BN_META: round(inGame),round(away),closed0/1.
- Backend filtra tuplas com exatamente4 números finitos; menos de6 amostras é erro, com textos distintos para closed/foco/geral. CPU/coreMax/GPU são médias aritméticas arredondadas; RAM é o máximo das amostras.
- partial=(secondsEfetivos<secondsPedidas-1); ramGb arredonda totalmem/GiB. Médias não são ponderadas pelo dt de cada amostra.

**Critérios para verificar uma implementação futura.**

- Com seis tuplas CPU=[10,20,30,40,50,60], cpu=35; ram deve ser maior RAM observada, não a média.
- Trocar foco durante10 s não deve acrescentar10 amostras de jogo; away cresce.
- Fechamento com7 amostras pode retornar success:true,closed:true,partial:true; com5 deve falhar.

**Limites observados.**

- Os limiares que transformam números em rótulo CPU/GPU/RAM são do renderer, separados da coleta. Não atribuir os números a um único processo: contadores são do sistema/adaptador.

**Evidência.** `measureBottleneck` L2279, `BOTTLENECK_PS` L2243.

<a id="bn-02"></a>
## BN-02 — Descoberta de processo para o teste de gargalo

**Entradas.** Lista de PIDs ignorados; processos com MainWindowHandle!=0 e contadores engines3D.

**Saídas e unidades.** {pid,name,exe,known} ou null.

**Regras extraídas.**

- Agrupa uso GPU por PID somando engines3D. Normaliza pid inteiro>4, diferente do processo atual e fora da lista ignorada.
- name/title/desc ficam em até120 caracteres. Ordena descendente por gpu e procura primeiro qualquer processo que corresponda a padrões de GAMES.
- Se não houver conhecido, escolhe primeiro com GPU>=10 e nome fora de BN_NOT_GAMES. Nome de fallback title||desc||exe, known=false.
- A lista BN_NOT_GAMES inclui navegadores, launchers, shells e aplicativos de fundo. O reconhecedor aqui usa GAMES fixo, diferentemente de fpsTarget que usa allGames incluindo biblioteca dinâmica.

**Critérios para verificar uma implementação futura.**

- Conhecido comGPU1 pode vencer desconhecido comGPU90; catálogo conhecido tem precedência.
- Desconhecido comGPU9 deve ser rejeitado, comGPU10 pode passar se não estiver na denylist.

**Limites observados.**

- Aplicativos sem janela não são candidatos. GPU não NVIDIA depende de disponibilidade dos contadores Windows.

**Evidência.** `findGameProcess` L2362, `BN_FIND_PS` L2352.

<a id="game-01"></a>
## GAME-01 — Descoberta de jogos fixos e biblioteca dinâmica

**Entradas.** Catálogo GAMES; registro do Steam/uninstall, manifests Steam/Epic, metadata Riot e caminhos declarados.

**Saídas e unidades.** GAME_SCAN por ID {installed,exes}; LIB_GAMES derivado de Steam/Epic; STEAM_ROOT.

**Regras extraídas.**

- Steam lê HKCU/Software/Valve/Steam/SteamPath e libraryfolders.vdf por regex de "path"; SteamDir lê installdir de appmanifest_<id>.acf.
- Catálogo fixo soma diretórios por SteamIDs, AppName Epic, YAML Riot product_install_full_path, regex DisplayName de uninstall e paths expandindo variáveis. installed fica true quando achar exe OU marker OU Appx, podendo não existir exe.
- Epic e Riot usam C:/ProgramData literal no script, não ProgramData do ambiente. Isso pode falhar em instalações fora desse caminho.
- Dinâmicos Steam varrem manifests fora do catálogo, pulam appid228980 e nomes de ferramentas/runtime. GameExes pesquisa *.exe até Depth3, filtra instalador/redist/crash/anti-cheat e pega8 maiores.
- Epic exige jogo principal e categoria games quando declarada. Prioriza LaunchExecutable válido e completa com GameExes até8 únicos.
- libGame aceita só src steam/epic; nome é sanitizado e cortado em60, key lowercase não alfanumérico vira hífen e corta40. ID=<src>-<key>. Aceita caminhos EXE_RE com letra de drive; exclui basenames genéricos Python/Java/Node/shell.
- Catálogo fixo aceita até12 exes, dinâmico até8. Remove basenames compartilhados entre jogos dinâmicos se restar ao menos um exclusivo. Se todos forem compartilhados mantém a lista original.
- scanGames usa Promise em cache sem TTL; force limpa cache. Erro deixa último GAME_SCAN disponível e permite nova tentativa. Timeout60 s.

**Critérios para verificar uma implementação futura.**

- Marker de FiveM pode gerar installed:true com exes:[]. A UI não deve inferir executável sem evidência.
- Dois jogos dinâmicos com mesmo launcher e executáveis exclusivos devem usar basenames exclusivos; ambos só launcher mantêm ambiguidade.
- Adicionar jogo enquanto cache existe só aparece após force/renovação apropriada.

**Limites observados.**

- Regex ACF/VDF/YAML não são parsers completos. Jogos fora de Steam/Epic dinâmicos dependem do catálogo fixo.

**Evidência.** `scanGames` L2588, `libGame` L2489, `allGames` L2486, `gameById` L2487, `GAME_PS_HEAD` L2508, `GAMES_SCAN_PS` L2524.

<a id="game-02"></a>
## GAME-02 — Reconhecimento de processos e capas locais

**Entradas.** tasklist CSV e padrões proc/exe; ID Steam e arquivos locais de librarycache.

**Saídas e unidades.** Set de nomes de processos lowercase, lista IDs em execução e capa data URL/kind ou null.

**Regras extraídas.**

- runningProcesses chama tasklist /FO CSV /NH, timeout10 s e buffer8MiB. Falha devolve Set vazio, indistinguível de nenhum jogo reconhecido.
- procMatchers converte * em .* e ancora ^...$, escapando os metacaracteres listados no helper; tanto padrão quanto nome são convertidos a lowercase. runningGameIds cruza nomes, não caminho completo nem assinatura do executável.
- Capas priorizam library_600x900.jpg,library_capsule.jpg,header.jpg,library_header.jpg em <Steam>/appcache/librarycache/<id> e subpastas imediatas. Fallback ao layout antigo id_nome.jpg só quando a leitura da pasta moderna falha.
- Capas maiores que420px de largura são reduzidas a420 e convertidas JPEG82. Sem capa usa ícone grande do primeiro exe. Guarda cache apenas dos resultados bem-sucedidos.

**Critérios para verificar uma implementação futura.**

- Falha de tasklist pode disparar interpretação de jogo fechado no modo automático; preserve um estado de leitura falha numa implementação mais robusta.
- Nome de processo igual basta no alvo, mesmo se exe estiver em outra pasta.

**Limites observados.**

- Capas não são baixadas da internet por esse caminho. A presença de um basename não prova identidade do jogo.

**Evidência.** `runningProcesses` L2756, `procMatchers` L2795, `runningGameIds` L2796, `steamCoverFile` L2688, `gameCover` L2721.

<a id="auto-01"></a>
## AUTO-01 — Máquina de estados do modo automático

**Entradas.** settings.autoMode e autoState; currentUser presente; lista de jogos rodando; capacidades laptop/x3dDual.

**Saídas e unidades.** autoState persistido {game,scheme,killed}; efeitos temporários e notificação.

**Regras extraídas.**

- Tick5 s com lock _autoBusy. Retorna sem usuário; também retorna se modo desligado e estado ausente. Entra quando autoMode=true, há jogo e não há estado válido.
- Rebaixa chrome,msedge,firefox,opera,Discord para BelowNormal; encerra OneDrive,Dropbox,GoogleDriveFS por Stop-Process Force e guarda caminhos únicos que estavam acessíveis.
- Captura plano ativo. Se não laptop/x3dDual e plano difere de HIGH, tenta powercfg /setactive HIGH; só guarda scheme anterior se LASTEXITCODE=0. Não guarda todas as prioridades originais.
- cleanAutoState conserva game só se gameById existir; scheme só se GUID_RE; killed só caminhos EXE_RE, primeiros10. Um objeto válido com game=null ainda pode ser estado para restauração.
- Saída ocorre quando estado existe e não há mais nenhum jogo ou autoMode=false. Se um jogo fecha mas outro permanece, não troca autoState.game nem reaplica.
- Restaura somente processos LOW cuja prioridade ainda seja BelowNormal, definindo Normal; não recupera original AboveNormal/High/etc. Reinicia caminhos killed existentes, com /background apenas OneDrive. Plano volta quando scheme salvo.
- Grava autoState somente após script de entrada; saída limpa estado após script resolvido. Exceções são absorvidas no tick; persistência de settings também absorve erros.

**Critérios para verificar uma implementação futura.**

- JogoA→JogoB com algum jogo sempre rodando deve manter estado único até todos fecharem.
- Processo originalmente High passa BelowNormal e depois Normal no alvo; isso deve ser uma decisão consciente na nova especificação.
- Encerrar app entre entrada/saída deixa autoState em arquivo para próxima sessão; usuário precisa estar autenticado para tick trabalhar.

**Limites observados.**

- A falha de tasklist aparenta ausência de jogos. Não foi verificado se processos reiniciados recuperam sessão/argumentos anteriores.

**Evidência.** `autoTick` L2870, `autoStartScript` L2859, `autoStopScript` L2860, `cleanAutoState` L2861, `loadSettings` L2838, `saveSettings` L2849, `AUTO_APPS_LOW` L2857, `AUTO_APPS_CLOSE` L2858.

<a id="backup-01"></a>
## BACKUP-01 — Formato de arquivo e tipos de snapshot

**Entradas.** SPECS allowlist de itens; arquivo userData/gdz-backups.json.

**Saídas e unidades.** {seq:integer,entries:{action:{seq,at,items:[...],after?:[...]}}}. Snapshot via GDZ_SNAPSHOT:<JSON>.

**Regras extraídas.**

- loadBackups valida seq inteiro, entries objeto não array; aceita entry só de ação com spec, seq inteiro e items array. Não valida todos os itens nessa primeira etapa: sanitize acontece depois.
- saveBackups escreve JSON em .tmp e rename, sem try/catch local. at é Date.now em ms. after é snapshot posterior opcional, não substitui items original.
- Dados de spec são JSON UTF-8/Base64 substituindo __DATA__; isso evita interpolar valores brutos no PowerShell. Se houver t=drs, injeta DRS_PS antes.
- Snapshot captura reg,svc,scheme,pwr,bcd,tcp,dns,drs. Reg contém exists e, se presente, kind/value; Binary vira Base64, QWord string decimal, MultiString array, DWord int32.
- Serviço captura StartMode,DelayedAutoStart e State Running. Scheme captura GUID ativo. pwr captura SOMENTE índice AC do plano ativo a partir da penúltima linha /q; DC não entra.
- bcd guarda existência e reduz valor a Yes para yes/sim/true/on, senão No. TCP captura autotuning/rss/heuristics. DNS captura NameServer por GUID apenas de adaptadores Up. DRS distingue override ausente de valor0.
- takeSnapshot exige marcador, JSON válido e sanitiza contra spec; se nenhum item sobreviver retorna []. run-action aborta antes da mutação quando snapshot lança, mas não exige que todos os itens da spec apareçam.

**Critérios para verificar uma implementação futura.**

- QWord maior que2^53 deve permanecer string no JSON.
- Snapshot de reg ausente deve restaurar ausência, não valor zero.
- Duas ações sobre mesmo recurso precisam compartilhar identidade para reversão em cadeia.

**Limites observados.**

- Não é checkpoint completo do Windows. Pacotes Appx, arquivos removidos e recursos sem spec dependem de extras/fallback e nem sempre são recuperáveis.

**Evidência.** `loadBackups` L4163, `saveBackups` L4183, `psData` L4188, `takeSnapshot` L4190, `withDrs` L4189, `SNAPSHOT_PS` L3949.

<a id="backup-02"></a>
## BACKUP-02 — Sanitização e identidade estável dos recursos

**Entradas.** Item vindo de arquivo/snapshot e lista de recursos permitidos para a ação.

**Saídas e unidades.** Item normalizado seguro para o restaurador ou null; identidade textual para sobreposição.

**Regras extraídas.**

- Sanitize só aceita t presente na spec; reg compara path/name case-insensitive,svc nome; pwr sub/set,BCD el,DRS id,TCP k com comparação estrita. Scheme e DNS precisam apenas tipo permitido e validações próprias.
- DWord inteiro -2.147.483.648..4.294.967.295; QWord string /^-?\d{1,20}$/; Binary string com alfabeto Base64 (não prova decodificação canônica); MultiString array só de strings; String/ExpandString são string.
- Serviço mode apenas Auto/Manual/Disabled, delayed/running estritamente true. Plan GUID_RE; pwr ac inteiro0..2^32-1. bcd normaliza qualquer value diferente Yes para No.
- TCP aceita letras1..32, porém restaurador tem allowlist ainda mais estreita. DNS exige GUID e ns de até300 caracteres [0-9a-fA-F:., ]; DRS uint32 ou exists:false,value0.
- itemId: reg|pathLower|nameLower;svc|nameLower;pwr|schemeLower|sub|set;bcd|el;tcp|k;dns|guidLower;drs|id;scheme usa só t.
- Spec dinâmica de GPU depende dos executáveis descobertos, portanto carregar/reverter pode depender do catálogo de jogos atual.

**Critérios para verificar uma implementação futura.**

- Registro com mesmo nome e caixa diferente deve colidir; mesmo sub/set em planos distintos deve ter itemId diferente.
- DWord4294967295 passa;4294967296 falha. QWord decimal string passa sem virar Number.
- Tipo permitido não significa valor aceito pelo Windows; nova implementação deve validar o domínio real.

**Limites observados.**

- O regex QWord admite até20 dígitos, podendo exceder Int64 e falhar na restauração. GUID_RE aceita chaves opcionais independentemente; não é parser GUID completo.

**Evidência.** `sanitize` L4070, `itemId` L4142, `specFor` L3942.

<a id="backup-03"></a>
## BACKUP-03 — Restauração por tipo e verificações de retorno

**Entradas.** Itens já sanitizados; extras de reversão por ação.

**Saídas e unidades.** psResult {error,message}; erro final lista t/name/el/k/id dos itens cujo bloco lançou.

**Regras extraídas.**

- Reg cria chave se necessária e aplica tipo original; DWord>Int32.MaxValue subtrai2^32; valor inexistente remove propriedade, sem remover a chave criada.
- Serviço usa sc.exe config start=auto/delayed-auto/demand/disabled e confere LASTEXITCODE; se running era true chama Start-Service com SilentlyContinue. Se running era false, não manda parar um serviço atualmente rodando.
- Scheme e pwr conferem exit code; pwr restaura só AC. Depois reativa o plano atual para efetivar índices. BCD /set verifica exit code, /deletevalue não verifica.
- TCP só aplica autotuning normal/highlyrestricted/restricted/disabled/experimental, RSS enabled/disabled, heuristics enabled/disabled; não verifica todos os exit codes de netsh.
- DNS encontra adaptador por GUID; NameServer não vazio vira lista por vírgula/espaço; vazio usa ResetServerAddresses. Adaptador ausente simplesmente não restaura nem lança.
- DRS inicializa, Set se exists, Del se ausente. runExtra pode lançar/rejeitar; quando só retorna {error}, o tratamento depende do chamador.

**Critérios para verificar uma implementação futura.**

- Serviço originalmente parado não volta necessariamente a parado: isso é lacuna concreta do original.
- DNS com ns vazio deve resetar automático; adaptador removido deve ser marcado como não verificável numa implementação mais transparente.
- Restaurar DWord4294967295 deve gravar representação int32 -1 preservando os bits.

**Limites observados.**

- Success do restaurador não garante pós-estado de todos os recursos: há chamadas silenciosas e caminhos de no-op.

**Evidência.** `restoreItems` L4197, `runExtra` L4204, `RESTORE_PS` L4013.

<a id="backup-04"></a>
## BACKUP-04 — Sobreposição de ações e restaurar tudo

**Entradas.** Ação, entries sequenciadas e estado atual do Windows.

**Saídas e unidades.** Revert individual {success,message}; restoreAll {success,count,message}.

**Regras extraídas.**

- Ao reverter A, para cada item original procura a primeira ação posterior por seq que contenha itemId igual. Se não houver, restaura agora; se houver, transfere o valor anterior de A para snapshot da ação posterior e não toca esse recurso agora.
- Só após restoreItems sem erro faz as transferências, remove entry A e salva. Depois roda extra. Se extra falhar, entry já foi removida, portanto não há atomicidade entre restauração e extra.
- Sem snapshot, usa REVERT fixo se existir; pode restaurar padrões em vez de estado original. Ação já ligada antes do GDZ com DETECT mas sem snapshot não tem estado anterior conhecido.
- restoreAll ordena seq desc e insere items em Map; entradas mais antigas sobrescrevem as novas para mesmo recurso, preservando valor anterior à primeira ação. Aplica uma vez, limpa entries e salva.
- Depois de limpar entries, restoreAll percorre extras. Lê apenas message; {error:...} de extra não derruba success, mas uma exceção/rejeição ainda propaga.
- actionStates produz mapa booleano por ação, não estados categóricos aplicado/externo/misto. Ausência de backup não significa desligado; a regra exata está em BACKUP-05.

**Critérios para verificar uma implementação futura.**

- Exemplo: A capturaX0 e põe1; B capturaX1 e põe2. ReverterA mantémX2 e troca snapshotB paraX0; reverterB devolveX0.
- restoreAll em A/B deve devolverX0, nãoX1.
- Falha em extra após remoção deve ficar visível como restauração parcial, sem declarar transação integral.

**Limites observados.**

- Snapshot de ação não captura alterações manuais posteriores. A nova implementação pode manter histórico e pós-verificação mais fortes sem copiar as lacunas.

**Evidência.** `revertAction` L4213, `restoreAll` L4276, `sameItem` L4439, `actionStates` L4491, `canRevert` L4212.

<a id="backup-05"></a>
## BACKUP-05 — Detecção do estado atual e igualdade por tipo

**Entradas.** Snapshots before/after e snapshot novo das specs cobertas, mais detectores booleanos independentes.

**Saídas e unidades.** Mapa {nomeDaAção:boolean}. Não expõe estado parcial detalhado no valor final.

**Regras extraídas.**

- actionStates junta specs únicas de entries que têm after array; lê snapshot atual e detectActions em paralelo. Falha de snapshot vira null; detectActions falho vira objeto vazio.
- Começa marcando true cada detector que retornou estritamente true. Depois marca toda ação com backup como true, mesmo se a verificação atual falhou.
- Para cada item válido do before: se existe after diferente do before, existe atual e atual voltou a ser igual ao before, troca o booleano da ação para detect[action]===true e para o loop. Basta um item alterado voltar ao before para essa regra disparar; não classifica restante como parcial.
- sameItem compara reg exists/kind/JSON.stringify(value); serviço somente mode/delayed, ignorando running; scheme/TCP strings lowercase; pwr somente ac; BCD exists/value; DNS normaliza espaços/vírgulas mas não ordena endereços; DRS exists/value.
- 720p Boost Ativo é sobrescrito pela resolução física primária calculada com size*scaleFactor=1280x720, independentemente do snapshot desse nome.

**Critérios para verificar uma implementação futura.**

- Serviço cujo StartMode permanece igual mas Running muda ainda é sameItem true.
- DNS [1.1.1.1,8.8.8.8] e ordem invertida diferem; espaço versus vírgula é normalizado.
- Com snapshot atual indisponível, uma ação com backup continua true; UI futura deve distinguir desconhecido de confirmado.

**Limites observados.**

- O booleano mistura detecção e presença histórica do backup. Não usar esse mapa como confirmação completa de que todos os valores da ação ainda estão aplicados.

**Evidência.** `sameItem` L4439, `actionStates` L4491, `detectActions` L3128.

<a id="fps-05"></a>
## FPS-05 — Overlay: janela, posição e atualização

**Entradas.** fpsOverlay {on,pos,show}; pos0 superior esquerdo,1 superior direito,2 inferior esquerdo,3 inferior direito.

**Saídas e unidades.** Janela Electron própria e evento overlay-update a cada500ms com {live,sys,show,right,bottom}.

**Regras extraídas.**

- Janela de260x230 e margem12 em unidades do Electron. Usa bounds da tela primária, não workArea. right=pos1/3 e bottom=pos2/3; coordenadas incluem origem x/y do monitor.
- Janela sem moldura,transparente,não redimensionável,não movível,não focável,fora da barra de tarefas,sempre no topo,sem sombra. Ignora eventos de mouse; nível screen-saver e showInactive.
- Carrega fps-overlay.html empacotado; nodeIntegrationfalse,contextIsolationtrue,sandboxfalse,preload do produto. Não injeta esse overlay no processo do jogo.
- A cada500ms chama fpsStart, fpsLive(fpsTarget()),sensorsSnapshot e moveTop. Abrir novamente só ajusta bounds e chama sensorsStart. Configuração show alterada para/recria coletores conforme handler.
- Ficar acima de janelas fullscreen depende do compositor e modo do jogo; essa rotina não contém hook DirectX para desenhar dentro da swapchain.

**Critérios para verificar uma implementação futura.**

- Posição deve usar dimensão fixa da janela e margem subtraídas dos bounds, evitando dupla aplicação de DPI.
- Overlay aberto deve impedir idle-stop de PresentMon; fechar deve liberar janela/timer e sensores próprios.

**Limites observados.**

- O propósito da janela é visualização, não geração de quadros. Não confundir este overlay com DrawFps do core Scaling.

**Evidência.** `overlayBounds` L7524, `overlayOpen` L7535, `overlayClose` L7586.

<a id="drs-01"></a>
## DRS-01 — Ponte NVIDIA DRS embutida e valores default

**Entradas.** nvapi64.dll instalado pelo driver; id e value uint32.

**Saídas e unidades.** Get retorna string decimal de override, string vazia para default/ausente, null se wrapper PowerShell não consegue ler.

**Regras extraídas.**

- Usa nvapi_QueryInterface via Cdecl e delegates. IDs: initialize0x0150E828,createSession0x0694D52E,loadSettings0x375DBD6B,getBaseProfile0xDA8466A0,getSetting0x73BF8338,setSetting0x577DD202,saveSettings0xFCBC7E14,deleteSetting0xE4A26362.
- Opera no base profile global, não em perfil por exe. A estrutura NVDRS_SETTING aloca12.320 bytes e zera em passos de4; versão=0x10000|12320.
- Offsets: id4100,type4104,isCurrentPredefined4112,currentValue8220. Só aceita type0 (DWORD). NOT_FOUND=-160 e isCurrentPredefined!=0 resultam string vazia.
- Set escreve id e value no buffer e Save após cada operação; Del aceita NOT_FOUND e também Save. Buffers são liberados no finally; o texto embutido não implementa DestroySession explícito.
- GdzDrsInit guarda sucesso em flag dentro do PowerShell atual; não persiste entre subprocessos. Falha de init retorna false; GdzDrsGet absorve erro e retorna null.

**Critérios para verificar uma implementação futura.**

- Override ausente e override explícito0 são diferentes e devem sobreviver a snapshot/reversão.
- Um tipo DRS diferente de DWORD deve ser rejeitado, sem ler layout como uint32 arbitrário.

**Limites observados.**

- Este contrato é da ponte embutida, não do corpo de nvapi64.dll. Compatibilidade depende do driver e ABI; não foi chamado driver para confirmar.

**Evidência.** `DRS_PS` L3184.

<a id="fivem-01"></a>
## FIVEM-01 — Localização, leitura e atualização do XML FiveM

**Entradas.** APPDATA/CitizenFX/gta5_settings.xml primeiro; LOCALAPPDATA/CitizenFX/gta5_settings.xml depois. Apply {preset?,scaling?,borderless?}.

**Saídas e unidades.** Status values mantém strings/null. Apply sucesso sem detalhar quantidade de tags escritas.

**Regras extraídas.**

- Escolhe primeiro caminho existente, não compara data nem caminho realmente usado pelo jogo. installed só verifica LOCALAPPDATA/FiveM/FiveM.exe e é separado de file.
- fivemRunning reconhece fivem.exe ou ^fivem_.*gtaprocess.exe$ na lista tasklist. Se leitura de processos falhar, o helper devolve false por ausência na lista.
- xmlGet busca primeira ocorrência <Tag whitespace value="..." com atributo value em primeiro lugar e aspas duplas. xmlSet troca apenas a primeira ocorrência no mesmo padrão; tag ausente permanece ausente.
- scaling deve ser inteiro0..4 e mapeia para SamplingMode [0,4,3,2,1]. Esse array é enum de configuração; não se deduz daqui razão de pixels exata.
- borderless só é aplicado se booleano: true→Windowed2,false→0. Apply não usa ScreenWidth/ScreenHeight apesar de status expô-los.
- Antes de mudar salva uma única cópia original no userData se ainda não existe; não vincula esse backup ao caminho fonte. Escreve .gdz-tmp e rename; não confere se todas as tags pretendidas existiam.

**Critérios para verificar uma implementação futura.**

- XML sem ShadowQuality deve continuar sem tag após preset: success não significa todos os campos aplicados.
- XML com value depois de outro atributo não corresponde ao regex, logo não é alterado.
- scaling4 deve gravar SamplingMode1, não4.

**Limites observados.**

- O editor é regex textual, não parser XML. Preset registrado em settings pode dizer escolhido sem refletir 100% do arquivo.

**Evidência.** `fivemXml` L8842, `xmlGet` L8851, `xmlSet` L8855, `fivemRunning` L8859, `FIVEM_KEYS` L8841, `FIVEM_SCALING` L8800.

<a id="fivem-02"></a>
## FIVEM-02 — Presets exatos e restauração FiveM

**Entradas.** Preset fps ou balanced; restore sem parâmetros.

**Saídas e unidades.** Apply grava settings.fivemPreset; restore copia backup para caminho encontrado (ou APPDATA padrão), apaga backup e remove preset salvo.

**Regras extraídas.**

- fps: MSAA/MSAAFragments/MSAAQuality/ReflectionMSAA/GrassQuality/ReflectionQuality/PostFX/SSAO/Tessellation=0; ShadowQuality/Shadow_SoftShadows=1; MaxLodScale=0.000000; Shadow_LongShadows/UltraShadows_Enabled/HdStreamingInFlight/DoF/TXAA_Enabled=false; MotionBlurStrength=0.000000.
- balanced: MSAA/MSAAFragments/MSAAQuality/ReflectionMSAA=0; GrassQuality/ReflectionQuality/SSAO/Tessellation=1; ShadowQuality/Shadow_SoftShadows/PostFX=2; MaxLodScale=0.000000; Shadow_LongShadows/UltraShadows_Enabled/DoF=false; MotionBlurStrength=0.000000.
- balanced não define HdStreamingInFlight nem TXAA_Enabled; alternar fps→balanced preserva o false anterior nesses campos. Nenhum preset define FXAA_Enabled ou LodScale.
- Apply/restore entram em withActionLock e recusam FiveM reconhecido em execução. Restore exige backup; usa copyFileSync e só depois remove original e saveSettings.
- Como backup é único, alterações manuais posteriores somem ao restaurar o arquivo inteiro. Se o caminho preferencial mudar de APPDATA para LOCALAPPDATA ou inverso, restore pode copiar para arquivo diferente do originalmente salvo.

**Critérios para verificar uma implementação futura.**

- Gerar quadro de diff dos atributos existentes para cada preset; comparar com tabela abaixo.
- fps→balanced deve manter TXAA_Enabled false. Restore deve devolver o arquivo inteiro byte a byte do backup, não só campos da lista.

**Limites observados.**

- Estado dos presets e arquivo real precisam ser reconciliados em uma nova implementação. Não assumir que balanced restaura todos os valores do preset fps.

**Evidência.** `xmlSet` L8855, `fivemRunning` L8859, `FIVEM_PRESETS` L8801.

<a id="opti-01"></a>
## OPTI-01 — Análise de compatibilidade de diretório OptiScaler

**Entradas.** Caminho Windows absoluto .exe que exista.

**Saídas e unidades.** {success,exe,dir,notes,upscalers:{dlss,fsr,xess},block,antiCheat,installed,foreign}.

**Regras extraídas.**

- Se exe não termina -Win64-Shipping.exe ou -WinGDK-Shipping.exe, procura em subpastas imediatas exceto Engine por <sub>/Binaries/Win64 ou WinGDK; usa primeiro Shipping encontrado.
- Raiz para inspeção sobe três níveis quando dir termina Binaries/Win64 ou WinGDK. Varredura até depth5 e20.000 entradas, pulando GDZ_OptiScaler.
- Reconhece DLSS/FSR/XeSS por nomes de DLL, não por análise dos imports nem render API. Anti-cheat é heurística de nomes de arquivos/pastas; devolve primeiros5.
- Se basename corresponde a qualquer GAMES fixo, produz block textual que a instalação recusa sem opção de ignorar. A heurística de antiCheat separada pode ser aceita somente pelo sinal explícito acceptAntiCheat do fluxo existente.
- foreign=true quando não há manifest GDZ e existe OptiScaler.ini ou OptiScaler.asi. Não detecta todos os mods possíveis por hash.
- CrimsonDesert.exe tem noSpoof=true e nota específica. exeRunning consulta Path exato case-insensitive de Get-Process; falha de consulta devolve false.

**Critérios para verificar uma implementação futura.**

- Launcher Unreal deve apontar para Shipping encontrado; bloqueio de catálogo conhecido tem precedência sobre confirmação de heurística.
- Ausência de DLL reconhecida não prova impossibilidade de upscaling; só indica a heurística.

**Limites observados.**

- Não foi feito download, injeção, hook ou instalação em jogo. A análise descreve o integrador; algoritmos de OptiScaler não estão contidos no JS.

**Evidência.** `optiAnalyze` L8289, `optiQuirk` L8371, `exeRunning` L8279.

<a id="opti-02"></a>
## OPTI-02 — Opções normalizadas e contrato INI

**Entradas.** upscaler auto/fsr/xess/dlss; fg off/dlssg/optifg; spoof/fps/menu booleanos; menuKey home/insert/end.

**Saídas e unidades.** Seções Upscalers,FrameGen,Spoofing,Menu escritas em OptiScaler.ini.

**Regras extraídas.**

- Defaults auto/off/spooftrue/fpsfalse/menutrue/menuKeyhome. Campos de tipo ou enum inválido mantêm default; propriedades desconhecidas são ignoradas.
- Upscalers Dx11/Dx12/Vulkan: auto=(auto,auto,auto);fsr=(fsr31,fsr31,fsr31);xess=(xess_12,xess,xess);dlss=(dlss,dlss,dlss).
- FrameGen Enabled/FGInput/FGOutput: off=(false,nofg,nofg);dlssg=(true,dlssg,fsrfg);optifg=(true,upscaler,fsrfg). Valores são strings no INI.
- Spoofing.Dxgi=false se GPU NVIDIA, opção spooffalse ou quirk noSpoof; nos demais casos auto. Menu OverlayMenu/ShowFps seguem booleanos; ShortcutKey home0x24,insert0x2D,end0x23.
- Editor INI mantém linhas alheias, identifica seção com regex e chave só se começar na coluna0 com alfanumérico/underscore seguido de =. Primeiro match alvo é substituído e retirado da lista pendente; duplicatas posteriores permanecem.
- Chaves faltantes são inseridas depois da linha da seção; seção ausente é acrescentada. Regrava tudo com CRLF, sem comparação de hash/pós-leitura. Chave indentada não é reconhecida e pode ganhar duplicata.

**Critérios para verificar uma implementação futura.**

- INI com "  Enabled=false" deve conservar linha indentada e inserir Enabled novo no alvo; isso é divergência a corrigir conscientemente.
- xess+dlssg deve produzir Dx11Upscaler=xess_12,FGInput=dlssg,FGOutput=fsrfg.
- CrimsonDesert.exe deve ter Dxgi=false mesmo em GPU não NVIDIA com spooftrue.

**Limites observados.**

- São opções de configuração de componente externo, sem garantia de suporte de cada jogo/API/GPU. Não confundir FGOutput=fsrfg com implementação própria do GDZ.

**Evidência.** `optiSanitizeOpts` L8460, `optiIniValues` L8372, `optiWriteIni` L8406, `OPTI_OPTS_DEFAULT` L8066, `OPTI_MENU_KEYS` L8074, `OPTI_QUIRKS` L8365.

<a id="opti-03"></a>
## OPTI-03 — Manifesto transacional de instalação e remoção

**Entradas.** exe,dll proxy,opts e pacote já preparado; diretório GDZ_OptiScaler dentro da pasta alvo.

**Saídas e unidades.** Manifest {v:1,tag,exe,dll,opts,at,files:[],backups:[],dirs:[],preexisting:[]}.

**Regras extraídas.**

- DLL proxy permitida dxgi.dll,winmm.dll,version.dll,dbghelp.dll,d3d12.dll,wininet.dll,winhttp.dll; inválida vira dxgi.dll. Renomeia OptiScaler.dll do pacote para o proxy escolhido.
- Cria GDZ_OptiScaler/backup e manifesto antes da cópia. Para cada arquivo: cria diretório faltante, move preexistente para backup, registra files/backups/dirs, salva manifesto, copia pacote.
- Pula .ok e setup_windows.bat/setup_linux.sh/!!*.txt segundo regex; a regex é aplicada ao caminho relativo inteiro, portanto o mesmo nome em subpasta pode não ser pulado.
- preexisting preserva apenas presença anterior de DlssOverrides. Ajusta INI; em erro chama optiRemoveFiles como tentativa de rollback e retorna mensagem de permissão ou falha.
- Remoção apaga arquivos registrados e logs OptiScaler.log/fakenvapi.log/dlssg_to_fsr3.log, remove DlssOverrides se não preexistia, renomeia backups de volta e tenta remover dirs vazios em ordem reversa.
- Se alguma remoção/restauração de arquivo falha, mantém diretório de manifesto; reporta até3 nomes como arquivos em uso. Erros removendo logs/dirs/marcador são absorvidos.
- settings.opti.installs é índice por dir {exe,name}; manifesto é fonte dos detalhes. Remover diretório sem manifesto simplesmente apaga o índice e retorna sucesso informando ausência.

**Critérios para verificar uma implementação futura.**

- Falha após mover arquivo original deve permitir recuperar backup pelo manifesto.
- Pasta DlssOverrides preexistente deve permanecer na remoção; gerada depois da instalação pode ser removida inteira.
- Hash do arquivo instalado não é comparado na remoção; nova implementação deve decidir como preservar mudanças posteriores do usuário.

**Limites observados.**

- Rollback tenta recuperar estado, mas não é atômico e seu erro pode não substituir a mensagem original. Validar caminhos/manifestos em implementação futura; não executamos remoção.

**Evidência.** `walkFiles` L8189, `optiInstall` L8527, `optiRemoveFiles` L8470, `optiUninstall` L8629, `optiManifest` L8270, `OPTI_DLLS` L8061.

<a id="telem-01"></a>
## TELEM-01 — Fila FPS, pares e envio de hardware

**Entradas.** Sessões FPS salvas, consentimento, hardware e estado autenticado online.

**Saídas e unidades.** telemetry-queue.json e chamadas POST /app/consent e /app/telemetry; nenhum endpoint foi contatado na análise.

**Regras extraídas.**

- Fila persiste últimos500 registros; lote50; flush a cada300.000ms. Antes de FPS exige consentimento true,fpsAvg>=1,frames>=30.
- Slot a cria pairId UUID e phase before, mesmo antes de decidir se vai enfileirar. Outro slot vira after apenas se nome exatamente igual à sessãoa, pairId existente e diferença de at<24h; caso contrário manual.
- Item inclui clientId,pairId,phase,game até64,durationS clamp5..120,fpsAvg/low1/low01 arredondados2 casas,frametimeAvg3,frames inteiro,optimizations até80 IDs cortados64,appVersion,capturedAt ISO.
- hardwareSnapshot junta capacidades, detalhes e display; RAM em GiB arredondado mínimo1, disco em GB decimais, resolução física=Electron size*scaleFactor.
- flush só roda sem concorrência,currentUser presente,não offline e loadAuth existente. Hardware é incluído quando SHA256(JSON.stringify(snapshot)) difere de telemetryHwHash, independentemente de consentimento FPS.
- Se há FPS e consentimento ainda não sincronizado, POST /app/consent telemetrytrue deve responder200 primeiro. Telemetria200 remove lote por clientId e atualiza hash de hardware.400 descarta FPS do lote; outros status deixam para próxima tentativa.
- saveTelemetryQueue não usa .tmp; erros de gravação são absorvidos. Leitura só exige array e não valida cada item nessa etapa.

**Critérios para verificar uma implementação futura.**

- Consentimento FPSfalse com hardware alterado ainda pode gerar body fps:[],hardware:{...} no alvo.
- Status400 deve remover somente clientIds do lote, sem marcar hardware como sincronizado.
- Nome do jogo com diferença de caixa não forma par nessa função, embora baseline use comparação case-insensitive.

**Limites observados.**

- Comportamento estático do cliente não comprova armazenamento/uso no servidor. Reimplementar contratos próprios de conta/consentimento, sem depender de infraestrutura privada do GDZ.

**Evidência.** `fpsTelemetryItem` L7653, `enqueueFpsTelemetry` L7680, `loadTelemetryQueue` L7638, `saveTelemetryQueue` L7646, `hardwareDetails` L7688, `hardwareSnapshot` L7794, `displayDetails` L7777, `flushTelemetry` L7817.

## Presets FiveM: tabela exata de valores

| Tag | fps | balanced |
| --- | --- | --- |
| MSAA | 0 | 0 |
| MSAAFragments | 0 | 0 |
| MSAAQuality | 0 | 0 |
| ReflectionMSAA | 0 | 0 |
| GrassQuality | 0 | 1 |
| ShadowQuality | 1 | 2 |
| Shadow_SoftShadows | 1 | 2 |
| ReflectionQuality | 0 | 1 |
| PostFX | 0 | 2 |
| SSAO | 0 | 1 |
| Tessellation | 0 | 1 |
| MaxLodScale | 0.000000 | 0.000000 |
| Shadow_LongShadows | false | false |
| UltraShadows_Enabled | false | false |
| HdStreamingInFlight | false | não altera |
| DoF | false | false |
| MotionBlurStrength | 0.000000 | 0.000000 |
| TXAA_Enabled | false | não altera |

## Contratos de arquivos e estado

| Arquivo | Base | Formato/papel | Regra de escrita/restauração |
| --- | --- | --- | --- |
| gdz-settings.json | userData | JSON livre de configurações; defaults autoMode/autoState, ramAuto/ramMinutes, fpsOverlay, fpsSessions, fivemPreset, opti.installs, telemetryConsent/telemetryHwHash. | Escrita .tmp+rename com erros absorvidos. Não foi aberto arquivo real de perfil. |
| gdz-backups.json | userData | {seq,entries:{action:{seq,at,items,after?}}}; oito tipos de recurso descritos em BACKUP-01/02. | Escrita .tmp+rename; exceções propagam conforme chamador. |
| gdz-disk-<sha1-prefix10>.cs/.dll | userData | Fonte C# e DLL cacheada derivada do hash do texto DISK_CS. | Compilação Add-Type apenas na execução do alvo; não realizada por esta análise. |
| fivem-gta5_settings.original.xml | userData | Cópia única do XML inteiro antes da primeira alteração; não registra caminho fonte. | Restore copia inteiro e apaga backup. |
| CitizenFX/gta5_settings.xml | APPDATA ou LOCALAPPDATA | XML com atributos value em tags; prioridade de busca APPDATA. | Apply usa .gdz-tmp+rename; backup separado. |
| GDZ_OptiScaler/manifest.json | pasta do executável real do jogo | Manifestv1 com arrays files/backups/dirs/preexisting e opções normalizadas. | Persistido antes de cada cópia para permitir rollback parcial. |
| GDZ_OptiScaler/backup/<relative> | pasta do jogo | Arquivos originais movidos antes da substituição. | Remoção devolve por rename; falhas conservam evidência. |
| OptiScaler.ini | pasta do jogo | INI textual das quatro seções descritas em OPTI-02. | Editor normaliza CRLF; pode conservar duplicatas. |
| optiscaler/<tag>/.ok | userData | Marcador de pacote local preparado; arquivos OptiScaler.dll e .ini precisam existir após extração. | Não é assinatura criptográfica; contrato de download está no catálogo IPC. |
| telemetry-queue.json | userData | Array com até500 itens FPS, envio em lotes50. | Escrita direta; itens removidos por clientId. |
| GDZ Scaling/Settings.xml | LOCALAPPDATA ou fallback appData | Perfis do wrapper WPF e ponte legado Electron. | Ver GDZ-SCALING-FUNCOES.md e GDZ-IPC.md; não atribuir HTML órfão à janela WPF. |
| gdz-<UUID>.ps1 | TEMP | Script temporário UTF-8 com BOM usado por execPSFile. | Unlink tentado no callback de conclusão. |

Nenhum arquivo real de conta, token, sessão ou perfil do usuário foi lido para produzir esta tabela. Os nomes e formatos vêm do código do pacote.

## Constantes extraídas por interpretação literal

| Nome | Linha | Tipo | Tamanho/itens |
| --- | --- | --- | --- |
| MEM_GEN | 1507 | dict | 10 |
| MEM_MAKER_ID | 1519 | dict | 12 |
| MEM_LAYOUT_PS | 1626 | str | 799 |
| STANDBY_PS | 1655 | str | 187 |
| RES_DLL | 1696 | str | 990 |
| BOTTLENECK_PS | 2243 | str | 2133 |
| BN_FIND_PS | 2352 | str | 652 |
| GAME_PS_HEAD | 2508 | str | 926 |
| GAMES_SCAN_PS | 2524 | str | 6230 |
| CLEAN_TAIL | 2814 | str | 773 |
| SHADER_CLEAN_PS | 2836 | str | 1058 |
| AUTO_APPS_LOW | 2857 | list | 5 |
| AUTO_APPS_CLOSE | 2858 | list | 3 |
| RAM_CLEAN_PS | 2952 | str | 1013 |
| RAM_INTERVALS | 2953 | list | 5 |
| STEAM_VDF_WRITE | 3147 | str | 1502 |
| DRS_PS | 3184 | str | 3130 |
| SNAPSHOT_PS | 3949 | str | 3492 |
| RESTORE_PS | 4013 | str | 3389 |
| DISK_CS | 4768 | str | 7520 |
| CLEAN_PS_HEAD | 5011 | str | 1555 |
| FPS_REC_SECS | 6253 | list | 3 |
| SENSOR_PS | 6281 | str | 817 |
| PCINFO_PS | 6471 | str | 7660 |
| PCLIVE_PS | 6597 | str | 1362 |
| OPTI_DLLS | 8061 | list | 7 |
| OPTI_OPTS_DEFAULT | 8066 | dict | 6 |
| OPTI_MENU_KEYS | 8074 | dict | 3 |
| OPTI_QUIRKS | 8365 | dict | 1 |
| FIVEM_SCALING | 8800 | list | 5 |
| FIVEM_PRESETS | 8801 | dict | 2 |
| FIVEM_KEYS | 8841 | list | 16 |

## Reprodução e limites da evidência

Com as dependências Babel já instaladas em `.tools/gdz-static`, executar somente as ferramentas próprias:

```powershell
node scripts/extract-gdz-contracts.cjs
python scripts/build-gdz-algorithm-contracts.py
```

A ferramenta lê o JS como AST e aceita apenas literais, arrays/objetos literais, `String.raw` sem interpolação, concatenação literal e `join` literal. Nenhum `eval`, `vm`, import do alvo, chamada Win32, NVAPI ou PowerShell é efetuado. Os blocos recuperados ficam em `artifacts/private/gdz-analysis/deep/embedded-contracts.json` para inspeção, sem execução.

A especificação estruturada está em `artifacts/private/gdz-analysis/deep/algorithm-contracts.json`, com vínculo por linha/offset/hash. Este capítulo não afirma engenharia reversa completa de motores nativos fechados nem do servidor remoto; separa o que já pode ser reimplementado por regras verificáveis das dependências que continuam externas.
