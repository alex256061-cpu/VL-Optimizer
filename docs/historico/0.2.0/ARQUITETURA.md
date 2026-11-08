# Arquitetura do VL Optimizer

Revisão 0.2.0 registrada em 08/11/2026. Esta documentação descreve o desenho e os limites do MVP; resultados de execução ficam em [VALIDACAO.md](VALIDACAO.md). Nenhum número de consumo, ganho de FPS ou certificação comercial é pressuposto.

## Objetivo e sequência

O produto segue **analisar → explicar → selecionar → aplicar → verificar → comparar → desfazer**. A abertura normal inicia uma primeira análise somente leitura para preencher o painel, sem exigir um clique adicional. Uma análise não autoriza uma alteração. A seleção é explícita, sem itens de inicialização previamente marcados e sem aplicação automática durante a abertura. Modos de smoke controlam a própria sequência de coleta e captura.

Desempenho potencial, armazenamento, preferências e manutenção são categorias distintas. A contagem de recomendações não representa porcentagem de desempenho. Uma observação curta pode indicar algo a investigar; não confirma gargalos nem prova ganho.

## Plataforma e escolha da interface

O alvo principal é Windows 11 x64, C# e .NET 10 LTS com WPF. A aplicação deve verificar arquitetura, versão/build, edição e privilégios antes de disponibilizar ações. Windows 10, Windows ARM64 e Windows Server não são plataformas validadas por esta entrega.

A política oficial consultada em 08/11/2026 mantém .NET 10 suportado até novembro de 2028. É necessário manter o patch de manutenção atualizado; suporte da plataforma não equivale a teste do aplicativo em toda edição/build. [Política .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [sistemas suportados pelo .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).

| Opção | Integração e manutenção | Instalação e interface | Decisão para este MVP |
|---|---|---|---|
| .NET 10 + WPF | APIs Windows por interop e bibliotecas .NET; uma linguagem para lógica e UI | XAML, binding, templates, vetores e animações; publicação autocontida possível | Escolhida: boa integração e escopo de distribuição previsível para um produto somente Windows |
| .NET + WinUI 3 | APIs modernas e controles Fluent; também exige interop para diagnóstico e operações | Windows App SDK acrescenta decisões de runtime, bootstrap e empacotamento | Alternativa válida; a aparência premium desejada não exige introduzir essa dependência nesta fatia |
| Tauri 2 | Backend Rust e ponte de comandos para a UI web | Usa WebView2 no Windows; requer validar runtime, fronteira IPC e acessibilidade web | Benefício maior se houver equipe web ou exigência multiplataforma; essas condições não existem no escopo atual |
| Electron | Ecossistema web e backend Node.js; integração nativa ainda precisa ser implementada | Embute Chromium e Node.js e exige manutenção desses componentes | Não selecionado: não há necessidade funcional que justifique adicionar esse runtime ao otimizador |

Não foram medidos comparativos de RAM, CPU, GPU ou tamanho entre essas opções. A tabela é uma decisão de arquitetura, não um benchmark. Fontes primárias: [WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/), [distribuição Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview), [pré-requisitos Tauri](https://v2.tauri.app/start/prerequisites/), [Electron](https://www.electronjs.org/docs/latest/).

## Componentes e limites

| Componente | Responsabilidade | Limite |
|---|---|---|
| Interface WPF | Navegação, seleção, prévia, estados, foco, acessibilidade e animações | Não escreve no Registro nem aplica planos diretamente |
| Contratos/Core | Modelos de diagnóstico, recomendações, prévias, operações e estados | Sem dependência de janelas ou XAML |
| Coletor Windows | Snapshot, amostras curtas de CPU, memória, discos, processos, inicialização e energia | Falhas individuais produzem avisos e campos indisponíveis, não valores fabricados |
| Motor de diagnóstico | Interpreta os dados coletados, explica limitações e prioriza recomendações | Não diagnostica gargalo só pelo modelo do hardware ou por um pico |
| Catálogo | Declara operações suportadas, versão, risco, fonte e reversão | Apenas `DisableStartup` e `SetPowerPlan` escrevem configurações no MVP |
| Executor | Revalida a prévia, grava estado anterior, aplica e verifica | Sem execução arbitrária de comandos ou scripts recebidos da UI |
| Histórico/restauração | Diário persistente e reversão com detecção de conflito | Não sobrescreve silenciosamente mudanças posteriores |
| Persistência local | Arquivos locais versionados com gravação atômica | Não é backup integral do Windows; dados não saem do PC pelo fluxo básico |

O contrato `IOptimizerBackend` expõe análise, prévia, aplicação, restauração, histórico e recomendações. As operações são identificadas por `OperationKind` e um alvo específico: identidade da entrada de inicialização ou GUID de plano. `CatalogVersion` registra a versão do comportamento aplicado; a versão do aplicativo deve ser registrada na distribuição e nos resultados de validação.

## Diagnóstico e coleta

A análise realiza uma janela curta de aproximadamente cinco segundos. O tempo efetivo, horário e quantidade de amostras devem acompanhar o resultado. Enumerações e recursos indisponíveis podem acrescentar tempo; a interface mostra a etapa real e progresso indeterminado. Não simula porcentagem de conclusão.

| Informação | Fonte pretendida/implementável sem agente externo | Interpretação |
|---|---|---|
| Windows, build e privilégios | APIs/.NET e metadados do Windows | Verificar compatibilidade antes de escrever |
| Nome de CPU/GPU | Inventário Windows, consultado de forma pontual | Nome não estabelece gargalo ou capacidade medida |
| CPU média e série curta | Diferenças de `GetSystemTimes` | `KernelTime` inclui `IdleTime`; subtrair Idle uma vez |
| RAM instalada | `GetPhysicallyInstalledSystemMemory` | Retorna KB; pode diferir da memória utilizável |
| RAM utilizável e disponível | `GlobalMemoryStatusEx` | Bytes; disponível não é sinônimo de memória fisicamente instalada |
| Memória comprometida | `GetPerformanceInfo` | `CommitTotal × PageSize`; não inferir apenas por RAM ocupada |
| Discos | `DriveInfo`/APIs Windows | Capacidade e espaço livre, sem medir latência ou atividade do disco |
| Processos | `System.Diagnostics.Process` | Working set e, quando coletado, delta de tempo de CPU; tolerar acesso negado e saída do processo |
| Inicialização | HKCU Run e origens adicionais somente leitura quando disponíveis | A cobertura é parcial; não inclui todos os mecanismos de logon |
| Energia | `PowerEnumerate`, `PowerGetActiveScheme`, `GetSystemPowerStatus` | Planos disponíveis, GUID ativo e bateria/tomada |
| Reinicialização pendente | Sinais conhecidos disponíveis no Windows | Indicação complementar; não é detector universal de todos os instaladores |

Uso de GPU, sensores/temperaturas, atividade percentual de disco, FPS e frametime não são implementados nesta fatia. Esses campos devem aparecer como indisponíveis ou fora do escopo, nunca como zero. Não há driver próprio nem biblioteca de sensores com acesso ao kernel.

`GetSystemTimes` tem limitação documentada em máquinas com mais de 64 processadores: os valores se referem ao grupo primário da thread chamadora. Essa classe de equipamento exige validação e coleta por grupos antes de alegar utilização global. [CPU](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes), [RAM instalada](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getphysicallyinstalledsystemmemory), [RAM disponível](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex), [comprometimento](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-performance_information).

## Escrita, privilégio e reversão

A UI usa `asInvoker`. As operações do MVP são locais ao usuário: gerenciamento limitado de HKCU Run e seleção de plano de energia existente pelas APIs oficiais. Se política, permissão ou plataforma bloquear a ação, a aplicação deve registrar o motivo e não tentar contornar a restrição.

Para energia, a recusa é tratada pelo código de retorno de `PowerSetActiveScheme`. A versão atual não chama `PowerSettingAccessCheck` durante a prévia; portanto, só a execução determina se o Windows permitirá aquela escrita. A verificação posterior confere o plano ativo, sem presumir sucesso pelo envio da solicitação.

Não há serviço privilegiado, broker elevado nem pedido genérico de administrador nesta fatia. Operações futuras que precisem de elevação exigirão um contrato separado, com lista fechada de ações, canal autenticado, validação dos parâmetros e revisão específica. O usuário não deve precisar iniciar o painel como administrador para analisar seu PC.

O executor deve:

1. Ler novamente o alvo e comparar com a prévia aprovada.
2. Persistir o estado anterior antes de qualquer escrita.
3. Executar uma operação curta e definida.
4. Ler novamente para verificar o resultado; somente então marcar conclusão.
5. Atualizar o diário de maneira atômica.
6. Na restauração, comparar o estado atual com o último estado verificado e parar em conflito.

Uma interrupção entre a alteração do Windows e a gravação final do diário é possível: não existe transação atômica conjunta entre o Registro/energia e um arquivo JSON. Um registro pendente após fechamento inesperado precisa ser conciliado com o estado real antes de oferecer nova aplicação. Registros cuja verificação não foi concluída não equivalem a sucesso.

Os estados distinguem pendente, concluído, ignorado, falhou, reinicialização necessária, não verificado, revertido e conflito. Cancelamento é observado entre etapas seguras. Uma escrita única já iniciada termina e é verificada antes de atender o cancelamento.

O diário deve usar arquivo temporário no mesmo diretório, flush e substituição/rename atômico. Atomicidade evita arquivo parcialmente gravado; não assegura durabilidade absoluta contra perda de energia nem torna o backup imune a alteração por programas do mesmo usuário. Guardar o tipo e valor original do Registro sem expandir variáveis.

## Interface, movimento e acessibilidade

Navegação: visão geral, inicialização, energia, armazenamento, processos/diagnóstico, histórico e preferências. O destaque principal leva de analisar a revisar, e de revisar à prévia aprovada. Histórico e restauração devem estar sempre localizáveis. Energia usa planos realmente enumerados, identifica o ativo e permite revisar a mudança em um painel compacto; antes da coleta, oferece o diagnóstico dentro de um único painel de orientação.

A identidade usa a logo VL fornecida e o sistema vermelho/preto de `Palette.cs`, refletido nos recursos de `App.xaml`. O pedido explícito do usuário de 08/11/2026 substitui a paleta roxo/lima do exemplo e a opção anterior por órbitas/circuitos estáticos. A composição da referência é mantida sem copiar a marca ou o código do aplicativo mostrado.

`DashboardDesign.cs` constrói o shell e a composição. A navegação reserva uma rail de 72 DIPs; o drawer tem 264 DIPs reais, `Grid.ColumnSpan=2`, fundo opaco e recorte animado. Ele sobrepõe o conteúdo sem reflow. A animação de abertura usa recorte de 200 ms, opacidade de rótulos de 160 ms e transformação de 200 ms; não altera largura a cada frame. A navegação por teclado revela rótulos imediatamente e conserva nomes acessíveis.

`MotionScene.cs` contém `AnimatedOrbitVisual`, `AnimatedCircuitVisual`, `SceneClocks`, `MotionFeedback` e `MotionTransitions`. As órbitas têm transformações independentes em torno da logo VL fixa; circuitos usam trilhas e pulsos luminosos. Os desenhos são retidos e os movimentos usam relógios nativos WPF, sem reconstruir geometrias em `CompositionTarget.Rendering`. Os relógios ambientes solicitam no máximo 60 atualizações por segundo, evitando seguir automaticamente os 165 Hz do monitor observado; esse limite não comprova que 60 FPS foram sustentados. Microinterações de ponteiro/pressão e transições usam opacidade/transformação e spline `(.23, 1, .32, 1)`, retargetando do valor atual.

O contrato `IMotionVisual` expõe a ativação e o estado de execução. O controlador de janela registra as cenas e centraliza a permissão: animações do Windows, movimento reduzido, modo econômico, visibilidade e estado da janela. Minimização, cenas descarregadas e preferências de movimento suspendem os relógios; a restauração retoma a animação quando permitida. Eventos do sistema são removidos no fechamento. A análise somente leitura em andamento pode terminar, com cancelamento disponível.

O círculo representa etapa de análise ou contagem explicitamente legendada. Cartões e listas mostram dados reais com horário da leitura. O detalhe de recomendação explica motivo, risco, estado anterior, mudança, fonte, limitações e reversão. Movimento não simula porcentagem, pontuação de desempenho ou resultado de otimização.

Validar teclado, foco visível, nomes acessíveis e contraste. WPF usa unidades independentes de resolução, mas isso não dispensa teste em 125%, 150% e 200%. [DPI WPF](https://github.com/microsoft/WPF-Samples/blob/main/PerMonitorDPI/readme.md).

A validação separa render estático de execução temporal: `--smoke-test` captura composição; `--motion-smoke` captura 30 quadros, compara pixels e verifica pausa/retomada. O teste temporal confirma comportamento observado nessa sequência, não um orçamento de CPU/GPU nem uma meta de 60 FPS. Evidências e resultados ficam em [VALIDACAO.md](VALIDACAO.md) e [RESULTADOS.md](RESULTADOS.md).

## Armazenamento e funcionalidades adiadas

A tela de armazenamento apresenta discos e acesso às páginas oficiais do Windows. Não exclui temporários, Downloads, documentos, cache, sessões de navegador ou Lixeira. A liberação de espaço efetivamente obtida por este MVP é zero enquanto não existir limpeza própria.

Uma próxima etapa de limpeza só entra após validar catálogo de inclusão, escopo de caminhos, links/junctions, corrida entre análise e exclusão, arquivos em uso, seleção, prévia e cálculo do resultado. Ponto de restauração não recupera necessariamente arquivos e não substitui backup.

Não fazem parte do MVP: tweaks de rede/BCD/HPET, desativação de segurança/updates/serviços, limpeza de Registro/RAM, drivers, overclock, encerramento forçado de processos, reparos pesados e alegações de ganho em jogos. Assinatura e atualizações verificadas são preparação de distribuição futura, descrita em [DISTRIBUICAO.md](DISTRIBUICAO.md).
