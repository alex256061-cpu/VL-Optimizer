# GDZ Optimizer — auxiliares nativos e gerenciados

**Registro da etapa inicial.** Depois desta inspeção foram instaladas ferramentas estáticas isoladas em `.tools`, recuperados C#/XAML e desmontados exports/shaders. Consulte [o contrato WPF](GDZ-SCALING-CONTRATO-WPF.md) e [o motor aprofundado](GDZ-MOTOR-NATIVO-APROFUNDADO.md). As declarações de ferramenta indisponível e fluxo nativo desconhecido abaixo são históricas, não o estado final da análise.

Análise estática local realizada em 08/11/2026. Origem examinada: `%LOCALAPPDATA%\Programs\GDZ Optimizer\resources\app.asar.unpacked\bin`.

O resultado principal é que **`GDZOptimizer.exe` e `GDZOptimizer_original.exe` são iniciadores nativos para `LosslessScaling.dll`**, e não evidência de dois motores independentes de otimização do Windows. As seções de código e dados examinadas desses executáveis são idênticas às de `LosslessScaling.exe`. O conjunto inclui uma interface WPF gerenciada, um motor gráfico nativo e o capturador de métricas PresentMon.

## Escopo e método

- Leitura dos cabeçalhos PE/COFF, diretórios de importação e exportação, importações atrasadas, recursos de versão, assinaturas e strings ASCII/UTF-16LE.
- Leitura dos metadados e instruções IL de `LosslessScaling.dll` com `System.Reflection.Metadata` e `PEReader`. A DLL alvo **não foi carregada** com `Assembly.Load`, nem executada. O programa de inspeção é código novo, separado dos alvos.
- Comparação SHA-256 de arquivos e de cada seção PE.
- Não foram executados os executáveis/DLLs do produto, nem modificadas instalação ou configurações; nenhum diretório Roaming foi lido. A análise não avaliou autenticação ou licença.
- Não havia `ilspycmd`, `ildasm`, `dumpbin` ou `strings` disponíveis no `PATH`; também não foram localizados esses nomes no SDK e em `.tools` pela busca de arquivos. Nenhuma nova ferramenta de decompilação foi instalada.

“Confirmado” abaixo significa observável nos bytes ou metadados examinados. “Indício” significa hipótese consistente com esses bytes, sem validação dinâmica ou reconstrução completa das rotinas nativas. Ausência de strings não prova ausência de comportamento.

## Inventário

Todos os seis arquivos são PE32+ x64, `Machine = 0x8664`.

| Arquivo | Bytes | CLR | Versão/produto nos recursos | Authenticode local |
|---|---:|---|---|---|
| `GDZOptimizer.exe` | 268.288 | Ausente | GDZ Scaling 3.2.2; `OriginalFilename = GDZScaling.exe` | `NotSigned` |
| `GDZOptimizer_original.exe` | 157.696 | Ausente | GDZ Scaling 3.2.2; `OriginalFilename = LosslessScaling.dll` | `NotSigned` |
| `LosslessScaling.exe` | 266.240 | Ausente | Lossless Scaling 3.2.2; `OriginalFilename = LosslessScaling.dll` | `NotSigned` |
| `LosslessScaling.dll` | 275.456 | Presente, IL-only | GDZ Scaling 3.2.2 | `NotSigned` |
| `Lossless.dll` | 7.521.280 | Ausente | Lossless Scaling 3.2.2.0 | `NotSigned` |
| `PresentMon\PresentMon.exe` | 956.768 | Ausente | Sem campos usuais de versão consultáveis; strings identificam 2.5.1 | `Valid`, Intel Corporation |

Os nomes e recursos são declarações presentes no arquivo; não determinam autoria, procedência ou legitimidade. `NotSigned` por si só não demonstra conteúdo malicioso. O status `Valid` do PresentMon foi devolvido por `Get-AuthenticodeSignature` no ambiente local; não constitui auditoria independente da cadeia de certificados.

| Arquivo | SHA-256 |
|---|---|
| `GDZOptimizer.exe` | `0ead1d13e847c98168b5650fe3c4e623217b5fee02ca4336dfd0b413ae8d04e3` |
| `GDZOptimizer_original.exe` | `76f05eb76b3c9d6dff9f838615612416c9561d40bb42563c6e4a8144bae702ca` |
| `LosslessScaling.exe` | `c5bd72484efb3f07e0d3983d2783edb1affd2128b1d49e57210de61f22ae729d` |
| `LosslessScaling.dll` | `3dc441fda2f444e7ac4f33927ac0902c7801cdbe6237d95519972437a1af2337` |
| `Lossless.dll` | `626b196d799606cd4250b7b29e04228692ab70cf56a5d1bbb56d748c8219f0eb` |
| `PresentMon\PresentMon.exe` | `9bec3083069f58f911e6a512f4806db51a27bd096103087bc1d05ef54c80a191` |

## Iniciadores `GDZOptimizer*.exe`

**Confirmado:** os três iniciadores `GDZOptimizer.exe`, `GDZOptimizer_original.exe` e `LosslessScaling.exe` têm conteúdo idêntico nas cinco seções `.text`, `.rdata`, `.data`, `.pdata` e `.reloc`. A seção `.text` tem 94.208 bytes e SHA-256 `ee203e44efe919ba91a6d6e3543d5ab7fd02a28988ddc277957f1fbfec09fa08`. A seção `.rsrc` varia entre eles: 122.368, 11.776 e 120.320 bytes, respectivamente. Cabeçalhos relacionados aos recursos também podem variar; a comparação não trata os arquivos inteiros como iguais.

Os três contêm `LosslessScaling.dll` no offset de arquivo `0x21740`, e `hostfxr.dll` na área de strings. Importam `ShellExecuteW`, carregamento dinâmico (`LoadLibraryExW`, `GetProcAddress`) e leitura de Registro (`RegOpenKeyExW`, `RegGetValueW`). Há strings de descoberta de runtime como `SOFTWARE\dotnet`.

**Conclusão sustentada:** a estrutura corresponde a iniciadores/apphosts .NET apontando para o mesmo assembly. Os imports de Registro desses iniciadores, isoladamente, não demonstram ajustes de desempenho: também fazem parte da localização/inicialização de runtime. O sufixo `_original` não prova que essa cópia seja um original de fornecedor ou uma versão anterior confiável.

Nenhum dos iniciadores exporta funções PE. Não foram encontradas strings ASCII/UTF-16LE `powershell` ou `powercfg` nesse conjunto de seis arquivos; isso não cobre o JavaScript do aplicativo Electron, recursos externos ou comandos montados dinamicamente.

## `LosslessScaling.dll`: interface e ligação ao motor gráfico

**Confirmado nos metadados:** assembly `LosslessScaling`, versão `3.2.2.0`, metadados `v4.0.30319`, cabeçalho CLR `ILOnly = 1`, ponto de entrada `0x06000003`. A versão de metadados não significa .NET Framework 4: `LosslessScaling.runtimeconfig.json` declara `net9.0`, com runtimes incluídos `Microsoft.NETCore.App` e `Microsoft.WindowsDesktop.App` 9.0.5. O arquivo `.deps.json` indica alvo `win-x64`, WPF-UI 4.0.1 e componentes Windows SDK.

O assembly contém 50 tipos, incluindo `UI.App`, `UI.MainWindow`, `UI.Profile`, `UI.Settings`, `UI.Core`, páginas WPF e observação de janela em primeiro plano. As referências incluem WPF, `Wpf.Ui`, `Wpf.Ui.Tray`, `Microsoft.Win32.Registry`, processos e serialização XML.

### Argumento de inicialização confirmado

Em `UI.App.App_Startup` (token `0x06000001`), o IL percorre os argumentos e compara cada um com o literal **`-StartMinimized`** usando `String.op_Equality`. O resultado é passado ao construtor da janela. Também cria mutex chamado `LosslessScaling`; em outra instância, envia a mensagem registrada de exibição e encerra a instância recém-iniciada.

Esse é o único argumento de aplicação identificado nesse parser de entrada. Não foi encontrado um CLI gerenciado para comandos de energia, limpeza, rede ou reparos do Windows. Isso não exclui funções existentes em outros componentes.

### Registro, privilégios e persistência

**Confirmado no IL de `UI.Pages.SettingsPage.StartAtWindowsStartup_Toggled`**, token `0x060001e0`:

- Abre para escrita `Registry.CurrentUser\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`.
- Usa como nome do valor `Assembly.GetName().Name`, que neste assembly é `LosslessScaling`.
- Ao ativar, grava o caminho do assembly com extensão trocada para `.exe`, entre aspas, seguido de `-StartMinimized`; ao desativar, chama `DeleteValue` para esse nome.

O fluxo portanto constrói o caminho de `LosslessScaling.exe`, ainda que a entrada inicial tenha usado outro nome de apphost. Não foi consultado o valor real do Registro do usuário.

`UI.MainWindow.StartAsAdministrator`, token `0x06000049`, constrói `ProcessStartInfo` para o próprio executável, `UseShellExecute = true`, verbo `runas`, com `-StartMinimized` quando aplicável. É um caminho de solicitação de elevação, não prova de que a sessão atual esteja elevada.

`UI.Settings.Save/Load` usa `XmlSerializer`. A janela contém os nomes `Settings.xml` e `CorruptedSettingsBackup.xml`; o logger referencia `log.txt` junto ao assembly. Nenhum desses arquivos de usuário foi aberto nesta análise.

### Chamadas ao motor

`UI.Core` declara P/Invoke Cdecl para `Lossless.dll`: `Init`, `UnInit`, `Activate`, `ApplySettings`, `GetForegroundWindowEx`, `GetAdapterNames`, `GetDisplayNames`, `GetDwmRefreshRate`, `IsWindowsBuildAtLeast`, `SetDriverSettings` e `SetWindowsSettings`.

Em `UI.MainWindow.ApplyProfileToCore`, token `0x0600004d`, os getters lidos antes de `UI.Core.ApplySettings` incluem tipo/fator de escala, FSR/LS1/Anime4K, geração de quadros LSFG, sincronização, captura, latência, HDR, cursor, GPU, monitor e recorte. Isso confirma a ligação estrutural entre interface, perfil e motor; não valida eficácia, qualidade de imagem ou ganho de FPS.

Há P/Invoke para hooks de teclado e eventos de janela (`SetWindowsHookEx`, `RegisterHotKey`, `SetWinEventHook`). O contexto gerenciado contém configuração de tecla de atalho e observação da janela ativa. A existência de hooks, isoladamente, não estabelece coleta persistente de digitação.

## `Lossless.dll`: engine nativo

**Confirmado:** 11 exports, exatamente os mesmos nomes declarados em `UI.Core`. Imports gráficos incluem `dxgi.dll`, `dwmapi.dll`, `d2d1.dll`, `DWrite.dll`, `GDI32.dll`, e importação atrasada de `d3d11.dll` (`D3D11CreateDevice`, `CreateDirect3D11DeviceFromDXGIDevice`). `CoreMessaging.dll` é carregado por importação atrasada para `CreateDispatcherQueueController`.

Também importa `RegSetValueExW` e contém strings como:

- `Software\Microsoft\DirectX\UserGpuPreferences`, offset `0x410a0`.
- `SYSTEM\CurrentControlSet\Control\Class\`, offset `0x37bd0`.
- Chaves Khronos/OpenCL/Vulkan e nomes `nvapi64.dll`, `nvpowerapi.dll` e componentes NVIDIA.

**Indício:** esses elementos são compatíveis com seleção de GPU, enumeração de drivers e preferências gráficas. O conjunto `SetWindowsSettings` + string `UserGpuPreferences` + import de escrita no Registro sugere ajuste de preferência gráfica. Sem reconstruir o fluxo nativo dessas funções, não foi confirmado qual valor/tipo é escrito, em quais condições ou se há reversão. Não se deve executar `SetDriverSettings` ou `SetWindowsSettings` com uma assinatura ou parâmetros presumidos.

## PresentMon

**Confirmado:** PE x64 nativo, assinatura Intel localmente válida. A string de nome original `PresentMon-2.5.1-x64.exe` e o caminho de PDB contendo `PresentMon-2.5.1-x64.pdb` identificam a versão anunciada como 2.5.1; a consulta de recursos não retornou `FileVersion`.

Os imports normais incluem `KERNEL32.dll`; os imports **atrasados** esclarecem a captura ETW: `ADVAPI32.dll` fornece `StartTraceW`, `EnableTraceEx2`, `ControlTraceW`, `OpenTraceW`, `ProcessTrace` e `CloseTrace`; `tdh.dll` fornece APIs de metadados de eventos. `SHELL32.dll` contém `ShellExecuteExW`; `USER32.dll` contém hotkeys e processamento de mensagens. Há sete exports decorados C++ de suporte interno de serialização, sem API simples de captura identificada nesses exports.

A ajuda embutida anuncia as opções abaixo; os nomes foram observados estaticamente, **sem executar `--help` ou testar argumentos**:

| Grupo | Opções anunciadas |
|---|---|
| Seleção de captura | `--process_name`, `--process_id`, `--exclude`, `--etl_file` |
| Saída | `--output_file`, `--output_stdout`, `--multi_csv`, `--no_csv`, `--no_console_stats` |
| Tempo e formato | `--qpc_time`, `--qpc_time_ms`, `--date_time`, `--exclude_dropped`, `--v1_metrics`, `--v2_metrics` |
| Gravação | `--hotkey`, `--delay`, `--timed`, `--scroll_indicator` |
| Rastreamento | `--track_gpu_video`, `--no_track_display`, `--no_track_input`, `--no_track_gpu` |
| Sessão/encerramento | `--session_name`, `--stop_existing_session`, `--terminate_existing_session`, `--restart_as_admin`, `--terminate_on_proc_exit`, `--terminate_after_timed` |
| Opções experimentais | `--track_frame_type`, `--track_hw_measurements`, `--track_app_timing`, `--track_hybrid_present`, `--track_pc_latency`, `--set_circular_buffer_size` |

As strings de aviso tratam de permissões insuficientes, sessão ETW já existente e incompatibilidades entre opções. Isso é consistente com medição de apresentações de quadros e exportação de métricas; não demonstra uma função de otimização. A forma concreta de invocação pelo aplicativo principal deve ser confirmada no código de integração do Electron.

## Artefatos reproduzíveis e limites

Os artefatos locais de inspeção estão em `artifacts/private/gdz-analysis/native/`:

- `inspect_pe.py`: parser próprio de PE e extração de strings; somente leitura dos alvos.
- `pe-inventory.json`: cabeçalhos, seções e hashes, imports normais/atrasados e exports.
- `versions-signatures.json`: recursos de versão, hashes e status de assinatura coletados por PowerShell.
- `MetadataReader/`: leitor genérico de metadados/IL, construído com o SDK já existente em `.tools/dotnet`.
- `LosslessScaling.metadata.json` e arquivos `*.strings.json`: evidências brutas privadas; não devem ser distribuídas como reconstrução do produto.

Esta inspeção não produziu um projeto-fonte equivalente ao motor nativo e não verificou desempenho, correção, compatibilidade de drivers ou efeitos reais no Registro. Não foi reconstruído o código gráfico proprietário; apenas a estrutura, nomes de interfaces, literais necessários e chamadas de alto nível. Os executáveis, recursos de versão e nomes de função não devem ser usados como prova de procedência ou eficácia.
