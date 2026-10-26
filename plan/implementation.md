# NightDesk implementation plan

更新时间：2026-10-27（Asia/Seoul）
当前代码基线：`93147b357a9b8497cd2be4c67d3af99668279724`
最新交接：`docs/20261027-080919-paginated-ui-desktop-update-1.6.0-handoff.md`

## Scope / assumptions

- Native Windows desktop application: C# WPF/.NET 10 + Win32 API.
- Physical outputs are detached through a validated display-topology switch to a NightDesk-owned Parsec VDD; no global monitor power broadcast.
- Recovery snapshot remains on disk until restoration is independently verified.
- Official Parsec VDD installer is bundled and may be auto-installed when missing. Automatic Internet download is not used.
- Remote control uses loopback HTTP behind Tailscale Serve HTTPS, bearer authentication and fixed commands; no Funnel.
- go-nx is integrated as a separate Go host sidecar plus native Android and Windows WPF viewers. It is a low-bandwidth tile data plane, not a video-streaming replacement.
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
10. [done] Run Windows Release tests/build/publish and Android 1.0.4 instrumented tests/publication.
11. [done] Add go-nx sidecar integration: monitor-aware GDI capture, 64x64 tile deltas, QUIC + TLS 1.3, pinned identity, paired-device trust and JSON-line IPC.
12. [done] Add the Android native NX viewer and stage 5 connection lifecycle: explicit phases, 20-second deadline, classified recovery actions, background input release and stale-callback isolation.
13. [done] Stage 6 packaging fix: copy `nx-host.exe` to the publish directory, exclude it from the single-file bundle, add `tools/publish-windows.ps1`, and rebuild Windows 1.4.0.
14. [done] Re-run the Android stage 5 build and tests on AI; confirm the local candidate hashes match the AI outputs.
15. [done] Update current handoff, acceptance, progress and planning documents with the 1.4.0 candidate and the production safety gate.
16. [done] Stage 7 safe end-to-end probe: `nx-host -test-mode` with a synthetic pattern source and log-only injector, standalone `nx-probe`, negative misuse guard, moved-input receipts and published-artifact acceptance.
17. [done] Publish Android 1.1.0 / versionCode 7 with backup, immutable APK, version alias and final atomic manifest replacement; verify HTTPS downloads and the emulator update flow.
18. [done] Add Windows-to-Windows control: validated address/code/fingerprint input, persistent Viewer identity and host pins, native WPF QUIC viewer, tile rendering, input release, real test-mode integration and 1.5.0 publication.

19. [done] Paginated Windows/Android UI, layout overlap correction, Windows automatic downloader/install helper, dual-platform AI publication and verification.

## Verification

- Windows：210 tests passed. No display state changed. 串行 Release build 0 warnings / 0 errors。
- WPF：纯 XAML 渲染 980/1180 DIP × 5 页通过，NX host/viewer 两组三字段的列和边界互不重叠。
- 安装器：实际 helper 在隔离临时目录等待自有测试进程、替换双合成文件、保留备份、重启合成 WinExe；未安装生产程序。
- 真实 Windows 下载器：临时缓存 + 旧版本比较值，HTTPS 下载当前 ZIP，校验包和双 EXE，解包 ready 成功；未调用安装。
- Android：27 JVM tests，lint 0 errors / 22 warnings；HostStore 3 + MainUi 1 + UpdateFlow 1 = 5 instrumented tests。发布后 UpdateFlow 另跑 1 项通过。
- Feed：5 Python tests；双平台 HTTPS manifest/HEAD 200；本地/AI/线上大小和哈希一致。
- 服务 active / enabled，healthz = ok android=ok windows=ok；其它 Serve 路由和 Funnel 状态未改变。

## Current artifact facts

- 实现提交：`93147b357a9b8497cd2be4c67d3af99668279724`。
- Windows 1.6.0 / 10600，ZIP 已在 AI 发布；当前生产桌面未被本轮替换。
- 本地 Windows：`C:\dev\NightDesk\artifacts\win-x64-1.6.0`；NightDesk.exe 201922308 字节，SHA-256 `d5c1c09832ad80d638fe27af9e68c41b8ff4e4dd0b5dcb89faabc2559be72e5d`。
- Windows ZIP：85183345 字节，SHA-256 `de162ec69f0f9c9d2e855fc7a084b1c316b4d78284be5b131fd393ec461004cb`。
- nx-host.exe 0.2.0 未改变，7938048 字节，SHA-256 `17b420ce531270e28bef58d42dd68f5c13cac6c5fe4cbfd4fdbb20051ad35e3`。
- Android 1.2.0 / 8，55058900 字节，SHA-256 `82136d2992ff30c3bf84a5d45a02bc04bda4d38203880193883abb747100c52f`，AI 不可变文件 nightdesk-8.apk。
- Android 发布证书保持 `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`。

## Tracked issues / safety gate

- 2026-10-27 08:07 KST 只读：Desktop NightDesk PID 23116、正式 nx-host PID 20232；100.109.77.29:7000，primary / 10 FPS。
- recovery.json 存在，352 字节，修改时间 07:03:42。PID 会变，不可据此强杀。
- 没有替换 Desktop、没有强杀/重启正式进程、没有切屏/恢复/VDD 操作、没有真实 GDI/SendInput 验收。
- 首次进入 1.6.0 仍须手动部署两个 EXE；先确认物理输出/快照恢复，再从旧版 UI 正常退出。后续更高版本自动下载，安装需确认。
- Windows ZIP 未代码签名；SHA-256/HTTPS/tailnet ACL 不等同于独立签名；helper 不提权。
- 真实双机 tailnet UDP/QUIC、GDI/SendInput、DPI/UAC、安全桌面、Android 物理真机、长期资源占用、生产自更新仍需验收。
