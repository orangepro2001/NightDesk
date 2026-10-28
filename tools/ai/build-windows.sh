#!/usr/bin/env bash
# Cross-build the Windows release on a Linux host.
#
# .NET supports building (not running) Windows desktop targets off-Windows, so the
# AI build machine produces exactly the two files the updater ships:
# NightDesk.exe (self-contained, single file) and nx-host.exe (left unbundled).
#
# What this cannot do: run the result. nx-host.exe -version and the 216 Windows
# assertions need a real Windows runtime, which is what .github/workflows/verify.yml
# and the manual acceptance step are for.
set -euo pipefail

repo="${NIGHTDESK_REPO_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
cd "$repo"

dotnet="${DOTNET:-}"
if [[ -z "$dotnet" ]]; then
    if [[ -x "$HOME/.dotnet/dotnet" ]]; then dotnet="$HOME/.dotnet/dotnet"; else dotnet="$(command -v dotnet)"; fi
fi

version="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' src/NightDesk/NightDesk.csproj | head -1)"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    printf 'cannot read a x.y.z <Version> from src/NightDesk/NightDesk.csproj\n' >&2
    exit 1
fi
IFS=. read -r major minor patch <<<"$version"
version_code=$(( major * 10000 + minor * 100 + patch ))

out="artifacts/win-x64-$version"
rm -rf "$out"
printf 'publishing %s (versionCode %s) to %s\n' "$version" "$version_code" "$out"
"$dotnet" publish src/NightDesk/NightDesk.csproj -c Release -r win-x64 --self-contained true \
    -p:EnableWindowsTargeting=true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$out"

for name in NightDesk.exe nx-host.exe; do
    if [[ ! -f "$out/$name" ]]; then
        printf 'missing publish output: %s/%s\n' "$out" "$name" >&2
        exit 1
    fi
    printf '%s %s bytes SHA-256 %s\n' "$name" "$(stat -c%s "$out/$name")" "$(sha256sum "$out/$name" | cut -d' ' -f1)"
done

python3 tools/ai/package_windows.py --version "$version" --version-code "$version_code" --source-dir "$out"
