# Resultados — VL Optimizer 0.3.0

Entrega de 08/11/2026 sobre o projeto fornecido. O ZIP 0.2.0 de referência foi preservado, com SHA-256 `42091a133c2dd63a18f35c1459e286bb98aaca007fb2ad94ebc5f29b06749274`.

## Implementado

O aplicativo passou de duas famílias de alteração para um catálogo de 56 configurações e serviços de limpeza, jogos, sessões, FiveM, GPU por executável, FPS/frametime, memória, disco, programas/Store, hardware e manutenção. A interface VL existente foi mantida e recebeu onze páginas, sete abas de medições e três de jogos. A comparação detalhada com o GDZ e o mapeamento dos nomes recuperados estão em [EVOLUCAO-0.3.0.md](EVOLUCAO-0.3.0.md).

Foram melhorados: busca global, estado vazio/indisponível, seleção para revisão, diferenciação entre selecionar e estar ativo, origem das métricas, progresso real, tratamento parcial e restauração. Uma revisão em lote impede aprovar configurações que compartilham o mesmo recurso, evitando que um snapshot fique obsoleto pela ação anterior do próprio lote.

O Core guarda estados exatos, verifica depois de escrever e preserva conflitos externos. Snapshots de serviços/pagefile/DNS/vídeo incluem seus estados compostos; falhas intermediárias observadas continuam recuperáveis. Os arquivos FiveM, bibliotecas locais OptiScaler e preferências GPU possuem backups por caminho, integridade e restauração condicional.

## Evidência final

| Verificação | Resultado |
| --- | --- |
| Build Release | 0 avisos, 0 erros |
| Testes isolados | 204/204 passaram |
| Catálogo | 56 definições, incluindo 33 tweaks |
| Smoke do portátil autocontido | Saída 0, somente leitura, onze páginas |
| Subpáginas | Sete abas de medições e três de jogos renderizadas |
| Busca e comparação | Exercitadas pelo smoke |
| Dimensões de janela | 1366×768, 1536×884, 683×384 DIPs |
| Interop somente leitura | PDH RAM/CPU/GPU, DNS, vídeo, pagefile, sete fontes WMI e inventário UWP |
| Scripts PowerShell | Sete scripts, parser Windows PowerShell 5.1 sem erro |
| Publicação | Portátil autocontido, ZIP e instalador IExpress 0.3.0 |
| Instalador | CAB extraído sem executar instalação; cinco arquivos e hashes do manifesto conferidos |
| Correspondência | EXE do instalador idêntico ao portátil por SHA-256 |
| Alterações reais no PC | Nenhuma configuração, limpeza, purge, kill, desinstalação ou manutenção executada |

Evidências públicas: `artifacts/test-results.json`, `artifacts/smoke-summary.json`, `artifacts/catalogo-0.3.0.json`, `artifacts/release-validation-0.3.0.json` e `release/VERIFICACAO-0.3.0.json`. Capturas completas/fixtures ficam em `artifacts/private`, excluídas do código-fonte distribuído.

## Limites materiais

O editor de scaling e a instalação local OptiScaler não equivalem a reconstruir o motor nativo do GDZ. Seus quarenta campos são armazenados e exportados pelo VL; um programa externo selecionado é configurado manualmente. Não há LSFG/upscaling próprio, overlay sobre jogos nem aplicação automática dos perfis em outro produto.

PresentMon é um coletor externo opcional e não foi executado em um jogo nesta entrega. Drivers NVIDIA DRS e todas as mutações Windows precisam de validação em ambiente descartável. Os resultados não comprovam ganho de FPS nem vantagem de consumo sobre GDZ.

Temperaturas, estado comprovado XMP/EXPO/Resizable BAR, medição física USB, driver próprio, backend remoto, licença/conta, autoatualização e assinatura de código permanecem fora da implementação. Instalar/desinstalar o próprio VL em VM e a matriz de DPI/acessibilidade ainda são pendências de homologação. Consulte [VALIDACAO.md](VALIDACAO.md).