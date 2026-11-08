> Revisão 0.4.0: veja [funções do menu](FUNCOES-DO-MENU-0.4.0.md). São 13 entradas principais, três atalhos inferiores e quatro páginas auxiliares preservadas. Os detalhes históricos 0.3.0 estão em `historico/0.3.0`.

# Validação — VL Optimizer 0.4.0

Revisão de 08/11/2026. Esta validação não aplicou ajustes, limpeza, purga, kill, desinstalação ou manutenção reais ao computador de desenvolvimento.

## Executado

- Build Release da solução, sem avisos e sem erros.
- Suíte executável com cenários isolados e arquivos de fixture. 218/218 passaram; detalhes em `artifacts/test-results.json`.
- Configurações: os 33 tweaks e 24 definições adicionais, snapshots tipados, ausência de chave/valor, estado composto inválido, prévia forjada/replay, privilégio/build, dependências, falha parcial e reabertura/undo.
- Serviços: CSV/frames/lows, leitura incremental e cancelamento, limpeza e identidade de arquivo, XML FiveM, manifests Steam/Epic, diagnóstico, sessões/PID reutilizado, uninstall/sobras, processos protegidos e pacotes UWP.
- Integração: journal antes da escrita, sobreposição e planos legados, persistência corrompida, perfis scaling e pacote OptiScaler sintético/restore/conflito. As fixtures PE não contêm programa executado.
- Hardware/GPU por jogo: falha parcial de fonte, origem/limites, estatística de eventos, mesclagem de tokens, hash do executável, estado anterior e restauração após falha.
- Smoke nativo somente leitura Windows 10 Pro x64 build 19045: análise, RAM/standby PDH, CPU/núcleo/GPU 3D, DNS, modo de vídeo, pagefile WMI e sete grupos de hardware WMI. Não chama setters.
- Smoke do EXE portátil final com bloqueio de mutação: 16 destinos do menu, quatro páginas auxiliares, 22 subabas das áreas, sete abas de medições, três de jogos, busca e comparação; 57 PNGs em três dimensões. Não executa recursos externos nem abre diálogos de arquivo para simular sucesso.
- Editor OptiScaler: UTF-8/UTF-16, comentários, chaves desconhecidas, valores fechados, rejeição de ambiguidades, prévia sem escrita, replay, conflito, hash do jogo, restauração em ordem e backup corrompido. Pacotes de teste não executam DLLs.
- Overlay: estilos Win32 de passagem de cliques, janela de ferramenta e não ativação relidos no HWND próprio. A renderização de uma janela invisível não comprova compatibilidade com jogos, tela cheia exclusiva ou anticheat.
- Instalador: CAB extraído sem executar a instalação; cinco arquivos, manifesto conferido e executável idêntico ao portátil. Sete scripts analisados pelo parser do Windows PowerShell 5.1, sem erros.

Os resultados detalhados de máquina, caminhos, processos e screenshots completos ficam em `artifacts/private`, excluídos dos fontes distribuídos. O resumo público contém contagens/estados, sem inventário completo.

## O que os testes verificam

As fronteiras falsas contam escritas e simulam acesso negado, retorno sem efeito, falha depois de escrever, cancelamento, PID reaproveitado e mudanças externas. Arquivos de fixture são reais no diretório privado de testes. Os testes confirmam que ausência de alteração, conflito e interrupção não são tratados como sucesso, e que os dados anteriores são gravados antes de operações recuperáveis.

A inspeção WMI real encontrou incompatibilidade entre resultado forward-only e enumeração COM scripting; o adaptador foi corrigido para resultado bidirecional e as sete consultas responderam. Isso ilustra a diferença entre um teste de contrato e validação da ABI no sistema.

A captura PresentMon não foi executada contra um jogo. Os parsers e o tail incremental foram exercitados com CSVs/streams controlados. Baixo 1%/0,1% é o inverso da média dos frames mais lentos da fração, não percentil instantâneo. FPS ao vivo é média acumulada dos frames publicados no CSV, não FPS instantâneo do monitor.

## Pendente para homologação

1. Mutação/undo em VMs Windows 10/11 elegíveis, normal/admin, políticas corporativas e arquivos protegidos.
2. NVIDIA DRS com GPUs/drivers distintos; perda de dispositivo, modo de vídeo, DNS estático/DHCP e reinicialização de NIC.
3. Hibernação, pagefile manual, serviços com dependências, refresh de acessibilidade/tema e dual-CCD X3D real.
4. Pacotes Appx em builds/edições distintas, desinstaladores que delegam a outro processo e ferramentas Windows com saída localizada.
5. Captura PresentMon real e cancelamento no jogo, múltiplas swapchains, anticheat e ETW sem permissão.
6. Instalação/desinstalação do próprio VL em VM limpa; atualização sobre versão antiga continua recusada pelo instalador.
7. DPI real 100/125/150/200%, troca de monitor, leitor de tela e navegação completa somente por teclado.
8. Sessões longas, consumo e responsividade sob carga. Não há benchmark comparativo contra GDZ nem medição de ganho de FPS.

A revisão 0.2.0 tinha ensaios temporais de animação próprios; foram preservados em `docs/historico/0.2.0` e não são reapresentados como novas medições da versão 0.4.0. Movimento, pausa e retomada existentes foram mantidos. Três dimensões de render não comprovam toda a matriz de acessibilidade/DPI.

## Reproduzir

```powershell
powershell -NoProfile -File .\scripts\build.ps1
& .\.tools\dotnet\dotnet.exe .\src\VLOptimizer.App\bin\Release\net10.0-windows\VLOptimizer.dll --smoke-test --screenshot-dir .\artifacts\private\ui-smoke
powershell -NoProfile -File .\scripts\publish.ps1
powershell -NoProfile -File .\scripts\package-source.ps1
```

Não use o smoke de UI como autorização de executar operações destrutivas na máquina. A revisão de cada recurso precisa ocorrer em ambiente apropriado com estado recuperável.
