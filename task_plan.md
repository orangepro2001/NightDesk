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
20. [done] Stage 6 packaging fix: publish `nx-host.exe` beside the single-file `NightDesk.exe`, add a fresh-staging publish verifier, rebuild Windows 1.4.0 and re-verify the Android stage 5 candidate on AI.
21. [done] Record the stage 6 packaging and recovery handoff in `docs/20261027-035456-remote-control-stage-6-packaging-handoff.md`.
22. [done] Stage 7: add `nx-host -test-mode` with synthetic source and log injector, standalone `nx-probe`, negative safety guard and a published-artifact end-to-end probe.
23. [done] Re-run Go Linux/Windows checks and the Android stage 5 test suite on AI; record the stage 7 handoff and acceptance facts.
24. [done] Repair the Windows XAML startup crash, add a static regression test, republish the Windows candidate and pass WPF startup smoke verification.
25. [done] Publish Android 1.1.0 (versionCode 7) to the AI feed, back up the old manifest, verify immutable APK/alias/manifest atomically, and re-run the 4 live-feed instrumented tests.
26. [done] Add desktop-to-desktop control to the Windows app: address/code/fingerprint input, pinned C# QUIC viewer window, safe input handling, synthetic host integration test and 1.5.0 recovery documentation.

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

- Windows Release tests: **203 passed**; output says `203 tests passed. No display state changed.`
- Windows Release build: **0 warnings / 0 errors**.
- Windows candidate directory: `artifacts\win-x64-1.5.0`.
- `NightDesk.exe`: ProductVersion `1.5.0+d720ce2eb18a793b348c3be209d9dedc095bc79d`, size `201885444`, SHA-256 `50C71DADC8C1E425026CDC38C64DFEBE971E834B3E2DEE94F695F7E056EBD626`.
- `nx-host.exe`: size `7938048`, SHA-256 `17B420CE531270E28BEF58D42DD68F5C13CAC6C5FE4CBFD4FDDBB20051AD35E3`, `-version` returns `nx-host 0.2.0`.
- `tools\publish-windows.ps1` uses a fresh staging directory, requires both EXEs and runs a sidecar smoke check.
- Go Linux/Windows: `go vet ./...`, `go test ./...` and Windows cross-build pass; 8 cross-compiled test binaries pass on Windows.
- Safe probe: `1920x1080`, 4 frames, 538 tile updates, 7/7 input receipts, differing frame digests and host exit 0; no GDI capture and no SendInput.
- Android stage 5 JVM tests: `CoreTest 9 + NxConnectionTest 10 + NxTest 7 = 26 passed`.
- Android stage 5 instrumented tests: `HostStoreInstrumentedTest: OK (3 tests)` + `UpdateFlowInstrumentedTest: OK (1 test)` on `emulator-5580`.
- Android stage 5 lint/build: `0 errors / 22 warnings`; emulator UI smoke screenshots captured.
- Android published release: 1.1.0 / versionCode 7, local artifact `artifacts\android\nightdesk-1.1.0-stage5.apk`, size `55058900`, SHA-256 `01734a26365b859c831a9ba7806ff1d133b96e0993061053bb2a60ab72caf480`; AI immutable file `nightdesk-7.apk`.
- Live Android feed is versionCode 7 / 1.1.0. The manifest, APK size/SHA-256, certificate, HTTPS download and emulator update flow were verified after publication.
- Desktop control integration test: real `nx-host.exe -test-mode -source pattern -inject log` verified wrong-code rejection, 1920x1080 frame decode, ACK/next frame, input receipt and pin persistence. Actual `NxViewerWindow` rendering was smoke-tested against the same synthetic source.

## Remaining manual work

- Windows 1.5.0 desktop-control details are in `docs/20261027-064503-windows-desktop-control-1.5.0-handoff.md`; synthetic verification does not replace two-machine acceptance.
- Android 1.1.0 publication/recovery details are in `docs/20261027-062721-android-1.1.0-publication-handoff.md`; publication does not replace real-device acceptance.
- At the 2026-10-27 06:47 KST read-only check, Desktop `NightDesk.exe` PID 28504 was running and `nx-host.exe` PID 4444 was listening on `100.109.77.29:7000` with the production state directory. Do not kill, replace, or start a second production host; 1.5.0 deployment requires a normal UI exit after display recovery is verified. `recovery.json` exists again (351 bytes).
- After controlled deployment of 1.5.0, verify that `NightDesk.exe` and `nx-host.exe` are in the same directory, then check local status, Tailscale address, remote page, QR pairing, long-term token persistence, trusted-device revocation, display off/on/restore and hotkey behavior.
- Verify real VDD add/remove with all other VDD managers closed; verify VDD auto-install on a machine where the driver is genuinely absent.
- Real NX end-to-end acceptance still requires independent Windows test machines with recoverable display/input and an Android physical device; the synthetic probe and desktop viewer smoke do not validate GDI capture, SendInput, DPI mapping, secure desktop or real tailnet UDP. Recovery steps: `docs/20261027-041844-remote-control-stage-7-safe-e2e-probe-handoff.md`.
- Required real-device checks:
  - GDI first frame and tile deltas;
  - mouse move/click/wheel, Unicode text and key chords;
  - sidecar firewall/tailnet reachability;
  - disconnect, background, forced stop, replacement takeover, wrong code and certificate change;
  - no residual button/key state after disconnect;
  - DPI, lock screen and display sleep behavior.
- The current host cannot safely run a real production-desktop `nx-host.exe` capture/SendInput test. This is the hard blocker; it is not evidence that the integrated code has failed.

## Safety state

- `C:\Users\pixel\AppData\Local\NightDesk\recovery.json` existed at 06:34 KST (351 bytes). Its presence does not prove the snapshot or physical output is safe; do not restore, toggle displays or run real capture/SendInput until physical output and VDD state are independently verified.
- Last recorded read-only diagnostic reports the only active source as `\\.\DISPLAY6` (Parsec VDD); no active physical output was confirmed in that diagnostic.
- Do not toggle displays, restore a snapshot, add/remove a VDD, install/uninstall a display driver, or run real capture/SendInput while recovery is unverified.
- `docs/HANDOFF.md`现场安全说明优先于本文件的任何旧状态记录。

## Current feature plan: desktop-to-desktop control

Goal: let a Windows NightDesk instance control another Windows NightDesk go-nx host without relying on Android or a QR scanner.

1. [done] Add validated target address, one-time pairing code and optional host-fingerprint fields to the WPF UI; persist address/fingerprint but never the pairing code.
2. [done] Implement a pinned C# QUIC client with a persistent viewer identity, TLS 1.3, go-nx ALPN, tile decoding, ACK/receipt handling and classified connection errors.
3. [done] Add a dedicated viewer window with aspect-correct rendering, keyboard/mouse/wheel forwarding and release-all on focus loss/close.
4. [done] Verify address/fingerprint/protocol parsing with unit tests and the real `nx-host.exe -test-mode -source pattern -inject log` binary; do not use GDI or SendInput.
5. [done] Rebuild/publish the Windows candidate, record tests, hashes and manual acceptance limits in timestamped documentation.

Success criteria: entering a reachable host address and the current pairing code opens a live control window; a wrong code or wrong fingerprint fails closed; closing the viewer releases all input; synthetic frames and input receipts pass without touching the display topology.