# NightDesk implementation plan

更新时间：2026-10-26（Asia/Seoul）

## Scope / assumptions

- Native Windows desktop application: C# WPF/.NET 10 + Win32 API.
- Physical outputs are detached through a validated display-topology switch to a NightDesk-owned Parsec VDD; no global monitor power broadcast.
- Recovery snapshot remains on disk until restoration is independently verified.
- Official Parsec VDD installer is bundled and may be auto-installed when missing. Automatic Internet download is not used.
- Remote control uses loopback HTTP behind Tailscale Serve HTTPS, bearer authentication and fixed commands; no Funnel.
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
10. [done] Run Windows 1.3.1 Release tests/build/publish and Android 1.0.4 instrumented tests/publication.
11. [done] Update README, handoff, acceptance and planning documents.

## Verification

- Pure logic + authenticated HTTP integration tests; malformed or unauthorized requests never reach display actions.
- Release build with 0 warnings / 0 errors; self-contained win-x64 publish.
- Read-only diagnostics only.
- Embedded installer presence, extraction, hash/signature validation and tamper rejection tested.
- Android multi-host persistence, migration and plaintext-token checks tested on emulator.
- Live Tailnet update page/manifest/APK checked for status, size and hash.

## Tracked issues

- `C:\dev` is not a Git repository; NightDesk lives under `C:\dev\NightDesk`.
- Running desktop PID 22708 is 1.2.0 and must not be restarted until the physical display and recovery snapshot are safe.
- Real display switching, absent-driver VDD installation, physical Android and second-tailnet-node acceptance remain manual.
