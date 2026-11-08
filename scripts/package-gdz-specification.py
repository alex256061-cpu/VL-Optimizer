#!/usr/bin/env python3
"""Package our reports/tools and derived specs, excluding target code/binaries."""
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parent.parent
DEEP = ROOT / "artifacts/private/gdz-analysis/deep"


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def read(name):
    return json.loads((DEEP / name).read_text(encoding="utf-8-sig"))


def main():
    entries = {}

    def add(path):
        entries[path.relative_to(ROOT).as_posix()] = path.read_bytes()

    for report in sorted((ROOT / "docs").glob("GDZ-*.md")):
        add(report)
    for filename in (
        "inspect-asar.py", "analyze-gdz.ps1", "decode-gdz-static.cjs", "index-gdz-functions.cjs",
        "build-gdz-internal-catalog.py", "build-gdz-bound-catalog.py", "verify-gdz-catalog.py",
        "extract-gdz-operations.cjs", "build-gdz-operation-specs.py",
        "extract-gdz-contracts.cjs", "build-gdz-algorithm-contracts.py",
        "catalog-gdz-wpf.py", "verify-gdz-deep.py", "package-gdz-specification.py",
    ):
        add(ROOT / "scripts" / filename)
    for script in sorted((ROOT / "scripts/gdz-native").glob("*.py")):
        add(script)
    add(ROOT / "scripts/gdz-native/README.md")
    for filename in ("package.json", "package-lock.json"):
        add(ROOT / ".tools/gdz-static" / filename)

    # These are descriptive contracts written during analysis, without target
    # function bodies. Raw JavaScript/C#/BAML/bytecode remain in private evidence.
    algorithms = read("algorithm-contracts.json")
    entries["data/algoritmos-e-arquivos.json"] = json_bytes(algorithms)
    ops = read("operation-specs.json")
    safe_ops = {key: ops[key] for key in ("schema", "version", "source", "scope", "counts", "syntheticScenario")}
    safe_ops["operations"] = []
    allowed = {"id", "kind", "request", "inPages", "summary", "conditions", "uiCapability", "evidence", "effectsVerified",
               "snapshotSpecSyntheticScenario", "applicationReportsOnWithoutRunningDetector", "postSnapshotIsRecordedButNotCompared"}
    for operation in ops["operations"]:
        safe_ops["operations"].append({k: v for k, v in operation.items() if k in allowed})
    safe_ops["note"] = "Full script/probe bodies and dynamic catalog expressions stay in private evidence; see report for exact values and behavior. SyntheticScenario is illustrative, not this PC."
    entries["data/operacoes-descritas.json"] = json_bytes(safe_ops)
    wpf = read("wpf-controls.json")
    for control in wpf["controls"]:
        for binding in control["bindings"]:
            binding["evidence"].pop("code", None)
    entries["data/controles-wpf.json"] = json_bytes(wpf)
    shaders = read("native-deep/resources-shaders.json")
    shader_summary = {"targetSha256": shaders["target_sha256"], "coverage": shaders["coverage"],
                      "scope": "Inventory and reflection data only; no shader bytecode, assembly listing or implementation included.",
                      "dxbc": [{k: s[k] for k in ("resource_path", "sha256", "size", "stage", "model", "thread_group", "bindings", "constant_buffers")}
                               for s in shaders["shaders"]],
                      "spirv": [{k: s[k] for k in ("resource_path", "sha256", "size", "version", "entry_points", "execution_modes", "names", "bindings")}
                                for s in shaders["spirv"]]}
    entries["data/inventario-shaders.json"] = json_bytes(shader_summary)
    for filename in ("catalog-verification.json", "deep-verification.json", "operation-specs-verification.json", "algorithm-contracts-verification.json"):
        entries["verification/" + filename] = (DEEP / filename).read_bytes()
    entries["verification/native-verification.json"] = (DEEP / "native-deep/verification.json").read_bytes()
    entries["verification/ilspy-tool-provenance.json"] = (DEEP / "ilspy-tool-provenance.json").read_bytes()

    entries["LEIAME.md"] = (
        "# Extração aprofundada do GDZ 1.1.27\n\n"
        "Comece em docs/GDZ-ESPECIFICACAO-PARA-IMPLEMENTACAO.md.\n\n"
        "Este pacote contém relatórios, ferramentas de análise próprios, dados derivados e verificações estáticas. "
        "Nenhuma função foi incorporada ao produto e a interface existente foi preservada.\n\n"
        "Não contém executáveis/DLLs/ASAR do alvo, decompilação C#/JavaScript, XAML original ou shaders/bytecode/assembly do alvo. "
        "As evidências completas continuam em artifacts/private/gdz-analysis na máquina de origem.\n\n"
        "Scripts de análise pressupõem a estrutura de diretórios do workspace e as evidências/dependências indicadas nos capítulos. "
        "As dependências .tools não estão incluídas; os dois manifests Babel permitem reproduzir a instalação dessa dependência. "
        "Não execute comandos de aplicação do otimizador para reproduzir a leitura estática.\n\n"
        "Os dados de capacidade com C:\\Example são sintéticos. As contagens se sobrepõem e não devem ser somadas como novas otimizações. "
        "Verificações de hashes, cobertura e estrutura não demonstram efeitos no Windows nem equivalência integral do pipeline gráfico.\n"
    ).encode("utf-8")
    manifest = {"schema": 1, "createdAtUtc": datetime.now(timezone.utc).isoformat(),
                "scope": "Own reports/tools and derived descriptive evidence; excludes target programs/source/shaders.",
                "files": [{"path": name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}
                          for name, data in sorted(entries.items())]}
    entries["MANIFEST.json"] = json_bytes(manifest)
    output = ROOT / "artifacts/GDZ-Especificacao-Aprofundada-1.1.27-2026-11-08.zip"
    output.parent.mkdir(exist_ok=True)
    with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(entries.items()):
            archive.writestr(name, data)
    with zipfile.ZipFile(output) as archive:
        assert archive.testzip() is None
        assert set(archive.namelist()) == set(entries)
        for row in manifest["files"]:
            content = archive.read(row["path"])
            assert len(content) == row["bytes"] and hashlib.sha256(content).hexdigest() == row["sha256"]
    result = {"archive": str(output), "bytes": output.stat().st_size,
              "sha256": hashlib.sha256(output.read_bytes()).hexdigest(), "files": len(entries), "manifest": manifest}
    output.with_suffix(".manifest.json").write_bytes(json_bytes(result))
    print(json.dumps({k: v for k, v in result.items() if k != "manifest"}))


if __name__ == "__main__":
    main()
