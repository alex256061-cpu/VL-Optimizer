# GDZ Optimizer — todas as funções da ponte IPC

Análise estática de 08/11/2026. Este relatório explica o comportamento do backend de cada canal exposto por preload.js, com payload, validações, efeitos, retorno e reversão. Não foi enviada nenhuma chamada IPC ao produto, iniciado um processo alvo ou aplicado um ajuste.

**Cobertura: 87 canais invoke permitidos, 85 handlers registrados, 2 sem implementação localizada; 9 sends com handler; 4 eventos permitidos, 3 com emissor localizado.**

Fonte de leitura: deep/backend-focused.js, SHA-256 `52ca9c4279b739ec674637dae36a361b62eaf06b94aaa6db2a9caba6d4baaef0`. As referências L abaixo são linhas dessa vista legível derivada estaticamente do app-main.js. O JSON privado deep/ipc-explained.json preserva offsets, assinaturas originais, chamadas, expressões de retorno e dependências. A vista não substitui os bytes originais como evidência.

## Regras comuns de entrada e execução

- preload permite apenas nomes das listas SEND_CHANNELS/INVOKE_CHANNELS/ON_CHANNELS. invoke fora da lista rejeita; send/on fora da lista são ignorados. on exige callback e retira o objeto de evento Electron dos argumentos.
- O wrapper global de ipcMain.handle verifica channelAllowed: ALWAYS_CHANNELS passa pelo filtro de plano; demais exigem usuário e feature * ou feature específica. run-action tem alternativa prevista apenas para Otimização Rápida com quick-opt. Isso não elimina requireAuth nos handlers que o usam.
- requireAuth apenas exige currentUser. Canais ipcMain.on não passam pelo wrapper invoke; eventuais checagens são próprias de cada send.
- withActionLock serializa ações em cadeia de promises; runPS tem outra fila de PowerShell. Essas filas evitam concorrência, sem garantir sucesso/restauração. Scripts usam permissões do processo, e várias operações podem exigir elevação.
- success:true frequentemente significa pedido aceito, subprocesso iniciado ou script sem marcador GDZ_ERRO. Não equivale a ganho de FPS nem confirmação de todos os efeitos externos.
- Os campos de entrada abaixo omitem event de Electron. Quase todos os handlers usam objeto padrão {}; gdz-scaling-apply e kill-process desestruturam objeto sem padrão e podem rejeitar se payload ausente.

## Mapa dos casos que parecem função, mas diferem na implementação

| Canal | O que foi encontrado |
|---|---|
| ls-browse-exe / ls-find-exe | Permitidos no preload, sem ipcMain.handle correspondente. |
| gs-minimize / gs-maximize | Registrados, mas ambos têm corpo vazio. |
| fps-update | Permitido para listener, sem emissor no backend examinado. FPS usa polling e overlay-update. |
| update-install | Registro condicional na inicialização de atualização; pode não existir em dev/portable. |
| ls-config | Pode restaurar XML corrompido; não é só consulta. |
| fps-status / pcinfo-live | Podem iniciar coletores para responder. |
| opti-download / apps-uninstall | Retornam antes da conclusão do trabalho externo. |
| gdz-scaling-revert | Descarta código Win32; success:true não valida troca de resolução. |

## Invoke: 87 canais, um por um

### `login`

Evidência: backend-focused.js:L865–L877; sem requireAuth no handler.

**Entrada:** {email:string,password:string}

**Validação e fluxo:** Exige strings, email trim não vazio e senha não vazia; chama appAuth POST /app/login. Helper exige HWID válido, acrescenta serial da placa, hostname (64 caracteres), versão e build do SO; só aceita token/entitlement verificável.

**Leituras, escritas e processos:** Rede de autenticação; grava estado local protegido via acceptEntitlement e agenda flushTelemetry em20s.

**Retorno:** {success,user} ou {success:false,message,code?}.

**Reversão:** logout limpa credenciais locais e solicita logout remoto.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `appAuth` (L899).

### `register`

Evidência: backend-focused.js:L877–L898; sem requireAuth no handler.

**Entrada:** {name?,email,password,terms}

**Validação e fluxo:** Valida email/senha como login e terms===true; nome trim limitado40 ou null. appAuth POST /app/register com identidade do dispositivo; trata HTTP429 e entitlement inválido.

**Leituras, escritas e processos:** Cria conta remota e persiste sessão local aceita; agenda telemetria20s.

**Retorno:** {success,user} ou falha com message/code.

**Reversão:** logout encerra sessão; este canal não exclui conta.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `appAuth` (L899).

### `check-session`

Evidência: backend-focused.js:L939–L952; sem requireAuth no handler.

**Entrada:** sem payload

**Validação e fluxo:** removeLegacyAuthFiles e refreshEntitlement; consulta servidor ou verifica entitlement assinado/cache offline e proteção de relógio. Consome logoutReason.

**Leituras, escritas e processos:** Pode apagar arquivos legados, atualizar/limpar auth/currentUser e agendar telemetria20s; não concede plano sem verificação.

**Retorno:** {success:true,user,offline} ou {success:false,message}.

**Reversão:** logout para sessão; não altera ajustes do Windows.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `removeLegacyAuthFiles` (L212); `refreshEntitlement` (L296); `flushTelemetry` (L7817).

### `get-user`

Evidência: backend-focused.js:L1333–L1333; sem requireAuth no handler.

**Entrada:** sem payload

**Validação e fluxo:** Devolve currentUser em memória; sem consulta remota nem atualização do entitlement neste handler.

**Leituras, escritas e processos:** Leitura de estado.

**Retorno:** Objeto do usuário atual ou null.

**Reversão:** Não aplicável.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

### `open-site`

Evidência: backend-focused.js:L1018–L1056; sem requireAuth no handler.

**Entrada:** {page,plan?,coupon?}

**Validação e fluxo:** Mapeia page em lista fixa (planos,conta,criar-conta,recuperar,download,termos,privacidade), senão /. Plan regex a-z0-9 1..16; cupom3..32 alfanumérico/_/-. Para planos/conta online tenta POST /app/site-link e só aceita URL começando https://www.gdzfps.com/entrar/app?.

**Leituras, escritas e processos:** Abre navegador por shell.openExternal; acelera sync do plano. Pode enviar token à API própria para gerar link.

**Retorno:** {success:true}; rejeições openExternal não são tratadas no handler.

**Reversão:** Fechar página; não há alteração de Windows.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `loadAuth` (L186); `apiCall` (L222).

### `update-status`

Evidência: backend-focused.js:L1285–L1288; sem requireAuth no handler.

**Entrada:** sem payload

**Validação e fluxo:** Copia updateState e acrescenta app.getVersion; não busca atualização nesta chamada.

**Leituras, escritas e processos:** Leitura de memória/versão.

**Retorno:** {status,version,error,percent?,current}.

**Reversão:** Não aplicável.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

### `update-install`

Evidência: backend-focused.js:L1273–L1283; sem requireAuth no handler.

**Entrada:** sem payload

**Validação e fluxo:** Registrado somente dentro startAutoUpdate: build instalado, não portable e electron-updater disponível. Exige updateState.status=ready; agenda quitAndInstall(true,true). O estado ready é definido após verificação de assinatura do instalador baixado.

**Leituras, escritas e processos:** Sai do app e inicia instalador já baixado. Não testa instalação concluída ao retornar.

**Retorno:** {success:true} ou {success:false,error}; canal pode não estar registrado em dev/portable.

**Reversão:** Sem rollback de versão neste canal.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

### `claim-key`

Evidência: backend-focused.js:L977–L1018; sem requireAuth no handler.

**Entrada:** {key:string}

**Validação e fluxo:** Chave trim não vazia, auth local existente e HWID válido; POST /app/claim-key com token, chave maiúscula e HWID. Exige200+entitlement aceito.

**Leituras, escritas e processos:** Resgate remoto, atualiza auth/plano, envia entitlement-update à janela principal.

**Retorno:** {success:true,user} ou {success:false,error}.

**Reversão:** Sem desfazer resgate neste canal; logout apenas encerra sessão.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `loadAuth` (L186); `getHWID` (L725); `isRealHWID` (L742); `apiCall` (L222); `acceptEntitlement` (L280).

### `telemetry-consent`

Evidência: backend-focused.js:L7914–L7916; sem requireAuth no handler.

**Entrada:** sem payload

**Validação e fluxo:** Lê telemetryConsent quando booleano; se não definido retorna null.

**Leituras, escritas e processos:** Leitura de Settings.

**Retorno:** {consent:true|false|null}.

**Reversão:** telemetry-consent-set altera preferência.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

### `telemetry-consent-set`

Evidência: backend-focused.js:L7916–L7933; sem requireAuth no handler.

**Entrada:** {on}

**Validação e fluxo:** Interpreta somente on===true como consentimento; salva Settings. Se false zera fila local. Se auth existe envia POST /app/consent com telemetry; erro remoto é ignorado.

**Leituras, escritas e processos:** Escrita de preferência/fila local e envio remoto; true agenda flushTelemetry em3s.

**Retorno:** {success:true,consent:boolean}.

**Reversão:** Chamar com on:false desativa e apaga fila local; não desfaz dados já enviados.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `saveSettings` (L2849); `saveTelemetryQueue` (L7646); `loadAuth` (L186); `apiCall` (L222); `flushTelemetry` (L7817).

### `offers`

Evidência: backend-focused.js:L1089–L1105; sem requireAuth no handler.

**Entrada:** {force?:boolean}

**Validação e fluxo:** loadOffers(force===true), usa cache/API de ofertas; exceção é convertida em offers:null.

**Leituras, escritas e processos:** Leitura remota e cache de ofertas.

**Retorno:** Sempre {success:true,offers} neste corpo, inclusive offers:null ao falhar.

**Reversão:** Não aplicável.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `loadOffers` (L1059).

### `plan-event`

Evidência: backend-focused.js:L1121–L1136; sem requireAuth no handler.

**Entrada:** {type,trigger}

**Validação e fluxo:** type deve pertencer a upsell_view,upsell_click,offer_view,offer_click,referral_copy; trigger deve casar [a-z0-9_-]{1,32}; rejeita fila>=30.

**Leituras, escritas e processos:** Enfileira evento em memória e agenda flushPlanEvents após15s se ainda não agendado.

**Retorno:** {success:boolean}.

**Reversão:** Não há apagamento individual oferecido aqui.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `flushPlanEvents` (L1109).

### `autostart-get`

Evidência: backend-focused.js:L4619–L4625; sem requireAuth no handler.

**Entrada:** sem payload

**Validação e fluxo:** autostartSupported só para app empacotado não portable; aguarda autostartReady e consulta tarefa agendada.

**Leituras, escritas e processos:** Consulta ScheduledTask GDZ Optimizer.

**Retorno:** {supported,on}.

**Reversão:** Não aplicável.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `autostartSupported` (L4604); `autostartIsOn` (L4611).

### `autostart-set`

Evidência: backend-focused.js:L4625–L4641; requireAuth.

**Entrada:** {on}

**Validação e fluxo:** Exige versão instalada não portable; aguarda sincronização. true chama autostartRegister(false), senão remove tarefa. Register cria ação próprio exe --autostart, logon usuário atual, Interactive/Highest, bateria permitida, sem limite de tempo, prioridade4, IgnoreNew.

**Leituras, escritas e processos:** Cria/remove tarefa agendada GDZ Optimizer; persiste autoStart só se operação aceita.

**Retorno:** {success,on,error?}, on consultado de novo.

**Reversão:** on:false unregister tarefa; on:true recria.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `requireAuth` (L1289); `autostartSupported` (L4604); `autostartRegister` (L4605); `autostartRemove` (L4608); `saveSettings` (L2849); `autostartIsOn` (L4611).

### `get-hardware`

Evidência: backend-focused.js:L1333–L1438; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** PowerShell lê CPU, placas PCI ordenadas por AdapterRAM, SO e módulos RAM por CIM com fallback WMI. Parse9campos. Fallback lê ProcessorNameString no Registro e RAM física.

**Leituras, escritas e processos:** Somente leitura de hardware/Registro/processo PowerShell.

**Retorno:** {cpu,cpuDetail,gpu,gpuDetail,ram,ramDetail}; mensagens de detecção/admin em falha.

**Reversão:** Não aplicável.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `requireAuth` (L1289); `runPS` (L785).

### `get-stats`

Evidência: backend-focused.js:L1440–L1445; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Reutiliza _statsP se consulta já pendente; readStats lê LoadPercentage médio da CPU, RAM ocupada por Win32_OperatingSystem e soma GPU3D limitada99; usa CIM/WMI e limpa promise em finally.

**Leituras, escritas e processos:** Leitura e cache de consulta; inicia PowerShell de métricas via helper.

**Retorno:** {cpu,ram,gpu} em percentuais; erro de readStats retorna todos0.

**Reversão:** Não aplicável.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `requireAuth` (L1289); `readStats` (L1446).

### `get-mem-stats`

Evidência: backend-focused.js:L1683–L1695; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** os.totalmem/freemem; used/total em GiB arredondados a0.1 e pct inteiro. standbyBytes dá cache ou null; limita cache a free; memSticks informa módulos.

**Leituras, escritas e processos:** Leitura de memória; helpers podem agendar atualização assíncrona de cache.

**Retorno:** {used,total,pct,cache,sticks}.

**Reversão:** Não aplicável.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `requireAuth` (L1289); `standbyBytes` (L1662); `memSticks` (L1630).

### `get-processes`

Evidência: backend-focused.js:L8980–L9056; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** PowerShell Get-Process filtra janela visível ou WorkingSet>30MB, ordena memória desc e pega12. Parse PID|MB|nome.

**Leituras, escritas e processos:** Consulta processos; não os altera.

**Retorno:** {success:true,processes:[{pid,ram,name}]} ou {success:false,error,processes:[]}.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `runPS` (L785).

### `kill-process`

Evidência: backend-focused.js:L9058–L9083; requireAuth.

**Entrada:** {pid,name?}; objeto obrigatório

**Validação e fluxo:** parseInt PID, exige inteiro>0. Se name fornecido pertencer a PROTECTED_PROCS bloqueia; não resolve nome real do PID neste handler. PowerShell Stop-Process -Id PID -Force -ErrorAction Stop.

**Leituras, escritas e processos:** Encerramento forçado de processo.

**Retorno:** {success:true} ou {success:false,error}.

**Reversão:** Sem restauração de estado/processo; usuário pode reabrir programa.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `runPS` (L785).

### `gdz-scaling-get-res`

Evidência: backend-focused.js:L1701–L1717; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** PowerShell carrega System.Windows.Forms e lê bounds PrimaryScreen; parse width|height com fallback1920x1080.

**Leituras, escritas e processos:** Leitura de resolução; sem troca de modo.

**Retorno:** {width,height}; fallback1920x1080 em erro.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `runPS` (L785).

### `gdz-scaling-apply`

Evidência: backend-focused.js:L1717–L1742; requireAuth.

**Entrada:** {width,height,origW,origH}; objeto obrigatório na assinatura

**Validação e fluxo:** safeRes usa parseInt e aceita320..16000, senão fallback; alvo fallback1280x720, original fallback1920x1080. Guarda originalResolution só na primeira chamada. PowerShell compila P/Invoke DEVMODE e chama ChangeDisplaySettings(dmFields=0x180000,flags=0). Não consulta resolução original internamente: usa origW/origH fornecidos.

**Leituras, escritas e processos:** Altera resolução do desktop por user32; original guardado apenas em memória.

**Retorno:** {success: saída.trim()==0} ou {success:false,error}.

**Reversão:** gdz-scaling-revert usa originalResolution ou1920x1080; original perde-se ao fechar processo.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `safeRes` (L1697); `runPS` (L785).

### `gdz-scaling-revert`

Evidência: backend-focused.js:L1742–L1768; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Usa originalResolution ou fallback1920x1080, normaliza safeRes e chama ChangeDisplaySettings. Descarta retorno Win32 (Out-Null), limpa originalResolution.

**Leituras, escritas e processos:** Altera resolução e apaga estado original em memória.

**Retorno:** {success:true} se PowerShell não rejeitou; não verifica código ChangeDisplaySettings; erro capturado retorna false/error.

**Reversão:** Nova chamada apply com dimensões desejadas; não guarda cadeia de versões.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `runPS` (L785); `safeRes` (L1697).

### `ls-browse-exe`

Evidência: permitido em preload.js; nenhum handler correspondente localizado na fonte indexada.

**Entrada:** canal permitido pelo preload

**Validação e fluxo:** Não há ipcMain.handle correspondente no backend inspecionado.

**Leituras, escritas e processos:** Nenhum efeito implementado localizado.

**Retorno:** invoke sem handler deve rejeitar; nenhuma resposta de produto reconstruída.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

### `ls-find-exe`

Evidência: permitido em preload.js; nenhum handler correspondente localizado na fonte indexada.

**Entrada:** canal permitido pelo preload

**Validação e fluxo:** Não há ipcMain.handle correspondente no backend inspecionado.

**Leituras, escritas e processos:** Nenhum efeito implementado localizado.

**Retorno:** invoke sem handler deve rejeitar; nenhuma resposta de produto reconstruída.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

### `ls-launch`

Evidência: backend-focused.js:L676–L697; requireAuth.

**Entrada:** {recommended?,fgType?,lsfg3Mode?,lsfg3Multiplier?,lsfg3Target?,flowScale?,perfMode?,captureApi?,queueTarget?,scaleType?,fsrType?,scaleMode?,scaleFactor?,fitMode?,sharpness?,syncMode?,hdrCompat?,resizeBefore?,scaleCursor?,vrs?,autoScale?,windowedMode?,startAsAdmin?}

**Validação e fluxo:** Consulta processos GDZOptimizer/LosslessScaling por PowerShell. Recupera XML; usa recomendado quando recommended===true ou converte UI. Só grava quando auxiliar não está rodando e há opções; scalingWrite valida enums, clampa números, escreve tags existentes e cria .gdz-original. Há incompatibilidades entre mapas UI e allowlist de enums, listadas em skipped.

**Leituras, escritas e processos:** Escreve primeiro Profile/global XML, settings.scalingConfigured e Settings; abre/foca GDZOptimizer.exe detached, retitula janela por PowerShell. Auxiliar já aberto não recebe novas opções.

**Retorno:** {success,applied,skipped,message}; falha de XML retorna error. applied significa não estava aberto, não prova ativação gráfica.

**Reversão:** ls-stop encerra auxiliar; backup XML existe, mas este canal não restaura valores automaticamente.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `runPS` (L785); `scalingRecoverIfCorrupted` (L579); `scalingRecommended` (L637); `scalingFromUi` (L619); `scalingWrite` (L519); `saveSettings` (L2849); `openScalingWindow` (L393).

### `ls-stop`

Evidência: backend-focused.js:L697–L702; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** PowerShell seleciona GDZOptimizer/LosslessScaling e Stop-Process -Force; erros são ignorados; zera lsProcess.

**Leituras, escritas e processos:** Encerra processos do auxiliar. Não restaura XML.

**Retorno:** {success:true}, mesmo se houve erro ignorado.

**Reversão:** ls-launch reabre; configurações permanecem.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `runPS` (L785).

### `ls-config`

Evidência: backend-focused.js:L666–L676; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** scalingRecoverIfCorrupted pode restaurar Settings.xml.gdz-original quando CorruptedSettingsBackup.xml existe; scalingRead lê só primeiro Profile e opções globais; scalingRecommended consulta capacidades/tela.

**Leituras, escritas e processos:** Pode copiar backup sobre XML e remover marcador de corrupção; lê XML, hardware e tela. Consulta não é puramente leitura.

**Retorno:** {success:true,current,recommended,configured,recovered}.

**Reversão:** Preserva .gdz-original; sem botão de restaurar XML completo neste handler.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `scalingRecoverIfCorrupted` (L579); `scalingRead` (L498); `scalingRecommended` (L637).

### `run-action`

Evidência: backend-focused.js:L4334–L4404; requireAuth.

**Entrada:** {action:string}

**Validação e fluxo:** Serializa. Resolve ACTIONS, ou Otimizar <nome> (1..60caracteres), ou ação GPU reconhecida após scanGames. Checa blockedActions/gpuBlocked. Snapshot specFor antes da primeira execução; em falha de snapshot não altera. runPS do script tem ACTION_TIMEOUT90s; pode salvar snapshot after.

**Leituras, escritas e processos:** Executa roteiro correspondente de Registro/serviço/energia/jogo e escreve backups; scripts detalhados no catálogo de ações.

**Retorno:** {success:true,revertible,on:true,message?} ou erro. Mensagem success depende de psResult, não medição de FPS.

**Reversão:** revert-action/restore-all quando canRevert e snapshot/REVERT permitirem.

**Acesso:** Exige currentUser e feature *; alternativa prevista: action=Otimização Rápida com feature quick-opt.

**Dependências nomeadas:** `requireAuth` (L1289); `withActionLock` (L4312); `getCaps` (L2053); `hasOwn` (L3941); `gpuActionGame` (L2802); `scanGames` (L2588); `gpuBlocked` (L2809); `gpuScript` (L2808); `blockedActions` (L2078); `specFor` (L3942); `takeSnapshot` (L4190); `saveBackups` (L4183); `psResult` (L4316); `runPS` (L785); `valueFor` (L2088); `canRevert` (L4212).

### `revert-action`

Evidência: backend-focused.js:L4404–L4429; requireAuth.

**Entrada:** {action}

**Validação e fluxo:** Serializa; chama revertAction. Se havia backup, sucesso e REVERT+DETECT existem, detecta estado; se ainda ativo executa script REVERT explícito.

**Leituras, escritas e processos:** Restaura snapshot/roteiro reverso e atualiza backup conforme helper.

**Retorno:** Resultado de revertAction ou {success:false,error}.

**Reversão:** É a reversão; não reaplica automaticamente ação após falha.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `requireAuth` (L1289); `withActionLock` (L4312); `hasOwn` (L3941); `revertAction` (L4213); `detectActions` (L3128); `psResult` (L4316); `runPS` (L785).

### `restore-all`

Evidência: backend-focused.js:L4429–L4438; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Serializa e delega restoreAll, que percorre backups e reverte; captura exceção.

**Leituras, escritas e processos:** Restaura itens registrados pelo mecanismo de backup; não é restore point Windows universal.

**Retorno:** Resultado agregado de restoreAll ou {success:false,error}.

**Reversão:** Sem undo único da restauração; ações podem ser reaplicadas.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `requireAuth` (L1289); `withActionLock` (L4312); `restoreAll` (L4276).

### `get-backups`

Evidência: backend-focused.js:L4438–L4438; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Object.keys(backups.entries). Não lê cada configuração atual nem prova restaurabilidade.

**Leituras, escritas e processos:** Somente leitura de mapa em memória.

**Retorno:** Array de nomes/chaves de backup.

**Reversão:** Não aplicável.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `requireAuth` (L1289).

### `get-caps`

Evidência: backend-focused.js:L4548–L4554; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Obtém getCaps e acrescenta blockedActions; regras dependem do hardware/sistema detectados.

**Leituras, escritas e processos:** Leitura/cache de capacidades.

**Retorno:** Objeto de capacidades mais blocked.

**Reversão:** Não aplicável.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `requireAuth` (L1289); `getCaps` (L2053); `blockedActions` (L2078).

### `run-check`

Evidência: backend-focused.js:L6182–L6196; requireAuth.

**Entrada:** {name}

**Validação e fluxo:** Delega runCheck(name) para check conhecido no catálogo; captura exceção e limita texto do erro200.

**Leituras, escritas e processos:** Executa verificação correspondente, descrita em CHECKS; não é canal arbitrário de comando.

**Retorno:** Resultado {level,title,lines,...} do check; em exceção level:info/title/lines.

**Reversão:** Depende do check; maioria consulta, sem rollback global deste handler.

**Acesso:** Exige currentUser e feature * ou hardware.

**Dependências nomeadas:** `requireAuth` (L1289); `runCheck` (L2201).

### `measure-bottleneck`

Evidência: backend-focused.js:L6228–L6249; requireAuth.

**Entrada:** {seconds?,pid?}

**Validação e fluxo:** Helper limita duração10..120s (padrão30), aceita pid inteiro>4 ou0. Executa BOTTLENECK_PS, parse GDZ_BN e metadata; exige>=6amostras. Conta período em primeiro plano, pausas/fechamento. Sempre tenta notificação final e flashFrame.

**Leituras, escritas e processos:** Mede CPU/máximo núcleo/GPU/RAM por PowerShell; não muda afinidade nem otimiza.

**Retorno:** {success,samples,seconds,away,closed,partial,cpu,coreMax,gpu,ram,ramGb} ou erro.

**Reversão:** Não aplicável; medição temporária.

**Acesso:** Exige currentUser e feature * ou gargalo.

**Dependências nomeadas:** `requireAuth` (L1289); `measureBottleneck` (L2279).

### `bn-find-game`

Evidência: backend-focused.js:L6196–L6207; requireAuth.

**Entrada:** {ignore?:PID[]}

**Validação e fluxo:** Mantém inteiros e no máximo50 ignorados. findGameProcess consulta janelas/processos e GPU3D; exclui próprio PID/<=4/ignore. Prefere jogo conhecido; senão candidato fora BN_NOT_GAMES com GPU>=10.

**Leituras, escritas e processos:** Consulta processos/CIM GPU; sem alterar jogo.

**Retorno:** {pid,name,exe,known} ou null em ausência/erro.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * ou gargalo.

**Dependências nomeadas:** `requireAuth` (L1289); `findGameProcess` (L2362).

### `bn-notify`

Evidência: backend-focused.js:L6207–L6228; requireAuth.

**Entrada:** {name?}

**Validação e fluxo:** Nome string limitado80, padrão o jogo. Se Notification suportado mostra aviso; clique mostra/foca mainWindow; flashFrame(true). Exceções ignoradas.

**Leituras, escritas e processos:** Notificação e atenção visual.

**Retorno:** true, mesmo em erro ignorado.

**Reversão:** Notificação expira/fecha; sem ajuste.

**Acesso:** Exige currentUser e feature * ou gargalo.

**Dependências nomeadas:** `requireAuth` (L1289).

### `games-scan`

Evidência: backend-focused.js:L4554–L4580; requireAuth.

**Entrada:** {force?:boolean}

**Validação e fluxo:** getCaps, scanGames(force===true), processos/jogos em execução. force limpa cache de capas. Para jogos instalados/rodando busca gameCover; calcula canClean/gpu e híbrida.

**Leituras, escritas e processos:** Lê bibliotecas/caminhos de jogos/processos e pode obter capas pela rede; cache de biblioteca/capas.

**Retorno:** {games:[{id,name,opt,lib,steamId,installed,running,canClean,gpu,cover}],hybrid,autoMode,autoGame}.

**Reversão:** Consulta/cache; não instala nem otimiza jogo.

**Acesso:** Exige currentUser e feature * ou jogos.

**Dependências nomeadas:** `requireAuth` (L1289); `getCaps` (L2053); `scanGames` (L2588); `runningGameIds` (L2796); `runningProcesses` (L2756); `allGames` (L2486); `gameCover` (L2721); `cleanScript` (L2827); `gpuBlocked` (L2809); `hasHybridGpu` (L2800).

### `games-status`

Evidência: backend-focused.js:L4580–L4584; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** runningProcesses -> runningGameIds, lê autoMode e autoState.game.

**Leituras, escritas e processos:** Consulta processos e estado automático.

**Retorno:** {running,autoMode,autoGame}.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * ou jogos.

**Dependências nomeadas:** `requireAuth` (L1289); `runningGameIds` (L2796); `runningProcesses` (L2756).

### `set-auto-mode`

Evidência: backend-focused.js:L4584–L4591; requireAuth.

**Entrada:** {on}

**Validação e fluxo:** on===true vira settings.autoMode; salva e chama autoTick imediatamente. Helper acompanha jogos e executa autoStartScript ou autoStopScript conforme estado.

**Leituras, escritas e processos:** Persiste preferência; autoTick pode alterar plano de energia/prioridade/processos pelo mecanismo automático.

**Retorno:** {autoMode:boolean}; chamada não aguarda conclusão de autoTick.

**Reversão:** on:false desativa e autoTick trata estado anterior por autoStopScript; ações em curso dependem da trava _autoBusy.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `saveSettings` (L2849); `autoTick` (L2870).

### `game-clean`

Evidência: backend-focused.js:L4719–L4747; requireAuth.

**Entrada:** {id}

**Validação e fluxo:** Serializa; gameById+cleanScript devem resolver cache suportado; se jogo correspondente rodando bloqueia. Executa script180s e interpreta GDZ_ERRO/AVISO.

**Leituras, escritas e processos:** Apaga caches de jogo conforme cleanScript; sem backup do conteúdo neste handler.

**Retorno:** {success:true,message?} ou erro.

**Reversão:** Sem restauração automática; jogo pode recriar cache.

**Acesso:** Exige currentUser e feature * ou jogos.

**Dependências nomeadas:** `requireAuth` (L1289); `withActionLock` (L4312); `gameById` (L2487); `cleanScript` (L2827); `runningGameIds` (L2796); `runningProcesses` (L2756); `psResult` (L4316); `runPS` (L785).

### `shader-clean`

Evidência: backend-focused.js:L4747–L4767; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Serializa; bloqueia se qualquer jogo detectado estiver rodando. runPS SHADER_CLEAN_PS180s, interpreta resultado.

**Leituras, escritas e processos:** Apaga caches de shaders definidos no script; sem backup.

**Retorno:** {success:true,message?} ou erro.

**Reversão:** Sem undo; drivers/jogos podem regenerar shaders.

**Acesso:** Exige currentUser e feature * ou jogos.

**Dependências nomeadas:** `requireAuth` (L1289); `withActionLock` (L4312); `runningGameIds` (L2796); `runningProcesses` (L2756); `psResult` (L4316); `runPS` (L785).

### `ram-clean`

Evidência: backend-focused.js:L4591–L4591; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** cleanRam(false) tem trava _ramBusy. PowerShell/C# ajusta privilégio13 e chama NtSetSystemInformation(classe80,cmd4); lê standby antes/depois, verifica NTSTATUS e calcula freedMB.

**Leituras, escritas e processos:** Purga cache standby da RAM; atualiza _standby/ramLast. Não remove arquivos nem instala RAM.

**Retorno:** {success,freedMB,message} ou {success:false,error}.

**Reversão:** Sem desfazer purga; sistema recompõe cache com uso.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `cleanRam` (L2964).

### `ram-status`

Evidência: backend-focused.js:L4591–L4591; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Lê ramStatus: preferência automática, intervalo e última purga.

**Leituras, escritas e processos:** Somente leitura de Settings/ramLast.

**Retorno:** {auto,minutes,intervals:[5,10,15,30,60],lastAt,lastFreedMB,lastAuto,nextAt}.

**Reversão:** Não aplicável.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `requireAuth` (L1289); `ramStatus` (L3030).

### `ram-auto-set`

Evidência: backend-focused.js:L4591–L4601; requireAuth.

**Entrada:** {on,minutes?}

**Validação e fluxo:** on===true define ramAuto; minutes aceito só em [5,10,15,30,60]. Salva. Se acabou de ligar ou minutes foi fornecido (mesmo inválido), atualiza ramLast.at. Timer global checa15s e exige currentUser.

**Leituras, escritas e processos:** Persiste automação de purga de standby; pode provocar purgas futuras.

**Retorno:** Objeto ramStatus.

**Reversão:** on:false desativa futuras purgas; cache já purgado não restaurado.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `saveSettings` (L2849); `ramStatus` (L3030).

### `startup-list`

Evidência: backend-focused.js:L4673–L4687; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** listStartup enumera entradas das fontes STARTUP_SOURCES e aprovação StartupApproved; captura falha.

**Leituras, escritas e processos:** Leitura do Registro de inicialização.

**Retorno:** {success:true,items} ou {success:false,error}.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * ou inicializacao.

**Dependências nomeadas:** `requireAuth` (L1289); `listStartup` (L4656).

### `startup-set`

Evidência: backend-focused.js:L4687–L4713; requireAuth.

**Entrada:** {name:string,scope,enabled:boolean}

**Validação e fluxo:** Serializa. scope deve existir STARTUP_SOURCES; name não vazio<=260 e sem controles; enabled booleano. Confere valor ainda existente em Run, cria chave de aprovação e grava Binary12bytes iniciado2(habilitado) ou3(desabilitado). Nome escapado por psQuote.

**Leituras, escritas e processos:** Altera StartupApproved correspondente, preservando comando Run original.

**Retorno:** {success:true} ou erro de entrada/permissão/entrada removida.

**Reversão:** Mesmo canal com enabled oposto; não guarda snapshot anterior.

**Acesso:** Exige currentUser e feature * ou inicializacao.

**Dependências nomeadas:** `requireAuth` (L1289); `withActionLock` (L4312); `runPS` (L785); `psQuote` (L1847); `ENSURE_KEY` (L1848).

### `get-recommendations`

Evidência: backend-focused.js:L4713–L4719; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Promise.allSettled listStartup e tempCleanableBytes; conta entradas enabled e bytes temporários.

**Leituras, escritas e processos:** Consulta inicialização/arquivos temporários; não limpa.

**Retorno:** {startupOn:numero|null,tempBytes:numero|null}.

**Reversão:** Não aplicável.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `requireAuth` (L1289); `listStartup` (L4656); `tempCleanableBytes` (L4669).

### `clean-scan`

Evidência: backend-focused.js:L5308–L5333; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** execPSFile(cleanScanScript(),120s), psJson, associa por id às CLEAN_CATS, inclui só exists e normaliza bytes/files/open.

**Leituras, escritas e processos:** Varredura de categorias locais de limpeza; arquivo temporário de script pelo executor é removido no helper.

**Retorno:** {success:true,cats:[...CLEAN_INFO,bytes,files,open,...]} ou erro.

**Reversão:** Leitura; sem remoção de conteúdo.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `psJson` (L5302); `execPSFile` (L4939); `cleanScanScript` (L5259); `CLEAN_INFO` (L5182).

### `clean-run`

Evidência: backend-focused.js:L5333–L5363; requireAuth.

**Entrada:** {ids:string[]}

**Validação e fluxo:** Serializa; usa somente strings da lista fornecida que existam em CLEAN_CATS, rejeita vazio. runPSFile(cleanRunScript(categorias),600s), psJson e soma freed.

**Leituras, escritas e processos:** Remove conteúdo das categorias validadas pelo script, com regras de idade/arquivos abertos por categoria. Sem backup geral.

**Retorno:** {success:true,results:[{id,open,freed,files,failed}],freed} ou erro; success pode coexistir com failed>0.

**Reversão:** Sem rollback de arquivos removidos; certas categorias recriam cache.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `withActionLock` (L4312); `psJson` (L5302); `runPSFile` (L5006); `cleanRunScript` (L5280).

### `disk-drives`

Evidência: backend-focused.js:L5389–L5406; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Consulta listDrives e SystemDrive (padrãoC:) maiúsculo.

**Leituras, escritas e processos:** Leitura de unidades/volumes.

**Retorno:** {success:true,drives,system} ou erro.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `listDrives` (L5380).

### `disk-scan`

Evidência: backend-focused.js:L5406–L5507; requireAuth.

**Entrada:** {drive:string}

**Validação e fluxo:** Rejeita outra leitura running; drive deve corresponder sem case a listDrives. Inicializa diskScan e spawn powershell.exe oculto com script diskPS + [GDZ.Disk]::Scan(root,40). Parse linhas P(progresso),D(nós),F(grandes arquivos); close monta resultado ou error.

**Leituras, escritas e processos:** Varre volume escolhido, mantém subprocesso/cache de resultados; não apaga arquivos.

**Retorno:** Retorna já {success:true}; progresso/final por disk-scan-status. Pode retornar false se drive inválido/ocupado.

**Reversão:** disk-scan-cancel interrompe processo; não desfaz pois é leitura.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `listDrives` (L5380); `diskPS` (L4929); `psQuote` (L1847).

### `disk-scan-status`

Evidência: backend-focused.js:L5507–L5523; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Lê diskScan; inclui result somente state=done.

**Leituras, escritas e processos:** Somente leitura de estado.

**Retorno:** {state,drive,files,bytes,error,result}.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289).

### `disk-scan-cancel`

Evidência: backend-focused.js:L5523–L5531; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** diskScanStop encerra subprocesso; se ainda running muda para done se há resultado anterior ou idle.

**Leituras, escritas e processos:** Cancela varredura, não remove arquivos.

**Retorno:** {success:true}.

**Reversão:** disk-scan reinicia leitura.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `diskScanStop` (L5373).

### `disk-open`

Evidência: backend-focused.js:L5531–L5567; requireAuth.

**Entrada:** {target:string}

**Validação e fluxo:** Exige resultado anterior, string<=1000 sem controles, path.resolve sob root do resultado e existsSync. Diretório -> shell.openPath; arquivo -> showItemInFolder.

**Leituras, escritas e processos:** Abre Explorer/pasta; não executa arquivo escolhido diretamente.

**Retorno:** {success:true} ou {success:false,error}.

**Reversão:** Fechar janela do Explorer.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289).

### `apps-list`

Evidência: backend-focused.js:L5712–L5743; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** listApps lê programas instalados e popula appsCache; remove campos internos antes de responder.

**Leituras, escritas e processos:** Consulta Registro de uninstall, sem desinstalar.

**Retorno:** {success:true,apps:[{key,name,pub,ver,date,bytes,noRemove}]} ou erro.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `listApps` (L5684).

### `apps-extra`

Evidência: backend-focused.js:L5743–L5803; requireAuth.

**Entrada:** sem payload; depende appsCache preenchido

**Validação e fluxo:** Filtra locais existentes/profundidade>=3 e exclusões protectedDirs/systemDirs/pastas compartilhadas. Mede tamanho via [GDZ.Disk]::Measure por até40s e obtém ícones por app.getFileIcon para exe/ico existentes.

**Leituras, escritas e processos:** Varredura de pastas e extração de ícones; não exclui conteúdo.

**Retorno:** {success:true,sizes:mapa,icons:dataURLs}; falhas individuais ignoradas.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `protectedDirs` (L5664); `systemDirs` (L5670); `sameOrInside` (L5659); `execPSFile` (L4939); `diskPS` (L4929); `psQuote` (L1847); `cleanDir` (L5587); `uninstallExe` (L5655).

### `apps-uninstall`

Evidência: backend-focused.js:L5812–L5861; requireAuth.

**Entrada:** {key:string}

**Validação e fluxo:** Lookup só em appsCache, rejeita noRemove. parseUninstall resolve file/args. Se caminho do desinstalador sumiu, registra pendingUninstall broken e retorna erro. Caso contrário spawn detached com argumentos verbatim, sem aguardar conclusão, e registra pendência/others.

**Leituras, escritas e processos:** Abre desinstalador registrado do programa; efeitos posteriores dependem do executável externo.

**Retorno:** {success:true} significa iniciou tentativa, não concluiu desinstalação; ou {success:false,broken?,error}. Evento error do filho é ignorado.

**Reversão:** Sem reinstalação/undo neste canal; usuário controla desinstalador externo.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `parseUninstall` (L5599); `othersOf` (L5805).

### `apps-exists`

Evidência: backend-focused.js:L5866–L5875; requireAuth.

**Entrada:** {key:string}

**Validação e fluxo:** Exige UNINSTALL_KEY_RE; inválido retorna exists:false; regExists consulta chave.

**Leituras, escritas e processos:** Leitura do Registro.

**Retorno:** {exists:boolean}.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `regExists` (L5862).

### `apps-leftovers`

Evidência: backend-focused.js:L5894–L6114; requireAuth.

**Entrada:** {key:string}; exige pendingUninstall

**Validação e fluxo:** Exige tentativa de uninstall via GDZ. Se chave ainda existe e não parece quebrada retorna stillInstalled:true. Constrói candidatos por pasta/nome/editor sob ProgramFiles/ProgramData/APPDATA/LOCALAPPDATA/Programs; exclui diretórios usados por outros apps e aplica safeAppDir. Mede diretórios e testa chaves HKCU/Software,HKLM/SOFTWARE/WOW6432Node. Deduplica descendentes e guarda whitelist de IDs na pendência.

**Leituras, escritas e processos:** Consulta arquivos/Registro e cria catálogo de sobras em memória; não remove conteúdo.

**Retorno:** {success:true,stillInstalled,items:[{id,kind,path,bytes,files,label}]} ou erro.

**Reversão:** Não aplicável; remoção exige apps-remove-leftovers.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `regExists` (L5862); `uninstallExe` (L5655); `othersOf` (L5805); `listApps` (L5684); `nameVariants` (L5877); `pubVariants` (L5881); `safeAppDir` (L5674); `execPSFile` (L4939); `diskPS` (L4929); `psList` (L5191); `sameOrInside` (L5659).

### `apps-remove-leftovers`

Evidência: backend-focused.js:L6114–L6182; requireAuth.

**Entrada:** {key,ids:array}; exige catálogo pendente de sobras

**Validação e fluxo:** Serializa; IDs só resolvem itens previamente encontrados, rejeita vazio. Diretórios -> shell.trashItem; pode remover pasta pai vazia após safeAppDir. Registro -> reg.exe export .reg em userData/desinstalacoes; só remove recursivamente chave se export exit0; verifica ausência. Limpa pendência ao final.

**Leituras, escritas e processos:** Envia pastas à Lixeira; apaga chaves de Registro com export prévio; registra falhas por item.

**Retorno:** {success:true,trashed,regs,bytes,failed,backup}; pode haver falhas parciais mesmo success:true.

**Reversão:** Pastas pela Lixeira e Registro por importação manual dos .reg; não há canal undo dedicado.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `withActionLock` (L4312); `safeAppDir` (L5674); `runPSFile` (L5006); `psQuote` (L1847); `psJson` (L5302).

### `tweaks-status`

Evidência: backend-focused.js:L3697–L3729; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** getCaps + tweakStates; monta cada TWEAKS com id/categoria/textos/rec/restart/on/bloqueio, DNS atual e ferramentas em execução.

**Leituras, escritas e processos:** Lê Registro/serviços/rede por helpers; não aplica ajuste.

**Retorno:** {tweaks,dns,tools} ou {error} ao não ler estados.

**Reversão:** Não aplicável.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `requireAuth` (L1289); `getCaps` (L2053); `tweakStates` (L3618); `tweakBlocks` (L3599); `valueFor` (L2088); `tweakAction` (L3568); `dnsProviderOf` (L3653).

### `tweak-set`

Evidência: backend-focused.js:L3762–L3841; requireAuth.

**Entrada:** {id:string,on}

**Validação e fluxo:** Serializa por withActionLock; exige tweakById válido. Ao ligar (on===true), checa tweakBlocks, captura snapshot tweakSpec antes de primeira modificação; se snapshot falhar não executa. runTweak aplica. Ao desligar tenta revertAction de backup, relê estado e executa runTweak(false) se ainda ligado.

**Leituras, escritas e processos:** Executa scripts de Registro/serviço/energia de TWEAKS; salva backups com seq/at/items. Efeitos exatos por id no catálogo de ajustes.

**Retorno:** {success,on,message?} ou {success:false,error}.

**Reversão:** on:false restaura snapshot quando presente e usa desligamento explícito se necessário; não é garantia dinâmica de restauração total.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `withActionLock` (L4312); `tweakById` (L3567); `getCaps` (L2053); `tweakAction` (L3568); `tweakBlocks` (L3599); `tweakSpec` (L3592); `hasOwn` (L3941); `takeSnapshot` (L4190); `saveBackups` (L4183); `runTweak` (L3730); `revertAction` (L4213); `tweakStates` (L3618).

### `tweak-dns`

Evidência: backend-focused.js:L3841–L3906; requireAuth.

**Entrada:** {provider:'auto'|chave DNS_PROVIDERS}

**Validação e fluxo:** Serializa; rejeita provider desconhecido. Custom tira snapshot DNS antes da primeira alteração e executa DNS_SET. Auto restaura backup se houver; se DNS ainda não automático, ResetServerAddresses em adaptadores Up e Clear-DnsClientCache.

**Leituras, escritas e processos:** Muda DNS de adaptadores ativos e limpa cache; salva snapshot.

**Retorno:** {success:true,dns:providerDetectado} ou erro.

**Reversão:** provider:auto usa snapshot e/ou reset para DNS automático.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `withActionLock` (L4312); `hasOwn` (L3941); `revertAction` (L4213); `dnsProviderOf` (L3653); `tweakStates` (L3618); `psResult` (L4316); `runPS` (L785); `takeSnapshot` (L4190); `saveBackups` (L4183); `DNS_SET` (L3659).

### `tweak-tool`

Evidência: backend-focused.js:L3906–L3940; requireAuth.

**Entrada:** {id:chave TWEAK_TOOLS}

**Validação e fluxo:** Rejeita id desconhecido ou já rodando; marca _toolsRunning. execPS executa script da ferramenta com timeout próprio e marcador GDZ_FIM; interpreta GDZ_ERRO/AVISO; remove marca em finally.

**Leituras, escritas e processos:** Executa ferramenta de reparo Windows especificada no catálogo TWEAK_TOOLS; sem snapshot deste handler.

**Retorno:** {success,message?} ou erro/timeout.

**Reversão:** Sem undo geral; depende da ferramenta escolhida.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `hasOwn` (L3941); `psResult` (L4316); `execPS` (L747).

### `actions-status`

Evidência: backend-focused.js:L4536–L4548; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** actionStates combina detectores e estados/snapshots das ações.

**Leituras, escritas e processos:** Leituras Windows por helper; sem aplicar otimização.

**Retorno:** {success:true,on:mapa} ou {success:false,error}.

**Reversão:** Não aplicável.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `requireAuth` (L1289); `actionStates` (L4491).

### `fps-status`

Evidência: backend-focused.js:L7592–L7617; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Chama fpsStart, seleciona fpsTarget e monta fpsLive. Inclui até8processos ordenados por FPS, gravação, sessões, overlay, baseline e disponibilidade Ctrl+Alt+B.

**Leituras, escritas e processos:** Pode iniciar capturador PresentMon/ETW via helper; leitura não é passiva. Atualiza uso do coletor.

**Retorno:** {success,error,live,pinned,processes,recording,sessions,overlay,base,hotkey}.

**Reversão:** Encerramento/inatividade do coletor nos helpers; não existe canal fps-stop listado.

**Acesso:** Exige currentUser e feature * ou fps.

**Dependências nomeadas:** `requireAuth` (L1289); `fpsStart` (L7426); `fpsTarget` (L7332); `fpsLive` (L7345).

### `fps-set-target`

Evidência: backend-focused.js:L7617–L7625; requireAuth.

**Entrada:** {pid}

**Validação e fluxo:** Somente inteiro que esteja fps.procs vira pinned; qualquer outro valor limpa pin.

**Leituras, escritas e processos:** Muda alvo em memória; não altera processo/jogo.

**Retorno:** {success:true,pinned:PID|null}.

**Reversão:** PID inválido/null remove fixação.

**Acesso:** Exige currentUser e feature * ou fps.

**Dependências nomeadas:** `requireAuth` (L1289).

### `fps-record`

Evidência: backend-focused.js:L7933–L8024; requireAuth.

**Entrada:** {slot:'a'|'b',secs?:15|30|60,label?:string}

**Validação e fluxo:** Rejeita slot inválido/gravação concorrente; duração fora lista vira30; exige fpsStart e alvo. Guarda frametimes até prazo/processo parar, polling250ms; exige>=30frames. fpsStats calcula métricas; label remove controles/<> e limita40, padrão Antes/Depois.

**Leituras, escritas e processos:** Captura métricas, enfileira telemetria conforme consentimento, salva sessão a/b em Settings e mostra notificação. Não grava vídeo.

**Retorno:** Promise resolve ao fim: {success:true,session:{métricas,name,label,secs,at}} ou erro.

**Reversão:** fps-clear apaga sessões locais; não remove telemetria já enviada.

**Acesso:** Exige currentUser e feature * ou fps.

**Dependências nomeadas:** `requireAuth` (L1289); `fpsStart` (L7426); `fpsTarget` (L7332); `fpsStats` (L7310); `enqueueFpsTelemetry` (L7680); `saveSettings` (L2849).

### `fps-clear`

Evidência: backend-focused.js:L8024–L8032; requireAuth.

**Entrada:** {slot?}

**Validação e fluxo:** slot a/b apaga só esse; qualquer outro valor zera fpsSessions inteiro. Salva Settings.

**Leituras, escritas e processos:** Remove resultados gravados localmente; não cancela fps.rec neste handler.

**Retorno:** {success:true,sessions}.

**Reversão:** Sem recuperar sessão apagada pelo canal.

**Acesso:** Exige currentUser e feature * ou fps.

**Dependências nomeadas:** `requireAuth` (L1289); `saveSettings` (L2849).

### `fps-overlay-set`

Evidência: backend-focused.js:L8032–L8053; requireAuth.

**Entrada:** {on?:boolean,pos?:0|1|2|3,show?:objeto}

**Validação e fluxo:** Aplica apenas tipos/pos válidos. show só aceita booleans para frametime,low1,cpu,cpuTemp,gpu,gpuTemp,ram,name; reinicia sensores ao alterar. Salva; se on inicia FPS e abre overlay, senão fecha.

**Leituras, escritas e processos:** Escreve preferências, cria/fecha janela transparente sempre no topo, inicia/para sensores.

**Retorno:** {success:true,overlay}; não propaga falha fpsStart como success:false.

**Reversão:** on:false fecha overlay; opções continuam gravadas.

**Acesso:** Exige currentUser e feature * ou fps.

**Dependências nomeadas:** `requireAuth` (L1289); `sensorsStop` (L6448); `saveSettings` (L2849); `fpsStart` (L7426); `overlayOpen` (L7535); `overlayClose` (L7586).

### `fps-base-mark`

Evidência: backend-focused.js:L7625–L7627; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Atualiza fps.lastUse; fpsMarkBase exige alvo com métricas e pelo menos30frames dos últimos5s. Guarda FPS arredondado0.1 e low1 inteiro com nome/PID/data.

**Leituras, escritas e processos:** Baseline em memória para comparação FPS/1%low.

**Retorno:** {success:true,base} ou {success:false,error}.

**Reversão:** fps-base-clear apaga baseline.

**Acesso:** Exige currentUser e feature * ou fps.

**Dependências nomeadas:** `requireAuth` (L1289); `fpsMarkBase` (L7375).

### `fps-base-clear`

Evidência: backend-focused.js:L7627–L7632; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** fps.base=null.

**Leituras, escritas e processos:** Apaga baseline em memória.

**Retorno:** {success:true}.

**Reversão:** fps-base-mark pode criar novo; anterior não recuperado.

**Acesso:** Exige currentUser e feature * ou fps.

**Dependências nomeadas:** `requireAuth` (L1289).

### `opti-status`

Evidência: backend-focused.js:L8653–L8677; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Inicializa opti.pkg por pacote local se necessário; getCaps; percorre settings.opti.installs e só inclui manifestos válidos.

**Leituras, escritas e processos:** Leitura de pacote/manifestos e cache.

**Retorno:** {success:true,pkg:{tag}|null,dl,installs:[{dir,exe,dll,tag,at,opts}],nvidia,dlls}.

**Reversão:** Não aplicável.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `requireAuth` (L1289); `optiLocalPkg` (L8198); `getCaps` (L2053); `optiManifest` (L8270).

### `opti-download`

Evidência: backend-focused.js:L8677–L8681; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Chama optiDownload sem await; inicia/respeita estado do download do pacote OptiScaler.

**Leituras, escritas e processos:** Rede e arquivos de pacote via helper; progresso em opti-status.dl.

**Retorno:** {success:true} imediato, não significa download concluído.

**Reversão:** Sem canal de cancelamento/rollback de download.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `optiDownload` (L8214).

### `opti-browse`

Evidência: backend-focused.js:L8681–L8698; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Abre diálogo nativo seleção exe; cancel retorna canceled. Arquivo escolhido passa optiAnalyze.

**Leituras, escritas e processos:** Seleção/leitura de diretório; não instala só por selecionar.

**Retorno:** {success:false,canceled:true} ou análise OptiScaler.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `optiAnalyze` (L8289).

### `opti-analyze`

Evidência: backend-focused.js:L8698–L8700; requireAuth.

**Entrada:** {exe:string}

**Validação e fluxo:** optiAnalyze exige caminho exe existente, procura executável real Unreal Binaries/Win64|WinGDK; varre até profundidade5/20000itens, identifica upscalers/anti-cheat, instalação própria/estrangeira e quirks.

**Leituras, escritas e processos:** Lê árvore e manifestos; não injeta DLL nem inicia jogo.

**Retorno:** {success,exe,dir,notes,upscalers,block,antiCheat,installed,foreign} ou erro.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `optiAnalyze` (L8289).

### `opti-detect`

Evidência: backend-focused.js:L8700–L8736; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Inicia FPS; espera alvo até10x300ms. Consulta Path do PID por PowerShell. Exige jogo conhecido, processo com flip, ou pasta de biblioteca/FiveM. Chama optiAnalyze e acrescenta detected.

**Leituras, escritas e processos:** Capturador/processos e leitura de arquivos; não instala.

**Retorno:** Análise mais detected ou {success:false,error}.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `fpsStart` (L7426); `fpsTarget` (L7332); `execPS` (L747); `allGames` (L2486); `procMatchers` (L2795); `optiAnalyze` (L8289).

### `opti-install`

Evidência: backend-focused.js:L8736–L8745; requireAuth.

**Entrada:** {exe,dll?,opts?,acceptAntiCheat?}

**Validação e fluxo:** Serializa optiInstall. Reanalisa exe; bloqueia jogo conhecido online, exige aceite quando anti-cheat detectado, rejeita instalação existente/estrangeira/jogo rodando. DLL fora OPTI_DLLS vira dxgi.dll; obtém pacote local ou download; sanitiza opts. Cria manifesto/backup, move arquivos existentes, copia pacote e escreve OptiScaler.ini; falha tenta optiRemoveFiles.

**Leituras, escritas e processos:** Instala DLL proxy/arquivos na pasta do jogo, backups e manifesto, persiste installs. Nada foi instalado nesta análise.

**Retorno:** {success:true,dir,manifest} ou erro.

**Reversão:** opti-uninstall usa manifesto para remover adicionados/restaurar backups; rollback em falha é tentado, não comprovado em execução.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `withActionLock` (L4312); `optiInstall` (L8527).

### `opti-uninstall`

Evidência: backend-focused.js:L8745–L8758; requireAuth.

**Entrada:** {dir:string}

**Validação e fluxo:** Serializa; exige caminho drive absoluto, lê manifesto. Sem manifesto só remove registro local da instalação. Com manifesto exige jogo fechado, optiRemoveFiles; se arquivos em uso retorna erro.

**Leituras, escritas e processos:** Remove arquivos registrados e restaura backups; atualiza Settings após sucesso.

**Retorno:** {success:true,message?} ou {success:false,error}.

**Reversão:** opti-install reinstala; não existe histórico múltiplo de instalações.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `withActionLock` (L4312); `optiUninstall` (L8629).

### `opti-config`

Evidência: backend-focused.js:L8758–L8783; requireAuth.

**Entrada:** {dir:string,opts:objeto}

**Validação e fluxo:** Exige dir em settings.opti.installs e manifesto válido. Combina opts anteriores/novas e optiSanitizeOpts; optiIniValues considera hardware/quirk do exe; escreve ini e manifest.json, consulta exeRunning.

**Leituras, escritas e processos:** Altera OptiScaler.ini/manifesto; não exige jogo fechado neste handler.

**Retorno:** {success:true,opts,running} ou erro de gravação.

**Reversão:** Pode regravar opções antigas se conhecidas; sem snapshot por edição.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `optiManifest` (L8270); `optiSanitizeOpts` (L8460); `optiWriteIni` (L8406); `optiIniValues` (L8372); `getCaps` (L2053); `exeRunning` (L8279).

### `opti-open-folder`

Evidência: backend-focused.js:L8783–L8790; requireAuth.

**Entrada:** {dir}

**Validação e fluxo:** Só chama shell.openPath se dir string registrada em settings.opti.installs; não aguarda resultado e retorna sucesso até para dir inválida.

**Leituras, escritas e processos:** Abre pasta quando aceita.

**Retorno:** {success:true} não comprova abertura.

**Reversão:** Fechar Explorer.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289).

### `fivem-gfx-status`

Evidência: backend-focused.js:L8863–L8885; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** getCaps, fivemXml, verifica FiveM.exe LOCALAPPDATA, backup e processo; lê XML e xmlGet para FIVEM_KEYS, inferindo GPU/preferência.

**Leituras, escritas e processos:** Leituras de configuração/hardware/processos.

**Retorno:** {success:true,installed,file,backup,running,gpu,gpuName,values,preset}.

**Reversão:** Não aplicável.

**Acesso:** ALWAYS_CHANNELS permite no gate de plano; requireAuth ainda vale se indicado.

**Dependências nomeadas:** `requireAuth` (L1289); `getCaps` (L2053); `fivemXml` (L8842); `fivemRunning` (L8859); `xmlGet` (L8851).

### `fivem-gfx-apply`

Evidência: backend-focused.js:L8885–L8928; requireAuth.

**Entrada:** {preset?,scaling?,borderless?}

**Validação e fluxo:** Serializa; exige XML localizado e FiveM fechado. preset conhecido ou null/undefined; scaling inteiro índice de FIVEM_SCALING. Cria backup inicial se ausente; preset altera chaves, scaling altera SamplingMode, borderless bool define Windowed2 ou0. Grava .gdz-tmp e rename para XML.

**Leituras, escritas e processos:** Muda configuração gráfica FiveM e settings.fivemPreset; backup inicial preservado.

**Retorno:** {success:true} ou erro.

**Reversão:** fivem-gfx-restore copia backup e o remove.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `withActionLock` (L4312); `fivemXml` (L8842); `fivemRunning` (L8859); `xmlSet` (L8855); `saveSettings` (L2849).

### `fivem-gfx-restore`

Evidência: backend-focused.js:L8928–L8950; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Serializa; usa XML encontrado ou primeiro FIVEM_XML, exige backup e FiveM fechado; copia backup, remove arquivo backup e fivemPreset, salva Settings.

**Leituras, escritas e processos:** Restaura XML inicial; consome backup.

**Retorno:** {success:true} ou erro.

**Reversão:** É restauração; nova edição recria backup do estado corrente.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289); `withActionLock` (L4312); `fivemXml` (L8842); `fivemRunning` (L8859); `saveSettings` (L2849).

### `open-gpu-panel`

Evidência: backend-focused.js:L8950–L8980; requireAuth.

**Entrada:** {vendor:'nvidia'|'amd'}

**Validação e fluxo:** NVIDIA executa explorer shell:AppsFolder com ID fixo do painel; AMD exige RadeonSoftware.exe em ProgramFiles/AMD/CNext/CNext e execFile. Outros vendors rejeitados.

**Leituras, escritas e processos:** Abre painel externo do driver; não muda configuração gráfica diretamente.

**Retorno:** {success:true} significa tentativa de abrir; ou erro para inexistência/vendor.

**Reversão:** Fechar painel; alterações feitas nele ficam fora deste handler.

**Acesso:** Exige currentUser e feature * no gate global, se o handler estiver registrado.

**Dependências nomeadas:** `requireAuth` (L1289).

### `pcinfo-get`

Evidência: backend-focused.js:L6853–L6877; requireAuth.

**Entrada:** {force?} opcional

**Validação e fluxo:** Retorna _pcInfo em cache, exceto se force truthy; usa _pcInfoP para juntar concorrentes. Promise.all PCINFO_PS45s+nvidiaStatic, parse GDZ_PCINFO e buildPcInfo.

**Leituras, escritas e processos:** Consulta ampla hardware/Windows e NVIDIA; cache em memória.

**Retorno:** {success:true,info} ou {success:false,error}.

**Reversão:** Não aplicável.

**Acesso:** Exige currentUser e feature * ou hardware.

**Dependências nomeadas:** `requireAuth` (L1289); `execPS` (L747); `nvidiaStatic` (L6786); `buildPcInfo` (L6622).

### `pcinfo-live`

Evidência: backend-focused.js:L7217–L7242; requireAuth.

**Entrada:** sem payload

**Validação e fluxo:** Atualiza lastUse e pcLiveStart. Combina CPU/per-core MHz/temp, RAM, métricas NVIDIA se houver ou GPU Windows por LUID, discos.

**Leituras, escritas e processos:** Inicia/renova coletor temporário de métricas; não altera clocks.

**Retorno:** {success:true,cpu,ram,gpus,disks}.

**Reversão:** pcLiveStop ocorre ao encerrar e coletor tem regra de inatividade no helper; sem canal stop separado.

**Acesso:** Exige currentUser e feature * ou hardware.

**Dependências nomeadas:** `requireAuth` (L1289); `pcLiveStart` (L7172).

## Send: 9 comandos sem resposta

### `minimize-window`

Evidência: backend-focused.js:L862–L862; sem requireAuth no handler.

**Entrada:** send sem payload

**Validação e fluxo:** Se mainWindow existe chama minimize().

**Leituras, escritas e processos:** Minimiza janela principal.

**Retorno:** Sem resposta.

**Reversão:** Restaurar pela barra de tarefas/bandeja.

**Acesso:** ipcMain.on não passa pelo wrapper de ipcMain.handle; validações próprias descritas.

### `maximize-window`

Evidência: backend-focused.js:L862–L865; sem requireAuth no handler.

**Entrada:** send sem payload

**Validação e fluxo:** Se mainWindow existe alterna isMaximized ? restore : maximize.

**Leituras, escritas e processos:** Alterna tamanho da janela.

**Retorno:** Sem resposta.

**Reversão:** Mesmo canal alterna de volta.

**Acesso:** ipcMain.on não passa pelo wrapper de ipcMain.handle; validações próprias descritas.

### `close-window`

Evidência: backend-focused.js:L865–L865; sem requireAuth no handler.

**Entrada:** send sem payload

**Validação e fluxo:** Se mainWindow existe chama close(); ciclo de fechamento da aplicação decide encerramento.

**Leituras, escritas e processos:** Solicita fechamento da janela; helpers podem encerrar coletores no ciclo de saída.

**Retorno:** Sem resposta.

**Reversão:** Reabrir programa.

**Acesso:** ipcMain.on não passa pelo wrapper de ipcMain.handle; validações próprias descritas.

### `load-dashboard`

Evidência: backend-focused.js:L1311–L1325; sem requireAuth no handler.

**Entrada:** send sem payload

**Validação e fluxo:** Exige currentUser; loadFile dashboard.html. Se overlay habilitado inicia FPS/overlay; registra hotkey; did-finish-load injeta correção de wheel/foco/tabindex/MutationObserver para scroll.

**Leituras, escritas e processos:** Navega UI e pode iniciar medição/overlay; injeta JS próprio no renderer local.

**Retorno:** Sem resposta.

**Reversão:** logout navega login e fecha overlay.

**Acesso:** ipcMain.on não passa pelo wrapper de ipcMain.handle; validações próprias descritas.

**Dependências nomeadas:** `getSrcPath` (L150); `fpsStart` (L7426); `overlayOpen` (L7535); `fpsRegisterHotkey` (L7402).

### `logout`

Evidência: backend-focused.js:L1325–L1333; sem requireAuth no handler.

**Entrada:** send sem payload

**Validação e fluxo:** Lê auth, zera currentUser, fecha overlay, clearAuth; se havia sessão POST /app/logout sem aguardar erro; navega login.html.

**Leituras, escritas e processos:** Limpa sessão local, solicita fim remoto e fecha overlay; não reverte ajustes Windows no handler.

**Retorno:** Sem resposta.

**Reversão:** login cria nova sessão; ajustes do sistema requerem reversão separada.

**Acesso:** ipcMain.on não passa pelo wrapper de ipcMain.handle; validações próprias descritas.

**Dependências nomeadas:** `loadAuth` (L186); `overlayClose` (L7586); `clearAuth` (L207); `apiCall` (L222); `getSrcPath` (L150).

### `open-scaling-window`

Evidência: backend-focused.js:L443–L443; sem requireAuth no handler.

**Entrada:** send sem payload

**Validação e fluxo:** Chama openScalingWindow sem requireAuth nem gate invoke. Se exe ausente mostra erro; se auxiliar vivo tenta focar janela; senão spawn detached GDZOptimizer.exe e retitula janela com retries.

**Leituras, escritas e processos:** Abre/foca auxiliar nativo e executa PowerShell para título/foco; não grava opções de scaling por este send.

**Retorno:** Sem resposta IPC.

**Reversão:** gs-close/ls-stop encerram auxiliar; não restauram XML.

**Acesso:** ipcMain.on não passa pelo wrapper de ipcMain.handle; validações próprias descritas.

**Dependências nomeadas:** `openScalingWindow` (L393).

### `gs-minimize`

Evidência: backend-focused.js:L702–L702; sem requireAuth no handler.

**Entrada:** send sem payload

**Validação e fluxo:** Handler vazio.

**Leituras, escritas e processos:** Nenhum.

**Retorno:** Sem resposta.

**Reversão:** Não aplicável.

**Acesso:** ipcMain.on não passa pelo wrapper de ipcMain.handle; validações próprias descritas.

### `gs-maximize`

Evidência: backend-focused.js:L702–L702; sem requireAuth no handler.

**Entrada:** send sem payload

**Validação e fluxo:** Handler vazio.

**Leituras, escritas e processos:** Nenhum.

**Retorno:** Sem resposta.

**Reversão:** Não aplicável.

**Acesso:** ipcMain.on não passa pelo wrapper de ipcMain.handle; validações próprias descritas.

### `gs-close`

Evidência: backend-focused.js:L702–L711; sem requireAuth no handler.

**Entrada:** send sem payload

**Validação e fluxo:** Se lsProcess existe tenta kill e zera referência; exceções ignoradas.

**Leituras, escritas e processos:** Tenta encerrar processo filho que ainda está referenciado; não procura todos auxiliares por nome.

**Retorno:** Sem resposta.

**Reversão:** open-scaling-window reabre.

**Acesso:** ipcMain.on não passa pelo wrapper de ipcMain.handle; validações próprias descritas.

## On: 4 eventos que a interface pode escutar

### `fps-update`

**Comportamento:** Nenhum emissor encontrado no backend examinado. Canal permitido no preload é insuficiente para provar evento implementado.

**Payload:** Payload não definido por emissor neste backend.

**Evidência:** backend-focused.js: nenhum emissor localizado.

### `overlay-update`

**Comportamento:** overlayOpen envia somente à janela fps.overlay a cada500ms; atualiza métricas e moveTop. Timer para ao destruir/fechar overlay.

**Payload:** {live:fpsLive(fpsTarget()),sys:sensorsSnapshot(),show:settings.fpsOverlay.show,right:boolean,bottom:boolean}.

**Evidência:** backend-focused.js: L7577.

### `entitlement-update`

**Comportamento:** claim-key envia currentUser após resgate aceito. syncPlan envia após alteração de plano/features/validade/lifetime/PC; inclui bought quando detecta compra/upgrade. Só em janela principal existente não destruída.

**Payload:** currentUser, ou {...currentUser,bought:boolean} no syncPlan.

**Evidência:** backend-focused.js: L1013, L1161.

### `update-status`

**Comportamento:** sendUpdateState emite updateState à janela principal em mudanças de download/progresso/erro/ready; update-install é outro canal com o mesmo sistema. Algumas transições atribuem estado sem emitir imediatamente.

**Payload:** {status:idle|downloading|ready,version,error,percent?}. Evento não acrescenta current; invoke update-status acrescenta versão corrente.

**Evidência:** backend-focused.js: L1184.

## Limites da reversão e evidências

Os canais de consulta e medição não são equivalentes a alterações persistentes. Limpeza de caches/RAM não possui undo de conteúdo neste código; desinstalação depende de executável externo; sobras de diretórios usam Lixeira e chaves usam export .reg; alterações de ação usam snapshot/REVERT; FiveM e OptiScaler têm seus próprios backups. Cada uma dessas rotinas tem condições e falhas parciais descritas acima.

A lista de ações, tweaks, ferramentas, categorias de limpeza e presets continua nos catálogos específicos. run-action/tweak-set despacham esses roteiros; não foram reduzidos a uma promessa genérica de otimização. Os handlers e seus helpers foram lidos como texto/AST, sem execução nem teste de impacto.

Reprodução da análise: `node artifacts/private/gdz-analysis/deep/extract-ipc.cjs` e `python artifacts/private/gdz-analysis/deep/build_ipc_catalogue.py`. Esses scripts próprios fazem leitura de fonte, extraem metadados e geram relatório/JSON; não importam app-main.js nem falam com o GDZ.

## Incompatibilidades confirmadas na ponte de scaling

Esses casos vêm da comparação dos corpos de scalingFromUi e scalingWrite com a assinatura gerenciada, sem teste do auxiliar:

| Entrada/conversão | O que o gravador realmente aceita | Consequência observada no fluxo |
|---|---|---|
| perfMode → LSFGSize=PERFORMANCE/BALANCED | LSFGSize não está em SCALING_ENUM, SCALING_RANGE, SCALING_BOOL ou SCALING_GLOBAL | A opção é ignorada silenciosamente, sem entrar em skipped. |
| fsrType fsr1/fsr2/fsr3 → FSR1/FSR2/FSR3 | FSRType aceita somente ORIGINAL | Valores produzidos por esse mapa são recusados e FSRType entra em skipped. |
| scaleType nis/integer/nearest/xbr/anime4k → NIS/INTEGER/NN/XBR/ANIME4K | ScalingType só aceita Off,LS1,FSR | Esses valores são recusados, mesmo existindo mais algoritmos no assembly WPF. |
| fitMode fullscreen/stretch/fit → Full/Full/Fit | ScalingFitMode só aceita AspectRatio | São recusados; aspect→AspectRatio é compatível. |
| sharpness de UI → Sharpness | ApplyProfileToCore escolhe LS1Sharpness quando algoritmo=LS1 | Essa conversão não modifica LS1Sharpness. Não equivale a demonstrar mudança de nitidez LS1. |

Os ranges de scalingWrite são: LSFG3Multiplier2..4, LSFG3Target30..480, LSFGFlowScale25..100, QueueTarget0..3, Sharpness0..10, LS1Sharpness0..10, MaxFrameLatency1..10, ScaleFactor1..4. Números finitos são limitados ao range e arredondados; ScaleFactor tem duas casas. Só tags já existentes são substituídas. Os parâmetros aceitos pela ponte são, portanto, um subconjunto das opções do auxiliar.
