# Revisão visual e de interação — 0.3.0

A interface vermelha/preta e o monograma VL foram preservados. A referência GDZ foi usada para avaliar hierarquia de entrada, localização dos recursos e organização em áreas. Nenhum recurso visual do GDZ foi copiado para o aplicativo.

## Mudanças verificadas

- Cinco cartões de entrada levam às áreas principais; onze destinos na navegação lateral.
- Catálogo filtrável por categoria e busca, com opções avançadas em expanders e antes/depois em revisão.
- Seleção de configuração usa checkbox e texto Selecionar para revisão; não finge exibir o estado ligado/desligado sem consulta.
- Sete abas de medições e três de jogos organizam os novos serviços. A navegação por teclado preserva foco na aba/categoria recriada.
- Seletores apresentam títulos humanos, após a inspeção revelar exibição acidental do texto interno dos objetos.
- Reabrir elevado aparece como ação secundária; não preenche a largura como uma recomendação prioritária.
- Configurações sobrepostas em um lote ficam indisponíveis na revisão, com motivo, antes de qualquer escrita.
- Dependências ausentes, fontes indisponíveis, resultado parcial e ausência de um motor gráfico próprio ficam explícitos.

## Evidência e limites

O EXE portátil foi exercitado em modo somente leitura. O smoke percorre todas as páginas e subabas, busca, duas análises e comparação, registra 26 PNGs e encerra com sucesso. Foram inspecionados o painel, catálogo, captura FPS, sessões, programas, manutenção, scaling e composição compacta.

Dimensões de render: 1366×768, 1536×884 e 683×384 DIPs. A janela compacta usa rolagem e reorganiza o cabeçalho. Isso não comprova troca de DPI real, leitor de tela ou todos os diálogos preenchidos. A publicação foi iniciada para inspeção própria, sem aplicar configurações.

Os componentes orbitais/circuitos, pausa ao minimizar, modo econômico e movimento reduzido existentes continuam no projeto. Os ensaios temporais da versão 0.2.0 foram preservados em `historico/0.2.0/AUDITORIA-VISUAL.md`; não são apresentados como novas medições desta versão.

Não há medição comparativa de latência, consumo, FPS da UI ou desempenho de jogos contra o GDZ. A melhora afirmada é de cobertura funcional, organização, estados e revisão de operações.