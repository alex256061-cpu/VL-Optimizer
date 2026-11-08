#!/usr/bin/env python3
"""Cross-check the GDZ reports against static evidence; never execute targets."""
from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parent.parent
DEEP = ROOT / "artifacts/private/gdz-analysis/deep"


def read_json(name: str):
    return json.loads((DEEP / name).read_text(encoding="utf-8-sig"))


def sha(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def main() -> None:
    checks = []

    def check(name: str, condition: bool) -> None:
        checks.append({"name": name, "passed": bool(condition)})
        if not condition:
            raise ValueError(f"Verification failed: {name}")

    meta = read_json("backend-static-metadata.json")
    original = ROOT / "artifacts/private/gdz-analysis/extracted/app-main.js"
    check("original_javascript_sha256", sha(original) == meta["sourceSha256"])
    check("decoder_checksum", meta["stringArray"]["checksum"] == meta["stringArray"]["target"])
    fixtures = meta["regressionFixtures"]
    check("decoder_regressions", len(fixtures) >= 13 and all(x["passed"] for x in fixtures))
    check("readable_syntax_validation", meta["validation"]["readableReparsed"])
    check("focused_syntax_validation", meta["validation"]["focusedReparsed"])

    declarations = read_json("functions-explained.json")
    bound = read_json("bound-functions-explained.json")
    recovered = read_json("backend-functions-readable.json")
    readable_names = {x["name"] for x in recovered}
    described_names = [x["name"] for x in declarations["functions"]] + [x["name"] for x in bound]
    check("229_named_bindings_exact_coverage", len(described_names) == len(set(described_names)) == 229
          and set(described_names) == readable_names)
    focused_hash = sha(DEEP / "backend-focused.js")
    check("159_catalog_matches_final_view", declarations["focusedSha256"] == focused_hash)
    focused_lines = (DEEP / "backend-focused.js").read_text(encoding="utf-8").splitlines()
    check("named_function_line_references", all(
        x["focusedLine"] >= 1 and x["focusedEndLine"] <= len(focused_lines)
        and x["name"] in focused_lines[x["focusedLine"] - 1] for x in recovered))

    index = read_json("function-index.json")
    check("all_original_js_blocks_parsed", index["summary"]["parseErrors"] == 0
          and index["summary"]["sourceFiles"] == 21 and index["summary"]["functions"] == 5019)
    check("original_js_file_hashes", all(sha(ROOT / "artifacts/private/gdz-analysis/extracted" / x["file"]) == x["sha256"]
          for x in index["files"]))

    features = read_json("frontend-features.json")
    check("78_pages_entries", len(features["pages_entries"]) == features["stats"]["pages_declared"] == 78)
    check("additional_resources_count", len(features["additional_features"]) == features["stats"]["additional_features"])
    check("resource_total", features["stats"]["total_catalogued"] == len(features["pages_entries"]) + len(features["additional_features"]))
    check("resource_unique_keys", len({x["key"] for x in features["pages_entries"]}) == 78)
    check("33_tweak_definitions", sum(x["constructor"] == "T" for x in features["pages_entries"]) == 33)

    native = read_json("native-functions.json")
    check("533_managed_methods", len(native["methods"]) == native["coverage"]["methodCount"] == 533)
    check("11_native_exports", len(native["nativeExports"]) == 11)
    check("33_settings_parameters", len(native["applySettingsParameterMap"]) == 33)
    check("42_profile_properties", len(native["profileProperties"]) == 42)
    check("native_category_total", sum(native["coverage"]["categories"].values()) == 533)

    ipc = read_json("ipc-explained.json")
    check("ipc_final_source_hash", ipc["source"]["sha256"] == focused_hash)
    check("87_invokes", len(ipc["invoke"]) == 87)
    check("85_registered_2_absent", sum(x["registered"] for x in ipc["invoke"]) == 85
          and {x["channel"] for x in ipc["invoke"] if not x["registered"]} == {"ls-browse-exe", "ls-find-exe"})
    check("9_sends_4_events", len(ipc["send"]) == 9 and len(ipc["events"]) == 4)
    preload = (ROOT / "artifacts/private/gdz-analysis/extracted/preload.js").read_text(encoding="utf-8")
    for const_name, key in [("INVOKE_CHANNELS", "invoke"), ("SEND_CHANNELS", "send"), ("ON_CHANNELS", "events")]:
        match = re.search(r"const\s+" + const_name + r"\s*=\s*\[(.*?)\]\s*;", preload, re.S)
        expected = re.findall(r"'([^']+)'", match.group(1))
        actual = [x["channel"] for x in ipc[key]]
        check("preload_" + key + "_exact_coverage", len(actual) == len(set(actual)) and set(actual) == set(expected))

    reports = sorted((ROOT / "docs").glob("GDZ-*.md"))
    missing = []
    replacements = []
    for file in reports:
        text = file.read_text(encoding="utf-8-sig")
        if "\ufffd" in text:
            replacements.append(file.name)
        # Command fragments can contain ]( without being Markdown links.
        prose = re.sub(r"```[^\n]*\n.*?```", "", text, flags=re.S)
        prose = re.sub(r"`[^`\n]*`", "", prose)
        for raw in re.findall(r"\]\(([^)]+)\)", prose):
            if "://" in raw or raw.startswith("#"):
                continue
            local = raw.strip("<>").split("#", 1)[0]
            if not (file.parent / local).exists():
                missing.append({"file": file.name, "target": raw})
    check("report_local_links", not missing)
    check("report_unicode_integrity", not replacements)

    asar_manifest = ROOT / "artifacts/private/gdz-analysis/verified-asar/manifest.json"
    archive = json.loads(asar_manifest.read_text(encoding="utf-8"))["archive"]
    check("installed_asar_unchanged", sha(Path(archive["path"])) == archive["sha256"])
    native_path = Path(archive["path"] + ".unpacked") / "bin/LosslessScaling.dll"
    check("installed_scaling_assembly_unchanged", sha(native_path) == native["scope"]["assemblySha256"])

    result = {
        "schemaVersion": 1, "verifiedAtUtc": datetime.now(timezone.utc).isoformat(),
        "checks": checks, "passed": len(checks), "sourceOriginalSha256": meta["sourceSha256"],
        "focusedSha256": focused_hash, "archiveSha256": archive["sha256"],
        "counts": {"namedFunctionsExplained": len(described_names),
                   "productResourcesAndControls": features["stats"]["total_catalogued"],
                   "rendererHelpers": len(features["renderer_helpers"]),
                   "invokeAllowed": len(ipc["invoke"]), "invokeRegistered": 85,
                   "sendChannels": len(ipc["send"]), "eventChannels": len(ipc["events"]),
                   "managedMethodsEnumerated": len(native["methods"]),
                   "nativeExports": len(native["nativeExports"]), "reports": len(reports)},
        "scope": "Static cross-checks; no target binary/JS/IPC/PowerShell executed. Counts do not prove runtime equivalence or performance gains.",
    }
    (DEEP / "catalog-verification.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"passed": len(checks), "counts": result["counts"]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
