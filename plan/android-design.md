# Android client design and delivery — 2026-10-26（已完成）

## Goal

交付原生 Kotlin Android 客户端：扫码绑定 NightDesk 桌面端、获得长期设备令牌、安全控制多台 Windows 主机，并从独立于桌面进程的 AI 更新源自动检查、下载、校验和请求安装后续版本。Android SDK/JDK/Gradle/adb/模拟器操作只在 SSH 主机 `AI` 执行。

**最终状态：1.0.4（versionCode 5）已构建、签名、测试并发布。**

## Current package / environment

- Package: `dev.nightdesk.android`
- minSdk 29 / target 35
- Kotlin + native Views + `HttpURLConnection` + ZXing
- Update check on startup, every 6 hours through WorkManager, and manual
- Local source: `C:\dev\NightDesk\android`
- Remote source: `AI:/home/tony/dev/nightdesk-android`
- Build only in owned tmux sessions `nightdesk-build` / `nightdesk-emulator`; never touch `main`
- Windows does not install Android SDK, Gradle, adb or emulator
- Signing cert SHA-256: `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`

## Multi-host model

```kotlin
data class Profile(controlOrigin, controlToken, updateOrigin)
data class HostProfile(id, label, profile, addedAt)
data class Hosts(items: List<HostProfile>, activeId: String?)
```

- Host ID is the normalized Tailscale control origin (`https://<node>.<tailnet>.ts.net`).
- Up to 20 hosts.
- Upserting the same origin replaces that host's token and makes it active; no duplicate entry.
- UI offers a Spinner for saved hosts, automatic status refresh on switch, and “删除当前主机”.
- Deleting the active host falls back to the first remaining host; deleting the last clears storage.
- `schemaVersion 3` persists the list; `schemaVersion 2` single-profile JSON is migrated into one host.
- `ProfileStore` encrypts the entire JSON document with Android Keystore AES-GCM. SharedPreferences stores only base64 ciphertext; plaintext token persistence is explicitly tested against.

## Pairing / security

- QR payload: `v`, `kind`, `origin`, one-time `ndp1_` ticket, expiry.
- QR ticket lifetime is 5 minutes, single use, and never contains a long-lived token.
- `POST /api/v1/pair` returns an `ndd1_` per-device token.
- Device tokens have no expiry; they remain valid until the desktop user deletes the trusted-device binding.
- Desktop stores token hashes in a DPAPI-protected registry.
- Revocation takes effect on the next request (401).
- Android accepts only HTTPS Tailscale MagicDNS control origins.
- Control origin and update origin are independent; update requests never include the control token.

## Update chain

- Default update origin: `https://tony-ai-pc-1.tail9f7f1f.ts.net`
- Manifest: `/api/v1/android-update`
- APK: `/nightdesk/latest.apk` or versioned `/api/v1/android-apk`
- Validate package name, versionCode/versionName, minSdk, byte size, SHA-256 and APK signer certificate before handing the file to the system installer.
- Never silently install; Android's installer confirmation is always shown.
- AI feed publishes atomically: APK -> alias -> manifest. Server verifies size and SHA-256 before serving.

## Test evidence

- Build: `build-1.0.4.log`, exit `0`.
- Instrumented multi-host test on `emulator-5580`: `HostStoreInstrumentedTest: OK (3 tests)`, `0.778s`.
  - multiple hosts persist and active host switches;
  - same-origin upsert replaces token;
  - no plaintext token in SharedPreferences.
- Earlier update-flow instrumented tests on API 35 emulator covered both “already latest” and downgrade-then-upgrade branches.
- Live feed verified: versionCode 5 / 1.0.4, size 6991035, SHA-256 `906142e6abda3d193272b7f68adcb053d9cbbdd00fca40423b806e1d2979e436`; page and APK HTTP 200.

## Known limits / future plan

- No `/dev/kvm` on AI; emulator is software-rendered and slow. Physical-device acceptance remains required.
- Physical Android end-to-end pairing, camera permission, background/WorkManager limits, lock-screen behavior and installer UX still need a real phone.
- Future enhancements (not required for 1.0.4):
  - optional labels/renaming beyond auto-derived host names;
  - host health badges and last-success timestamps in the picker;
  - Quick Settings tile / widget;
  - per-host default display mode editor;
  - target SDK upgrade and Play Store/distribution policy if distribution expands beyond tailnet sideloading.
