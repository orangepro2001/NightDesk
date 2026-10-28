#!/usr/bin/env bash
# Full non-Windows gate for a NightDesk checkout on the AI Linux host.
#
# Covers everything the AI host can prove on its own:
#   Go        unit tests, vet, Windows cross-build of the sidecar
#   Windows   cross-compiled release + update ZIP
#   Android   unit tests, lint, debug and signed release APK
#
# Not covered here, by design:
#   - the 216 Windows assertions  -> GitHub CI or a Windows machine
#   - Android instrumented tests  -> android/scripts/run-instrumented.sh (needs the emulator)
#   - real GDI/VDD/SendInput/display topology and phone testing -> manual acceptance
set -euo pipefail

repo="${NIGHTDESK_REPO_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
cd "$repo"

step() { printf '\n=== %s ===\n' "$1"; }

step "checkout"
git status --short --branch
git log -1 --format='%h %ci %s'
if [[ -n "$(git status --porcelain)" ]]; then
    printf 'WARNING: working tree is not clean; the release artifacts below are not reproducible from a commit.\n' >&2
fi

step "go: unit tests, vet, windows cross-build"
(
    cd native
    go test ./...
    go vet ./...
    GOOS=windows GOARCH=amd64 go build -trimpath -ldflags='-s -w' -o /tmp/nightdesk-nx-host-crosscheck.exe ./cmd/nx-host
    printf 'cross-build ok: %s bytes\n' "$(stat -c%s /tmp/nightdesk-nx-host-crosscheck.exe)"
)
rm -f /tmp/nightdesk-nx-host-crosscheck.exe

step "windows: cross-compile release and package update ZIP"
bash tools/ai/build-windows.sh

step "android: unit tests, lint, debug and release APK"
NIGHTDESK_ANDROID_DIR="$repo/android" bash android/scripts/build-ai.sh

printf '\nAI host checks passed.\n'
printf 'Windows runtime assertions still need GitHub CI (windows job) or a real Windows machine.\n'
