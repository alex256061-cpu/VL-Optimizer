#!/usr/bin/env python3
"""Cross-check extracted specifications and byte ranges; never execute targets."""
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
DEEP = ROOT / "artifacts/private/gdz-analysis/deep"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def read(name):
    return json.loads((DEEP / name).read_text(encoding="utf-8-sig"))


def main():
    checks = []

    def check(name, condition):
        checks.append({"name": name, "passed": bool(condition)})
        if not condition:
            raise ValueError("Failed: " + name)

    focused = DEEP / "backend-focused.js"
    focused_hash = digest(focused.read_bytes())
    operations = read("operation-specs.json")
    declarative = read("operations-declarative.json")
    embedded = read("embedded-contracts.json")
    for label, value in (("operations", operations), ("declarative", declarative), ("embedded", embedded)):
        check(label + "_source_hash", value["source"]["sha256"] == focused_hash)
        check(label + "_source_file", Path(value["source"]["file"]).resolve() == focused.resolve())
    check("69_unique_operations", len(operations["operations"]) == len({x["id"] for x in operations["operations"]}) == 69)
    check("operations_not_mislabeled_as_effects_observed", all(x["effectsVerified"] is False for x in operations["operations"]))
    check("36_base_33_tweaks", sum(x["kind"] == "base-action" for x in operations["operations"]) == 36
          and operations["counts"]["tweaks"] == 33)
    check("2_patterns_8_legacy", len(operations["patterns"]) == 2 and len(operations["legacyRevertOnly"]) == 8)
    check("16_cleaners_12_games_4_dns_2_presets", len(operations["cleanCategories"]) == 16
          and len(operations["games"]) == 12 and len(operations["dnsProviders"]) == 4
          and set(operations["fiveMPresets"]) == {"fps", "balanced"})
    checks_op = read("operation-specs-verification.json")
    check("operation_specific_checks", len(checks_op) >= 19 and all(value is True for value in checks_op.values()))
    check("32_embedded_constants_resolved", len(embedded["constants"]) == 32 and not embedded["unresolved"])
    check("6_csharp_groups_54_payloads", len(embedded["csharp"]) == 6 and len(embedded["payloads"]) == 54)
    check("embedded_payload_hashes", all(digest(x["value"].encode("utf-8")) == x["sha256"]
          and len(x["value"].encode("utf-8")) == x["bytes"] for x in embedded["payloads"]))
    algorithms = read("algorithm-contracts.json")
    algorithm_checks = read("algorithm-contracts-verification.json")
    check("algorithm_source_and_report_hashes", algorithms["source"]["sha256"] == focused_hash
          and digest((ROOT / "docs/GDZ-ALGORITMOS-E-CONTRATOS.md").read_bytes()) == algorithms["reportSha256"])
    check("34_unique_algorithm_contracts", len(algorithms["contracts"]) == algorithms["contractCount"] == 34
          and len({x["id"] for x in algorithms["contracts"]}) == 34)
    check("12_file_contracts", len(algorithms["files"]) == 12)
    check("algorithm_specific_checks", algorithm_checks["passed"] == algorithm_checks["total"] == 12
          and all(x["passed"] for x in algorithm_checks["checks"]) and not algorithm_checks["targetExecuted"])

    wpf = read("wpf-controls.json")
    source = DEEP / "wpf-decompiled"
    check("wpf_all_file_hashes", all(digest((source / x["file"]).read_bytes()) == x["sha256"]
          and (source / x["file"]).stat().st_size == x["bytes"] for x in wpf["files"]))
    check("7_xaml_files", len(list(source.rglob("*.xaml"))) == wpf["xamlFiles"] == 7)
    check("all_xaml_parse", all(ET.parse(path).getroot() is not None for path in source.rglob("*.xaml")))
    check("72_wpf_elements_81_events", len(wpf["controls"]) == wpf["controlsCount"] == 72
          and sum(len(c["bindings"]) for c in wpf["controls"]) == wpf["boundEvents"] == 81)
    check("158_base_resource_strings", len(ET.parse(source / "UI.Resources.Strings.resx").getroot().findall("data"))
          == wpf["resourceStringsCount"] == 158)
    managed = read("native-functions.json")
    core_strings = [x for x in managed["methods"] if x["type"] == "UI.Core"
                    and x["name"] in ("GetAdapterNames", "GetDisplayNames")]
    check("BSTR_marshal_0x13_confirmed", len(core_strings) == 2 and all(
        next(p for p in x["parameters"] if p["sequence"] == 0)["marshal"] == "13"
        and "BStr" in x["explanation"] for x in core_strings)
        and (source / "UI/Core.cs").read_text(encoding="utf-8").count("MarshalAs(UnmanagedType.BStr)") == 2)
    callback = next(x for x in managed["methods"] if x["type"] == ".StatusListenerDelegate" and x["name"] == "Invoke")
    check("callback_resized_I1_confirmed", next(p for p in callback["parameters"] if p["name"] == "resized")["marshal"] == "03")
    event_evidence_valid = True
    for control in wpf["controls"]:
        for binding in control["bindings"]:
            e = binding["evidence"]
            path = source / e["file"]
            text = path.read_text(encoding="utf-8-sig")
            lines = text.splitlines()
            actual = "\n".join(lines[e["line"] - 1:e["endLine"]])
            event_evidence_valid &= (digest(path.read_bytes()) == e["sha256"]
                                     and actual == e["code"] and binding["handler"] in actual)
    check("all_81_event_bodies_and_lines", event_evidence_valid)

    native = read("native-deep/manifest.json")
    resources = read("native-deep/resources-shaders.json")
    asm = read("native-deep/shader-disassembly-manifest.json")
    data = Path(native["target"]).read_bytes()
    check("installed_native_dll_unchanged", digest(data) == native["sha256"] == resources["target_sha256"])
    check("11_exports_match_managed_contract", {x["name"] for x in native["exports"]}
          == {x["name"] for x in read("native-functions.json")["nativeExports"]})
    native_contracts = read("native-deep/native-contracts.json")
    native_checks = read("native-deep/verification.json")
    check("11_native_contracts_33_parameter_rows", native_contracts["target_sha256"] == native["sha256"]
          and len(native_contracts["exports"]) == 11 and len(native_contracts["apply_settings"]) == 33)
    check("native_specific_checks", len(native_checks["checks"]) >= 19
          and len(native_checks["checks"]) == native_checks["coverage"]["checks_passed"]
          and all(x["passed"] for x in native_checks["checks"]))
    check("302_pe_resources_hashes", len(resources["resources"]) == 302 and all(
        digest(data[x["offset"]:x["offset"] + x["size"]]) == x["sha256"] for x in resources["resources"]))
    check("202_dxbc_98_spirv", len(resources["shaders"]) == 202 and len(resources["spirv"]) == 98)
    programs = resources["shaders"] + resources["spirv"]
    check("all_300_program_byte_ranges", all(
        digest(data[x["file_offset"]:x["file_offset"] + x["size"]]) == x["sha256"] for x in programs))
    check("all_300_instruction_framing", all(x["valid_instruction_stream"] for x in programs))
    check("202_compute_sm5_thread_groups", all(x["stage"] == "compute" and x["model"] == "5.0"
          and x["thread_group"] for x in resources["shaders"]))
    check("98_spirv_compute_entries", all(x["version"] == "1.0" and x["entry_points"]
          and all(e["execution_model"] == 5 for e in x["entry_points"]) for x in resources["spirv"]))
    check("300_disassembly_outputs", len(asm["outputs"]) == 300
          and sum(x["format"] == "DXBC" for x in asm["outputs"]) == 202
          and sum(x["format"] == "SPIR-V" for x in asm["outputs"]) == 98)
    check("all_300_asm_file_hashes", all(
        digest((DEEP / "native-deep/shader-asm" / x["file"]).read_bytes()) == x["output_sha256"]
        and (DEEP / "native-deep/shader-asm" / x["file"]).stat().st_size == x["bytes"] for x in asm["outputs"]))
    check("disassembly_sources_match_resources", {x["source_sha256"] for x in asm["outputs"]}
          == {x["sha256"] for x in programs})
    check("no_unicode_replacement_in_new_json", all("\ufffd" not in (DEEP / name).read_text(encoding="utf-8")
          for name in ("operation-specs.json", "operations-declarative.json", "embedded-contracts.json", "wpf-controls.json")))
    result = {"schema": 1, "verifiedAtUtc": datetime.now(timezone.utc).isoformat(),
              "scope": "Static provenance/coverage/byte-range checks only; framing is not shader semantic validation; no target code executed.",
              "checks": checks, "passed": len(checks),
              "counts": {"operations": 69, "wpfElements": 72, "wpfEvents": 81, "dxbc": 202, "spirv": 98}}
    (DEEP / "deep-verification.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"passed": len(checks), "counts": result["counts"]}))


if __name__ == "__main__":
    main()
