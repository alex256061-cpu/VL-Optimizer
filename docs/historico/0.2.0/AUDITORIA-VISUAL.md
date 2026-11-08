# Auditoria visual e de animações — 0.2.0

Referência analisada em 08/11/2026: `Desktop 2026.11.08 - 10.16.27.01.mp4`, fornecido pelo usuário. A gravação tem 55,666 segundos, resolução de 1920 × 1080 e 3.340 quadros. Houve decodificação sequencial de todos os quadros, inspeção de sequências consecutivas nas regiões do aplicativo e comparação com a captura enviada e o código do VL Optimizer.

A identidade solicitada nesta rodada é **vermelho e preto**, com o monograma VL. O vídeo usa roxo e verde-lima; sua composição e seu comportamento servem de referência para a adaptação da identidade.

## Antes / depois / motivo

| Antes | Depois nesta rodada | Motivo |
| --- | --- | --- |
| Logo vermelha cercada por botões verde-lima, ícones roxos, painéis arroxeados e cores de foco inconsistentes. | Paleta centralizada em `Palette.cs`: fundo `#080809`, painéis escuros, destaque `#FF3542`, variações vermelhas e texto claro. Recursos XAML, barras, switches, bordas e diálogos acompanham a identidade. | Atender ao pedido de vermelho/preto em toda a interface, incluindo estados de interação. |
| O menu revelava os rótulos, mas eles eram cortados na largura da coluna de 72 px. A largura do filho mudava sem reservar uma camada adequada. | O menu ocupa uma camada de 264 px que atravessa as duas colunas, acima do corpo. Um recorte revela 72 px recolhido ou 264 px expandido; a posição do conteúdo permanece fixa. | Eliminar o recorte de layout imposto pelo WPF e reproduzir a navegação sobreposta da referência. |
| A abertura e o recolhimento do menu aconteciam instantaneamente. | Recorte com transição de 200 ms; rótulos com opacidade em 160 ms e deslocamento curto em 200 ms. A navegação por teclado revela o menu imediatamente. | O vídeo mostra recolhimento suave entre aproximadamente 17,337 e 17,487 s. O teclado precisa alcançar os itens sem aguardar a apresentação visual. |
| As elipses orbitais eram desenhadas com ângulos constantes; em repouso a composição ficava parada. Apenas a análise criava um spinner. | Três órbitas possuem escala e inclinação independentes, com variação suave. Logo e marcações do mostrador permanecem orientadas. | Entre 1,200–3,950 s e 5,100–6,400 s, a referência mostra precessão das elipses, com sensação de volume, mesmo sem análise em andamento. |
| O fundo era uma malha de circuitos inteiramente estática. | A malha e os chips permanecem fixos; pequenos pulsos luminosos vermelhos com cauda percorrem seis caminhos por transformações animadas. | No vídeo, pontos de luz seguem trilhos diagonais, verticais e horizontais. Exemplos visíveis em 2,000–2,500 s e 16,000–18,300 s. |
| Na primeira execução de validação da nova cena, os relógios permaneciam pausados apesar de estarem criados. A sequência capturada continuava idêntica. | A inicialização usa `Resume` e `SeekAlignedToLastTick` para posicionar a fase inicial, preservando `Pause`/`Resume` nas interrupções seguintes. | Criar animações no código não comprova movimento na tela. A falha de inicialização dos clocks foi localizada com a captura temporal e corrigida antes da nova verificação completa. |
| O comportamento contínuo não possuía uma coordenação única com visibilidade, minimização e preferências de movimento. | `SceneClocks` coordena as cenas; movimento reduzido, economia, minimização, invisibilidade e remoção da árvore visual suspendem os relógios. A retomada preserva a fase. | Fazer a interface animar enquanto está visível e respeitar os estados que pedem suspensão. |
| A nova página tinha uma transição simples e isolada; os controles só mudavam de estado por setters instantâneos. | Entrada de página com opacidade e deslocamento curto em 200 ms; feedback de ponteiro com escala sutil e transições que partem do valor atual. Entradas de teclado permanecem imediatas. | A referência troca de página por volta de 6,483–6,700 s com uma transição curta. Interrupções não devem reiniciar a interação de forma brusca. |
| Diálogos abriam sem uma camada visual que separasse claramente a decisão do conteúdo de fundo. | Uma camada preta translúcida cobre a janela proprietária durante o diálogo; o conteúdo do diálogo tem entrada curta e segue a paleta vermelha. | A referência escurece e desfoca o fundo durante a entrada do modal em aproximadamente 4,017–4,167 s. Esta implementação utiliza escurecimento, sem afirmar equivalência do desfoque. |
| Antes da análise, Energia empilhava dois grandes avisos que repetiam o próximo passo, com o botão isolado abaixo. | Um único card inicial de largura limitada reúne a explicação e a ação. Após a coleta, a página apresenta o contexto de alimentação e os planos realmente identificados. | Melhorar hierarquia e ocupação da página sem preencher o estado inicial com métricas inventadas. |
| A verificação anterior gerava capturas estáticas com movimento reduzido forçado. Isso não demonstrava as animações solicitadas. | A rodada inclui uma sequência de 30 capturas da interface com movimento habilitado, uma sequência isolada do mesmo fundo animado e pares de captura para estados suspensos. | Confirmar mudança real de pixels e suspensão efetiva, além da presença de animações no código. |

## O que foi comparado

A captura de reclamação mostra a página **Energia**. A órbita da referência aparece no **Painel**; sua ausência específica em Energia não é um defeito. A diferença de movimento nessa página era o fundo de circuitos parado.

O desenho ambiente permanece estável: os efeitos são as órbitas e os pulsos sobre os trilhos. A geometria foi mantida em visuais WPF retidos; os relógios modificam transformações, sem reconstruir a malha a cada quadro.

## Verificação e evidências

Os resultados executados da rodada devem ser consultados nos arquivos gerados pelo aplicativo e pelo comparador:

| Evidência | O que permite verificar |
| --- | --- |
| `artifacts/private/final-motion-smoke/motion-smoke-result.json` | Movimento observado no Painel, suspensão por pausa, movimento reduzido, economia e minimização; retomada; largura do menu e encaixe dos rótulos; limites da órbita e tempos das capturas. |
| `artifacts/private/final-motion-smoke/motion-verification.json` | Diferença média absoluta RGB, quantidade de pixels alterados e igualdade exata dos pares suspensos. Contém os critérios e o resultado quantitativo do comparador. |
| `artifacts/private/final-motion-smoke/motion-000.png` a `motion-029.png` | Sequência da mesma interface em repouso, com movimento habilitado. |
| `artifacts/private/final-motion-smoke/circuit-000.png` a `circuit-029.png` | Renderização isolada do mesmo circuito ao vivo. Permite confirmar seus pulsos mesmo quando cards ocultam os caminhos na interface completa. |
| `artifacts/private/final-motion-smoke/paused-0.png` / `paused-1.png`, `reduced-0.png` / `reduced-1.png`, `economy-0.png` / `economy-1.png` | Pares usados para exigir igualdade exata nos estados suspensos. |
| `artifacts/private/final-motion-smoke/sidebar-expanded.png`, `sidebar-collapsed.png` e `sidebar-compact-expanded.png` | Inspeção do menu expandido, recolhido e em janela compacta. |
| `artifacts/private/final-motion-smoke/energy-before-analysis.png` e `energy-after-analysis.png` | Organização da página Energia antes e depois de coletar os dados reais. |
| `artifacts/VL-Optimizer-animacoes.gif` e `artifacts/VL-Optimizer-animacoes.mp4` | Prévia compartilhável com 30 quadros reproduzidos a 10 fps; essa reprodução não mede FPS do aplicativo. |
| `artifacts/private/final-smoke-02/smoke-result.json` e capturas associadas | Smoke estático do EXE final: sete páginas, duas análises, busca, comparação e layouts de 1366×768, 1536×884 e 683×384 DIPs. |
| `artifacts/motion-summary.json` | Resumo público dos resultados temporais sem os snapshots privados da máquina. |

O comparador considera alterado um pixel cuja maior diferença absoluta de canal RGB seja **maior que 2**. Para pausa, movimento reduzido e economia, exige igualdade exata da imagem inteira. Quando a série isolada do circuito está disponível, registra `CircuitSource: isolated-render-of-same-live-visual` e conserva as métricas da região correspondente da interface completa em `circuit_shell`.

### Resultado da execução final

O EXE autocontido 0.2.0 final foi executado diretamente nos modos estático e temporal, ambos com código de saída 0. A compilação Release terminou com 0 avisos/erros e a rodada atual do motor aprovou 33/33 testes isolados. Os dois JSONs temporais registram sucesso.

| Critério temporal | Resultado observado |
| --- | --- |
| Sequência ativa | 30 quadros com timestamps, amostrados nominalmente a cada 100 ms |
| Região orbital | Mudança RGB > 2 em 29/29 pares consecutivos; diferença absoluta RGB média de 0,97958333 |
| Mesmo circuito ao vivo, renderizado isoladamente | Mudança RGB > 2 em 29/29 pares consecutivos; diferença absoluta RGB média de 0,03215827 |
| Região `circuit_shell` da interface completa | 0/29 pares com mudança; o recorte selecionado permaneceu estático/coberto pela composição |
| Pausa explícita | Igualdade exata da imagem inteira entre `paused-0.png` e `paused-1.png` |
| Movimento reduzido | Igualdade exata da imagem inteira entre `reduced-0.png` e `reduced-1.png` |
| Economia | Igualdade exata da imagem inteira entre `economy-0.png` e `economy-1.png` |
| Minimização e restauração | Relógios suspensos ao minimizar e retomados ao restaurar |
| Drawer | Alocação overlay, encaixe dos rótulos e recolhimento aprovados |

O circuito usa `CircuitSource: isolated-render-of-same-live-visual`: o teste isola o mesmo visual ativo, sem criar uma animação de demonstração alternativa. O recorte `circuit_shell` está preservado no relatório e sua ausência de variação não deve ser apresentada como movimento visível naquela área da janela.

O ambiente registrou WPF Tier 2 em monitor de 165 Hz. Os relógios têm limite desejado de 60 quadros/s. A prévia GIF/MP4 reproduz apenas os 30 quadros capturados a 10 fps. Nem o limite configurado nem a prévia ou o comparador comprovam 60 FPS, fluidez contínua ou ganho de desempenho do computador.

As amostras curtas de consumo, a inspeção do instalador e as pendências de validação estão em [RESULTADOS.md](RESULTADOS.md). O instalador não foi executado e nenhuma otimização foi aplicada ao PC. A revisão corrige os problemas identificados nesta comparação, sem afirmar equivalência de todos os pixels ou ausência de todos os defeitos.

As sequências da referência e a análise anterior à correção estão em `artifacts/private/video-motion-audit/`, incluindo `orbit-clean.png`, `circuit-sequence.png`, `sidebar-collapse.png`, `page-change.png`, `modal-enter.png` e `COMPARACAO-VIDEO.md`.
