# NightDesk progress

更新时间：2026-10-27 10:10 KST（Asia/Seoul）
最新恢复入口：docs/20261027-163807-nx-color-fix-dual-release-1.6.2-1.3.2-handoff.md

## Android 移动远控体验（2026-10-27）

- 重新开始：仓库干净，HEAD=`b5d8b88`；已读取现有状态机、截图、AI 构建/发布交接文档。
- 根因证据：触摸按下立即发送左键按下，手指位移造成拖选；释放坐标映射失败会跳过 MouseUp。
- 已新增待运行的纯 JVM 回归用例，覆盖点按、光标移动、长按拖动、双指滚动、取消及显式释放。
- AI Android 工具链仍在 AI；为本阶段复制独立构建目录 `/home/tony/dev/nightdesk-android-1.3.0`，未改动旧 stage5 目录和其它 tmux 会话。
- Red 已执行：新测试在没有 `NxTouchGesture`/`NxTouchAction` 时按预期失败；实现后 36 项 JVM 测试全部通过。
- 手势调整：轻点在抬手发一次左键单击；未长按的移动只移动鼠标；系统长按计时器后移动才按住左键拖动；双指纵向滚动；取消、多指切换、失焦、连接关闭、页面后台和销毁均释放。
- UI 调整：远控底栏用短提示解释四种手势，保留 48dp“释放”按钮；释放所有鼠标按钮和键盘键，拖动时提示抬手结束；全屏安全区已加入系统栏 inset。
- Android AI 完整构建成功；lint 0 errors / 23 warnings；instrumented 全套 6 项通过。发布后模拟器从 Android 1.2.0 / code8 实测更新下载到 1.3.0 / code9、大小/SHA/签名均匹配，最后恢复模拟器为 code9。
- Feed 发布成功：manifest versionCode 9，APK 55,075,280 bytes，SHA-256 `9e278bafb126b18963764fd829414075c30ba9ee795b39d4ad2f1db443d7aa42`。保留旧 APK v8，发布前清单备份为 `manifest.json.pre-1.3.0-20261027-0945`。
- 已完成：详细 timestamped handoff、项目概览和验收记录已更新；实现提交 `89ed923dae29d555e2c511fe6556c2dd8b563a80` 已推送到 `origin/main`。

## 当前交付状态

- Windows 基线实现提交：`93147b357a9b8497cd2be4c67d3af99668279724`；Android 手势提交见本轮 `89ed923dae29d555e2c511fe6556c2dd8b563a80`。
- Windows 1.6.0 / 10600，ZIP 已在 AI 发布；当前生产桌面未被本轮替换。
- 本地 Windows：`C:\dev\NightDesk\artifacts\win-x64-1.6.0`；NightDesk.exe 201922308 字节，SHA-256 `d5c1c09832ad80d638fe27af9e68c41b8ff4e4dd0b5dcb89faabc2559be72e5d`。
- Windows ZIP：85183345 字节，SHA-256 `de162ec69f0f9c9d2e855fc7a084b1c316b4d78284be5b131fd393ec461004cb`。
- nx-host.exe 0.2.0 未改变，7938048 字节，SHA-256 `17b420ce531270e28bef58d42dd68f5c13cac6c5fe4cbfd4fdbb20051ad35e3`。
- Android 1.3.0 / 9，55075280 字节，SHA-256 `9e278bafb126b18963764fd829414075c30ba9ee795b39d4ad2f1db443d7aa42`，AI 不可变文件 nightdesk-9.apk。
- Android 发布证书保持 `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`。

## 本轮完成

- 修复 WPF Grid.Column 缺失导致的文字/输入框重叠。
- Windows 五分页：概览、显示、远程访问、控制桌面、更新与诊断。
- Android 四页底部导航：首页、显示、远控、更新；系统栏 inset、页状态恢复和独立更新反馈。
- Windows 启动/6 小时自动检查并下载 ZIP，校验包和双 EXE；用户确认后等待正常退出再替换，保存备份。
- AI 双平台分发和原子 manifest；Windows 1.6.0、Android 1.3.0 上线。

## 验证

- Windows：210 tests passed. No display state changed. 串行 Release build 0 warnings / 0 errors。
- WPF：纯 XAML 渲染 980/1180 DIP × 5 页通过，NX host/viewer 两组三字段的列和边界互不重叠。
- 安装器：实际 helper 在隔离临时目录等待自有测试进程、替换双合成文件、保留备份、重启合成 WinExe；未安装生产程序。
- 真实 Windows 下载器：临时缓存 + 旧版本比较值，HTTPS 下载当前 ZIP，校验包和双 EXE，解包 ready 成功；未调用安装。
- Android：36 JVM tests，lint 0 errors / 23 warnings；HostStore 3 + MainUi 1 + RemoteControlUI 1 + UpdateFlow 1 = 6 instrumented tests。发布后从 code 8 到 code 9 的 UpdateFlow 另跑 1 项通过。
- Feed：5 Python tests；双平台 HTTPS manifest/HEAD 200；本地/AI/线上大小和哈希一致。
- 服务 active / enabled，healthz = ok android=ok windows=ok；其它 Serve 路由和 Funnel 状态未改变。

## 安全与剩余验收

- 2026-10-27 08:07 KST 只读：Desktop NightDesk PID 23116、正式 nx-host PID 20232；100.109.77.29:7000，primary / 10 FPS。
- recovery.json 存在，352 字节，修改时间 07:03:42。PID 会变，不可据此强杀。
- 没有替换 Desktop、没有强杀/重启正式进程、没有切屏/恢复/VDD 操作、没有真实 GDI/SendInput 验收。
- 首次进入 1.6.0 仍须手动部署两个 EXE；先确认物理输出/快照恢复，再从旧版 UI 正常退出。后续更高版本自动下载，安装需确认。
- Windows ZIP 未代码签名；SHA-256/HTTPS/tailnet ACL 不等同于独立签名；helper 不提权。
- 真实双机 tailnet UDP/QUIC、GDI/SendInput、DPI/UAC、安全桌面、Android 物理真机、长期资源占用、生产自更新仍需验收。

## 恢复入口

- AI Android 构建目录：/home/tony/dev/nightdesk-android-1.3.0。
- 服务目录：/home/tony/dev/nightdesk-update-feed。
- 备份：server.py.bak-20261027-080511、manifest.json.pre-1.3.0-20261027-0945；旧 code 8 APK 保留。
- 不删除旧 APK/ZIP，不重建覆盖同一已发布版本；修复提高版本号。

## Android 鼠标释放坐标复位修复（2026-10-27）

- 起点：仓库 `main` 干净，HEAD=`81f950e`。用户报告移动后光标可见，但松开鼠标后跳至左上角。
- 根因：Android Go `Session` 仅给 `Move` 消息带 host 坐标；MouseDown/MouseUp/Wheel/ReleaseAll MouseUp 均使用零值坐标。Windows injector 对每个鼠标事件执行消息坐标定位，造成复位。
- Red：在 AI `nightdesk-cursor-fix/native` 执行新增的移动 QUIC 集成测试，MouseUp 和 ReleaseAll 实际坐标为 `(0,0)`，按预期失败。
- Green：`Session` 在共享输入锁下记录最近成功鼠标位置，按钮、滚轮、释放消息复用它。`go test ./...`、`go test -race ./mobile`、`go vet ./...`、Windows amd64 cross-vet/build 通过。
- AAR 在 AI 以 arm/arm64/386/amd64 重新 gomobile bind；提交 AAR SHA-256 `bfa20670742caace05c940fd68fc790b3bf49b6a431d8f2268712936d08f22ad`。
- Android AI 构建目录 `/home/tony/dev/nightdesk-android-1.3.0`；36 JVM tests、lint、release/debug/AndroidTest build 和 6 项 instrumented tests 通过。
- 线上发布 Android 1.3.1/code 10，55,076,236 字节，SHA-256 `11496d15cd997cff45e37637a828548a41c396fdfeb403d948839424eb3f0311`；保留 code 9 与发布前清单备份。
- 模拟器先安装 code 9，UpdateFlow 从线上下载候选并校验长度/hash，然后安装实际 HTTPS 下载的 APK，最终报告 code 10。Feed health=`ok android=ok windows=ok`。
- 实现提交 `4a9930bbadb9b58a873307088914881636b71ac3` 已推送。新增详细 handoff，README、HANDOFF、ACCEPTANCE 与恢复进度已同步，随文档提交推送。
- 真正 Windows SendInput/Android 真机远控仍未验收；没有对生产桌面执行输入、GDI 或显示器操作。

## 双端新版本分发（2026-10-27）

- 用户要求分发并推送 Windows 与 Android 新版本。起点是干净 `main`、HEAD=`c1f37f2`。
- 当前版本核对：Windows 1.6.1/code 10601，nx-host 0.2.1；Android 1.3.1/code 10。此次按维护版重新发布当前功能，不声明额外功能改动，目标版本 Windows 1.6.2/code 10602 与 Android 1.3.2/code 11。
- AI 更新服务已确认 active，health=`ok android=ok windows=ok`。一个带复杂 shell 引号的 manifest 查询失败，未改远端；下一步直接读取服务目录 JSON 文件并检查不可变目标文件，再更新版本号与构建。

## NX 颜色修复与双端发布（2026-10-27）

- 起点 `main` / `c1f37f2`，保留先前工作区中的 Windows 1.6.2、Android 1.3.2/code 11 版本编辑和计划文件改动。
- 根因复现：Android AI emulator 的 BGRA 纯红色条按原逻辑画成蓝色；Windows WPF `Bgra32` 会将 GDI DIB 未定义的第 4 字节当 alpha。Go/tile BGRA 协议本身没有损坏。
- 修复：Android 在现有 alpha 清理循环内同时交换 B/R，设 alpha=255（单次 pass）；WPF 改用 `PixelFormats.Bgr32` 忽略该字节；协议和 `nx-host` 0.2.1 不变。
- Red/Green：Android 新色条 test 修复前失败/修复后纯红绿蓝及非对称 RGB pass，alpha 0 / 0x7f 被忽略；Windows WPF 格式回归修复前失败/修复后全套 pass。
- AI Android：36 JVM tests，lint 0 errors / 23 warnings，debug/release/AndroidTest build，6 常规 instrumented tests + 色条仪器测试通过。模拟器 code10 -> UpdateFlow download/check -> HTTPS 安装 code11 / 1.3.2。
- Windows：211 asserts、Release 0 warnings/errors、真实 DesktopUpdateService live-feed 下载/哈希验证/临时 staging、nx-probe 4 frames / 538 tiles / 7/7 receipts 全通过。
- AI Go：`go test ./...`、`go vet ./...`、Windows amd64 cross-vet/build 通过；Go 源无改动。
- 双端已发布：Windows 1.6.2/10602 ZIP 85,184,082 bytes SHA-256 `9856a7c03b9dff7b5ed89fbf106a597add1ec983a8f1b43db340fbfd13694717`；Android 1.3.2/code11 APK 55,076,236 bytes SHA-256 `1f5fc894074d468f92b6f19f0fbea64be1f7c55c1df696544690d630a0206118`。HTTPS manifests/HEAD/download hashes一致，签名证书保持不变，health `ok android=ok windows=ok`。
- 备份：Android manifest `/home/tony/dev/nightdesk-update-feed/manifest.json.pre-nx-colors-1.3.2-20261027-1625`；Windows manifest `/home/tony/dev/nightdesk-update-feed/windows-manifest.json.bak-20261027-162723`。旧 immutable artifacts 保留。
- 生产桌面未替换、重启或运行真实 GDI/SendInput；HDR/ICC/多 DPI、真机/双机 tailnet 仍需现场验收。详细复建见 `docs/20261027-163807-nx-color-fix-dual-release-1.6.2-1.3.2-handoff.md`。
- 本地代码、版本、README、handoff 与 acceptance 文件已更新；提交 `f4c95c5` 已推送到 `origin/main`。补充记录后应保持工作区干净。

## NX remote-control unobstructed controls — 2026-10-27

- Started after user clarified the previous NX image colors are correct; remaining request is usability: persistent top/bottom translucent overlays impede viewing/input; asks for balanced mature remote-control interaction with side menu and keyboard invocation.
- Upstream design references reviewed: Microsoft Remote Desktop connection bar/command surface and keyboard action; AnyDesk fullscreen edge toolbar; RustDesk collapsed session toolbar option.
- No implementation or release performed yet. Next: inspect live Android and WPF viewer layouts and their event routing, then select the smallest shared interaction change.

## NX remote controls and physical cursor investigation — 2026-10-27

- Read existing Android RemoteActivity/RemoteTouchControls: fixed translucent full-width header and footer occupy the viewer, with keyboard, special keys, right-click, and release split across them. Desktop NxViewerWindow reserves fixed header/footer rows.
- Confirmed mouse safety behavior already releases input on focus loss, background, disconnect, and close. The new menu toggle also calls release-all so collapsing cannot preserve a held drag/key.
- Cursor code trace: Windows sends remote absolute pointer actions through SendInput, moving the actual physical interactive desktop cursor. Cursor image compositing via GetCursorInfo/DrawIconEx targets the capture memory DC only. No NX path calls ShowCursor/SetCursor/ClipCursor, so flashing cannot be confirmed from source alone; physical repro is required before changing global pointer visibility.
- RustDesk official docs describe a post-connect collapsed toolbar (`collapse-toolbar`); using the analogous small edge handle + on-demand sidebar pattern.
- Next: refactor Android viewer overlays, then WPF viewer, test the layout/input release shortcut, build Android on AI and Windows locally. Do not touch production mouse/display state.

- AI first build error: immersive behavior constant was imported from the wrong AndroidX class. Corrected to `WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE`; no tests passed on the failed build, and the corrected source has not yet been rebuilt.

### NX remote-control sidebar / physical cursor review — completed 2026-10-27

- Resumed from source commit `e75618d` already pushed to `origin/main`; verified the changed viewer code and user-selected cursor decision: retain current physical cursor behavior until reproduced on isolated hardware.
- Re-ran Windows Release assertions: 215 passed, `No display state changed.` Re-ran `dotnet build src/NightDesk/NightDesk.csproj -c Release --no-restore`: 0 warnings/errors.
- On AI, installed the release + AndroidTest APK into `emulator-5580` and reran the isolated instrumentation script. HostStore 3, MainUI 1, RemoteControl UI 1, UpdateFlow 1 all passed; exit code 0. Emulator reports `versionCode=12`, `versionName=1.3.3`. JVM 36 and lint 0 errors / 23 warnings were already recorded by the completed build.
- Verified local Android APK and Windows ZIP SHA-256 equal the AI feed immutable artifacts and current manifest values. HTTPS latest HEAD is 200 on both; feed service active/healthy.
- Wrote timestamped detailed handoff and synchronized README, HANDOFF, ACCEPTANCE, plan/progress/findings. Documentation commit `3412b6b` is pushed to `origin/main`; `main` and `origin/main` match and the worktree is clean. Physical cursor flicker itself is not reproduced or claimed fixed.
