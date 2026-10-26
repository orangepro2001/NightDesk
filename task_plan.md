# NightDesk task plan

更新时间：2026-10-26（Asia/Seoul）

## Scope / current assumptions

- Native Windows desktop application: C# / WPF + Win32 API, Chinese UI, tray integration, no Electron.
- Windows x64 self-contained publish; Tailscale Serve is the only remote ingress; no Funnel.
- The app owns only virtual displays it creates. Third-party virtual outputs never satisfy the safety check for removing owned displays.
- Parsec VDD installation is now in scope: the pinned official installer is embedded in the single-file EXE and runs only when the driver interface is absent.
- Android is in scope: native Kotlin app, built and tested only on SSH host `AI`.
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

## Verification invariants

- Invalid or unauthorized API requests never invoke display operations.
- Display switching captures, validates, applies and verifies a topology; recovery snapshots remain until verification succeeds.
- Exit does not remove NightDesk-owned virtual displays unless an independent active hardware output remains.
- Existing Tailscale Serve routes are never overwritten; disable removes only an unchanged NightDesk-owned route.
- QR tickets are short-lived and single-use; long-lived device tokens are stored hashed under DPAPI and can be revoked individually.
- Android control origin and update origin remain separate; the update client never sends the control token.
- Android host data is encrypted with Android Keystore AES-GCM; no plaintext token is persisted.
- Embedded VDD installer must match the pinned size/hash and Parsec signer subject before execution.
- No automated test changes active physical/virtual display topology or disables a monitor.

## Current verification

- Windows Release tests: **115 passed**; output says `No display state changed.`
- Windows Release build: **0 warnings / 0 errors**.
- Windows artifact: `artifacts\win-x64-1.3.1\NightDesk.exe`, ProductVersion `1.3.1`, SHA-256 `04AAFCF79470AAE0CE7E2894A2E4177B9A37E220BB750470EA86B98CF394C8F0`.
- Android instrumented tests: `HostStoreInstrumentedTest: OK (3 tests)`, `0.778s`, `emulator-5580`.
- Live Android feed: versionCode 5 / 1.0.4; download page and APK return HTTP 200; manifest hash matches the local APK.
- AI service: `nightdesk-update-feed.service` active/enabled; loopback health returns `ok`.

## Remaining manual work

- Do not replace, exit or restart PID 22708 until a physical display is available and `recovery.json` can be verified.
- After controlled deployment of 1.3.1, verify:
  - local status, Tailscale address and remote page;
  - QR pairing, long-term token persistence and trusted-device revocation end to end;
  - physical display off/on/restore and hotkey behavior;
  - real VDD add/remove with all other VDD managers closed;
  - a second tailnet device and applicable ACLs.
- Verify VDD auto-install on a machine/user profile where the driver is genuinely absent. This host already has version 0.45 installed, so only detection, embedded payload integrity, extraction and rejection paths were tested here.
- Physical Android device acceptance remains outstanding; the emulator covers multi-host persistence, update chain and build/runtime basics.
