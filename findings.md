# Findings

更新时间：2026-10-27（Asia/Seoul）

## Git / repository

- Workspace: `C:\dev\NightDesk`.
- Remote: `git@github.com:orangepro2001/NightDesk.git`.
- Branch: `main`.
- Initial import: `b1855e4 chore: initial NightDesk source import`.
- Current code baseline: `40355ae239d98961951ef2ca7de10f5ff1fb253a fix(desktop): ship nx-host with the published app`.
- This is a Git repository; the earlier note saying otherwise was stale.
- `artifacts/` is intentionally ignored and is not committed; binaries are local delivery evidence, not source.

## Host / display safety

- Host has Parsec VDD 0.45 installed, AMD graphics and GameViewer VDD.
- Last recorded read-only diagnostic (`diagnostics.json`) shows only `\\.\DISPLAY6` (Parsec Virtual Display Adapter) as an active source; no active physical output was confirmed there.
- `C:\Users\pixel\AppData\Local\NightDesk\recovery.json` exists (352 bytes, last write `2026/10/27 00:10:30`).
- The running desktop process is PID **13816**, product version **1.3.1**, path `C:\Users\pixel\Desktop\NightDesk.exe`, started `2026/10/27 00:07:24`. The production directory has no `nx-host.exe` and no `nx-host` process is running.
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
- Live current release: versionCode **5**, versionName **1.0.4**, size **6991035**, SHA-256 `906142e6abda3d193272b7f68adcb053d9cbbdd00fca40423b806e1d2979e436`.
- Stage 5 candidate: versionCode **7**, versionName **1.1.0**, size **55058900**, SHA-256 `01734a26365b859c831a9ba7806ff1d133b96e0993061053bb2a60ab72caf480`; not published.
- Signing cert SHA-256: `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`.
- Signing material is outside the source tree with 0600 permissions; passwords/tokens are not written to docs, logs or screenshots.
- Funnel is not enabled. Tailnet ACL is the access boundary.

## Other current facts

- Windows 1.4.0 candidate directory: `artifacts\win-x64-1.4.0`.
- `NightDesk.exe`: size `201832196`, SHA-256 `74DE6CF2B134A30AD2290FBB58827BC4FDE75BB7548F3856DFCE0ED04C465D6D`, ProductVersion `1.4.0+40355ae239d98961951ef2ca7de10f5ff1fb253a`.
- `nx-host.exe`: size `7895040`, SHA-256 `FFA6491BFC193BA91B40A506EB71239CBE93EA2918C05B6C171659ADB4D4A12C`, version `nx-host 0.1.0`.
- Release tests: **176 passed**, no display mutation.
- Release build: **0 warnings / 0 errors**.
- `C:\dev\NightDesk` is a Git repository with remote `origin`; do not treat local artifact hashes as proof of end-to-end remote-control acceptance.