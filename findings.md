# Findings

更新时间：2026-10-27（Asia/Seoul）

## 当前发布事实（2026-10-27 10:10 KST）

- 最新 Android 实现提交：`89ed923dae29d555e2c511fe6556c2dd8b563a80`，已推送至 `origin/main`。
- Windows 1.6.0 / 10600，ZIP 已在 AI 发布；当前生产桌面未被本轮替换。
- 本地 Windows：`C:\dev\NightDesk\artifacts\win-x64-1.6.0`；NightDesk.exe 201922308 字节，SHA-256 `d5c1c09832ad80d638fe27af9e68c41b8ff4e4dd0b5dcb89faabc2559be72e5d`。
- Windows ZIP：85183345 字节，SHA-256 `de162ec69f0f9c9d2e855fc7a084b1c316b4d78284be5b131fd393ec461004cb`。
- nx-host.exe 0.2.0 未改变，7938048 字节，SHA-256 `17b420ce531270e28bef58d42dd68f5c13cac6c5fe4cbfd4fdbb20051ad35e3`。
- Android 1.3.0 / 9，55075280 字节，SHA-256 `9e278bafb126b18963764fd829414075c30ba9ee795b39d4ad2f1db443d7aa42`，AI 不可变文件 nightdesk-9.apk。
- Android 发布证书保持 `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`。

## Android 远控手势调查（2026-10-27）

- 截图中远控页面提示“单指拖动鼠标”，普通触摸 `ACTION_DOWN` 当前就发送 `MouseDown(0)`，随后任何位移继续移动，容易把手指抖动解释为桌面拖选。
- `ACTION_UP` 仅当落点仍可映射到远端画面时才发送鼠标移动和抬起；滑到 Letterbox 黑边可能造成左键残留。
- 屏幕失焦、取消、退后台及销毁已有释放路径，但目前没有常驻的用户可操作“释放所有”按钮。
- 本机正式 NightDesk / nx-host 只读检查是旧数据，不能在该远端桌面做真实点击/输入验收；使用纯 JVM 逻辑测试、AI 模拟器合成画面。
- AI 可通过 SSH Alias 访问；既有 `/home/tony/dev/nightdesk-android-stage5` 保持不动，本功能使用独立 `/home/tony/dev/nightdesk-android-1.3.0` 复制目录，仅使用现有 `nightdesk-build` 和 `nightdesk-emulator` 任务会话，不操作 `main` tmux 会话。

## Android 远控 UX 1.3.0 验证结果（2026-10-27）

- 新 `NxTouchGesture` 把轻点、光标移动、长按拖动、双指滚动、取消和全量释放分开；所有 MouseDown/KeyDown 的中断异常路径由 `Session.releaseAll()` 兜底。
- `RemoteTouchControls` 常驻底栏显示手势说明和 48dp 以上的醒目“释放”按钮；RemoteActivity 处理系统栏 inset，触摸抬起即使落在 letterbox 也释放拖动。
- JVM：Core 10 + Connection 10 + NX 7 + Gesture 9 = 36 项通过。仪器：HostStore 3 + MainUi 1 + RemoteControlUI 1 + UpdateFlow 1 = 6 项通过。发布后另把模拟器暂时降到 v8，UpdateFlow 从真实 HTTPS 更新源下载、校验并恢复 v9，1 项通过。
- lint 0 errors / 23 warnings；AI 完整构建和 AndroidTest 构建成功；release APK 元数据为 dev.nightdesk.android 1.3.0 / versionCode 9 / minSdk 29，原签名证书不变。
- AI 更新服务 active；healthz `ok android=ok windows=ok`；线上 manifest、APK 路由、长度、SHA-256 和模拟器端下载校验匹配。
- 产物大小 55,075,280 字节，SHA-256 `9e278bafb126b18963764fd829414075c30ba9ee795b39d4ad2f1db443d7aa42`；本地忽略产物 `artifacts/android/nightdesk-1.3.0.apk`。
- Feed 保留 v8；发布前 manifest 备份 `manifest.json.pre-1.3.0-20261027-0945`。实际手机上的真实 Windows GDI/SendInput/tailnet 操作仍未验收。

- Windows：210 tests passed. No display state changed. 串行 Release build 0 warnings / 0 errors。
- WPF：纯 XAML 渲染 980/1180 DIP × 5 页通过，NX host/viewer 两组三字段的列和边界互不重叠。
- 安装器：实际 helper 在隔离临时目录等待自有测试进程、替换双合成文件、保留备份、重启合成 WinExe；未安装生产程序。
- 真实 Windows 下载器：临时缓存 + 旧版本比较值，HTTPS 下载当前 ZIP，校验包和双 EXE，解包 ready 成功；未调用安装。
- Android：27 JVM tests，lint 0 errors / 22 warnings；HostStore 3 + MainUi 1 + UpdateFlow 1 = 5 instrumented tests。发布后 UpdateFlow 另跑 1 项通过。
- Feed：5 Python tests；双平台 HTTPS manifest/HEAD 200；本地/AI/线上大小和哈希一致。
- 服务 active / enabled，healthz = ok android=ok windows=ok；其它 Serve 路由和 Funnel 状态未改变。

- 2026-10-27 08:07 KST 只读：Desktop NightDesk PID 23116、正式 nx-host PID 20232；100.109.77.29:7000，primary / 10 FPS。
- recovery.json 存在，352 字节，修改时间 07:03:42。PID 会变，不可据此强杀。
- 没有替换 Desktop、没有强杀/重启正式进程、没有切屏/恢复/VDD 操作、没有真实 GDI/SendInput 验收。
- 首次进入 1.6.0 仍须手动部署两个 EXE；先确认物理输出/快照恢复，再从旧版 UI 正常退出。后续更高版本自动下载，安装需确认。
- Windows ZIP 未代码签名；SHA-256/HTTPS/tailnet ACL 不等同于独立签名；helper 不提权。
- 真实双机 tailnet UDP/QUIC、GDI/SendInput、DPI/UAC、安全桌面、Android 物理真机、长期资源占用、生产自更新仍需验收。

## 历史技术调查（版本与 PID 以当前发布事实为准）

## Git / repository

- Workspace: `C:\dev\NightDesk`.
- Remote: `git@github.com:orangepro2001/NightDesk.git`.
- Branch: `main`.
- Initial import: `b1855e4 chore: initial NightDesk source import`.
- Previous feature baseline: `d720ce2eb18a793b348c3be209d9dedc095bc79d feat(desktop): add Windows-to-Windows control viewer`; current release facts are listed above.
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

## Mouse release cursor reset (2026-10-27)

- Confirmed protocol root cause in `native/mobile/session.go`: `Move` carried coordinates, while `MouseDown`, `MouseUp`, `Wheel`, and `ReleaseAll` mouse-up messages left `X/Y` at zero.
- Confirmed Windows `native/internal/inject/inject_windows.go` positions the cursor from mouse-event coordinates before applying button/wheel actions, so an Android release message reset the pointer to `(0,0)`.
- Integration Red test through the QUIC host and fake injector reported `(0,0)` for both ordinary `MouseUp` and `ReleaseAll`; this was not inferred from a source-only unit test.
- Fix stores the last successfully sent coordinates and serializes position changes, button/wheel events, and release-all under `inputMu`. Tests cover button down/up, wheel, and release-all at nonzero coordinates.
- Go tests/race/vet and Windows cross-vet/build passed on AI. A fresh gomobile AAR was included in Android 1.3.1/code 10; full Android build, 36 JVM tests, six instrumentation tests, and code9→code10 emulator update/install passed.
- Limit: the fake injector proves the coordinates delivered over QUIC, not actual Windows `SendInput`; real acceptance needs an isolated Windows host and Android phone.

## Dual-platform maintenance release (2026-10-27)

- Current pushed code is `c1f37f2` on a clean `main` branch. Current versions: Windows 1.6.1 / 10601, sidecar nx-host 0.2.1; Android 1.3.1 / code 10.
- User explicitly requests new distributable versions for both targets. Plan is to re-release the existing feature set as Windows 1.6.2 / 10602 and Android 1.3.2 / 11; preserve Android cert and nx-host binary/version.
- AI service is active/healthy. Need read `/home/tony/dev/nightdesk-update-feed/manifest.json` and `windows-manifest.json` directly and verify target versioned files are absent before writing releases.
- First combined JSON one-liner failed from SSH/zsh quoting only; no remote files were changed. Use plain `cat` and `ls`, not nested shell-escaped Python.

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
- Historical Android 1.1.0 release: versionCode **7**, versionName **1.1.0**, size **55058900**, SHA-256 `01734a26365b859c831a9ba7806ff1d133b96e0993061053bb2a60ab72caf480`; immutable file `nightdesk-7.apk`, alias `nightdesk-1.1.0.apk`.
- The previous release, 1.0.4 / versionCode 5, remains available for rollback reference. Android clients do not accept downgrades. The pre-publication manifest backup is `manifest.json.bak-20261027-062447`.
- Signing cert SHA-256: `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`.
- Signing material is outside the source tree with 0600 permissions; passwords/tokens are not written to docs, logs or screenshots.
- Client updates are discovered on app start, by a WorkManager periodic check (about every 6 hours), or by manual check. This is not an FCM push; installation still requires the Android system confirmation UI.
- Funnel is not enabled. Tailnet ACL is the access boundary.

## Historical 1.5.0 artifact facts

- Historical Windows 1.5.0 candidate directory: `artifacts\win-x64-1.5.0`.
- `NightDesk.exe`: size `201885444`, SHA-256 `50C71DADC8C1E425026CDC38C64DFEBE971E834B3E2DEE94F695F7E056EBD626`, ProductVersion `1.5.0+d720ce2eb18a793b348c3be209d9dedc095bc79d`.
- `nx-host.exe`: size `7938048`, SHA-256 `17B420CE531270E28BEF58D42DD68F5C13CAC6C5FE4CBFD4FDDBB20051AD35E3`, version `nx-host 0.2.0`.
- Safe end-to-end probe: synthetic 1920x1080 source, 4 frames, 538 tile updates, 7/7 input receipts, differing frame digests, host exit 0; no GDI capture and no SendInput.
- Windows desktop-to-desktop Viewer uses `System.Net.Quic`, TLS 1.3 and ALPN `go-nx`; the real test-mode host verified wrong-code rejection, first frame, ACK next frame, input receipt and host pin persistence.
- Actual `NxViewerWindow` rendered the synthetic frame and status in a WPF smoke; screenshot hash `330922E18899891316F9F050425E860D6E4CC750BDD62BB0B4B1F285F82278D2`.
- The native WPF QUIC viewer is currently limited to Windows 11 / Server 2022 or newer; Android remains independent.
- Android 1.1.0 publication verification: package dev.nightdesk.android, versionCode 7, minSdk 29, v2 signature, ZIP integrity, local/AI/HTTPS SHA-256 equality, HTTP 200 for latest and versioned APK, and emulator live-feed tests OK (4 tests).
- Publication documentation: docs/20261027-080919-paginated-ui-desktop-update-1.6.0-handoff.md.
- Desktop-control documentation: docs/20261027-064503-windows-desktop-control-1.5.0-handoff.md.
- Real GDI capture, SendInput/DPI/UAC behavior, two-machine tailnet UDP/QUIC and physical Android tailnet acceptance remain unverified.
- Release tests: **210 passed**, no display mutation.
- Release build: **0 warnings / 0 errors**.
- `C:\dev\NightDesk` is a Git repository with remote `origin`; do not treat local artifact hashes as proof of end-to-end remote-control acceptance.
# 2026-10-27 — NX 颜色修复与发布（完成）

- 协议事实：Go `tile.Frame`、Android callback 与 Windows viewer 输入帧均为 BGRA；tile 编码/解码字节完全保真，本轮未修改 Go 协议或 sidecar。
- 确认 Android 根因：原始 BGRA bytes 直接拷入 Android `ARGB_8888` Bitmap raw buffer 后，纯红被绘制为蓝。修复在一个 pass 内交换 B/R 并将 alpha 设为 255；Android byte buffer 不再设置 native byte order，因为它只复制原始 byte 序列。
- 确认 WPF 根因：GDI 32-bit DIB 高字节未定义，但 viewer `PixelFormats.Bgra32` 将其按 alpha 混合。使用 `PixelFormats.Bgr32` 忽略无效字节，不做每像素转换。
- Red/Green：Android AI emulator 色条修复前因纯红显示成蓝色失败，修复后 red/green/blue/非对称 RGB 均精确通过；测试同时验证 alpha=0/0x7f 被强制为不透明。WPF 静态像素格式检查在 Bgra32 时失败，改 Bgr32 后通过。
- Android AI build：36 JVM tests；lint 0 errors / 23 warnings；Debug/Release/AndroidTest 成功；HostStore 3 + MainUi 1 + RemoteControlUI 1 + UpdateFlow 1 = 6 项常规仪器测试通过，加 1 项颜色仪器测试通过。
- Android update：模拟器从 code 10 运行线上 UpdateFlow，确认 code 11 下载和 hash；通过 HTTPS 安装 APK 后显示 `versionCode=11`, `versionName=1.3.2`。证书 SHA-256 保持 `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`。
- Windows：211 项断言通过；Release 0 warnings/errors；`DesktopUpdateService -live-update` 从公网 tailnet feed 下载 1.6.2 并隔离 staging 两个 EXE，未执行安装；NX 合成 probe 4 frames / 538 tiles / 7 of 7 receipts。
- Go/AI：`go test ./...`、`go vet ./...`、Windows amd64 cross-vet/build 通过；未改 Go 源码。
- 已发布 Windows 1.6.2/code10602 和 Android 1.3.2/code11；HTTPS manifest、HEAD 长度与完整 GET SHA-256 一致，healthz=`ok android=ok windows=ok`。Release hashes 与具体 backup paths 记录于 `docs/20261027-163807-nx-color-fix-dual-release-1.6.2-1.3.2-handoff.md`。
- 未做真实 Android 手机、Windows 实时 GDI/SendInput、DPI/HDR/ICC、生产桌面升级验收；无生产进程重启或显示拓扑更改。

## NX remote-control unobstructed controls (2026-10-27)

- User reports persistent translucent overlays at the top and bottom of the NX remote screen obstructing both visibility and interaction. Wants a mature remote-support pattern with a side menu and keyboard access.
- Verified design references: RustDesk documents `collapse-toolbar` for starting a session with its toolbar collapsed; Microsoft Remote Desktop for Android documents an in-session connection bar, expandable command/selection bars, and a keyboard control that shows/hides the touch keyboard. Apply the minimal shared pattern: collapsed edge handle and on-demand side panel, not permanent top/bottom bands.
- Scope: update both Android and Windows viewers; preserve remote content sizing/gesture semantics, keep keyboard/special keys and explicit release-all one tap away.

## Confirmed code-path evidence: controlled-host cursor

- Windows `native/internal/capture/capture_windows.go` performs `BitBlt` into an off-screen memory DC, calls `GetCursorInfo` and `DrawIconEx` on that memory DC, then copies the DIB to the network frame. It does not draw on the physical desktop.
- Windows `native/internal/inject/inject_windows.go` converts every absolute remote pointer input into Win32 `SendInput`; this intentionally moves the interactive desktop cursor, so the physical host pointer can move while controlled.
- Repository-wide search found no NX `ShowCursor`, `SetCursor`, or `ClipCursor` calls. Static code therefore supports expected physical cursor movement, but cannot establish whether the reported visible blinking is an actual visibility toggle, capture artifact, monitor/compositor issue, or a particular Windows cursor theme. Do not add global cursor suppression without a reproducible isolated-host test and explicit restoration design.

## UX design reference

- RustDesk official advanced settings describe `collapse-toolbar` as collapsing the remote toolbar after connecting: https://rustdesk.com/docs/en/self-host/client-configuration/advanced-settings/ (section `collapse-toolbar`).
- Microsoft Remote Desktop for Android documents the connection bar, additional-options command/selection bars, and an icon to show/hide the keyboard: https://learn.microsoft.com/en-us/previous-versions/remote-desktop-client/remote-desktop-android ("Start a Remote Desktop connection", "Use the command bar").
- Implementation direction: full-bleed remote image, a small edge handle, and an on-demand vertical control panel; keyboard input/special keys and release-all stay one tap away. Desktop viewer gets equivalent collapsible controls and a local shortcut.

## NX sidebar release and physical cursor status (2026-10-27)

- Implemented and pushed in `e75618d`: Android/Windows viewer now keep only an edge handle over the remote image while idle; on-demand side panel contains session actions. Android local keys are F1 (menu), F2 (phone keyboard), Esc (collapse); Windows F1 remains local and remote F1 has a separate explicit action.
- Sidebar visibility changes release all remote mouse/key state. Android `RemoteControlUiInstrumentedTest` covers idle handle, discoverability, expanded menu, available controls and release operation; Windows assertions cover removal of permanent bands and menu/release actions.
- User's selected policy is to keep existing system cursor behavior and change it only after an isolated physical Windows reproduction.
- Confirmed local source: `DrawIconEx` receives the capture `memDC`; it composites into the off-screen frame. `SendInput` is the mechanism that moves the controlled Windows session's actual pointer. No NX capture/injection code uses `ShowCursor`, `SetCursor`, or `ClipCursor`.
- Therefore the source trace confirms expected physical pointer movement but does not confirm a blink/toggle bug or its root cause. Neither the production desktop nor another isolated Windows host was used for real GDI/SendInput this round.
- Current artifacts: Windows 1.6.3/10603 ZIP SHA-256 `3214419d6ed87d95111a1c1c2e719307b8cc046ad624ceeebd09b0310243ebc7`; Android 1.3.3/code12 APK SHA-256 `61f967a0d53d2fdbcf6fde6659404216cb804db847ff801576e22c2da9108e9f`. Both published and HTTPS HEAD length-checked; emulator reports code12.
- Detailed files and manual repro checklist: `docs/20261027-201142-nx-sidebar-cursor-review-1.6.3-1.3.3-handoff.md`.
