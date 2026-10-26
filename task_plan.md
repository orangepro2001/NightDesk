# NightDesk task plan

更新时间：2026-10-27（Asia/Seoul）
仓库：`git@github.com:orangepro2001/NightDesk.git`
分支：`main`

## Scope / current assumptions

- Native Windows desktop application: C# / WPF + Win32 API, Chinese UI, tray integration, no Electron.
- Windows x64 self-contained publish; Tailscale Serve is the only remote ingress; no Funnel.
- The app owns only virtual displays it creates. Third-party virtual outputs never satisfy the safety check for removing owned displays.
- Parsec VDD installation is in scope: the pinned official installer is embedded in the single-file EXE and runs only when the driver interface is absent.
- Android is in scope: native Kotlin app, built and tested only on SSH host `AI`.
- go-nx is integrated as an optional low-bandwidth tile remote-control data plane, not as a video-streaming replacement.
- Hardware-changing display tests remain manual. Automated tests must not alter the active display topology.

## Completed phases

1. [done] Install .NET SDK through Scoop; confirm Windows, VDD, Tailscale and existing manager state.
2. [done] Implement native display topology switching/recovery, VDD lifecycle, 100 ms heartbeat, sleep inhibition, hotkey and DPAPI settings.
3. [done] Implement WPF UI/tray, authenticated loopback API, embedded remote page and ownership-checked Tailscale Serve controls.
4. [done] Implement QR pairing, permanent per-device tokens, DPAPI + hashed device registry and trusted-device removal.
5. [done] Fix remote-enable ownership logic so a never-paired host whose Serve route is already structurally correct can adopt it without a stale fingerprint mismatch.
6. [done] Unify Windows EXE/window/tray icon and Android adaptive/round icon.
7. [done] Android: QR scanning, secure per-host token storage, status/control, update checks and system-installer handoff.
8. [done] Move update feed off the desktop process to AI `nightdesk-update-feed.service`, exposed through independent Tailscale Serve paths.
9. [done] Android multi-host management: up to 20 hosts, origin deduplication, active selection, deletion, schemaVersion 3 migration and Keystore AES-GCM persistence.
10. [done] Bundle the official Parsec VDD installer, verify size/SHA-256/signer subject, auto-install once per user when missing, and expose manual retry.
11. [done] Windows 1.3.1 release test/build/publish and read-only diagnostics.
12. [done] Android 1.0.4 (versionCode 5) AI build, instrumented multi-host tests and publication to the live feed.
13. [done] Update README, handoff, acceptance, progress and implementation-plan documents.
14. [done] Analyze go-nx and document the integration boundary: keep the protocol/host core, do not embed the Ebiten desktop executable.
15. [done] Add a monitor-aware `nx-host.exe` sidecar with GDI capture, tile deltas, QUIC + TLS 1.3, pinned identity and paired-device trust.
16. [done] Integrate sidecar lifecycle into WPF: JSON-line IPC, Job Object cleanup, Tailscale IPv4 validation, pairing QR and stale-state cleanup after unexpected exit.
17. [done] Add the Android native viewer: pinned certificate pairing, tile rendering, touch/wheel/keyboard/Unicode input and explicit connection lifecycle.
18. [done] Stage 5 Android lifecycle hardening: 20-second front-end deadline, retry/pair-again/rescan recovery, background input release and stale-callback isolation.
19. [done] Stage 5 verification: Windows 176 tests, Android 26 JVM tests, 4 instrumented tests, lint/build and emulator UI smoke.

## Verification invariants

- Invalid or unauthorized API requests never invoke display operations.
- Display switching captures, validates, applies and verifies a topology; recovery snapshots remain until verification succeeds.
- Exit does not remove NightDesk-owned virtual displays unless an independent active hardware output remains.
- Existing Tailscale Serve routes are never overwritten; disable removes only an unchanged NightDesk-owned route.
- QR tickets are short-lived and single-use; long-lived device tokens are stored hashed under DPAPI and can be revoked individually.
- Android control origin and update origin remain separate; the update client never sends the control token.
- Android host data is encrypted with Android Keystore AES-GCM; no plaintext token is persisted.
- Embedded VDD installer must match the pinned size/hash and Parsec signer subject before execution.
- The NX control plane is pinned to the sidecar certificate; an identity change requires a new QR scan, not a silent retry.
- NX input state is released on disconnection, backgrounding and session teardown.
- No automated test changes active physical/virtual display topology or disables a monitor.
- The sidecar is killed if NightDesk exits; a sidecar exit cannot leave the WPF service reporting a healthy running state.

## Current verification

- Windows Release tests: **176 passed**; output says `176 tests passed. No display state changed.`
- Windows Release build: **0 warnings / 0 errors**.
- Windows artifact: `artifacts\win-x64-1.3.1\NightDesk.exe`, ProductVersion `1.3.1`, size `201766660`, SHA-256 `04AAFCF79470AAE0CE7E2894A2E4177B9A37E220BB750470EA86B98CF394C8F0`.
- Android stage 5 JVM tests: `CoreTest 9 + NxConnectionTest 10 + NxTest 7 = 26 passed`.
- Android stage 5 instrumented tests: `HostStoreInstrumentedTest: OK (3 tests)` + `UpdateFlowInstrumentedTest: OK (1 test)` on `emulator-5580`.
- Android stage 5 lint/build: `0 errors / 22 warnings`; emulator UI smoke screenshots captured.
- Android stage 5 artifact: `artifacts\android\nightdesk-1.1.0-stage5.apk`, size `55058900`, SHA-256 `01734a26365b859c831a9ba7806ff1d133b96e0993061053bb2a60ab72caf480`.
- Live Android feed remains versionCode 5 / 1.0.4; stage 5 has not been published.

## Remaining manual work

- The production desktop process is **PID 13816**, ProductVersion `1.2.0`, `C:\Users\pixel\Desktop\NightDesk.exe`. Do not exit, restart or replace it until a physical display is available and the recovery snapshot can be verified.
- After controlled deployment of 1.3.1, verify local status, Tailscale address, remote page, QR pairing, long-term token persistence, trusted-device revocation, display off/on/restore and hotkey behavior.
- Verify real VDD add/remove with all other VDD managers closed; verify VDD auto-install on a machine where the driver is genuinely absent.
- Real NX end-to-end acceptance requires an independent Windows test machine with recoverable display/input and an Android physical device:
  - GDI first frame and tile deltas;
  - mouse move/click/wheel, Unicode text and key chords;
  - sidecar firewall/tailnet reachability;
  - disconnect, background, forced stop, replacement takeover, wrong code and certificate change;
  - no residual button/key state after disconnect;
  - DPI, lock screen and display sleep behavior.
- The current host cannot safely run a real production-desktop `nx-host.exe` capture/SendInput test. This is the hard blocker; it is not evidence that the integrated code has failed.

## Safety state

- `C:\Users\pixel\AppData\Local\NightDesk\recovery.json` exists.
- Last recorded read-only diagnostic reports the only active source as `\\.\DISPLAY6` (Parsec VDD); no active physical output was confirmed in that diagnostic.
- Do not toggle displays, restore a snapshot, add/remove a VDD, install/uninstall a display driver, or run real capture/SendInput while recovery is unverified.
- `docs/HANDOFF.md`现场安全说明优先于本文件的任何旧状态记录。