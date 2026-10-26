#!/usr/bin/env python3
"""Atomically publish a signed NightDesk Android APK to the AI feed."""
import argparse
import hashlib
import json
import os
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apk", required=True)
    ap.add_argument("--version-code", type=int, required=True)
    ap.add_argument("--version-name", required=True)
    ap.add_argument("--certificate-sha256", required=True)
    ap.add_argument("--min-sdk", type=int, default=29)
    ap.add_argument("--notes", default="")
    ap.add_argument("--package-name", default="dev.nightdesk.android")
    a = ap.parse_args()

    apk = Path(a.apk).resolve()
    if not apk.is_file() or apk.stat().st_size <= 0:
        raise SystemExit(f"APK 不存在或为空：{apk}")
    if apk.stat().st_size > 100 * 1024 * 1024:
        raise SystemExit("APK 超过 100 MiB")
    cert = a.certificate_sha256.lower()
    if len(cert) != 64 or any(c not in "0123456789abcdef" for c in cert):
        raise SystemExit("certificate-sha256 必须是 64 位十六进制")

    digest = hashlib.sha256(apk.read_bytes()).hexdigest()
    size = apk.stat().st_size
    target = ROOT / f"nightdesk-{a.version_code}.apk"
    temp = ROOT / f".nightdesk-{a.version_code}.apk.tmp"
    shutil.copy2(apk, temp)
    os.replace(temp, target)

    link = ROOT / f"nightdesk-{a.version_name}.apk"
    try:
        if link.is_symlink() or link.exists():
            link.unlink()
        link.symlink_to(target.name)
    except OSError:
        pass

    release = {
        "schemaVersion": 1,
        "packageName": a.package_name,
        "versionCode": a.version_code,
        "versionName": a.version_name,
        "minSdk": a.min_sdk,
        "size": size,
        "sha256": digest,
        "certificateSha256": cert,
        "apkPath": "/nightdesk/latest.apk",
        "notes": a.notes,
    }
    manifest = ROOT / "manifest.json"
    tmp_manifest = ROOT / ".manifest.json.tmp"
    tmp_manifest.write_text(json.dumps(release, ensure_ascii=False, indent=2), "utf-8")
    os.replace(tmp_manifest, manifest)
    print(json.dumps(release, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()