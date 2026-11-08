# GDZ Scaling — o que cada função faz

**Etapa posterior disponível:** este capítulo preserva o inventário CLR/IL com o marshaling conferido no C# recuperado. O [contrato WPF de C#/XAML](GDZ-SCALING-CONTRATO-WPF.md) acrescenta controles e limites reais. A [desmontagem nativa aprofundada](GDZ-MOTOR-NATIVO-APROFUNDADO.md) esclarece exports antes descritos aqui como desconhecidos, incluindo preferência GPU/driver e repasse de parâmetros. O pipeline gráfico completo continua parcialmente aberto.

Análise estática de 08/11/2026 do auxiliar `LosslessScaling.dll` 3.2.2.0 e da fronteira com `Lossless.dll`, incluídos no GDZ Optimizer instalado. Este catálogo cobre scaling; os demais relatórios cobrem a aplicação Electron e os ajustes de Windows.

**Foram enumerados 50 tipos e 533 métodos.** O manifesto privado `artifacts/private/gdz-analysis/deep/native-functions.json` contém cada token, assinatura, parâmetros, retorno, chamadas, evidências e classificação. Nenhum binário alvo foi carregado nem executado; foi executado apenas o leitor próprio de bytes/metadados.

## Cobertura e limites

Explicado por IL significa fluxo gerenciado observado. Fronteira nativa significa assinatura e chamadores observados; **o corpo nativo não foi reconstruído**. LSFG, FSR, HDR e G-SYNC são nomes de opções do produto, não prova de eficácia. Limites internos, gestão de memória, shaders e ganho de FPS não foram verificados.

| Categoria | Métodos |
|---|---:|
| behavior | 136 |
| xaml-generated | 13 |
| constructor-boilerplate | 13 |
| native-boundary | 26 |
| event-accessor | 6 |
| accessor-boilerplate | 134 |
| compiler-callback | 32 |
| resource-accessor | 158 |
| delegate-runtime | 12 |
| async-generated | 3 |

A cobertura completa é do inventário CLR; não representa decompilação integral de `Lossless.dll`.

## Fluxo confirmado

```text
App.Main → App_Startup → MainWindow
 ├─ Settings.Load/Save (XML)
 ├─ SetWindowsSettings(preferência GPU) → possível relançamento
 ├─ Core.Init(callback de status)
 ├─ Hotkey → ActivateCore
 └─ ForegroundWatcher → perfil wildcard → delay → ActivateCore
ActivateCore → ApplyProfileToCore → ApplySettings(33 parâmetros) → Core.Activate(HWND)
Callback nativo → status/dimensões/erros → UI
Fechamento efetivo → descarta hooks/bandeja → Core.UnInit
```

## Os 11 exports do motor

Todos usam Cdecl. Retornos/parâmetros bool de UI.Core com descritor explícito usam `I1` (blob03, 1 byte); nomes de adaptador/monitor usam `BStr` (blob13), confirmado no C# decompilado e pela chamada nativa SysAllocStringLen. O callback de Init usa `FunctionPtr` (blob26), e resized no Invoke do delegado possui descritor `I1` (blob03). Esta conferência corrige a interpretação textual anterior de LPWStr e de ausência de descritor no callback. Nenhum export foi chamado para esta conferência.

| Token / função | Entradas | Retorno | Comportamento e limite |
|---|---|---|---|
| `0x06000005 GetForegroundWindowEx` | nenhuma | `IntPtr` | Entrega HWND da janela em primeiro plano segundo o motor. Callback de status usa isso para compor título/classe focada. Diferença exata para o GetForegroundWindow do Win32 não reconstruída. |
| `0x06000006 GetAdapterNames` | nenhuma | `String` | Retorna nomes de adaptadores como String BStr (descritor 0x13), separados por ponto e vírgula. A desmontagem posterior confirmou cópia por SysAllocStringLen sobre a lista DXGI em memória; ver capítulo nativo aprofundado. |
| `0x06000008 GetDisplayNames` | nenhuma | `String` | Retorna nomes de monitores como String BStr (descritor 0x13), separados por ponto e vírgula. A desmontagem posterior confirmou cópia por SysAllocStringLen; ver capítulo nativo aprofundado. |
| `0x0600000a SetDriverSettings` | nenhuma | `Boolean` | Export de configuração de driver com retorno Boolean I1. Nenhum chamador encontrado neste assembly. Ações, fabricantes, valores e reversão nativos desconhecidos. |
| `0x0600000b IsWindowsBuildAtLeast` | UInt32 build | `Boolean` | Consulta build mínimo de Windows. No Loaded, UI passa 19041; se false, mostra diálogo incompatível e fecha ao encerrá-lo. Implementação nativa não reconstruída. |
| `0x0600000c GetDwmRefreshRate` | nenhuma | `Single` | Retorna Single usado pela UI como refresh rate do DWM, arredondado para inicializar alvo LSFG3. Método de obtenção e precisão nativos não reconstruídos. |
| `0x0600000d SetWindowsSettings` | Int32 gpuPreference | `Boolean` | Recebe preferência GPU Int32. MainWindow gira valores 0..2 por até três mudanças; quando retorna true salva a escolha, fecha e relança o próprio processo. Escritas exatas e semântica interna nativa de true desconhecidas. |
| `0x0600000e Init` | .StatusListenerDelegate callback | `Boolean` | Inicializa engine com ponteiro StatusListenerDelegate. UI recebe status, HWND, resoluções, resize, fator e erro pelo callback; descarta retorno Boolean. Recursos/threads nativos não reconstruídos. |
| `0x0600000f UnInit` | nenhuma | `Void` | Encerra motor no fechamento efetivo da janela após descarte dos hooks e bandeja. Recursos nativos liberados não enumerados. |
| `0x06000010 Activate` | IntPtr hwnd | `Boolean` | Solicita ativação/alternância do motor para HWND; ActivateCore atualiza perfil/estado primeiro e descarta retorno Boolean. Seleção de janela e alternância interna nativas não reconstruídas. |
| `0x06000011 ApplySettings` | 33 parâmetros, tabela abaixo | `Void` | Recebe 33 argumentos de scaling, geração de quadros, cursor, sync, captura, GPU, display, recorte e setup. Sem retorno; progresso/erros via callback Init. Shaders, interpolação e captura nativos não reconstruídos. |

### Origem dos 33 parâmetros de ApplySettings

`UI.MainWindow.ApplyProfileToCore` é token `0x0600004d`, IL0x0000..0x0121; chama Core em IL_011c. Recorte é enviado na ordem **Left, Top, Right, Bottom**.

| # | Nome / tipo declarado | Valor gerenciado |
|---:|---|---|
| 1 | `scalingMode: Int32` | Profile.ScalingMode |
| 2 | `scalingFitMode: Int32` | Profile.ScalingFitMode |
| 3 | `scalingType: Int32` | Profile.ScalingType |
| 4 | `scalingSubtype: Int32` | LS1 -> LS1Type; FSR -> FSRType; Anime4K -> Anime4kType; demais -> 0 |
| 5 | `scaleFactor: Single` | Profile.ScaleFactor (Double -> Single) |
| 6 | `resizeBeforeScale: Boolean` | Profile.ResizeBeforeScaling |
| 7 | `sharpness: Int32` | LS1 -> LS1Sharpness; demais -> Sharpness |
| 8 | `VRS: Boolean` | Profile.VRS |
| 9 | `frameGenerationType: Int32` | Profile.FrameGeneration |
| 10 | `frameGenerationSize: Int32` | Profile.LSFGSize |
| 11 | `frameGenerationMode: Int32` | LSFG3 -> LSFG3Mode1; demais -> LSFG2Mode |
| 12 | `frameGenerationMultiplier: Single` | Profile.LSFG3Multiplier |
| 13 | `frameGenerationTarget: Single` | Profile.LSFG3Target |
| 14 | `frameGenerationFlowScale: Int32` | Profile.LSFGFlowScale |
| 15 | `clipCursor: Boolean` | Profile.ClipCursor |
| 16 | `cursorSensitivity: Boolean` | Profile.AdjustCursorSpeed |
| 17 | `hideCursor: Boolean` | Profile.HideCursor |
| 18 | `scaleCursor: Boolean` | Profile.ScaleCursor |
| 19 | `syncMode: Int32` | Profile.SyncMode |
| 20 | `maxFrameLatency: Int32` | Profile.MaxFrameLatency |
| 21 | `gsyncSupport: Boolean` | Profile.GsyncSupport |
| 22 | `hdrSupport: Boolean` | Profile.HdrSupport |
| 23 | `captureApi: Int32` | Profile.CaptureApi |
| 24 | `queueTarget: Int32` | Profile.QueueTarget |
| 25 | `drawFps: Boolean` | Profile.DrawFps |
| 26 | `gpuId: Int32` | Profile.PreferredGpuId |
| 27 | `displayId: Int32` | Profile.OutputDisplayId |
| 28 | `captureOffsetLeft: Int32` | Profile.CropInputLeft |
| 29 | `captureOffsetTop: Int32` | Profile.CropInputTop |
| 30 | `captureOffsetRight: Int32` | Profile.CropInputRight |
| 31 | `captureOffsetBottom: Int32` | Profile.CropInputBottom |
| 32 | `multiDisplayMode: Boolean` | Profile.MultiDisplayMode |
| 33 | `setupPhase: Int32` | Settings.SetupPhase |

Callback de Init: `void(int status, IntPtr hwnd, int inputWidth, int inputHeight, int outputWidth, int outputHeight, bool resized, float scaleFactor, int errorCode)`. Consumidor: `UI.MainWindow.<.ctor>b__19_1` (`0x0600005f`). Estados: Disabled=0, TempDisabled=1, Enabled=2, Countdown=3. Erros: NO_CODE=0, RESIZE_FAILED=1, LARGE_OFFSET=2, CAPTURE_FAILED=3; nem todo erro recebe mensagem específica no callback.

## Perfil: cada opção e seus acessores

Os 84 acessores de Profile apenas leem/escrevem campos. Não validam, salvam XML nem aplicam ao motor sozinhos. Os padrões vêm do construtor, não de configurações pessoais.

| Opção / tipo | Padrão | Função | Getter / setter |
|---|---|---|---|
| `Title: String` | `null` | Nome visual do perfil. | `0x0600006b` / `0x0600006c` |
| `Path: String` | `null` | Regras de caminho/executável/título: wildcards e entradas separadas por ;. | `0x0600006d` / `0x0600006e` |
| `AutoScale: Boolean` | `false` | Ativação quando janela correspondente entra em primeiro plano. | `0x0600006f` / `0x06000070` |
| `AutoScaleDelay: Int32` | `0` | Contagem de segundos (Timer=1000ms) antes de ativação automática. | `0x06000071` / `0x06000072` |
| `ScalingMode: .ScalingModeEnum` | `0` | Automático ou fator personalizado. | `0x06000073` / `0x06000074` |
| `ScalingFitMode: .ScalingFitModeEnum` | `0` | Proporção ou preenchimento de tela. | `0x06000075` / `0x06000076` |
| `ScaleFactor: Double` | `1.5` | Fator de escala enviado como Single ao motor. | `0x06000077` / `0x06000078` |
| `ResizeBeforeScaling: Boolean` | `false` | Flag de redimensionamento prévio enviada ao motor. | `0x06000079` / `0x0600007a` |
| `WindowedMode: Boolean` | `false` | Propriedade persistível e copiada; não é enviada por ApplySettings. Efeito gráfico não confirmado. | `0x0600007b` / `0x0600007c` |
| `ScalingType: .ScalingTypeEnum` | `0` | Seleção de algoritmo de upscaling. | `0x0600007d` / `0x0600007e` |
| `FSRType: .FSRTypeEnum` | `0` | Original/Optimized de FSR. | `0x0600007f` / `0x06000080` |
| `LS1Type: .LS1TypeEnum` | `0` | Balanced/Performance de LS1. | `0x06000081` / `0x06000082` |
| `LSFG2Mode: .LSFG2ModeEnum` | `0` | X2/X3/X4; usado no argumento de modo quando não se usa LSFG3. | `0x06000083` / `0x06000084` |
| `LSFG3Mode1: .LSFG3ModeEnum` | `0` | Fixed/Adaptive de LSFG3. | `0x06000085` / `0x06000086` |
| `LSFG3Multiplier: Single` | `2.0` | Multiplicador no modo Fixed. | `0x06000087` / `0x06000088` |
| `LSFG3Target: Single` | `0` | Meta no modo Adaptive; se zero UI inicializa por DWM arredondado. | `0x06000089` / `0x0600008a` |
| `LSFGFlowScale: Int32` | `100` | Parâmetro flow scale; transformação nativa desconhecida. | `0x0600008b` / `0x0600008c` |
| `LSFGSize: .LSFGSizeEnum` | `0` | Balanced/Performance de geração. | `0x0600008d` / `0x0600008e` |
| `Anime4kType: .Anime4kTypeEnum` | `0` | Tamanho S/M/L/VL/UL. | `0x0600008f` / `0x06000090` |
| `Sharpness: Int32` | `5` | Nitidez comum FSR/NIS/SGSR/BicubicCAS. | `0x06000091` / `0x06000092` |
| `LS1Sharpness: Int32` | `1` | Nitidez própria de LS1. | `0x06000093` / `0x06000094` |
| `VRS: Boolean` | `false` | Flag exposta para Anime4K; shader nativo não reconstruído. | `0x06000095` / `0x06000096` |
| `FrameGeneration: .FrameGenerationEnum` | `0` | Off, LSFG3, LSFG2, LSFG1 e valores legados LSFI/LSFI1. | `0x06000097` / `0x06000098` |
| `ClipCursor: Boolean` | `true` | Flag de confinamento do cursor. | `0x06000099` / `0x0600009a` |
| `AdjustCursorSpeed: Boolean` | `false` | Flag enviada como cursorSensitivity. Alteração real não medida. | `0x0600009b` / `0x0600009c` |
| `HideCursor: Boolean` | `false` | Flag de ocultação do cursor. | `0x0600009d` / `0x0600009e` |
| `ScaleCursor: Boolean` | `false` | Flag de escala do cursor. | `0x0600009f` / `0x060000a0` |
| `SyncMode: .SyncModeEnum` | `1` | Modo de sincronização enviado à engine. | `0x060000a1` / `0x060000a2` |
| `MaxFrameLatency: Int32` | `3` | Parâmetro de latência máxima de quadros. | `0x060000a3` / `0x060000a4` |
| `GsyncSupport: Boolean` | `true` | Flag G-SYNC; compatibilidade real não testada. | `0x060000a5` / `0x060000a6` |
| `HdrSupport: Boolean` | `false` | Flag HDR; pipeline de cor nativo não reconstruído. | `0x060000a7` / `0x060000a8` |
| `DrawFps: Boolean` | `true` | Contador visual de FPS. Não equivale à captura PresentMon. | `0x060000a9` / `0x060000aa` |
| `CaptureApi: .CaptureApiEnum` | `0` | DXGI/WGC/GDI por enum. | `0x060000ab` / `0x060000ac` |
| `QueueTarget: Int32` | `1` | Alvo de fila, unidade/limites nativos não reconstruídos. | `0x060000ad` / `0x060000ae` |
| `PreferredGpuId: Int32` | `0` | Índice da lista de GPU; Auto=0. Enviado ao motor sem subtrair1. | `0x060000af` / `0x060000b0` |
| `OutputDisplayId: Int32` | `0` | Índice de monitor; Auto=0. Enviado sem subtrair1. | `0x060000b1` / `0x060000b2` |
| `CropInput: Boolean` | `false` | Flag persistível sem consumidor de comportamento neste assembly; quatro offsets enviados independentemente. | `0x060000b3` / `0x060000b4` |
| `CropInputLeft: Int32` | `0` | Offset esquerdo de captura. | `0x060000b5` / `0x060000b6` |
| `CropInputTop: Int32` | `0` | Offset superior de captura. | `0x060000b7` / `0x060000b8` |
| `CropInputRight: Int32` | `0` | Offset direito de captura. | `0x060000b9` / `0x060000ba` |
| `CropInputBottom: Int32` | `0` | Offset inferior de captura. | `0x060000bb` / `0x060000bc` |
| `MultiDisplayMode: Boolean` | `false` | Flag multi-monitor; implementação nativa desconhecida. | `0x060000bd` / `0x060000be` |

### Valores dos enums

| Enum | Valores declarados |
|---|---|
| `ErrorCode` | NO_CODE=0; RESIZE_FAILED=1; LARGE_OFFSET=2; CAPTURE_FAILED=3 |
| `ScalingStatusEnum` | Disabled=0; TempDisabled=1; Enabled=2; Countdown=3 |
| `ScalingModeEnum` | Auto=0; Custom=1 |
| `ScalingFitModeEnum` | AspectRatio=0; Fullscreen=1 |
| `ScalingTypeEnum` | Off=0; LS1=1; FSR=2; NIS=3; SGSR=4; BicubicCAS=5; Anime4K=6; XBR=7; SharpBilinear=8; Integer=9; Nearest=10 |
| `FSRTypeEnum` | ORIGINAL=0; OPTIMIZED=1 |
| `LS1TypeEnum` | BALANCED=0; PERFORMANCE=1 |
| `Anime4kTypeEnum` | S=0; M=1; L=2; VL=3; UL=4 |
| `FrameGenerationEnum` | Off=0; LSFG3=1; LSFG2=2; LSFG1=3; LSFI=4; LSFI1=5 |
| `LSFG2ModeEnum` | X2=0; X3=1; X4=2 |
| `LSFGSizeEnum` | BALANCED=0; PERFORMANCE=1 |
| `LSFG3ModeEnum` | FIXED=0; ADAPTIVE=1 |
| `SyncModeEnum` | OFF=0; DEFAULT=1; VSYNC1=2; VSYNC2=3; VSYNC3=4; VSYNC4=5 |
| `CaptureApiEnum` | DXGI=0; WGC=1; GDI=2 |
| `ThemeModeEnum` | System=0; Light=1; Dark=2 |

## Métodos de comportamento por classe

Assinaturas indicam entradas e saída; descrições registram efeitos e condições. Tokens localizam o método no JSON de evidências. O corpo de APIs Win32 não está no assembly.

`ProfilePage` normalmente verifica IsLoaded antes de mudar opções. `ProfileChanged` salva XML, mas só reenvia ao motor em TempDisabled e para o mesmo perfil ativo: alterar um controle não significa reaplicar qualquer sessão em execução.

### `UI.App`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x06000001 Void UI.App::App_Startup(Object sender, System.Windows.StartupEventArgs e)` | Percorre Args procurando -StartMinimized; cria mutex LosslessScaling. Se já existe outra instância, envia WM_SHOW_LS em broadcast e solicita Shutdown; constrói MainWindow com opção de minimização. |
| `0x06000003 Void UI.App::Main()` | Ponto de entrada STA: instancia App, inicializa XAML e entra no loop WPF Application.Run. |

### `UI.Core`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x06000007 String[] UI.Core::GetAdapters()` | Separa GetAdapterNames por ponto e vírgula em String[], removendo entradas vazias (opção Split=1). |
| `0x06000009 String[] UI.Core::GetDisplays()` | Separa GetDisplayNames por ponto e vírgula em String[], removendo entradas vazias. |
| `0x06000013 Void UI.Core::.cctor()` | Inicializador estático cria vetor separator contendo somente ponto e vírgula. |

### `UI.ForegroundWatcher`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x06000016 Void UI.ForegroundWatcher::.ctor(System.Action`1<IntPtr> foregroundEvent)` | Assina callback e instala SetWinEventHook para evento 3..3 (foreground), sem filtro PID/thread; conserva delegado e handle. |
| `0x06000017 Void UI.ForegroundWatcher::Finalize()` | Finalizador chama Dispose e finalizador base. |
| `0x06000018 Void UI.ForegroundWatcher::WinEventProc(IntPtr hWinEventHook, UInt32 eventType, IntPtr hwnd, Int32 idObject, Int32 idChild, UInt32 dwEventThread, UInt32 dwmsEventTime)` | Recebe WinEvent e repassa somente HWND aos inscritos; captura e ignora exceções. |
| `0x06000019 Void UI.ForegroundWatcher::Dispose()` | Remove hook de eventos com UnhookWinEvent. |

### `UI.GlobalKeyboardHook`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x0600001a IntPtr UI.GlobalKeyboardHook::SetWindowsHookEx(Int32 idHook, .KeyboardHookProc callback, IntPtr hInstance, UInt32 threadId)` | Declara P/Invoke user32.dll!SetWindowsHookEx. Assinatura preservada abaixo; uso concreto aparece nos chamadores. Corpo da API externa não está neste assembly. |
| `0x0600001b Boolean UI.GlobalKeyboardHook::UnhookWindowsHookEx(IntPtr hInstance)` | Declara P/Invoke user32.dll!UnhookWindowsHookEx. Assinatura preservada abaixo; uso concreto aparece nos chamadores. Corpo da API externa não está neste assembly. |
| `0x0600001c Int32 UI.GlobalKeyboardHook::CallNextHookEx(IntPtr idHook, Int32 nCode, Int32 wParam, .KeyboardHookStruct& lParam)` | Declara P/Invoke user32.dll!CallNextHookEx. Assinatura preservada abaixo; uso concreto aparece nos chamadores. Corpo da API externa não está neste assembly. |
| `0x0600001d IntPtr UI.GlobalKeyboardHook::LoadLibrary(String lpFileName)` | Declara P/Invoke kernel32.dll!LoadLibrary. Assinatura preservada abaixo; uso concreto aparece nos chamadores. Corpo da API externa não está neste assembly. |
| `0x06000024 Void UI.GlobalKeyboardHook::.ctor(System.Windows.Input.ModifierKeys modifierKeys, System.Windows.Input.Key key, System.Action`1<UI.GlobalKeyboardHook> onKeyAction)` | Armazena tecla/modificadores, callback e delegado KeyboardHookProc; instala hook. |
| `0x06000025 Void UI.GlobalKeyboardHook::Finalize()` | Finalizador chama Dispose e finalizador base. |
| `0x06000026 Void UI.GlobalKeyboardHook::Dispose()` | Descarta hook por Unhook. |
| `0x06000027 Void UI.GlobalKeyboardHook::Hook()` | LoadLibrary(User32), SetWindowsHookEx(idHook=13, delegado, módulo, threadId=0); guarda handle do hook de teclado de baixo nível. |
| `0x06000028 Void UI.GlobalKeyboardHook::Unhook()` | Remove handle guardado por UnhookWindowsHookEx. |
| `0x06000029 Int32 UI.GlobalKeyboardHook::HookProc(Int32 code, Int32 wParam, .KeyboardHookStruct& lParam)` | Converte vkCode em Key e filtra tecla configurada. Em keydown/syskeydown evita repetição enquanto mantida; calcula Ctrl/Shift/Alt/Win e exige igualdade exata de modificadores. Se casar invoca HotKeyPressed e retorna 1; keyup libera trava; demais eventos seguem CallNextHookEx. Não há gravação de todas as teclas neste fluxo. |

### `UI.HotKey`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x0600002a Void UI.HotKey::.ctor(System.Windows.Input.ModifierKeys modifierKeys, System.Windows.Input.Key key, System.Windows.Window window)` | Adapta construtor: Window vira WindowInteropHelper; delega com callback nulo. |
| `0x0600002b Void UI.HotKey::.ctor(System.Windows.Input.ModifierKeys modifierKeys, System.Windows.Input.Key key, System.Windows.Interop.WindowInteropHelper window)` | Adapta construtor: extrai handle do helper e delega com callback nulo. |
| `0x0600002c Void UI.HotKey::.ctor(System.Windows.Input.ModifierKeys modifierKeys, System.Windows.Input.Key key, System.Windows.Window window, System.Action`1<UI.HotKey> onKeyAction)` | Adapta construtor: Window vira helper, preservando callback. |
| `0x0600002d Void UI.HotKey::.ctor(System.Windows.Input.ModifierKeys modifierKeys, System.Windows.Input.Key key, System.Windows.Interop.WindowInteropHelper window, System.Action`1<UI.HotKey> onKeyAction)` | Adapta construtor: helper vira handle, preservando callback. |
| `0x0600002e Void UI.HotKey::.ctor(System.Windows.Input.ModifierKeys modifierKeys, System.Windows.Input.Key key, IntPtr windowHandle, System.Action`1<UI.HotKey> onKeyAction)` | Configura Key/KeyModifier, ID por GetHashCode, registra hotkey e assina ThreadPreprocessMessage. Particularidade literal do IL: quando windowHandle é diferente de zero seleciona GetForegroundWindow; no caso contrário usa o argumento zero. |
| `0x0600002f Void UI.HotKey::Finalize()` | Finalizador chama Dispose e finalizador base. |
| `0x06000036 Int32 UI.HotKey::get_InteropKey()` | Converte Key para código virtual Win32 com KeyInterop.VirtualKeyFromKey. |
| `0x06000037 Void UI.HotKey::Dispose()` | Desassina ThreadPreprocessMessage e chama UnregisterHotKey. |
| `0x06000038 Void UI.HotKey::OnHotKeyPressed()` | Despacha callback em Task.Factory.StartNew. |
| `0x06000039 Void UI.HotKey::RegisterHotKey()` | Se há tecla configurada, remove registro anterior e chama WinApi.RegisterHotKey; lança ApplicationException se falhar, indicando possível hotkey em uso. |
| `0x0600003a Void UI.HotKey::ThreadPreprocessMessageMethod(System.Windows.Interop.MSG& msg, Boolean& handled)` | Filtra mensagem de hotkey e ID, chama OnHotKeyPressed e marca mensagem tratada. |
| `0x0600003b Void UI.HotKey::UnregisterHotKey()` | Remove hotkey Win32 pelo handle e ID guardados. |
| `0x0600003c Void UI.HotKey::<OnHotKeyPressed>b__23_0()` | Corpo de callback de OnHotKeyPressed: invoca HotKeyPressed com a própria instância. |

### `UI.LogWriter`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x0600003d Void UI.LogWriter::LogWrite(String logMessage)` | Anexa texto e nova linha em log.txt ao lado do assembly por File.AppendText; descarta writer ao sair. |

### `UI.MainWindow`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x0600003f Void UI.MainWindow::CreateSettings()` | Cria Settings e adiciona perfil padrão localizado, sem caminho e sem autoscale. |
| `0x06000040 Void UI.MainWindow::SaveSettings()` | Combina LocalApplicationData, ProductName e Settings.xml; chama Settings.Save. |
| `0x06000041 Void UI.MainWindow::.ctor(Boolean startMinimized)` | Define pasta corrente e carrega Settings.xml de LocalApplicationData/ProductName. Se ausente cria padrão; se corrompido mostra erro e copia para CorruptedSettingsBackup.xml. Preserva primeiro perfil e ordena demais por título. Verifica admin/StartAsAdmin, pode elevar/reabrir; tenta preferência GPU rotativa por SetWindowsSettings e pode relançar. Aplica idioma/tema/XAML/título, registra callback Core.Init, hotkey, watcher e Loaded; mostra/minimiza/oculta conforme preferências. |
| `0x06000042 Void UI.MainWindow::Dialog_Closed(Wpf.Ui.Controls.ContentDialog sender, Wpf.Ui.Controls.ContentDialogClosedEventArgs args)` | Corpo lança NotImplementedException; rotina sem funcionalidade implementada. |
| `0x06000043 Void UI.MainWindow::ShowProfilePage()` | Garante seleção de perfil, ajusta botões e navega Frame para nova ProfilePage do perfil selecionado. |
| `0x06000044 IntPtr UI.MainWindow::WndProc(IntPtr hwnd, Int32 msg, IntPtr wParam, IntPtr lParam, Boolean& handled)` | Trata WM_SHOW_LS: mostra, normaliza estado e ativa Window. |
| `0x06000045 UI.Profile UI.MainWindow::FindProfileForWindow(IntPtr hwnd)` | HWND -> PID -> processo; tenta caminho do executável e título da janela. Procura perfil por caminho primeiro, depois título. Falhas de processo/permissão podem resultar null. |
| `0x06000046 String UI.MainWindow::WildcardToRegex(String value)` | Transforma wildcard em regex ancorada: Escape, troca \? por ponto e \* por .* e envolve em ^ e $. |
| `0x06000047 UI.Profile UI.MainWindow::FindProfileForExeOrWindowName(String ExeOrProcessName)` | Varre perfis na ordem; Path nulo é ignorado; regras são separadas por ; e Trim. Compara regex wildcard ignorando maiúsculas contra nome completo e Path.GetFileName; primeiro match vence. Retorna null se não achar; erros de regra são ignorados. |
| `0x06000048 Void UI.MainWindow::ActivateCore(IntPtr hwnd)` | Em Disabled/Countdown encontra perfil por HWND/janela ativa ou usa selecionado, envia ao motor, guarda ActivatedProfile e marca Enabled. Em Enabled/TempDisabled limpa perfil e marca Disabled. Atualiza botão via Dispatcher e sempre chama Core.Activate(hwnd) ao final. |
| `0x06000049 Void UI.MainWindow::StartAsAdministrator(Boolean startMinimized)` | Relança próprio exe com UseShellExecute=true, verbo runas, pasta corrente; usa -StartMinimized quando solicitado. |
| `0x0600004a Boolean UI.MainWindow::IsRunningAsAdministrator()` | WindowsIdentity/WindowsPrincipal.IsInRole(Administrator) para token atual; retorna Boolean. |
| `0x0600004b UI.Profile UI.MainWindow::GetSelectedProfile()` | Retorna GameProfiles no índice visual selecionado. |
| `0x0600004c Void UI.MainWindow::ProfileChanged()` | Salva Settings. Somente reenvia perfil ao motor se ScalingStatus=TempDisabled(1) e ActivatedProfile é o mesmo objeto selecionado. Não reaplica incondicionalmente uma sessão Enabled. |
| `0x0600004d Void UI.MainWindow::ApplyProfileToCore(UI.Profile profile)` | Converte Profile nos 33 argumentos ApplySettings. Subtipo/nitidez dependem do algoritmo; frameGenerationMode usa LSFG3Mode1 só para LSFG3, caso contrário LSFG2Mode; ScaleFactor Double vira Single. |
| `0x0600004e Void UI.MainWindow::ApplyProfileToUI(UI.Profile profile)` | Se conteúdo atual é ProfilePage, encaminha Profile para ApplyProfileToUI. |
| `0x0600004f Void UI.MainWindow::SetUILanguage()` | Ajusta CurrentUICulture conforme Language ou cultura corrente no modo System; lista de códigos de idiomas está no IL. |
| `0x06000050 Void UI.MainWindow::SetUITheme()` | Aplica tema do sistema ou enum escolhido via ApplicationThemeManager. |
| `0x06000051 Void UI.MainWindow::MainWindow_StateChanged(Object sender, System.EventArgs e)` | Após carregamento, se minimizada e MinimizeToTray habilitado chama MinimizeToTray. |
| `0x06000052 Void UI.MainWindow::MinimizeToTray()` | Oculta janela e liga clique esquerdo do ícone a mostrar/ativar. |
| `0x06000053 Void UI.MainWindow::MainWindow_SizeChanged(Object sender, System.Windows.SizeChangedEventArgs e)` | Quando carregada e normal, guarda Height/Width e salva Settings. |
| `0x06000054 Void UI.MainWindow::MainWindow_Closing(Object sender, System.ComponentModel.CancelEventArgs e)` | Se CloseToTray e não houve saída explícita da bandeja, oculta e cancela fechamento. Caso contrário salva estado/dimensões/largura da lista; descarta ForegroundWatcher, KeyboardHook e NotifyIcon; chama Core.UnInit. |
| `0x06000055 Void UI.MainWindow::AddButton_Click(Object sender, System.Windows.RoutedEventArgs e)` | Abre ProfileNewPage, limpa seleção/desabilita botões e retorna scroll ao início. |
| `0x06000056 Void UI.MainWindow::EditButton_Click(Object sender, System.Windows.RoutedEventArgs e)` | Abre ProfileEditPage do selecionado e retorna scroll ao início. |
| `0x06000057 Void UI.MainWindow::RemoveButton_Click(Object sender, System.Windows.RoutedEventArgs e)` | Inicia máquina assíncrona de remoção (MoveNext=0x0600020d): confirmação, remoção, ajuste de seleção e refresh visual. Não chama SaveSettings diretamente nesse corpo assíncrono. |
| `0x06000058 Void UI.MainWindow::ProfileList_SelectionChanged(Object sender, System.Windows.Controls.SelectionChangedEventArgs e)` | Após IsLoaded, atualiza ProfilePage existente ou cria página para nova seleção; ajusta botões e scroll. |
| `0x06000059 Void UI.MainWindow::ScaleButton_Click(Object sender, System.Windows.RoutedEventArgs e)` | Enabled/TempDisabled -> ActivateCore para desativar; Disabled -> Timer de 5 ticks/1 segundo para escolher janela; Countdown -> cancela Timer, restaura texto e marca Disabled. |
| `0x0600005a Void UI.MainWindow::BottomMenuList_SelectionChanged(Object sender, System.Windows.Controls.SelectionChangedEventArgs e)` | Navega menu inferior para ManualPage, SettingsPage ou página vazia; ajusta seleção, botões e scroll. |
| `0x0600005b Void UI.MainWindow::LicensesButton_Click(Object sender, System.Windows.RoutedEventArgs e)` | Lê Licenses.txt se existir e mostra ContentDialog; se ausente usa mensagem apropriada. |
| `0x0600005c Void UI.MainWindow::NotifyIcon_ExitItemClicked(Object sender, System.Windows.RoutedEventArgs e)` | Marca NofifyIconExitRequested e fecha Window, permitindo encerramento efetivo no handler Closing. |
| `0x0600005f Void UI.MainWindow::<.ctor>b__19_1(Int32 status, IntPtr hwnd, Int32 inputWidth, Int32 inputHeight, Int32 outputWidth, Int32 outputHeight, Boolean resized, Single scaleFactor, Int32 errorCode)` | Callback de status nativo: errorCode>0 limpa perfil/marca Disabled e exibe erro; status 1/2 compõe janela, resoluções, resize/fator/perfil. Status1 marca TempDisabled e adiciona título/classe da janela focada. Status0 limpa perfil/restaura botão. Atualiza UI por Dispatcher. |
| `0x06000060 Void UI.MainWindow::<.ctor>b__19_8()` | Callback visual mostra contêiner de status e usa RowSpan=1 no conteúdo. |
| `0x06000061 Void UI.MainWindow::<.ctor>b__19_6()` | Callback visual restaura botão Scale, oculta status e usa RowSpan=2. |
| `0x06000062 Void UI.MainWindow::<.ctor>b__19_2(UI.GlobalKeyboardHook <p0>)` | Callback hotkey inicia máquina assíncrona 0x060001f6, que avisa se a própria janela for alvo ou chama ActivateCore para janela externa. |
| `0x06000063 Void UI.MainWindow::<.ctor>b__19_3(IntPtr hwnd)` | Callback foreground só em Disabled: encontra perfil; se AutoScale e Delay=0 ativa já; Delay>0 cria Timer de 1 segundo e marca Countdown. |
| `0x06000064 Void UI.MainWindow::<.ctor>b__19_4(Object <p0>, System.Windows.RoutedEventArgs <p1>)` | Loaded: localiza título padrão, vincula lista, restaura tamanho/estado/largura, instala WndProc e exige build>=19041. Se incompatível mostra diálogo e fecha quando encerrado. |
| `0x06000065 Void UI.MainWindow::<.ctor>b__19_12(Wpf.Ui.Controls.ContentDialog <p0>, Wpf.Ui.Controls.ContentDialogClosedEventArgs <p1>)` | Callback do diálogo de Windows incompatível fecha Window. |
| `0x06000066 Void UI.MainWindow::<MinimizeToTray>b__36_0(Wpf.Ui.Tray.Controls.NotifyIcon <p0>, System.Windows.RoutedEventArgs <p1>)` | Callback do clique na bandeja mostra e ativa Window. |
| `0x06000067 System.Threading.Tasks.Task UI.MainWindow::<ScaleButton_Click>b__43_3()` | Cria máquina Task 0x060001f8 para fim da contagem manual, valida janela e pode abrir aviso/manual. |
| `0x06000068 Void UI.MainWindow::<ScaleButton_Click>b__43_0()` | Callback visual restaura texto Scale. |

### `UI.Profile`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x06000069 Void UI.Profile::.ctor()` | Cria Profile: fator1.5, multiplicador2, flowScale100, nitidez5, nitidezLS1=1, clipCursor=true, sync=DEFAULT, maxLatency3, Gsync=true, DrawFps=true, queueTarget1; demais campos zero/false/null. |
| `0x0600006a Void UI.Profile::.ctor(String title, String exePath, Boolean autoScale, Int32 autoScaleDelay, UI.Profile copy)` | Cria Profile com título/caminho/autoscale/delay; se copy presente copia maioria das opções gráficas. Não copia VRS, DrawFps nem CropInput: estes ficam nos padrões. |

### `UI.Settings`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x060000e7 Void UI.Settings::Save(String filename, UI.Settings settings)` | Abre StreamWriter(filename), serializa Settings com XmlSerializer e descarta writer em finally. |
| `0x060000e8 UI.Settings UI.Settings::Load(String filename)` | Abre StreamReader(filename), desserializa XML como Settings e retorna objeto (ou null se tipo incompatível), descartando reader em finally. Corrupção é tratada pelo chamador. |
| `0x060000e9 Void UI.Settings::.ctor()` | Padrões Settings: Height650, Width1150, ProfileListWidth250, Hotkey enum62, ModifierKeys enum3; GameProfiles vazio; demais preferências zero/false. |

### `UI.WinApi`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x060000ea Boolean UI.WinApi::PostMessage(IntPtr hwnd, Int32 msg, IntPtr wparam, IntPtr lparam)` | Declara P/Invoke user32!PostMessage. Assinatura preservada abaixo; uso concreto aparece nos chamadores. Corpo da API externa não está neste assembly. |
| `0x060000eb Int32 UI.WinApi::RegisterWindowMessage(String message)` | Declara P/Invoke user32!RegisterWindowMessage. Assinatura preservada abaixo; uso concreto aparece nos chamadores. Corpo da API externa não está neste assembly. |
| `0x060000ec Boolean UI.WinApi::RegisterHotKey(IntPtr hWnd, Int32 id, System.Windows.Input.ModifierKeys fsModifiers, Int32 vk)` | Declara P/Invoke user32.dll!RegisterHotKey. Assinatura preservada abaixo; uso concreto aparece nos chamadores. Corpo da API externa não está neste assembly. |
| `0x060000ed Boolean UI.WinApi::UnregisterHotKey(IntPtr hWnd, Int32 id)` | Declara P/Invoke user32.dll!UnregisterHotKey. Assinatura preservada abaixo; uso concreto aparece nos chamadores. Corpo da API externa não está neste assembly. |
| `0x060000ee IntPtr UI.WinApi::GetForegroundWindow()` | Declara P/Invoke user32.dll!GetForegroundWindow. Assinatura preservada abaixo; uso concreto aparece nos chamadores. Corpo da API externa não está neste assembly. |
| `0x060000ef IntPtr UI.WinApi::SetWinEventHook(UInt32 eventMin, UInt32 eventMax, IntPtr hmodWinEventProc, .WinEventDelegate lpfnWinEventProc, UInt32 idProcess, UInt32 idThread, UInt32 dwFlags)` | Declara P/Invoke user32.dll!SetWinEventHook. Assinatura preservada abaixo; uso concreto aparece nos chamadores. Corpo da API externa não está neste assembly. |
| `0x060000f0 Boolean UI.WinApi::UnhookWinEvent(IntPtr hWinEventHook)` | Declara P/Invoke user32.dll!UnhookWinEvent. Assinatura preservada abaixo; uso concreto aparece nos chamadores. Corpo da API externa não está neste assembly. |
| `0x060000f1 UInt32 UI.WinApi::GetWindowThreadProcessId(IntPtr hWnd, UInt32& lpdwProcessId)` | Declara P/Invoke user32.dll!GetWindowThreadProcessId. Assinatura preservada abaixo; uso concreto aparece nos chamadores. Corpo da API externa não está neste assembly. |
| `0x060000f2 Int32 UI.WinApi::GetWindowText(IntPtr hwnd, System.Text.StringBuilder lpString, Int32 nMaxCount)` | Declara P/Invoke user32.dll!GetWindowText. Assinatura preservada abaixo; uso concreto aparece nos chamadores. Corpo da API externa não está neste assembly. |
| `0x060000f3 Int32 UI.WinApi::GetClassName(IntPtr hwnd, System.Text.StringBuilder lpClassName, Int32 nMaxCount)` | Declara P/Invoke user32.dll!GetClassName. Assinatura preservada abaixo; uso concreto aparece nos chamadores. Corpo da API externa não está neste assembly. |
| `0x060000f4 Int32 UI.WinApi::GetWindowTextLength(IntPtr hwnd)` | Declara P/Invoke user32.dll!GetWindowTextLength. Assinatura preservada abaixo; uso concreto aparece nos chamadores. Corpo da API externa não está neste assembly. |
| `0x060000f5 String UI.WinApi::GetWindowTitle(IntPtr hwnd)` | Aloca StringBuilder de GetWindowTextLength+1, chama GetWindowText e retorna string. |
| `0x060000f6 String UI.WinApi::GetWindowClass(IntPtr hwnd)` | Aloca StringBuilder de capacidade256, chama GetClassName e retorna classe da janela. |
| `0x060000f8 Void UI.WinApi::.cctor()` | Registra mensagem WM_SHOW_LS por RegisterWindowMessage e guarda número. |

### `UI.Resources.Strings`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x060000fa System.Resources.ResourceManager UI.Resources.Strings::get_ResourceManager()` | Inicialização preguiçosa de ResourceManager UI.Resources.Strings para assembly; reaproveita instância guardada. |

### `UI.Pages.ManualPage`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x0600019b Void UI.Pages.ManualPage::.ctor()` | Constrói ManualPage, carrega XAML e conecta callback Loaded para visibilidade. |
| `0x0600019c Void UI.Pages.ManualPage::BackArrow_Click(Object sender, System.Windows.RoutedEventArgs e)` | Volta por MainWindow.ShowProfilePage. |
| `0x0600019f Void UI.Pages.ManualPage::<.ctor>b__0_0(Object <p0>, System.Windows.RoutedEventArgs <p1>)` | Loaded manual: oculta Scale/Licenses e mostra Steam/Discord. Não é ajuste de desempenho. |

### `UI.Pages.ProfileEditPage`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x060001a0 Void UI.Pages.ProfileEditPage::.ctor(UI.Profile profile)` | Constrói editor e preenche Title/Path/AutoScale/Delay do Profile; mostra Delay se AutoScale. |
| `0x060001a1 Void UI.Pages.ProfileEditPage::Browse_Click(Object sender, System.Windows.RoutedEventArgs e)` | Abre seletor .exe/todos e acrescenta filename a GamePath, inserindo ; se necessário. Não executa arquivo selecionado. |
| `0x060001a2 Void UI.Pages.ProfileEditPage::Edit_Click(Object sender, System.Windows.RoutedEventArgs e)` | Exige título não vazio, grava Title/Path/AutoScale/AutoScaleDelay no objeto, atualiza lista, volta ao perfil e salva Settings. |
| `0x060001a3 Void UI.Pages.ProfileEditPage::AutoScale_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Mostra/oculta DelayContainer segundo AutoScale; não aplica ao motor. |
| `0x060001a4 Void UI.Pages.ProfileEditPage::BackArrow_Click(Object sender, System.Windows.RoutedEventArgs e)` | Volta por MainWindow.ShowProfilePage. |

### `UI.Pages.ProfileNewPage`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x060001a7 Void UI.Pages.ProfileNewPage::.ctor()` | Constrói ProfileNewPage e carrega XAML. |
| `0x060001a8 Void UI.Pages.ProfileNewPage::Browse_Click(Object sender, System.Windows.RoutedEventArgs e)` | Seletor .exe/todos; acrescenta caminho separado por ; sem executá-lo. |
| `0x060001a9 Void UI.Pages.ProfileNewPage::AutoScale_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Mostra/oculta Delay conforme AutoScale. |
| `0x060001aa Void UI.Pages.ProfileNewPage::Add_Click(Object sender, System.Windows.RoutedEventArgs e)` | Exige título, evita GamePath exatamente igual a entrada ; já cadastrada; cria perfil copiando padrão índice0, adiciona, seleciona último e salva. Não valida colisões gerais de wildcards. |
| `0x060001ab Void UI.Pages.ProfileNewPage::BackArrow_Click(Object sender, System.Windows.RoutedEventArgs e)` | Volta por MainWindow.ShowProfilePage. |

### `UI.Pages.ProfilePage`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x060001ae Void UI.Pages.ProfilePage::.ctor(UI.Profile profile)` | Constrói ProfilePage; se alvoLSFG3=0 usa DWM arredondado. Enumera adaptadores/monitores, acrescenta Auto em índice0 e conecta Loaded a ApplyProfileToUI. |
| `0x060001af Void UI.Pages.ProfilePage::ApplyProfileToUI(UI.Profile profile)` | Preenche controles e chama handlers de seleção/visibilidade; migra FrameGeneration LSFI1(5)->LSFG1(3), LSFI(4)->LSFG2(2); limita índices GPU/display à lista atual. |
| `0x060001b0 Void UI.Pages.ProfilePage::ScalingMode_SelectionChanged(Object sender, System.Windows.Controls.SelectionChangedEventArgs e)` | Grava ScalingMode pelo índice, ajusta visibilidade do fator/opções de ajuste e chama ProfileChanged. |
| `0x060001b1 Void UI.Pages.ProfilePage::ScalingFitMode_SelectionChanged(Object sender, System.Windows.Controls.SelectionChangedEventArgs e)` | Após IsLoaded grava ScalingFitMode pelo índice e chama ProfileChanged. Não contém redimensionamento gráfico direto. |
| `0x060001b2 Void UI.Pages.ProfilePage::ScalingType_SelectionChanged(Object sender, System.Windows.Controls.SelectionChangedEventArgs e)` | Grava algoritmo e mostra controles específicos. Slider nitidez: máximo10 para FSR/NIS/BicubicCAS/SGSR e4 para LS1. Integer arredonda fator/passo1; XBR arredonda décimos, ajusta décimo ímpar e usa passo0.2; outros arredondam a3 casas/passo0.1. Chama ProfileChanged. |
| `0x060001b3 Void UI.Pages.ProfilePage::ScaleFactor_PreviewTextInput(Object sender, System.Windows.Input.TextCompositionEventArgs e)` | Filtra entrada: aceita dígitos ou separador decimal da cultura corrente; entrada inválida marca Handled=true. Não valida globalmente colagem/XML. |
| `0x060001b4 Void UI.Pages.ProfilePage::ScaleFactor_ValueChanged(Object sender, System.Windows.RoutedEventArgs args)` | Após IsLoaded, substitui fator vazio por1, grava Double ScaleFactor e chama ProfileChanged. |
| `0x060001b5 Void UI.Pages.ProfilePage::ResizeBeforeScaling_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.ResizeBeforeScaling. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001b6 Void UI.Pages.ProfilePage::Sharpness_ValueChanged(Object sender, System.Windows.RoutedPropertyChangedEventArgs`1<Double> e)` | Após IsLoaded grava slider como Int32 Sharpness para FSR/NIS/BicubicCAS/SGSR, ou LS1Sharpness para LS1; chama ProfileChanged. |
| `0x060001b7 Void UI.Pages.ProfilePage::FsrOptimized_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.FSRType. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001b8 Void UI.Pages.ProfilePage::LS1Performance_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.LS1Type. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001b9 Void UI.Pages.ProfilePage::VRS_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.VRS. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001ba Void UI.Pages.ProfilePage::Anime4kType_SelectionChanged(Object sender, System.Windows.Controls.SelectionChangedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.Anime4kType. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001bb Void UI.Pages.ProfilePage::FrameGeneration_SelectionChanged(Object sender, System.Windows.Controls.SelectionChangedEventArgs e)` | Grava FrameGeneration por índice; mostra performance/FlowScale para LSFG2/LSFG3 e grupo correspondente; chama ProfileChanged. |
| `0x060001bc Void UI.Pages.ProfilePage::LSFGMode2_SelectionChanged(Object sender, System.Windows.Controls.SelectionChangedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.LSFG2Mode. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001bd Void UI.Pages.ProfilePage::LSFGMode3_SelectionChanged(Object sender, System.Windows.Controls.SelectionChangedEventArgs e)` | Grava LSFG3Mode1: Fixed mostra multiplicador, Adaptive mostra alvo; chama ProfileChanged. |
| `0x060001be Void UI.Pages.ProfilePage::LSFG3Multiplier_ValueChanged(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, multiplicador vazio vira2, arredonda3 casas, grava Single LSFG3Multiplier e chama ProfileChanged. |
| `0x060001bf Void UI.Pages.ProfilePage::LSFG3Multiplier_PreviewTextInput(Object sender, System.Windows.Input.TextCompositionEventArgs e)` | Filtra entrada: aceita dígitos ou separador decimal da cultura corrente; entrada inválida marca Handled=true. Não valida globalmente colagem/XML. |
| `0x060001c0 Void UI.Pages.ProfilePage::LSFG3Target_ValueChanged(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, alvo vazio vira DWM arredondado; arredonda3 casas, grava Single LSFG3Target e chama ProfileChanged. |
| `0x060001c1 Void UI.Pages.ProfilePage::LSFG3Target_PreviewTextInput(Object sender, System.Windows.Input.TextCompositionEventArgs e)` | Filtra entrada: aceita dígitos ou separador decimal da cultura corrente; entrada inválida marca Handled=true. Não valida globalmente colagem/XML. |
| `0x060001c2 Void UI.Pages.ProfilePage::LSFGPerformance_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.LSFGSize. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001c3 Void UI.Pages.ProfilePage::LSFGFlowScale_ValueChanged(Object sender, System.Windows.RoutedPropertyChangedEventArgs`1<Double> e)` | Após IsLoaded, lê controle e grava Profile.LSFGFlowScale. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001c4 Void UI.Pages.ProfilePage::ClipCursor_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.ClipCursor. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001c5 Void UI.Pages.ProfilePage::AdjustCursorSpeed_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.AdjustCursorSpeed. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001c6 Void UI.Pages.ProfilePage::HideCursor_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.HideCursor. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001c7 Void UI.Pages.ProfilePage::ScaleCursor_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.ScaleCursor. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001c8 Void UI.Pages.ProfilePage::SyncMode_SelectionChanged(Object sender, System.Windows.Controls.SelectionChangedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.SyncMode. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001c9 Void UI.Pages.ProfilePage::MaxFrameLatency_PreviewTextInput(Object sender, System.Windows.Input.TextCompositionEventArgs e)` | Filtra entrada: aceita dígitos; entrada inválida marca Handled=true. Não valida globalmente colagem/XML. |
| `0x060001ca Void UI.Pages.ProfilePage::MaxFrameLatency_ValueChanged(Object sender, System.Windows.RoutedEventArgs args)` | Após IsLoaded, latência vazia vira3, NumberBox vira Int32 MaxFrameLatency; chama ProfileChanged. |
| `0x060001cb Void UI.Pages.ProfilePage::GsyncSupport_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.GsyncSupport. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001cc Void UI.Pages.ProfilePage::HdrSupport_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.HdrSupport. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001cd Void UI.Pages.ProfilePage::DrawFps_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.DrawFps. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001ce Void UI.Pages.ProfilePage::CaptureApi_SelectionChanged(Object sender, System.Windows.Controls.SelectionChangedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.CaptureApi. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001cf Void UI.Pages.ProfilePage::QueueTarget_ValueChanged(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, fila vazia vira1, NumberBox vira Int32 QueueTarget; chama ProfileChanged. |
| `0x060001d0 Void UI.Pages.ProfilePage::QueueTarget_PreviewTextInput(Object sender, System.Windows.Input.TextCompositionEventArgs e)` | Filtra entrada: aceita dígitos; entrada inválida marca Handled=true. Não valida globalmente colagem/XML. |
| `0x060001d1 Void UI.Pages.ProfilePage::MultiDisplayMode_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.MultiDisplayMode. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001d2 Void UI.Pages.ProfilePage::Crop_PreviewTextInput(Object sender, System.Windows.Input.TextCompositionEventArgs e)` | Filtra entrada: aceita dígitos; entrada inválida marca Handled=true. Não valida globalmente colagem/XML. |
| `0x060001d3 Void UI.Pages.ProfilePage::CropTop_ValueChanged(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.CropInputTop. Entrada vazia vira0. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001d4 Void UI.Pages.ProfilePage::CropLeft_ValueChanged(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.CropInputLeft. Entrada vazia vira0. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001d5 Void UI.Pages.ProfilePage::CropRight_ValueChanged(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.CropInputRight. Entrada vazia vira0. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001d6 Void UI.Pages.ProfilePage::CropBottom_ValueChanged(Object sender, System.Windows.RoutedEventArgs e)` | Após IsLoaded, lê controle e grava Profile.CropInputBottom. Entrada vazia vira0. Chama ProfileChanged para salvar e, quando cabível, reenviar ao motor. |
| `0x060001d7 Void UI.Pages.ProfilePage::PreferredGpu_SelectionChanged(Object sender, System.Windows.Controls.SelectionChangedEventArgs e)` | Após IsLoaded grava índice PreferredGpuId (0=Auto visual); Auto limpa tooltip, explícito usa nome selecionado; chama ProfileChanged. |
| `0x060001d8 Void UI.Pages.ProfilePage::OutputDisplay_SelectionChanged(Object sender, System.Windows.Controls.SelectionChangedEventArgs e)` | Após IsLoaded grava índice OutputDisplayId (0=Auto visual); atualiza tooltip; chama ProfileChanged. |
| `0x060001d9 Void UI.Pages.ProfilePage::UniformGrid_SizeChanged(Object sender, System.Windows.SizeChangedEventArgs e)` | Reorganiza linhas/colunas, margens, MaxWidth e ordem dos cartões conforme largura disponível. |
| `0x060001da Void UI.Pages.ProfilePage::NumberBox_KeyDown(Object sender, System.Windows.Input.KeyEventArgs e)` | Ao pressionar Key enum6 (Return), remove foco do NumberBox por FocusManager/Keyboard.ClearFocus. |

### `UI.Pages.SettingsPage`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x060001dd Void UI.Pages.SettingsPage::.ctor()` | Constrói SettingsPage, carrega XAML e conecta Loaded. |
| `0x060001de Void UI.Pages.SettingsPage::ApplySettingsToUI(Object sender, System.Windows.RoutedEventArgs e)` | Preenche hotkey textual Ctrl/Alt/Shift/Win e campos admin/startup/tray/idioma/tema. |
| `0x060001df Void UI.Pages.SettingsPage::StartAsAdmin_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Grava StartAsAdmin e salva Settings; elevação ocorre no fluxo de inicialização. |
| `0x060001e0 Void UI.Pages.SettingsPage::StartAtWindowsStartup_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Grava StartAtWindowsStartup e salva; abre HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run com escrita. Habilitar grava valor LosslessScaling com exe derivado do assembly e -StartMinimized; desabilitar remove o valor. |
| `0x060001e1 Void UI.Pages.SettingsPage::MinimizeToTray_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Grava MinimizeToTray e salva Settings. |
| `0x060001e2 Void UI.Pages.SettingsPage::CloseToTray_Toggled(Object sender, System.Windows.RoutedEventArgs e)` | Grava CloseToTray e salva Settings. |
| `0x060001e3 Void UI.Pages.SettingsPage::ScaleHotkey_KeyDown(Object sender, System.Windows.Input.KeyEventArgs e)` | Durante edição de hotkey descarta hook atual, captura modificadores/Key/SystemKey, preenche texto e marca evento tratado; não persiste todas as teclas. |
| `0x060001e4 Void UI.Pages.SettingsPage::ScaleHotkey_KeyUp(Object sender, System.Windows.Input.KeyEventArgs e)` | Ao terminar hotkey grava tecla/modificadores, salva e instala GlobalKeyboardHook novo; ajusta foco/texto. |
| `0x060001e5 Void UI.Pages.SettingsPage::Theme_SelectionChanged(Object sender, System.Windows.Controls.SelectionChangedEventArgs e)` | Se mudou, grava Theme, salva e chama SetUITheme. |
| `0x060001e6 Void UI.Pages.SettingsPage::LanguageUI_SelectionChanged(Object sender, System.Windows.Controls.SelectionChangedEventArgs e)` | Se mudou, grava Language, salva e chama SetUILanguage; muda visibilidade de aviso. |
| `0x060001e7 Void UI.Pages.SettingsPage::UniformGrid_SizeChanged(Object sender, System.Windows.SizeChangedEventArgs e)` | Adapta layout à largura: ajusta linhas/colunas, margens, MaxWidth e ordem de elementos. |
| `0x060001e8 Void UI.Pages.SettingsPage::BackArrow_Click(Object sender, System.Windows.RoutedEventArgs e)` | Volta por MainWindow.ShowProfilePage. |
| `0x060001eb Void UI.Pages.SettingsPage::<.ctor>b__4_0(Object <p0>, System.Windows.RoutedEventArgs <p1>)` | Loaded SettingsPage ajusta visibilidade de botões da janela. |

### `.<PrivateImplementationDetails>`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x060001ec System.ReadOnlySpan`1<!!1> .<PrivateImplementationDetails>::InlineArrayAsReadOnlySpan(!!0& buffer, Int32 length)` | Helper gerado: expõe inline array como ReadOnlySpan por Unsafe/MemoryMarshal para formatação de mensagens. |
| `0x060001ed !!1& .<PrivateImplementationDetails>::InlineArrayElementRef(!!0& buffer, Int32 index)` | Helper gerado: referência a elemento de inline array por Unsafe.As/Add para argumentos de mensagens. |

### `.<<-ctor>b__19_2>d`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x060001f6 Void .<<-ctor>b__19_2>d::MoveNext()` | MoveNext da hotkey: compara janela ativa com HWND próprio; mostra aviso/manual se própria janela, caso apropriado chama ActivateCore. Gerencia await e erros. |
| `0x060001f7 Void .<<-ctor>b__19_2>d::SetStateMachine(System.Runtime.CompilerServices.IAsyncStateMachine stateMachine)` | Encaminha máquina ao AsyncVoid/TaskMethodBuilder.SetStateMachine, suporte ao MoveNext associado. |

### `.<<ScaleButton_Click>b__43_3>d`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x060001f8 Void .<<ScaleButton_Click>b__43_3>d::MoveNext()` | MoveNext do fim da contagem manual: verifica alvo contra própria janela, pode mostrar aviso/manual; chama ActivateCore quando apropriado; gerencia await. |
| `0x060001f9 Void .<<ScaleButton_Click>b__43_3>d::SetStateMachine(System.Runtime.CompilerServices.IAsyncStateMachine stateMachine)` | Encaminha máquina ao AsyncVoid/TaskMethodBuilder.SetStateMachine, suporte ao MoveNext associado. |

### `.<>c`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x060001fc String .<>c::<.ctor>b__19_0(UI.Profile p)` | Seletor de ordenação retorna Profile.Title. |

### `.<>c__DisplayClass19_0`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x060001fe Void .<>c__DisplayClass19_0::<.ctor>b__5()` | Callback visual de erro mostra status, texto acumulado, restaura Scale e ajusta RowSpan. |
| `0x060001ff Void .<>c__DisplayClass19_0::<.ctor>b__7()` | Callback visual atribui texto de status acumulado ao TextBlock. |

### `.<>c__DisplayClass19_1`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x06000201 Void .<>c__DisplayClass19_1::<.ctor>b__11()` | Conclusão do autoscale: botão Unscale e ActivateCore para HWND capturado. |

### `.<>c__DisplayClass19_2`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x06000203 Void .<>c__DisplayClass19_2::<.ctor>b__9(Object <p0>)` | Tick da contagem automática: despacha número para UI; no fim descarta Timer e ativa HWND capturado. |
| `0x06000204 Void .<>c__DisplayClass19_2::<.ctor>b__10()` | Callback visual do countdown automático atualiza conteúdo do botão. |

### `.<>c__DisplayClass26_0`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x06000206 Void .<>c__DisplayClass26_0::<ActivateCore>b__0()` | Callback de foco pré-ativação ajusta foco quando conteúdo corresponde ao tipo esperado. |
| `0x06000207 Void .<>c__DisplayClass26_0::<ActivateCore>b__3()` | Callback visual define Unscale. |
| `0x06000208 Void .<>c__DisplayClass26_0::<ActivateCore>b__1()` | Callback visual define Scale. |
| `0x06000209 Void .<>c__DisplayClass26_0::<ActivateCore>b__2()` | Chama Core.Activate(hwnd) e descarta Boolean retornado. |

### `.<>c__DisplayClass43_0`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x0600020b Void .<>c__DisplayClass43_0::<ScaleButton_Click>b__1(Object <p0>)` | Tick da contagem manual: despacha número para UI, ao fim descarta Timer e dispara Task que verifica janela alvo. |
| `0x0600020c Void .<>c__DisplayClass43_0::<ScaleButton_Click>b__2()` | Callback visual countdown manual atualiza texto do botão. |

### `.<RemoveButton_Click>d__41`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x0600020d Void .<RemoveButton_Click>d__41::MoveNext()` | MoveNext de remoção: diálogo com título do perfil; se confirmado remove objeto, ajusta seleção e refresh visual. Não chama SaveSettings diretamente; persistência depende de eventos posteriores. |
| `0x0600020e Void .<RemoveButton_Click>d__41::SetStateMachine(System.Runtime.CompilerServices.IAsyncStateMachine stateMachine)` | Encaminha máquina ao AsyncVoid/TaskMethodBuilder.SetStateMachine, suporte ao MoveNext associado. |

### `.<>c__DisplayClass0_0`

| Token / assinatura | Função e efeitos |
|---|---|
| `0x06000214 Void .<>c__DisplayClass0_0::<.ctor>b__0(Object <p0>, System.Windows.RoutedEventArgs <p1>)` | Loaded de ProfilePage ajusta visibilidade de botões da janela. |
| `0x06000215 Void .<>c__DisplayClass0_0::<.ctor>b__1(Object <p0>, System.Windows.RoutedEventArgs <p1>)` | Loaded de ProfilePage chama ApplyProfileToUI com Profile capturado. |

## Acessores globais e suporte gerado

Getters/setters de Settings apenas acessam campos; Save/Load e handlers efetuam persistência/ações. Grupos *Collapsed armazenam estado visual, não otimizações.

| Propriedade de Settings | Getter / setter |
|---|---|
| `Hotkey: System.Windows.Input.Key` | `0x060000bf` / `0x060000c0` |
| `HotkeyModifierKeys: System.Windows.Input.ModifierKeys` | `0x060000c1` / `0x060000c2` |
| `GpuPreference: Int32` | `0x060000c3` / `0x060000c4` |
| `GpuPreferenceChangeCount: Int32` | `0x060000c5` / `0x060000c6` |
| `SetupPhase: Int32` | `0x060000c7` / `0x060000c8` |
| `StartAsAdmin: Boolean` | `0x060000c9` / `0x060000ca` |
| `StartAtWindowsStartup: Boolean` | `0x060000cb` / `0x060000cc` |
| `MinimizeToTray: Boolean` | `0x060000cd` / `0x060000ce` |
| `CloseToTray: Boolean` | `0x060000cf` / `0x060000d0` |
| `Language: .LanguageEnum` | `0x060000d1` / `0x060000d2` |
| `Theme: .ThemeModeEnum` | `0x060000d3` / `0x060000d4` |
| `FrameGenerationCollapsed: Boolean` | `0x060000d5` / `0x060000d6` |
| `CursorOptionsCollapsed: Boolean` | `0x060000d7` / `0x060000d8` |
| `RenderingOptionsCollapsed: Boolean` | `0x060000d9` / `0x060000da` |
| `CaptureOptionsCollapsed: Boolean` | `0x060000db` / `0x060000dc` |
| `GpuDisplayOptionsCollapsed: Boolean` | `0x060000dd` / `0x060000de` |
| `CropInputOptionsCollapsed: Boolean` | `0x060000df` / `0x060000e0` |
| `BehaviorCollapsed: Boolean` | `0x060000e1` / `0x060000e2` |
| `LegacyOptionsCollapsed: Boolean` | `0x060000e3` / `0x060000e4` |
| `GameProfiles: System.Collections.Generic.List`1<UI.Profile>` | `0x060000e5` / `0x060000e6` |

Os campos públicos WindowMaximized, WindowHeight, WindowWidth e ProfileListWidth são persistidos como estado da janela.

Os demais métodos de suporte ficam individualizados no manifesto; abaixo a relação completa por classe.

| Classe | Tokens, nomes e categorias |
|---|---|
| `UI.App` | 0x06000002: InitializeComponent [xaml-generated]; 0x06000004: .ctor [constructor-boilerplate] |
| `UI.Core` | 0x06000012: .ctor [constructor-boilerplate] |
| `UI.ForegroundWatcher` | 0x06000014: add_OnForegroundEvent [event-accessor]; 0x06000015: remove_OnForegroundEvent [event-accessor] |
| `UI.GlobalKeyboardHook` | 0x0600001e: add_HotKeyPressed [event-accessor]; 0x0600001f: remove_HotKeyPressed [event-accessor]; 0x06000020: get_Key [accessor-boilerplate]; 0x06000021: set_Key [accessor-boilerplate]; 0x06000022: get_KeyModifier [accessor-boilerplate]; 0x06000023: set_KeyModifier [accessor-boilerplate] |
| `UI.HotKey` | 0x06000030: add_HotKeyPressed [event-accessor]; 0x06000031: remove_HotKeyPressed [event-accessor]; 0x06000032: get_Key [accessor-boilerplate]; 0x06000033: set_Key [accessor-boilerplate]; 0x06000034: get_KeyModifier [accessor-boilerplate]; 0x06000035: set_KeyModifier [accessor-boilerplate] |
| `UI.LogWriter` | 0x0600003e: .ctor [constructor-boilerplate] |
| `UI.MainWindow` | 0x0600005d: InitializeComponent [xaml-generated]; 0x0600005e: System.Windows.Markup.IComponentConnector.Connect [xaml-generated] |
| `UI.WinApi` | 0x060000f7: .ctor [constructor-boilerplate] |
| `UI.Pages.ManualPage` | 0x0600019d: InitializeComponent [xaml-generated]; 0x0600019e: System.Windows.Markup.IComponentConnector.Connect [xaml-generated] |
| `UI.Pages.ProfileEditPage` | 0x060001a5: InitializeComponent [xaml-generated]; 0x060001a6: System.Windows.Markup.IComponentConnector.Connect [xaml-generated] |
| `UI.Pages.ProfileNewPage` | 0x060001ac: InitializeComponent [xaml-generated]; 0x060001ad: System.Windows.Markup.IComponentConnector.Connect [xaml-generated] |
| `UI.Pages.ProfilePage` | 0x060001db: InitializeComponent [xaml-generated]; 0x060001dc: System.Windows.Markup.IComponentConnector.Connect [xaml-generated] |
| `UI.Pages.SettingsPage` | 0x060001e9: InitializeComponent [xaml-generated]; 0x060001ea: System.Windows.Markup.IComponentConnector.Connect [xaml-generated] |
| `.StatusListenerDelegate` | 0x060001ee: .ctor [delegate-runtime]; 0x060001ef: Invoke [delegate-runtime]; 0x060001f0: BeginInvoke [delegate-runtime]; 0x060001f1: EndInvoke [delegate-runtime] |
| `.KeyboardHookProc` | 0x060001f2: .ctor [delegate-runtime]; 0x060001f3: Invoke [delegate-runtime]; 0x060001f4: BeginInvoke [delegate-runtime]; 0x060001f5: EndInvoke [delegate-runtime] |
| `.<>c` | 0x060001fa: .cctor [constructor-boilerplate]; 0x060001fb: .ctor [constructor-boilerplate] |
| `.<>c__DisplayClass19_0` | 0x060001fd: .ctor [constructor-boilerplate] |
| `.<>c__DisplayClass19_1` | 0x06000200: .ctor [constructor-boilerplate] |
| `.<>c__DisplayClass19_2` | 0x06000202: .ctor [constructor-boilerplate] |
| `.<>c__DisplayClass26_0` | 0x06000205: .ctor [constructor-boilerplate] |
| `.<>c__DisplayClass43_0` | 0x0600020a: .ctor [constructor-boilerplate] |
| `.WinEventDelegate` | 0x0600020f: .ctor [delegate-runtime]; 0x06000210: Invoke [delegate-runtime]; 0x06000211: BeginInvoke [delegate-runtime]; 0x06000212: EndInvoke [delegate-runtime] |
| `.<>c__DisplayClass0_0` | 0x06000213: .ctor [constructor-boilerplate] |

`InitializeComponent` carrega XAML; `Connect` conecta controles/eventos; `add_*`/`remove_*` modificam delegados atomicamente; construtores auxiliares preparam closures; membros de delegado são suporte do runtime. SetStateMachine encaminha máquina ao builder, e MoveNext tem comportamento explicado acima.

### UI.Resources.Strings: um getter por texto

Todos os getters de texto fazem `ResourceManager.GetString(chave, resourceCulture)`. São recursos de interface, não ajustes. ResourceManager (`0x060000fa`) usa inicialização preguiçosa; Culture (`0x060000fb`/`0x060000fc`) só lê/escreve o campo; construtor (`0x060000f9`) chama base.

| Token | Getter → chave |
|---|---|
| `0x060000fd` | `get_Control_Off` → `Control_Off` |
| `0x060000fe` | `get_Control_On` → `Control_On` |
| `0x060000ff` | `get_EditProfile` → `EditProfile` |
| `0x06000100` | `get_EditProfile_Edit` → `EditProfile_Edit` |
| `0x06000101` | `get_Main_AdjustCursorSpeed` → `Main_AdjustCursorSpeed` |
| `0x06000102` | `get_Main_AdjustCursorSpeed_Tooltip` → `Main_AdjustCursorSpeed_Tooltip` |
| `0x06000103` | `get_Main_Administrator` → `Main_Administrator` |
| `0x06000104` | `get_Main_Anime4k_L` → `Main_Anime4k_L` |
| `0x06000105` | `get_Main_Anime4k_M` → `Main_Anime4k_M` |
| `0x06000106` | `get_Main_Anime4k_S` → `Main_Anime4k_S` |
| `0x06000107` | `get_Main_Anime4k_UL` → `Main_Anime4k_UL` |
| `0x06000108` | `get_Main_Anime4k_VL` → `Main_Anime4k_VL` |
| `0x06000109` | `get_Main_Behavior` → `Main_Behavior` |
| `0x0600010a` | `get_Main_Capture` → `Main_Capture` |
| `0x0600010b` | `get_Main_Capture_QueueTarget` → `Main_Capture_QueueTarget` |
| `0x0600010c` | `get_Main_Capture_QueueTarget_Tooltip` → `Main_Capture_QueueTarget_Tooltip` |
| `0x0600010d` | `get_Main_CaptureApi` → `Main_CaptureApi` |
| `0x0600010e` | `get_Main_CaptureApi_Tooltip` → `Main_CaptureApi_Tooltip` |
| `0x0600010f` | `get_Main_ClipCursor` → `Main_ClipCursor` |
| `0x06000110` | `get_Main_ClipCursor_Tooltip` → `Main_ClipCursor_Tooltip` |
| `0x06000111` | `get_Main_CropInput` → `Main_CropInput` |
| `0x06000112` | `get_Main_CursorOptions` → `Main_CursorOptions` |
| `0x06000113` | `get_Main_DrawFps` → `Main_DrawFps` |
| `0x06000114` | `get_Main_DrawFps_Tooltip` → `Main_DrawFps_Tooltip` |
| `0x06000115` | `get_Main_FrameGeneration` → `Main_FrameGeneration` |
| `0x06000116` | `get_Main_FrameGeneration_LSFG_FlowScale` → `Main_FrameGeneration_LSFG_FlowScale` |
| `0x06000117` | `get_Main_FrameGeneration_LSFG_FlowScale_Tooltip` → `Main_FrameGeneration_LSFG_FlowScale_Tooltip` |
| `0x06000118` | `get_Main_FrameGeneration_LSFG_Mode` → `Main_FrameGeneration_LSFG_Mode` |
| `0x06000119` | `get_Main_FrameGeneration_LSFG_Mode_Adaptive` → `Main_FrameGeneration_LSFG_Mode_Adaptive` |
| `0x0600011a` | `get_Main_FrameGeneration_LSFG_Mode_Fixed` → `Main_FrameGeneration_LSFG_Mode_Fixed` |
| `0x0600011b` | `get_Main_FrameGeneration_LSFG_Mode_Tooltip` → `Main_FrameGeneration_LSFG_Mode_Tooltip` |
| `0x0600011c` | `get_Main_FrameGeneration_LSFG_Multiplier` → `Main_FrameGeneration_LSFG_Multiplier` |
| `0x0600011d` | `get_Main_FrameGeneration_LSFG_Performance` → `Main_FrameGeneration_LSFG_Performance` |
| `0x0600011e` | `get_Main_FrameGeneration_LSFG_Performance_Tooltip` → `Main_FrameGeneration_LSFG_Performance_Tooltip` |
| `0x0600011f` | `get_Main_FrameGeneration_LSFG_Target` → `Main_FrameGeneration_LSFG_Target` |
| `0x06000120` | `get_Main_FrameGeneration_LSFG3_Mode_Tooltip` → `Main_FrameGeneration_LSFG3_Mode_Tooltip` |
| `0x06000121` | `get_Main_FrameGeneration_Off` → `Main_FrameGeneration_Off` |
| `0x06000122` | `get_Main_FrameGeneration_Type` → `Main_FrameGeneration_Type` |
| `0x06000123` | `get_Main_GameProfiles` → `Main_GameProfiles` |
| `0x06000124` | `get_Main_GpuDisplay` → `Main_GpuDisplay` |
| `0x06000125` | `get_Main_GsyncSupport` → `Main_GsyncSupport` |
| `0x06000126` | `get_Main_GsyncSupport_Tooltip` → `Main_GsyncSupport_Tooltip` |
| `0x06000127` | `get_Main_HdrSupport` → `Main_HdrSupport` |
| `0x06000128` | `get_Main_HdrSupport_Tooltip` → `Main_HdrSupport_Tooltip` |
| `0x06000129` | `get_Main_HideCursor` → `Main_HideCursor` |
| `0x0600012a` | `get_Main_LS1_Performance` → `Main_LS1_Performance` |
| `0x0600012b` | `get_Main_LS1_Performance_Tooltip` → `Main_LS1_Performance_Tooltip` |
| `0x0600012c` | `get_Main_Manual` → `Main_Manual` |
| `0x0600012d` | `get_Main_MaxFrameLatency` → `Main_MaxFrameLatency` |
| `0x0600012e` | `get_Main_MaxFrameLatency_Tooltip` → `Main_MaxFrameLatency_Tooltip` |
| `0x0600012f` | `get_Main_MultiDisplayMode` → `Main_MultiDisplayMode` |
| `0x06000130` | `get_Main_MultiDisplayMode_Tooltip` → `Main_MultiDisplayMode_Tooltip` |
| `0x06000131` | `get_Main_OptimizedVersion` → `Main_OptimizedVersion` |
| `0x06000132` | `get_Main_OptimizedVersion_Tooltip` → `Main_OptimizedVersion_Tooltip` |
| `0x06000133` | `get_Main_OutputDisplay` → `Main_OutputDisplay` |
| `0x06000134` | `get_Main_OutputDisplay_Auto` → `Main_OutputDisplay_Auto` |
| `0x06000135` | `get_Main_PreferredGpu` → `Main_PreferredGpu` |
| `0x06000136` | `get_Main_PreferredGpu_Auto` → `Main_PreferredGpu_Auto` |
| `0x06000137` | `get_Main_ProfileAdd` → `Main_ProfileAdd` |
| `0x06000138` | `get_Main_ProfileCancel` → `Main_ProfileCancel` |
| `0x06000139` | `get_Main_ProfileConfirm` → `Main_ProfileConfirm` |
| `0x0600013a` | `get_Main_ProfileDefault` → `Main_ProfileDefault` |
| `0x0600013b` | `get_Main_ProfileRemove` → `Main_ProfileRemove` |
| `0x0600013c` | `get_Main_RenderingOptions` → `Main_RenderingOptions` |
| `0x0600013d` | `get_Main_ResizeBeforeScaling` → `Main_ResizeBeforeScaling` |
| `0x0600013e` | `get_Main_ResizeBeforeScaling_Tooltip` → `Main_ResizeBeforeScaling_Tooltip` |
| `0x0600013f` | `get_Main_Scale` → `Main_Scale` |
| `0x06000140` | `get_Main_Scale_Tooltip` → `Main_Scale_Tooltip` |
| `0x06000141` | `get_Main_ScaleCursor` → `Main_ScaleCursor` |
| `0x06000142` | `get_Main_ScaleFactor` → `Main_ScaleFactor` |
| `0x06000143` | `get_Main_Scaling` → `Main_Scaling` |
| `0x06000144` | `get_Main_Scaling_Factor` → `Main_Scaling_Factor` |
| `0x06000145` | `get_Main_Scaling_Mode` → `Main_Scaling_Mode` |
| `0x06000146` | `get_Main_Scaling_Type` → `Main_Scaling_Type` |
| `0x06000147` | `get_Main_ScalingFitMode_AspectRatio` → `Main_ScalingFitMode_AspectRatio` |
| `0x06000148` | `get_Main_ScalingFitMode_Fullscreen` → `Main_ScalingFitMode_Fullscreen` |
| `0x06000149` | `get_Main_ScalingFitMode_Tooltip` → `Main_ScalingFitMode_Tooltip` |
| `0x0600014a` | `get_Main_ScalingMode` → `Main_ScalingMode` |
| `0x0600014b` | `get_Main_ScalingMode_Auto` → `Main_ScalingMode_Auto` |
| `0x0600014c` | `get_Main_ScalingMode_Custom` → `Main_ScalingMode_Custom` |
| `0x0600014d` | `get_Main_ScalingMode_Tooltip` → `Main_ScalingMode_Tooltip` |
| `0x0600014e` | `get_Main_ScalingType` → `Main_ScalingType` |
| `0x0600014f` | `get_Main_ScalingType_Anime4k` → `Main_ScalingType_Anime4k` |
| `0x06000150` | `get_Main_ScalingType_BicubicCAS` → `Main_ScalingType_BicubicCAS` |
| `0x06000151` | `get_Main_ScalingType_FSR` → `Main_ScalingType_FSR` |
| `0x06000152` | `get_Main_ScalingType_Integer` → `Main_ScalingType_Integer` |
| `0x06000153` | `get_Main_ScalingType_LS1` → `Main_ScalingType_LS1` |
| `0x06000154` | `get_Main_ScalingType_NIS` → `Main_ScalingType_NIS` |
| `0x06000155` | `get_Main_ScalingType_NN` → `Main_ScalingType_NN` |
| `0x06000156` | `get_Main_ScalingType_Off` → `Main_ScalingType_Off` |
| `0x06000157` | `get_Main_ScalingType_SGSR` → `Main_ScalingType_SGSR` |
| `0x06000158` | `get_Main_ScalingType_SharpBilinear` → `Main_ScalingType_SharpBilinear` |
| `0x06000159` | `get_Main_ScalingType_XBR` → `Main_ScalingType_XBR` |
| `0x0600015a` | `get_Main_Settings` → `Main_Settings` |
| `0x0600015b` | `get_Main_Sharpness` → `Main_Sharpness` |
| `0x0600015c` | `get_Main_Size` → `Main_Size` |
| `0x0600015d` | `get_Main_Status` → `Main_Status` |
| `0x0600015e` | `get_Main_Status_Error_1` → `Main_Status_Error_1` |
| `0x0600015f` | `get_Main_Status_Error_2` → `Main_Status_Error_2` |
| `0x06000160` | `get_Main_Status_Focused` → `Main_Status_Focused` |
| `0x06000161` | `get_Main_Status_Profile` → `Main_Status_Profile` |
| `0x06000162` | `get_Main_Status_Resolution` → `Main_Status_Resolution` |
| `0x06000163` | `get_Main_Status_Resolution_Resized` → `Main_Status_Resolution_Resized` |
| `0x06000164` | `get_Main_Status_Scaling` → `Main_Status_Scaling` |
| `0x06000165` | `get_Main_SyncMode` → `Main_SyncMode` |
| `0x06000166` | `get_Main_SyncMode_Default` → `Main_SyncMode_Default` |
| `0x06000167` | `get_Main_SyncMode_Off` → `Main_SyncMode_Off` |
| `0x06000168` | `get_Main_SyncMode_Tooltip` → `Main_SyncMode_Tooltip` |
| `0x06000169` | `get_Main_SyncMode_Vsync1` → `Main_SyncMode_Vsync1` |
| `0x0600016a` | `get_Main_SyncMode_Vsync2` → `Main_SyncMode_Vsync2` |
| `0x0600016b` | `get_Main_SyncMode_Vsync3` → `Main_SyncMode_Vsync3` |
| `0x0600016c` | `get_Main_Unscale` → `Main_Unscale` |
| `0x0600016d` | `get_Main_VRS` → `Main_VRS` |
| `0x0600016e` | `get_Main_VRS_Tooltip` → `Main_VRS_Tooltip` |
| `0x0600016f` | `get_Manual` → `Manual` |
| `0x06000170` | `get_Manual_Close` → `Manual_Close` |
| `0x06000171` | `get_Manual_CommunityPage` → `Manual_CommunityPage` |
| `0x06000172` | `get_Manual_DiscordServer` → `Manual_DiscordServer` |
| `0x06000173` | `get_Manual_Text` → `Manual_Text` |
| `0x06000174` | `get_NewProfile` → `NewProfile` |
| `0x06000175` | `get_NewProfile_Add` → `NewProfile_Add` |
| `0x06000176` | `get_NewProfile_Autoscale` → `NewProfile_Autoscale` |
| `0x06000177` | `get_NewProfile_Autoscale_Tooltip` → `NewProfile_Autoscale_Tooltip` |
| `0x06000178` | `get_NewProfile_Browse` → `NewProfile_Browse` |
| `0x06000179` | `get_NewProfile_Browse_AllFiles` → `NewProfile_Browse_AllFiles` |
| `0x0600017a` | `get_NewProfile_Browse_ExecutableFiles` → `NewProfile_Browse_ExecutableFiles` |
| `0x0600017b` | `get_NewProfile_Cancel` → `NewProfile_Cancel` |
| `0x0600017c` | `get_NewProfile_Delay` → `NewProfile_Delay` |
| `0x0600017d` | `get_NewProfile_Delay_Tooltip` → `NewProfile_Delay_Tooltip` |
| `0x0600017e` | `get_NewProfile_Executable` → `NewProfile_Executable` |
| `0x0600017f` | `get_NewProfile_GamePath_Tooltip` → `NewProfile_GamePath_Tooltip` |
| `0x06000180` | `get_NewProfile_Title` → `NewProfile_Title` |
| `0x06000181` | `get_NotifyIcon_Exit` → `NotifyIcon_Exit` |
| `0x06000182` | `get_Settings` → `Settings` |
| `0x06000183` | `get_Settings_Behavior` → `Settings_Behavior` |
| `0x06000184` | `get_Settings_Close` → `Settings_Close` |
| `0x06000185` | `get_Settings_CloseToTray` → `Settings_CloseToTray` |
| `0x06000186` | `get_Settings_Interface` → `Settings_Interface` |
| `0x06000187` | `get_Settings_Language` → `Settings_Language` |
| `0x06000188` | `get_Settings_Language_RestartRequired` → `Settings_Language_RestartRequired` |
| `0x06000189` | `get_Settings_Language_System` → `Settings_Language_System` |
| `0x0600018a` | `get_Settings_Licenses` → `Settings_Licenses` |
| `0x0600018b` | `get_Settings_MinimizeToTray` → `Settings_MinimizeToTray` |
| `0x0600018c` | `get_Settings_ScaleHotkey` → `Settings_ScaleHotkey` |
| `0x0600018d` | `get_Settings_StartAsAdmin` → `Settings_StartAsAdmin` |
| `0x0600018e` | `get_Settings_StartAsAdmin_Tooltip` → `Settings_StartAsAdmin_Tooltip` |
| `0x0600018f` | `get_Settings_StartAtWindowsStartup` → `Settings_StartAtWindowsStartup` |
| `0x06000190` | `get_Settings_Theme` → `Settings_Theme` |
| `0x06000191` | `get_Settings_Theme_Dark` → `Settings_Theme_Dark` |
| `0x06000192` | `get_Settings_Theme_Light` → `Settings_Theme_Light` |
| `0x06000193` | `get_Settings_Theme_System` → `Settings_Theme_System` |
| `0x06000194` | `get_SteamDialog_Notification` → `SteamDialog_Notification` |
| `0x06000195` | `get_SteamDialog_Title` → `SteamDialog_Title` |
| `0x06000196` | `get_WindowsVersionDialog_Close` → `WindowsVersionDialog_Close` |
| `0x06000197` | `get_WindowsVersionDialog_Notification` → `WindowsVersionDialog_Notification` |
| `0x06000198` | `get_WindowsVersionDialog_Title` → `WindowsVersionDialog_Title` |
| `0x06000199` | `get_WrongWindowDialog_Notification` → `WrongWindowDialog_Notification` |
| `0x0600019a` | `get_WrongWindowDialog_Title` → `WrongWindowDialog_Title` |

## Achados práticos

1. SetDriverSettings existe como export, mas nenhum método do assembly examinado o chama.
2. SetWindowsSettings é chamado ao iniciar com GPU rotativa e possível relançamento. Escritas exatas, condições internas e reversão nativas continuam desconhecidas.
3. Alteração de perfil só reenvia parâmetros imediatamente em TempDisabled e para o mesmo objeto ativo.
4. WindowedMode e CropInput não são enviados como flags a ApplySettings. Os quatro offsets de recorte são enviados independentemente.
5. Cópia de Profile omite VRS, DrawFps e CropInput; permanecem padrões. Impacto em sessão não testado.
6. UI migra LSFI1→LSFG1 e LSFI→LSFG2 ao preencher perfil.
7. Hook observado filtra hotkey/modificadores; não há persistência de toda a digitação nesse fluxo.
8. Remoção de perfil não chama SaveSettings diretamente no MoveNext; persistência pode ocorrer por eventos/fechamento posteriores.

## Evidência e reprodução

Arquivos privados: deep/LosslessScaling.enriched.json (campos, enums, parâmetros, marshal e IL); deep/native-functions.json (533 métodos); deep/NativeMetadataReader (leitor); deep/native-descriptions.json e deep/build_native_catalogue.py (catálogo reprodutível).

O leitor usa File.OpenRead e PEReader. Não usa Assembly.Load, não chama exports nem inicia o produto. typeof(OpCodes) consulta apenas a biblioteca padrão do leitor. Não foram abertos Settings.xml/log.txt pessoais nem alterados Windows ou perfis reais.

```powershell
# Leitura de bytes/metadados, sem iniciar alvo:
& '.tools/dotnet/dotnet.exe' run --project 'artifacts/private/gdz-analysis/deep/NativeMetadataReader' -- `
  '%LOCALAPPDATA%\Programs\GDZ Optimizer\resources\app.asar.unpacked\bin\LosslessScaling.dll' `
  'artifacts/private/gdz-analysis/deep/LosslessScaling.enriched.json'
python 'artifacts/private/gdz-analysis/deep/build_native_catalogue.py'
```

O catálogo de exports não enumera funções internas de Lossless.dll. Reconstrução dos corpos nativos, gráficos de chamada, shaders e qualidade/performance em uso permanece pendente. PresentMon e apphosts são descritos no relatório de auxiliares, não como funções CLR desta tabela.
