# GDZ Optimizer: o que cada função faz

**Aprofundamento posterior:** a [especificação para implementação](GDZ-ESPECIFICACAO-PARA-IMPLEMENTACAO.md) reúne comandos/valores completos, algoritmos, a janela WPF decompilada e a nova desmontagem nativa com 300 shaders. Este índice preserva o catálogo funcional anterior; as limitações nativas descritas abaixo correspondem àquela etapa e foram parcialmente reduzidas no novo relatório.

Manual técnico da versão **1.1.27** instalada neste computador, aprofundado em **08/11/2026**. A análise recuperou strings e expressões do backend, conferiu o catálogo da interface e leu metadados/IL dos auxiliares. Nenhuma otimização, limpeza, reparo, função exportada ou chamada IPC do produto foi executada.

## Por onde começar

| O que você quer entender | Entrega |
|---|---|
| Cada botão, opção e recurso do programa | [Catálogo de recursos e controles](GDZ-RECURSOS-DETALHADOS.md) |
| Cada função interna declarada no processo principal | [159 funções: entradas, saídas e efeitos](GDZ-FUNCOES-INTERNAS.md) |
| Helpers atribuídos a variáveis, incluindo geradores de comandos | [70 funções complementares](GDZ-HELPERS-EXPRESSOES.md) |
| O que cada pedido da interface faz no backend | [87 chamadas IPC, 9 mensagens e 4 eventos](GDZ-IPC.md) |
| Scaling, geração de quadros e integração nativa | [533 métodos do auxiliar, perfil e 11 exports](GDZ-SCALING-FUNCOES.md) |
| Como o backend ofuscado foi tornado legível | [Método, transformações e validação](GDZ-DESOFUSCACAO.md) |
| Arquitetura e identificação dos arquivos originais | [Relatório inicial](GDZ-ENGENHARIA-REVERSA.md) e [auxiliares PE/CLR](GDZ-AUXILIARES.md) |

Para consultar um identificador, use Ctrl+F no capítulo correspondente. Os catálogos identificam funções, payloads, respostas, comandos, valores do Registro quando recuperados, mecanismos de reversão e limites de confirmação. “Gera script” e “executa script” são efeitos diferentes e aparecem separados.

## O que o programa faz na prática

| Área | Mecanismo observado | Limite importante |
|---|---|---|
| Energia e desempenho | Planos `powercfg`, Modo de Jogo, DVR, políticas, serviços e preferências gráficas | As regras mudam conforme hardware; algumas opções são bloqueadas, por exemplo em X3D de dois CCDs |
| Mouse e teclado | Valores de aceleração, repetição, acessibilidade e Num Lock | Teste de polling é estimativa de eventos de mouse no renderer, não medição elétrica da porta USB |
| Memória | Leitura de RAM/SMBIOS e purga de cache standby por API nativa do Windows | Limpar cache não aumenta a RAM física e o valor liberado não comprova ganho de FPS |
| Internet | DNS, propriedades do adaptador e parâmetros TCP previstos no catálogo | Alterações de configuração não demonstram por si só menor ping |
| Armazenamento | Medição por categorias, idade de arquivos, caches, Lixeira e worker de ocupação de disco | Limpeza remove arquivos; snapshot de configurações não restaura automaticamente os arquivos apagados |
| Programas | Inventário Uninstall, desinstalador do app e busca heurística de sobras | Semelhança de nome de pasta/publicador é uma heurística, não prova de que todo conteúdo seja sobra |
| Jogos e modo automático | Descoberta de bibliotecas/processos, prioridades, fechamento/reabertura de apps e troca de plano | O modo automático tem efeitos em processos reais quando ativado; não foi ativado nesta inspeção |
| Gargalo | Amostragem de CPU total, maior núcleo, GPU e RAM, com pausa por perda de foco | Resultado final é uma heurística; não é diagnóstico causal garantido |
| FPS | PresentMon fornece intervalos de apresentação; o app calcula médias e lows | Mede apresentações de quadros; não mede input lag nesse fluxo |
| Scaling | O menu inicia o auxiliar WPF ligado a `Lossless.dll`; o pacote também contém ponte de configuração e HTML de outra interface | Presença de HTML não comprova uso no fluxo principal; algoritmos e shaders nativos não foram integralmente reconstruídos |
| DLSS/FSR/OptiScaler | Descoberta de jogo, download de pacote, cópia/backup de DLLs e edição de INI | O GDZ integra um pacote; o JavaScript não contém a implementação dos upscalers |
| Histórico | Snapshot tipado, sequências e restauração de configurações | Não é imagem completa do sistema nem backup geral de jogos e arquivos |

## Achados que não apareciam na análise superficial

**O medidor de FPS tem fórmulas concretas.** A média é `quantidade_de_frames × 1000 / soma_dos_frametimes_ms`. O “1% low” usa o inverso da média dos 1% frametimes mais lentos; o “0,1% low” faz o mesmo com 0,1%. Ambos usam ao menos um frame. O baseline exige pelo menos 30 frames dos últimos cinco segundos. São cálculos sobre a amostra recebida, sem prova automática de que uma mudança no Windows causou a diferença.

**A limpeza de RAM purga standby.** `cleanRam` prepara um helper C# para ajustar privilégio e chamar `NtSetSystemInformation` com a operação de purga. A diferença de cache antes/depois vira o número de MB liberados. Há controle de concorrência e tratamento do retorno do Windows; a rotina não foi chamada nesta análise.

**A restauração considera sobreposição entre ações.** `revertAction` identifica itens também usados por uma ação posterior e transfere para o backup posterior o estado antigo apropriado. `restoreAll` reúne o estado mais antigo por item. Isso é mais elaborado do que apenas inverter um switch, mas não demonstra uma proteção completa contra mudanças externas ou recuperação de arquivos removidos.

**Hardware e métricas de FPS têm condições diferentes de envio.** No corpo de `flushTelemetry`, estar autenticado e online habilita a tentativa. Os eventos de FPS dependem de `telemetryOn()`. O hardware pode ser incluído quando seu hash muda, mesmo sem esses eventos. O código envia para `/app/telemetry` e só marca o hash como enviado após HTTP 200. Foi examinada a rotina, não o tráfego real da sessão.

**Há uma interface HTML de Scaling empacotada que não deve ser confundida com a janela efetivamente aberta.** `openScalingWindow` inicia `GDZOptimizer.exe`, o auxiliar WPF. Não foi localizado carregamento de `src/gdz-scaling.html` nesse fluxo. Nesse HTML, `Clip cursor`, `Max frame latency` e `Draw FPS` não entram no objeto produzido por `getConfig()`, e o seletor de perfil só muda aparência/rótulo. O motor gerenciado possui parâmetros correspondentes: essas limitações do HTML não demonstram defeitos equivalentes na janela WPF.

**Enviar um campo à ponte legada não garante que ele seja gravado.** Na configuração via `ls-launch`/`scalingFromUi`, a conversão de `perfMode` gera `LSFGSize`, mas a allowlist de escrita não inclui essa tag. Algumas opções de FSR, algoritmo de escala e preenchimento também não coincidem com os enums aceitos. Há ainda diferença entre a tag de nitidez geral e a usada por LS1. O [contrato IPC](GDZ-IPC.md) identifica essas incompatibilidades; elas não devem ser generalizadas para opções configuradas diretamente pelo auxiliar WPF.

**Nem todo canal permitido tem implementação encontrada.** `ls-browse-exe` e `ls-find-exe` estão na allowlist do preload, mas não foram encontrados entre os handlers registrados do backend examinado. São 87 nomes permitidos e 85 handlers identificados. `fps-update` é permitido como evento recebido, mas seu emissor não foi encontrado nessa versão; `overlay-update` é enviado à janela própria de overlay.

Os handlers `gs-minimize` e `gs-maximize` existem, mas têm corpo vazio nessa versão. Isso não descreve os controles de janela próprios do WPF. O catálogo diferencia “permitido”, “registrado” e “implementa uma operação”.

**O autostart principal usa tarefa agendada.** O GDZ principal registra tarefa de logon com `RunLevel Highest`. Já o auxiliar Scaling tem fluxo separado em HKCU Run. São dois mecanismos diferentes, descritos nos respectivos catálogos; nenhum deles foi alterado nesta inspeção.

**Os auxiliares com nome GDZOptimizer não são dois motores independentes de tweaks.** Os apphosts de Scaling apontam para `LosslessScaling.dll`. Os 11 exports observados em `Lossless.dll` recebem as opções gráficas; `ApplySettings` tem 33 parâmetros. A leitura permite identificar essa fronteira, sem afirmar autoria ou reconstruir a implementação gráfica nativa.

## Cobertura e o que continua desconhecido

- **177 recursos e controles** estão no catálogo: 78 entradas de PAGES e 99 itens adicionais. Os 71 itens operacionais de PAGES têm evidência do backend; as sete dicas são orientações. Há ainda 37 helpers relevantes do renderer explicados separadamente. Essas contagens não significam 177 otimizações.
- **229 funções com nomes semânticos no backend** foram explicadas individualmente: 159 declarações e 70 expressões atribuídas a variáveis. Os nomes originais dos parâmetros, linhas e offsets foram preservados como evidência.
- **87 chamadas permitidas, 9 mensagens e 4 eventos** têm inventário de contrato; ausências e registros condicionais são explicitados.
- **533 métodos de 50 tipos CLR** do auxiliar foram enumerados e classificados, incluindo acessores, recursos e código gerado. Assinaturas P/Invoke e chamadores gerenciados não significam que o corpo nativo foi decompilado.
- **5.019 nós de função JavaScript em 21 fontes próprias** foram inventariados por AST, incluindo callbacks e estruturas de ofuscação. Esse inventário estrutural não é uma alegação de explicação manual de cada callback anônimo ou de todas as funções das bibliotecas terceiras.

A contagem anterior de “175 funções” vinha de busca textual e incluía 16 funções PowerShell dentro de strings. A análise sintática separou essas ocorrências das 159 declarações JavaScript reais. A contagem de recursos, a de handlers e a de funções não devem ser somadas como se fossem unidades equivalentes.

O backend foi recuperado por interpretação restrita de dados, com 61.803 substituições registradas e 13 regressões aprovadas. Estado mutável permaneceu dinâmico. A versão focada serve à leitura e não foi executada como substituta do aplicativo. Não há prova de equivalência integral nem validação dinâmica dos efeitos de todos os comandos. A etapa posterior [desmonta exports e shaders nativos](GDZ-MOTOR-NATIVO-APROFUNDADO.md), mantendo explicitamente abertas a reconstrução completa do pipeline gráfico e a implementação do servidor remoto.

## Arquivos e reprodução

O código original do GDZ continua na instalação. Extrações e views ficam somente em `artifacts/private/gdz-analysis`, fora de `src` e das releases do VL Optimizer. Os relatórios e as ferramentas novas são próprios deste trabalho.

```powershell
# Inventariar/extrair textos da instalação sem iniciar seus executáveis:
powershell -NoProfile -File '.\scripts\analyze-gdz.ps1'

# Recuperar strings/expressões do snapshot já extraído, sem executá-lo:
Set-Location '.'
node .\scripts\decode-gdz-static.cjs

# Gerar catálogos a partir das anotações e evidências locais:
python .\scripts\build-gdz-internal-catalog.py
python .\scripts\build-gdz-bound-catalog.py
```

As dependências Babel e os limites da ferramenta estão no [método de recuperação](GDZ-DESOFUSCACAO.md). A execução da análise não aplicou otimizações, não chamou serviços de conta/licença e não consultou tokens ou configurações pessoais em Roaming.
