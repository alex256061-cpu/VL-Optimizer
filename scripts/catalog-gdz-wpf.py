#!/usr/bin/env python3
"""Inventory already-decompiled BAML/C# as text. Never load target assemblies."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
DEEP = ROOT / "artifacts/private/gdz-analysis/deep"
SOURCE = DEEP / "wpf-decompiled"
EVENTS = {
    "Click", "SelectionChanged", "ValueChanged", "PreviewTextInput", "KeyDown",
    "KeyUp", "TextChanged", "SizeChanged", "Checked", "Unchecked", "LostFocus",
}
TYPES = {"ComboBox", "NumberBox", "Slider", "ToggleSwitch", "Button", "TextBox", "HyperlinkButton"}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def local(name):
    return name.rsplit("}", 1)[-1]


def get_methods(text):
    # ILSpy's block indentation gives exact method boundaries in this view.
    pattern = re.compile(r"^\t(?:public|private|protected|internal) [^\n;=]+?\b(\w+)\([^\n]*\)\s*\n\t\{", re.M)
    methods = {}
    for match in pattern.finditer(text):
        closing = re.search(r"^\t\}", text[match.end():], re.M)
        if closing is None:
            raise ValueError("Unclosed method: " + match[1])
        end = match.end() + closing.end()
        methods[match[1]] = {
            "name": match[1], "line": text.count("\n", 0, match.start()) + 1,
            "endLine": text.count("\n", 0, end) + 1,
            "code": text[match.start():end],
        }
    return methods


def resolve_resource(value, strings):
    match = re.fullmatch(r"\{x:Static resx:Strings\.(\w+)\}", value or "")
    return strings.get(match[1], value) if match else value


def cell(value):
    return str(value or "—").replace("|", "\\|").replace("\n", " ").replace("\r", "")


def main():
    resources = ET.parse(SOURCE / "UI.Resources.Strings.resx").getroot()
    strings = {e.attrib["name"]: e.findtext("value", "") for e in resources.findall("data")}
    controls = []
    files = []
    for path in sorted(SOURCE.rglob("*")):
        if path.is_file():
            files.append({"file": path.relative_to(SOURCE).as_posix(), "bytes": path.stat().st_size, "sha256": sha(path)})
    for path in sorted(SOURCE.rglob("*.xaml")):
        text = path.read_text(encoding="utf-8-sig")
        code_path = path.with_suffix(path.suffix + ".cs")
        code = code_path.read_text(encoding="utf-8-sig") if code_path.exists() else ""
        methods = get_methods(code)
        parent_map = {}
        tree = ET.fromstring(text)
        for parent in tree.iter():
            for child in parent:
                parent_map[child] = parent
        for element in tree.iter():
            attrs = {local(k): v for k, v in element.attrib.items()}
            event_map = {k: v for k, v in attrs.items() if k in EVENTS}
            kind = local(element.tag)
            if kind not in TYPES and not event_map:
                continue
            name = attrs.get("Name", "")
            label = resolve_resource(attrs.get("Content") or attrs.get("Text") or name or "/".join(event_map.values()), strings)
            options = [resolve_resource(c.attrib.get("Content", ""), strings)
                       for c in element if local(c.tag) == "ComboBoxItem"]
            bindings = []
            for event, handler in event_map.items():
                if handler not in methods:
                    raise ValueError(f"Unresolved handler {path.name}: {handler}")
                method = methods[handler]
                destinations = sorted(set(re.findall(r"(?:GetSelectedProfile\(\)|Settings|Profile)\.(\w+)\s*=", method["code"])))
                bindings.append({"event": event, "handler": handler, "destinations": destinations,
                                 "evidence": {"file": code_path.relative_to(SOURCE).as_posix(),
                                              "sha256": sha(code_path), **method}})
            ancestors = []
            current = element
            while current in parent_map:
                current = parent_map[current]
                parent_name = current.attrib.get("Name")
                if parent_name:
                    ancestors.append(parent_name)
            marker = f'Name="{name}"' if name else next((f'{k}="{v}"' for k, v in event_map.items()), f'NavigateUri="{attrs["NavigateUri"]}"' if "NavigateUri" in attrs else "<" + kind)
            offset = text.find(marker)
            controls.append({
                "file": path.relative_to(SOURCE).as_posix(), "line": text.count("\n", 0, max(0, offset)) + 1,
                "name": name, "type": kind, "label": label, "attributes": attrs,
                "options": options, "ancestors": ancestors, "bindings": bindings,
                "resourceTooltip": resolve_resource(attrs.get("ToolTip", ""), strings),
            })
    result = {
        "schema": 1, "tool": "ILSpyCmd 11.2.0.10050", "target": "LosslessScaling.dll 3.2.2.0",
        "targetSha256": "3dc441fda2f444e7ac4f33927ac0902c7801cdbe6237d95519972437a1af2337",
        "scope": "Static C#/BAML decompilation; generated project was not built; no target loaded or executed.",
        "files": files, "xamlFiles": len(list(SOURCE.rglob("*.xaml"))),
        "controls": controls, "controlsCount": len(controls),
        "boundEvents": sum(len(c["bindings"]) for c in controls),
        "resourceStringsCount": len(strings),
    }
    (DEEP / "wpf-controls.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    lines = [
        "# Scaling: contrato da janela WPF realmente aberta", "",
        "Aprofundamento estático de 08/11/2026, GDZ 1.1.27. O assembly gerenciado `LosslessScaling.dll` 3.2.2.0 foi decompilado em C# e seus recursos BAML em XAML pelo ILSpyCmd 11.2.0.10050. A análise anterior enumerava IL e assinaturas; esta etapa recupera as opções, limites e vínculos dos controles efetivamente usados no WPF.", "",
        "O projeto intermediário gerado pelo decompilador ficou em `artifacts/private/gdz-analysis/deep/wpf-decompiled`; **não foi compilado nem iniciado**. Não houve leitura do Settings.xml real do usuário. Os nomes são evidência da versão examinada, não especificação de um motor próprio.", "",
        f"Foram recuperados **{result['xamlFiles']} arquivos XAML**, **{len(files)} arquivos intermediários**, **{len(controls)} elementos interativos ou com eventos**, **{result['boundEvents']} vínculos de evento** e **{len(strings)} strings base de recursos**. Elementos de layout com SizeChanged e navegação também entram nessa contagem; ela não representa esse número de otimizações.", "",
        "## Novas regras úteis para implementar", "",
        "| Recurso | Contrato observado | Evidência C# privada |", "|---|---|---|",
        "| Algoritmos de escala | Off, LS1, FSR, NIS, SGSR, BicubicCAS, Anime4K, XBR, SharpBilinear, Integer e Nearest são valores distintos. Não equiparar o FSR espacial deste menu a integração FSR 3 do jogo. | UI/Profile.cs; UI.Pages/ProfilePage.xaml |",
        "| Geração de quadros | Rótulos visíveis GDZ 3.1, GDZ 2.3 e GDZ 1.1 correspondem por índice a LSFG3, LSFG2 e LSFG1; o rótulo não altera o enum. LSFI/LSFI1 persistidos são migrados para LSFG2/LSFG1 ao exibir o perfil. | ProfilePage.xaml.cs:66,340 |",
        "| LSFG3 | Fixed usa multiplicador, Adaptive usa alvo. Multiplicador na UI 1–20; alvo 10–1000. Entrada vazia do multiplicador volta a 2; do alvo usa refresh DWM arredondado. Valores são arredondados a três casas e convertidos para float. | ProfilePage.xaml.cs:385,404,426 |",
        "| Flow scale | Slider inteiro 25–100, passo 5. Exposto para LSFG2/3; valor inteiro enviado ao core, não fração 0–1. | ProfilePage.xaml; ProfilePage.xaml.cs:340,464 |",
        "| Performance | Toggle muda LSFGSize entre BALANCED=0 e PERFORMANCE=1; existe de verdade no WPF. | ProfilePage.xaml.cs:448 |",
        "| Nitidez | FSR/NIS/SGSR/BicubicCAS usam Sharpness 0–10; LS1 usa LS1Sharpness com máximo 4. Trocar algoritmo troca o campo editado. | ProfilePage.xaml.cs:167,274 |",
        "| Fator | Ao trocar algoritmo, Integer arredonda ao inteiro e passo 1; XBR arredonda a uma casa e ajusta décimo ímpar para baixo em 0,1, passo 0,2; demais arredondam a três casas, passo 0,1. ScaleFactor_ValueChanged só repõe 1 se vazio e salva Value: não repete essa coerção a cada entrada. O filtro de digitação aceita separador decimal da cultura atual. | ProfilePage.xaml.cs:167,244,252 |",
        "| Captura | DXGI=0 e WGC=1. QueueTarget na UI 0–2, padrão/entrada vazia 1. O contrato da ponte JavaScript legada é diferente. | ProfilePage.xaml; ProfilePage.xaml.cs:566,575 |",
        "| Enum versus opção oferecida | CaptureApiEnum também contém GDI=2 e SyncModeEnum contém VSYNC4=5, mas o XAML atual só oferece DXGI/WGC e Off/Default/Vsync/½/⅓. Valor persistível não comprova controle disponível no menu. | UI/Profile.cs; ProfilePage.xaml |",
        "| Cursor, latência e FPS | ClipCursor, AdjustCursorSpeed, HideCursor, ScaleCursor, MaxFrameLatency, GsyncSupport, HdrSupport e DrawFps têm handlers que escrevem perfil e chamam ProfileChanged. Não são controles decorativos no WPF. | ProfilePage.xaml.cs:473–566 |",
        "| GPU/monitor | UI oferece Auto e só adiciona nomes quando há mais de um. Índice que excede a lista volta a 0 na exibição. Handlers persistem o índice selecionado; enumeração e ordenação vêm do core. | ProfilePage.xaml.cs:17,665,682 |",
        "| Recorte | Quatro NumberBoxes de inteiros não negativos, vazios voltam a zero. São enviados Left,Top,Right,Bottom ao core. CropInput/WindowedMode não viram argumentos booleanos de ApplySettings. | ProfilePage.xaml.cs:605–652; MainWindow.xaml.cs:540 |",
        "| Aplicar alteração | Cada handler chama ProfileChanged, que salva XML. Reaplica parâmetros imediatamente somente quando status=TempDisabled e o perfil alterado é o mesmo objeto do perfil ativo. Edição durante Enabled não chama ApplySettings por esse método. | MainWindow.xaml.cs:531 |",
        "| Atalho padrão | Ctrl+Alt+S. Hook WH_KEYBOARD_LL=13 compara modificadores exatos, não apenas subconjunto. O primeiro keydown correspondente dispara callback e retorna 1; repetidos até keyup não repetem callback, mas seguem para CallNextHookEx. Não grava histórico de teclas nesse fluxo. | UI/Settings.cs; UI/GlobalKeyboardHook.cs |",
        "| Atalho editável | Aceita A–Z com modificadores, ou F1–F24; Alt usa SystemKey. Enquanto captura a combinação, descarta hook anterior; ao confirmar salva e cria novo hook. Uma combinação inválida não recria o hook nesse caminho, podendo deixá-lo inativo até confirmação válida. | SettingsPage.xaml.cs:111 |",
        "| Perfis automáticos | EVENT_SYSTEM_FOREGROUND=3; só inicia AutoScale quando Disabled. Delay 0 ativa de imediato; positivo cria Timer com dueTime=0 e período 1000 ms até tick=0. Não há revalidação de foco no callback da contagem mostrado. | ForegroundWatcher.cs; MainWindow.xaml.cs:261 |",
        "| Seleção por jogo | Percorre perfis em ordem, divide Path por ;, remove vazios e Trim. Wildcards ? e * viram . e .* após Regex.Escape, com âncoras ^$, IgnoreCase. Tenta caminho completo e basename; depois título principal do processo. Primeiro match vence. | MainWindow.xaml.cs:383,417,422 |",
        "| Sem perfil correspondente | ActivateCore usa o perfil selecionado (ou LastSelectedProfile quando seleção <0). AutoScale do watcher requer perfil correspondente; não usa esse fallback para iniciar sozinho. | MainWindow.xaml.cs:261,452,522 |",
        "| Botão manual | Inativo inicia contagem fixa de 5 segundos; outro clique cancela. No final verifica se a própria janela está em primeiro plano e abre aviso se estiver. Atalho pode ativar imediatamente em outra janela. | MainWindow.xaml.cs:230,839 |",
        "| Novo perfil | Nome não vazio; duplica parte dos parâmetros do perfil 0. A checagem de duplicidade compara todo texto informado com cada segmento de perfil existente; não normaliza todos os segmentos novos. Editar perfil não faz a mesma checagem. | ProfileNewPage.xaml.cs:53; ProfileEditPage.xaml.cs:49 |",
        "| Excluir perfil | Solicita confirmação, remove da lista, decrementa seleção e atualiza Items. Não chama SaveSettings diretamente nesse handler; a reapresentação pode disparar handlers que salvam, e o fechamento efetivo salva Settings. O handler já muda o status gerenciado para Disabled antes da confirmação. | MainWindow.xaml.cs:788,809,741 |",
        "| Ordem e padrão | Carrega e mantém primeiro perfil, ordena demais por Title na inicialização. Primeiro recebe título localizado e não pode ser editado/removido pelos botões normais. | MainWindow.xaml.cs:109,809 |",
        "| Persistência | XmlSerializer de Settings com lista GameProfiles; grava StreamWriter direto, sem replace atômico nesse corpo. Caminho em LocalApplicationData/ProductName/Settings.xml. ProductName vem dos recursos do assembly. | UI/Settings.cs; MainWindow.xaml.cs:34,66 |",
        "| XML corrompido | DirectoryNotFound/FileNotFound criam padrões. InvalidOperation de desserialização mostra mensagem, copia para CorruptedSettingsBackup.xml sobrescrevendo e recria. Falha nessa cópia não recebe um catch próprio. | MainWindow.xaml.cs:77 |",
        "| Autostart e bandeja | WPF grava HKCU Run usando nome LosslessScaling e caminho do assembly trocado para .exe com -StartMinimized. CloseToTray/MinimizeToTray controlam ocultação; saída explícita descarta hook/watcher e chama UnInit. | SettingsPage.xaml.cs:81; MainWindow.xaml.cs:741 |",
        "| Configurações globais | Tema System/Light/Dark; enum de idioma tem 39 entradas incluindo System, Português e Português brasileiro. Mudança de idioma marca reinício requerido. Estados collapsed persistidos não provam presença de todos os controles no XAML atual. | UI/Settings.cs; SettingsPage.xaml.cs:179 |",
        "", "## Todos os controles recuperados", "",
        "A tabela registra limites declarados no XAML e handlers. Esses limites valem para entrada pela UI, não provam validação equivalente ao editar XML diretamente. As opções numéricas também estão completas no JSON privado `wpf-controls.json`, com tooltips e código dos handlers vinculados.", "",
    ]
    current_file = None
    for control in controls:
        if control["file"] != current_file:
            current_file = control["file"]
            lines += ["", f"### {current_file}", "", "| Controle | Tipo / limites / opções | Destino e handlers | Evidência XAML |", "|---|---|---|---|"]
        attrs = control["attributes"]
        limits = "; ".join(f"{k}={attrs[k]}" for k in ("Minimum", "Maximum", "Value", "SmallChange", "TickFrequency", "IsSnapToTickEnabled", "SelectedIndex", "IsChecked", "Visibility") if k in attrs)
        if control["options"]:
            limits += "; opções: " + ", ".join(f"{i}={value}" for i, value in enumerate(control["options"]))
        handlers = "; ".join(f"{b['event']} → {b['handler']}" + (" → " + ",".join(b["destinations"]) if b["destinations"] else "") for b in control["bindings"]) or attrs.get("NavigateUri", "")
        lines.append("| " + " | ".join(cell(x) for x in (control["name"] or control["label"], control["type"] + ("; " + limits if limits else ""), handlers, f"L{control['line']}")) + " |")
    lines += ["", "## Critérios para uma implementação futura", "",
              "1. Validar limites da UI no serviço também, sem depender do XML ou do NumberBox. Manter valores, unidades e enums explícitos, com teste de ida e volta de persistência.",
              "2. Tratar seleção de perfil, atraso, cancelamento e estados Disabled/Countdown/Enabled/TempDisabled como máquina de estados; cobrir troca de foco durante delay e fechamento durante timer. O comportamento observado do alvo não deve ser assumido como livre de corridas.",
              "3. Confirmar que o motor escolhido suporta cada opção. Serializar DrawFps ou LSFG3 no perfil não implementa overlay nem interpolação. O core gráfico continua uma dependência separada.",
              "4. Separar este contrato do HTML legado e da allowlist scalingWrite. Eles têm capacidades e intervalos diferentes e não devem servir como única especificação da janela real.", "",
              "## Reproduzir a leitura", "",
              "Ferramenta oficial: [ILSpy / ILSpyCmd](https://github.com/icsharpcode/ILSpy/blob/master/ICSharpCode.ILSpyCmd/README.md). Pacote fixado: [ilspycmd 11.2.0.10050](https://www.nuget.org/packages/ilspycmd/11.2.0.10050). O decompilador lê bytes/metadados e converte BAML; não foi usado para executar funções do alvo.", "",
              "Após a decompilação privada, `python scripts/catalog-gdz-wpf.py` regenera esta tabela e o inventário. Linhas referem-se à view decompilada desta execução, cujos hashes constam no JSON. Consulte [533 métodos e 33 parâmetros do core](GDZ-SCALING-FUNCOES.md) e [extração nativa aprofundada](GDZ-MOTOR-NATIVO-APROFUNDADO.md).", ""]
    (ROOT / "docs/GDZ-SCALING-CONTRATO-WPF.md").write_text("\n".join(lines), encoding="utf-8")
    print(json.dumps({k: result[k] for k in ("controlsCount", "boundEvents", "xamlFiles", "resourceStringsCount")}, ensure_ascii=False))


if __name__ == "__main__":
    main()
