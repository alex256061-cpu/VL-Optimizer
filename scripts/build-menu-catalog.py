"""Generate the product coverage book from public, local metadata; does not execute target software."""
from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parents[1]
catalog = json.loads((ROOT / 'artifacts/catalogo-0.4.0.json').read_text(encoding='utf-8-sig'))
source = (ROOT / 'docs/GDZ-RECURSOS-DETALHADOS.md').read_text(encoding='utf-8-sig')
blocks = list(re.finditer(r'^#### (\d+)\. ([^\n]+)\n(.*?)(?=^#### |\Z)', source, re.M | re.S))
mapping = {1:'power-high',2:'power-ultimate',3:'x3d-mode',4:'game-mode',5:'dvr-off',6:'gamebar-off',7:'steam-overlay',10:'updates-no-reboot',11:'notifications-off',12:'bg-apps',13:'edge-bg',14:'hags',15:'windowed-games',16:'dlss-rec',17:'gpu-msi',18:'mpo',19:'visual-effects-off',20:'transparency',21:'sysmain-off',22:'indexing-off',23:'unused-services-off',24:'xbox-services-off',26:'resolution-720p',30:'mouse-accel',31:'kbd-repeat',32:'sticky',33:'numlock',34:'pagefile-auto',36:'hibernate',38:'delivery',39:'lso',40:'eee',41:'nic-intmod',44:'obs-priority',46:'telemetry',47:'suggestions',48:'bing',49:'activity',50:'widgets',51:'location',52:'end-task',53:'file-ext',54:'hidden-files',55:'this-pc',56:'classic-menu',57:'dark',58:'taskbar-left',59:'task-view',60:'search-icon',61:'bsod-detail',67:'windows-update-services'}
manual = {
    8:'Guia da sobreposição NVIDIA; configuração manual no aplicativo do fabricante.',
    9:'Guia da sobreposição Discord; sem encerrar ou editar arquivos internos do aplicativo.',
    25:'Catálogo UWP: remoção individual XboxGamingOverlay, protegida em X3D; sem protocolos substitutos.',
    27:'Catálogo fechado de dez pacotes UWP; prévia individual, verificação e sem undo fictício.',
    28:'Sessões opt-in com prioridades originais e identidade dos processos. BelowNormal não é limite de RAM/CPU.',
    29:'Processos: encerramento individual revalidando PID, início e caminho; sem fechar sincronizadores automaticamente.',
    35:'CleanupWindowsUpdate: DISM StartComponentCleanup, sem ResetBase.',
    37:'Cinco opções DNS: DHCP e quatro provedores, snapshot por GUID de NIC física.',
    42:'FlushDns: API local, sem mudar o provedor DNS.',
    43:'ResetNetwork: Winsock/TCP-IP com revisão, saída por etapa e aviso de reinício.',
    45:'Assistente oficial do OBS e guia da plataforma. Não grava bitrate ou chave de transmissão arbitrariamente.',
    62:'CreateRestorePoint: confere nova sequência; depende da Proteção do Sistema.',
    63:'RepairWindows: DISM/SFC, códigos e saída por etapa; exit code não prova saúde.',
    64:'RestartExplorer: valida caminho, usuário e sessão do shell e verifica o novo processo.',
    65:'Substituição deliberada: recuperar snapshots próprios no histórico. Não impõe padrões universais de BCD/rede/HID de outro produto.',
    66:'EnableDefender: solicita proteção e verifica estado; não remove políticas nem contorna Tamper Protection.',
    68:'EnableSystemRestore: solicita ativação na unidade Windows. Confirmação de ponto é outra etapa.',
    69:'Abre Escala e monitores no Windows, em vez de impor 96 DPI para todo monitor.'}
by_id = {d['Id']: d for d in catalog['Definitions']}
entries = []
for match in blocks:
    number = int(match.group(1))
    if not 1 <= number <= 78:
        continue
    area = 'performance' if number <= 29 else 'input' if number <= 33 else 'memory' if number == 34 else 'storage' if number <= 36 else 'network' if number <= 45 else 'windows' if number <= 69 else 'hardware'
    option = mapping.get(number)
    key = re.search(r'Chave: `([^`]+)`', match.group(3))
    mechanism = re.search(r'\*\*O que o código prepara:\*\* ([^\n]+)', match.group(3))
    implementation = (by_id[option]['Description'] + ' ID: ' + option) if option else manual.get(number, 'Inventário com fonte WMI e orientação manual de firmware/driver. Não comprova correções, XMP/EXPO ou suporte por heurísticas de nome. Guias GPU em DLSS / FSR.')
    entries.append({'OriginalNumber': number, 'Title': match.group(2).strip(), 'OriginalKey': key.group(1) if key else None, 'OriginalMechanism': mechanism.group(1) if mechanism else 'Orientação/fluxo descrito no relatório estático.', 'Area': area, 'ConfigurationId': option, 'VLImplementation': implementation})
assert len(entries) == 78 and set(mapping.values()) <= set(by_id)
result = {'Version':'0.4.0','OriginalCatalogEntries':78,'NavigationPages':catalog['Pages'],'WindowsDefinitions':len(by_id),'OptiEditableFields':len(catalog['OptiScalerSettings']),'Mappings':entries}
(ROOT / 'artifacts/menu-functions-0.4.0.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
lines = ['# Funções do menu — VL Optimizer 0.4.0', '', 'A imagem orientou a navegação. O nome GDZ Scaling da referência corresponde a VL Scaling, preservando a identidade deste projeto. Bloqueios comerciais do GDZ não foram copiados; cada função confere sistema, hardware e permissão.', '', '## Áreas solicitadas', '', '| Menu | Função disponível |', '| --- | --- |']
for page in catalog['Pages']:
    lines.append('| ' + page['Title'] + ' | ' + page['Description'] + ' |')
lines += ['', 'São 13 entradas principais e três atalhos inferiores: hardware, histórico e configurações. Em janela larga o menu fica aberto. A seleção do catálogo vale para a área atual; prévias sobrepostas são bloqueadas antes de escrever.', '', '## Pesquisa e critérios', '', 'Foram cruzados os 78 itens de PAGES, ações/handlers recuperados e contratos já implementados, com documentação primária atual. O INI oficial OptiScaler mudou em relação à UI antiga: geração tem origem/saída separadas; contador e atalho pertencem a Menu. O editor modifica dez chaves fechadas somente quando já existem, preserva codificação/comentários e salva backup antes de escrever. Presença de chave não comprova suporte a todo valor por um binário antigo.', '', 'O overlay FPS novo é uma janela externa com passagem de cliques e sem ativação, nos quatro cantos da tela principal. Mostra média acumulada, frametime e frames do CSV da captura própria. Não injeta biblioteca nem garante aparecer em tela cheia exclusiva. Lows ficam no relatório final.', '', 'As dicas são identificadas como orientação manual: OBS, sincronização/latência, BIOS, chipset e perfis de memória. Não há medições ou correções fictícias por modelo de hardware.', '', '## Mapa das 78 entradas', '']
for entry in entries:
    lines += [f"### {entry['OriginalNumber']:02d}. {entry['Title']}", '', 'Mecanismo recuperado da amostra: ' + entry['OriginalMechanism'], '', 'No VL: ' + entry['VLImplementation'], '']
lines += ['## Detalhamento das configurações atuais', '', 'As definições abaixo têm prévia, aprovação consumível, snapshot anterior, releitura e restauração com conflito. O JSON público preserva os valores técnicos e as fontes.', '']
for definition in catalog['Definitions']:
    lines += [f"### `{definition['Id']}` — {definition['Title']}", '', definition['Description']]
    if definition['Warning']:
        lines.append('Impacto: ' + definition['Warning'])
    lines += [f"Build mínima: {definition['MinimumBuild']}; dependências/privilégios são consultados na prévia.", '']
lines += ['## Fontes primárias conferidas em 08/11/2026', '', '- [INI OptiScaler](https://github.com/optiscaler/OptiScaler/blob/master/OptiScaler.ini).', '- [Assistente OBS](https://obsproject.com/kb/quick-start-guide/) e [codificador YouTube](https://support.google.com/youtube/answer/2853702?hl=pt-BR).', '- [Defender](https://learn.microsoft.com/powershell/module/defender/set-mppreference) e [Proteção do Sistema](https://learn.microsoft.com/powershell/module/microsoft.powershell.management/enable-computerrestore?view=powershell-5.1).', '- [Atalhos Windows Settings](https://learn.microsoft.com/windows/apps/develop/launch/launch-settings).', '- [DLSS](https://www.nvidia.com/en-us/geforce/technologies/dlss/) e [Reflex](https://www.nvidia.com/en-us/geforce/technologies/reflex/).', '- [Anti-Lag](https://www.amd.com/en/products/software/adrenalin/radeon-software-anti-lag.html) e [AFMF](https://www.amd.com/en/products/software/adrenalin/afmf.html).', '', '## Limites', '', 'O motor gráfico proprietário GDZ/Lossless não foi reconstruído ou incluído. Os 40 campos VL Scaling continuam armazenamento/exportação e lançamento manual de programa externo. Nenhum shader/assembly do alvo foi incorporado. Não há bypass de anticheat, backend remoto ou promessa de FPS.', '', '218 cenários isolados passaram, sem alterações reais de Windows/Defender/serviços/Proteção do Sistema no PC de desenvolvimento. O smoke percorre 16 destinos, quatro páginas legadas e 22 subabas novas, além das abas existentes. Estilos Win32 de passagem de cliques/não ativação do overlay foram conferidos em leitura do próprio HWND. Captura em jogo, mutações e homologação de drivers/VMs continuam pendentes.', '']
(ROOT / 'docs/FUNCOES-DO-MENU-0.4.0.md').write_text('\n'.join(lines), encoding='utf-8')
print(f'{len(entries)} entradas mapeadas; {len(by_id)} ajustes; {len(catalog["Pages"])} destinos.')
