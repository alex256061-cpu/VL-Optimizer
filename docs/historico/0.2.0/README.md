# VL Optimizer

Aplicativo desktop nativo para entender o PC, revisar ajustes e manter um histórico verificável. Interface em português com o monograma VL fornecido no briefing.

**Versão 0.2.0 — candidato de validação, sem assinatura de código.** O diagnóstico é real e inicia automaticamente ao abrir. A interface usa vermelho e preto, órbitas em movimento e pontos luminosos nos circuitos. Não aplica ajustes ao abrir, não exige conta e não envia telemetria. O alvo de alterações é Windows 11 x64; Windows 10 e Windows Server ficam em diagnóstico somente leitura.

## Abrir

- `release/VL-Optimizer-0.2.0.exe`: versão portátil autocontida; não precisa instalar .NET.
- `release/Instalar-VL-Optimizer-0.2.0.exe`: instalação apenas para o usuário atual, com atalho no menu Iniciar e desinstalação em Aplicativos do Windows.
- `release/VL-Optimizer-0.2.0-portatil.zip`: portátil com instruções.
- `release/SHA256SUMS.txt`: integridade dos arquivos. Hash não autentica o publicador.

A instalação não abre o aplicativo nem aplica otimizações. Esta primeira versão recusa sobrepor instalação anterior: desinstale antes de reinstalar. O histórico é preservado. Desinstalar não desfaz ajustes; use a restauração no histórico antes, se desejar.

## O que funciona

1. **Analisar:** Windows/build/permissão, CPU e GPU identificadas, RAM instalada/utilizável/disponível/comprometida, discos, fontes de inicialização, alimentação, planos de energia, processos e sinais conhecidos de reinicialização. A CPU é observada por cerca de cinco segundos, com duração real registrada.
2. **Explicar:** recomendações contextuais, origem técnica, limites e categorias distintas. Sem nota fictícia de desempenho, promessa de FPS ou diagnóstico de gargalo baseado só no hardware.
3. **Revisar e aplicar:** remover entradas HKCU Run individualmente selecionadas e ativar planos de energia existentes. A prévia exige aprovação antes de escrever e expira; sua autorização é de uso único. Entradas sensíveis conhecidas são protegidas; a classificação não substitui sua revisão de programas desconhecidos.
4. **Verificar e desfazer:** estado anterior persistido antes da escrita, leitura posterior para confirmar, conflitos preservados, journal atômico e recuperação de operações interrompidas. Reverter uma configuração não é um backup do computador.
5. **Comparar:** duas análises na mesma sessão, com horário/duração e aviso de que cargas diferentes não comprovam ganho. Histórico mantém antes/depois das configurações. Exportação JSON inclui informações do PC e deve ser revisada antes de compartilhar.
6. **Armazenamento e processos:** informações reais, acesso guiado às configurações oficiais e Gerenciador de Tarefas. O aplicativo não apaga arquivos nem encerra processos.

F5 inicia a análise; Esc a cancela entre etapas seguras. Navegação por teclado, foco visível, modo econômico e movimento reduzido são oferecidos. A janela usa rolagem e reorganização em áreas menores; mudanças reais de DPI entre monitores ainda exigem validação.

## Compilar e verificar

Requisitos: Windows x64, acesso à internet no primeiro preparo e PowerShell. O script obtém o SDK **10.0.403** da Microsoft e confere SHA512 oficial, sem instalar ferramentas globalmente.

```powershell
powershell -NoProfile -File .\scripts\bootstrap-sdk.ps1
powershell -NoProfile -File .\scripts\build.ps1
powershell -NoProfile -File .\scripts\publish.ps1
```

`build.ps1` compila em Release e executa testes de cenários com uma fronteira de sistema falsa, sem alterar startup ou energia da máquina. `publish.ps1` gera o portátil autocontido e o instalador IExpress do Windows. Nenhuma biblioteca de terceiros é necessária.

Para a revisão visual de leitura, após compilar:

```powershell
& .\.tools\dotnet\dotnet.exe .\src\VLOptimizer.App\bin\Release\net10.0-windows\VLOptimizer.dll --smoke-test --screenshot-dir .\artifacts\private\ui-smoke
```

Esse modo faz uma análise real, navega pelas telas, registra capturas e amostras de consumo, e encerra. Apply/Undo e abertura de recursos externos estão bloqueados nele. Os arquivos privados contêm informações da máquina e ficam excluídos do Git.

Para verificar movimento real e pausas, execute em uma sessão Windows com animações habilitadas:

```powershell
& .\.tools\dotnet\dotnet.exe .\src\VLOptimizer.App\bin\Release\net10.0-windows\VLOptimizer.dll --motion-smoke --screenshot-dir .\artifacts\private\motion-smoke
python scripts/verify_motion_frames.py --directory artifacts/private/motion-smoke --mp4
```

O primeiro comando verifica pixels das órbitas e circuitos em 30 capturas e confirma pausas, retomada e dimensões do menu. O segundo exige Python, NumPy e Pillow; `--mp4` também requer imageio-ffmpeg. A prévia é reproduzida a 10 quadros por segundo e não mede o FPS do aplicativo. Ambos usam os visuais reais; o smoke impede aplicar/desfazer ajustes e não salva preferências.

## Organização

- `src/VLOptimizer.App`: apresentação WPF, marca, navegação e revisão de operações.
- `src/VLOptimizer.Core/Contracts.cs`: contratos entre interface e motor.
- `src/VLOptimizer.Core/SystemAccess.cs`: fronteira Win32/Registro e coleta de métricas.
- `src/VLOptimizer.Core/WindowsOptimizerBackend.cs`: catálogo, prévias, execução, persistência e restauração.
- `tests/VLOptimizer.Tests`: testes executáveis sem frameworks externos.
- `installer` e `scripts`: instalação, remoção, compilação e empacotamento reproduzíveis.

Nome e textos da marca ficam centralizados em `Brand.cs`; metadados do executável, instalador e caminhos persistentes também precisam ser versionados numa mudança comercial de identidade.

Histórico: `%LOCALAPPDATA%\VLOptimizer\history.json`. Há uma cópia anterior e um arquivo de bloqueio para coordenação entre instâncias. Não edite nem apague backups enquanto houver ações que deseje restaurar.

## Limites desta entrega

Ainda não inclui limpeza própria, temperaturas, utilização de GPU/disco, inventário de todas as tarefas agendadas/apps empacotados, driver, broker elevado, benchmark de jogos ou autoatualizador. Não existe certificado de assinatura configurado. O instalador foi empacotado e inspecionado; a instalação/desinstalação em VM ainda precisa ser executada.

Os testes de mutação usam fakes; não equivalem a testes de integração em Windows 11. A execução real realizada nesta máquina Windows 10 é de diagnóstico somente leitura. Resoluções por render não comprovam todas as escalas reais de DPI, acessibilidade por leitor de tela nem 60 FPS. Consulte o relatório para evidências e pendências.

## Documentação

- [Arquitetura e decisões](docs/ARQUITETURA.md)
- [Catálogo, matriz de riscos e fontes técnicas](docs/CATALOGO-E-EVIDENCIAS.md)
- [Critérios de validação](docs/VALIDACAO.md)
- [Distribuição, assinatura e atualizações](docs/DISTRIBUICAO.md)
- [Resultados da entrega](docs/RESULTADOS.md)
- [Comparação visual e de movimento](docs/AUDITORIA-VISUAL.md)

A pesquisa avaliou a documentação Microsoft, WinUtil e Winhance. O código foi desenvolvido neste projeto; não foram incorporados tweaks ou código desses produtos. O repositório Reverse-Engineering-ChatGPT trata de prompts, não de inspeção de executáveis. Em atendimento ao novo pedido, ele foi clonado em `.tools/Reverse-Engineering-ChatGPT`; não contém aplicação para instalar. A instalação local do GDZ Optimizer 1.1.27 foi inspecionada estaticamente, com ferramentas próprias e evidências privadas: [relatório de engenharia reversa](docs/GDZ-ENGENHARIA-REVERSA.md) e [mapa aprofundado de cada função](docs/GDZ-MAPA-COMPLETO.md). O código extraído não foi incorporado ao VL Optimizer.

O aprofundamento posterior está na [especificação para implementação independente](docs/GDZ-ESPECIFICACAO-PARA-IMPLEMENTACAO.md): operações/valores completos, algoritmos e contratos, C#/XAML da janela WPF e desmontagem de exports e shaders. Esta etapa produziu análise e ferramentas estáticas; preservou a interface e o código do aplicativo.

A direção visual foi revista a partir do vídeo original após a correção do usuário. Consulte [DESIGN.md](docs/DESIGN.md) para a composição, proporções e fonte Inter, cuja licença acompanha a entrega.

