# Resultados da entrega — 08/11/2026

VL Optimizer **0.2.0**, prévia técnica de validação. Compilação Release sem avisos/erros, 33/33 testes isolados aprovados e os dois smokes do EXE autocontido final concluídos com código de saída 0. Nenhuma otimização ou alteração de configuração foi aplicada ao computador de desenvolvimento; o instalador foi inspecionado sem instalação.

## Revisão visual solicitada

A identidade agora é vermelho/preto em toda a interface: superfícies, CTAs, ícones, seleção, foco, barras, switches e diálogos. O pedido explícito do usuário substituiu o roxo/lima do exemplo e a escolha anterior de manter os elementos decorativos estáticos. A logo VL fornecida e a fonte Inter embutida, com licença incluída, foram preservadas.

A navegação recolhida tem 72 DIPs; o drawer opaco tem alocação real de 264 DIPs e se expande sobre o conteúdo, sem deslocá-lo ou cortar rótulos. A cena principal apresenta três órbitas independentes ao redor da logo fixa. Pequenos pulsos vermelhos com cauda percorrem seis trilhas do circuito; chips e malha permanecem parados. São desenhos retidos e relógios nativos WPF, com suspensão por movimento reduzido, economia, minimização e remoção da árvore visível.

A abertura normal inicia um diagnóstico somente leitura. Energia usa um painel inicial compacto com ação integrada; depois da análise, mostra os planos realmente identificados e o plano ativo. Revisão e aprovação continuam obrigatórias antes de qualquer alteração disponível. A auditoria detalhada está em [AUDITORIA-VISUAL.md](AUDITORIA-VISUAL.md).

![Captura atual](../artifacts/VL-Optimizer-preview.png)

![Prévia das animações](../artifacts/VL-Optimizer-animacoes.gif)

A prévia animada contém 30 quadros com reprodução a 10 fps. Ela demonstra os movimentos capturados; sua taxa de reprodução não mede o FPS do aplicativo.

## Verificações executadas

| Verificação | Resultado da 0.2.0 |
|---|---|
| SDK | 10.0.403 local; arquivo oficial previamente conferido por SHA-512 |
| Compilação Release da solução | 0 avisos, 0 erros |
| Testes do motor com adaptadores falsos | 33/33 passaram na rodada atual |
| Diagnóstico real sem administrador | Windows 10 Pro x64, build 19045; somente leitura; sistema permanece com alterações bloqueadas |
| Smoke estático do EXE final | Sucesso, código de saída 0; sete páginas, duas análises, busca e comparação |
| Capturas estáticas | 1366×768, 1536×884 e 683×384 DIPs; não equivalem a uma matriz de DPI real |
| Smoke temporal do mesmo EXE final | Sucesso, código de saída 0; 30 quadros com timestamps reais |
| Órbitas | 29/29 pares consecutivos com mudança de pixels acima do limiar RGB > 2 |
| Circuito isolado do mesmo visual ao vivo | 29/29 pares consecutivos com mudança de pixels acima do limiar RGB > 2 |
| Pausa, movimento reduzido e economia | Pares da imagem inteira exatamente iguais em cada estado suspenso |
| Minimização/restauração | Relógios suspensos e retomada confirmados pelo smoke |
| Sidebar | Alocação overlay, rótulos inteiros e recolhimento confirmados; capturas aberta/recolhida e compacta |
| Scripts PowerShell | Sete arquivos analisados pelo parser do Windows PowerShell 5.1, sem erros de sintaxe |
| Publicação | EXE autocontido, ZIP portátil e instalador IExpress 0.2.0 gerados |
| Conteúdo do instalador | CAB extraído sem iniciar instalação; cinco arquivos; hashes dos três arquivos do manifesto conferidos |
| Correspondência do binário | EXE no instalador idêntico ao portátil por SHA-256 |
| Privilégio do instalador | Manifesto PE confirma `requestedExecutionLevel=asInvoker` |

Os testes isolados cobrem prévia somente leitura, aprovação fabricada/reutilizada, idempotência, plataforma não suportada, falha de permissão, verificação sem efeito, falha parcial, restauração de tipo/valor, conflitos posteriores, plano removido, cancelamento, nomes com espaços/acentos, diário interrompido/corrompido e coordenação entre executores. Não substituem alterações em uma VM Windows 11.

Evidências públicas resumidas: [test-results.json](../artifacts/test-results.json), [smoke-summary.json](../artifacts/smoke-summary.json) e [motion-summary.json](../artifacts/motion-summary.json). Evidências completas ficam em `artifacts/private/final-smoke-02/` e `artifacts/private/final-motion-smoke/`, fora do pacote público de fontes porque incluem dados da máquina.

## O que a sequência temporal demonstra

O comparador usa 30 capturas da interface e 30 capturas isoladas do mesmo fundo ao vivo. Um pixel conta como alterado quando a maior diferença absoluta entre canais RGB supera 2. A região orbital variou nos 29 pares adjacentes. O circuito isolado também variou nos 29 pares.

A região `circuit_shell` escolhida na janela completa permaneceu estática nos 29 pares, pois o recorte estava coberto pela composição nessa sequência. Esse recorte não comprova movimento visível do fundo naquela região. A métrica primária de circuito vem da renderização isolada do mesmo visual ativo, registrada como `CircuitSource: isolated-render-of-same-live-visual`.

Pausa explícita, movimento reduzido e economia produziram igualdade exata da imagem inteira em seus pares de comparação. O smoke também confirmou suspensão ao minimizar, retomada ao restaurar e limites dos rótulos do menu. O ambiente registrou renderização WPF Tier 2. Os relógios têm limite desejado de 60 quadros/s para evitar acompanhar desnecessariamente o monitor de 165 Hz; isso é uma configuração, não medição de 60 FPS alcançados.

## Amostras de consumo

Equipamento: Ryzen 7 7800X3D, 16 processadores lógicos, 32 GB de RAM instalada, RTX 4070 SUPER e GPU AMD integrada. Windows 10 Pro x64, com outras ferramentas abertas. As leituras abaixo vêm do smoke temporal final, após a sequência de capturas, com animações habilitadas no estado visível.

| Estado | Duração | Working set | Memória privada | CPU do app normalizada por 16 lógicos |
|---|---:|---:|---:|---:|
| Visível, cenas animadas | 3,000 s | 213,16 MiB | 184,42 MiB | 1,009% |
| Minimizado | 3,001 s | 199,81 MiB | 171,98 MiB | 0,000% na amostra |

Leituras por `Process.WorkingSet64`, `PrivateMemorySize64` e diferenças de `TotalProcessorTime`. São amostras curtas; não estabelecem orçamento permanente de memória/CPU, consumo de GPU ou melhora de desempenho do computador.

Um probe separado, com apenas as duas cenas WPF em 1536×884 e amostras de 8 s, observou CPU normalizada de 1,099% com limite desejado de 60 e 4,761% sem esse limite, no monitor de 165 Hz e Tier 2. Esses valores justificam limitar o ritmo dos relógios nesse ambiente; não são uma comparação do aplicativo inteiro nem medição de FPS. Fontes locais: `artifacts/private/tests/motion-clock-probe/profile-60.json` e `profile--1.json`.

## Artefatos

Os arquivos 0.2.0 ficam em `release`: EXE portátil, ZIP portátil, instalador, ZIP de fontes, `VERIFICACAO.json` e `SHA256SUMS.txt`. Versões antigas podem continuar na pasta; usar os nomes que contêm `0.2.0`. Os hashes identificam os arquivos produzidos, sem substituir assinatura do publicador.

O EXE portátil tem aproximadamente 135,58 MiB e o instalador 46,58 MiB. O runtime .NET acompanha a publicação. Os dois smokes executaram diretamente o EXE final, sem depender do host do SDK.

O instalador foi extraído e inspecionado; **não foi instalado nem desinstalado nesta máquina**. Nenhum plano de energia, entrada de inicialização ou outra configuração do Windows foi alterado pela validação.

## Limites e próximos critérios de liberação

Permanecem pendentes integração de aplicação/restauração em Windows 11 x64, políticas reais, notebook em bateria/tomada, instalação/desinstalação em VM, DPI real entre monitores, leitor de tela e profiling de GPU, latência e fluidez contínua. A composição e as animações foram comparadas ao vídeo, sem alegação de igualdade de todos os pixels ou ausência de todos os defeitos.

A entrega continua como prévia técnica. Windows 10/Server permanecem em diagnóstico. Não há assinatura, autoatualizador, limpeza própria, temperaturas ou utilização de GPU/disco. As limitações aparecem no produto e na documentação.

A preparação comercial está em [DISTRIBUICAO.md](DISTRIBUICAO.md), os contratos e fontes em [CATALOGO-E-EVIDENCIAS.md](CATALOGO-E-EVIDENCIAS.md) e a matriz em [VALIDACAO.md](VALIDACAO.md).
