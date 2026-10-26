# Findings

更新时间：2026-10-27（Asia/Seoul）

## Git / repository

- Workspace: `C:\dev\NightDesk`.
- Remote: `git@github.com:orangepro2001/NightDesk.git`.
- Branch: `main`.
- Initial import: `b1855e4 chore: initial NightDesk source import`.
- Current feature baseline: `d720ce2eb18a793b348c3be209d9dedc095bc79d feat(desktop): add Windows-to-Windows control viewer`; documentation is added after this baseline.
- This is a Git repository; the earlier note saying otherwise was stale.
- `artifacts/` is intentionally ignored and is not committed; binaries are local delivery evidence, not source.

## Host / display safety

- Host has Parsec VDD 0.45 installed, AMD graphics and GameViewer VDD.
- Last recorded read-only diagnostic (`diagnostics.json`) shows only `\\.\DISPLAY6` (Parsec Virtual Display Adapter) as an active source; no active physical output was confirmed there.
- At 2026-10-27 06:34 KST, `C:\Users\pixel\AppData\Local\NightDesk\recovery.json` existed again (351 bytes). The snapshot has not been independently verified; do not restore, toggle displays, or rebuild VDD.
- At 2026-10-27 06:47 KST, `C:\Users\pixel\Desktop\NightDesk.exe` PID 28504 was running and `nx-host.exe` PID 4444 was listening on `100.109.77.29:7000` with `primary`, 10 FPS and the production state directory. Do not kill, replace, or start a second production host; real GDI/SendInput acceptance remains prohibited until the physical display and snapshot are verified.
- Startup failure root cause: a literal `` `r`n `` text node in `src/NightDesk/MainWindow.xaml` made WPF add non-UI content to `WrapPanel.Children`, throwing from `UIElementCollection`. Fixed in commit `090b5c1`; see `docs/20261027-061608-windows-xaml-startup-fix-handoff.md`.
- Do not exit/restart/replace that process, restore a snapshot, toggle displays, add/remove VDDs, install/uninstall display drivers, or run real `nx-host.exe` capture/SendInput on the production desktop until a physical display is available and the snapshot can be verified.
- A later read-only WMI query listed an AMD adapter, but WMI resolution/status is not proof that the physical output topology is safe. Treat the recovery snapshot as authoritative until independently verified.
- Tailscale node: `minipc-xoh4t.tail9f7f1f.ts.net`; desktop remote UI is `https://minipc-xoh4t.tail9f7f1f.ts.net/`. Serve is tailnet-only; Funnel is not enabled.

## Parsec VDD packaging

- Official installer used: `https://builds.parsec.app/vdd/parsec-vdd-0.45.0.0.exe`.
- Size: **517256** bytes.
- SHA-256: `e23332448fdaf5aa017cb308db5ef6855fac526a7ded05d80c039404126d5362`.
- Signer subject: `CN="Parsec Cloud, Inc."`.
- The installer is embedded in the 1.4.0 single-file EXE as `NightDesk.Assets.vdd.parsec-vdd-0.45.0.0.exe`.
- Startup probes `VddClient`; if the driver interface is unavailable, it extracts the embedded installer to `%LOCALAPPDATA%\NightDesk\driver\` and starts it with `runas` + `/S`.
- The file is rejected unless size, SHA-256 and signer subject match. Tampered or missing payloads never execute.
- Automatic installation is attempted only once per user; the UI's manual install button remains available for retry.
- This host already has the driver, so no uninstall/reinstall was performed. Verified paths: detection, embedded resource presence, size, hash, signer check, extraction, tamper rejection and missing-file rejection.
- A true absent-driver auto-install still requires a clean Windows machine or VM for manual acceptance.

## go-nx integration

- go-nx's Ebiten desktop executable is not embedded in WPF and is not reused on Android.
- Windows uses a separate `nx-host.exe` sidecar, launched and supervised by `NxHostService`.
- Sidecar protocol: JSON Lines over stdin/stdout; process is assigned to a Win32 Job Object with kill-on-job-close.
- Capture: GDI, selected monitor, 64×64 tile differential encoding.
- Transport: QUIC + TLS 1.3, ALPN, pinned host identity and paired client trust.
- Pairing: short-lived 6-digit code plus QR payload carrying address, certificate fingerprint and frame dimensions.
- Input: SendInput for mouse movement/buttons/wheel, virtual keys and Unicode text; input state is released on disconnect and teardown.
- Important limit: this is a low-bandwidth lossless tile viewer for static or low-dynamic desktops. It does not provide audio, video codecs, clipboard, file transfer, multi-monitor switching, dynamic bitrate or game/video quality.
- Tailscale Serve cannot proxy the QUIC/UDP data plane; Android must reach the Windows sidecar directly over the tailnet/UDP path.
- Windows tests include `NxHostOptions`, JSON protocol, QR validation, sidecar lifecycle, stale-state cleanup and a fake sidecar; real GDI/SendInput remains unverified.
- Packaging regression: `CopyToOutputDirectory` alone did not copy `nx-host.exe` into the single-file publish output. The 1.4.0 csproj adds `CopyToPublishDirectory=PreserveNewest` and `ExcludeFromSingleFile=true`.
- `tools/publish-windows.ps1` publishes into a fresh staging directory and fails unless both `NightDesk.exe` and `nx-host.exe` exist; it also runs `nx-host.exe -version`. Never ship only the main EXE.

## Android client

- App: `dev.nightdesk.android`, minSdk 29 / target 35, Kotlin Views.
- Persistence schema: **schemaVersion 3** for host lists; schemaVersion 2 single-profile data is migrated automatically.
- Up to **20** hosts. Host identity is the normalized Tailscale control origin; re-adding the same origin updates its token instead of creating a duplicate.
- UI: spinner for saved hosts, automatic switch/refresh, and “删除当前主机”.
- Storage: Android Keystore AES-GCM over the complete JSON document; SharedPreferences contains only base64 ciphertext.
- Stage 5 adds `NxConnectionModel`: explicit Connecting/Streaming/Failed/Disconnected/Background phases, 20-second front-end deadline, failure classification and recovery actions.
- Recovery rules:
  - `bad-code`, `untrusted` -> re-enter pairing code;
  - `host-identity` -> rescan QR, never silently retry;
  - `identity` -> no automatic fix;
  - other link errors -> explicit retry.
- The model never reconnects automatically. Old attempt callbacks are ignored; `session` and `generation` are `@Volatile`.
- `onStop` marks the attempt Background, invalidates late callbacks, releases input and clears the screen; returning to the app requires an explicit retry.
- Stage 5 verification: 26 JVM tests, 4 instrumented tests, lint `0 errors / 22 warnings`, emulator screenshots.
- Real Android physical-device/tailnet/GDI/SendInput acceptance is still outstanding.

## AI Android toolchain / feed

- Android SDK/JDK/Gradle/adb/emulator operations run only on SSH host `AI`; Windows does not install the Android toolchain.
- SSH defaults into tmux. The pre-existing `main` session belongs to other work and must not be touched. Owned sessions: `nightdesk-build`, `nightdesk-emulator`.
- Noninteractive remote shell may lack `adb` on PATH; use `/home/tony/Android/Sdk/platform-tools/adb`.
- AVD uses software rendering because the host has no `/dev/kvm`.
- Stage 5 verification directory: `/home/tony/dev/nightdesk-android-stage5`.
- Build command: `NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-stage5 bash scripts/build-ai.sh -q -PnightdeskVersionCode=7 -PnightdeskVersionName=1.1.0`.
- Update feed: `nightdesk-update-feed.service`, loopback `127.0.0.1:17768`, enabled at boot.
- Tailnet paths: `/nightdesk` download page, `/nightdesk/latest.apk`, `/api/v1/android-update` manifest, `/api/v1/android-apk` versioned download.
- Atomic publish order: APK copy -> version alias -> manifest last. The service verifies size and SHA-256 and returns 503 rather than exposing a half-published release.
- Live current release: versionCode **7**, versionName **1.1.0**, size **55058900**, SHA-256 `01734a26365b859c831a9ba7806ff1d133b96e0993061053bb2a60ab72caf480`; immutable file `nightdesk-7.apk`, alias `nightdesk-1.1.0.apk`.
- The previous release, 1.0.4 / versionCode 5, remains available for rollback reference. Android clients do not accept downgrades. The pre-publication manifest backup is `manifest.json.bak-20261027-062447`.
- Signing cert SHA-256: `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`.
- Signing material is outside the source tree with 0600 permissions; passwords/tokens are not written to docs, logs or screenshots.
- Client updates are discovered on app start, by a WorkManager periodic check (about every 6 hours), or by manual check. This is not an FCM push; installation still requires the Android system confirmation UI.
- Funnel is not enabled. Tailnet ACL is the access boundary.

## Other current facts

- Windows 1.5.0 candidate directory: `artifacts\win-x64-1.5.0`.
- `NightDesk.exe`: size `201885444`, SHA-256 `50C71DADC8C1E425026CDC38C64DFEBE971E834B3E2DEE94F695F7E056EBD626`, ProductVersion `1.5.0+d720ce2eb18a793b348c3be209d9dedc095bc79d`.
- `nx-host.exe`: size `7938048`, SHA-256 `17B420CE531270E28BEF58D42DD68F5C13CAC6C5FE4CBFD4FDDBB20051AD35E3`, version `nx-host 0.2.0`.
- Safe end-to-end probe: synthetic 1920x1080 source, 4 frames, 538 tile updates, 7/7 input receipts, differing frame digests, host exit 0; no GDI capture and no SendInput.
- Windows desktop-to-desktop Viewer uses `System.Net.Quic`, TLS 1.3 and ALPN `go-nx`; the real test-mode host verified wrong-code rejection, first frame, ACK next frame, input receipt and host pin persistence.
- Actual `NxViewerWindow` rendered the synthetic frame and status in a WPF smoke; screenshot hash `330922E18899891316F9F050425E860D6E4CC750BDD62BB0B4B1F285F82278D2`.
- The native WPF QUIC viewer is currently limited to Windows 11 / Server 2022 or newer; Android remains independent.
- Android 1.1.0 publication verification: package dev.nightdesk.android, versionCode 7, minSdk 29, v2 signature, ZIP integrity, local/AI/HTTPS SHA-256 equality, HTTP 200 for latest and versioned APK, and emulator live-feed tests OK (4 tests).
- Publication documentation: docs/20261027-062721-android-1.1.0-publication-handoff.md.
- Desktop-control documentation: docs/20261027-064503-windows-desktop-control-1.5.0-handoff.md.
- Real GDI capture, SendInput/DPI/UAC behavior, two-machine tailnet UDP/QUIC and physical Android tailnet acceptance remain unverified.
- Release tests: **203 passed**, no display mutation.
- Release build: **0 warnings / 0 errors**.
- `C:\dev\NightDesk` is a Git repository with remote `origin`; do not treat local artifact hashes as proof of end-to-end remote-control acceptance.
