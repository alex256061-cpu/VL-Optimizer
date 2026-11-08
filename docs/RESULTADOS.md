# Resultados — VL Optimizer 0.4.0

As 13 opções principais solicitadas estão no menu: Painel; Desempenho; Mouse e teclado; Memória; Limpeza; Programas; Internet e live; Windows; Inicialização; Meus jogos; DLSS / FSR; VL Scaling; Medidor de FPS. Hardware, histórico e configurações ficam nos atalhos inferiores. A identidade vermelha/preta do VL foi mantida.

O [mapa de funções](FUNCOES-DO-MENU-0.4.0.md) cruza as 78 entradas recuperadas com os mecanismos atuais e distingue ajustes, ferramentas, orientações manuais e substituições deliberadas. O [JSON](../artifacts/menu-functions-0.4.0.json) conserva os vínculos e a descrição mecânica da amostra analisada.

Foram ligados 57 ajustes às áreas apropriadas; a seleção de uma área não aplica itens ocultos de outra. A busca abre a aba do ajuste. A contagem de Inicialização é real. Navegação larga permanece aberta e telas menores usam expansão/rolagem.

Novos caminhos: dez opções do INI OptiScaler com prévia, backup, verificação e restauração; overlay externo FPS; reativação dos serviços Windows Update com snapshot; solicitações de Defender/Proteção do Sistema e confirmação de estado disponível. Dicas BIOS/OBS/driver continuam identificadas como ações manuais, sem alterar políticas ou componentes ocultamente.

Validação: 218/218 testes isolados e build Release sem avisos/erros. Smoke de leitura: 16 destinos, quatro páginas legadas, 22 subabas novas e abas existentes, 57 PNGs. Estilos Win32 de passagem de cliques/não ativação do overlay foram conferidos no próprio HWND. Foram inspecionados painel, menu, desempenho, rede, DLSS/FSR, OptiScaler e FPS em render real WPF.

O motor gráfico GDZ/Lossless não foi reconstruído. VL Scaling guarda/exporta 40 propriedades e abre manualmente um programa externo; não promete aplicar esses perfis nele. O overlay exige janela/sem bordas e fica na tela principal. Nenhuma captura de jogo ou alteração real de Windows/Defender/serviços/Proteção do Sistema foi executada nesta revisão. Mutações e integração gráfica ainda exigem testes em VM/hardware/driver distintos.

Evidências públicas: artifacts/test-results.json, artifacts/smoke-summary.json, artifacts/catalogo-0.4.0.json, artifacts/menu-functions-0.4.0.json e artifacts/release-validation-0.4.0.json. Evidências completas e fixtures permanecem em artifacts/private. Relatórios 0.3.0 foram preservados em docs/historico/0.3.0.
