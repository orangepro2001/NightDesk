#!/usr/bin/env python3
"""Atomically publish a verified Windows NightDesk ZIP to the AI feed."""
import argparse
import hashlib
import json
import os
import re
import shutil
import time
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PACKAGE_NAME = "dev.nightdesk.windows"
MAX_SIZE = 300 * 1024 * 1024


def valid_hex(value):
    return isinstance(value, str) and len(value) == 64 and all(c in "0123456789abcdef" for c in value.lower())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--zip", required=True)
    ap.add_argument("--version", required=True)
    ap.add_argument("--version-code", type=int, required=True)
    ap.add_argument("--notes", default="")
    args = ap.parse_args()

    if not re.fullmatch(r"\d+\.\d+\.\d+", args.version):
        raise SystemExit("version 必须为 x.y.z")
    package = Path(args.zip).resolve()
    if not package.is_file() or package.stat().st_size <= 0:
        raise SystemExit(f"Windows ZIP 不存在或为空：{package}")
    if package.stat().st_size > MAX_SIZE:
        raise SystemExit("Windows ZIP 超过 300 MiB")
    if args.version_code <= 0:
        raise SystemExit("version-code 必须为正整数")
    if len(args.notes) > 4096:
        raise SystemExit("notes 过长")

    try:
        with zipfile.ZipFile(package) as archive:
            names = set(archive.namelist())
            if len(archive.namelist()) != 3 or names != {"release.json", "NightDesk.exe", "nx-host.exe"}:
                raise SystemExit(f"ZIP 条目不符合预期：{sorted(names)}")
            metadata = json.loads(archive.read("release.json").decode("utf-8"))
            if metadata.get("schemaVersion") != 1 or metadata.get("packageName") != PACKAGE_NAME:
                raise SystemExit("包内 release.json 的 schema/packageName 不正确")
            if metadata.get("version") != args.version or int(metadata.get("versionCode", 0)) != args.version_code:
                raise SystemExit("包内 release.json 的版本与参数不一致")
            files = metadata.get("files")
            if not isinstance(files, list) or len(files) != 2 or {f.get("name") for f in files} != {"NightDesk.exe", "nx-host.exe"}:
                raise SystemExit("包内文件清单必须正好包含两个 EXE")
            for item in files:
                name = item["name"]
                if int(item.get("size", 0)) <= 0 or not valid_hex(item.get("sha256")):
                    raise SystemExit(f"{name} 的大小或 SHA-256 无效")
                if archive.getinfo(name).file_size != int(item["size"]):
                    raise SystemExit(f"{name} 大小与包内清单不一致")
                digest = hashlib.sha256()
                with archive.open(name) as source:
                    for chunk in iter(lambda: source.read(1024 * 1024), b""):
                        digest.update(chunk)
                if digest.hexdigest().lower() != item["sha256"].lower():
                    raise SystemExit(f"{name} 的实际 SHA-256 与包内清单不一致")
    except zipfile.BadZipFile as exc:
        raise SystemExit(f"ZIP 损坏：{exc}")

    digest = hashlib.sha256()
    with package.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    sha256 = digest.hexdigest()
    size = package.stat().st_size

    target_dir = ROOT / "windows"
    target_dir.mkdir(parents=True, exist_ok=True)
    target = target_dir / f"nightdesk-windows-{args.version}.zip"
    if target.exists() and hashlib.sha256(target.read_bytes()).hexdigest() != sha256:
        raise SystemExit("同版本不可变发布文件已存在且内容不同，请提高版本号")
    temporary = target_dir / f".nightdesk-windows-{args.version}.zip.tmp"
    shutil.copy2(package, temporary)
    os.replace(temporary, target)

    manifest = {
        "schemaVersion": 1,
        "packageName": PACKAGE_NAME,
        "version": args.version,
        "versionCode": args.version_code,
        "arch": "win-x64",
        "size": size,
        "sha256": sha256,
        "packagePath": "/nightdesk/windows/latest.zip",
        "notes": args.notes,
    }
    manifest_path = ROOT / "windows-manifest.json"
    if manifest_path.exists():
        backup = ROOT / f"windows-manifest.json.bak-{time.strftime('%Y%m%d-%H%M%S')}"
        shutil.copy2(manifest_path, backup)
    temporary_manifest = ROOT / ".windows-manifest.json.tmp"
    temporary_manifest.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), "utf-8")
    os.replace(temporary_manifest, manifest_path)
    print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
