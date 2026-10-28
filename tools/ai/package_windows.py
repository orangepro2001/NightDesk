#!/usr/bin/env python3
"""Package a published win-x64 directory into the Windows update ZIP.

This is the non-PowerShell twin of tools/package-windows.ps1, used on hosts that
have no PowerShell (the AI Linux host). The ZIP layout is identical: three flat
entries release.json, NightDesk.exe and nx-host.exe, where release.json lists the
size and SHA-256 of both executables.

Anything produced here is validated by tools/ai-update-feed/publish_windows.py
before it can reach the update feed, so a drift between the two packagers fails
loudly instead of publishing a broken package.
"""
import argparse
import hashlib
import json
import re
import sys
import zipfile
from pathlib import Path

PACKAGE_NAME = "dev.nightdesk.windows"
REQUIRED = ("NightDesk.exe", "nx-host.exe")


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main() -> int:
    repo_root = Path(__file__).resolve().parents[2]
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", required=True)
    ap.add_argument("--version-code", type=int, required=True)
    ap.add_argument("--source-dir")
    ap.add_argument("--out-zip")
    args = ap.parse_args()

    if not re.fullmatch(r"\d+\.\d+\.\d+", args.version):
        raise SystemExit("version must be x.y.z")
    if args.version_code <= 0:
        raise SystemExit("version-code must be a positive integer")

    source = Path(args.source_dir or repo_root / "artifacts" / f"win-x64-{args.version}").resolve()
    out_zip = Path(
        args.out_zip or repo_root / "artifacts" / "windows-packages" / f"nightdesk-windows-{args.version}.zip"
    ).resolve()

    missing = [name for name in REQUIRED if not (source / name).is_file()]
    if missing:
        raise SystemExit(f"missing in {source}: {', '.join(missing)}")

    files = []
    for name in REQUIRED:
        path = source / name
        if path.stat().st_size <= 0:
            raise SystemExit(f"{path} is empty")
        files.append({"name": name, "size": path.stat().st_size, "sha256": sha256_of(path)})

    metadata = {
        "schemaVersion": 1,
        "packageName": PACKAGE_NAME,
        "version": args.version,
        "versionCode": args.version_code,
        "files": files,
    }

    out_zip.parent.mkdir(parents=True, exist_ok=True)
    if out_zip.exists():
        out_zip.unlink()

    with zipfile.ZipFile(out_zip, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        archive.writestr("release.json", json.dumps(metadata, ensure_ascii=False, indent=2))
        for name in REQUIRED:
            archive.write(source / name, arcname=name)

    print(f"{out_zip} {out_zip.stat().st_size} bytes SHA-256 {sha256_of(out_zip)}")
    for item in files:
        print(f"  {item['name']} {item['size']} bytes SHA-256 {item['sha256']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
