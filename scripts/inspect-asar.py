#!/usr/bin/env python3
"""Inspect an Electron ASAR without executing its contents (Python stdlib only).

Usage: python scripts/inspect-asar.py --archive APP.asar --output NEW_PRIVATE_DIR
       [--extract-text]

The output directory must not exist and must be outside the archive's directory.
manifest.json records SHA-256 for the archive and every packed file. Unpacked
entries are listed but never read; archive links are rejected. Optional extraction
copies only allowlisted, bounded UTF-8 text into text/. Nothing is imported,
evaluated, launched, or installed. Hashes establish identity, not publisher trust.

Format reference (consulted 2026-11-08):
https://github.com/electron/asar/blob/main/src/disk.ts
The size pickle occupies 8 bytes. The header pickle follows; its JSON string
starts at byte 8 inside that block. Packed offsets are relative to 8 + headerSize.

Paths and reparse points are checked before filesystem operations, and files are
created exclusively. On Windows, stdlib path operations do not provide an atomic
directory-handle sandbox against a concurrent process replacing parent folders.
Use a private output parent that other processes are not changing. Failed writes
may leave an incomplete new directory; existing output is never overwritten.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import struct
import sys
from typing import BinaryIO


MAX_ARCHIVE = 8 * 1024**3
MAX_HEADER = 32 * 1024**2
MAX_FILE = 512 * 1024**2
MAX_TEXT_FILE = 16 * 1024**2
MAX_TEXT_TOTAL = 256 * 1024**2
MAX_ENTRIES = 200_000
MAX_DEPTH = 64
CHUNK = 1024**2
SAFE_INTEGER = 2**53 - 1
TEXT_SUFFIXES = frozenset({
    ".js", ".cjs", ".mjs", ".jsx", ".ts", ".tsx", ".json", ".map",
    ".html", ".htm", ".css", ".scss", ".less", ".txt", ".md",
    ".xml", ".svg", ".yaml", ".yml", ".toml", ".ini", ".conf",
    ".config", ".csv", ".sql", ".graphql", ".gql", ".sh", ".ps1",
    ".psm1", ".bat", ".cmd", ".vbs", ".ahk", ".py", ".cs", ".resx",
})
TEXT_NAMES = frozenset({"license", "licence", "notice", "readme", "authors", "changelog"})
RESERVED = re.compile(r"^(?:CON|PRN|AUX|NUL|CLOCK\$|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])$", re.I)
INVALID_CHARS = frozenset('<>:"/\\|?*')
HEX256 = re.compile(r"[0-9a-fA-F]{64}\Z")


class InspectionError(Exception):
    """An invalid input or unsafe output was rejected."""


def require(condition: bool, message: str) -> None:
    if not condition:
        raise InspectionError(message)


def validate_name(name: str) -> None:
    require(isinstance(name, str) and name not in ("", ".", ".."), "Invalid empty/dot entry name")
    require(not any(ord(c) < 32 or c in INVALID_CHARS for c in name), f"Unsafe Windows entry name: {name!r}")
    require(not name.endswith((" ", ".")), f"Trailing dot/space in entry name: {name!r}")
    try:
        units = len(name.encode("utf-16-le")) // 2
    except UnicodeEncodeError as exc:
        raise InspectionError("Invalid Unicode in entry name") from exc
    require(units <= 255, "Entry name exceeds 255 UTF-16 units")
    require(not RESERVED.fullmatch(name.split(".", 1)[0]), f"Reserved Windows entry name: {name!r}")


def no_reparse(path: Path, *, must_exist: bool = True) -> os.stat_result | None:
    """Inspect a path without following its final component."""
    try:
        info = path.lstat()
    except FileNotFoundError:
        if must_exist:
            raise InspectionError(f"Missing path: {path}") from None
        return None
    require(not stat.S_ISLNK(info.st_mode) and not (getattr(info, "st_file_attributes", 0) & 0x400),
            f"Symlink/reparse point rejected: {path}")
    return info


def check_chain(path: Path, *, missing_ok: bool = False) -> None:
    for part in reversed((path, *path.parents)):
        info = no_reparse(part, must_exist=not missing_ok)
        if info is not None and part != path:
            require(stat.S_ISDIR(info.st_mode), f"Parent is not a directory: {part}")


def local_absolute(raw: str) -> Path:
    path = Path(os.path.abspath(os.path.expanduser(raw)))
    require(not (os.name == "nt" and path.drive.startswith("\\\\")), "Only local drive paths are supported")
    for name in path.parts[1:]:
        validate_name(name)
    require(len(str(path).encode("utf-16-le")) // 2 < 30_000, "Path is too long")
    return path


def unique_object(pairs: list[tuple[str, object]]) -> dict:
    result: dict = {}
    for key, value in pairs:
        require(key not in result, f"Duplicate JSON key: {key!r}")
        result[key] = value
    return result


def read_exact(stream: BinaryIO, size: int) -> bytes:
    data = stream.read(size)
    require(len(data) == size, "Archive truncated while reading")
    return data


def fingerprint(info: os.stat_result) -> tuple[int, int, int, int]:
    # Windows lstat/fstat can expose different creation-time values as st_ctime.
    # Identity, length and modification time are comparable across both APIs.
    return (info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns)


def read_header(stream: BinaryIO, archive_size: int) -> tuple[dict, int, bytes]:
    size_payload, header_size = struct.unpack("<II", read_exact(stream, 8))
    require(size_payload == 4, "Invalid size pickle payload length")
    require(8 <= header_size <= MAX_HEADER and header_size % 4 == 0, "Invalid/excessive header size")
    require(8 + header_size <= archive_size, "Header extends beyond archive")
    block = read_exact(stream, header_size)
    payload_size, json_size = struct.unpack_from("<II", block)
    require(payload_size == header_size - 4, "Invalid header pickle payload length")
    require(header_size == 8 + ((json_size + 3) & ~3), "Invalid JSON string size/alignment")
    require(all(b == 0 for b in block[8 + json_size:]), "Nonzero pickle padding")
    raw_json = block[8:8 + json_size]
    try:
        header = json.loads(raw_json.decode("utf-8"), object_pairs_hook=unique_object,
                            parse_constant=lambda value: (_ for _ in ()).throw(InspectionError(f"Invalid JSON constant: {value}")))
    except (UnicodeDecodeError, json.JSONDecodeError, RecursionError) as exc:
        raise InspectionError(f"Invalid header JSON: {type(exc).__name__}") from exc
    require(isinstance(header, dict) and isinstance(header.get("files"), dict), "Header root must contain a files object")
    return header, 8 + header_size, raw_json


def bounded_integer(value: object, maximum: int, label: str) -> int:
    require(type(value) is int and 0 <= value <= maximum, f"Invalid/excessive {label}")
    return value


def collect_entries(header: dict, data_offset: int, archive_size: int) -> list[dict]:
    entries: list[dict] = []
    ranges: list[tuple[int, int, str]] = []

    def walk(files: dict, parents: tuple[str, ...], inherited_unpacked: bool) -> None:
        require(len(parents) <= MAX_DEPTH, "Archive exceeds directory depth limit")
        seen: set[str] = set()
        for name, node in files.items():
            validate_name(name)
            require(name.casefold() not in seen, "Case-insensitive entry collision")
            seen.add(name.casefold())
            require(isinstance(node, dict), "Entry must be an object")
            for flag in ("unpacked", "executable"):
                require(flag not in node or type(node[flag]) is bool, f"Invalid {flag} flag")
            require("link" not in node, "Archive symlink entries are not supported")
            parts = (*parents, name)
            require(len(parts) <= MAX_DEPTH, "Archive exceeds path depth limit")
            relative = "/".join(parts)
            require(len(relative.encode("utf-16-le")) // 2 <= 4096, "Archive path exceeds length limit")
            unpacked = inherited_unpacked or node.get("unpacked", False)
            require(len(entries) < MAX_ENTRIES, "Archive exceeds entry count limit")
            if "files" in node:
                require(isinstance(node["files"], dict) and "size" not in node and "offset" not in node,
                        "Invalid directory entry")
                entries.append({"path": relative, "type": "directory", "unpacked": unpacked})
                walk(node["files"], parts, unpacked)
                continue
            size = bounded_integer(node.get("size"), MAX_FILE, "file size")
            entry = {"path": relative, "type": "file", "size": size, "unpacked": unpacked,
                     "sha256": None, "extracted": False}
            if unpacked:
                entry["skipReason"] = "unpacked_not_read"
            else:
                offset_string = node.get("offset")
                require(isinstance(offset_string, str) and re.fullmatch(r"[0-9]+", offset_string) is not None
                        and len(offset_string) <= 16, "Invalid packed file offset")
                offset = bounded_integer(int(offset_string), SAFE_INTEGER, "packed file offset")
                absolute = data_offset + offset
                require(absolute <= archive_size and size <= archive_size - absolute, f"File extends beyond archive: {relative}")
                entry["offset"] = offset
                if size:
                    ranges.append((absolute, absolute + size, relative))
            if "integrity" in node:
                integrity = node["integrity"]
                require(isinstance(integrity, dict), "Invalid integrity metadata")
                require(integrity.get("algorithm") == "SHA256" and isinstance(integrity.get("hash"), str)
                        and HEX256.fullmatch(integrity["hash"]) is not None, "Unsupported/invalid integrity hash")
                entry["declaredSha256"] = integrity["hash"].lower()
            entries.append(entry)

    walk(header["files"], (), False)
    previous: tuple[int, int, str] | None = None
    for current in sorted(ranges):
        if previous is not None:
            require(current[0] >= previous[1] or current[:2] == previous[:2], "Partially overlapping packed file ranges")
        previous = current
    return entries


def hash_region(stream: BinaryIO, offset: int, size: int) -> str:
    stream.seek(offset)
    digest = hashlib.sha256()
    while size:
        chunk = read_exact(stream, min(size, CHUNK))
        digest.update(chunk)
        size -= len(chunk)
    return digest.hexdigest()


def text_candidate(entry: dict) -> bool:
    name = Path(entry["path"]).name
    return Path(name).suffix.lower() in TEXT_SUFFIXES or name.lower() in TEXT_NAMES


def is_utf8_text(data: bytes) -> bool:
    try:
        decoded = data.decode("utf-8-sig")
    except UnicodeDecodeError:
        return False
    return not any(ord(c) < 32 and c not in "\t\r\n\f" or 0x7f <= ord(c) < 0xa0 for c in decoded)


def make_directory(path: Path) -> None:
    missing: list[Path] = []
    cursor = path
    while no_reparse(cursor, must_exist=False) is None:
        missing.append(cursor)
        cursor = cursor.parent
    check_chain(cursor)
    require(cursor.is_dir(), f"Not a directory: {cursor}")
    for directory in reversed(missing):
        check_chain(directory.parent)
        directory.mkdir(mode=0o700)
        no_reparse(directory)


def exclusive_write(path: Path, data: bytes) -> None:
    check_chain(path.parent)
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0)
    with os.fdopen(os.open(path, flags, 0o600), "wb") as output:
        info = os.fstat(output.fileno())
        require(stat.S_ISREG(info.st_mode) and not (getattr(info, "st_file_attributes", 0) & 0x400), "Unsafe output file")
        output.write(data)


def inspect(archive: Path, output: Path, extract_text: bool) -> dict:
    check_chain(archive)
    check_chain(output, missing_ok=True)
    require(no_reparse(output, must_exist=False) is None, "Output already exists; refusing to overwrite")
    require(not output.is_relative_to(archive.parent), "Output must be outside the archive's source directory")
    before = no_reparse(archive)
    require(before is not None and stat.S_ISREG(before.st_mode), "Archive must be a regular file")
    require(16 <= before.st_size <= MAX_ARCHIVE, "Archive size is outside inspection limits")
    flags = os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0)
    with os.fdopen(os.open(archive, flags), "rb") as source:
        initial = os.fstat(source.fileno())
        require(fingerprint(initial) == fingerprint(before), "Archive changed while opening")
        header, data_offset, raw_json = read_header(source, initial.st_size)
        entries = collect_entries(header, data_offset, initial.st_size)
        archive_hash = hash_region(source, 0, initial.st_size)
        text_total = 0
        extraction: list[dict] = []
        for entry in entries:
            if entry["type"] != "file" or entry["unpacked"]:
                continue
            entry["sha256"] = hash_region(source, data_offset + entry["offset"], entry["size"])
            if "declaredSha256" in entry:
                require(entry["sha256"] == entry["declaredSha256"], f"Declared integrity mismatch: {entry['path']}")
                entry["declaredSha256Verified"] = True
            if not extract_text:
                entry["skipReason"] = "extraction_not_requested"
            elif not text_candidate(entry):
                entry["skipReason"] = "extension_not_allowlisted"
            elif entry["size"] > MAX_TEXT_FILE:
                entry["skipReason"] = "text_file_size_limit"
            else:
                source.seek(data_offset + entry["offset"])
                content = read_exact(source, entry["size"])
                if not is_utf8_text(content):
                    entry["skipReason"] = "not_utf8_text"
                    continue
                text_total += entry["size"]
                require(text_total <= MAX_TEXT_TOTAL, "Text extraction exceeds total size limit")
                extraction.append(entry)
        require(fingerprint(os.fstat(source.fileno())) == fingerprint(initial), "Archive changed during inspection")
        check_chain(archive)
        require(fingerprint(no_reparse(archive)) == fingerprint(initial), "Archive replaced during inspection")
        make_directory(output.parent)
        check_chain(output.parent)
        output.mkdir(mode=0o700)  # Exclusive: even an empty existing destination is refused.
        for entry in extraction:
            target = output / "text" / Path(*entry["path"].split("/"))
            require(target.is_relative_to(output / "text"), "Extraction path escaped destination")
            require(len(str(target).encode("utf-16-le")) // 2 < 30_000, "Extraction target path is too long")
            make_directory(target.parent)
            source.seek(data_offset + entry["offset"])
            content = read_exact(source, entry["size"])
            require(hashlib.sha256(content).hexdigest() == entry["sha256"], "Archive content changed before extraction")
            exclusive_write(target, content)
            entry["extracted"] = True
            entry["outputPath"] = "text/" + entry["path"]
        require(fingerprint(os.fstat(source.fileno())) == fingerprint(initial), "Archive changed during extraction")
        manifest = {
            "schemaVersion": 1,
            "createdAtUtc": datetime.now(timezone.utc).isoformat(),
            "archive": {"path": str(archive), "size": initial.st_size, "sha256": archive_hash,
                        "headerJsonSha256": hashlib.sha256(raw_json).hexdigest(), "dataOffset": data_offset},
            "policy": {"execution": "never", "unpacked": "listed_only_not_read", "links": "rejected",
                       "textEncoding": "UTF-8", "extractText": extract_text,
                       "maxArchiveBytes": MAX_ARCHIVE, "maxHeaderBytes": MAX_HEADER, "maxFileBytes": MAX_FILE,
                       "maxTextFileBytes": MAX_TEXT_FILE, "maxTextTotalBytes": MAX_TEXT_TOTAL,
                       "maxEntries": MAX_ENTRIES, "maxDepth": MAX_DEPTH,
                       "textExtensions": sorted(TEXT_SUFFIXES), "textNames": sorted(TEXT_NAMES)},
            "summary": {"entries": len(entries), "packedFiles": sum(e["type"] == "file" and not e["unpacked"] for e in entries),
                        "unpackedFiles": sum(e["type"] == "file" and e["unpacked"] for e in entries),
                        "extractedTextFiles": len(extraction), "extractedTextBytes": text_total},
            "entries": entries,
        }
        exclusive_write(output / "manifest.json", (json.dumps(manifest, ensure_ascii=True, indent=2) + "\n").encode("utf-8"))
        return manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--archive", required=True, help="Existing local ASAR file (read only)")
    parser.add_argument("--output", required=True, help="New private output directory, outside the source directory")
    parser.add_argument("--extract-text", action="store_true", help="Also copy bounded allowlisted UTF-8 text; never execute it")
    args = parser.parse_args()
    try:
        manifest = inspect(local_absolute(args.archive), local_absolute(args.output), args.extract_text)
    except (InspectionError, OSError, ValueError, OverflowError, RecursionError) as exc:
        print(f"Inspection failed: {exc}", file=sys.stderr)
        return 1
    print(json.dumps({"manifest": str(local_absolute(args.output) / "manifest.json"),
                      "archiveSha256": manifest["archive"]["sha256"], **manifest["summary"]}, ensure_ascii=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
