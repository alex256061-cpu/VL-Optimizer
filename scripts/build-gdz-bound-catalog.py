"""Build the reviewed semantic catalogue of 70 named function expressions.

Consumes static-analysis JSON only. Never imports or executes target JavaScript.
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEEP = ROOT / "artifacts/private/gdz-analysis/deep"

# name | semantic inputs | output | observed behavior | immediate / generated effects
DATA = """
entitlementKey|nenhuma|objeto KeyObject da chave pública|Decodifica ENTITLEMENT_PUBLIC_KEY em base64 DER/SPKI, cria a chave pública com crypto.createPublicKey e reutiliza _entitlementKey nas chamadas seguintes.|Atualiza somente o cache _entitlementKey; não concede licença nem modifica autenticação.
tagValue|texto XML, nome da tag|string do conteúdo ou null|Procura o primeiro par simples `<tag>...</tag>` por expressão regular e devolve o conteúdo sem outras tags internas.|Nenhum; não é um parser XML completo.
setTagValue|texto XML, nome da tag, novo valor|texto XML|Substitui o conteúdo da primeira tag simples existente, preservando abertura e fechamento; se não encontra a tag devolve o original.|Nenhum; o helper apenas cria texto, não grava arquivo.
getHWID|nenhuma|Promise de identificador de hardware, null ou undefined|Obtém o resultado de readHWIDCached e seleciona o campo id quando existe resultado.|Herda a leitura/cache de readHWIDCached; não escreve identificação no firmware.
getBoardSerial|nenhuma|Promise de serial da placa ou string vazia|Obtém readHWIDCached e seleciona board, usando vazio quando não há serial.|Herda a leitura/cache do helper; não modifica hardware.
planKey|objeto de usuário/plano|string JSON ou undefined|Serializa plan, features, licenseExpiresAt, lifetime e pcBound numa assinatura de comparação do estado do plano; entrada undefined mantém retorno undefined.|Nenhum; não é chave criptográfica nem gerador de licença.
memText|valor textual, tamanho máximo|string higienizada|Converte em string, remove caracteres fora de ASCII imprimível, comprime espaços, corta até o limite e elimina valores que batem em MEM_BLANK.|Nenhum; limpa rótulos de hardware/memória.
reg|caminho do Registro, nome do valor|descritor {t:'reg',path,name}|Monta uma referência de valor do Registro para outros componentes consultarem ou guardarem backup.|Nenhum acesso ao Registro nesta função.
svc|nome do serviço|descritor {t:'svc',name}|Monta uma referência de serviço para a infraestrutura de leitura/backup.|Nenhuma mudança de serviço nesta função.
pwr|nome simbólico de configuração|descritor {t:'pwr',sub,set}|Usa PWR.SUB_PROCESSOR e PWR[nome] para descrever um ajuste do plano de energia.|Nenhuma execução de powercfg nesta função.
bcd|elemento BCD|descritor {t:'bcd',el}|Descreve uma opção do banco de configuração de inicialização.|Nenhuma execução de bcdedit nesta função.
drs|identificador de configuração NVIDIA|descritor {t:'drs',id}|Monta uma referência de configuração DRS para leitura e backup do perfil do driver.|Nenhuma chamada ao driver nesta função.
tcp|nome da opção TCP|descritor {t:'tcp',k}|Descreve uma opção global TCP para a infraestrutura de captura/restauração.|Nenhuma alteração da rede nesta função.
psQuote|valor|string entre aspas simples PowerShell|Converte o valor em string e duplica aspas simples internas, produzindo um literal PowerShell.|Nenhum; não executa o texto produzido.
ENSURE_KEY|caminho de chave do Registro|string PowerShell|Gera Test-Path seguido de New-Item -Force quando a chave não existe.|Só gera texto; quando executado pelo chamador pode criar a chave.
SET_DWORD|caminho, nome, valor numérico|string PowerShell|Combina ENSURE_KEY com Set-ItemProperty -Type DWord -Force, usando SilentlyContinue.|Só gera texto; a execução posterior escreve um DWORD no Registro.
SET_PLAN|GUID do plano de energia|string PowerShell|Gera powercfg /setactive; se falhar tenta /duplicatescheme com o mesmo GUID e reativa; se falhar outra vez emite GDZ_ERRO e encerra o script.|Só gera texto; a execução posterior troca/cria plano de energia.
GENERIC_GAME_OPT|contexto de hardware com x3dDual|string PowerShell|Monta a otimização genérica: plano de alto desempenho salvo em PLAN.HIGH, exceto em x3dDual, seguido dos trechos GAME_MODE_ON e DVR_OFF.|Só gera texto; execução posterior altera energia, Modo de Jogo e captura DVR.
valueFor|valor ou função, contexto|valor resolvido|Quando o primeiro argumento é função, chama essa função com o contexto; caso contrário devolve o próprio valor.|Pode executar o callback recebido; efeitos dependem dele.
allGames|nenhuma|array de jogos|Concatena GAMES e LIB_GAMES para unir catálogo nativo e jogos adicionais.|Não altera os arrays de origem; não faz nova varredura de disco.
gameById|id de jogo|jogo encontrado ou undefined|Procura por igualdade estrita do campo id no catálogo produzido por allGames.|Nenhum.
procMatchers|jogo com lista proc|array de RegExp|Transforma máscaras de processo em regex ancoradas, converte para minúsculas, escapa metacaracteres e converte * em .* .|Nenhum; não enumera nem encerra processos.
hasHybridGpu|hardware com lista gpus|boolean|Exige ao menos uma GPU integrada e outra não integrada cujo vendor não seja 'other'.|Nenhum; só classifica os dados recebidos.
gpuExes|jogo com id|array de caminhos de executáveis|Consulta GAME_SCAN[id].exes quando existe resultado de varredura; caso contrário devolve [].|Nenhuma varredura é feita aqui.
gpuSpec|jogo|array de descritores reg|Para cada executável de gpuExes, monta referência ao valor correspondente na chave K.UGP de preferências gráficas do Windows.|Nenhuma escrita; prepara captura/backup.
gpuScript|jogo|string PowerShell|Gera criação de K.UGP e gravação GpuPreference=2; para cada executável do jogo, selecionando preferência de GPU de alto desempenho.|Só gera texto; a execução posterior altera preferências gráficas por executável.
autoStartScript|contexto com x3dDual e laptop|string PowerShell|Gera captura do plano atual; baixa prioridade de AUTO_APPS_LOW; encerra AUTO_APPS_CLOSE guardando caminhos; em desktop sem x3dDual pode ativar PLAN.HIGH; emite GDZ_AUTO com plano anterior e caminhos encerrados.|Só gera texto; execução posterior muda prioridades, encerra processos e pode trocar plano.
autoStopScript|estado com scheme e killed|string PowerShell|Gera retorno de BelowNormal para Normal nos processos configurados, restauração do plano salvo e reinício dos executáveis encerrados ainda existentes; OneDrive recebe /background.|Só gera texto; execução posterior relança processos e restaura energia/prioridades.
fmtMB|quantidade em MB|string para a interface|A partir de 1024 MB converte para GB com uma casa decimal e vírgula; abaixo disso adiciona ' MB'.|Nenhum.
WIN11|contexto com build|string de indisponibilidade ou null|Retorna 'Só existe no Windows 11.' se há build menor que 22000; sem build conhecido ou com build suficiente retorna null.|Nenhum; é um filtro de disponibilidade.
steamUserData|contexto com steamPath|caminho userdata|Acrescenta \\userdata ao diretório Steam informado.|Nenhuma leitura de arquivo.
STEAM_VDF_LIST|contexto com steamPath|string de expressão PowerShell|Gera enumeração dos diretórios de contas Steam e de cada config\\localconfig.vdf existente.|Só gera texto; execução posterior consulta diretórios e arquivos.
steamOverlayProbe|contexto com steamPath|string de expressão PowerShell booleana|Sem instalação Steam retorna '$false'; com instalação exige ao menos um VDF e que todos contenham EnableGameOverlay igual a 0.|Só gera texto; execução posterior lê as configurações Steam.
steamOverlayScript|contexto Steam, boolean desativar|string PowerShell|Gera verificação de contas, recusa alteração com jogo Steam aberto, tenta fechar Steam por até 20 segundos, escreve overlay=0/1 via GdzOverlayWrite e reabre Steam via explorer se estava aberto; emite erro/aviso específico.|Só gera texto; execução posterior fecha/reabre Steam e modifica localconfig.vdf.
gpuMsiReg|contexto com gpuIrq|array de especificações do Registro|Se há caminho da GPU gera MSISupported DWORD=1 em MessageSignaledInterruptProperties e DevicePriority DWORD=3 em Affinity Policy; sem caminho retorna [].|Nenhuma escrita; fornece valores para aplicação/backup.
nicLsoReg|contexto com nicLso|array de especificações do Registro|Gera *LsoV2IPv4 String='0' e, quando suportado, *LsoV2IPv6='0' nas subchaves das placas; o valor de retorno padrão informado é '1'.|Nenhuma escrita; descreve desligamento de Large Send Offload.
nicRestart|lista de subchaves das placas, mensagem|string PowerShell|Gera resolução NetCfgInstanceId para InterfaceGuid, localiza a placa e reinicia somente adaptadores não desativados; inclui aviso de interrupção temporária.|Só gera texto; execução posterior reinicia adaptadores e interrompe a conexão por alguns segundos.
nicLsoRestart|contexto nicLso, boolean ativar ajuste|string PowerShell|Extrai as subchaves das placas afetadas e delega a nicRestart com mensagem LSO desligado/religado.|Só gera texto; efeito de rede ocorre no script executado pelo chamador.
nicOptOf|contexto nicOpt, lista de nomes de opções|array de opções|Filtra nicOpt mantendo apenas entradas cujo campo n consta na lista pedida.|Nenhum.
nicOptReg|lista de nomes de opções|função contexto→especificações|Cria uma função que filtra opções via nicOptOf e produz tuplas de Registro com tipo String, valor aplicado '0' e padrão d da própria opção.|Nenhuma escrita; é uma fábrica de descritores.
nicOptRestart|lista de nomes, mensagem|função contexto→script|Cria função que seleciona subchaves via nicOptOf, remove duplicatas com Set e gera nicRestart.|Só gera função/texto; reinício de rede ocorre na execução posterior.
tweakById|id de ajuste|entrada TWEAKS ou undefined|Procura ajuste em TWEAKS por igualdade estrita do campo id.|Nenhum.
tweakAction|ajuste com id|string 'Tweak: <id>'|Gera o nome interno usado para registrar a ação de ajuste.|Nenhum.
tweakReg|ajuste, contexto|lista de tuplas de Registro|Resolve ajuste.reg via valueFor, aceitando lista estática ou função do contexto; ausente resulta em [].|Só executa eventual callback reg; não grava Registro diretamente.
tweakSvc|ajuste, contexto|lista de tuplas de serviços|Resolve ajuste.svc via valueFor; ausente resulta em [].|Só executa eventual callback svc; não modifica serviços diretamente.
tweakDrs|ajuste, contexto|lista de configurações DRS|Resolve ajuste.drs via valueFor; ausente resulta em [].|Só executa eventual callback drs; não aplica configurações NVIDIA diretamente.
psRegValue|tipo do Registro, valor|string de literal PowerShell|DWord vira String(valor); Binary vira [byte[]] com cada item convertido por Number e mascarado com &255; os demais tipos usam psQuote.|Nenhum; serializa valores para um script.
regProbeValue|tipo do Registro, valor|string de comparação|Binary vira sequência de bytes decimais separados por espaço; outros valores passam por String.|Nenhum; auxilia comparação de estado.
tweakScript|ajuste, boolean ativar, contexto|string PowerShell completo|Inclui DRS_PS se necessário; monta blocos de Registro, serviços e DRS sob ErrorActionPreference=Stop; falha produz GDZ_ERRO; depois acrescenta on/off resolvido e marcador GDZ_FIM.|Só gera texto; a execução posterior faz as alterações descritas pelo ajuste.
tweakSpec|ajuste, contexto|lista de descritores de backup|Converte entradas reg/svc/drs do ajuste em descritores padronizados reg(), svc() e drs().|Nenhuma alteração; descreve o estado a capturar/restaurar.
DNS_SET|lista de endereços DNS|string PowerShell|Gera Set-DnsClientServerAddress em adaptadores com Status Up; exige ao menos uma alteração bem sucedida e limpa cache DNS ao final.|Só gera texto; execução posterior altera DNS de todas as placas conectadas que aceitarem a configuração.
hasOwn|objeto, chave|boolean|Exige chave string e chama Object.prototype.hasOwnProperty.call; distingue propriedade própria de herdada.|Nenhum.
psData|dado serializável|string base64|Serializa JSON em UTF-8 e codifica base64 para transporte ao PowerShell.|Nenhum; não é criptografia.
withDrs|lista de descritores|string DRS_PS ou vazia|Inclui o suporte DRS apenas quando algum descritor tem t='drs'.|Nenhum; seleciona texto de suporte.
canRevert|nome da ação|boolean|Retorna true se há entrada própria em backups.entries ou função de retorno em REVERT.|Nenhuma restauração é executada aqui.
autostartSupported|nenhuma|boolean|Exige app.isPackaged e ausência de PORTABLE_EXECUTABLE_DIR no ambiente.|Nenhuma alteração da inicialização; só checa disponibilidade.
CLEAN_INFO|categoria de limpeza|objeto de apresentação|Seleciona id, group, name, desc, note, on e app; note/app ausentes viram null.|Nenhum; não faz limpeza.
psList|lista de valores|string de array PowerShell|Gera @(...), usando psQuote em cada elemento; lista ausente vira array vazio.|Nenhum.
psJson|saída textual do PowerShell|array de resultados|Extrai linha GDZ_JSON:, faz JSON.parse e envolve objeto único em array; sem marcador lança 'resposta vazia do PowerShell'; JSON inválido também lança erro.|Nenhum comando é executado; apenas interpreta retorno textual.
envPath|caminho com %VAR%|caminho expandido|Substitui variáveis de ambiente reconhecidas; quando não há valor mantém o trecho original %VAR%.|Lê process.env; não modifica ambiente.
cleanDir|texto de diretório|caminho absoluto normalizado ou vazio|Converte em string, remove espaços e aspas nas extremidades, expande %VAR%, remove barras finais e usa path.resolve se restar conteúdo.|Nenhuma criação/exclusão; apenas normalização textual.
sameOrInside|caminho candidato, diretório base|boolean|Compara em minúsculas, remove barras invertidas finais do diretório base e aceita igualdade ou prefixo base+\\.|Nenhum; é comparação textual, não resolve links/reparse points.
systemDirs|nenhuma|array de caminhos absolutos|Monta lista a partir de SystemRoot, WindowsApps, Package Cache, Packages e Common Files de 32/64 bits; remove entradas ausentes.|Lê variáveis de ambiente; não consulta existência nem altera diretórios.
appDirs|entrada de aplicativo|array de diretórios|Une loc e diretório do desinstalador retornado por uninstallExe, removendo entradas vazias.|Herda análise de uninstallExe; não desinstala.
othersOf|chave do aplicativo atual, lista de aplicativos|objeto {dirs,names}|Exclui a chave atual, agrega diretórios dos demais aplicativos e variantes de nomes em minúsculas.|Nenhum; prepara proteção contra remover resíduos compartilhados.
regExists|chave do Registro|Promise<boolean>|Executa reg query com janela oculta e timeout 8000 ms; resolve true quando o processo não retorna erro.|Consulta somente leitura do Registro; executa processo reg.exe sem alterar chave.
telemetryOn|nenhuma|boolean|Verifica se settings.telemetryConsent é exatamente true.|Nenhuma transmissão é feita por este helper.
round|número, casas decimais|número arredondado ou null|Aceita somente number finito; aplica toFixed e reconverte por Number; valores inválidos viram null.|Nenhum; toFixed pode lançar se o número de casas for inválido.
optiQuirk|caminho de executável|entrada OPTI_QUIRKS ou null|Extrai basename em minúsculas e consulta a tabela de exceções de compatibilidade.|Nenhuma execução de programa.
fivemXml|nenhuma|primeiro caminho XML existente ou null|Percorre FIVEM_XML com fs.existsSync; erros individuais são tratados como ausência e a primeira localização válida é escolhida.|Consulta somente leitura da existência dos arquivos.
"""


def main():
    raw_index = json.loads((DEEP / "backend-function-index.json").read_text(encoding="utf-8"))
    original = [
        {"name": x["name"], "kind": x["kind"], "start": x["originalStart"], "end": x["originalEnd"]}
        for x in raw_index
        if x.get("name") and not x["name"].startswith("_0x") and x["kind"] != "FunctionDeclaration"
    ]
    independent_index = DEEP / "main-bound-function-expressions.json"
    if independent_index.exists():
        independent = json.loads(independent_index.read_text(encoding="utf-8"))
        if {(x["name"], x["start"], x["end"]) for x in independent} != {(x["name"], x["start"], x["end"]) for x in original}:
            raise ValueError("Independent AST inventory disagrees with decoder inventory")
    readable = json.loads((DEEP / "backend-functions-readable.json").read_text(encoding="utf-8"))
    meta = json.loads((DEEP / "backend-static-metadata.json").read_text(encoding="utf-8"))
    definitions = {}
    for line in DATA.strip().splitlines():
        fields = line.split("|")
        if len(fields) != 5:
            raise ValueError(f"Expected five fields: {line}")
        definitions[fields[0]] = fields[1:]
    expected = {x["name"] for x in original}
    if set(definitions) != expected:
        raise ValueError(f"Coverage mismatch: missing={expected-set(definitions)}, extra={set(definitions)-expected}")
    result = []
    for f in original:
        r = next(x for x in readable if x["name"] == f["name"] and x["originalStart"] == f["start"])
        inputs, output, behavior, effects = definitions[f["name"]]
        result.append({
            "name": f["name"], "kind": f["kind"], "input": inputs,
            "output": output, "behavior": behavior, "effects": effects,
            "confidence": "alta: corpo legível inspecionado estaticamente; efeitos de scripts não executados",
            "original": {"file": "extracted/app-main.js", "start": f["start"], "end": f["end"]},
            "evidence": {"file": "deep/backend-focused.js", "line": r["focusedLine"], "endLine": r["focusedEndLine"]},
            "parametersAsFound": r["parameters"], "directCalls": r["directCalls"], "code": r["code"],
        })
    (DEEP / "bound-functions-explained.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    lines = [
        "# GDZ Optimizer — 70 funções atribuídas a variáveis",
        "",
        "Complemento do catálogo de declarações `function`: estas 70 funções são expressões/arrow functions com nome de variável semanticamente legível. Helpers ofuscadores e callbacks anônimos não são contados como recursos separados. As 70 entradas foram conferidas contra o inventário AST original e o corpo de cada função recuperado.",
        "",
        "**Método e limites.** Leitura estática do `app-main.js`; nenhum comando do programa, script PowerShell, mudança de sistema ou validação de licença foi executado. Quando a função **gera um script**, a coluna de efeitos diferencia a geração de texto do que ocorrerá se outro componente executar esse texto. Os nomes dos parâmetros abaixo são descrições semânticas; os identificadores ofuscados originais constam no JSON privado.",
        "",
        "**Proveniência.** SHA-256 do `app-main.js`: `" + meta["sourceSha256"] + "`. Evidências nas linhas indicadas de `artifacts/private/gdz-analysis/deep/backend-focused.js`; os offsets originais e o código de cada função estão em `bound-functions-explained.json` no mesmo diretório. O arquivo original é preservado.",
        "",
        "**Cuidado aplicado na recuperação.** `const` não torna propriedades de objetos imutáveis. O decodificador conserva estado de aplicação (`_standby`, `settings`, etc.) como dinâmico e só resolve dicionários ofuscadores sem mutação nem escape. " + str(len(meta["regressionFixtures"])) + " casos de regressão verificam mutação, escape, efeitos de inicializadores/argumentos e tipos de funções. A cobertura de strings não comprova equivalência de execução.",
        "",
        "| # | Função e evidência | Entrada → saída | O que faz | Efeitos observáveis |",
        "|---|---|---|---|---|",
    ]
    for i, r in enumerate(result, 1):
        lines.append(f"| {i} | `{r['name']}` · L{r['evidence']['line']} | {r['input']} → {r['output']} | {r['behavior']} | {r['effects']} |")
    lines += [
        "", "## Distinções úteis para interpretar a interface", "",
        "- `reg`, `svc`, `pwr`, `bcd`, `drs`, `tcp`, `gpuSpec` e `tweakSpec` montam descritores; a escrita efetiva pertence aos executores e scripts de outra camada.",
        "- `autoStartScript` pode produzir encerramento de aplicativos; `autoStopScript` tenta reiniciá-los pelos caminhos salvos. Isso não recupera trabalho não salvo nem representa restauração integral do estado dos aplicativos.",
        "- `SET_PLAN`, `DNS_SET`, `gpuScript`, `tweakScript` e os helpers de rede geram alterações concretas quando seus scripts são executados. A presença de um item na interface não significa que o comando foi aceito pelo Windows ou que houve ganho de desempenho.",
        "- `canRevert` informa a existência de backup ou rotina de retorno, sem provar que o retorno será bem sucedido. `psData` usa base64 para transporte, sem sigilo criptográfico.",
        "", "## Reprodução do catálogo", "", "Após executar `node scripts/decode-gdz-static.cjs`, execute `python scripts/build-gdz-bound-catalog.py`. O gerador exige cobertura exata de 70 nomes e cruza cada função com seu offset original e sua linha na versão legível. Ele não carrega JavaScript do alvo.", "",
    ]
    (ROOT / "docs/GDZ-HELPERS-EXPRESSOES.md").write_text("\n".join(lines), encoding="utf-8")
    print(json.dumps({"functionsExplained": len(result), "allCovered": set(definitions) == expected}, ensure_ascii=False))


if __name__ == "__main__":
    main()
