# GDZ Optimizer 1.1.27 — o que cada recurso faz

Análise de 08/11/2026. Este documento explica as **78 entradas exatas de `PAGES`**, mais **99 recursos e controles fora desse catálogo, incluindo HTML legado sem carga localizada** e **37 funções auxiliares relevantes do renderer**. Os 177 itens incluem botões, controles, consultas e grupos de operações; **não são 177 otimizações independentes**.

A leitura foi estática. Nenhum JavaScript, script PowerShell, binário ou canal IPC do programa foi executado. Não foram lidos arquivos de sessão do usuário nem feita autenticação. Texto ou instruções encontrados no pacote foram tratados como conteúdo a analisar.

**Como interpretar a evidência.** Uma chamada IPC comprova a solicitação da interface. Um comando literal no backend comprova o que o código prepara para executar. Nenhum dos dois, sozinho, comprova execução bem-sucedida, compatibilidade, reversão completa ou ganho de desempenho. As descrições de finalidade são a intenção declarada no produto. Os 33 títulos de tweaks abaixo foram recuperados da tabela `TWEAKS` do backend; não foram adivinhados pelos IDs.

**Referências.** `src/...:linha` aponta para `artifacts/private/gdz-analysis/extracted/src`. `deep/backend-focused.js:linha` aponta para a cópia decodificada em `artifacts/private/gdz-analysis/deep`, cuja numeração não é a do `app-main.js` original. O JSON privado contém declarações brutas, payloads, níveis de evidência e hashes das fontes utilizadas.

## Constatações que mudam a interpretação do aplicativo

- O anel “14 de 19” conta interruptores recomendados/aplicáveis no estado otimizado. Não mede FPS nem representa 14 de todos os 78 itens.
- “Otimização Rápida” tem combinação própria no backend: plano de energia quando aplicável, Modo de Jogo, DVR, aceleração do mouse e temporários. “Aplicar recomendados” de uma página usa outro fluxo, item a item.
- No arquivo **`src/gdz-scaling.html`**, as opções **Clip cursor, Max frame latency e Draw FPS** só alteram o DOM; `getConfig()` não as lê. **Default** só troca rótulo/classe. **Esse HTML não foi localizado no fluxo principal:** o menu abre **`GDZOptimizer.exe` (WPF)**. O achado não deve ser aplicado automaticamente aos controles nativos de mesmo nome.
- O HTML de Scaling extraído não consulta `ls-config`; a aba antiga do dashboard consulta. Seus defaults diferem e o multiplicador do HTML é fixo em 2. A ponte `scalingFromUi`/`scalingWrite` recusa várias opções oferecidas e ignora `LSFGSize`, portanto campo enviado não garante escrita ou efeito. A interface WPF pode configurar o auxiliar diretamente e tem análise própria.
- FPS do medidor é recebido do backend; o renderer não inventa um benchmark. O fallback com valores aleatórios só é definido se `window.electron` estiver ausente. Isso não prova que a captura ou o app instalado usem números simulados.
- Frame Generation pode aumentar o número de quadros apresentados sem acelerar a simulação nem reduzir atraso de entrada; a própria UI do OptiScaler avisa disso.
- Ajustes de aparência, privacidade e conveniência do Windows não devem ser contados como ganho de FPS. A página declara que quase nada ali muda FPS.
- Desinstalações, remoção UWP, temporários e Lixeira não têm a mesma reversibilidade dos interruptores com backup. “Reverter Tudo” não é recuperação universal.

## Contagem verificável do catálogo principal

| Página | Entradas declaradas |
|---|---:|
| Desempenho (`desempenho`) | 29 |
| Mouse e teclado (`inputlag`) | 4 |
| Memória (`memoria`) | 1 |
| Limpeza (`limpeza`) | 2 |
| Internet e live (`live`) | 9 |
| Windows (`tweaks`) | 24 |
| Verificar hardware (`hardware`) | 9 |
| **Total PAGES** | **78** |

Tipos: **33 tweaks**, **18 ações reversíveis**, **5 ações pontuais**, **5 verificações**, **5 ferramentas**, **4 reparos**, **7 dicas** e **1 seletor DNS**. São 51 interruptores declarados antes dos filtros de capacidade/disponibilidade.

Cada linha abaixo mantém a chave exata usada pelo catálogo. Um interruptor “ligado” significa **otimização aplicada**; em “Game Bar desligada”, por exemplo, ligado no GDZ significa a desativação aplicada, não Game Bar ligado.

## Catálogo principal: as 78 entradas

### Desempenho

#### 01. Plano de energia Alto Desempenho

Chave: `act:Plano de Alta Performance` · tipo `A` · grupo: Energia e processador.

**Finalidade declarada:** O processador não baixa a velocidade para economizar energia, então não perde tempo acelerando quando o jogo pede. Menos travadinhas e FPS mínimo mais alto.

**O que o código prepara:** powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c; se falha tenta /duplicatescheme com esse GUID, depois /setactive novamente. Emite GDZ_ERRO se rejeitado.

**Entrada:** `run-action` `{"action":"Plano de Alta Performance"}`; `revert-action` `{"action":"Plano de Alta Performance"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão. Nota condicional na UI: em notebook gasta mais bateria e orienta uso na tomada; em desktop aumenta consumo.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:46`, `deep/backend-focused.js:1863`.

#### 02. Plano de energia Desempenho Máximo

Chave: `act:Modo Máximo Desempenho` · tipo `A` · grupo: Energia e processador.

**Finalidade declarada:** Versão mais forte do Alto Desempenho: também tira a economia de energia do disco, do USB e da placa de rede. Ligado, ele já conta como Alto Desempenho: não precisa dos dois.

**O que o código prepara:** Mesmo procedimento com o GUID e9a42b02-d5df-448d-aa00-03f14749eb61 (Ultimate Performance).

**Entrada:** `run-action` `{"action":"Modo Máximo Desempenho"}`; `revert-action` `{"action":"Modo Máximo Desempenho"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Limites e efeitos colaterais:** Mais consumo e temperatura. Indicado só para PC de mesa.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:50`, `deep/backend-focused.js:1864`.

#### 03. Modo certo para Ryzen X3D

Chave: `act:Modo X3D Correto` · tipo `A` · grupo: Energia e processador.

**Finalidade declarada:** Para Ryzen 7900X3D, 7950X3D, 9900X3D e 9950X3D: plano Equilibrado, Modo de Jogo e Game Bar ligados. É assim que o driver da AMD coloca o jogo nos núcleos com cache extra.

**O que o código prepara:** Ativa plano Equilibrado 381b4222-f694-41f0-9685-ff5bb260df2e; liga AllowAutoGameMode/AutoGameModeEnabled/UseNexusForGameBarEnabled; remove política AllowGameDVR. Se Game Bar falta, abre sua página na Store; se amd3dvcacheSvc falta, apenas avisa instalar chipset AMD.

**Entrada:** `run-action` `{"action":"Modo X3D Correto"}`; `revert-action` `{"action":"Modo X3D Correto"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Aplicabilidade/reinício:** capacidade `x3dDual`.

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:53`, `deep/backend-focused.js:1898`.

#### 04. Modo de Jogo do Windows

Chave: `tweak:game-mode` · tipo `T` · grupo: Jogo sem interrupção.

**Finalidade declarada:** Quando um jogo abre, o Windows segura atualizações e tarefas de fundo e dá prioridade de processador e placa de vídeo para ele.

**O que o código prepara:** Grava AllowAutoGameMode=1 e AutoGameModeEnabled=1 em HKCU\SOFTWARE\Microsoft\GameBar.

**Entrada:** `tweak-set` `{"id":"game-mode","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [[K["GAMEBAR"], "AllowAutoGameMode", "DWord", 1, 0], [K["GAMEBAR"], "AutoGameModeEnabled", "DWord", 1, 0]]
probe: "((GdzReg '" + K["GAMEBAR"] + "' 'AutoGameModeEnabled') -ne '0')"
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:57`, `deep/backend-focused.js:3264`.

#### 05. Sem gravação em segundo plano

Chave: `act:DVR Desativado` · tipo `A` · grupo: Jogo sem interrupção.

**Finalidade declarada:** Desliga a gravação automática do Xbox Game Bar, que fica filmando os últimos minutos de jogo e ocupa a placa de vídeo o tempo todo. Também libera o encoder para o OBS.

**O que o código prepara:** DVR_OFF: AppCaptureEnabled=0 em GameDVR e GameDVR_Enabled=0 em GameConfigStore (HKCU).

**Entrada:** `run-action` `{"action":"DVR Desativado"}`; `revert-action` `{"action":"DVR Desativado"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:58`, `deep/backend-focused.js:1889`.

#### 06. Game Bar (Win+G) desligada

Chave: `act:Game Bar Desativado` · tipo `A` · grupo: Jogo sem interrupção.

**Finalidade declarada:** A tela do Xbox Game Bar para de abrir por cima do jogo com o Win+G e de ficar carregada em segundo plano. A gravação automática também sai. O app continua instalado: desligar aqui devolve tudo.

**O que o código prepara:** Executa o bloco GAMEBAR_OFF: políticas/valores do Game Bar e DVR, parada de processos; difere da remoção do pacote Appx.

**Entrada:** `run-action` `{"action":"Game Bar Desativado"}`; `revert-action` `{"action":"Game Bar Desativado"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:60`, `deep/backend-focused.js:1867`.

#### 07. Sobreposição do Steam (Shift+Tab) desligada

Chave: `tweak:steam-overlay` · tipo `T` · grupo: Jogo sem interrupção.

**Finalidade declarada:** Desliga a tela que o Steam abre por cima do jogo com o Shift+Tab. Ela injeta código em todo jogo do Steam e é causa conhecida de queda de FPS, travadinhas e conflito com outros programas de captura.

**O que o código prepara:** Usa helper de leitura/escrita VDF para a opção da sobreposição: valor 0 ao aplicar e 1 ao desfazer. Recusa se há jogo Steam aberto; pede shutdown da Steam, espera até 40×500 ms, altera VDF e reabre se estava aberta.

**Entrada:** `tweak-set` `{"id":"steam-overlay","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
probe: steamOverlayProbe
on: _0x511a53 => steamOverlayScript(_0x511a53, true)
off: _0x3f09e1 => steamOverlayScript(_0x3f09e1, false)
block: _0x10d9f3 => _0x10d9f3["steamPath"] ? null : "O Steam não está instalado neste PC."
```

**Limites e efeitos colaterais:** O Steam fecha e abre de novo para a alteração valer. Sem a sobreposição você perde o chat, o navegador e as conquistas por cima do jogo (e o Steam Input de alguns controles).

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:62`, `deep/backend-focused.js:3337`.

#### 08. Dica: a sobreposição da NVIDIA se desliga na NVIDIA App

Chave: `tip:Dica: a sobreposição da NVIDIA se desliga na NVIDIA App` · tipo `TIP` · grupo: Jogo sem interrupção.

**Finalidade declarada:** A NVIDIA App guarda essa opção por conta própria e reescreve qualquer alteração feita por fora, então nenhum otimizador consegue desligá-la de verdade. Na NVIDIA App: Configurações → Sobreposição na tela → desligue. Ela fica acoplada ao jogo o tempo todo e leva junto a gravação do ShadowPlay.

**O que o código prepara:** Apresenta a recomendação escrita pelo produto; desempenho e compatibilidade não foram confirmados nesta análise.

**Entrada:** nenhum IPC; conteúdo informativo/local.  
**Retorno esperado:** Apenas texto e filtragem por capacidade; não envia ajuste.  
**Reversão/leitura:** Orientação; mudanças seriam manuais fora do GDZ.

**Aplicabilidade/reinício:** capacidade `nvidia`.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/otimizacoes.js:63`.

#### 09. Dica: a sobreposição do Discord se desliga dentro do Discord

Chave: `tip:Dica: a sobreposição do Discord se desliga dentro do Discord` · tipo `TIP` · grupo: Jogo sem interrupção.

**Finalidade declarada:** Essa opção fica guardada na sua conta do Discord, não no PC, então nenhum otimizador consegue desligá-la de fora (quem diz que desliga, não desliga). No Discord: Configurações (engrenagem) → Sobreposição de jogo → desligue "Ativar sobreposição no jogo". Ela injeta no jogo igual às outras e costuma custar FPS.

**O que o código prepara:** Apresenta a recomendação escrita pelo produto; desempenho e compatibilidade não foram confirmados nesta análise.

**Entrada:** nenhum IPC; conteúdo informativo/local.  
**Retorno esperado:** Apenas texto e filtragem por capacidade; não envia ajuste.  
**Reversão/leitura:** Orientação; mudanças seriam manuais fora do GDZ.

**Aplicabilidade/reinício:** capacidade `discord`.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/otimizacoes.js:65`.

#### 10. Windows não reinicia sozinho

Chave: `act:Sem Reinício Durante o Jogo` · tipo `A` · grupo: Jogo sem interrupção.

**Finalidade declarada:** As atualizações de segurança continuam chegando, mas o PC só reinicia quando você mandar. Nada de reinício no meio da partida ou da live.

**O que o código prepara:** Grava NoAutoRebootWithLoggedOnUsers=1 em HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU. A existência de uma política não prova impedir todos os tipos de reinício.

**Entrada:** `run-action` `{"action":"Sem Reinício Durante o Jogo"}`; `revert-action` `{"action":"Sem Reinício Durante o Jogo"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:67`, `deep/backend-focused.js:1894`.

#### 11. Sem notificações do Windows

Chave: `act:Desativar Notificações` · tipo `A` · grupo: Jogo sem interrupção.

**Finalidade declarada:** Os avisos que sobem no canto da tela (apps, e-mail, Windows) param de aparecer.

**O que o código prepara:** Grava ToastEnabled=0 em HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\PushNotifications.

**Entrada:** `run-action` `{"action":"Desativar Notificações"}`; `revert-action` `{"action":"Desativar Notificações"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Limites e efeitos colaterais:** Vale o tempo todo, não só no jogo: você deixa de ver os avisos também fora dele.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:69`, `deep/backend-focused.js:1871`.

#### 12. Sem apps da Loja rodando escondidos

Chave: `tweak:bg-apps` · tipo `T` · grupo: Jogo sem interrupção.

**Finalidade declarada:** Apps instalados pela Microsoft Store (Clima, Fotos, Xbox, Your Phone...) param de rodar em segundo plano quando estão fechados.

**O que o código prepara:** Grava GlobalUserDisabled=1 em BackgroundAccessApplications e BackgroundAppGlobalToggle=0 em Search (HKCU).

**Entrada:** `tweak-set` `{"id":"bg-apps","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [["HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\BackgroundAccessApplications", "GlobalUserDisabled", "DWord", 1, 0], [K_SEARCH, "BackgroundAppGlobalToggle", "DWord", 0, 1]]
```

**Limites e efeitos colaterais:** Apps da Loja, como o WhatsApp, deixam de mostrar notificações com a janela fechada.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:72`, `deep/backend-focused.js:3290`.

#### 13. Edge fechado de verdade

Chave: `tweak:edge-bg` · tipo `T` · grupo: Jogo sem interrupção.

**Finalidade declarada:** Impede o Edge de abrir escondido junto com o Windows e de continuar rodando depois que você fecha a janela. Devolve memória e deixa a inicialização mais leve.

**O que o código prepara:** Grava StartupBoostEnabled=0 e BackgroundModeEnabled=0 nas políticas do Microsoft Edge no HKLM.

**Entrada:** `tweak-set` `{"id":"edge-bg","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [["HKLM:\\SOFTWARE\\Policies\\Microsoft\\Edge", "StartupBoostEnabled", 'DWord', 0, null], ["HKLM:\\SOFTWARE\\Policies\\Microsoft\\Edge", "BackgroundModeEnabled", "DWord", 0, null]]
```

**Limites e efeitos colaterais:** Nas configurações do Edge vai aparecer "Gerenciado pela sua organização". É normal: é assim que o Windows marca essa regra.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:73`, `deep/backend-focused.js:3281`.

#### 14. Agendamento de GPU por hardware (HAGS)

Chave: `act:HAGS Ativo` · tipo `A` · grupo: Placa de vídeo e tela.

**Finalidade declarada:** A própria placa de vídeo organiza a fila de quadros, tirando trabalho do processador. Obrigatório para o DLSS Frame Generation.

**O que o código prepara:** Grava HwSchMode=2 em HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers; reinício indicado na UI.

**Entrada:** `run-action` `{"action":"HAGS Ativo"}`; `revert-action` `{"action":"HAGS Ativo"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Aplicabilidade/reinício:** marcador `reboot`.

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:76`, `deep/backend-focused.js:1870`.

#### 15. Jogos em janela e sem bordas otimizados

Chave: `act:Jogos em Janela Otimizados` · tipo `A` · grupo: Placa de vídeo e tela.

**Finalidade declarada:** Jogos em janela ou em "tela cheia sem bordas" (o jeito que o FiveM costuma rodar) passam a mostrar a imagem do modo moderno: menos atraso, com G-Sync/FreeSync e Auto HDR funcionando.

**O que o código prepara:** No valor String DirectXUserGlobalSettings de HKCU\Software\Microsoft\DirectX\UserGpuPreferences, remove SwapEffectUpgradeEnable=\d; existente e acrescenta SwapEffectUpgradeEnable=1;.

**Entrada:** `run-action` `{"action":"Jogos em Janela Otimizados"}`; `revert-action` `{"action":"Jogos em Janela Otimizados"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:78`, `deep/backend-focused.js:1892`.

#### 16. DLSS no modelo recomendado pela NVIDIA

Chave: `tweak:dlss-rec` · tipo `T` · grupo: Placa de vídeo e tela.

**Finalidade declarada:** Os jogos com DLSS passam a usar a versão mais nova que o driver tem, com o modelo que a NVIDIA recomenda para cada modo: o DLSS 4.5 nos modos Desempenho e Ultra Desempenho e o DLSS 4 nos outros. É o botão "Recomendado" de Substituição do DLSS na NVIDIA App. A imagem dos modos mais leves melhora, então dá para trocar Qualidade por Desempenho dentro do jogo e ganhar FPS.

**O que o código prepara:** Usa NVAPI DRS: IDs 283385345 (DLSS_SR_OVERRIDE)=1 e 283385331 (DLSS_SR_PRESET)=16777215 (recomendado). Não modifica DLLs do jogo neste ajuste.

**Entrada:** `tweak-set` `{"id":"dlss-rec","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


**Aplicabilidade/reinício:** capacidade `rtx`.

Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
drs: _0x38969b => _0x38969b["rtx"] && _0x38969b["nvapi"] ? [[DLSS_SR_OVERRIDE, 1], [DLSS_SR_PRESET, DLSS_PRESET_RECOMMENDED]] : []
block: _0x4960a6 => !_0x4960a6['rtx'] ? "Só para placas de vídeo NVIDIA RTX, que são as que têm DLSS." : !_0x4960a6["nvapi"] ? "O driver da NVIDIA não está instalado neste PC." : null
```

**Limites e efeitos colaterais:** Nota condicional do backend: _0x365274 => _0x365274['rtxOld'] ? "Nas RTX 20 e 30 o DLSS 4.5 é pesado: no modo Desempenho o FPS pode cair de 15 a 25% em relação ao DLSS 4. Meça antes de deixar ligado. O FiveM clássico não tem DLSS, então lá não muda nada." : "Sozinho este ajuste não dá FPS (o DLSS 4.5 custa uns 3 a 5% nas RTX 40 e 50): o ganho vem de baixar o modo do DLSS no jogo. Vale para o jogo aberto depois de ligar. O FiveM clássico não tem DLSS, então lá não muda nada."

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:80`, `deep/backend-focused.js:3328`.

#### 17. Placa de vídeo com interrupção MSI e prioridade alta

Chave: `tweak:gpu-msi` · tipo `T` · grupo: Placa de vídeo e tela.

**Finalidade declarada:** A placa de vídeo passa a avisar o processador pelo modo moderno (MSI), em vez de dividir uma linha de interrupção antiga com outros dispositivos, e o Windows atende esse aviso na frente dos demais. É o que o MSI Utility faz, sem instalar programa nenhum. Costuma tirar travadinhas e encurtar o tempo entre o clique e o tiro.

**O que o código prepara:** Para a GPU detectada em caps.gpuIrq, grava MSISupported=1 em MessageSignaledInterruptProperties e DevicePriority=3 em Affinity Policy. Caminho depende da GPU; exige reinício.

**Entrada:** `tweak-set` `{"id":"gpu-msi","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


**Aplicabilidade/reinício:** marcador `reboot`.

Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: gpuMsiReg
block: _0x58585c => _0x58585c['gpuIrq'] ? null : "Não encontrei uma placa de vídeo dedicada NVIDIA ou AMD neste PC."
```

**Limites e efeitos colaterais:** Vale só depois de reiniciar o PC. Se a imagem ficar estranha ou o PC travar ao iniciar, desligue aqui de novo (ou use um ponto de restauração).

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:81`, `deep/backend-focused.js:3318`.

#### 18. Correção de tela piscando (MPO)

Chave: `tweak:mpo` · tipo `T` · grupo: Placa de vídeo e tela.

**Finalidade declarada:** Desliga o Multiplane Overlay, causa conhecida de tela piscando, faixas pretas e travadinhas com G-Sync/FreeSync ou com dois monitores.

**O que o código prepara:** Grava OverlayTestMode=5 no Dwm e DisableOverlays=1 em GraphicsDrivers (HKLM); exige reinício segundo o contrato.

**Entrada:** `tweak-set` `{"id":"mpo","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


**Aplicabilidade/reinício:** marcador `reboot`.

Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [[K["DWM"], "OverlayTestMode", 'DWord', 5, null], [K["GFX"], "DisableOverlays", 'DWord', 1, null]]
```

**Limites e efeitos colaterais:** Só ative se você tem esse problema. Sem ele, não muda nada.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:82`, `deep/backend-focused.js:3309`.

#### 19. Sem efeitos visuais do Windows

Chave: `act:Efeitos Visuais Off` · tipo `A` · grupo: Para PC fraco.

**Finalidade declarada:** Tira sombras e animações de janelas e menus. Ajuda em PC com vídeo integrado ou pouca memória.

**O que o código prepara:** Grava VisualFXSetting=2 em Explorer\VisualEffects e UserPreferencesMask=bytes [90,12,03,80,10,00,00,00] hex em HKCU\Control Panel\Desktop.

**Entrada:** `run-action` `{"action":"Efeitos Visuais Off"}`; `revert-action` `{"action":"Efeitos Visuais Off"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Limites e efeitos colaterais:** O Windows fica com a cara mais simples.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:86`, `deep/backend-focused.js:1872`.

#### 20. Sem transparência nas janelas

Chave: `tweak:transparency` · tipo `T` · grupo: Para PC fraco.

**Finalidade declarada:** Desliga o efeito de vidro fosco da barra de tarefas, do menu Iniciar e das janelas. A placa de vídeo para de desfocar o fundo o tempo todo, o que alivia PC com vídeo integrado.

**O que o código prepara:** Grava EnableTransparency=0 em Themes\Personalize (HKCU) e transmite WM_SETTINGCHANGE/ImmersiveColorSet por SendMessageTimeout para atualizar aparência.

**Entrada:** `tweak-set` `{"id":"transparency","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [["HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "EnableTransparency", "DWord", 0, 1]]
on: THEME_BROADCAST
off: THEME_BROADCAST
```

**Limites e efeitos colaterais:** Com placa de vídeo dedicada a diferença no FPS é praticamente zero. A barra de tarefas e os menus ficam com cor sólida.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:89`, `deep/backend-focused.js:3527`.

#### 21. SysMain (Superfetch) desligado

Chave: `act:SysMain Desativado` · tipo `A` · grupo: Para PC fraco.

**Finalidade declarada:** O Windows para de pré-carregar na memória os programas que você mais usa. Ajuda em PC com HD (não SSD) e 8 GB de RAM ou menos.

**O que o código prepara:** Stop-Service SysMain e Set-Service SysMain -StartupType Disabled.

**Entrada:** `run-action` `{"action":"SysMain Desativado"}`; `revert-action` `{"action":"SysMain Desativado"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Limites e efeitos colaterais:** Com SSD o ganho é quase zero, e os programas podem abrir um pouco mais devagar.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:90`, `deep/backend-focused.js:1865`.

#### 22. Indexação de arquivos desligada

Chave: `act:Indexação Off` · tipo `A` · grupo: Para PC fraco.

**Finalidade declarada:** O Windows para de ler o disco o tempo todo para montar o índice da pesquisa. Alivia HDs lentos.

**O que o código prepara:** Stop-Service WSearch e Set-Service WSearch -StartupType Disabled.

**Entrada:** `run-action` `{"action":"Indexação Off"}`; `revert-action` `{"action":"Indexação Off"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Limites e efeitos colaterais:** A pesquisa de arquivos do Windows fica bem mais lenta.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:93`, `deep/backend-focused.js:1874`.

#### 23. Serviços pouco usados desligados

Chave: `act:Serviços Desativados` · tipo `A` · grupo: Para PC fraco.

**Finalidade declarada:** Desliga impressão, fax, registro remoto, mapas offline e leitor de digital.

**O que o código prepara:** Para e define Disabled para Spooler, Fax, RemoteRegistry, MapsBroker e WbioSrvc: impressão, fax, registro remoto, mapas e biometria.

**Entrada:** `run-action` `{"action":"Serviços Desativados"}`; `revert-action` `{"action":"Serviços Desativados"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Limites e efeitos colaterais:** Não use se você tem impressora ou entra no Windows pela digital.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:96`, `deep/backend-focused.js:1873`.

#### 24. Serviços do Xbox desligados

Chave: `act:Serviços Xbox Desativados` · tipo `A` · grupo: Para PC fraco.

**Finalidade declarada:** Desliga o login e a sincronização de saves do Xbox. Controles do Xbox continuam funcionando.

**O que o código prepara:** Para e define Disabled para XblAuthManager, XblGameSave e XboxNetApiSvc.

**Entrada:** `run-action` `{"action":"Serviços Xbox Desativados"}`; `revert-action` `{"action":"Serviços Xbox Desativados"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Limites e efeitos colaterais:** Jogos do Game Pass, Forza e Minecraft Bedrock deixam de abrir.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:99`, `deep/backend-focused.js:1869`.

#### 25. Xbox Game Bar desinstalado

Chave: `act:Game Bar Removido` · tipo `A` · grupo: Para PC fraco.

**Finalidade declarada:** Remove o Game Bar (Win+G) e o aviso "ms-gamingoverlay" que aparece sem ele. Desligar aqui reinstala.

**O que o código prepara:** Desliga DVR/Game Bar, encerra GameBar/GameBarFTServer/GameBarPresenceWriter e remove Microsoft.XboxGamingOverlay com Remove-AppxPackage. Sem o pacote, cria handlers ms-gamebar/ms-gamebarservices/ms-gamingoverlay apontando para systray.exe. A reversão extra usa GAMEBAR_REINSTALL.

**Entrada:** `run-action` `{"action":"Game Bar Removido"}`; `revert-action` `{"action":"Game Bar Removido"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:102`, `deep/backend-focused.js:1868`.

#### 26. Tela em 720p (emergência)

Chave: `act:720p Boost Ativo` · tipo `A` · grupo: Para PC fraco.

**Finalidade declarada:** Baixa a resolução do Windows para 1280×720 para a placa de vídeo aguentar. Volta ao desligar ou ao reiniciar o PC.

**O que o código prepara:** Lê PrimaryScreen.Bounds e grava PrevResW/PrevResH em HKCU\Software\GDZ; DEVMODE recebe 1280×720 e dmFields=0x180000; user32 ChangeDisplaySettings(ref m,0), sem CDS_UPDATEREGISTRY neste script.

**Entrada:** `run-action` `{"action":"720p Boost Ativo"}`; `revert-action` `{"action":"720p Boost Ativo"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Limites e efeitos colaterais:** Tudo fica maior e menos nítido. Prefira baixar a resolução dentro do jogo ou usar o GDZ Scaling. O script descarta o retorno inteiro de ChangeDisplaySettings; não há checagem explícita desse código nesta ação literal.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:104`, `deep/backend-focused.js:1890`.

#### 27. Remover apps que vêm com o Windows

Chave: `once:UWP Removidos` · tipo `O` · grupo: Para PC fraco.

**Finalidade declarada:** Desinstala Cortana, Clima, Mapas, Filmes e TV, Your Phone, apps do Xbox e outros apps de fábrica.

**O que o código prepara:** Remove por Get-AppxPackage/Remove-AppxPackage uma lista fixa: Xbox.TCUI, XboxApp, XboxGamingOverlay, Cortana, BingWeather, GetHelp, People, WindowsMaps, ZuneMusic e ZuneVideo; em X3D de dois CCDs preserva TCUI e XboxGamingOverlay. A descrição menciona outros apps, mas a lista é que delimita a ação.

**Entrada:** `run-action` `{"action":"UWP Removidos"}`  
**Retorno esperado:** {success,message?,error?}; mostra retorno e registra histórico recente.  
**Reversão/leitura:** Ação pontual sem interruptor de desfazer neste item; duração/reinstalação conforme finalidade.


**Limites e efeitos colaterais:** Não tem desfazer: para ter algum de volta, reinstale pela Microsoft Store. Sem desfazer no GDZ; reinstalação pela Microsoft Store. A lista real é mais restrita que a descrição comercial.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:107`, `deep/backend-focused.js:1875`.

#### 28. Chrome e Discord em prioridade baixa

Chave: `once:Chrome/Discord Limitados` · tipo `O` · grupo: Antes de jogar.

**Finalidade declarada:** Quando o processador ficar disputado, o jogo tem a vez. Os dois continuam funcionando normalmente.

**O que o código prepara:** Define PriorityClass=BelowNormal para processos chrome/Discord em execução; não fixa porcentagem de CPU/RAM.

**Entrada:** `run-action` `{"action":"Chrome/Discord Limitados"}`  
**Retorno esperado:** {success,message?,error?}; mostra retorno e registra histórico recente.  
**Reversão/leitura:** Ação pontual sem interruptor de desfazer neste item; duração/reinstalação conforme finalidade.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:113`, `deep/backend-focused.js:1887`.

#### 29. Fechar OneDrive, Dropbox e Google Drive

Chave: `once:Apps de Sincronização Fechados` · tipo `O` · grupo: Antes de jogar.

**Finalidade declarada:** Fecha os apps de nuvem para não usarem disco e internet durante o jogo. Voltam a abrir na próxima vez que você ligar o PC.

**O que o código prepara:** Stop-Process -Force para OneDrive, Dropbox, GoogleDriveFS e BackgroundTransferHost; não remove autostart.

**Entrada:** `run-action` `{"action":"Apps de Sincronização Fechados"}`  
**Retorno esperado:** {success,message?,error?}; mostra retorno e registra histórico recente.  
**Reversão/leitura:** Ação pontual sem interruptor de desfazer neste item; duração/reinstalação conforme finalidade.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:115`, `deep/backend-focused.js:1882`.

### Mouse e teclado

#### 30. Mira sem aceleração do mouse

Chave: `tweak:mouse-accel` · tipo `T` · grupo: Mouse.

**Finalidade declarada:** Desliga a "Precisão do ponteiro aprimorada". O cursor anda sempre a mesma distância para o mesmo movimento da mão, e a memória muscular da mira funciona.

**O que o código prepara:** Grava MouseSpeed, MouseThreshold1 e MouseThreshold2 como String 0 em HKCU\Control Panel\Mouse e aplica SystemParametersInfo(4,0,[0,0,0],2). Desfazer reaplica os valores do Registro.

**Entrada:** `tweak-set` `{"id":"mouse-accel","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: ["MouseSpeed", "MouseThreshold1", "MouseThreshold2"]["map"]((_0x6feabb, _0x5f21d1) => [K["MOUSE"], _0x6feabb, "String", '0', ['1', '6', '10'][_0x5f21d1]])
on: "Add-Type -Namespace GDZ -Name Mouse -MemberDefinition '[DllImport(\"user32.dll\")] public static extern bool SystemParametersInfo(int a, int b, int[] c, int d);' -ErrorAction SilentlyContinue\n    [GDZ.Mouse]::SystemParametersInfo(4, 0, [int[]](0,0,0), 2) | Out-Null"
off: MOUSE_APPLY_FROM_REG
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:123`, `deep/backend-focused.js:3384`.

#### 31. Teclado repetindo na velocidade máxima

Chave: `tweak:kbd-repeat` · tipo `T` · grupo: Teclado.

**Finalidade declarada:** Põe no máximo as duas réguas de "Propriedades do Teclado": o intervalo até a tecla começar a repetir vai para o mais curto e a taxa de repetição para a mais rápida. Segurar A ou D passa a responder no primeiro instante.

**O que o código prepara:** Grava KeyboardDelay=String 0 e KeyboardSpeed=String 31 em HKCU\Control Panel\Keyboard; usa KBD_APPLY_FROM_REG para aplicar e desfazer.

**Entrada:** `tweak-set` `{"id":"kbd-repeat","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [[K['KEYBOARD'], "KeyboardDelay", "String", '0', '1'], [K["KEYBOARD"], "KeyboardSpeed", 'String', '31', '31']]
on: KBD_APPLY_FROM_REG
off: KBD_APPLY_FROM_REG
```

**Limites e efeitos colaterais:** Vale para o Windows todo: digitando, segurar uma tecla enche a linha de letras mais rápido.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:124`, `deep/backend-focused.js:3411`.

#### 32. Sem janelas de acessibilidade no meio do jogo

Chave: `tweak:sticky` · tipo `T` · grupo: Teclado.

**Finalidade declarada:** Apertar Shift 5 vezes ou segurar Num Lock/Shift não abre mais as janelas de Teclas de Aderência e Filtragem no meio da partida. Os recursos continuam nas Configurações.

**O que o código prepara:** Grava Flags=String 506 em StickyKeys, 58 em ToggleKeys e 122 em Keyboard Response (HKCU\Control Panel\Accessibility). O probe verifica o bit 4; desativa atalhos, sem remover os recursos.

**Entrada:** `tweak-set` `{"id":"sticky","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [[K["ACCESS"] + "\\StickyKeys", 'Flags', "String", "506", "510"], [K["ACCESS"] + "\\ToggleKeys", "Flags", "String", '58', '62'], [K["ACCESS"] + "\\Keyboard Response", 'Flags', "String", "122", '126']]
probe: "(@('StickyKeys','ToggleKeys','Keyboard Response') | Where-Object { ([int](GdzReg \"" + K['ACCESS'] + "\\$_\" 'Flags')) -band 4 }).Count -eq 0"
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:124`, `deep/backend-focused.js:3394`.

#### 33. Num Lock ligado ao iniciar

Chave: `tweak:numlock` · tipo `T` · grupo: Teclado.

**Finalidade declarada:** O teclado numérico já vem ligado na tela de login e quando você entra no Windows.

**O que o código prepara:** Grava InitialKeyboardIndicators=String 2 em HKCU e HKEY_USERS\.DEFAULT\Control Panel\Keyboard; os padrões de fallback são 0 e 2147483648.

**Entrada:** `tweak-set` `{"id":"numlock","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


**Aplicabilidade/reinício:** marcador `reboot`.

Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [["HKCU:\\Control Panel\\Keyboard", "InitialKeyboardIndicators", "String", '2', '0'], ["Registry::HKEY_USERS\\.DEFAULT\\Control Panel\\Keyboard", "InitialKeyboardIndicators", "String", '2', "2147483648"]]
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:124`, `deep/backend-focused.js:3403`.

### Memória

#### 34. Memória virtual gerenciada pelo Windows

Chave: `act:Pagefile Automático` · tipo `A` · grupo: Memória.

**Finalidade declarada:** Volta o arquivo de paginação para o automático. Otimizadores que desligam ou fixam esse arquivo fazem jogos pesados, como o GTA, fecharem do nada.

**O que o código prepara:** Lê Win32_ComputerSystem; se já automático emite aviso; caso contrário Set-CimInstance -Property @{AutomaticManagedPagefile=$true}. Não escreve diretamente um tamanho fixo.

**Entrada:** `run-action` `{"action":"Pagefile Automático"}`; `revert-action` `{"action":"Pagefile Automático"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Aplicabilidade/reinício:** marcador `reboot`.

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:131`, `deep/backend-focused.js:1897`.

### Limpeza

#### 35. Limpar sobras do Windows Update

Chave: `tool:update-cleanup` · tipo `TOOL` · grupo: Mais espaço no disco C:.

**Finalidade declarada:** Apaga versões antigas de arquivos do sistema que as atualizações deixam para trás. Costuma liberar alguns GB no disco C:.

**O que o código prepara:** Mede espaço livre do disco do sistema; Dism.exe /Online /Cleanup-Image /StartComponentCleanup /NoRestart. Confere LASTEXITCODE; mede diferença e informa GB se >0,05. Timeout 2400000 ms (40 min). Não usa ResetBase neste script.

**Entrada:** `tweak-tool` `{"id":"update-cleanup"}`  
**Retorno esperado:** {success,message?,error?}; tools de tweaks-status informa execução em curso.  
**Reversão/leitura:** Sem desfazer específico neste item. Ponto de restauração usa recuperação externa do Windows; limpeza e reparo são operações pontuais.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:142`, `src/otimizacoes.js:211`, `deep/backend-focused.js:3687`.


**Timeout do backend:** `2400000` ms.

#### 36. Desligar de verdade (sem hibernação)

Chave: `tweak:hibernate` · tipo `T` · grupo: Mais espaço no disco C:.

**Finalidade declarada:** Desativa a hibernação e a Inicialização Rápida. O "Desligar" passa a zerar o Windows (drivers e memória limpos a cada boot) e o arquivo hiberfil.sys some, liberando alguns GB no disco C:.

**O que o código prepara:** Grava HibernateEnabled=0 e chama powercfg /hibernate off. Ao desligar, powercfg /hibernate on só é chamado se o Registro restaurado indicar 1.

**Entrada:** `tweak-set` `{"id":"hibernate","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [["HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Power", "HibernateEnabled", "DWord", 0, 1]]
on: "powercfg /hibernate off 2>$null | Out-Null"
off: "if ((Get-ItemProperty -LiteralPath 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Power' -ErrorAction SilentlyContinue).HibernateEnabled -eq 1) { powercfg /hibernate on 2>$null | Out-Null }"
```

**Limites e efeitos colaterais:** O PC pode levar alguns segundos a mais para ligar. Em notebook, você perde a opção Hibernar.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:143`, `deep/backend-focused.js:3298`.

### Internet e live

#### 37. Servidor DNS

Chave: `dns` · tipo `DNS` · grupo: Internet.

**Finalidade declarada:** Escolha de provedor para resolução de nomes; não diminui ping dentro do jogo segundo o próprio texto.

**O que o código prepara:** Nos adaptadores com Status Up, Set-DnsClientServerAddress recebe os dois IPv4 do provedor e Clear-DnsClientCache limpa cache. Cria snapshot sob Tweak: dns antes da primeira troca. Automático tenta reverter snapshot e, se ainda não reconhecido como auto, usa ResetServerAddresses.

**Entrada:** `tweak-dns` `{"provider":"auto|cloudflare|google|quad9|adguard"}`  
**Retorno esperado:** {success,dns?,error?}; custom indica DNS fora das escolhas conhecidas.  
**Reversão/leitura:** Automático retorna ao DNS automático; pode restaurar e depois resetar um DNS personalizado anterior. O snapshot não significa um botão separado de recuperar o personalizado.

**Limites e efeitos colaterais:** Identificação do provedor usa somente o primeiro endereço não vazio retornado, não confirma uniformidade de todos adaptadores/servidores.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:151`, `deep/backend-focused.js:3645`.



**Endereços declarados:** `{"cloudflare": ["1.1.1.1", "1.0.0.1"], "google": ["8.8.8.8", "8.8.4.4"], "quad9": ["9.9.9.9", "149.112.112.112"], "adguard": ["94.140.14.14", "94.140.15.15"]}`.

#### 38. Sem upload de atualizações para outros PCs

Chave: `tweak:delivery` · tipo `T` · grupo: Internet.

**Finalidade declarada:** O Windows para de enviar pedaços de atualizações para computadores de outras pessoas pela internet. Sobra upload para o jogo, a chamada e a live.

**O que o código prepara:** Grava DODownloadMode=0 em política DeliveryOptimization do HKLM.

**Entrada:** `tweak-set` `{"id":"delivery","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [[K["DO_POL"], "DODownloadMode", "DWord", 0, null]]
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:151`, `deep/backend-focused.js:3273`.

#### 39. LSO da placa de rede desligado

Chave: `tweak:lso` · tipo `T` · grupo: Internet.

**Finalidade declarada:** Com o LSO (Large Send Offload) quem divide os envios grandes é a placa de rede, e não o processador. Alguns drivers (Intel I219/I225 e certos Realtek) fazem isso com defeito: upload lento, Discord e live engasgando ou a conexão caindo do nada. Desligar resolve nesses casos.

**O que o código prepara:** Nos adaptadores físicos detectados, grava *LsoV2IPv4=String 0 e, quando existe, *LsoV2IPv6=String 0 sob a classe de rede {4d36e972-e325-11ce-bfc1-08002be10318}; reinicia adaptador. Valor padrão listado é 1, sujeito ao backup.

**Entrada:** `tweak-set` `{"id":"lso","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


**Aplicabilidade/reinício:** marcador `net`.

Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: nicLsoReg
on: _0x47886c => nicLsoRestart(_0x47886c, true)
off: _0x8b74f3 => nicLsoRestart(_0x8b74f3, false)
block: _0x33c0d4 => _0x33c0d4["nicLso"]["length"] ? null : "Nenhuma placa de rede deste PC tem LSO para desligar."
```

**Limites e efeitos colaterais:** Só ative se você tem esse problema: não muda o ping nem o FPS do FiveM, e o processador trabalha um pouco mais em upload pesado. A internet cai por alguns segundos ao ligar e ao desligar.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:151`, `deep/backend-focused.js:3348`.

#### 40. Economia de energia da placa de rede desligada (EEE)

Chave: `tweak:eee` · tipo `T` · grupo: Internet.

**Finalidade declarada:** O EEE (Energy Efficient Ethernet) e o Green Ethernet põem a placa de rede para "cochilar" entre um pacote e outro e gastar menos energia. Com alguns roteadores e switches isso dá defeito: a conexão cai e volta, o ping dá picos ou a velocidade do cabo cai para 100 Mb. Desligar resolve nesses casos.

**O que o código prepara:** Para palavras de driver detectadas (*EEE, EEE, AdvancedEEE, EEELinkAdvertisement, EnableGreenEthernet), grava String 0 sob a classe de rede. O fallback de reversão vem do default declarado pelo driver em Ndi\Params; reinicia adaptador.

**Entrada:** `tweak-set` `{"id":"eee","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


**Aplicabilidade/reinício:** marcador `net`.

Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: nicOptReg(NIC_EEE)
on: nicOptRestart(NIC_EEE, "economia de energia da placa de rede desligada")
off: nicOptRestart(NIC_EEE, "economia de energia da placa de rede religada")
block: _0x49684e => nicOptOf(_0x49684e, NIC_EEE)['length'] ? null : "Nenhuma placa de rede com cabo deste PC tem EEE ou Green Ethernet para desligar."
```

**Limites e efeitos colaterais:** É correção, não ganho de ping: com a rede funcionando bem, não muda nada no FiveM. A internet cai por alguns segundos ao ligar e ao desligar.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:151`, `deep/backend-focused.js:3360`.

#### 41. Placa de rede sem moderação de interrupção

Chave: `tweak:nic-intmod` · tipo `T` · grupo: Internet.

**Finalidade declarada:** Por padrão a placa de rede junta vários pacotes antes de avisar o processador. Sem a moderação ela avisa a cada pacote, e o pacote chega ao jogo uma fração de milissegundo antes.

**O que o código prepara:** Para *InterruptModeration detectado no driver, grava String 0 sob a classe de rede, depois reinicia adaptador; fallback de reversão é o default obtido no driver.

**Entrada:** `tweak-set` `{"id":"nic-intmod","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


**Aplicabilidade/reinício:** marcador `net`.

Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: nicOptReg(NIC_INTMOD)
on: nicOptRestart(NIC_INTMOD, "moderação de interrupção desligada")
off: nicOptRestart(NIC_INTMOD, "moderação de interrupção religada")
block: _0x35941e => nicOptOf(_0x35941e, NIC_INTMOD)["length"] ? null : "Nenhuma placa de rede deste PC tem moderação de interrupção para desligar."
```

**Limites e efeitos colaterais:** O ganho é menor que 1 ms: não espere ver diferença no ping. Em troca o processador trabalha mais durante downloads e lives, e é ele que limita o FPS do FiveM. Não use em PC fraco. A internet cai por alguns segundos ao ligar e ao desligar.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:151`, `deep/backend-focused.js:3372`.

#### 42. Limpar cache de DNS

Chave: `tool:flush-dns` · tipo `TOOL` · grupo: Internet.

**Finalidade declarada:** Resolve site ou servidor de jogo que "não abre" depois de mudar de rede ou de DNS. Instantâneo.

**O que o código prepara:** Clear-DnsClientCache e ipconfig /flushdns. Timeout 30000 ms. Saída do ipconfig é descartada e a mensagem de sucesso é escrita pelo script.

**Entrada:** `tweak-tool` `{"id":"flush-dns"}`  
**Retorno esperado:** {success,message?,error?}; tools de tweaks-status informa execução em curso.  
**Reversão/leitura:** Sem desfazer específico neste item. Ponto de restauração usa recuperação externa do Windows; limpeza e reparo são operações pontuais.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:151`, `src/otimizacoes.js:211`, `deep/backend-focused.js:3679`.


**Timeout do backend:** `0x7530` ms.

#### 43. Resetar a rede

Chave: `tool:net-reset` · tipo `TOOL` · grupo: Internet.

**Finalidade declarada:** Refaz as configurações de rede do Windows (Winsock e TCP/IP). Use quando a internet funciona no celular mas não no PC.

**O que o código prepara:** ipconfig /flushdns; netsh winsock reset; netsh int ip reset. Timeout 120000 ms. Escreve aviso de reiniciar; não há verificação explícita de todos os códigos de saída nesta string.

**Entrada:** `tweak-tool` `{"id":"net-reset"}`  
**Retorno esperado:** {success,message?,error?}; tools de tweaks-status informa execução em curso.  
**Reversão/leitura:** Sem desfazer específico neste item. Ponto de restauração usa recuperação externa do Windows; limpeza e reparo são operações pontuais.


**Limites e efeitos colaterais:** Apaga IP fixo e pode desconfigurar VPNs. Reinicie o PC depois.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:151`, `src/otimizacoes.js:211`, `deep/backend-focused.js:3683`.


**Timeout do backend:** `120000` ms.

#### 44. OBS com prioridade

Chave: `act:OBS Prioritário` · tipo `A` · grupo: Live e gravação.

**Finalidade declarada:** Abre o OBS como administrador (assim ele reserva placa de vídeo e acaba o "lag de codificação") e impede o Windows de jogá-lo para núcleos lentos.

**O que o código prepara:** Para caps.obsExe, preserva flags existentes e acrescenta RUNASADMIN em AppCompatFlags\Layers (HKCU); executa powercfg /powerthrottling disable /path exe. Desfazer extra usa /powerthrottling reset; pode provocar UAC toda abertura.

**Entrada:** `run-action` `{"action":"OBS Prioritário"}`; `revert-action` `{"action":"OBS Prioritário"}`  
**Retorno esperado:** {success,message?,error?}; atualiza estado local e solicita releitura.  
**Reversão/leitura:** Desligar chama revert-action com o mesmo nome; recuperação depende do backup e dos extras correspondentes.


**Aplicabilidade/reinício:** capacidade `obs`.

**Limites e efeitos colaterais:** O Windows vai pedir confirmação toda vez que o OBS abrir.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:154`, `deep/backend-focused.js:1899`.

#### 45. Configuração ideal do OBS

Chave: `check:encoder` · tipo `C` · grupo: Live e gravação.

**Finalidade declarada:** Diz qual encoder, preset e bitrate usar no OBS com a sua placa de vídeo.

**O que o código prepara:** Retorna recomendações fixas por capacidade: NVIDIA→NVENC H.264 Twitch/AV1 ou HEVC YouTube, P5/P6; AMD→AMF e AV1 citado RX7000+; Intel→QSV; nenhum→x264 veryfast 720p. CBR/keyframe 2 s; Twitch 6000 kbps 1080p60 ou 4500 kbps 720p60. Informa se OBS foi detectado. Não configura OBS nem mede upload.

**Entrada:** `run-check` `{"name":"encoder"}`  
**Retorno esperado:** {title,level,lines[],success?,error?}; título e observações na linha.  
**Reversão/leitura:** Só leitura neste contrato; não troca driver, BIOS ou memória automaticamente.

**Limites e efeitos colaterais:** Recomendações de plataforma/bitrate são fixas no pacote e não foram verificadas como regras atuais; upload e qualidade não são medidos.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:157`, `deep/backend-focused.js:2180`.

### Windows


#### 46. Telemetria e anúncios personalizados

Chave: `tweak:telemetry` · tipo `T` · grupo: Privacidade e propaganda.

**Finalidade declarada:** Desliga o serviço que envia dados de uso para a Microsoft, o ID de propaganda e as pesquisas de "dê sua opinião". Um processo a menos rodando o tempo todo.

**O que o código prepara:** Grava AllowTelemetry=0, AdvertisingInfo\Enabled=0, TailoredExperiencesWithDiagnosticDataEnabled=0 e NumberOfSIUFInPeriod=0; desativa e para DiagTrack. Desfazer tenta iniciar o serviço se ele não estiver Disabled.

**Entrada:** `tweak-set` `{"id":"telemetry","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [["HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection", "AllowTelemetry", 'DWord', 0, null], ["HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo", "Enabled", "DWord", 0, 1], ["HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", "DWord", 0, 1], ["HKCU:\\Software\\Microsoft\\Siuf\\Rules", "NumberOfSIUFInPeriod", "DWord", 0, null]]
svc: [['DiagTrack', "Disabled", 'Automatic']]
on: "Stop-Service DiagTrack -Force -ErrorAction SilentlyContinue"
off: "if ((Get-Service DiagTrack -ErrorAction SilentlyContinue).StartType -ne 'Disabled') { Start-Service DiagTrack -ErrorAction SilentlyContinue }"
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:166`, `deep/backend-focused.js:3422`.

#### 47. Sem sugestões, dicas e apps instalados sozinhos

Chave: `tweak:suggestions` · tipo `T` · grupo: Privacidade e propaganda.

**Finalidade declarada:** O Windows para de instalar apps promovidos (joguinhos, TikTok...) e tira as "sugestões" do Iniciar e das Configurações e as dicas que aparecem na tela.

**O que o código prepara:** Grava 0 nos oito indicadores do ContentDeliveryManager (SilentInstalledAppsEnabled, SystemPaneSuggestionsEnabled, SoftLandingEnabled e cinco SubscribedContent-*Enabled) e DisableWindowsConsumerFeatures=1 em CloudContent.

**Entrada:** `tweak-set` `{"id":"suggestions","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [...["SilentInstalledAppsEnabled", "SystemPaneSuggestionsEnabled", "SoftLandingEnabled", "SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled", "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled", "SubscribedContent-310093Enabled"]["map"](_0x146595 => [K_CDM, _0x146595, "DWord", 0, 1]), ["HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent", "DisableWindowsConsumerFeatures", "DWord", 1, null]]
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:166`, `deep/backend-focused.js:3433`.

#### 48. Pesquisa do Iniciar sem Bing

Chave: `tweak:bing` · tipo `T` · grupo: Privacidade e propaganda.

**Finalidade declarada:** A pesquisa do menu Iniciar procura só no seu PC: sem resultados da web e sem sugestões do Bing. Fica mais rápida e o que você digita não vai para a internet.

**O que o código prepara:** Grava BingSearchEnabled=0 e DisableSearchBoxSuggestions=1; encerra SearchHost/SearchApp e reinicia Explorer ao aplicar e ao desfazer.

**Entrada:** `tweak-set` `{"id":"bing","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


**Aplicabilidade/reinício:** marcador `explorer`.

Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [[K_SEARCH, "BingSearchEnabled", "DWord", 0, null], ["HKCU:\\Software\\Policies\\Microsoft\\Windows\\Explorer", "DisableSearchBoxSuggestions", "DWord", 1, null]]
on: "Stop-Process -Name SearchHost,SearchApp -Force -ErrorAction SilentlyContinue\n    \n    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue\n    Start-Sleep -Milliseconds 1500\n    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer }"
off: "Stop-Process -Name SearchHost,SearchApp -Force -ErrorAction SilentlyContinue\n    \n    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue\n    Start-Sleep -Milliseconds 1500\n    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer }"
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:166`, `deep/backend-focused.js:3441`.

#### 49. Sem histórico de atividades

Chave: `tweak:activity` · tipo `T` · grupo: Privacidade e propaganda.

**Finalidade declarada:** O Windows para de registrar quais arquivos, sites e apps você abriu e de enviar esse histórico para a sua conta Microsoft.

**O que o código prepara:** Grava EnableActivityFeed=0, PublishUserActivities=0 e UploadUserActivities=0 em política HKLM\SOFTWARE\Policies\Microsoft\Windows\System.

**Entrada:** `tweak-set` `{"id":"activity","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: ["EnableActivityFeed", "PublishUserActivities", "UploadUserActivities"]["map"](_0x1f4457 => ["HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\System", _0x1f4457, "DWord", 0, null])
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:166`, `deep/backend-focused.js:3452`.

#### 50. Sem Widgets (notícias e clima)

Chave: `tweak:widgets` · tipo `T` · grupo: Privacidade e propaganda.

**Finalidade declarada:** Tira o painel de notícias e clima da barra de tarefas e impede que ele fique carregado em segundo plano: são centenas de MB de memória a menos.

**O que o código prepara:** Grava AllowNewsAndInterests=0 em política Dsh e EnableFeeds=0 em Windows Feeds; encerra Widgets/WidgetService. Reinício indicado.

**Entrada:** `tweak-set` `{"id":"widgets","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


**Aplicabilidade/reinício:** marcador `reboot`.

Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [["HKLM:\\SOFTWARE\\Policies\\Microsoft\\Dsh", "AllowNewsAndInterests", "DWord", 0, null], ["HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Feeds", "EnableFeeds", 'DWord', 0, null]]
on: "Get-Process -Name Widgets,WidgetService -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue"
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:166`, `deep/backend-focused.js:3460`.

#### 51. Localização desligada

Chave: `tweak:location` · tipo `T` · grupo: Privacidade e propaganda.

**Finalidade declarada:** Nenhum app consegue saber onde você está.

**O que o código prepara:** Grava Value=String Deny em ConsentStore\location (HKLM); fallback desligado Allow. Não é prova de impedir toda inferência de localização por qualquer programa.

**Entrada:** `tweak-set` `{"id":"location","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [["HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\CapabilityAccessManager\\ConsentStore\\location", "Value", "String", "Deny", "Allow"]]
```

**Limites e efeitos colaterais:** O fuso horário automático, o Clima e o "Localizar meu dispositivo" deixam de funcionar. A frase do produto sobre nenhum app saber a localização é mais forte que o efeito demonstrado de uma permissão do Windows.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:166`, `deep/backend-focused.js:3470`.

#### 52. Finalizar tarefa pela barra de tarefas

Chave: `tweak:end-task` · tipo `T` · grupo: Praticidade e aparência.

**Finalidade declarada:** Botão direito num programa na barra de tarefas → "Finalizar tarefa". Fecha na hora um jogo travado, sem abrir o Gerenciador de Tarefas.

**O que o código prepara:** Grava TaskbarEndTask=1 em Explorer\Advanced\TaskbarDeveloperSettings (HKCU); bloqueado abaixo do build 22631 quando build é conhecido.

**Entrada:** `tweak-set` `{"id":"end-task","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [["HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced\\TaskbarDeveloperSettings", "TaskbarEndTask", "DWord", 1, null]]
block: _0x5023af => _0x5023af["build"] && _0x5023af["build"] < 22631 ? "Requer Windows 11 23H2 ou mais novo." : null
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:169`, `deep/backend-focused.js:3478`.

#### 53. Mostrar a extensão dos arquivos

Chave: `tweak:file-ext` · tipo `T` · grupo: Praticidade e aparência.

**Finalidade declarada:** Mostra o final do nome dos arquivos (.exe, .zip, .pdf). Ajuda a não abrir vírus disfarçado, como "foto.jpg.exe".

**O que o código prepara:** Grava HideFileExt=0 em Explorer\Advanced (HKCU).

**Entrada:** `tweak-set` `{"id":"file-ext","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [[K_ADV, "HideFileExt", 'DWord', 0, 1]]
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:169`, `deep/backend-focused.js:3487`.

#### 54. Mostrar arquivos ocultos

Chave: `tweak:hidden-files` · tipo `T` · grupo: Praticidade e aparência.

**Finalidade declarada:** Mostra pastas escondidas como a AppData, onde ficam configs e saves de muitos jogos.

**O que o código prepara:** Grava Hidden=1 em Explorer\Advanced (HKCU). Não altera ShowSuperHidden nesta declaração.

**Entrada:** `tweak-set` `{"id":"hidden-files","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [[K_ADV, "Hidden", "DWord", 1, 2]]
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:169`, `deep/backend-focused.js:3495`.

#### 55. Explorador abre em "Este Computador"

Chave: `tweak:this-pc` · tipo `T` · grupo: Praticidade e aparência.

**Finalidade declarada:** Ao abrir o Explorador (Win+E) você cai direto nos seus discos, em vez da página Início com arquivos recentes.

**O que o código prepara:** Grava LaunchTo=1 em Explorer\Advanced (HKCU), abrindo em Este Computador.

**Entrada:** `tweak-set` `{"id":"this-pc","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [[K_ADV, "LaunchTo", "DWord", 1, null]]
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:169`, `deep/backend-focused.js:3502`.

#### 56. Menu do botão direito completo

Chave: `tweak:classic-menu` · tipo `T` · grupo: Praticidade e aparência.

**Finalidade declarada:** Volta o menu de contexto do Windows 10, com todas as opções, sem precisar clicar em "Mostrar mais opções".

**O que o código prepara:** Cria HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32 com valor padrão vazio e reinicia Explorer. Ao desfazer remove recursivamente a chave CLSID deste truque; só Windows 11.

**Entrada:** `tweak-set` `{"id":"classic-menu","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


**Aplicabilidade/reinício:** marcador `explorer`.

Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
probe: "(Test-Path -LiteralPath 'HKCU:\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32')"
on: "if (-not (Test-Path -LiteralPath 'HKCU:\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32')) { New-Item -Path 'HKCU:\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32' -Force | Out-Null }\n    Set-ItemProperty -LiteralPath 'HKCU:\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32' -Name '(default)' -Value '' -Force\n    \n    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue\n    Start-Sleep -Milliseconds 1500\n    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer }"
off: "Remove-Item -LiteralPath 'HKCU:\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}' -Recurse -Force -ErrorAction SilentlyContinue\n    \n    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue\n    Start-Sleep -Milliseconds 1500\n    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer }"
block: WIN11
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:169`, `deep/backend-focused.js:3509`.

#### 57. Tema escuro

Chave: `tweak:dark` · tipo `T` · grupo: Praticidade e aparência.

**Finalidade declarada:** Deixa o Windows e os apps compatíveis no modo escuro.

**O que o código prepara:** Grava AppsUseLightTheme=0 e SystemUsesLightTheme=0 em Themes\Personalize (HKCU).

**Entrada:** `tweak-set` `{"id":"dark","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [["HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "AppsUseLightTheme", 'DWord', 0, 1], ["HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "SystemUsesLightTheme", "DWord", 0, 1]]
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:169`, `deep/backend-focused.js:3520`.

#### 58. Barra de tarefas à esquerda

Chave: `tweak:taskbar-left` · tipo `T` · grupo: Praticidade e aparência.

**Finalidade declarada:** Botão Iniciar e ícones no canto esquerdo, como no Windows 10.

**O que o código prepara:** Grava TaskbarAl=0 em Explorer\Advanced (HKCU), bloqueado fora do Windows 11.

**Entrada:** `tweak-set` `{"id":"taskbar-left","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [[K_ADV, 'TaskbarAl', "DWord", 0, null]]
block: WIN11
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:170`, `deep/backend-focused.js:3537`.

#### 59. Esconder o botão Visão de Tarefas

Chave: `tweak:task-view` · tipo `T` · grupo: Praticidade e aparência.

**Finalidade declarada:** Tira da barra o botão de áreas de trabalho virtuais. O atalho Win+Tab continua funcionando.

**O que o código prepara:** Grava ShowTaskViewButton=0 em Explorer\Advanced; Win+Tab continua disponível segundo a descrição.

**Entrada:** `tweak-set` `{"id":"task-view","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [[K_ADV, "ShowTaskViewButton", 'DWord', 0, 1]]
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:170`, `deep/backend-focused.js:3545`.

#### 60. Pesquisa da barra só como ícone

Chave: `tweak:search-icon` · tipo `T` · grupo: Praticidade e aparência.

**Finalidade declarada:** Troca a caixa de pesquisa larga por uma lupa e libera espaço na barra de tarefas.

**O que o código prepara:** Grava SearchboxTaskbarMode=1 em HKCU\...\Search; o fallback é 2.

**Entrada:** `tweak-set` `{"id":"search-icon","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [[K_SEARCH, "SearchboxTaskbarMode", "DWord", 1, 2]]
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:170`, `deep/backend-focused.js:3552`.

#### 61. Tela azul com detalhes do erro

Chave: `tweak:bsod-detail` · tipo `T` · grupo: Praticidade e aparência.

**Finalidade declarada:** Se o PC der tela azul, ela mostra o código e o arquivo que causou o erro. É a pista para achar o culpado (driver de vídeo, overclock, memória...).

**O que o código prepara:** Grava DisplayParameters=1 e DisableEmoticon=1 em HKLM\SYSTEM\CurrentControlSet\Control\CrashControl.

**Entrada:** `tweak-set` `{"id":"bsod-detail","on":"boolean"}`  
**Retorno esperado:** {success,on,message?,error?}; confere se on retornado coincide com a solicitação.  
**Reversão/leitura:** Desligar envia tweak-set {id,on:false}; o backend prevê backup e restauração. Os últimos valores de reg são fallback, não garantia de valor anterior.


Dados declarados no backend (trechos de evidência; não são instruções para executar):

```text
reg: [["HKLM:\\SYSTEM\\CurrentControlSet\\Control\\CrashControl", "DisplayParameters", "DWord", 1, 0], ["HKLM:\\SYSTEM\\CurrentControlSet\\Control\\CrashControl", "DisableEmoticon", 'DWord', 1, 0]]
```

**Limites e efeitos colaterais:** Altera configuração. Não houve execução nem medição de benefício, estabilidade ou reversão.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:170`, `deep/backend-focused.js:3559`.

#### 62. Criar ponto de restauração

Chave: `tool:restore-point` · tipo `TOOL` · grupo: Manutenção e reparo.

**Finalidade declarada:** Todo ajuste do GDZ já tem volta. O ponto de restauração é uma garantia extra do próprio Windows antes de mexer em muita coisa.

**O que o código prepara:** Temporariamente grava SystemRestorePointCreationFrequency=0 em HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore; Enable-ComputerRestore no disco do sistema e Checkpoint-Computer -Description GDZ Optimizer -RestorePointType MODIFY_SETTINGS; finally restaura/remove o valor anterior. Timeout 300000 ms (5 min).

**Entrada:** `tweak-tool` `{"id":"restore-point"}`  
**Retorno esperado:** {success,message?,error?}; tools de tweaks-status informa execução em curso.  
**Reversão/leitura:** Sem desfazer específico neste item. Ponto de restauração usa recuperação externa do Windows; limpeza e reparo são operações pontuais.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:173`, `src/otimizacoes.js:211`, `deep/backend-focused.js:3662`.


**Timeout do backend:** `300000` ms.

#### 63. Reparar arquivos do Windows

Chave: `tool:repair` · tipo `TOOL` · grupo: Manutenção e reparo.

**Finalidade declarada:** Roda o DISM e o SFC: verificam e consertam arquivos do Windows corrompidos. Indicado para travamentos, telas azuis e erros estranhos.

**O que o código prepara:** Dism.exe /Online /Cleanup-Image /RestoreHealth /NoRestart; guarda código DISM; depois sfc.exe /scannow. Reporta erro se DISM falhou e aviso final se não. Timeout 3600000 ms (60 min). O código de saída do SFC não é avaliado explicitamente nesse script.

**Entrada:** `tweak-tool` `{"id":"repair"}`  
**Retorno esperado:** {success,message?,error?}; tools de tweaks-status informa execução em curso.  
**Reversão/leitura:** Sem desfazer específico neste item. Ponto de restauração usa recuperação externa do Windows; limpeza e reparo são operações pontuais.

**Limites e efeitos colaterais:** Aviso final não comprova reparo SFC: a string só checa o código DISM.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:174`, `src/otimizacoes.js:211`, `deep/backend-focused.js:3691`.



**Timeout do backend:** `3600000` ms.

#### 64. Reiniciar a barra de tarefas

Chave: `once:Explorer Mínimo` · tipo `O` · grupo: Manutenção e reparo.

**Finalidade declarada:** Reinicia o Explorador do Windows. Resolve barra de tarefas ou Iniciar travados. Não muda o FPS.

**O que o código prepara:** Stop-Process explorer -Force; espera 600 ms; Start-Process explorer. Não substitui o shell.

**Entrada:** `run-action` `{"action":"Explorer Mínimo"}`  
**Retorno esperado:** {success,message?,error?}; mostra retorno e registra histórico recente.  
**Reversão/leitura:** Ação pontual sem interruptor de desfazer neste item; duração/reinstalação conforme finalidade.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:175`, `deep/backend-focused.js:1888`.

#### 65. Limpar ajustes antigos do GDZ

Chave: `once:Padrões Restaurados` · tipo `O` · grupo: Manutenção e reparo.

**Finalidade declarada:** Versões antigas do GDZ aplicavam ajustes sem efeito ou prejudiciais (recuperação da placa de vídeo, timer, fila do mouse, rede). Volta tudo ao padrão do Windows.

**O que o código prepara:** LEGACY_DEFAULTS remove TdrDelay, IRQ8Priority, PollInterval100us, MaxFrameLatency, ajustes NVTweak, TcpAckFrequency/TCPNoDelay/TcpMaxDataRetransmissions e opções BCD useplatformtick/useplatformclock; restaura Win32PrioritySeparation=2, MouseDataQueueSize=100, SystemResponsiveness=20, autotuning TCP normal, DoubleClickSpeed=500 e suavização de fontes.

**Entrada:** `run-action` `{"action":"Padrões Restaurados"}`  
**Retorno esperado:** {success,message?,error?}; mostra retorno e registra histórico recente.  
**Reversão/leitura:** Ação pontual sem interruptor de desfazer neste item; duração/reinstalação conforme finalidade.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:177`, `deep/backend-focused.js:1900`.

#### 66. Religar o Windows Defender

Chave: `fix:Defender Off` · tipo `FIX` · grupo: Manutenção e reparo.

**Finalidade declarada:** Religa a proteção em tempo real, se uma versão antiga do GDZ a desligou.

**O que o código prepara:** Set-MpPreference -DisableRealtimeMonitoring $false e Remove-ItemProperty DisableAntiSpyware da política Windows Defender. Exceções/erros são suprimidos nesse fallback, portanto a declaração não garante que Tamper Protection/políticas permitam religar.

**Entrada:** `revert-action` `{"action":"Defender Off"}`  
**Retorno esperado:** {success,message?,error?}; pede reversão de ação antiga.  
**Reversão/leitura:** Este botão já desfaz uma desativação antiga; não há alternância para aplicar a desativação.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:179`, `deep/backend-focused.js:3047`.

#### 67. Religar o Windows Update

Chave: `fix:Desativar Atualizações Windows` · tipo `FIX` · grupo: Manutenção e reparo.

**Finalidade declarada:** Religa o serviço de atualizações, se uma versão antiga do GDZ o desativou.

**O que o código prepara:** Set-Service wuauserv -StartupType Manual e Start-Service wuauserv. Erros são silenciosos nesse fallback.

**Entrada:** `revert-action` `{"action":"Desativar Atualizações Windows"}`  
**Retorno esperado:** {success,message?,error?}; pede reversão de ação antiga.  
**Reversão/leitura:** Este botão já desfaz uma desativação antiga; não há alternância para aplicar a desativação.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:181`, `deep/backend-focused.js:3048`.

#### 68. Religar a Restauração do Sistema

Chave: `fix:System Restore Off` · tipo `FIX` · grupo: Manutenção e reparo.

**Finalidade declarada:** Volta a criar pontos de restauração, se uma versão antiga do GDZ os desligou.

**O que o código prepara:** Enable-ComputerRestore -Drive $env:SystemDrive com ErrorAction SilentlyContinue. Não cria por si só um novo ponto neste reparo.

**Entrada:** `revert-action` `{"action":"System Restore Off"}`  
**Retorno esperado:** {success,message?,error?}; pede reversão de ação antiga.  
**Reversão/leitura:** Este botão já desfaz uma desativação antiga; não há alternância para aplicar a desativação.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:183`, `deep/backend-focused.js:3053`.

#### 69. Consertar a escala da tela

Chave: `fix:DPI Scaling Off` · tipo `FIX` · grupo: Manutenção e reparo.

**Finalidade declarada:** Desfaz o antigo "Desativar DPI Scaling", que deixava textos e janelas minúsculos em telas grandes.

**O que o código prepara:** Remove-ItemProperty Win8DpiScaling de HKCU\Control Panel\Desktop. Não define explicitamente uma escala percentual nova nem corrige toda incompatibilidade DPI.

**Entrada:** `revert-action` `{"action":"DPI Scaling Off"}`  
**Retorno esperado:** {success,message?,error?}; pede reversão de ação antiga.  
**Reversão/leitura:** Este botão já desfaz uma desativação antiga; não há alternância para aplicar a desativação.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:185`, `deep/backend-focused.js:3062`.

### Verificar hardware

#### 70. Verificar Ryzen

Chave: `check:amd` · tipo `C` · grupo: Processador.

**Finalidade declarada:** Confere o driver de chipset da AMD e, nos X3D de dois CCDs, a configuração que o driver precisa.

**O que o código prepara:** Lê nome CPU, procura dispositivos PnP AMD 3D V-Cache/PPM/GPIO/PSP/SMBus/I2C. Para 7900X3D/7950X3D/9900X3D/9950X3D/7945HX3D confere amd3dvcacheSvc, pacote XboxGamingOverlay, política AllowGameDVR e GUID Equilibrado ativo. Fora desses modelos só orienta EXPO/PBO; não altera BIOS.

**Entrada:** `run-check` `{"name":"amd"}`  
**Retorno esperado:** {title,level,lines[],success?,error?}; título e observações na linha.  
**Reversão/leitura:** Só leitura neste contrato; não troca driver, BIOS ou memória automaticamente.

**Aplicabilidade/reinício:** capacidade `amd`.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:194`, `deep/backend-focused.js:2119`.

#### 71. Verificar processador Intel

Chave: `check:intel` · tipo `C` · grupo: Processador.

**Finalidade declarada:** Confere o microcode dos Core de 13ª/14ª geração (degradação), os requisitos do Core Ultra 200S e o Modo de Jogo nos híbridos.

**O que o código prepara:** Lê Update Revision no Registro e extrai UInt32 a partir do byte 4. Para desktop Core i5/i7/i9 13ª/14ª gerações avisa se microcode conhecido<0x12B; para Core Ultra 200 confere build>=26100.2314 e microcode conhecido>=0x114. Em híbridos olha AutoGameModeEnabled. Não consulta versão mais recente online.

**Entrada:** `run-check` `{"name":"intel"}`  
**Retorno esperado:** {title,level,lines[],success?,error?}; título e observações na linha.  
**Reversão/leitura:** Só leitura neste contrato; não troca driver, BIOS ou memória automaticamente.

**Aplicabilidade/reinício:** capacidade `intel`.

**Limites e efeitos colaterais:** Se microcode não foi lido (mc=0), a condição de antigo falha e o texto pode dizer correção presente; esse texto não prova atualização da BIOS.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:195`, `deep/backend-focused.js:2137`.


#### 72. Dica: o maior ganho do Ryzen está na BIOS

Chave: `tip:Dica: o maior ganho do Ryzen está na BIOS` · tipo `TIP` · grupo: Processador.

**Finalidade declarada:** Ligue o EXPO para a memória e, se souber ajustar, o PBO com Curve Optimizer.

**O que o código prepara:** Apresenta a recomendação escrita pelo produto; desempenho e compatibilidade não foram confirmados nesta análise.

**Entrada:** nenhum IPC; conteúdo informativo/local.  
**Retorno esperado:** Apenas texto e filtragem por capacidade; não envia ajuste.  
**Reversão/leitura:** Orientação; mudanças seriam manuais fora do GDZ.

**Aplicabilidade/reinício:** capacidade `amd`.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/otimizacoes.js:196`.

#### 73. Dica: Intel com P-cores e E-cores

Chave: `tip:Dica: Intel com P-cores e E-cores` · tipo `TIP` · grupo: Processador.

**Finalidade declarada:** Deixe o Windows escolher os núcleos com o Modo de Jogo ligado. Desligar E-cores ou forçar afinidade não traz ganho.

**O que o código prepara:** Apresenta a recomendação escrita pelo produto; desempenho e compatibilidade não foram confirmados nesta análise.

**Entrada:** nenhum IPC; conteúdo informativo/local.  
**Retorno esperado:** Apenas texto e filtragem por capacidade; não envia ajuste.  
**Reversão/leitura:** Orientação; mudanças seriam manuais fora do GDZ.

**Aplicabilidade/reinício:** capacidade `intelHybrid`.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/otimizacoes.js:197`.

#### 74. Verificar placa de vídeo

Chave: `check:gpu` · tipo `C` · grupo: Placa de vídeo.

**Finalidade declarada:** Confere a idade do driver, o HAGS e, na NVIDIA, se o Resizable BAR está ligado.

**O que o código prepara:** Lê Win32_VideoController de VEN_10DE/1002/8086. Marca driver NVIDIA/AMD não integrado com data>180 dias; lê HwSchMode. Se encontra nvidia-smi, usa -q -d MEMORY e heurística BAR1 Total>256 MiB como ReBAR ligado. Não muda driver/BIOS.

**Entrada:** `run-check` `{"name":"gpu"}`  
**Retorno esperado:** {title,level,lines[],success?,error?}; título e observações na linha.  
**Reversão/leitura:** Só leitura neste contrato; não troca driver, BIOS ou memória automaticamente.

**Limites e efeitos colaterais:** Sem data, idade assume 0. ReBAR é inferência por BAR1, não uma consulta direta da opção BIOS. Não compara driver com release mais novo.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:200`, `deep/backend-focused.js:2159`.


#### 75. Dica NVIDIA RTX: mais FPS com o DLSS 4.5

Chave: `tip:Dica NVIDIA RTX: mais FPS com o DLSS 4.5` · tipo `TIP` · grupo: Placa de vídeo.

**Finalidade declarada:** Ligue "DLSS no modelo recomendado pela NVIDIA" em Desempenho → Placa de vídeo e tela (é o botão "Recomendado" de Substituição do DLSS na NVIDIA App). Depois, dentro do jogo, baixe o DLSS de Qualidade para Equilibrado ou Desempenho: a imagem desses modos melhorou bastante no DLSS 4.5 e é daí que vem o FPS. Não force a Predefinição M em todos os modos: ela só pesa mais. Vale para jogos que têm DLSS (o FiveM clássico não tem).

**O que o código prepara:** Apresenta a recomendação escrita pelo produto; desempenho e compatibilidade não foram confirmados nesta análise.

**Entrada:** nenhum IPC; conteúdo informativo/local.  
**Retorno esperado:** Apenas texto e filtragem por capacidade; não envia ajuste.  
**Reversão/leitura:** Orientação; mudanças seriam manuais fora do GDZ.

**Aplicabilidade/reinício:** capacidade `rtx`.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/otimizacoes.js:201`.

#### 76. Dica NVIDIA: menor atraso sem cortes na imagem

Chave: `tip:Dica NVIDIA: menor atraso sem cortes na imagem` · tipo `TIP` · grupo: Placa de vídeo.

**Finalidade declarada:** No jogo, ligue o NVIDIA Reflex. No Painel NVIDIA: V-Sync ligado + G-Sync ligado + limite de FPS 3 abaixo da taxa do monitor (ex.: 141 em 144 Hz).

**O que o código prepara:** Apresenta a recomendação escrita pelo produto; desempenho e compatibilidade não foram confirmados nesta análise.

**Entrada:** nenhum IPC; conteúdo informativo/local.  
**Retorno esperado:** Apenas texto e filtragem por capacidade; não envia ajuste.  
**Reversão/leitura:** Orientação; mudanças seriam manuais fora do GDZ.

**Aplicabilidade/reinício:** capacidade `nvidia`.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/otimizacoes.js:202`.

#### 77. Dica AMD Radeon: menor atraso

Chave: `tip:Dica AMD Radeon: menor atraso` · tipo `TIP` · grupo: Placa de vídeo.

**Finalidade declarada:** No jogo, ligue o AMD Anti-Lag 2 quando existir. No Adrenalin: FreeSync ligado, Enhanced Sync desligado e limite de FPS um pouco abaixo da taxa do monitor.

**O que o código prepara:** Apresenta a recomendação escrita pelo produto; desempenho e compatibilidade não foram confirmados nesta análise.

**Entrada:** nenhum IPC; conteúdo informativo/local.  
**Retorno esperado:** Apenas texto e filtragem por capacidade; não envia ajuste.  
**Reversão/leitura:** Orientação; mudanças seriam manuais fora do GDZ.

**Aplicabilidade/reinício:** capacidade `amdGpu`.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/otimizacoes.js:203`.

#### 78. Verificar memória (XMP/EXPO)

Chave: `check:ram` · tipo `C` · grupo: Memória RAM.

**Finalidade declarada:** Confere se a memória está na velocidade do kit e em dual channel. XMP/EXPO desligado é o maior ganho de FPS de graça que existe.

**O que o código prepara:** Lê Win32_PhysicalMemory, maior ConfiguredClockSpeed, tipo SMBIOS, capacidade e PartNumber. Extrai números de quatro dígitos 2400..9000 do modelo como velocidade presumida do kit. Avisa se kit>config+100; ou DDR4<=2666/DDR5<=4800; ou só um módulo. Menos de 16 GB acrescenta orientação. Não lê diretamente XMP/EXPO na BIOS.

**Entrada:** `run-check` `{"name":"ram"}`  
**Retorno esperado:** {title,level,lines[],success?,error?}; título e observações na linha.  
**Reversão/leitura:** Só leitura neste contrato; não troca driver, BIOS ou memória automaticamente.

**Limites e efeitos colaterais:** Velocidade do kit é inferida do PartNumber; contar pentes não é leitura garantida da topologia de canais em todo hardware.

**Evidência:** comprovado-estaticamente-no-renderer-e-declaracao-backend. Fontes: `src/otimizacoes.js:206`, `deep/backend-focused.js:2095`.

## Recursos fora de PAGES

Esta parte inclui consultas, operações de disco/programas/jogos, diagnósticos e cada opção de Scaling/OptiScaler declarada nos arquivos do renderer, com distinção de HTML legado sem carga localizada. As entradas são uma taxonomia de análise; seus IDs `extra:*` não são IDs oficiais de ação do GDZ. O canal e o payload são os identificadores efetivamente enviados.

### Painel


#### 079. Hardware e uso no painel

ID da análise: `extra:dashboard-hardware`.

**Para que serve:** Mostra modelos e barras de CPU/GPU/RAM.  
**Funcionamento:** Consulta hardware; abrevia nomes; uso não é benchmark.

**Entrada:** `get-hardware` sem argumento; `get-stats` sem argumento  
**Retorno:** {cpu,gpu,ram} e percentuais.  
**Reversão/leitura:** Só leitura.  

**Limites:** Fallback de desenvolvimento pode simular valores apenas sem ponte Electron.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:1627`, `src/dashboard.html:1658`, `src/dashboard.html:2233`.

#### 080. Anel aplicados/recomendados

ID da análise: `extra:dashboard-ring`.

**Para que serve:** Conta os ajustes recomendados já aplicados.  
**Funcionamento:** Filtra T/A visíveis, disponíveis, recomendados e não bloqueados. 14 de 19 é uma seleção, não todo o catálogo.

**Entrada:** `tweaks-status` sem argumento; `actions-status` sem argumento; `get-caps` sem argumento  
**Retorno:** Aplicados/total e pendentes.  
**Reversão/leitura:** Só leitura.  

**Limites:** Não representa ganho de FPS nem nota do PC.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/otimizacoes.js:518`, `src/otimizacoes.js:706`, `src/dash-v3.js:145`.

#### 081. Recomendações do painel

ID da análise: `extra:dashboard-recs`.

**Para que serve:** Mostra pendências, inicialização e temporários.  
**Funcionamento:** Leva ao ajuste pertinente; temporários entram no texto a partir de 100 MiB.

**Entrada:** `get-recommendations` sem argumento  
**Retorno:** {startup,temp,...}.  
**Reversão/leitura:** Só leitura.  

**Limites:** Quantidade depende do hardware/estado.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:1826`, `src/dash-v3.js:171`.

#### 082. Otimização Rápida

ID da análise: `extra:quick-opt`.

**Para que serve:** Aciona a combinação global.  
**Funcionamento:** ACTIONS combina Alto Desempenho salvo X3D dual, GAME_MODE_ON, DVR_OFF, MOUSE_ACCEL_OFF e SAFE_TEMP_CLEAN. Não aplica todos os recomendados.

**Entrada:** `run-action` `{"action":"Otimização Rápida"}`  
**Retorno:** {success,message?,error?}.  
**Reversão/leitura:** Reverter configurações respaldadas; temporários excluídos não voltam.  

**Limites:** Timestamp registra resposta de sucesso, não medição independente.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:1946`, `deep/backend-focused.js:1862`.

### Operação do catálogo

#### 083. Aplicar recomendados da página

ID da análise: `extra:apply-page-recs`.

**Para que serve:** Aplica interruptores recomendados desligados, um por vez.  
**Funcionamento:** Exclui bloqueados/ocupados e executa sequencialmente.

**Entrada:** `tweak-set` `{"id":"item.id","on":true}`; `run-action` `{"action":"item.act"}`  
**Retorno:** Confirma lista; conta sucessos e reinícios.  
**Reversão/leitura:** Desligar cada interruptor.  

**Limites:** Falha em parte da lista pode deixar outros aplicados.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/otimizacoes.js:636`.

### Painel

#### 084. Reverter Tudo

ID da análise: `extra:restore-all`.

**Para que serve:** Solicita recuperar ações com backup.  
**Funcionamento:** Pede restauração após confirmação.

**Entrada:** `get-backups` sem argumento; `restore-all` sem argumento  
**Retorno:** {success,...}; relê lista.  
**Reversão/leitura:** É a própria recuperação.  

**Limites:** Não desfaz exclusões/desinstalações nem toda mudança do sistema.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:1565`, `src/dashboard.html:1578`.

#### 085. Atividades recentes

ID da análise: `extra:recent-history`.

**Para que serve:** Lista até cinco ações recentes.  
**Funcionamento:** addRecent elimina repetição e permite voltar ao contexto.

**Entrada:** sem IPC; operação local/visual.  
**Retorno:** Rótulos, destinos e timestamp local.  
**Reversão/leitura:** Preferência/histórico de UI.  

**Limites:** Não é log forense de execução.

**Evidência:** comprovado-no-renderer; efeito-local/visual. Fontes: `src/dashboard.html:1769`, `src/dash-v3.js:224`.

### Inicialização

#### 086. Listar inicialização

ID da análise: `extra:startup-list`.

**Para que serve:** Mostra nome, comando, escopo e estado.  
**Funcionamento:** Conta entradas habilitadas.

**Entrada:** `startup-list` sem argumento  
**Retorno:** {success,items:[{name,scope,cmd,enabled}],error?}.  
**Reversão/leitura:** Só leitura.  

**Limites:** Vazio não prova ausência de outros mecanismos de autostart.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:1860`.

#### 087. Ativar/desativar início de aplicativo

ID da análise: `extra:startup-toggle`.

**Para que serve:** Controla entrada identificada por nome e escopo.  
**Funcionamento:** Atualiza estado e recomendações após sucesso.

**Entrada:** `startup-set` `{"name":"item.name","scope":"item.scope","enabled":"boolean"}`  
**Retorno:** {success,error?}.  
**Reversão/leitura:** Mesmo interruptor.  

**Limites:** Não encerra nem desinstala o programa.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:1874`.

### Memória e processos

#### 088. Uso de RAM, cache e pentes

ID da análise: `extra:ram-usage`.

**Para que serve:** Mostra uso/cache/livre e slots, modelos, capacidade, geração e MT/s.  
**Funcionamento:** Todos os pentes recebem o MESMO percentual global; chips animados refletem esse uso.

**Entrada:** `get-mem-stats` sem argumento  
**Retorno:** {total,used,pct,cache?,sticks:[...]}.  
**Reversão/leitura:** Só leitura.  

**Limites:** Não mede ocupação por pente/chip; sensor ausente não é zero.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:2174`, `src/memoria.js:19`, `src/memoria.js:87`, `src/memoria.js:110`.

#### 089. Limpar cache da RAM agora

ID da análise: `extra:ram-clean`.

**Para que serve:** Pede esvaziamento do cache e atualiza status.  
**Funcionamento:** Desenha animação durante IPC; exibe memória liberada informada pelo backend.

**Entrada:** `ram-clean` sem argumento; `ram-status` sem argumento  
**Retorno:** {success,message?,error?}; lastAt,lastFreedMB,lastAuto.  
**Reversão/leitura:** Cache pode ser reconstruído; não há desfazer.  

**Limites:** Não adiciona RAM nem prova ganho; null em lastFreedMB significa não medido.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:2189`, `src/dashboard.html:2206`.

#### 090. Limpeza automática da RAM

ID da análise: `extra:ram-auto`.

**Para que serve:** Liga agenda e define intervalo.  
**Funcionamento:** Atualiza próxima execução e última limpeza.

**Entrada:** `ram-auto-set` `{"on":"boolean","minutes":"opcional; intervals"}`; `ram-status` sem argumento  
**Retorno:** {auto,minutes,intervals,nextAt,lastAt,...}.  
**Reversão/leitura:** Desligar interruptor.  

**Limites:** Intervals e política real vêm do backend.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:2189`, `src/dashboard.html:2218`.

### Mouse e teclado

#### 091. Teste de polling do mouse

ID da análise: `extra:mouse-polling`.

**Para que serve:** Estima taxa observada durante cinco segundos de movimento.  
**Funcionamento:** pointerrawupdate ou pointermove+getCoalescedEvents; ignora gaps <0,05 e >=20 ms; exige 50 gaps; Hz=1000/mediana. Nominal: taxa padrão dentro de 15%; stable: gaps dentro de ±25% da mediana; late: >1,8×mediana.

**Opções:** `{"rates": [125, 250, 500, 1000, 2000, 4000, 8000], "verdict": ">=85% estável; >=60% oscilando; abaixo instável"}`.

**Entrada:** sem IPC; operação local/visual.  
**Retorno:** {hz,nominal,stable,late,samples,at}; seis resultados em localStorage.  
**Reversão/leitura:** Só observação; limpar histórico apaga localmente.  

**Limites:** Entrega de eventos ao Chromium não é medição elétrica USB nem latência clique→tela.

**Evidência:** comprovado-no-renderer; efeito-local/visual. Fontes: `src/mouse-test.js:7`, `src/mouse-test.js:44`, `src/mouse-test.js:111`.

### Limpeza e disco

#### 092. Analisar o que pode ser limpo

ID da análise: `extra:clean-scan`.

**Para que serve:** Lê categorias Windows/navegadores/apps e tamanhos.  
**Funcionamento:** Primeira leitura usa seleção padrão; seguintes preservam escolhas ainda utilizáveis.

**Entrada:** `clean-scan` sem argumento  
**Retorno:** {success,cats:[{id,group,name,desc,on,bytes,files,open,app,note}],error?}.  
**Reversão/leitura:** Só leitura.  

**Limites:** IDs/caminhos reais vêm do backend; app aberto e zero bytes desabilitam seleção.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/limpeza.js:43`, `src/limpeza.js:52`, `src/limpeza.js:94`.

#### 093. Limpar categorias selecionadas

ID da análise: `extra:clean-run`.

**Para que serve:** Exclui somente categorias selecionadas utilizáveis.  
**Funcionamento:** Soma liberado e avisa arquivos em uso; recycle exige confirmação.

**Entrada:** `clean-run` `{"ids":"array de cats[].id"}`  
**Retorno:** {success,freed,results:[{id,freed,failed,open}],error?}.  
**Reversão/leitura:** Sem desfazer no fluxo.  

**Limites:** Lixeira esvaziada perde recuperação por este fluxo; promessa de preservar logins/histórico depende dos caminhos reais.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/limpeza.js:124`, `src/limpeza.js:138`.

#### 094. Discos e espaço livre

ID da análise: `extra:disk-drives`.

**Para que serve:** Seleciona unidade, por padrão a do sistema.  
**Funcionamento:** Desenha capacidade/livre e escolhe unidade.

**Entrada:** `disk-drives` sem argumento  
**Retorno:** {success,drives:[{name,total,free}],system,error?}.  
**Reversão/leitura:** Só leitura.  

**Limites:** Formatação usa múltiplos de 1024.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/limpeza.js:185`, `src/limpeza.js:194`.

#### 095. Analisar ocupação do disco

ID da análise: `extra:disk-scan`.

**Para que serve:** Varre unidade em segundo plano e acompanha progresso.  
**Funcionamento:** Polling 700 ms; árvore por ID/pai, filhos ordenados pelo tamanho.

**Entrada:** `disk-scan` `{"drive":"unidade"}`; `disk-scan-status` sem argumento  
**Retorno:** {state,drive,files,bytes,result:{nodes,files,scanned,at},error?}.  
**Reversão/leitura:** Só leitura.  

**Limites:** Barra usa bytes vistos/espaço ocupado e limita 99%; não é tempo restante exato.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/limpeza.js:213`, `src/limpeza.js:222`, `src/limpeza.js:235`, `src/limpeza.js:266`.

#### 096. Cancelar leitura do disco

ID da análise: `extra:disk-cancel`.

**Para que serve:** Pede parar e consulta último estado.  
**Funcionamento:** Interrompe consulta, não apaga arquivos.

**Entrada:** `disk-scan-cancel` sem argumento; `disk-scan-status` sem argumento  
**Retorno:** Estado final/parcial.  
**Reversão/leitura:** Só leitura.  

**Limites:** Pode permanecer resultado anterior com timestamp.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/limpeza.js:252`.

#### 097. Pastas, maiores arquivos e Explorador

ID da análise: `extra:disk-browse`.

**Para que serve:** Navega ocupação e abre item no Explorador.  
**Funcionamento:** Pastas/Maiores arquivos, breadcrumb e dicas para caminhos conhecidos.

**Entrada:** `disk-open` `{"target":"item.path"}`  
**Retorno:** {success,error?}.  
**Reversão/leitura:** Só navegação.  

**Limites:** Não existe botão de exclusão na árvore; WinSxS pode conter hardlinks.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/limpeza.js:169`, `src/limpeza.js:304`, `src/limpeza.js:354`, `src/limpeza.js:389`.

### Programas

#### 098. Listar/buscar/ordenar programas

ID da análise: `extra:apps-list`.

**Para que serve:** Mostra fabricante, versão, data, ícone e tamanho.  
**Funcionamento:** Carrega lista antes de extras; busca sem acentos; ordena nome/tamanho/data.

**Entrada:** `apps-list` sem argumento; `apps-extra` sem argumento  
**Retorno:** {success,apps:[{key,name,pub,ver,date,bytes,noRemove}]}; extras {sizes,icons}.  
**Reversão/leitura:** Só leitura.  

**Limites:** Tamanho desconhecido aparece como traço; inventário limitado à leitura do backend.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/programas.js:31`, `src/programas.js:46`, `src/programas.js:59`.

#### 099. Desinstalar programa

ID da análise: `extra:apps-uninstall`.

**Para que serve:** Abre o desinstalador original e aguarda término.  
**Funcionamento:** Consulta a cada 2,5 s por até 30 min; Já terminei inicia busca de sobras; broken oferece busca direta.

**Entrada:** `apps-uninstall` `{"key":"app.key"}`; `apps-exists` `{"key":"app.key"}`  
**Retorno:** {success,broken?,error?}; {exists}.  
**Reversão/leitura:** Reinstalar, se necessário.  

**Limites:** Cancelar diálogo GDZ não garante cancelar desinstalador externo; dados podem ser perdidos.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/programas.js:124`, `src/programas.js:141`.

#### 100. Procurar sobras

ID da análise: `extra:apps-leftovers`.

**Para que serve:** Mostra diretórios/chaves deixados após desinstalar.  
**Funcionamento:** Se ainda instalado pede concluir; sobras começam selecionadas, mas são revisáveis.

**Entrada:** `apps-leftovers` `{"key":"app.key"}`  
**Retorno:** {success,stillInstalled,items:[{id,path,label,kind,bytes}],error?}.  
**Reversão/leitura:** Só leitura.  

**Limites:** Para preservar configurações, desmarcar Dados e configurações segundo a UI.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/programas.js:157`, `src/programas.js:183`.

#### 101. Remover sobras selecionadas

ID da análise: `extra:apps-remove-leftovers`.

**Para que serve:** Exclui IDs aprovados na seleção.  
**Funcionamento:** Contrato manda pastas à Lixeira e chaves de Registro para backup em desinstalacoes; mostra falhas.

**Entrada:** `apps-remove-leftovers` `{"key":"app.key","ids":"IDs selecionados"}`  
**Retorno:** {success,trashed,bytes,regs,failed[],error?}.  
**Reversão/leitura:** Recuperação depende da Lixeira/backup; não há restaurador próprio nesta tela.  

**Limites:** Pode remover dados/configurações desejados para reinstalação.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/programas.js:205`, `src/programas.js:212`.

### Jogos e FiveM

#### 102. Detectar jogos instalados/abertos

ID da análise: `extra:games-scan`.

**Para que serve:** Exibe só encontrados/rodando com capas.  
**Funcionamento:** Revarre bibliotecas; status a cada 5 s na página aberta.

**Entrada:** `games-scan` `{"force":"boolean"}`; `games-status` sem argumento  
**Retorno:** {games,autoMode,autoGame}; {running,...}.  
**Reversão/leitura:** Só leitura.  

**Limites:** Não comprova detectar qualquer jogo. Capas podem vir de CDN.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:2614`, `src/dashboard.html:2720`, `src/dashboard.html:2760`.

#### 103. Aplicar/desfazer perfil de jogo

ID da análise: `extra:games-profile`.

**Para que serve:** Pede perfil pelo nome associado ao cartão.  
**Funcionamento:** Nome é dinâmico; conteúdo do perfil depende do mapeamento backend.

**Entrada:** `run-action` `{"action":"Otimizar <g.opt>"}`; `revert-action` `{"action":"Otimizar <g.opt>"}`  
**Retorno:** {success,message?,error?}.  
**Reversão/leitura:** Botão ↩ do cartão.  

**Limites:** Cartão não prova perfil exclusivo nem ganho validado.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:2689`, `src/dashboard.html:1899`, `src/dashboard.html:1931`.

#### 104. Preferir GPU dedicada

ID da análise: `extra:games-gpu`.

**Para que serve:** Mostra ação quando info.gpu existe.  
**Funcionamento:** Solicita preferência de GPU para executáveis desse ID.

**Entrada:** `run-action` `{"action":"GPU Dedicada: <info.id>"}`; `revert-action` `{"action":"GPU Dedicada: <info.id>"}`  
**Retorno:** {success,message?,error?}.  
**Reversão/leitura:** Botão ↩.  

**Limites:** Não cria GPU dedicada nem garante seleção de hardware pelo driver.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:2710`.

#### 105. Modo automático por jogo

ID da análise: `extra:games-auto`.

**Para que serve:** Liga ajustes ao detectar jogo aberto.  
**Funcionamento:** Atualiza toggle/badge; backend controla ciclo.

**Entrada:** `set-auto-mode` `{"on":"boolean"}`; `games-status` sem argumento  
**Retorno:** {autoMode,autoGame,running,...}.  
**Reversão/leitura:** Desligar modo automático.  

**Limites:** Reversão ao fechar jogo/sair do GDZ exige conferir backend.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:2743`, `src/dashboard.html:2752`.

#### 106. Limpar cache de jogo

ID da análise: `extra:game-cache`.

**Para que serve:** Pede limpeza para ID do cartão.  
**Funcionamento:** FiveM cita cache/server-cache/server-cache-priv; outros cartões cache de shaders Steam.

**Entrada:** `game-clean` `{"id":"info.id"}`  
**Retorno:** {success,message?,error?}.  
**Reversão/leitura:** Cache tende a baixar/recriar.  

**Limites:** Não deve remover GTA segundo texto; confirmar diretórios no backend.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:2711`, `src/dashboard.html:2781`, `src/dashboard.html:2790`.

#### 107. Limpar shaders NVIDIA/AMD/DirectX

ID da análise: `extra:shader-clean`.

**Para que serve:** Exclui caches globais após confirmação.  
**Funcionamento:** Solicita limpeza de caches conhecidos.

**Entrada:** `shader-clean` sem argumento  
**Retorno:** {success,message?,error?}.  
**Reversão/leitura:** Shaders serão recompilados.  

**Limites:** Primeiro carregamento pode ficar mais lento, conforme diálogo.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:2791`.

#### 108. Ler gráficos do FiveM

ID da análise: `extra:fivem-status`.

**Para que serve:** Mostra MSAA, grama, distância, resolução interna e modo de janela.  
**Funcionamento:** Oculta se não instalado/sem arquivo; bloqueia aplicar enquanto running.

**Entrada:** `fivem-gfx-status` sem argumento  
**Retorno:** {success,installed,file,values,preset,running,backup,gpu}.  
**Reversão/leitura:** Só leitura.  

**Limites:** Afirmações sobre Enhanced e anti-cheat são conteúdo do produto, não verificadas externamente.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/gtarp.js:26`, `src/gtarp.js:71`.

#### 109. Preset, Frame Scaling e janela do FiveM

ID da análise: `extra:fivem-apply`.

**Para que serve:** Configura arquivo de gráficos do GTA/FiveM.  
**Funcionamento:** FPS máximo ou Equilibrado; Manter envia null. Scaling 0=Nativo,1=83%,2=75%,3=67%,4=50%. borderless solicita sem bordas.

**Opções:** `{"samplingMode_to_index": {"0": 0, "5": 0, "4": 1, "3": 2, "2": 3, "1": 4}}`.

**Entrada:** `fivem-gfx-apply` `{"preset":"fps|balanced|null","scaling":"índice 0..4","borderless":"boolean"}`  
**Retorno:** {success,error?}.  
**Reversão/leitura:** fivem-gfx-restore com backup.  

**Limites:** Menor resolução reduz nitidez; FiveM pode regravar config. A UI bloqueia com jogo aberto.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/gtarp.js:6`, `src/gtarp.js:35`, `src/gtarp.js:94`.

#### 110. Restaurar gráficos originais

ID da análise: `extra:fivem-restore`.

**Para que serve:** Volta à configuração anterior ao GDZ.  
**Funcionamento:** Confirma intenção; só aparece se backup informado.

**Entrada:** `fivem-gfx-restore` sem argumento  
**Retorno:** {success,error?}.  
**Reversão/leitura:** É a recuperação.  

**Limites:** Fidelidade/escopo do backup dependem do handler.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/gtarp.js:103`.

#### 111. Abrir painel GPU e dicas NIS/AFMF

ID da análise: `extra:gpu-panel-help`.

**Para que serve:** Apresenta instruções específicas por fabricante.  
**Funcionamento:** Apenas abre painel; ativar AFMF/NIS é procedimento manual.

**Entrada:** `open-gpu-panel` `{"vendor":"amd|nvidia"}`  
**Retorno:** {success,error?}.  
**Reversão/leitura:** Desfazer no próprio painel.  

**Limites:** Não liga AFMF/NIS por esse IPC; ganho/segurança citados não foram verificados.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/gtarp.js:12`, `src/gtarp.js:122`.

### FPS

#### 112. FPS/frametime/1% low ao vivo

ID da análise: `extra:fps-live`.

**Para que serve:** Mostra números retornados e escolhe processo automático/PID.  
**Funcionamento:** Polling 1 s quando página FPS ativa; automático envia pid:null.

**Entrada:** `fps-status` sem argumento; `fps-set-target` `{"pid":"number|null"}`  
**Retorno:** {success,live:{fps,frametime,low1,name},processes,pinned,sessions,recording,overlay,base,hotkey}.  
**Reversão/leitura:** Só leitura.  

**Limites:** Renderer só desenha os números; atribui medição a PresentMon no texto, cuja coleta fica no backend.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/fps.js:1`, `src/fps.js:73`, `src/fps.js:162`, `src/fps.js:237`.

#### 113. Marcar/limpar base FPS

ID da análise: `extra:fps-baseline`.

**Para que serve:** Compara FPS atual com base do mesmo jogo.  
**Funcionamento:** Mostra ganho percentual e média atual de três segundos; diferença <3% tratada como igual.

**Entrada:** `fps-base-mark` sem argumento; `fps-base-clear` sem argumento  
**Retorno:** {success,base:{fps,name}}; live.base contém gain/now.  
**Reversão/leitura:** Limpar remove base.  

**Limites:** Não liga DLSS/FSR; cena/carga precisam ser comparáveis para atribuir causa.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/fps.js:85`, `src/fps.js:195`.

#### 114. Sessões antes/depois

ID da análise: `extra:fps-record`.

**Para que serve:** Grava números por 15/30/60 segundos em a ou b.  
**Funcionamento:** Resposta aguarda término; progresso pelo polling; mostra média/1%/0,1% low e frametime.

**Entrada:** `fps-record` `{"slot":"a|b","secs":"15|30|60","label":"até 40 caracteres"}`; `fps-clear` `{"slot":"a|b"}`  
**Retorno:** {success,session:{fps,low1,low01,frametime,name,secs,frames,at,label},error?}.  
**Reversão/leitura:** Apagar sessão remove números.  

**Limites:** Não grava vídeo. Cálculo/amostragem pertencem ao backend.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/fps.js:9`, `src/fps.js:101`, `src/fps.js:255`, `src/fps.js:271`.

#### 115. Comparar sessões

ID da análise: `extra:fps-compare`.

**Para que serve:** Calcula diferenças e veredito entre Antes/Depois.  
**Funcionamento:** (depois-antes)/antes×100; frametime menor é melhor; |diferença|<3% é neutro. FPS médio decide veredito e 1% low complementa.

**Entrada:** sem IPC; operação local/visual.  
**Retorno:** Percentuais das quatro métricas e texto.  
**Reversão/leitura:** Só cálculo visual.  

**Limites:** Jogos diferentes exibem aviso, mas tabela é desenhada. Não controla variação de cena/configurações.

**Evidência:** comprovado-no-renderer; efeito-local/visual. Fontes: `src/fps.js:130`, `src/fps.js:147`.

#### 116. FPS na tela

ID da análise: `extra:fps-overlay`.

**Para que serve:** Liga overlay, canto e campos extras.  
**Funcionamento:** Botões enviam patch de uma propriedade; quatro cantos da tela.

**Entrada:** `fps-overlay-set` `{"on":"boolean opcional","pos":"0..3 opcional","show":"{frametime?,low1?,cpu?,cpuTemp?,gpu?,gpuTemp?,ram?,name?}"}`; `evento: overlay-update`  
**Retorno:** {success,overlay}; evento alimenta fps-overlay.html.  
**Reversão/leitura:** Enviar on:false.  

**Limites:** UI exige janela/borderless para overlay externo; temperaturas dependem de sensores.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/fps.js:6`, `src/fps.js:77`, `src/fps.js:249`, `src/fps-overlay.html:81`.

### Gargalo

#### 117. Medir gargalo com jogo

ID da análise: `extra:bottleneck-measure`.

**Para que serve:** Espera jogo, confirma PID e solicita medição.  
**Funcionamento:** Espera até 10 min; Não é esse ignora PID. UI diz contar apenas jogo na tela e pausar em alt-tab. BN_SECONDS=40; procura jogo a cada 3 s.

**Entrada:** `bn-find-game` `{"ignore":"PIDs rejeitados"}`; `bn-notify` `{"name":"game.name"}`; `measure-bottleneck` `{"seconds":40,"pid":"game.pid"}`  
**Retorno:** {success,cpu,gpu,ram,coreMax,ramGb,seconds/samples,closed?,partial?,error?}.  
**Reversão/leitura:** Só consulta; cancelamento invalida fluxo local.  

**Limites:** Coleta/pausa real dependem do backend; resultado é heurístico.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:2389`, `src/dashboard.html:2402`, `src/dashboard.html:2428`, `src/dashboard.html:2444`, `src/dashboard.html:2345`.

#### 118. Classificar gargalo

ID da análise: `extra:bottleneck-classify`.

**Para que serve:** Converte métricas em RAM/GPU/CPU/equilibrado/sem limite.  
**Funcionamento:** Ordem: RAM>=90 → ram; senão GPU>=90 → gpu; senão GPU<40 e coreMax<70 → none; senão coreMax>=90 e GPU<85 → cpu; demais → balanced.

**Entrada:** sem IPC; operação local/visual.  
**Retorno:** ram|gpu|none|cpu|balanced.  
**Reversão/leitura:** Só cálculo; botões de aplicar são separados.  

**Limites:** Limiares fixos; RAM alta tem precedência sobre GPU. CPU usa coreMax, não uso total.

**Evidência:** comprovado-no-renderer; efeito-local/visual. Fontes: `src/dashboard.html:2468`, `src/dashboard.html:2476`.

### Memória e processos

#### 119. Listar processos

ID da análise: `extra:process-list`.

**Para que serve:** Mostra nome, PID e RAM.  
**Funcionamento:** Coluna CPU é literalmente travessão; desenha botão Encerrar.

**Entrada:** `get-processes` sem argumento  
**Retorno:** {success,processes:[{name,pid,ram}],error?}.  
**Reversão/leitura:** Só leitura.  

**Limites:** Não calcula CPU por processo nesse trecho; mensagem de administrador não demonstra causa da lista vazia.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:2572`, `src/dashboard.html:2575`.

#### 120. Encerrar processo

ID da análise: `extra:process-kill`.

**Para que serve:** Finaliza PID/nome escolhido.  
**Funcionamento:** Remove linha apenas após success.

**Entrada:** `kill-process` `{"pid":"number","name":"string"}`  
**Retorno:** {success,error?}.  
**Reversão/leitura:** Reabrir programa; dados não salvos não têm recuperação garantida.  

**Limites:** Proteção/validação de identidade do processo dependem do backend.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:2595`.

### Informações do PC

#### 121. Informações completas do PC

ID da análise: `extra:pcinfo-static`.

**Para que serve:** Modo simples/avançado: CPU, placa/BIOS, RAM, vídeo/telas, discos, sistema/rede/bateria e sensores.  
**Funcionamento:** Atualizar força leitura; omite ausências e guarda modo em gdz-pcinfo-mode.

**Entrada:** `pcinfo-get` `{"force":"boolean"}`  
**Retorno:** {success,info:{cpu,board,gpus,displays,disks,vols,win,nets,battery,...},error?}.  
**Reversão/leitura:** Só leitura.  

**Limites:** Nem todos sensores existem. Texto declara ausência de driver kernel e de CL/tensões/algumas temperaturas.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/pcinfo.js:231`, `src/pcinfo.js:381`, `src/pcinfo.js:387`.

#### 122. Sensores, mínimos e máximos

ID da análise: `extra:pcinfo-live`.

**Para que serve:** Acompanha valores disponíveis e extremos durante consulta.  
**Funcionamento:** Polling 2 s com página ativa; Zerar limpa st.range local.

**Entrada:** `pcinfo-live` sem argumento  
**Retorno:** {success,...} para CPU/GPU/RAM/discos.  
**Reversão/leitura:** Só leitura.  

**Limites:** Quatro leituras de aquecimento antes de considerar sensor ausente; ausência não é zero. Extremos são amostrados.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/pcinfo.js:10`, `src/pcinfo.js:127`, `src/pcinfo.js:399`, `src/pcinfo.js:417`.

#### 123. Copiar relatório do PC

ID da análise: `extra:pcinfo-report`.

**Para que serve:** Gera texto das abas/campos preenchidos.  
**Funcionamento:** report percorre tabsData; botão usa navigator.clipboard.

**Entrada:** sem IPC; operação local/visual.  
**Retorno:** Texto com data, hardware e sensores; clipboard.  
**Reversão/leitura:** Só geração/cópia.  

**Limites:** Não envia automaticamente a alguém; pode incluir configuração detalhada do hardware.

**Evidência:** comprovado-no-renderer; efeito-local/visual. Fontes: `src/pcinfo.js:336`, `src/pcinfo.js:428`.

### OptiScaler

#### 124. Pacote OptiScaler e instalações

ID da análise: `extra:opti-status`.

**Para que serve:** Mostra pacote, progresso, jogos instalados.  
**Funcionamento:** Download a pedido; polling 700 ms enquanto baixa.

**Entrada:** `opti-status` sem argumento; `opti-download` sem argumento  
**Retorno:** {pkg:{tag},dl:{running,pct,error},installs,dlls,nvidia,...}.  
**Reversão/leitura:** Remover instalação é separado.  

**Limites:** Texto diz origem oficial e ~55 MB; rota/validação real precisam ser conferidas no backend.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/opti.js:121`, `src/opti.js:158`, `src/opti.js:237`.

#### 125. Detectar jogo/selecionar executável

ID da análise: `extra:opti-detect-browse`.

**Para que serve:** Analisa upscalers e bloqueios antes de instalar.  
**Funcionamento:** Cria rascunho default; FiveM por caminho é redirecionado ao cartão GTA.

**Entrada:** `opti-detect` sem argumento; `opti-browse` sem argumento; `opti-analyze` `{"exe":"caminho exe"}`  
**Retorno:** {success,exe,upscalers:{dlss,fsr,xess},notes,block,foreign,installed,antiCheat}.  
**Reversão/leitura:** Só análise até Instalar.  

**Limites:** Busca de arquivos pode não detectar upscaler embutido no exe; detectar não prova compatibilidade.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/opti.js:38`, `src/opti.js:98`, `src/opti.js:113`, `src/opti.js:167`.

#### 126. Instalar OptiScaler

ID da análise: `extra:opti-install`.

**Para que serve:** Instala DLL/camada de substituição de upscaler e opções.  
**Funcionamento:** Baixa se falta pacote; block/foreign/installed escondem instalação; checkbox de anti-cheat requerido na UI.

**Entrada:** `opti-install` `{"exe":"draft.exe","dll":"nome escolhido","opts":"{upscaler,fg,spoof,fps,menu,menuKey}","acceptAntiCheat":"boolean"}`  
**Retorno:** {success,error?}.  
**Reversão/leitura:** opti-uninstall; promessa de backup dos originais.  

**Limites:** DLL no jogo pode causar incompatibilidade/ban; não adiciona suporte universal sem integração compatível.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/opti.js:38`, `src/opti.js:174`.

#### 127. Remover/atualizar OptiScaler

ID da análise: `extra:opti-remove-update`.

**Para que serve:** Remove com restauração; atualiza removendo e reinstalando.  
**Funcionamento:** Atualizar se tag difere; reaproveita opções e pede alerta anti-cheat quando detectado.

**Entrada:** `opti-uninstall` `{"dir":"game.dir"}`; `opti-analyze` `{"exe":"game.exe"}`; `opti-install` `{"exe":"game.exe","dll":"game.dll","opts":"game.opts","acceptAntiCheat":true}`  
**Retorno:** {success,error?}.  
**Reversão/leitura:** Remoção recupera originais conforme backup.  

**Limites:** Não atômico: se reinstalar falhar após remover, fica sem OptiScaler.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/opti.js:201`, `src/opti.js:210`.

#### 128. Abrir pasta do jogo

ID da análise: `extra:opti-folder`.

**Para que serve:** Abre diretório instalado.  
**Funcionamento:** Solicita abrir no Explorador.

**Entrada:** `opti-open-folder` `{"dir":"game.dir"}`  
**Retorno:** Frontend não trata retorno no clique.  
**Reversão/leitura:** Só navegação.  

**Limites:** Não modifica configuração neste botão.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/opti.js:245`.

#### 129. Upscaler (OptiScaler)

ID da análise: `extra:opti-option-upscaler`.

**Para que serve:** Altera rascunho antes da instalação ou salva instalação existente.  
**Funcionamento:** Troca tecnologia que roda no lugar do upscaler escolhido no jogo; DLSS desabilitado sem flag nvidia.

**Opções:** `auto|fsr|xess|dlss`.

**Entrada:** `opti-config` `{"dir":"game.dir","opts":{"upscaler":"valor escolhido"}}`  
**Retorno:** {success,opts,running,error?}; rascunho local só enviado ao instalar.  
**Reversão/leitura:** Mudar opção/remover OptiScaler.  

**Limites:** Compatibilidade/versão FSR real depende de GPU/jogo/pacote.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/opti.js:12`, `src/opti.js:16`, `src/opti.js:190`.

#### 130. Frame Generation (OptiScaler)

ID da análise: `extra:opti-option-fg`.

**Para que serve:** Altera rascunho antes da instalação ou salva instalação existente.  
**Funcionamento:** Desligado, trocar DLSS-FG por FSR FG ou OptiFG experimental DX12.

**Opções:** `off|dlssg|optifg`.

**Entrada:** `opti-config` `{"dir":"game.dir","opts":{"fg":"valor escolhido"}}`  
**Retorno:** {success,opts,running,error?}; rascunho local só enviado ao instalar.  
**Reversão/leitura:** Mudar opção/remover OptiScaler.  

**Limites:** Aumenta FPS mostrado, mas a própria UI diz não diminuir input lag.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/opti.js:12`, `src/opti.js:16`, `src/opti.js:190`.

#### 131. Liberar opção DLSS (OptiScaler)

ID da análise: `extra:opti-option-spoof`.

**Para que serve:** Altera rascunho antes da instalação ou salva instalação existente.  
**Funcionamento:** Apresenta GPU NVIDIA para abrir DLSS no menu e trocar por FSR/XeSS; só aparece sem NVIDIA.

**Opções:** `boolean`.

**Entrada:** `opti-config` `{"dir":"game.dir","opts":{"spoof":"valor escolhido"}}`  
**Retorno:** {success,opts,running,error?}; rascunho local só enviado ao instalar.  
**Reversão/leitura:** Mudar opção/remover OptiScaler.  

**Limites:** Pode mudar detecção do hardware e compatibilidade.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/opti.js:12`, `src/opti.js:16`, `src/opti.js:190`.

#### 132. Contador interno (OptiScaler)

ID da análise: `extra:opti-option-fps`.

**Para que serve:** Altera rascunho antes da instalação ou salva instalação existente.  
**Funcionamento:** Contador OptiScaler alternável Page Up; diferente do overlay externo GDZ.

**Opções:** `boolean`.

**Entrada:** `opti-config` `{"dir":"game.dir","opts":{"fps":"valor escolhido"}}`  
**Retorno:** {success,opts,running,error?}; rascunho local só enviado ao instalar.  
**Reversão/leitura:** Mudar opção/remover OptiScaler.  

**Limites:** Tela cheia exclusiva é promessa do pacote; renderer não implementa contador.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/opti.js:12`, `src/opti.js:16`, `src/opti.js:190`.

#### 133. Menu no jogo (OptiScaler)

ID da análise: `extra:opti-option-menu`.

**Para que serve:** Altera rascunho antes da instalação ou salva instalação existente.  
**Funcionamento:** Liga ajuste fino com jogo aberto.

**Opções:** `boolean`.

**Entrada:** `opti-config` `{"dir":"game.dir","opts":{"menu":"valor escolhido"}}`  
**Retorno:** {success,opts,running,error?}; rascunho local só enviado ao instalar.  
**Reversão/leitura:** Mudar opção/remover OptiScaler.  

**Limites:** Não é tutorial GDZ.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/opti.js:12`, `src/opti.js:16`, `src/opti.js:190`.

#### 134. Tecla do menu (OptiScaler)

ID da análise: `extra:opti-option-menuKey`.

**Para que serve:** Altera rascunho antes da instalação ou salva instalação existente.  
**Funcionamento:** Define tecla Home/Insert/End para menu.

**Opções:** `home|insert|end`.

**Entrada:** `opti-config` `{"dir":"game.dir","opts":{"menuKey":"valor escolhido"}}`  
**Retorno:** {success,opts,running,error?}; rascunho local só enviado ao instalar.  
**Reversão/leitura:** Mudar opção/remover OptiScaler.  

**Limites:** Pode conflitar com jogo.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/opti.js:12`, `src/opti.js:16`, `src/opti.js:190`.

#### 135. Nome da DLL (OptiScaler)

ID da análise: `extra:opti-option-dll`.

**Para que serve:** Altera rascunho antes da instalação ou salva instalação existente.  
**Funcionamento:** Nome de carregamento usado ao instalar; escolhas vêm do backend.

**Opções:** `dxgi.dll|winmm.dll|version.dll|dbghelp.dll|d3d12.dll|wininet.dll|winhttp.dll`.

**Entrada:** `opti-install` `{"exe":"...","dll":"nome","opts":"...","acceptAntiCheat":"..."}`  
**Retorno:** {success,opts,running,error?}; rascunho local só enviado ao instalar.  
**Reversão/leitura:** Mudar opção/remover OptiScaler.  

**Limites:** Não garante compatibilidade; para trocar nome a UI orienta remover/reinstalar.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/opti.js:12`, `src/opti.js:16`, `src/opti.js:190`.

### Interface global

#### 136. Busca Ctrl+K

ID da análise: `extra:search`.

**Para que serve:** Busca páginas e ajustes por texto/sinônimos.  
**Funcionamento:** Ignora acentos; toda palavra precisa casar. Pontuação título começo=30, interior=18, tags/sinônimos=12, descrição=5; página +3. Selecionar navega/destaca.

**Entrada:** sem IPC; operação local/visual.  
**Retorno:** Até oito sugestões; estado aplicado/recomendado/bloqueado.  
**Reversão/leitura:** Só navegação.  

**Limites:** Não aplica ajustes ao selecionar resultado.

**Evidência:** comprovado-no-renderer; efeito-local/visual. Fontes: `src/busca.js:15`, `src/busca.js:74`, `src/busca.js:90`, `src/busca.js:232`.

#### 137. Tema claro/escuro do GDZ

ID da análise: `extra:theme`.

**Para que serve:** Altera aparência local.  
**Funcionamento:** Head lê tema antes de desenhar; botão alterna light/dark.

**Entrada:** sem IPC; operação local/visual.  
**Retorno:** data-theme e localStorage gdz-theme.  
**Reversão/leitura:** Mesmo botão.  

**Limites:** Diferente do tweak dark do Windows.

**Evidência:** comprovado-no-renderer; efeito-local/visual. Fontes: `src/dashboard.html:8`, `src/dashboard.html:11`.

### Sobre e preferências

#### 138. Efeitos visuais do GDZ

ID da análise: `extra:visual-effects`.

**Para que serve:** Controla circuito/luz do cursor/giroscópio da UI.  
**Funcionamento:** Desliga animações; foco/visibilidade pausam quando ausente; CPU suavizada modula sinais.

**Entrada:** sem IPC; operação local/visual.  
**Retorno:** data-fx=off e gdz-efeitos em localStorage.  
**Reversão/leitura:** Ligar novamente.  

**Limites:** Decoração, sem otimizar Windows; pulsos não medem chips reais.

**Evidência:** comprovado-no-renderer; efeito-local/visual. Fontes: `src/fundo.js:262`, `src/fundo.js:289`, `src/fundo.js:305`.

### Interface global

#### 139. Sons da interface

ID da análise: `extra:sounds`.

**Para que serve:** Gera sons de clique, abas, sliders, diálogo e resultado.  
**Funcionamento:** Onze sons toque/forte/aba/passo/liga/desliga/abre/fecha/certo/aviso/erro; após gesto, janela visível, repetição mínima 40 ms.

**Entrada:** sem IPC; operação local/visual.  
**Retorno:** Web Audio local e preferência gdz-sons.  
**Reversão/leitura:** Botão áudio.  

**Limites:** Som de sucesso classifica texto toast por regex/DOM; não valida operação no Windows.

**Evidência:** comprovado-no-renderer; efeito-local/visual. Fontes: `src/sons.js:5`, `src/sons.js:57`, `src/sons.js:77`, `src/sons.js:161`, `src/sons.js:186`.

#### 140. Tutorial de primeiros passos/página/completo

ID da análise: `extra:tutorial`.

**Para que serve:** Destaca controles e orienta com teclado.  
**Funcionamento:** Três tours; reposiciona a cada 300 ms, espera avisos e restaura foco ao fechar.

**Entrada:** `get-user` sem argumento  
**Retorno:** Passos do tour; versão vista em gdz-tour.  
**Reversão/leitura:** Fechar tour.  

**Limites:** Não aplica automaticamente otimizações explicadas.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/tour.js:29`, `src/tour.js:443`, `src/tour.js:463`, `src/tour.js:500`.

### Sobre e preferências

#### 141. Plano/acesso a recursos

ID da análise: `extra:plan-access`.

**Para que serve:** Mostra plano, vencimento, offline/PC e restringe navegação.  
**Funcionamento:** Compara features com * ou recurso mapeado; upsell quando falta.

**Entrada:** `get-user` sem argumento; `evento: entitlement-update`  
**Retorno:** user/features e textos.  
**Reversão/leitura:** Não é ajuste Windows.  

**Limites:** Bloqueio visual não comprova autorização no handler.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/plano.js:43`, `src/plano.js:56`, `src/plano.js:390`, `src/plano.js:529`.

#### 142. Consentimento de métricas GDZ

ID da análise: `extra:telemetry-consent`.

**Para que serve:** Controla envio extra das medições FPS/otimizações.  
**Funcionamento:** Pergunta inicial se null; preferência alterável em Sobre.

**Entrada:** `telemetry-consent` sem argumento; `telemetry-consent-set` `{"on":"boolean"}`  
**Retorno:** {consent:null|boolean}.  
**Reversão/leitura:** Desligar consentimento.  

**Limites:** Texto afirma hardware enviado sempre e consentimento só para métricas extras; fluxo real exige backend. Distinto de telemetry do Windows.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/plano.js:454`, `src/plano.js:492`, `src/plano.js:522`.

#### 143. Iniciar GDZ com Windows

ID da análise: `extra:gdz-autostart`.

**Para que serve:** Controla autostart do próprio aplicativo.  
**Funcionamento:** Só mostra linha se supported; confirma estado retornado.

**Entrada:** `autostart-get` sem argumento; `autostart-set` `{"on":"boolean"}`  
**Retorno:** {supported,on}.  
**Reversão/leitura:** Desligar.  

**Limites:** Separado da lista geral de aplicativos; comentário diz instalado, não portátil/dev.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/plano.js:504`, `src/plano.js:513`.

#### 144. Atualização/reiniciar para instalar

ID da análise: `extra:app-updates`.

**Para que serve:** Exibe versão e download, oferece reiniciar.  
**Funcionamento:** Ocultar/Depois adiam UI; instalação reinicia aplicativo.

**Entrada:** `update-status` sem argumento; `evento: update-status`; `update-install` sem argumento  
**Retorno:** {status,current,version,percent}; {success,error?}.  
**Reversão/leitura:** Adiar antes de instalar; sem rollback na UI.  

**Limites:** Versão na UI não prova assinatura; instalação fica no updater/backend.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/plano.js:430`, `src/plano.js:444`, `src/plano.js:525`.

### Interface global

#### 145. Controles de janela

ID da análise: `extra:window-controls`.

**Para que serve:** Minimiza/maximiza/fecha principal e Scaling.  
**Funcionamento:** Envia pedido de janela; duplo clique titlebar maximiza.

**Entrada:** `send: minimize-window`; `send: maximize-window`; `send: close-window`; `send: gs-minimize`; `send: gs-maximize`; `send: gs-close`  
**Retorno:** Sem Promise de resposta.  
**Reversão/leitura:** Reabrir/restaurar conforme operação.  

**Limites:** Fechar janela não demonstra encerrar auxiliares. Backend registra gs-minimize/gs-maximize com corpo vazio; gs-close tenta matar lsProcess. Esses eventos não equivalem aos botões nativos WPF.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:604`, `src/dashboard.html:2823`, `src/gdz-scaling.html:244`, `deep/backend-focused.js:706`.

### Sobre e preferências

#### 146. Sair da conta

ID da análise: `extra:logout`.

**Para que serve:** Envia logout após transição.  
**Funcionamento:** Atraso visual 220 ms antes do send.

**Entrada:** `send: logout`  
**Retorno:** Sem retorno direto.  
**Reversão/leitura:** Novo login.  

**Limites:** Revogação remota/limpeza real não demonstradas neste trecho.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:1621`.

### GDZ Scaling

#### 147. Abrir GDZ Scaling

ID da análise: `extra:scaling-open`.

**Para que serve:** Abre o auxiliar nativo GDZOptimizer.exe, que é uma aplicação WPF separada.  
**Funcionamento:** open-scaling-window chama openScalingWindow: localiza binário, reutiliza/foca se já aberto ou spawn detached do GDZOptimizer.exe; tenta renomear janela para GDZ Scaling.

**Entrada:** `send: open-scaling-window`  
**Retorno:** Sem retorno via send; janela do auxiliar nativo se existir.  
**Reversão/leitura:** Fechar janela; Stop é separado.  

**Limites:** Não carrega src/gdz-scaling.html nesse fluxo. O HTML extraído tem controles próprios, mas sua carga não foi localizada no caminho principal.

**Evidência:** comprovado-estaticamente-no-renderer-e-handler-backend. Fontes: `src/dashboard.html:1697`, `src/dashboard.html:1787`, `src/gdz-scaling.html:248`, `deep/backend-focused.js:392`.

#### 148. Perfil Default — HTML extraído sem carga localizada

ID da análise: `extra:scaling-profile`.

**Para que serve:** Seleção visual de perfil. Este comportamento pertence a src/gdz-scaling.html; o fluxo principal abre o auxiliar WPF.  
**Funcionamento:** selectProfile só altera DOM; só Default declarado.

**Entrada:** sem IPC; operação local/visual.  
**Retorno:** Classe active e pbar-name.  
**Reversão/leitura:** Visual.  

**Limites:** Não lê/grava perfil nem chama IPC/persistência. Não caracteriza automaticamente a UI WPF que o usuário abre pelo menu; análise limitada a este arquivo HTML.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:254`, `src/gdz-scaling.html:514`, `deep/backend-focused.js:392`.

#### 149. Scale e Stop — HTML extraído sem carga localizada

ID da análise: `extra:scaling-start-stop`.

**Para que serve:** Envia opções ao motor e atualiza status. Este comportamento pertence a src/gdz-scaling.html; o fluxo principal abre o auxiliar WPF.  
**Funcionamento:** getConfig serializa campos; sucesso pinta Active. Esta janela não chama ls-config para carregar perfil atual.

**Entrada:** `ls-launch` `"getConfig()"`; `ls-stop` sem argumento  
**Retorno:** {success,error?}.  
**Reversão/leitura:** Stop chama ls-stop.  

**Limites:** Active não prova interpolação. Stop ignora erro e pinta Inactive mesmo assim. Não caracteriza automaticamente a UI WPF que o usuário abre pelo menu; análise limitada a este arquivo HTML.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:558`, `src/gdz-scaling.html:589`, `src/gdz-scaling.html:594`, `src/gdz-scaling.html:614`, `deep/backend-focused.js:392`.

#### 150. Tipo de Frame Generation (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-fgType`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Escolhe FG 3.1/2.3/1.1/desligado; mostra opções.

**Opções:** `lsfg31|lsfg23|lsfg11|off`.

**Entrada:** `ls-launch` `{"fgType":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Rótulo não prova três versões reais do motor. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF. Na ponte, lsfg11 vira LSFG1, porém SCALING_ENUM só aceita Off/LSFG2/LSFG3; LSFG1 é recusado e entra em skipped.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:281`, `src/gdz-scaling.html:558`, `deep/backend-focused.js:445`, `deep/backend-focused.js:519`, `deep/backend-focused.js:619`.

#### 151. Modo FG 3 (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-lsfg3Mode`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Solicita modo adaptativo ou fixo.

**Opções:** `adaptive|fixed`.

**Entrada:** `ls-launch` `{"lsfg3Mode":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Semântica real no motor. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:291`, `src/gdz-scaling.html:558`.

#### 152. Alvo FPS (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-lsfg3Target`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Define alvo de FPS na janela.

**Opções:** `30..300; default 100`.

**Entrada:** `ls-launch` `{"lsfg3Target":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Alvo não é FPS medido nem garantia. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:303`, `src/gdz-scaling.html:558`.

#### 153. Multiplicador fixo (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-lsfg3Multiplier`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** A janela envia sempre 2, sem controle visível.

**Opções:** `2 constante`.

**Entrada:** `ls-launch` `{"lsfg3Multiplier":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Aba antiga possui slider distinto. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:563`, `src/gdz-scaling.html:558`.

#### 154. Modo FG 2 (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-lsfg2Mode`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Fusion é convertido em fusao no payload.

**Opções:** `fusion|normal → fusao|normal`.

**Entrada:** `ls-launch` `{"lsfg2Mode":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Linha visível só lsfg23, não lsfg11. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF. scalingFromUi não lê lsfg2Mode; esta seleção não chega ao XML por essa conversão.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:321`, `src/gdz-scaling.html:558`, `deep/backend-focused.js:445`, `deep/backend-focused.js:519`, `deep/backend-focused.js:619`.

#### 155. Escala de fluxo (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-flowScale`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Envia escala de fluxo ao FG.

**Opções:** `0.5..2.0; passo 0.1; default 1`.

**Entrada:** `ls-launch` `{"flowScale":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Não é diretamente resolução do jogo. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF. scalingFromUi multiplica por 100; scalingWrite limita LSFGFlowScale a 25..100. Valores da UI acima de 1,0 viram no máximo 100.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:314`, `src/gdz-scaling.html:558`, `deep/backend-focused.js:445`, `deep/backend-focused.js:519`, `deep/backend-focused.js:619`.

#### 156. Modo desempenho (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-perfMode`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Solicita variante desempenho do FG.

**Opções:** `boolean`.

**Entrada:** `ls-launch` `{"perfMode":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Nenhum ganho medido pelo renderer. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF. perfMode é convertido em LSFGSize=PERFORMANCE/BALANCED, mas scalingWrite não inclui LSFGSize em nenhum grupo aceito: ignora silenciosamente, sem skipped.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:329`, `src/gdz-scaling.html:558`, `deep/backend-focused.js:445`, `deep/backend-focused.js:519`, `deep/backend-focused.js:619`.

#### 157. Algoritmo de ampliação (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-scaleType`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Escolhe algoritmo solicitado.

**Opções:** `off|ls1|fsr|nis|integer|nearest|xbr|anime4k`.

**Entrada:** `ls-launch` `{"scaleType":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Rótulo FSR/NIS não instala integração temporal nativa. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF. scalingWrite aceita somente Off/LS1/FSR. NIS/INTEGER/NN/XBR/ANIME4K produzidos pela conversão são recusados e entram em skipped.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:339`, `src/gdz-scaling.html:558`, `deep/backend-focused.js:445`, `deep/backend-focused.js:519`, `deep/backend-focused.js:619`.

#### 158. Modo de escala (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-scaleMode`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Escolhe automático/fator customizado.

**Opções:** `auto|custom`.

**Entrada:** `ls-launch` `{"scaleMode":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Só parâmetro até iniciar. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:353`, `src/gdz-scaling.html:558`.

#### 159. Fator de escala (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-scaleFactor`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Define fator customizado.

**Opções:** `1.0..3.0 passo 0.1; default 1.5`.

**Entrada:** `ls-launch` `{"scaleFactor":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Não verifica resolução final/qualidade. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:365`, `src/gdz-scaling.html:558`.

#### 160. Nitidez (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-sharpness`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Envia parâmetro de nitidez.

**Opções:** `0..100; default 0`.

**Entrada:** `ls-launch` `{"sharpness":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Não prova suporte por todo algoritmo. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF. Conversão grava Sharpness após dividir por 10. O auxiliar usa LS1Sharpness quando algoritmo=LS1; essa conversão não modifica LS1Sharpness (ver análise IPC/nativa).

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:376`, `src/gdz-scaling.html:558`, `deep/backend-focused.js:445`, `deep/backend-focused.js:519`, `deep/backend-focused.js:619`.

#### 161. Versão rotulada FSR (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-fsrType`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Subseleção mostrada para tipo fsr.

**Opções:** `fsr1|fsr2|fsr3`.

**Entrada:** `ls-launch` `{"fsrType":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Rótulo não comprova reconstrução temporal/FG dessas versões. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF. fsr1/fsr2/fsr3 viram FSR1/FSR2/FSR3; scalingWrite só aceita ORIGINAL. Todas essas escolhas são recusadas e entram em skipped.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:383`, `src/gdz-scaling.html:558`, `deep/backend-focused.js:445`, `deep/backend-focused.js:519`, `deep/backend-focused.js:619`.

#### 162. Redimensionar antes (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-resizeBefore`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Pede redimensionar janela antes do scaling.

**Opções:** `boolean`.

**Entrada:** `ls-launch` `{"resizeBefore":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Pode alterar tamanho/modo do jogo. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:392`, `src/gdz-scaling.html:558`.

#### 163. API de captura (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-captureApi`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Escolhe captura DXGI/WGC.

**Opções:** `dxgi|wgc`.

**Entrada:** `ls-launch` `{"captureApi":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Requisitos reais Windows/driver no backend. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:407`, `src/gdz-scaling.html:558`.

#### 164. Fila de captura (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-queueTarget`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Define alvo da fila; clampNum limita ao mudar.

**Opções:** `1..4; default 1`.

**Entrada:** `ls-launch` `{"queueTarget":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Aba antiga limita a três; fila pode afetar latência. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF. scalingWrite limita QueueTarget a 0..3; a escolha 4 deste HTML é limitada a 3.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:415`, `src/gdz-scaling.html:558`, `deep/backend-focused.js:445`, `deep/backend-focused.js:519`, `deep/backend-focused.js:619`.

#### 165. Ampliar cursor (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-scaleCursor`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Envia transformação do cursor.

**Opções:** `boolean; default true`.

**Entrada:** `ls-launch` `{"scaleCursor":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Diferente de Clip cursor não enviado. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:426`, `src/gdz-scaling.html:558`.

#### 166. Sincronização (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-syncMode`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Default/V-Sync/G-Sync/FreeSync/Adaptive.

**Opções:** `standard|vsync|gsync|adaptive`.

**Entrada:** `ls-launch` `{"syncMode":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Não garante suporte do monitor/driver. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF. Este HTML envia standard/vsync/gsync/adaptive; conversão usa maiúsculas, mas scalingWrite aceita só OFF/DEFAULT. Estes quatro valores não passam na enumeração.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:436`, `src/gdz-scaling.html:558`, `deep/backend-focused.js:445`, `deep/backend-focused.js:519`, `deep/backend-focused.js:619`.

#### 167. HDR (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-hdrCompat`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Solicita compatibilidade HDR no motor.

**Opções:** `boolean`.

**Entrada:** `ls-launch` `{"hdrCompat":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Não calibra tela nem prova preservar HDR. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:452`, `src/gdz-scaling.html:558`.

#### 168. Versão otimizada (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-optimized`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Solicita variante otimizada.

**Opções:** `boolean; default true`.

**Entrada:** `ls-launch` `{"optimized":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Alteração exata não explicada na UI. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF. scalingFromUi não lê optimized; este booleano não chega ao XML por essa conversão.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:462`, `src/gdz-scaling.html:558`, `deep/backend-focused.js:445`, `deep/backend-focused.js:519`, `deep/backend-focused.js:619`.

#### 169. Variable rate scaling (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-vrs`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Envia taxa variável de scaling.

**Opções:** `boolean`.

**Entrada:** `ls-launch` `{"vrs":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Não equivale automaticamente ao VRS nativo do jogo. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:467`, `src/gdz-scaling.html:558`.

#### 170. Auto Scale (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-autoScale`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Solicita ativação automática.

**Opções:** `boolean`.

**Entrada:** `ls-launch` `{"autoScale":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Foco/retomada são responsabilidade do motor. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:477`, `src/gdz-scaling.html:558`.

#### 171. Começar minimizado (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-startMinimized`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Solicita motor minimizado.

**Opções:** `boolean; default true`.

**Entrada:** `ls-launch` `{"startMinimized":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Não é autostart do GDZ no Windows. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF. scalingFromUi não lê startMinimized e ls-launch não usa esse campo ao spawn; sem efeito demonstrado por essa ponte.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:482`, `src/gdz-scaling.html:558`, `deep/backend-focused.js:445`, `deep/backend-focused.js:519`, `deep/backend-focused.js:619`.

#### 172. Começar administrador (HTML de Scaling no pacote)

ID da análise: `extra:scaling-option-startAsAdmin`.

**Para que serve:** Campo declarado em src/gdz-scaling.html; não foi localizada carga desse HTML no fluxo principal, que abre o auxiliar WPF.  
**Funcionamento:** Solicita elevação ao iniciar motor.

**Opções:** `boolean; default true`.

**Entrada:** `ls-launch` `{"startAsAdmin":"valor escolhido junto aos demais campos"}`  
**Retorno:** {success,error?}, sem verificação individual da opção.  
**Reversão/leitura:** Alterar e reiniciar/parar motor conforme necessário.  

**Limites:** Marcação não prova privilégio efetivo. A conclusão deste controle vale para o HTML e sua chamada potencial, não para o controle de mesmo nome no auxiliar WPF.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:487`, `src/gdz-scaling.html:558`.

#### 173. Clip cursor (controle sem envio) — HTML extraído sem carga localizada

ID da análise: `extra:scaling-visual-ls-clip-cursor`.

**Para que serve:** Controle altera aparência ou valor local. Este comportamento pertence a src/gdz-scaling.html; o fluxo principal abre o auxiliar WPF.  
**Funcionamento:** getConfig não lê este ID; nenhum IPC/persistência associado neste arquivo.

**Entrada:** sem IPC; operação local/visual.  
**Retorno:** Classe on/valor numérico no DOM.  
**Reversão/leitura:** Só visual/local.  

**Limites:** Sem efeito no motor demonstrado por este fluxo; Draw FPS não liga o overlay GDZ. Não caracteriza automaticamente a UI WPF que o usuário abre pelo menu; análise limitada a este arquivo HTML.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:421`, `src/gdz-scaling.html:558`, `deep/backend-focused.js:392`.

#### 174. Max frame latency (controle sem envio) — HTML extraído sem carga localizada

ID da análise: `extra:scaling-visual-ls-latency`.

**Para que serve:** Controle altera aparência ou valor local. Este comportamento pertence a src/gdz-scaling.html; o fluxo principal abre o auxiliar WPF.  
**Funcionamento:** getConfig não lê este ID; nenhum IPC/persistência associado neste arquivo.

**Entrada:** sem IPC; operação local/visual.  
**Retorno:** Classe on/valor numérico no DOM.  
**Reversão/leitura:** Só visual/local.  

**Limites:** Sem efeito no motor demonstrado por este fluxo; Draw FPS não liga o overlay GDZ. Não caracteriza automaticamente a UI WPF que o usuário abre pelo menu; análise limitada a este arquivo HTML.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:446`, `src/gdz-scaling.html:558`, `deep/backend-focused.js:392`.

#### 175. Draw FPS (controle sem envio) — HTML extraído sem carga localizada

ID da análise: `extra:scaling-visual-ls-drawfps`.

**Para que serve:** Controle altera aparência ou valor local. Este comportamento pertence a src/gdz-scaling.html; o fluxo principal abre o auxiliar WPF.  
**Funcionamento:** getConfig não lê este ID; nenhum IPC/persistência associado neste arquivo.

**Entrada:** sem IPC; operação local/visual.  
**Retorno:** Classe on/valor numérico no DOM.  
**Reversão/leitura:** Só visual/local.  

**Limites:** Sem efeito no motor demonstrado por este fluxo; Draw FPS não liga o overlay GDZ. Não caracteriza automaticamente a UI WPF que o usuário abre pelo menu; análise limitada a este arquivo HTML.

**Evidência:** comprovado-no-html-extraido; ativo-na-navegacao-principal-nao-localizado. Fontes: `src/gdz-scaling.html:457`, `src/gdz-scaling.html:558`, `deep/backend-focused.js:392`.

#### 176. Aba antiga/Recomendado

ID da análise: `extra:scaling-old-tab`.

**Para que serve:** Dashboard mantém aba com perfil atual/recomendado.  
**Funcionamento:** Carrega perfil; traduz XML e flowScale %. Recomendado só preenche antes de iniciar.

**Entrada:** `ls-config` sem argumento; `ls-launch` `"lsGetConfig()"`; `ls-stop` sem argumento  
**Retorno:** {success,current,recommended,configured}.  
**Reversão/leitura:** Stop encerra; reler perfil.  

**Limites:** Navegação principal abre auxiliar WPF via GDZOptimizer.exe; aba HTML antiga permanece no dashboard e a versão gdz-scaling.html não tem carga localizada. Não assumir que limites da ponte sejam todos os limites da interface nativa. A ponte recusa FSR1/2/3, algoritmos NIS/INTEGER/NN/XBR/ANIME4K, fit Full/Fit e vários modos Sync; ignora LSFGSize. Só altera tags XML já existentes.

**Evidência:** comprovado-no-renderer; efeito-do-handler-nao-confirmado. Fontes: `src/dashboard.html:2028`, `src/dashboard.html:2044`, `src/dashboard.html:2082`, `src/dashboard.html:2090`, `deep/backend-focused.js:445`, `deep/backend-focused.js:519`, `deep/backend-focused.js:619`.

### Interface global

#### 177. Fallback de demonstração

ID da análise: `extra:dev-fallback`.

**Para que serve:** Só ativo quando window.electron está ausente.  
**Funcionamento:** Permite desenhar UI fora do aplicativo.

**Entrada:** sem IPC; operação local/visual.  
**Retorno:** Hardware exemplo, uso aleatório e sucesso simulado de algumas ações.  
**Reversão/leitura:** Sem ajuste Windows.  

**Limites:** Não prova que valores da captura/app instalado sejam falsos; depende da ausência de ponte.

**Evidência:** comprovado-no-renderer; efeito-local/visual. Fontes: `src/dashboard.html:1519`, `src/dashboard.html:1528`, `src/dashboard.html:1533`.

## Funções auxiliares relevantes do renderer

As funções abaixo ajudam a localizar a implementação da interface. Esta lista de 37 é uma seleção explicada; não é uma promessa de abranger todos os símbolos/closures do JavaScript.

| Símbolo e referência | Responsabilidade |
|---|---|
| `info` — `src/otimizacoes.js:290` | Combina metadados de PAGES com tweaks-status; avalia nota/recomendação por capacidade e calcula bloqueio/estado. |
| `load` — `src/otimizacoes.js:304` | Consulta tweaks-status/actions-status/get-caps em paralelo, deduplica leitura em andamento e atualiza catálogo. |
| `renderAll` — `src/otimizacoes.js:338` | Monta páginas; tweaks devolvidos pelo backend sem lugar declarado entram em Outros ajustes do Windows. |
| `renderPage` — `src/otimizacoes.js:349` | Filtra por capacidade e cria grupos/linhas da página. |
| `paintRow` — `src/otimizacoes.js:463` | Pinta estado e bloqueio; permite desligar item bloqueado que já estava ligado. |
| `toggle` — `src/otimizacoes.js:548` | Serializa mudança por chave, chama tweak-set/run-action/revert-action, valida retorno e sincroniza UI. |
| `runButton` — `src/otimizacoes.js:579` | Dispara ação pontual/ferramenta/reparo e trata confirmação, progresso e resultado. |
| `runCheckRow` — `src/otimizacoes.js:605` | Envia run-check e desenha title/level/lines do retorno. |
| `setDns` — `src/otimizacoes.js:622` | Desabilita opções durante tweak-dns e atualiza provedor confirmado. |
| `applyRecommended` — `src/otimizacoes.js:636` | Confirma lista e aplica pendentes recomendados sequencialmente, contando reinícios. |
| `renderOverview` — `src/otimizacoes.js:673` | Monta cinco cartões e contadores recomendados; exclui páginas checksOnly/noBar. |
| `renderCompare` — `src/fps.js:130` | Calcula variações percentuais e tolerância de 3%; frametime tem sinal de benefício invertido. |
| `record` — `src/fps.js:255` | Solicita sessão a/b; chamada segura resposta enquanto polling mostra progresso. |
| `apply` — `src/gtarp.js:94` | Envia preset/scaling/borderless do rascunho FiveM e relê arquivo. |
| `useDraft` — `src/opti.js:113` | Inicializa opções padrão e redireciona FiveM para fluxo GTA sem instalar OptiScaler. |
| `saveOpt` — `src/opti.js:190` | Envia patch de uma opção; informa que deve reabrir jogo para aplicar. |
| `update` — `src/opti.js:210` | Analisa anti-cheat, remove instalação e reinstala mantendo opções; não é transação atômica. |
| `drawBar` — `src/memoria.js:87` | Separa uso/cache/livre e oculta cache quando desconhecido. |
| `render` — `src/memoria.js:110` | Usa percentual global em todos os pentes; animação não afirma uso por chip. |
| `selected` — `src/limpeza.js:124` | Seleciona apenas categorias marcadas, utilizáveis e ainda não concluídas. |
| `applyStatus` — `src/limpeza.js:235` | Reconstrói árvores de ocupação por IDs/parentesco e ordena por bytes. |
| `waiting` — `src/programas.js:141` | Espera desinstalação com consultas a apps-exists, limitada a trinta minutos. |
| `pickLeftovers` — `src/programas.js:183` | Mostra lista editável e soma bytes dos resíduos selecionados. |
| `tabsData` — `src/pcinfo.js:231` | Define todas as abas/campos do inventário e unidades de apresentação. |
| `report` — `src/pcinfo.js:336` | Gera relatório textual de campos preenchidos e sensores disponíveis. |
| `analyse` — `src/mouse-test.js:44` | Calcula Hz por mediana, estabilidade, taxa nominal e eventos atrasados. |
| `score` — `src/busca.js:90` | Classifica casamento de todas as palavras por título/tags/descrição. |
| `choose` — `src/busca.js:232` | Navega/destaca resultado sem executar ajuste. |
| `tocar` — `src/sons.js:77` | Sintetiza som se permitido por gesto, visibilidade e intervalo mínimo. |
| `definir` — `src/sons.js:186` | Altera estado de sons e persiste preferência local. |
| `start` — `src/tour.js:443` | Inicia sequência, captura foco e acompanha posição dos alvos. |
| `close` — `src/tour.js:463` | Remove tour/listeners, grava visto quando apropriado e restaura foco. |
| `quickOpt` — `src/dashboard.html:1946` | Dispara ação global própria em vez do laço de recomendados. |
| `diagnosticarGargalo` — `src/dashboard.html:2468` | Classifica métricas por limiares RAM/GPU/coreMax em ordem fixa. |
| `lsFillFromProfile` — `src/dashboard.html:2044` | Converte campos XML do perfil para controles da aba antiga. |
| `getConfig` — `src/gdz-scaling.html:558` | Serializa opções da janela; não lê Clip cursor/Max frame latency/Draw FPS. |
| `selectProfile` — `src/gdz-scaling.html:514` | Altera somente rótulo/classe no DOM, sem persistência. |

## Limite da conclusão

Este documento aprofunda a função de cada item visível e liga 71 entradas principais a declarações de backend legíveis (33 tweaks, 23 ações pontuais/reversíveis, 5 ferramentas, 4 reparos, 5 verificações e 1 seletor DNS). Os handlers ainda devem ser acompanhados por todo o fluxo antes de afirmar todos os caminhos de erro, autorização, persistência e restauração. As expressões brutas preservadas no JSON permitem aprofundar sem executar o alvo. Não houve medição de FPS nem teste das alterações no Windows.

Hashes finais da análise: `src/otimizacoes.js` SHA-256 `3f8fe8a4bbf5648f9ee7e38aaab81f1682e11f291435df7879f5808c81f5fad9`; `deep/backend-focused.js` SHA-256 `52ca9c4279b739ec674637dae36a361b62eaf06b94aaa6db2a9caba6d4baaef0`. As declarações de backend foram reextraídas dessa versão estável; títulos e descrições permaneceram idênticos.
