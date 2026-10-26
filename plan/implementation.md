# NightDesk implementation plan

更新时间：2026-10-27（Asia/Seoul）
当前代码基线：`d720ce2eb18a793b348c3be209d9dedc095bc79d`
最新交接：`docs/20261027-064503-windows-desktop-control-1.5.0-handoff.md`

## Scope / assumptions

- Native Windows desktop application: C# WPF/.NET 10 + Win32 API.
- Physical outputs are detached through a validated display-topology switch to a NightDesk-owned Parsec VDD; no global monitor power broadcast.
- Recovery snapshot remains on disk until restoration is independently verified.
- Official Parsec VDD installer is bundled and may be auto-installed when missing. Automatic Internet download is not used.
- Remote control uses loopback HTTP behind Tailscale Serve HTTPS, bearer authentication and fixed commands; no Funnel.
- go-nx is integrated as a separate Go host sidecar plus native Android and Windows WPF viewers. It is a low-bandwidth tile data plane, not a video-streaming replacement.
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
17. [done] Publish Android 1.1.0 / versionCode 7 with backup, immutable APK, version alias and final atomic manifest replacement; verify HTTPS downloads and the emulator update flow.
18. [done] Add Windows-to-Windows control: validated address/code/fingerprint input, persistent Viewer identity and host pins, native WPF QUIC viewer, tile rendering, input release, real test-mode integration and 1.5.0 publication.

## Verification

- Windows Release tests: **203 passed**, output `203 tests passed. No display state changed.`; the new integration test uses only `nx-host -test-mode -source pattern -inject log`.
- Windows Release build: **0 warnings / 0 errors**.
- Windows publish: `tools\publish-windows.ps1` publishes into fresh staging, requires both `NightDesk.exe` and `nx-host.exe`, and checks `nx-host.exe -version`.
- go-nx Linux/Windows: `gofmt` on touched files, `go vet`, `go test` and Windows cross-build pass; 8 cross-compiled test binaries pass on Windows.
- go-nx safe probe: published `nx-host.exe` + `nx-probe.exe` produced 1920x1080, 4 frames, 538 tile updates, 7/7 input receipts, differing frame digests and host exit code 0 without capture or SendInput.
- Android 1.1.0: 26 JVM tests, 4 instrumented tests, lint/build, emulator UI smoke, and a post-publication live-feed run of `OK (4 tests)`.
- Pure logic + authenticated HTTP integration tests; malformed or unauthorized requests never reach display actions.
- Embedded installer presence, extraction, hash/signature validation and tamper rejection tested.
- Publication checks prove download integrity and update-flow behavior only. Real display switching, absent-driver VDD installation, real GDI capture, real SendInput, physical Android and second-tailnet-node acceptance remain manual.

## Current artifact facts

- Windows candidate: `artifacts\win-x64-1.5.0`.
  - `NightDesk.exe`: `201885444` bytes, SHA-256 `50C71DADC8C1E425026CDC38C64DFEBE971E834B3E2DEE94F695F7E056EBD626`, ProductVersion `1.5.0+d720ce2eb18a793b348c3be209d9dedc095bc79d`.
  - `nx-host.exe`: `7938048` bytes, SHA-256 `17B420CE531270E28BEF58D42DD68F5C13CAC6C5FE4CBFD4FDDBB20051AD35E3`, version `nx-host 0.2.0`.
- Android published release: 1.1.0 / versionCode 7, local artifact `artifacts\android\nightdesk-1.1.0-stage5.apk`, `55058900` bytes, SHA-256 `01734a26365b859c831a9ba7806ff1d133b96e0993061053bb2a60ab72caf480`; AI file `nightdesk-7.apk`.
- Live Android feed is versionCode 7 / 1.1.0; old manifest backup is `manifest.json.bak-20261027-062447`.
- Windows 1.5.0 publication does not validate real GDI capture, SendInput, DPI/UAC, two-machine tailnet UDP/QUIC or long-run resource behavior. The WPF viewer requires Windows 11 / Server 2022 or newer because it uses System.Net.Quic; Android is unaffected.

## Tracked issues / safety gate

- At 2026-10-27 06:47 KST, Desktop `NightDesk.exe` PID 28504 and production `nx-host.exe` PID 4444 were running. Do not kill them, overwrite files, or start a second production host; deploy 1.5.0 only after a normal UI exit and verified display recovery. `recovery.json` exists again (351 bytes).
- `C:\Users\pixel\AppData\Local\NightDesk\recovery.json` exists again (351 bytes, 06:34 KST); the last read-only diagnostic still showed only `\\.\DISPLAY6` (Parsec VDD). Presence is not proof that display topology or the recovery snapshot is safe.
- Do not toggle displays, restore a snapshot, add/remove a VDD, install/uninstall a display driver, or run real capture/SendInput on the production desktop until recovery is independently verified.
- `docs/HANDOFF.md` safety instructions override older plan records.