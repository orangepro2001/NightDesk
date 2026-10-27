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
27. [done] Paginate and redesign the Windows and Android UIs, fix field/text overlap, add Windows auto-download updates, and publish both Windows and Android through the AI feed.

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

- Windows：210 tests passed. No display state changed. 串行 Release build 0 warnings / 0 errors。
- WPF：纯 XAML 渲染 980/1180 DIP × 5 页通过，NX host/viewer 两组三字段的列和边界互不重叠。
- 安装器：实际 helper 在隔离临时目录等待自有测试进程、替换双合成文件、保留备份、重启合成 WinExe；未安装生产程序。
- 真实 Windows 下载器：临时缓存 + 旧版本比较值，HTTPS 下载当前 ZIP，校验包和双 EXE，解包 ready 成功；未调用安装。
- Android：27 JVM tests，lint 0 errors / 22 warnings；HostStore 3 + MainUi 1 + UpdateFlow 1 = 5 instrumented tests。发布后 UpdateFlow 另跑 1 项通过。
- Feed：5 Python tests；双平台 HTTPS manifest/HEAD 200；本地/AI/线上大小和哈希一致。
- 服务 active / enabled，healthz = ok android=ok windows=ok；其它 Serve 路由和 Funnel 状态未改变。

- 实现提交：`93147b357a9b8497cd2be4c67d3af99668279724`。
- Windows 1.6.0 / 10600，ZIP 已在 AI 发布；当前生产桌面未被本轮替换。
- 本地 Windows：`C:\dev\NightDesk\artifacts\win-x64-1.6.0`；NightDesk.exe 201922308 字节，SHA-256 `d5c1c09832ad80d638fe27af9e68c41b8ff4e4dd0b5dcb89faabc2559be72e5d`。
- Windows ZIP：85183345 字节，SHA-256 `de162ec69f0f9c9d2e855fc7a084b1c316b4d78284be5b131fd393ec461004cb`。
- nx-host.exe 0.2.0 未改变，7938048 字节，SHA-256 `17b420ce531270e28bef58d42dd68f5c13cac6c5fe4cbfd4fdbb20051ad35e3`。
- Android 1.2.0 / 8，55058900 字节，SHA-256 `82136d2992ff30c3bf84a5d45a02bc04bda4d38203880193883abb747100c52f`，AI 不可变文件 nightdesk-8.apk。
- Android 发布证书保持 `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`。

## Remaining manual work / safety state

- 2026-10-27 08:07 KST 只读：Desktop NightDesk PID 23116、正式 nx-host PID 20232；100.109.77.29:7000，primary / 10 FPS。
- recovery.json 存在，352 字节，修改时间 07:03:42。PID 会变，不可据此强杀。
- 没有替换 Desktop、没有强杀/重启正式进程、没有切屏/恢复/VDD 操作、没有真实 GDI/SendInput 验收。
- 首次进入 1.6.0 仍须手动部署两个 EXE；先确认物理输出/快照恢复，再从旧版 UI 正常退出。后续更高版本自动下载，安装需确认。
- Windows ZIP 未代码签名；SHA-256/HTTPS/tailnet ACL 不等同于独立签名；helper 不提权。
- 真实双机 tailnet UDP/QUIC、GDI/SendInput、DPI/UAC、安全桌面、Android 物理真机、长期资源占用、生产自更新仍需验收。

## Current feature plan: desktop-to-desktop control

Goal: let a Windows NightDesk instance control another Windows NightDesk go-nx host without relying on Android or a QR scanner.

1. [done] Add validated target address, one-time pairing code and optional host-fingerprint fields to the WPF UI; persist address/fingerprint but never the pairing code.
2. [done] Implement a pinned C# QUIC client with a persistent viewer identity, TLS 1.3, go-nx ALPN, tile decoding, ACK/receipt handling and classified connection errors.
3. [done] Add a dedicated viewer window with aspect-correct rendering, keyboard/mouse/wheel forwarding and release-all on focus loss/close.
4. [done] Verify address/fingerprint/protocol parsing with unit tests and the real `nx-host.exe -test-mode -source pattern -inject log` binary; do not use GDI or SendInput.
5. [done] Rebuild/publish the Windows candidate, record tests, hashes and manual acceptance limits in timestamped documentation.

Success criteria: entering a reachable host address and the current pairing code opens a live control window; a wrong code or wrong fingerprint fails closed; closing the viewer releases all input; synthetic frames and input receipts pass without touching the display topology.

## Current feature plan: paginated UI, desktop updates, mobile UI

1. [done] Split the WPF window into clear pages (overview, display, remote access, control desktop, updates/diagnostics) and remove the single long scroll layout.
2. [done] Add consistent field spacing and wrapping so labels, values, hints and buttons cannot overlap at the supported window sizes.
3. [done] Split the Android app into persistent bottom navigation pages (home, display, remote control, updates) with a compact status header.
4. [done] Add a Windows update client: startup/6-hour check, automatic verified download, staged install helper and user-confirmed restart.
5. [done] Extend the AI feed to serve immutable Windows ZIPs plus an atomic Windows manifest alongside the existing Android feed.
6. [done] Run Windows tests/build/publish, Android JVM/lint/instrumented/emulator smoke tests, publish Windows 1.6.0 and Android 1.2.0, verify live downloads, and document recovery.

### UI/update implementation verification (2026-10-27 KST)

- Windows: 210 tests passed, serial Release build 0 warnings/errors.
- Layout: isolated XAML renderer verified 5 pages at 980 and 1180 DIP; all remote-control inputs have distinct columns. No application services started.
- Installer: the actual helper replaced two synthetic files in a temporary directory, preserved backups and restarted a synthetic WinExe. Production processes untouched.
- AI Android 1.2.0: 27 JVM tests, lint 0 errors/22 warnings, 5 instrumented tests passed; bottom navigation and system-bar insets verified.
- Feed: 5 Python tests passed for Android compatibility, Windows manifest/ZIP, dual index and proxy-stripped routes.
- Published Windows 1.6.0 and Android 1.2.0 on AI; verified HTTPS manifests, lengths, hashes and actual Windows downloader. Handoff: docs/20261027-080919-paginated-ui-desktop-update-1.6.0-handoff.md

## Current feature plan: Android remote-control touch UX

Goal: prevent accidental desktop drag-selection, guarantee mouse/key release, and make mobile control discoverable and recoverable.

1. [done] Add pure JVM gesture tests for tap-to-click, hover movement, long-press drag, two-finger scroll, cancel, and release.
2. [done] Implement a deterministic Android touch state machine: tap clicks only on release, movement never holds left, long press starts drag, pointer cancellation releases all.
3. [done] Add a persistent release-all control, update gesture instructions and expose active drag state.
4. [done] Run Android JVM, lint, release, instrumented and emulator checks on AI; preserve signing identity and verify published download.
5. [done] Write timestamped recovery/validation notes, update project handoff records, then commit and push the completed stage.

Success criteria: a normal tap generates one balanced click; finger movement cannot select text or icons unless the user intentionally long-presses; all cancellation/focus/background/close paths release input; explicit release remains visible during remote control.

### Current verification

- Worktree was clean at `b5d8b88` before this feature.
- Confirmed existing Android code sends `MouseDown` on every `ACTION_DOWN`, moves while it is held, and omits `MouseUp` when release coordinates map outside the host image.
- Android build/test operations remain on SSH host `AI`; never interact with the unrelated `main` tmux session or production desktop input/display state.
- AI Red run naturally failed before implementation (`NxTouchGesture` absent); after implementation all nine gesture tests pass.
- AI build used an isolated copy under `/home/tony/dev/nightdesk-android-1.3.0`; the prior stage5 directory and unrelated `main` tmux session remain untouched. A stale stage5 source snapshot was replaced by the complete current Android source before final verification.
- Final Android checks: 36 JVM tests; lint 0 errors / 23 warnings; release/debug/AndroidTest build passed; 6 full instrumented tests passed, including release-control UI; a post-publish 1.2.0 -> 1.3.0 emulator update/download/hash/signature test passed.
- Android 1.3.0 / code 9 published to AI feed; immutable APK SHA-256 `9e278bafb126b18963764fd829414075c30ba9ee795b39d4ad2f1db443d7aa42`, size 55,075,280; signing certificate unchanged.
- Android implementation and timestamped handoff pushed to `origin/main` as `89ed923dae29d555e2c511fe6556c2dd8b563a80`; `docs/HANDOFF.md` and `docs/ACCEPTANCE.md` were synchronized and pushed in follow-up commit `680f18f`.
- Errors and resolutions: test file initially landed in the copied main source folder (moved to test); UI test initially waited from the Android main thread (moved wait outside callback); stale Android snapshot lacked matching source/old tests (synchronized all current `android/app/src` and rebuilt).
- Remaining manual acceptance: use an Android phone and isolated Windows test host to verify real GDI/SendInput, the touch gestures, DPI, cancellation and recovery; do not perform this on the production desktop.

## Current bug investigation: cursor visibility/movement

Goal: verify the reported mobile cursor issue end-to-end and fix the first missing link found in the input/capture path.

1. [done] Trace Android touch mapping → QUIC `KindMouseMove` → Windows `SendInput` → GDI capture → tile diff → Android rendering; confirm frame capture omitted the system pointer.
2. [done] Composite the Windows cursor image into monitor-local GDI frames using `GetCursorInfo`, `GetIconInfo`, and `DrawIconEx`; account for monitor origin and cursor hotspot so the existing tile diff transmits its movement.
3. [done] Add portable cursor geometry tests; run Go unit tests/vet and Windows cross-vet/build on SSH host AI; run the published-artifact synthetic QUIC probe and all 210 Windows tests.
4. [done] Bump Windows/NX host to 1.6.1 / 0.2.1, publish the Windows ZIP to the AI update feed, verify HTTPS manifest and HEAD; update handoff and acceptance docs.

Success criteria: the captured remote frame includes the host cursor at the position moved to by the existing input pipeline. Physical SendInput and real GDI visual confirmation on an isolated Windows host remain explicitly unverified.

### Current verification and boundary (2026-10-27 KST)

- Android coordinate mapping and protocol path were traced; no Android source change or APK release was needed. Root cause in the code path: GDI `BitBlt` copied desktop pixels but did not include the system cursor, while Android rendered only transmitted frame tiles.
- Windows `Screen.Grab` now draws the actual cursor image into the captured monitor frame after `BitBlt`; hotspot and negative/secondary monitor origins are handled in tested portable geometry.
- AI Go checks: `go test ./...`, `go vet ./...`, Windows/amd64 cross-`vet` and build passed. Host version is 0.2.1.
- Safe end-to-end probe: 4 synthetic 1920×1080 frames, 538 tile updates, 7/7 input receipts; strict test mode logs input and does not call GDI or SendInput.
- Windows suite: 210 passed; no display state changed. Windows publish produced NightDesk 1.6.1 and nx-host 0.2.1; update manifest is 1.6.1 / 10601 and HTTPS package HEAD returns 200 with expected length.
- Not tested: actual Windows `GetCursorInfo`/`DrawIconEx` pixels on a live desktop, physical `SendInput`, Android phone, DPI/UAC/security desktop, or tailnet two-device session. Existing production processes/displays were not restarted or modified.
- At the Windows 1.6.1 capture-fix stage, Android APK was 1.3.0 / code 9 and was not rebuilt because that specific fix used the existing tile protocol. The later release state is recorded in the mouse-release investigation below.

## Current bug investigation: mouse release resets cursor to origin

Goal: keep the Android-controlled Windows cursor at the last touched position after clicks, drag release, wheel input, and release-all.

1. [done] Add integration regression assertions for MouseDown/MouseUp/Wheel and ReleaseAll coordinates; run on AI before implementation and confirm `(0,0)` failures.
2. [done] Track last successfully sent pointer position under the session input lock and attach it to all mouse button, wheel, and cleanup release messages.
3. [done] Re-run Go full/race tests, vet, Windows cross-vet/build; regenerate gomobile AAR on AI.
4. [done] Build Android 1.3.1/code 10 on AI; run JVM/lint/build/instrumented tests and emulator update/install verification.
5. [done] Publish immutable code 10 APK, verify manifest/HTTPS/size/hash and preserve code 9 history.
6. [done] Update timestamped recovery, handoff, README and acceptance documentation; commit and push docs.

Success criteria: every mouse release/wheel message after a Move preserves its nonzero host coordinate, the cursor does not return to `(0,0)` due to a default protocol coordinate, and remaining physical SendInput caveats are explicit.

### Verification

- Red: mobile QUIC integration tests received `(0,0)` for MouseUp and ReleaseAll; both failed against the original implementation.
- Green: all mouse button/wheel/release assertions pass; `go test ./...`, `go test -race ./mobile`, `go vet ./...`, Windows amd64 cross-vet/build passed on AI.
- Android AAR SHA-256 `bfa20670742caace05c940fd68fc790b3bf49b6a431d8f2268712936d08f22ad`; 36 JVM tests and 6 instrumented tests passed.
- Android 1.3.1/code 10 is live; APK size 55,076,236, SHA-256 `11496d15cd997cff45e37637a828548a41c396fdfeb403d948839424eb3f0311`; emulator updated from code 9 and reports code 10.
- Implementation commit `4a9930bbadb9b58a873307088914881636b71ac3` pushed; timestamped handoff, README, HANDOFF, ACCEPTANCE and recovery notes synchronized for the documentation push.
- No production Windows SendInput, capture, process restart or display topology operation was performed. Isolated Windows + Android real-device acceptance remains outstanding.

## Next Step

- The code and update-feed release are complete. When an isolated Windows test host and Android phone are available, execute the real-input acceptance checklist in `docs/20261027-134609-android-mouse-release-coordinate-1.3.1-handoff.md`.

## Current task: NX 颜色链路修复并分发双端版本

Goal: identify and fix the reported NX color rendering issue with color-bar regression tests and polish the resulting render path; then distribute both current applications, verify update manifests/artifacts, and push source/version/recovery documentation.

1. [done] Confirm source version, worktree changes, release pipeline and AI feed/service state.
2. [done] Add emulator pixel-color regression coverage and trace frame bytes through GDI/tile/Android; localize the abnormal color rendering.
3. [done] Apply the minimal correct color/alpha/rendering fix and polish the NX viewing path; rerun Go, Android, and Windows pixel-format checks.
4. [done] Build Windows 1.6.2/code 10602 and Android 1.3.2/code 11 candidates with full platform verification.
5. [done] Publish immutable packages, verify HTTPS manifests/hashes and Android emulator update path.
6. [done] Update README, acceptance and timestamped recovery docs; commit and push source plus documentation.

Release note policy: this is a maintenance re-release of the current feature set; do not claim unimplemented functionality. Keep the Android signing identity and Windows sidecar version 0.2.1 unchanged. Never overwrite existing immutable feed artifacts.

### Current observations

- Workspace started on `main` at `c1f37f2`; user-requested version edits and task records were preserved. Source and recovery docs are committed and pushed.
- AI update service is active and health endpoint reports `ok android=ok windows=ok`; manifests now serve Windows 1.6.2 / 10602 and Android 1.3.2 / 11.
- AI feed backups: Android `manifest.json.pre-nx-colors-1.3.2-20261027-1625`; Windows `windows-manifest.json.bak-20261027-162723`. Prior immutable artifacts remain present.
- On AI, the feed service listens on loopback port 17768; `/healthz` is `ok android=ok windows=ok`. Before publication, immutable targets `nightdesk-11.apk` and `windows/nightdesk-windows-1.6.2.zip` were both verified absent; prior manifests were Android 1.3.1/code 10 and Windows 1.6.1/code 10601.
- Red/green identified two client-specific issues: Android imported Go/GDI BGRA bytes as RGBA, swapping red/blue; WPF `Bgra32` treated the undefined high byte of GDI's 32-bit DIB as alpha. Android now swaps B/R while forcing alpha opaque in one pass; WPF uses Bgr32 to ignore that byte. Go protocol bytes remain BGRA.
- Color verification: AI emulator color bar tests red, green, blue, and asymmetric RGB with invalid alpha and passes after the fix. Android full build/JVM 36/lint 23 warnings/6 standard instrumented tests pass; the added color instrumentation test passes separately (7 total instrumented tests). Installed emulator reports code 11 / 1.3.2.
- Windows 211 assertions pass, Release build has 0 warnings/errors, published candidate has 1.6.2 and nx-host 0.2.1; ZIP contains exactly release.json and the two EXEs, all package member hashes match. NX safe synthetic probe passes 4 frames, 538 tile updates and 7/7 receipts. Go `test`, `vet`, Windows/amd64 cross-vet/build pass.
- Candidate SHA-256: Windows ZIP 85,184,082 bytes `9856a7c03b9dff7b5ed89fbf106a597add1ec983a8f1b43db340fbfd13694717`; Android APK 55,076,236 bytes `1f5fc894074d468f92b6f19f0fbea64be1f7c55c1df696544690d630a0206118`; signing certificate unchanged.

## Next Step

- Completed and pushed as `f4c95c5` (`fix: correct NX remote color rendering`); worktree matches `origin/main`.
