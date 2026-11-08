# VL Optimizer 0.4.0

[**Baixar versão de teste para Windows**](https://github.com/alex256061-cpu/VL-Optimizer/releases/tag/v0.4.0) · [Guia para testar](TESTAR.md) · [Relatar problema](https://github.com/alex256061-cpu/VL-Optimizer/issues/new/choose)

Para testar sem compilar: baixe **VL-Optimizer-0.4.0-portatil.zip** no link acima, extraia e abra **VLOptimizer.exe**. O diagnóstico inicial não altera configurações. Os executáveis ficam nos Releases; o botão **Code → Download ZIP** baixa somente o código-fonte.

Aplicativo desktop C# / .NET 10 / WPF para diagnóstico, configurações, jogos e manutenção local. Esta evolução integra os comportamentos recuperados na análise do GDZ à interface vermelha/preta existente, com implementação própria e sem incluir código, bibliotecas gráficas ou shaders do produto analisado.

**Candidato de validação, sem assinatura de código.** Abertura e diagnóstico são somente leitura. Nenhum ajuste, agendamento de RAM, sessão de jogo ou instalador externo começa automaticamente. Plataforma habilitada: Windows 10/11 cliente x64, build 19041 ou superior, com bloqueios por recurso/build, hardware e permissão. Isso não significa homologação em todas essas versões.

## Abrir

- `release/VL-Optimizer-0.4.0.exe`: portátil autocontido, dispensa instalar .NET.
- `release/VL-Optimizer-0.4.0-portatil.zip`: portátil com instruções.
- `release/Instalar-VL-Optimizer-0.4.0.exe`: instalação por usuário, com atalho e desinstalação.
- `release/VL-Optimizer-0.4.0-codigo-fonte.zip`: código, testes, relatórios e scripts.
- `SHA256SUMS-0.4.0.txt` no Release: hashes dos arquivos distribuídos.

O instalador não abre o aplicativo nem aplica ajustes. Ele recusa sobrepor uma instalação anterior: desinstale a versão instalada antes de reinstalar. O histórico permanece em `%LOCALAPPDATA%\VLOptimizer`. Desinstalar não desfaz configurações do Windows; revise as restaurações antes. O ZIP 0.2.0 fornecido como referência foi preservado.

## Menu solicitado

Painel; Desempenho; Mouse e teclado; Memória; Limpeza; Programas; Internet e live; Windows; Inicialização; Meus jogos; DLSS / FSR; VL Scaling; Medidor de FPS. Hardware, histórico e configurações ficam nos atalhos inferiores. O menu permanece aberto em janelas largas. [Explicação de cada função e comparação com a referência](docs/FUNCOES-DO-MENU-0.4.0.md).

Novidades: editor do INI OptiScaler com dez opções, backup e restauração; sobreposição externa de FPS nos quatro cantos da tela principal; recuperação explícita Defender/Proteção do Sistema; serviços Windows Update com snapshot. O overlay usa a captura PresentMon escolhida e exige janela/sem bordas; o motor gráfico próprio de scaling continua ausente.

## Funcionalidades

| Área | Implementação |
| --- | --- |
| Visão geral | Diagnóstico real, origem dos dados, recomendações contextuais, busca global e comparação entre análises. |
| Catálogo | 57 configurações: os 33 tweaks identificados e 24 operações adicionais; prévia, revalidação, snapshot tipado, verificação e restauração. Compatibilidade e dependências são conferidas antes de escrever. |
| Sistema e entrada | Modo Jogo, DVR/Game Bar, políticas de segundo plano/atualizações/privacidade, serviços, mouse/teclado, aparência e opções por versão do Windows. |
| Energia e rede | Planos existentes, hibernação, X3D com dois CCDs, propriedades anunciadas pelo driver da NIC, DNS IPv4 por interface física, DHCP e restauração do estado original. |
| GPU e vídeo | HAGS, MPO, MSI/IRQ onde compatível, DLSS por NVAPI/DRS oficial, preferência GPU por executável preservando tokens alheios e modo 1280×720 testado pelo driver. |
| Inicialização | Seleção individual de entradas HKCU Run, com snapshot e proteção contra mudanças posteriores. Outras origens permanecem inventário. |
| Limpeza | 16 categorias de arquivos, manifesto exportável, filtros por arquivo, preservação de links/cloud/arquivos alterados ou em uso. Lixeira separada com confirmação do Windows. Exclusão é permanente. |
| Jogos | Descoberta Steam/Epic/FiveM, perfil básico de configurações globais, sessões explícitas com prioridades/plano originais recuperáveis e presets XML FiveM com backup por caminho. |
| FPS | Importar CSV PresentMon, média, 1%/0,1% low, frametime e comparação A/B. Captura opcional por PID com executável escolhido, progresso real do próprio CSV e sessão ETW exclusiva. PresentMon não acompanha o pacote. |
| Diagnóstico | CPU total/núcleo, GPU 3D e RAM com amostragem e fontes indisponíveis identificadas. Inventário detalhado de sete grupos WMI e estimativa de eventos do mouse na janela. |
| Memória e disco | RAM/commit/standby reais, purga explícita, agendamento temporário opt-in e scanner de diretórios/maiores arquivos somente leitura. |
| Programas | Inventário Win32, prévia de desinstalação EXE/MSI, verificação posterior e revisão de sobras específicas após conclusão comprovada. Encerramento de processo exige PID, início e caminho conferidos. Apps Store possuem fluxo separado. |
| Manutenção | Oito ações: cache DNS, reset de rede, DISM/SFC, componentes Windows, ponto de restauração, reinício do Explorer, solicitação Defender e Proteção do Sistema. Resultados por etapa; não há desfazer universal dessas ferramentas. |
| Scaling | Armazenar, validar, editar e exportar perfis com 40 propriedades editáveis. Lançamento manual de programa externo selecionado. Instalação local de OptiScaler e editor de dez opções existentes do INI, com prévia, backup e restauração. |
| Histórico | Journal antes das configurações, estados de falha parcial/interrupção, conflitos preservados e restauração em ordem. Backups de XML, GPU por jogo e bibliotecas possuem revisão própria em Jogos/Scaling. |

As quantidades não devem ser somadas como recursos independentes: ações compostas, aliases, abas, campos e métodos internos se sobrepõem. A matriz atual está em [funções do menu](docs/FUNCOES-DO-MENU-0.4.0.md); a integração anterior está em [evolução 0.3.0](docs/EVOLUCAO-0.3.0.md).

## Uso e dependências

F5 analisa; Esc pede cancelamento entre etapas seguras. Operações que alteram o Windows passam por revisão. Ações HKLM, serviços e parte da manutenção exigem elevação; o botão para reabrir elevado só age ao ser escolhido. As opções de movimento reduzido e economia continuam disponíveis.

PresentMon e OptiScaler são dependências locais opcionais selecionadas pelo usuário. Não são baixados, incluídos ou executados na abertura. A instalação OptiScaler recusa anticheat identificado, exige jogo fechado e pacote PE x64 e não executa scripts do pacote. Drivers NVIDIA compatíveis precisam expor os ajustes DRS usados.

O editor VL Scaling **não implementa o motor de geração de quadros/upscaling do GDZ**. Não contém LosslessScaling.dll/Lossless.dll, injeção ou hotkey global que prometa ativar seus perfis. O programa externo é aberto manualmente e precisa receber suas configurações pela própria interface. Exportar um perfil VL não prova compatibilidade com o formato de outro produto.

Não há autenticação, assinatura, servidor de licenças, telemetria, download de pacotes remotos ou autoatualizador. Sensores de temperatura, estado comprovado XMP/EXPO/Resizable BAR, captura de FPS sem PresentMon permanecem fora da implementação. Dados ausentes nunca viram zero ou promessa de ganho.

## Compilar e verificar

Requer Windows x64 e PowerShell. O preparo obtém o SDK 10.0.403 da Microsoft e confere o SHA512 oficial, sem instalação global.

```powershell
powershell -NoProfile -File .\scripts\bootstrap-sdk.ps1
powershell -NoProfile -File .\scripts\build.ps1
powershell -NoProfile -File .\scripts\publish.ps1
powershell -NoProfile -File .\scripts\package-source.ps1
```

O build executa testes com fronteiras falsas e arquivos de fixture. Nenhum teste de mutação aplica configurações, limpa caches reais ou desinstala programas da máquina de desenvolvimento.

```powershell
& .\.tools\dotnet\dotnet.exe .\src\VLOptimizer.App\bin\Release\net10.0-windows\VLOptimizer.dll --smoke-test --screenshot-dir .\artifacts\private\ui-smoke
```

Esse modo analisa e renderiza os 16 destinos, quatro páginas auxiliares, 22 subabas das áreas, sete de medições e três de jogos, busca e comparação. Mutação e abertura de recursos externos são bloqueadas. As capturas completas contêm dados da máquina e ficam em `artifacts/private`.

## Estrutura e evidências

- `src/VLOptimizer.App`: UI WPF, navegação, estados e diálogos de revisão.
- `src/VLOptimizer.Core`: contratos, adaptadores Windows, catálogo, serviços e persistência.
- `tests/VLOptimizer.Tests`: cenários executáveis sem framework externo.
- `scripts` e `installer`: build, publicação, empacotamento e instalação por usuário.
- [Arquitetura](docs/ARQUITETURA.md), [validação](docs/VALIDACAO.md), [resultados](docs/RESULTADOS.md) e [distribuição](docs/DISTRIBUICAO.md).
- [Comparação GDZ → VL e limites atuais](docs/FUNCOES-DO-MENU-0.4.0.md), [integração 0.3.0](docs/EVOLUCAO-0.3.0.md), [serviços detalhados](docs/VL-TOOLKIT-IMPLEMENTADO.md).
- [Especificação recuperada do GDZ](docs/GDZ-ESPECIFICACAO-PARA-IMPLEMENTACAO.md); relatórios descrevem a amostra analisada, não certificam a execução do VL.
- `docs/historico/0.2.0`: relatórios da versão anterior, preservados como histórico.

A comparação com GDZ é funcional/estrutural. Não foi realizado benchmark de velocidade, consumo ou ganho de FPS entre os dois produtos. Os testes de mutação e o smoke de leitura não substituem uma matriz de testes em VMs Windows 10/11, drivers e hardware distintos.
