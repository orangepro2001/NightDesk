# Findings

更新时间：2026-10-26（Asia/Seoul）

## Host / display

- Host has Parsec VDD 0.45 installed (`ROOT\DISPLAY\0001`, signed `oem45.inf`), AMD graphics and GameViewer VDD.
- Latest read-only diagnostic: only active source is `\\.\DISPLAY6` (Parsec Virtual Display Adapter); no active physical output.
- `C:\Users\pixel\AppData\Local\NightDesk\recovery.json` is still present.
- The running desktop process is PID **22708**, product version **1.2.0**, path `C:\Users\pixel\Desktop\NightDesk.exe`.
- Do not exit/restart/replace that process, restore a snapshot, toggle displays or add/remove VDDs until a physical display is available and the snapshot can be verified.
- Tailscale node: `minipc-xoh4t.tail9f7f1f.ts.net`; desktop remote UI is `https://minipc-xoh4t.tail9f7f1f.ts.net/`. Serve is tailnet-only; Funnel is not enabled.

## Parsec VDD packaging

- Official installer used: `https://builds.parsec.app/vdd/parsec-vdd-0.45.0.0.exe`.
- Size: **517256** bytes.
- SHA-256: `e23332448fdaf5aa017cb308db5ef6855fac526a7ded05d80c039404126d5362`.
- Signer subject: `CN="Parsec Cloud, Inc."`.
- The installer is embedded in the 1.3.1 single-file EXE as `NightDesk.Assets.vdd.parsec-vdd-0.45.0.0.exe`.
- Startup probes `VddClient`; if the driver interface is unavailable, it extracts the embedded installer to `%LOCALAPPDATA%\NightDesk\driver\` and starts it with `runas` + `/S`.
- The file is rejected unless size, SHA-256 and signer subject match. Tampered or missing payloads never execute.
- Automatic installation is attempted only once per user; the UI's manual install button remains available for retry.
- This host already has the driver, so no uninstall/reinstall was performed. Verified paths: detection, embedded resource presence, size, hash, signer check, extraction, tamper rejection and missing-file rejection.
- A true absent-driver auto-install still requires a clean Windows machine or VM for manual acceptance.

## Android multi-host

- App: `dev.nightdesk.android`, minSdk 29 / target 35, Kotlin Views.
- Persistence schema: **schemaVersion 3** for host lists; schemaVersion 2 single-profile data is migrated automatically.
- Up to **20** hosts. Host identity is the normalized Tailscale control origin; re-adding the same origin updates its token instead of creating a duplicate.
- UI: spinner for saved hosts, automatic switch/refresh, and “删除当前主机”.
- Storage: Android Keystore AES-GCM over the complete JSON document; SharedPreferences contains only base64 ciphertext.
- Active host falls back to the first remaining host after deletion; deleting the last host clears the store.
- Instrumented result: `HostStoreInstrumentedTest: OK (3 tests)`, `0.778s`, `emulator-5580`.
- Tested: multi-host round trip/active switch/delete fallback, same-origin token replacement, and absence of plaintext token in SharedPreferences.

## AI Android toolchain / feed

- Android SDK/JDK/Gradle/adb/emulator operations run only on SSH host `AI`; Windows does not install the Android toolchain.
- SSH defaults into tmux. The pre-existing `main` session belongs to other work and must not be touched. Owned sessions: `nightdesk-build`, `nightdesk-emulator`.
- Noninteractive remote shell may lack `adb` on PATH; use `/home/tony/Android/Sdk/platform-tools/adb`.
- AVD uses software rendering because the host has no `/dev/kvm`.
- Update feed: `nightdesk-update-feed.service`, loopback `127.0.0.1:17768`, enabled at boot.
- Tailnet paths: `/nightdesk` download page, `/nightdesk/latest.apk`, `/api/v1/android-update` manifest, `/api/v1/android-apk` versioned download.
- Atomic publish order: APK copy -> version alias -> manifest last. The service verifies size and SHA-256 and returns 503 rather than exposing a half-published release.
- Live current release: versionCode **5**, versionName **1.0.4**, size **6991035**, SHA-256 `906142e6abda3d193272b7f68adcb053d9cbbdd00fca40423b806e1d2979e436`.
- Signing cert SHA-256: `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`.
- Signing material is outside the source tree with 0600 permissions; passwords/tokens are not written to docs, logs or screenshots.
- Funnel is not enabled. Tailnet ACL is the access boundary.

## Other current facts

- Windows 1.3.1 artifact: `artifacts\win-x64-1.3.1\NightDesk.exe`, size `201766660`, SHA-256 `04AAFCF79470AAE0CE7E2894A2E4177B9A37E220BB750470EA86B98CF394C8F0`.
- Release tests: **115 passed**, no display mutation.
- Release build: **0 warnings / 0 errors**.
- `C:\dev\NightDesk` is not a Git repository.
