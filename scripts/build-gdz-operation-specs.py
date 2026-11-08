#!/usr/bin/env python3
"""Build an implementation-oriented catalogue from parse-only GDZ evidence.

Does not execute target code, PowerShell, IPC, DLLs or optimizer actions.
"""
from __future__ import annotations
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEEP = ROOT / 'artifacts/private/gdz-analysis/deep'
data = json.loads((DEEP / 'operations-declarative.json').read_text(encoding='utf-8'))
features = json.loads((DEEP / 'frontend-features.json').read_text(encoding='utf-8'))

def cat(name):
    return {e['key']: e for e in data['catalogs'][name]['entries']}

def fields(entry):
    return {e['key']: e['value'] for e in entry['fields']}

def flat(name):
    return {e['key']: e['value'] for e in data['expanded'].get(name, data['catalogs'][name]['entries'])}

def fmt(value):
    if value is None:
        return 'remover valor'
    return json.dumps(value, ensure_ascii=False, separators=(',', ':'))

def cell(value):
    return str(value).replace('|', '\\|').replace('\n', '<br>').replace('\r', '')

def ref(ev):
    return f"`{ev['file']}:{ev['line']}`"

notes = {
    'Otimização Rápida': 'Combina plano HIGH (omitido em x3dDual), Modo Jogo, DVR desligado, aceleração do mouse desligada e temporários com mais de 1 dia. O backup não recupera arquivos apagados.',
    'Plano de Alta Performance': 'Seleciona HIGH; se falhar, tenta duplicar o mesmo GUID e selecionar novamente. Só avisa erro se a segunda seleção falhar.',
    'Modo Máximo Desempenho': 'Mesmo procedimento com ULTIMATE. Não cria ajustes específicos para cada jogo.',
    'SysMain Desativado': 'Para SysMain e troca o tipo de inicialização para Disabled.',
    'Desativar Telemetria': 'Desativa/para DiagTrack e define AllowTelemetry=0; este comando não altera a telemetria própria do GDZ.',
    'Game Bar Desativado': 'Desliga captura/DVR e UseNexusForGameBarEnabled, e cria política AllowGameDVR=0.',
    'Game Bar Removido': 'Aplica a desativação, encerra processos da Game Bar, remove XboxGamingOverlay do usuário e cria protocolos substitutos apontando para systray.exe se o pacote sumiu. Pode abrir a Store na reversão.',
    'Serviços Xbox Desativados': 'Para e desativa XblAuthManager, XblGameSave e XboxNetApiSvc; não inclui todo serviço que contenha Xbox.',
    'HAGS Ativo': 'Define GraphicsDrivers\\HwSchMode DWord=2. A reversão legada grava 1.',
    'Desativar Notificações': 'Define ToastEnabled DWord=0 no usuário atual.',
    'Efeitos Visuais Off': 'Define VisualFXSetting=2 e máscara binária 90 12 03 80 10 00 00 00. A volta legada usa máscara fixa 9E 1E 07 80 12 00 00 00.',
    'Serviços Desativados': 'Para/desativa Spooler, Fax, RemoteRegistry, MapsBroker e WbioSrvc. A volta legada seleciona Manual, não o estado anterior.',
    'Indexação Off': 'Para e desativa WSearch.',
    'UWP Removidos': 'Remove os 10 pacotes da lista UWP_APPS do usuário atual. Em x3dDual preserva Xbox.TCUI/XboxGamingOverlay. Não existe snapshot dos pacotes nem comando reverso correspondente.',
    'Temporários Removidos': 'Apaga filhos de TEMP e C:\\Windows\\Temp com LastWriteTime anterior a 1 dia; pode remover uma pasta inteira cujo filho seja recente.',
    'Temporários Limpos': 'Alias com script idêntico a Temporários Removidos.',
    'DNS Otimizado': 'Ação antiga: DNS IPv4 1.1.1.1 e 8.8.8.8 em todos os adaptadores Up. Distingue-se do seletor atual com quatro provedores.',
    'Apps de Sincronização Fechados': 'Encerra OneDrive, Dropbox, GoogleDriveFS e BackgroundTransferHost; não salva nem reabre o trabalho desses processos.',
    'Desativar Mouse Aceleração': 'Grava MouseSpeed/Threshold1/Threshold2 como strings 0 e aplica SystemParametersInfo SPI_SETMOUSE (4).',
    'Limpar Inicialização': 'Desabilita somente Discord, Spotify, OneDrive, Steam e EpicGamesLauncher existentes em HKCU Run, por bytes StartupApproved iniciados por 3.',
    'Reduzir Input Lag': 'Combinação antiga de aceleração do mouse desligada e flags de atalhos de acessibilidade desligadas.',
    'Modo Jogo': 'Define AllowAutoGameMode e AutoGameModeEnabled como DWord=1.',
    'Chrome/Discord Limitados': 'Seleciona BelowNormal para os processos chrome e Discord que já estão abertos; não aplica limite de RAM, CPU ou rede.',
    'Explorer Mínimo': 'Encerra explorer, espera 600 ms e o reabre. Não mantém o shell parado nem configura um modo persistente.',
    'DVR Desativado': 'Define AppCaptureEnabled e GameDVR_Enabled como DWord=0.',
    '720p Boost Ativo': 'Salva somente largura/altura primárias em HKCU\\Software\\GDZ e chama ChangeDisplaySettings para 1280×720, dmFields=0x180000. Não registra frequência/rotação/monitor; ignora retorno Win32.',
    'Todos Tweaks Aplicados': 'Nome histórico: aplica apenas aceleração do mouse, acessibilidade e Modo Jogo; adiciona otimização de janela se build≥22000. Não aplica os 33 TWEAKS.',
    'Jogos em Janela Otimizados': 'Preserva outras opções DirectXUserGlobalSettings, remove tokens SwapEffectUpgradeEnable e acrescenta SwapEffectUpgradeEnable=1;.',
    'Sem Upload P2P': 'Define DODownloadMode DWord=0 na política DeliveryOptimization.',
    'Sem Reinício Durante o Jogo': 'Define NoAutoRebootWithLoggedOnUsers DWord=1; não observa se um jogo está aberto.',
    'Correção de Flicker (MPO)': 'Ação antiga define somente Dwm\\OverlayTestMode=5. O tweak mpo atual também define GraphicsDrivers\\DisableOverlays=1.',
    'Sem Atalhos de Acessibilidade': 'Flags String: StickyKeys=506, ToggleKeys=58, Keyboard Response=122.',
    'Pagefile Automático': 'Set-CimInstance Win32_ComputerSystem AutomaticManagedPagefile=true; avisa e sai se já estiver ativo. O spec registra PagingFiles, não o valor CIM diretamente.',
    'Modo X3D Correto': 'Seleciona Balanced, liga Modo Jogo/Nexus e remove política de bloqueio DVR. Se faltar Game Bar abre Store; se faltar amd3dvcacheSvc avisa instalar chipset. Não instala esses componentes.',
    'OBS Prioritário': 'Acrescenta RUNASADMIN ao AppCompatFlags Layers do OBS e desativa powerthrottling para seu executável. Não muda PriorityClass do OBS. Snapshot só Layers; extra de volta reseta powerthrottling ao padrão.',
    'Padrões Restaurados': 'Restaura valores escolhidos pelo autor para ajustes antigos de GPU/prioridade/HID/TCP/BCD/mouse/fontes. Não equivale a restaurar uma imagem do Windows nem todos os ajustes do GDZ.',
}

ui_actions = {e['id'] for e in features['pages_entries'] if e['constructor'] in ('A', 'O')}
actions, specs, detectors, extras = flat('ACTIONS'), flat('SPECS'), flat('DETECT'), flat('REVERT_EXTRA')
reverts = data['finalRevert']
page_by_id = {e['id']: e for e in features['pages_entries'] if e['constructor'] in ('A', 'O')}
operations = []
for name, action in cat('ACTIONS').items():
    page = page_by_id.get(name)
    conditions = []
    if name in ('Plano de Alta Performance', 'Modo Máximo Desempenho', 'Game Bar Desativado', 'Game Bar Removido'):
        conditions.append('Bloqueado quando caps.x3dDual=true.')
    if name == 'Modo X3D Correto':
        conditions.append('Permitido somente quando caps.x3dDual=true.')
    if name == 'OBS Prioritário':
        conditions.append('Requer caps.obsExe válido/encontrado.')
    if name == 'Jogos em Janela Otimizados':
        conditions.append('Bloqueado se build conhecido e menor que 22000.')
    operations.append({
        'id': name, 'kind': 'base-action', 'request': {'channel': 'run-action', 'payload': {'action': name}},
        'inPages': name in ui_actions, 'summary': notes[name], 'conditions': conditions,
        'uiCapability': page.get('capability') if page else None,
        'applicationSource': action['value'], 'applyScriptSyntheticScenario': actions[name],
        'snapshotSpecSyntheticScenario': specs.get(name), 'fallbackRevert': reverts.get(name),
        'revertExtraSyntheticScenario': extras.get(name), 'stateProbeSyntheticScenario': detectors.get(name),
        'applicationReportsOnWithoutRunningDetector': True,
        'postSnapshotIsRecordedButNotCompared': True,
        'evidence': action['evidence'], 'effectsVerified': False,
    })
for raw, expanded, derived in zip(data['catalogs']['TWEAKS']['entries'], data['expanded']['TWEAKS'], data['derivedTweaks'], strict=True):
    values, ex = fields(raw), fields(expanded)
    operations.append({
        'id': 'Tweak: ' + values['id'], 'kind': 'tweak', 'tweakId': values['id'], 'title': values['title'],
        'request': {'channel': 'tweak-set', 'payload': {'id': values['id'], 'on': True}},
        'inPages': True, 'fields': values, 'fieldsSyntheticScenario': ex,
        'snapshotSpecSyntheticScenario': derived['observations']['tweakSpec'],
        'stateProbeSyntheticScenario': derived['observations']['tweakProbeExpr'],
        'applyScriptSyntheticScenario': derived['observations']['tweakScriptOn'],
        'offScriptSyntheticScenario': derived['observations']['tweakScriptOff'],
        'offFlow': 'Restore saved snapshot if present; query probe; if still on, apply declared off defaults and query again. A previous optimized state can therefore be changed to defaults.',
        'evidence': raw['evidence'], 'effectsVerified': False,
    })
orphan_reverts = sorted(set(reverts) - set(actions))
patterns = [
    {'pattern': '^Otimizar .{1,60}$', 'request': 'run-action', 'effects': 'GENERIC_GAME_OPT', 'snapshot': data['constants']['GENERIC_GAME_SPEC']['value'], 'bindingToActualGameRequired': False, 'evidence': {'file': 'deep/backend-focused.js', 'line': 4340}},
    {'pattern': '^GPU Dedicada: ([a-z0-9-]+)$', 'request': 'run-action', 'effects': "UserGpuPreferences[each discovered exe]='GpuPreference=2;'", 'snapshot': 'Each per-exe value with original type/existence/content', 'conditions': 'Recognized game id, hybrid GPU, discovered executable(s).', 'evidence': {'file': 'deep/backend-focused.js', 'line': 2801}},
]
spec = {
    'schema': 1, 'version': '1.1.27', 'source': data['source'],
    'scope': 'Implementation input inferred from static code. Synthetic examples do not describe this PC. No Windows changes or measured benefit.',
    'counts': {'baseActions': 36, 'tweaks': 33, 'namedApplyOperations': 69, 'actionPatterns': 2,
               'baseActionsAbsentFromPages': len(set(actions) - ui_actions), 'revertOnlyLegacyNames': len(orphan_reverts),
               'revertNamesFinal': len(reverts), 'cleanCategories': 16, 'knownGames': 12, 'dnsProviders': 4, 'fiveMPresets': 2,
               'registryRowsForSyntheticScenario': sum(len(fields(e).get('reg', [])) for e in data['expanded']['TWEAKS'])},
    'syntheticScenario': data['syntheticScenario'], 'operations': operations, 'patterns': patterns,
    'legacyRevertOnly': [{'name': n, 'script': reverts[n], 'evidence': cat('REVERT').get(n, {}).get('evidence')} for n in orphan_reverts],
    'cleanCategories': [dict(fields(e), evidence=e['evidence']) for e in data['catalogs']['CLEAN_CATS']['entries']],
    'games': [dict(fields(e), evidence=e['evidence']) for e in data['catalogs']['GAMES']['entries']],
    'dnsProviders': {k: e['value'] for k, e in cat('DNS_PROVIDERS').items()},
    'fiveMPresets': {k: e['value'] for k, e in cat('FIVEM_PRESETS').items()},
    'fiveMScaling': data['constants']['FIVEM_SCALING']['value'],
    'capabilityMatrixSynthetic': data['capabilityMatrixSynthetic'],
    'tools': [{'id': k, **e['value'], 'evidence': e['evidence']} for k, e in cat('TWEAK_TOOLS').items()],
}
(DEEP / 'operation-specs.json').write_text(json.dumps(spec, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

out = [
    '# GDZ Optimizer 1.1.27 — catálogo de operações para implementação', '',
    'Esta etapa transforma os nomes da interface em alvos concretos: comandos, valores de registro, pré-condições, snapshot, reversão e leitura do resultado. A interface do VL Optimizer não foi alterada e nenhum recurso do aplicativo novo foi construído.', '',
    'A fonte é o backend decodificado com SHA-256 `' + data['source']['sha256'] + '`. Todas as linhas abaixo apontam para `artifacts/private/gdz-analysis/deep/backend-focused.js`. A recuperação usa AST e um interpretador restrito de expressões puras; não carrega o programa, não dispara IPC, não executa os comandos produzidos e não consulta arquivos de sessão.', '',
    '**O que é novo:** 36 ações base + 33 tweaks registrados no mesmo dicionário = **69 nomes aplicáveis**, além de **2 padrões dinâmicos**. Destes 36, **13 não aparecem em PAGES**; vários são aliases/combinações antigas. Existem ainda **8 nomes apenas de reversão legada**: não devem ser anunciados como capacidades atuais de aplicar. As contagens se sobrepõem aos 177 itens de interface já catalogados e não podem ser somadas para anunciar um total de recursos.', '',
    'Os JSONs privados `operations-declarative.json` e `operation-specs.json` preservam os comandos completos e campos individuais. Expansões dinâmicas usam **capacidades sintéticas**, incluindo GPU/NIC e caminhos `C:\\Example`; nenhum desses exemplos foi descoberto neste computador.', '',
    '## 1. Nomes aceitos pelo executor de ações', '',
    '| Nome exato | Mecanismo recuperado | Itens no snapshot de exemplo | Detector | Em PAGES | Fonte |',
    '|---|---|---:|---|---|---|',
]
for op in operations[:36]:
    snapshot = op['snapshotSpecSyntheticScenario']
    out.append('| ' + ' | '.join(map(cell, [f"`{op['id']}`", op['summary'], len(snapshot) if snapshot is not None else 'nenhum spec', 'sim' if op['stateProbeSyntheticScenario'] else 'não', 'sim' if op['inPages'] else 'não', ref(op['evidence'])])) + ' |')
out += ['', 'As 13 ausentes de PAGES são alcançáveis estaticamente pelo `run-action`, sujeito à autenticação e regras do executor. Isso não prova que exista um botão ativo para cada uma. “Otimização Rápida” é usada pelo dashboard, apesar de não ser uma entrada PAGES.', '',
        '**Bloqueios do executor:** em `x3dDual`, impede HIGH, ULTIMATE e desativar/remover Game Bar; fora de `x3dDual`, impede Modo X3D Correto. Otimização em janela exige build conhecido ≥22000, e OBS exige executável encontrado. Fonte: `backend-focused.js:2077`. A detecção X3D usa modelos específicos, incluindo 7945HX3D, apesar de parte do texto exibido mencionar só modelos desktop.', '',
        '## 2. Os 33 tweaks e todos os seus valores', '',
        'Cada linha de registro tem `[caminho, nome, tipo, valor ligado, padrão desligado]`. `remover valor` corresponde a `null`: desativar sem snapshot remove a propriedade. **Com snapshot, o fluxo tenta restaurar o valor anterior primeiro; depois pode aplicar o padrão desligado caso o probe ainda indique ligado.** Portanto o padrão da tabela não substitui o estado salvo e desligar não é sempre uma restauração exata.', '',
        'A amostra sintética expande **59 linhas de registro**, **1 linha de serviço** e **2 ajustes DRS**. O número de linhas reais varia com as NICs/GPU encontradas. A raiz GPU `...PCI\\EXAMPLE\\0...` e NIC `0001` são marcadores explicativos.', '']
for op in operations[36:]:
    values, ex = op['fields'], op['fieldsSyntheticScenario']
    out += [f"### `{op['tweakId']}` — {op['title']}", '',
            f"Requisição: `tweak-set` com `id={op['tweakId']}` e `on=true/false`. Fonte: {ref(op['evidence'])}.", '']
    rows = ex.get('reg', [])
    if rows:
        out += ['| Caminho | Nome | Tipo | Ligado | Padrão desligado |', '|---|---|---|---|---|']
        for row in rows:
            out.append('| ' + ' | '.join(cell(f'`{x}`') if i < 3 else cell(fmt(x)) for i, x in enumerate(row)) + ' |')
        out.append('')
    if ex.get('svc'):
        out.append('Serviço: ' + '; '.join(f'`{s[0]}`: ligado `{s[1]}`, padrão desligado `{s[2]}`' for s in ex['svc']) + '.\n')
    if ex.get('drs'):
        out.append('NVIDIA DRS: ' + '; '.join(f'ID `{d[0]}` (`0x{d[0]:08X}`) → `{d[1]}` (`0x{d[1]:08X}`)' for d in ex['drs']) + '. Sem snapshot, desligar remove os overrides; com snapshot restaura os overrides anteriores.\n')
    dynamic = [k for k, v in values.items() if isinstance(v, dict) and 'function' in v]
    if dynamic:
        out.append('Campos dependentes de capacidade: ' + ', '.join(f'`{d}`' for d in dynamic) + '. As funções originais e exemplo expandido constam no JSON.\n')
    if values.get('restart'):
        out.append(f"Aplicação adicional declarada: `{values['restart']}`.\n")
    if op['tweakId'] == 'steam-overlay':
        out.append('Edita EnableGameOverlay no bloco system de cada `Steam\\userdata\\*\\config\\localconfig.vdf`. Exige conta existente e nenhum RunningAppID>0. Solicita Steam `-shutdown`, espera no máximo 40×500 ms, grava UTF-8 sem BOM e o reabre somente se estava aberto. Não há snapshot dos VDF; desligar escreve 1.\n')
    elif op['tweakId'] == 'classic-menu':
        out.append('Cria a subchave `InprocServer32` de `HKCU:\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` e seu valor padrão vazio. Desligar remove recursivamente a raiz desse CLSID. Não há snapshot dessa chave na declaração; reinicia Explorer.\n')
    elif op['tweakId'] in ('lso', 'eee', 'nic-intmod'):
        out.append('Reinicia adaptadores correspondentes ao NetCfgInstanceId depois da escrita; ignora adaptadores Disabled. `eee` e `nic-intmod` voltam para `Ndi\\Params\\<keyword>\\default` quando disponível, senão string 1. No LSO, o padrão desligado fixo é string 1; IPv6 só aparece se suportado.\n')
    elif op['tweakId'] == 'gpu-msi':
        out.append('O caminho é da primeira GPU dedicada NVIDIA/AMD selecionada em buildCaps; não de todas as GPUs. Deve ser resolvido e validado por PNPDeviceID antes de usar o modelo de chave acima.\n')
    elif op['tweakId'] == 'end-task':
        out.append('Bloqueado se build conhecido <22631 (Windows 11 23H2); não basta o requisito Windows 11 genérico.\n')
    probe = op['stateProbeSyntheticScenario']
    out += ['Probe exato da amostra:', '', '```powershell', probe, '```', '']

out += ['### Pré-condições e matriz de capacidades', '',
        'Foram expandidos **198 casos estáticos** (33 tweaks × 6 conjuntos sintéticos de capacidades). Isso testa a interpretação das declarações; não é um ensaio dos comandos nem da compatibilidade no Windows.', '',
        '| Tweaks com bloqueio | Regra exata |', '|---|---|',
        '| gpu-msi | Requer caps.gpuIrq não vazio. |',
        '| dlss-rec | Requer caps.rtx e caps.nvapi. |',
        '| steam-overlay | Requer caps.steamPath não vazio. |',
        '| lso | Requer caps.nicLso.length > 0. |',
        '| eee | Requer pelo menos uma nicOpt cujo nome esteja em *EEE, EEE, AdvancedEEE, EEELinkAdvertisement, EnableGreenEthernet. |',
        '| nic-intmod | Requer nicOpt com nome *InterruptModeration. |',
        '| end-task | Bloqueia build conhecido <22631. |',
        '| classic-menu / taskbar-left | Bloqueia build conhecido <22000. |', '',
        'Build igual a 0/desconhecido passa pelas três regras de versão acima; isso é comportamento do código, não garantia de suporte. Os demais 24 tweaks não têm função block declarada, embora Windows/driver ainda possam recusar a alteração. Hibernate é recomendado apenas quando laptop=false. As demais recomendações constam dos campos rec do inventário.', '',
        '| Perfil sintético | Tweaks bloqueados | Tweaks marcados recomendados |', '|---|---|---:|']
for profile in spec['capabilityMatrixSynthetic']:
    blocked = [e['id'] for e in profile['tweaks'] if e['blocked']]
    recommended = sum(e['recommended'] for e in profile['tweaks'])
    out.append('| ' + ' | '.join(map(cell, [f"`{profile['name']}`", ', '.join(blocked) or 'nenhum', recommended])) + ' |')

out += ['', '## 3. Reversões sem ação atual de aplicação', '',
        'Há 31 propriedades na declaração inicial REVERT. Uma atribuição posterior acrescenta Reduzir Input Lag e outra complementa Efeitos Visuais Off: **32 nomes finais**, dos quais os 8 abaixo não têm ACTIONS correspondente. A API `revert-action` pode recebê-los; isso não torna suas antigas ações de aplicar disponíveis.', '',
        '| Nome legado | O que a volta tenta fazer | Fonte |', '|---|---|---|']
legacy_notes = {
    'Defender Pausado': 'Reativar monitoramento em tempo real via Set-MpPreference.',
    'Defender Off': 'Reativar monitoramento e remover a política DisableAntiSpyware.',
    'Desativar Atualizações Windows': 'Selecionar wuauserv Manual e iniciar o serviço.',
    'System Restore Off': 'Enable-ComputerRestore no volume do sistema.',
    'Paginação Desativada': 'Win32_ComputerSystem.AutomaticManagedPagefile=true via WMI.',
    'Pagefile Ajustado': 'Mesma redefinição da paginação automática.',
    'DPI Scaling Off': 'Remover Win8DpiScaling do Desktop do usuário.',
    'Fonte Baixa Qualidade': 'FontSmoothing string 2 e FontSmoothingType DWord 2.',
}
for item in spec['legacyRevertOnly']:
    out.append('| ' + ' | '.join(map(cell, [f"`{item['name']}`", legacy_notes[item['name']], ref(item['evidence'])])) + ' |')

out += ['', '## 4. Padrões dinâmicos e jogos', '',
        '`run-action` aceita **qualquer texto** que corresponda a `^Otimizar .{1,60}$` quando não existe ação nominal: o sufixo não precisa ser um jogo instalado. O efeito é sempre HIGH (exceto X3D dual), Modo Jogo e DVR desligado, com snapshot desses estados. O programa não gera um perfil técnico diferente para CS2, Valorant ou outro nome nesse caminho.', '',
        '`GPU Dedicada: <id>` exige id reconhecido, GPU híbrida e executáveis encontrados. Grava `GpuPreference=2;` em `HKCU:\\Software\\Microsoft\\DirectX\\UserGpuPreferences`, um valor por caminho de executável, com snapshot. A escrita substitui o conteúdo do valor; não mescla outros tokens existentes.', '',
        'As 12 entradas conhecidas fornecem descoberta e identificação de processo. Além delas, o scanner forma uma biblioteca dinâmica de jogos Steam/Epic. Estas entradas não são 12 algoritmos exclusivos de otimização.', '',
        '| ID | Nome | Descoberta declarada | Executáveis relativos | Processos | Fonte |', '|---|---|---|---|---|---|']
for game in spec['games']:
    discover = {k: game[k] for k in ('steam', 'epic', 'riot', 'uninstall', 'marker', 'appx', 'paths') if k in game}
    out.append('| ' + ' | '.join(map(cell, [f"`{game['id']}`", game['name'], fmt(discover), fmt(game['exe']), fmt(game['proc']), ref(game['evidence'])])) + ' |')
out += ['', 'FiveM possui limpeza específica: `data\\cache`, `data\\server-cache`, `data\\server-cache-priv`; preserva somente o item `data\\cache\\game` na lista de exclusão. O caminho de limpeza está separado da limpeza geral de logs/crashes. O código verifica processos antes de limpar cache de jogo. Fonte: `backend-focused.js:2828`.', '',
        '## 5. As 16 categorias completas de limpeza', '',
        'O scanner calcula alvos existentes e únicos, sem apagar. Na execução, a seleção é filtrada pela lista de IDs conhecida. Processos abertos são consultados novamente e aquela categoria é pulada. A idade de temporários vale para itens de primeiro nível; nas categorias normais vale por arquivo. Lixeira e DeliveryOptimization têm caminhos especiais. O relatório de algoritmos detalha a travessia e as diferenças entre medir e excluir.', '',
        '| ID / nome | Seleção padrão | Idade em dias | Processo que bloqueia | Expressão exata de alvos | Fonte |', '|---|---|---:|---|---|---|']
for clean in spec['cleanCategories']:
    target = clean.get('t', 'Lixeira: $Recycle.Bin\\<SID atual> em discos Fixed/IsReady')
    out.append('| ' + ' | '.join(map(cell, [f"`{clean['id']}` — {clean['name']}", 'sim' if clean['on'] else 'não', clean.get('age', 'especial'), ', '.join(clean.get('procs', [])) or 'nenhum', f'`{target}`', ref(clean['evidence'])])) + ' |')
out += ['', 'Detalhes das expressões: `$L=LOCALAPPDATA`, `$R=APPDATA`. `Sub` combina a raiz com cada nome; `Chromium` inclui a raiz e cada subpasta com Preferences, usando Cache/Code Cache/GPUCache/DawnCache/DawnGraphiteCache/DawnWebGPUCache, além de ShaderCache/GrShaderCache/GraphiteDawnCache da raiz. Firefox inclui cache2/startupCache/thumbnails/jumpListCache. Não há rotina de reversão para esses arquivos.', '',
        'A lista de processos é um retrato no início do script, não um bloqueio de arquivo nem monitoramento contínuo. Arquivos inacessíveis/abertos podem ficar. `freed` vem da rotina de exclusão; o resultado deve conservar também `files`, `failed` e `open`, não transformar sucesso do pedido em todos os arquivos removidos.', '',
        '## 6. Provedores DNS e ferramentas pontuais', '',
        '| Provedor | Servidores IPv4 |', '|---|---|']
for name, ips in spec['dnsProviders'].items():
    out.append(f"| `{name}` | `{', '.join(ips)}` |")
out += ['', '`auto` é um quinto modo de seleção, não um quinto provedor: usa os servidores automáticos ou restaura o snapshot conforme fluxo. A ação antiga DNS Otimizado usa 1.1.1.1 + 8.8.8.8, portanto não deve ser confundida com Cloudflare do seletor.', '',
        '| ID ferramenta | Timeout | Sequência principal | Verificação / limite | Fonte |', '|---|---:|---|---|---|']
tool_notes = {
    'restore-point': ('Enable-ComputerRestore + Checkpoint-Computer MODIFY_SETTINGS; frequência temporária 0.', 'Salva/restaura SystemRestorePointCreationFrequency em finally; pode deixar proteção habilitada.'),
    'flush-dns': ('Clear-DnsClientCache + ipconfig /flushdns.', 'Não verifica código de saída dos dois comandos.'),
    'net-reset': ('ipconfig /flushdns; netsh winsock reset; netsh int ip reset.', 'Anuncia necessidade de reiniciar; não restaura ajustes anteriores e não verifica cada exit code.'),
    'update-cleanup': ('DISM /Online /Cleanup-Image /StartComponentCleanup /NoRestart.', 'Exige exit code 0; mede delta de espaço livre; diferencia >0,05 GB.'),
    'repair': ('DISM /RestoreHealth /NoRestart; depois sfc /scannow.', 'Guarda/valida exit code DISM; não valida exit code SFC.'),
}
for tool in spec['tools']:
    desc, check = tool_notes[tool['id']]
    out.append('| ' + ' | '.join(map(cell, [f"`{tool['id']}`", f"{tool['timeout']/1000:g} s", desc, check, ref(tool['evidence'])])) + ' |')

out += ['', '## 7. FiveM: valores completos dos presets', '',
        'Procura `CitizenFX\\gta5_settings.xml` primeiro em APPDATA e depois LOCALAPPDATA. Só escreve com FiveM fechado. O preset FPS tenta 18 campos; Balanced tenta 16. `xmlSet` troca somente a primeira tag existente com `value="..."`; **não cria tags ausentes** e não faz parse XML completo. Assim o número de tentativas não prova 18/16 alterações em todo arquivo.', '',
        '| Tag XML | FPS | Balanced |', '|---|---|---|']
fps = spec['fiveMPresets']['fps']
balanced = spec['fiveMPresets']['balanced']
for key in dict.fromkeys([*fps, *balanced]):
    out.append('| ' + ' | '.join(map(cell, [f'`{key}`', fmt(fps[key]) if key in fps else 'não altera', fmt(balanced[key]) if key in balanced else 'não altera'])) + ' |')
out += ['', 'Escala separada: índices de entrada 0,1,2,3,4 escrevem respectivamente `SamplingMode=0,4,3,2,1`. `borderless=true` escreve Windowed=2; `false` escreve Windowed=0. O código não altera resolução explicitamente nesse fluxo.', '',
        'O backup é criado uma única vez antes da primeira alteração e vale para o arquivo inteiro, não por preset. A gravação usa arquivo temporário e rename. Restaurar copia o backup integral, apaga esse backup e remove fivemPreset das configurações. Não compara o conteúdo final nem recria tags ausentes. Fontes: `backend-focused.js:8798`, `8899`, `8928`.', '',
        '## 8. Critérios concretos para reimplementar com equivalência', '',
        '| Área | Entrada e efeito já especificados | Verificação que a futura implementação precisa ter |', '|---|---|---|',
        '| Ações e tweaks | Nomes/payloads, chaves e tipos, comandos on/off, regras de capacidade, detectores e specs. | Capturar estado tipado antes; rejeitar operação bloqueada; reler cada valor; manter erro parcial; restaurar o estado capturado. |',
        '| Composições antigas | Lista exata de componentes; aliases e efeitos genéricos por jogo. | Confirmar que nenhum nome publicitário seja interpretado como algoritmo adicional. |',
        '| Limpeza | Todos os 16 seletores, idade, processos, paths e resultados. | Rodar primeiro sobre árvore de arquivos de teste; confirmar seleção e bytes sem atingir dados reais; ensaiar links, cloud, idade e erros. |',
        '| Jogos / GPU | IDs conhecidos, origens, exe/proc e preferência GPU. | Validar o executável descoberto e conservar valor de registro anterior; separar descoberta de otimização. |',
        '| DNS | Quatro pares, modo auto e preservação por GUID de adaptador. | Relê servidores efetivos de cada interface; não anuncia sucesso global se alguma recusou. |',
        '| FiveM | 2 presets, todas as tags/valores, escala, janela, backup integral. | Verificar tags efetivamente existentes/alteradas e permitir recuperar o XML integral. |',
        '| Gráficos nativos / OptiScaler | Apenas contratos de integração neste catálogo. | Usar os relatórios específicos; não tratar tabelas de opções como reimplementação de captura, upscale e frame generation. |', '',
        'O executor original `run-action` retorna `on=true` depois de ausência de GDZ_ERRO e eventualmente salva snapshot posterior, **sem comparar esse snapshot nem executar DETECT nesse retorno**. `runTweak` faz probe e pode retornar `success=true,on=false`. Para reconstruir comportamento visível, esses casos precisam ser distinguidos; para um app confiável, a equivalência técnica deve exigir o efeito relido.', '',
        'Lacunas práticas que continuam: resultado real e compatibilidade não foram ensaiados no Windows; políticas podem ser recusadas, serviços/pacotes podem faltar, semântica de drivers varia. Reinstalação UWP, lixo excluído, encerramento de processo e resets de rede não são cobertos por um snapshot universal. Há dados suficientes para especificar e implementar separadamente os recursos acima, mas isso não demonstra uma cópia funcional dos motores gráficos nativos.', '',
        '## 9. Arquivos e reprodução', '',
        '- [Extrator de declarações](../scripts/extract-gdz-operations.cjs).',
        '- [Gerador deste catálogo e especificação](../scripts/build-gdz-operation-specs.py).',
        '- [Inventário declarativo privado](../artifacts/private/gdz-analysis/deep/operations-declarative.json).',
        '- [Especificação normalizada privada](../artifacts/private/gdz-analysis/deep/operation-specs.json).', '',
        '```powershell', 'node scripts/extract-gdz-operations.cjs', 'python scripts/build-gdz-operation-specs.py', '```', '',
        'Esses comandos executam somente ferramentas próprias de análise, sobre textos já extraídos. A pasta privada contém trechos do software analisado e caminhos locais, por isso não deve integrar o código do produto nem ser publicada automaticamente.', '',
]

checks = {
    'source_hash_matches': hashlib.sha256((DEEP / 'backend-focused.js').read_bytes()).hexdigest() == data['source']['sha256'],
    '69_unique_named_operations': len(operations) == len({o['id'] for o in operations}) == 69,
    '33_derived_tweaks': len(data['derivedTweaks']) == 33,
    '32_final_reverts': len(reverts) == 32,
    '8_revert_only_names': len(orphan_reverts) == 8,
    '13_base_actions_absent_from_pages': len(set(actions) - ui_actions) == 13,
    'no_unresolved_fields': '"unresolved": true' not in json.dumps(data),
    'alias_temporary_scripts_match': actions['Temporários Limpos'] == actions['Temporários Removidos'],
    'quick_omits_file_snapshot': not any(i.get('t') == 'file' for i in specs['Otimização Rápida']),
    'dlss_both_drs_ids': fields(data['expanded']['TWEAKS'][7])['drs'] == [[283385345, 1], [283385331, 16777215]],
    'all_tweak_specs_are_lists': all(isinstance(t['observations']['tweakSpec'], list) for t in data['derivedTweaks']),
    'classic_menu_has_no_declared_snapshot': next(t for t in data['derivedTweaks'] if t['id'] == 'classic-menu')['observations']['tweakSpec'] == [],
    'steam_overlay_has_no_declared_snapshot': next(t for t in data['derivedTweaks'] if t['id'] == 'steam-overlay')['observations']['tweakSpec'] == [],
    'fivem_fields_18_16': len(fps) == 18 and len(balanced) == 16,
    '16_unique_cleaner_categories': len(spec['cleanCategories']) == len({c['id'] for c in spec['cleanCategories']}) == 16,
    '12_unique_known_games': len(spec['games']) == len({g['id'] for g in spec['games']}) == 12,
    '198_synthetic_capability_rows': sum(len(p['tweaks']) for p in spec['capabilityMatrixSynthetic']) == 198,
    '23H2_end_task_transition': bool(next(e for e in spec['capabilityMatrixSynthetic'][3]['tweaks'] if e['id'] == 'end-task')['blocked']) and not next(e for e in spec['capabilityMatrixSynthetic'][4]['tweaks'] if e['id'] == 'end-task')['blocked'],
    'unknown_build_gate_behavior': not next(e for e in spec['capabilityMatrixSynthetic'][5]['tweaks'] if e['id'] == 'classic-menu')['blocked'],
}
if not all(checks.values()):
    raise RuntimeError(f'Failed verification: {[k for k,v in checks.items() if not v]}')
(DEEP / 'operation-specs-verification.json').write_text(json.dumps(checks, indent=2) + '\n', encoding='utf-8')
document = ROOT / 'docs/GDZ-OPERACOES-IMPLEMENTAVEIS.md'
document.write_text('\n'.join(out), encoding='utf-8')
print(json.dumps({'document': str(document), 'operations': len(operations), 'checks': len(checks), 'allPassed': all(checks.values())}))
