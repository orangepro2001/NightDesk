# NightDesk implementation plan

更新时间：2026-10-27（Asia/Seoul）
当前代码基线：`fff73bb2b823f52f86f8807a9f0a59c6e3283ebd`
最新交接：`docs/20261027-041844-remote-control-stage-7-safe-e2e-probe-handoff.md`

## Scope / assumptions

- Native Windows desktop application: C# WPF/.NET 10 + Win32 API.
- Physical outputs are detached through a validated display-topology switch to a NightDesk-owned Parsec VDD; no global monitor power broadcast.
- Recovery snapshot remains on disk until restoration is independently verified.
- Official Parsec VDD installer is bundled and may be auto-installed when missing. Automatic Internet download is not used.
- Remote control uses loopback HTTP behind Tailscale Serve HTTPS, bearer authentication and fixed commands; no Funnel.
- go-nx is integrated as a separate Go sidecar plus a native Android viewer. It is a low-bandwidth tile data plane, not a video-streaming replacement.
- Android builds and debugging run only on SSH host `AI`.
- Automated tests must not mutate display topology.

## Phases

1. [done] Inspect host, VDD protocol, SDK and Tailscale; install .NET through Scoop.
2. [done] Define models/validation and pure tests for config, hotkey, display mode and command boundaries.
3. [done] Implement VDD adapter, topology/recovery, sleep inhibition, hotkey and persistence.
4. [done] Implement WPF UI/tray and authenticated loopback API with ownership-checked Tailscale Serve integration.
5. [done] Add QR pairing, permanent device tokens, trusted-device registry and revocation.
6. [done] Unify Windows and Android icons; fix remote-enable ownership matching.
7. [done] Implement Kotlin client with QR scan, multi-host storage, secure control and automatic update.
8. [done] Move update feed to AI systemd service with atomic publication and separate Tailscale routes.
9. [done] Bundle and validate the official Parsec VDD installer; add auto-install/manual-retry UI and tests.
10. [done] Run Windows Release tests/build/publish and Android 1.0.4 instrumented tests/publication.
11. [done] Add go-nx sidecar integration: monitor-aware GDI capture, 64x64 tile deltas, QUIC + TLS 1.3, pinned identity, paired-device trust and JSON-line IPC.
12. [done] Add the Android native NX viewer and stage 5 connection lifecycle: explicit phases, 20-second deadline, classified recovery actions, background input release and stale-callback isolation.
13. [done] Stage 6 packaging fix: copy `nx-host.exe` to the publish directory, exclude it from the single-file bundle, add `tools/publish-windows.ps1`, and rebuild Windows 1.4.0.
14. [done] Re-run the Android stage 5 build and tests on AI; confirm the local candidate hashes match the AI outputs.
15. [done] Update current handoff, acceptance, progress and planning documents with the 1.4.0 candidate and the production safety gate.
16. [done] Stage 7 safe end-to-end probe: `nx-host -test-mode` with a synthetic pattern source and log-only injector, standalone `nx-probe`, negative misuse guard, moved-input receipts and published-artifact acceptance.

## Verification

- Windows Release tests: **176 passed**, output `176 tests passed. No display state changed.`
- Windows Release build: **0 warnings / 0 errors**.
- Windows publish: `tools\publish-windows.ps1` publishes into fresh staging, requires both `NightDesk.exe` and `nx-host.exe`, and checks `nx-host.exe -version`.
- go-nx Linux/Windows: `gofmt` on touched files, `go vet`, `go test` and Windows cross-build pass; 8 cross-compiled test binaries pass on Windows.
- go-nx safe probe: published `nx-host.exe` + `nx-probe.exe` produced 1920x1080, 4 frames, 538 tile updates, 7/7 input receipts, differing frame digests and host exit code 0 without capture or SendInput.
- Android stage 5: 26 JVM tests, 4 instrumented tests, lint/build and emulator UI smoke.
- Pure logic + authenticated HTTP integration tests; malformed or unauthorized requests never reach display actions.
- Embedded installer presence, extraction, hash/signature validation and tamper rejection tested.
- Real display switching, absent-driver VDD installation, real GDI capture, real SendInput, physical Android and second-tailnet-node acceptance remain manual.

## Current artifact facts

- Windows candidate: `artifacts\win-x64-1.4.0`.
  - `NightDesk.exe`: `201832196` bytes, SHA-256 `F4C448970703BB489E3A762596A59AEA06E688ED39CC505128B01CF4B7864EF2`, ProductVersion `1.4.0+fff73bb2b823f52f86f8807a9f0a59c6e3283ebd`.
  - `nx-host.exe`: `7938048` bytes, SHA-256 `17B420CE531270E28BEF58D42DD68F5C13CAC6C5FE4CBFD4FDDBB20051AD35E3`, version `nx-host 0.2.0`.
- Android stage 5 candidate: `artifacts\android\nightdesk-1.1.0-stage5.apk`, `55058900` bytes, SHA-256 `01734a26365b859c831a9ba7806ff1d133b96e0993061053bb2a60ab72caf480`; not published.
- Live Android feed remains versionCode 5 / 1.0.4.

## Tracked issues / safety gate

- Production process is PID **13816**, ProductVersion **1.3.1**, `C:\Users\pixel\Desktop\NightDesk.exe`; its directory has no `nx-host.exe`.
- `C:\Users\pixel\AppData\Local\NightDesk\recovery.json` still exists; the last read-only diagnostic showed only `\\.\DISPLAY6` (Parsec VDD).
- Do not exit, restart or replace PID 13816; do not toggle displays, restore a snapshot, add/remove a VDD, install/uninstall a display driver, or run real capture/SendInput on the production desktop until recovery is independently verified.
- `docs/HANDOFF.md` safety instructions override older plan records.