# Direção visual — VL Optimizer 0.2.0

Revisão de 08/11/2026 após a comparação solicitada pelo usuário entre o vídeo original e a captura do aplicativo. O pedido atual exige **vermelho com preto e animações visíveis como no vídeo**. Essa orientação substitui o roxo/lima da referência e a decisão anterior de manter os elementos decorativos estáticos. A composição continua baseada no vídeo; a marca e os dados pertencem ao VL Optimizer.

## Referência e proporções

Fonte: vídeo fornecido pelo usuário, com inspeção da sequência e dos estados de navegação. A interface externa do ChatGPT e o aviso do gravador não pertencem ao aplicativo. No quadro aos 2 segundos, o aplicativo ocupa aproximadamente x191–1729/y78–963 de um vídeo 1920×1080, isto é, 1538×885.

- Barra de título própria: 36 DIPs, logo/nome à esquerda e ações funcionais/controles de janela à direita.
- Navegação: rail recolhida de 72 DIPs e drawer opaco de 264 DIPs. A expansão sobrepõe o conteúdo, sem deslocar a tela. A largura real do drawer é alocada nas duas colunas da janela; um recorte revela apenas a rail quando recolhida.
- Os rótulos têm espaço real e permanecem inteiros quando abertos. O ponteiro revela o drawer com recorte de 200 ms e opacidade de 160 ms; o foco por teclado revela os rótulos imediatamente. A saída do ponteiro recolhe o menu quando ele não está sendo usado por teclado.
- Cabeçalho: saudação/título, texto secundário e busca à direita; busca abaixo do título em janelas estreitas.
- Conteúdo: margem aproximada de 28 DIPs, compatível com um painel desktop.
- Primeira linha: painel principal de aproximadamente 65% e hardware de 35%. Texto e ação à esquerda, órbitas com a logo VL à direita, cantos marcados por pequenos colchetes vermelhos.
- Hardware: CPU, GPU e RAM em três linhas, com ícone, nome, leitura e barra contextual. Informação indisponível não produz barra numérica.
- Segunda linha: cinco categorias compactas, na mesma linha quando houver largura.
- Terceira linha: recomendações compactas em duas colunas, com detalhe sob demanda.

A visão principal deve caber na primeira tela equivalente à referência. Em áreas menores, reorganizar e permitir rolagem, conservando a ordem visual. As páginas específicas usam os dados reais da área. Energia apresenta um painel compacto de planos existentes, ativo identificado e ação de revisão; antes da coleta, mostra uma orientação com CTA dentro do painel, sem dois grandes avisos vazios.

## Cores, fontes e identidade

`src/VLOptimizer.App/Palette.cs` é o conjunto de tokens da interface C#: `Canvas #080809`, `Panel #141113`, `Raised #21171B`, `Accent #FF3542`, `AccentBright #FF7179`, `AccentDeep #971D2B`, `Text #F4F1F2`, `Muted #B1A6AD`, `Line #38282F` e `Circuit #332028`. `App.xaml` mantém os recursos dos controles na mesma direção. Botões principais usam vermelho profundo e texto branco; foco, seleção, barras, ícones e estados ativos seguem a paleta, sem roxo ou verde-limão.

Usar Inter 4.1 embutida, com corpo regular/semibold e títulos fortes em Inter Display. A fonte vem do repositório oficial rsms/inter e tem licença SIL OFL 1.1 incluída em `Assets/Fonts/OFL.txt`, na distribuição e na interface de licenças. A logo VL original permanece preservada e ancorada no centro da cena orbital; não deve girar com os anéis.

Ícones consistentes, superfícies pretas com leve tom vermelho, bordas finas, raio discreto e brilho localizado. O fundo tem circuitos, chips geométricos e pequenos nós; sua decoração permanece atrás do conteúdo e preserva a leitura.

## Movimento

O pedido explícito do usuário torna o movimento ambiente parte desta revisão. As três órbitas têm movimentos independentes de rotação/precessão e escala, em torno de uma logo fixa. O fundo percorre seis trilhas com pulsos vermelhos e pequenas caudas luminosas; a malha e os chips permanecem parados. Essas cenas usam desenhos retidos e relógios nativos de animação WPF; não recompõem geometria em um callback por frame. Os relógios ambientes solicitam no máximo 60 atualizações por segundo, evitando seguir automaticamente os 165 Hz do monitor observado; esse limite não comprova que 60 FPS foram sustentados.

Botões e categorias têm resposta curta de ponteiro/pressão; páginas e diálogos usam opacidade e transformação. A expansão do drawer anima o recorte, os rótulos e sua opacidade, sem recalcular a largura por frame nem mover o conteúdo principal. Os efeitos não deslocam dados enquanto o usuário os lê.

Movimento reduzido, modo econômico, preferência de animações do Windows e minimização suspendem os efeitos contínuos. Cenas fora da árvore visível não mantêm animação ativa. A restauração da janela retoma as cenas se o movimento estiver permitido. O fluxo funcional permanece disponível nos estados estáticos.

## Fidelidade sem inventar resultados

O elemento orbital é identidade e indicação de análise/recomendações; não representa potência, FPS ou percentual fictício. Manter dados reais, prévia e confirmação, histórico e restauração. A abertura normal inicia uma primeira análise somente leitura para preencher o painel; não aplica alterações nem seleciona itens automaticamente.

Não adicionar botões sem função para reproduzir a referência. Tutorial, preferências e controles de janela executam o que anunciam. Inicialização usa linhas compactas com detalhes e seleção explícita; alternar uma seleção não modifica o Windows. A disposição visual pode seguir o exemplo sem incorporar a marca GDZ, código ou bloqueio comercial.

## Revisão e evidências

Comparar capturas da janela inteira e do drawer aberto/recolhido com a referência. O smoke estático verifica composição, estados e recortes; uma imagem não demonstra movimento. O modo `--motion-smoke` amostra 30 quadros e compara pixels nas órbitas e no circuito, além de verificar pausas e retomada. O resultado deve ser registrado após a execução; a quantidade de quadros e os tempos configurados não comprovam 60 FPS.

Registrar novos screenshots, logs e hashes após a reconstrução. Um render em DIPs não equivale a validar todos os DPIs reais entre monitores. Resultados executados e pendências pertencem a [VALIDACAO.md](VALIDACAO.md) e [RESULTADOS.md](RESULTADOS.md).
