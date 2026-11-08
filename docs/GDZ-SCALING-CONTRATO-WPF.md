# Scaling: contrato da janela WPF realmente aberta

Aprofundamento estático de 08/11/2026, GDZ 1.1.27. O assembly gerenciado `LosslessScaling.dll` 3.2.2.0 foi decompilado em C# e seus recursos BAML em XAML pelo ILSpyCmd 11.2.0.10050. A análise anterior enumerava IL e assinaturas; esta etapa recupera as opções, limites e vínculos dos controles efetivamente usados no WPF.

O projeto intermediário gerado pelo decompilador ficou em `artifacts/private/gdz-analysis/deep/wpf-decompiled`; **não foi compilado nem iniciado**. Não houve leitura do Settings.xml real do usuário. Os nomes são evidência da versão examinada, não especificação de um motor próprio.

Foram recuperados **7 arquivos XAML**, **30 arquivos intermediários**, **72 elementos interativos ou com eventos**, **81 vínculos de evento** e **158 strings base de recursos**. Elementos de layout com SizeChanged e navegação também entram nessa contagem; ela não representa esse número de otimizações.

## Novas regras úteis para implementar

| Recurso | Contrato observado | Evidência C# privada |
|---|---|---|
| Algoritmos de escala | Off, LS1, FSR, NIS, SGSR, BicubicCAS, Anime4K, XBR, SharpBilinear, Integer e Nearest são valores distintos. Não equiparar o FSR espacial deste menu a integração FSR 3 do jogo. | UI/Profile.cs; UI.Pages/ProfilePage.xaml |
| Geração de quadros | Rótulos visíveis GDZ 3.1, GDZ 2.3 e GDZ 1.1 correspondem por índice a LSFG3, LSFG2 e LSFG1; o rótulo não altera o enum. LSFI/LSFI1 persistidos são migrados para LSFG2/LSFG1 ao exibir o perfil. | ProfilePage.xaml.cs:66,340 |
| LSFG3 | Fixed usa multiplicador, Adaptive usa alvo. Multiplicador na UI 1–20; alvo 10–1000. Entrada vazia do multiplicador volta a 2; do alvo usa refresh DWM arredondado. Valores são arredondados a três casas e convertidos para float. | ProfilePage.xaml.cs:385,404,426 |
| Flow scale | Slider inteiro 25–100, passo 5. Exposto para LSFG2/3; valor inteiro enviado ao core, não fração 0–1. | ProfilePage.xaml; ProfilePage.xaml.cs:340,464 |
| Performance | Toggle muda LSFGSize entre BALANCED=0 e PERFORMANCE=1; existe de verdade no WPF. | ProfilePage.xaml.cs:448 |
| Nitidez | FSR/NIS/SGSR/BicubicCAS usam Sharpness 0–10; LS1 usa LS1Sharpness com máximo 4. Trocar algoritmo troca o campo editado. | ProfilePage.xaml.cs:167,274 |
| Fator | Ao trocar algoritmo, Integer arredonda ao inteiro e passo 1; XBR arredonda a uma casa e ajusta décimo ímpar para baixo em 0,1, passo 0,2; demais arredondam a três casas, passo 0,1. ScaleFactor_ValueChanged só repõe 1 se vazio e salva Value: não repete essa coerção a cada entrada. O filtro de digitação aceita separador decimal da cultura atual. | ProfilePage.xaml.cs:167,244,252 |
| Captura | DXGI=0 e WGC=1. QueueTarget na UI 0–2, padrão/entrada vazia 1. O contrato da ponte JavaScript legada é diferente. | ProfilePage.xaml; ProfilePage.xaml.cs:566,575 |
| Enum versus opção oferecida | CaptureApiEnum também contém GDI=2 e SyncModeEnum contém VSYNC4=5, mas o XAML atual só oferece DXGI/WGC e Off/Default/Vsync/½/⅓. Valor persistível não comprova controle disponível no menu. | UI/Profile.cs; ProfilePage.xaml |
| Cursor, latência e FPS | ClipCursor, AdjustCursorSpeed, HideCursor, ScaleCursor, MaxFrameLatency, GsyncSupport, HdrSupport e DrawFps têm handlers que escrevem perfil e chamam ProfileChanged. Não são controles decorativos no WPF. | ProfilePage.xaml.cs:473–566 |
| GPU/monitor | UI oferece Auto e só adiciona nomes quando há mais de um. Índice que excede a lista volta a 0 na exibição. Handlers persistem o índice selecionado; enumeração e ordenação vêm do core. | ProfilePage.xaml.cs:17,665,682 |
| Recorte | Quatro NumberBoxes de inteiros não negativos, vazios voltam a zero. São enviados Left,Top,Right,Bottom ao core. CropInput/WindowedMode não viram argumentos booleanos de ApplySettings. | ProfilePage.xaml.cs:605–652; MainWindow.xaml.cs:540 |
| Aplicar alteração | Cada handler chama ProfileChanged, que salva XML. Reaplica parâmetros imediatamente somente quando status=TempDisabled e o perfil alterado é o mesmo objeto do perfil ativo. Edição durante Enabled não chama ApplySettings por esse método. | MainWindow.xaml.cs:531 |
| Atalho padrão | Ctrl+Alt+S. Hook WH_KEYBOARD_LL=13 compara modificadores exatos, não apenas subconjunto. O primeiro keydown correspondente dispara callback e retorna 1; repetidos até keyup não repetem callback, mas seguem para CallNextHookEx. Não grava histórico de teclas nesse fluxo. | UI/Settings.cs; UI/GlobalKeyboardHook.cs |
| Atalho editável | Aceita A–Z com modificadores, ou F1–F24; Alt usa SystemKey. Enquanto captura a combinação, descarta hook anterior; ao confirmar salva e cria novo hook. Uma combinação inválida não recria o hook nesse caminho, podendo deixá-lo inativo até confirmação válida. | SettingsPage.xaml.cs:111 |
| Perfis automáticos | EVENT_SYSTEM_FOREGROUND=3; só inicia AutoScale quando Disabled. Delay 0 ativa de imediato; positivo cria Timer com dueTime=0 e período 1000 ms até tick=0. Não há revalidação de foco no callback da contagem mostrado. | ForegroundWatcher.cs; MainWindow.xaml.cs:261 |
| Seleção por jogo | Percorre perfis em ordem, divide Path por ;, remove vazios e Trim. Wildcards ? e * viram . e .* após Regex.Escape, com âncoras ^$, IgnoreCase. Tenta caminho completo e basename; depois título principal do processo. Primeiro match vence. | MainWindow.xaml.cs:383,417,422 |
| Sem perfil correspondente | ActivateCore usa o perfil selecionado (ou LastSelectedProfile quando seleção <0). AutoScale do watcher requer perfil correspondente; não usa esse fallback para iniciar sozinho. | MainWindow.xaml.cs:261,452,522 |
| Botão manual | Inativo inicia contagem fixa de 5 segundos; outro clique cancela. No final verifica se a própria janela está em primeiro plano e abre aviso se estiver. Atalho pode ativar imediatamente em outra janela. | MainWindow.xaml.cs:230,839 |
| Novo perfil | Nome não vazio; duplica parte dos parâmetros do perfil 0. A checagem de duplicidade compara todo texto informado com cada segmento de perfil existente; não normaliza todos os segmentos novos. Editar perfil não faz a mesma checagem. | ProfileNewPage.xaml.cs:53; ProfileEditPage.xaml.cs:49 |
| Excluir perfil | Solicita confirmação, remove da lista, decrementa seleção e atualiza Items. Não chama SaveSettings diretamente nesse handler; a reapresentação pode disparar handlers que salvam, e o fechamento efetivo salva Settings. O handler já muda o status gerenciado para Disabled antes da confirmação. | MainWindow.xaml.cs:788,809,741 |
| Ordem e padrão | Carrega e mantém primeiro perfil, ordena demais por Title na inicialização. Primeiro recebe título localizado e não pode ser editado/removido pelos botões normais. | MainWindow.xaml.cs:109,809 |
| Persistência | XmlSerializer de Settings com lista GameProfiles; grava StreamWriter direto, sem replace atômico nesse corpo. Caminho em LocalApplicationData/ProductName/Settings.xml. ProductName vem dos recursos do assembly. | UI/Settings.cs; MainWindow.xaml.cs:34,66 |
| XML corrompido | DirectoryNotFound/FileNotFound criam padrões. InvalidOperation de desserialização mostra mensagem, copia para CorruptedSettingsBackup.xml sobrescrevendo e recria. Falha nessa cópia não recebe um catch próprio. | MainWindow.xaml.cs:77 |
| Autostart e bandeja | WPF grava HKCU Run usando nome LosslessScaling e caminho do assembly trocado para .exe com -StartMinimized. CloseToTray/MinimizeToTray controlam ocultação; saída explícita descarta hook/watcher e chama UnInit. | SettingsPage.xaml.cs:81; MainWindow.xaml.cs:741 |
| Configurações globais | Tema System/Light/Dark; enum de idioma tem 39 entradas incluindo System, Português e Português brasileiro. Mudança de idioma marca reinício requerido. Estados collapsed persistidos não provam presença de todos os controles no XAML atual. | UI/Settings.cs; SettingsPage.xaml.cs:179 |

## Todos os controles recuperados

A tabela registra limites declarados no XAML e handlers. Esses limites valem para entrada pela UI, não provam validação equivalente ao editar XML diretamente. As opções numéricas também estão completas no JSON privado `wpf-controls.json`, com tooltips e código dos handlers vinculados.


### UI/MainWindow.xaml

| Controle | Tipo / limites / opções | Destino e handlers | Evidência XAML |
|---|---|---|---|
| NotifyIcon_ExitItemClicked | MenuItem | Click → NotifyIcon_ExitItemClicked | L34 |
| ScaleButton | Button | Click → ScaleButton_Click | L57 |
| SteamButton | HyperlinkButton; Visibility=Collapsed | https://steamcommunity.com/app/993090/discussions/ | L58 |
| DiscordButton | HyperlinkButton; Visibility=Collapsed | https://discord.gg/5cCP6aACgT | L59 |
| LicensesButton | Button; Visibility=Collapsed | Click → LicensesButton_Click | L60 |
| ProfileList | ListView | SelectionChanged → ProfileList_SelectionChanged | L68 |
| AddButton_Click | Button | Click → AddButton_Click | L81 |
| EditButton | Button | Click → EditButton_Click | L82 |
| RemoveButton | Button | Click → RemoveButton_Click | L83 |
| BottomMenuList | ListView | SelectionChanged → BottomMenuList_SelectionChanged | L88 |

### UI.Pages/ManualPage.xaml

| Controle | Tipo / limites / opções | Destino e handlers | Evidência XAML |
|---|---|---|---|
| BackArrow_Click | Button | Click → BackArrow_Click | L5 |
| Steam Community page | HyperlinkButton | https://steamcommunity.com/app/993090/discussions/ | L16 |
| or Discord server. | HyperlinkButton | https://discord.gg/5cCP6aACgT | L17 |

### UI.Pages/ProfileEditPage.xaml

| Controle | Tipo / limites / opções | Destino e handlers | Evidência XAML |
|---|---|---|---|
| BackArrow_Click | Button | Click → BackArrow_Click | L15 |
| GameTitle | TextBox | — | L32 |
| GamePath | TextBox | — | L42 |
| Browse | Button | Click → Browse_Click | L43 |
| AutoScale | ToggleSwitch | Click → AutoScale_Toggled | L55 |
| Delay | NumberBox; Minimum=0; Maximum=100; Value=0; SmallChange=1 | — | L67 |
| Edit | Button | Click → Edit_Click → AutoScale,AutoScaleDelay,Path,Title | L73 |

### UI.Pages/ProfileNewPage.xaml

| Controle | Tipo / limites / opções | Destino e handlers | Evidência XAML |
|---|---|---|---|
| BackArrow_Click | Button | Click → BackArrow_Click | L15 |
| GameTitle | TextBox | — | L32 |
| GamePath | TextBox | — | L42 |
| Browse | Button | Click → Browse_Click | L43 |
| AutoScale | ToggleSwitch | Click → AutoScale_Toggled | L55 |
| Delay | NumberBox; Minimum=0; Maximum=100; Value=0; SmallChange=1 | — | L67 |
| Add | Button | Click → Add_Click → Path | L73 |

### UI.Pages/ProfilePage.xaml

| Controle | Tipo / limites / opções | Destino e handlers | Evidência XAML |
|---|---|---|---|
| UniformGrid | UniformGrid | SizeChanged → UniformGrid_SizeChanged | L16 |
| FrameGeneration | ComboBox; SelectedIndex=0; opções: 0=Off, 1=GDZ 3.1 , 2=GDZ 2.3 , 3=GDZ 1.1  | SelectionChanged → FrameGeneration_SelectionChanged → FrameGeneration | L30 |
| LSFG2Mode | ComboBox; SelectedIndex=0; opções: 0=X2, 1=X3, 2=X4 | SelectionChanged → LSFGMode2_SelectionChanged → LSFG2Mode | L47 |
| LSFG3Mode | ComboBox; SelectedIndex=0; opções: 0=Fixed, 1=Adaptive | SelectionChanged → LSFGMode3_SelectionChanged → LSFG3Mode1 | L65 |
| LSFG3Multiplier | NumberBox; Minimum=1; Maximum=20; SmallChange=1 | ValueChanged → LSFG3Multiplier_ValueChanged → LSFG3Multiplier; PreviewTextInput → LSFG3Multiplier_PreviewTextInput; KeyDown → NumberBox_KeyDown | L79 |
| LSFG3Target | NumberBox; Minimum=10; Maximum=1000; SmallChange=1 | ValueChanged → LSFG3Target_ValueChanged → LSFG3Target; PreviewTextInput → LSFG3Target_PreviewTextInput; KeyDown → NumberBox_KeyDown | L90 |
| LSFGFlowScale | Slider; Minimum=25; Maximum=100; Value=100; TickFrequency=5; IsSnapToTickEnabled=True | ValueChanged → LSFGFlowScale_ValueChanged → LSFGFlowScale | L102 |
| LSFGPerformance | ToggleSwitch | Click → LSFGPerformance_Toggled → LSFGSize | L114 |
| CaptureApi | ComboBox; SelectedIndex=0; opções: 0=DXGI, 1=WGC | SelectionChanged → CaptureApi_SelectionChanged → CaptureApi | L131 |
| QueueTarget | NumberBox; Minimum=0; Maximum=2; Value=1; SmallChange=1 | ValueChanged → QueueTarget_ValueChanged → QueueTarget; PreviewTextInput → QueueTarget_PreviewTextInput; KeyDown → NumberBox_KeyDown | L146 |
| ClipCursor | ToggleSwitch | Click → ClipCursor_Toggled → ClipCursor | L164 |
| AdjustCursorSpeed | ToggleSwitch | Click → AdjustCursorSpeed_Toggled → AdjustCursorSpeed | L177 |
| HideCursor | ToggleSwitch | Click → HideCursor_Toggled → HideCursor | L190 |
| ScaleCursor | ToggleSwitch | Click → ScaleCursor_Toggled → ScaleCursor | L203 |
| CropTop | NumberBox; Minimum=0; Maximum=9999 | PreviewTextInput → Crop_PreviewTextInput; ValueChanged → CropTop_ValueChanged → CropInputTop; KeyDown → NumberBox_KeyDown | L222 |
| CropLeft | NumberBox; Minimum=0; Maximum=9999 | PreviewTextInput → Crop_PreviewTextInput; ValueChanged → CropLeft_ValueChanged → CropInputLeft; KeyDown → NumberBox_KeyDown | L223 |
| CropRight | NumberBox; Minimum=0; Maximum=9999 | PreviewTextInput → Crop_PreviewTextInput; ValueChanged → CropRight_ValueChanged → CropInputRight; KeyDown → NumberBox_KeyDown | L245 |
| CropBottom | NumberBox; Minimum=0; Maximum=9999 | PreviewTextInput → Crop_PreviewTextInput; ValueChanged → CropBottom_ValueChanged → CropInputBottom; KeyDown → NumberBox_KeyDown | L246 |
| ScalingType | ComboBox; ; opções: 0=Off, 1=LS1, 2=FSR, 3=NIS, 4=SGSR, 5=BCAS, 6=Anime4K, 7=xBR, 8=Sharp Bilinear, 9=Integer, 10=Nearest Neighbor | SelectionChanged → ScalingType_SelectionChanged → ScalingType | L264 |
| Anime4kType | ComboBox; ; opções: 0=Small, 1=Medium, 2=Large, 3=Very Large, 4=Ultra Large | SelectionChanged → Anime4kType_SelectionChanged → Anime4kType | L288 |
| Sharpness | Slider; TickFrequency=1; IsSnapToTickEnabled=True | ValueChanged → Sharpness_ValueChanged → LS1Sharpness,Sharpness | L307 |
| FsrOptimized | ToggleSwitch | Click → FsrOptimized_Toggled → FSRType | L321 |
| LS1Performance | ToggleSwitch | Click → LS1Performance_Toggled → LS1Type | L336 |
| VRS | ToggleSwitch | Click → VRS_Toggled → VRS | L351 |
| ScalingMode | ComboBox; ; opções: 0=Auto, 1=Custom | SelectionChanged → ScalingMode_SelectionChanged → ScalingMode | L365 |
| ScalingFitMode | ComboBox; ; opções: 0=Aspect ratio, 1=Fullscreen | SelectionChanged → ScalingFitMode_SelectionChanged → ScalingFitMode | L379 |
| ScaleFactor | NumberBox; Minimum=1; Maximum=10; SmallChange=0.1 | ValueChanged → ScaleFactor_ValueChanged → ScaleFactor; PreviewTextInput → ScaleFactor_PreviewTextInput; KeyDown → NumberBox_KeyDown | L395 |
| ResizeBeforeScaling | ToggleSwitch | Click → ResizeBeforeScaling_Toggled → ResizeBeforeScaling | L407 |
| SyncMode | ComboBox; ; opções: 0=Off (Allow tearing), 1=Default, 2=Vsync, 3=Vsync ½, 4=Vsync ⅓ | SelectionChanged → SyncMode_SelectionChanged → SyncMode | L425 |
| MaxFrameLatency | NumberBox; Minimum=1; Maximum=15; SmallChange=1 | ValueChanged → MaxFrameLatency_ValueChanged → MaxFrameLatency; PreviewTextInput → MaxFrameLatency_PreviewTextInput; KeyDown → NumberBox_KeyDown | L442 |
| HdrSupport | ToggleSwitch | Click → HdrSupport_Toggled → HdrSupport | L454 |
| GsyncSupport | ToggleSwitch | Click → GsyncSupport_Toggled → GsyncSupport | L467 |
| DrawFps | ToggleSwitch | Click → DrawFps_Toggled → DrawFps | L480 |
| PreferredGpu | ComboBox; SelectedIndex=0 | SelectionChanged → PreferredGpu_SelectionChanged → PreferredGpuId | L497 |
| OutputDisplay | ComboBox; SelectedIndex=0 | SelectionChanged → OutputDisplay_SelectionChanged → OutputDisplayId | L508 |
| MultiDisplayMode | ToggleSwitch | Click → MultiDisplayMode_Toggled → MultiDisplayMode | L525 |

### UI.Pages/SettingsPage.xaml

| Controle | Tipo / limites / opções | Destino e handlers | Evidência XAML |
|---|---|---|---|
| BackArrow_Click | Button | Click → BackArrow_Click | L15 |
| UniformGrid | UniformGrid | SizeChanged → UniformGrid_SizeChanged | L20 |
| ScaleHotkey | TextBox | KeyDown → ScaleHotkey_KeyDown; KeyUp → ScaleHotkey_KeyUp → Hotkey,HotkeyModifierKeys | L25 |
| LanguageUI | ComboBox; ; opções: 0=System, 1=اَلْعَرَبِيَّةُ, 2=Български, 3=简体中文, 4=繁體中文, 5=Hrvatski, 6=Čeština, 7=Dansk, 8=Nederlands, 9=English, 10=Suomi, 11=Français, 12=ქართული ენა, 13=Deutsch, 14=Ελληνικά, 15=עברית, 16=Magyar, 17=Bahasa Indonesia, 18=Italiano, 19=日本語, 20=한국어, 21=Lietuvių kalba, 22=Македонски, 23=Norsk, 24=فارسی, 25=Polski, 26=Português, 27=Português (Brasil), 28=Română, 29=Srpski, 30=Slovenčina, 31=Русский, 32=Español, 33=Svenska, 34=ภาษาไทย, 35=Toki Pona, 36=Türkçe, 37=Українська, 38=Tiếng Việt | SelectionChanged → LanguageUI_SelectionChanged → Language | L41 |
| Theme | ComboBox; ; opções: 0=System, 1=Light, 2=Dark | SelectionChanged → Theme_SelectionChanged → Theme | L94 |
| StartAsAdmin | ToggleSwitch | Click → StartAsAdmin_Toggled → StartAsAdmin | L117 |
| StartAtWindowsStartup | ToggleSwitch | Click → StartAtWindowsStartup_Toggled → StartAtWindowsStartup | L130 |
| MinimizeToTray | ToggleSwitch | Click → MinimizeToTray_Toggled → MinimizeToTray | L143 |
| CloseToTray | ToggleSwitch | Click → CloseToTray_Toggled → CloseToTray | L156 |

## Critérios para uma implementação futura

1. Validar limites da UI no serviço também, sem depender do XML ou do NumberBox. Manter valores, unidades e enums explícitos, com teste de ida e volta de persistência.
2. Tratar seleção de perfil, atraso, cancelamento e estados Disabled/Countdown/Enabled/TempDisabled como máquina de estados; cobrir troca de foco durante delay e fechamento durante timer. O comportamento observado do alvo não deve ser assumido como livre de corridas.
3. Confirmar que o motor escolhido suporta cada opção. Serializar DrawFps ou LSFG3 no perfil não implementa overlay nem interpolação. O core gráfico continua uma dependência separada.
4. Separar este contrato do HTML legado e da allowlist scalingWrite. Eles têm capacidades e intervalos diferentes e não devem servir como única especificação da janela real.

## Reproduzir a leitura

Ferramenta oficial: [ILSpy / ILSpyCmd](https://github.com/icsharpcode/ILSpy/blob/master/ICSharpCode.ILSpyCmd/README.md). Pacote fixado: [ilspycmd 11.2.0.10050](https://www.nuget.org/packages/ilspycmd/11.2.0.10050). O decompilador lê bytes/metadados e converte BAML; não foi usado para executar funções do alvo.

Após a decompilação privada, `python scripts/catalog-gdz-wpf.py` regenera esta tabela e o inventário. Linhas referem-se à view decompilada desta execução, cujos hashes constam no JSON. Consulte [533 métodos e 33 parâmetros do core](GDZ-SCALING-FUNCOES.md) e [extração nativa aprofundada](GDZ-MOTOR-NATIVO-APROFUNDADO.md).
