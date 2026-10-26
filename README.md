# NightDesk · 屏幕与远程工作台

原生 Windows 桌面应用：C# / WPF / Win32 API，不使用 Electron。提供中文界面、系统托盘、统一应用图标（EXE / 窗口 / 托盘 / Android 自适应图标）、Tailscale HTTPS 远程网页、Windows 桌面控制 Windows，以及原生 Kotlin Android 客户端。桌面端和 Android 都支持基于 go-nx 协议内核的低带宽 tile 屏幕远控。

## 当前交付状态（2026-10-27）

- Windows 当前代码基线：`d720ce2eb18a793b348c3be209d9dedc095bc79d`（1.5.0 桌面控制）；Android 当前公开发布：**1.1.0（versionCode 7）**。
- Windows **1.5.0 候选**已通过 **203 项 Release 测试**，Release 构建 0 警告 / 0 错误。发布目录 `C:\dev\NightDesk\artifacts\win-x64-1.5.0` 必须同时包含：
  - `NightDesk.exe`：`201885444` 字节，SHA-256 `50C71DADC8C1E425026CDC38C64DFEBE971E834B3E2DEE94F695F7E056EBD626`，ProductVersion `1.5.0+d720ce2eb18a793b348c3be209d9dedc095bc79d`。
  - `nx-host.exe`：`7938048` 字节，SHA-256 `17B420CE531270E28BEF58D42DD68F5C13CAC6C5FE4CBFD4FDDBB20051AD35E3`，`-version` 输出 `nx-host 0.2.0`。
- 发布必须使用 `tools\publish-windows.ps1`；脚本使用全新 staging 目录并断言两份文件都存在。只复制 `NightDesk.exe` 会导致 NX 远控不可用。
- 阶段 7 新增 `nx-probe.exe` 与 `tools\test-nx-probe.ps1`：用与 Android 相同的 pinned TLS / 配对 / QUIC / tile 解码路径，在合成源上安全验收首帧、动态帧、tile 差分和 7/7 输入回执，不抓屏、不调用 SendInput。详见 `docs/20261027-041844-remote-control-stage-7-safe-e2e-probe-handoff.md`。
- 1.5.0 新增 Windows 桌面控制 Windows：第 06 区可输入目标 Tailscale 地址、6 位配对码和可选主机指纹，打开原生 WPF 控制窗口；首次连接后固定证书和客户端身份。控制窗口依赖 `System.Net.Quic`，当前要求 Windows 11 / Server 2022 或更新版本。
- 1.5.0 仍将官方 Parsec VDD 安装器内嵌在单文件 EXE 中。启动时未检测到 VDD 设备接口，会在用户授权 UAC 后自动安装；已安装时绝不重装或替换驱动。
- **当前本机状态**（2026-10-27 06:47 KST 只读检查）：`C:\Users\pixel\Desktop\NightDesk.exe`（修复前旧候选）PID 28504 正在运行；其 `nx-host.exe` PID 4444 正以 `100.109.77.29:7000`、`primary`、10 FPS 使用正式状态目录监听。不要强杀、不要并发启动第二个正式 host、不要执行真实抓屏/SendInput 验收。部署 1.5.0 前必须先通过 UI 正常退出旧进程并确认显示恢复。
- `%LOCALAPPDATA%\NightDesk\recovery.json` 在 2026-10-27 06:34 KST 存在（351 字节）；这不能证明显示拓扑已经安全。最近一次只读诊断仍只确认 `\\.\DISPLAY6`（Parsec VDD），没有确认活动物理输出。在独立复核物理输出、DPI 和 VDD 之前，不要执行熄屏、点亮、恢复快照或 VDD 增删，也不要运行真实抓屏/SendInput 验收。
- Android **1.1.0**（versionCode 7）已发布到 AI 更新源：新增 go-nx 固定证书 tile 查看器、触摸/滚轮/键盘/Unicode 输入、连接状态机、超时与重试、后台释放输入。产物 `C:\dev\NightDesk\artifacts\android\nightdesk-1.1.0-stage5.apk`，大小 `55058900`，SHA-256 `01734a26365b859c831a9ba7806ff1d133b96e0993061053bb2a60ab72caf480`。已完成 26 项 JVM 单测、4 项线上更新源仪器测试和模拟器冒烟；**真实 Windows 抓屏、SendInput、真机 tailnet 尚未验收**。
- Android **1.0.4**（versionCode 5）保留为上一发布版本和回滚参考：`C:\dev\NightDesk\artifacts\android\nightdesk-1.0.4.apk`，大小 `6991035`，SHA-256 `906142e6abda3d193272b7f68adcb053d9cbbdd00fca40423b806e1d2979e436`。

## 运行（1.5.0 受控部署后）

发布目录：`C:\dev\NightDesk\artifacts\win-x64-1.5.0`。必须把 `NightDesk.exe` 与 `nx-host.exe` 一起复制到同一目录；这是 Windows x64 自包含主程序，无需另装 .NET Runtime。需要已登录的 Tailscale Windows 客户端。

1. **先关闭现有 ParsecVDisplay 管理器**。NightDesk 不会杀进程，也不会接管其它程序创建的屏幕。
2. 在有物理显示器活动的 Windows **控制台用户会话**运行程序。Parsec / GameViewer 等控制台远控可使用；RDP 会话不支持切屏。
3. 若缺少 Parsec VDD，程序会提示并以管理员权限安装随包携带的官方安装器。拒绝 UAC 或安装失败后，可点击界面中的“安装 Parsec VDD 驱动”重试。
4. 填入分辨率和整数刷新率，点击“保存默认值”。默认 `1920×1080 @ 60 Hz`；数值范围只是输入边界，实际必须受驱动支持。
5. 点击“熄灭物理屏幕”：先保存布局、创建或使用本程序的虚拟屏幕，确认可用后只保留该虚拟输出。点击“点亮物理屏幕”恢复原布局。
6. 勾选“保持电脑唤醒”可在程序运行期间抑制自动系统休眠，取消或退出即释放。它不阻止手动休眠、关机、合盖、断电或系统策略。
7. 可单独添加/删除虚拟显示器，最多同时管理三个。为保护恢复快照，虚拟接管期间需先点亮，再增删显示器。
8. 默认全局切换键是 **Ctrl+Alt+F10**，可在界面更改。键冲突会提示，不会覆盖其它应用。
9. 关闭窗口只是隐藏到托盘；正常退出会先恢复显示，再清理本程序的虚拟屏幕。恢复失败时拒绝退出，以免丢失当前远程桌面。

### “熄屏”的含义

为保持虚拟桌面可被远控，NightDesk 通过 **Windows 显示拓扑断开物理输出**，而不是向所有显示器广播电源关闭。大多数物理屏幕会进入无信号待机；是否立即黑屏、是否显示无信号提示由显示器固件决定。不保证面板电源状态或笔记本内屏背光行为，也不替代 Parsec / GameViewer 的屏幕流传输与键鼠控制。

## Parsec VDD 驱动随包安装

- 安装器内嵌在单文件 EXE 中，启动时若 `VddClient` 无法打开设备接口，会提取到 `%LOCALAPPDATA%\NightDesk\driver\parsec-vdd-0.45.0.0.exe`，以 `runas` + `/S` 安装。
- 执行前强制校验：大小 `517256` 字节、SHA-256 `e23332448fdaf5aa017cb308db5ef6855fac526a7ded05d80c039404126d5362`、Authenticode 主体 `Parsec Cloud, Inc.`。任一不符即拒绝执行。
- 自动安装每个用户环境只尝试一次：取消 UAC 或安装失败后不会每次启动反复弹窗；手动按钮可随时重试。
- 已安装驱动时只检测、不重装。安装失败或不授权时程序仍可打开，只是不能添加虚拟显示器。
- 载荷来源为官方 `https://builds.parsec.app/vdd/parsec-vdd-0.45.0.0.exe`；不使用 GitHub 上未签名的安装包。也可把同名安装器放到程序目录的 `vdd\` 子目录作为备用。
- 本机已安装驱动，因此没有执行卸载/重装；已验证内嵌载荷存在、大小/哈希/签名校验、提取和“已安装”检测路径。

## Tailscale HTTPS 远程控制与扫码绑定

本机 HTTPS 地址：**https://minipc-xoh4t.tail9f7f1f.ts.net/**。应用启动时动态读取地址；重命名节点后以应用显示的地址为准。

1. 确认 Tailscale 已连接，tailnet 管理面板已允许 HTTPS，客户端设备受 ACL / grants 允许。
2. 点击“启用 HTTPS 远程控制”。1.2.0 起按 Serve 路由结构判定归属，不再要求从未配对过的机器预先匹配历史指纹。
3. 点击“扫描二维码绑定”，Android 扫码后获得独立设备令牌。二维码只包含 5 分钟有效的一次性票据，不含长期令牌。
4. 设备令牌长期有效，直到桌面端在“受信任设备”列表中删除该绑定；删除后下一次请求立即 401。
5. 兼容方式仍可复制共享令牌并在网页或 Android 手工填写；轮换共享令牌不影响已绑定设备。
6. NightDesk 只创建/管理 `https://<当前节点>.ts.net:443/` 到 `http://127.0.0.1:17645` 的路由，不覆盖其它 Serve 服务，不启用 Funnel。

## Windows 桌面控制 Windows

1. 在目标桌面启用第 05 区低带宽远控，监听 Tailscale 地址，然后点击“生成配对二维码”。
2. 在配对窗口点击“复制连接信息”，取得地址、6 位配对码和 64 位主机证书指纹。
3. 在另一台 Windows 11 NightDesk 的第 06 区粘贴地址、配对码和指纹，点击“打开桌面控制窗口”。
4. 首次成功后控制端会固定主机指纹；目标端“受信任设备”会新增本控制端的客户端证书指纹。后续连接可留空配对码。
5. 目标端重装或证书变化后，控制端会拒绝连接；确认变化后先点击“清除已固定指纹”，再重新配对。

控制窗口会转发鼠标、滚轮、Ctrl/Alt 组合键和 Unicode 文本；失焦或关闭时释放按键和鼠标按钮。它不提供音频、剪贴板同步、文件传输或视频编码，QUIC 走 tailnet UDP，Tailscale Serve 不能代理数据面。

详细恢复说明：[C:\dev\NightDesk\docs\20261027-064503-windows-desktop-control-1.5.0-handoff.md](C:/dev/NightDesk/docs/20261027-064503-windows-desktop-control-1.5.0-handoff.md)
## Android 客户端

Kotlin 原生应用（`dev.nightdesk.android`，minSdk 29 / target 35，Views + HttpURLConnection + ZXing）。**构建、签名、lint、仪器测试与模拟器调试全部在 SSH 主机 `AI` 上完成**，本机不安装 Android SDK / Gradle / adb。SSH 默认进入 tmux，`main` 会话属于其它工作，禁止触碰；只使用 `nightdesk-build`、`nightdesk-emulator`。

- 扫码绑定后保存主机；Spinner 在已保存主机间切换，可删除当前主机。
- 最多保存 20 台主机，按 Tailscale origin 去重；重新扫码同一主机只更新令牌。
- 配置使用 Android Keystore AES-GCM 加密；schemaVersion 3 从旧单主机格式自动迁移。
- 状态刷新、熄屏、点亮/恢复、防休眠、虚拟屏增删。
- 启动即检查更新、每 6 小时 WorkManager 检查、手动检查；下载后验证包名、版本、minSdk、大小、SHA-256 和签名，再交给系统安装器，不静默安装。
- 控制源和更新源分离：控制地址来自扫码主机，更新源固定为 AI 服务。
- 低带宽屏幕远控：扫码识别 `nightdesk-nx`，固定 Windows sidecar 证书，使用 QUIC + TLS 1.3 接收无损 tile 差分；支持单指拖动、双指滚动、右键、组合键和 Unicode 文本。不是视频/音频串流，不适合游戏或高动态画面。

### 下载与更新地址（仅 tailnet 内可访问）

- 下载页：[https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk)
- 最新 APK：[https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/latest.apk](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/latest.apk)
- 更新清单：[https://tony-ai-pc-1.tail9f7f1f.ts.net/api/v1/android-update](https://tony-ai-pc-1.tail9f7f1f.ts.net/api/v1/android-update)

三条路由由 AI 的 `nightdesk-update-feed.service` 监听 `127.0.0.1:17768`，再由 Tailscale Serve 在 `https://tony-ai-pc-1.tail9f7f1f.ts.net` 上代理。服务本身不做 Bearer 认证，访问边界由 tailnet ACL 控制；未启用 Funnel，公网不可达。

已签名安装包（证书 SHA-256 `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`，后续版本必须保持同一签名）：

- `nightdesk-1.0.0.apk` — versionCode 1
- `nightdesk-1.0.1.apk` — versionCode 2
- `nightdesk-1.0.2.apk` — versionCode 3，SHA-256 `d1f71eb1a58c6428739b69114bb17b924b5d965fb06fae42c1e228cab1eedcb3`
- `nightdesk-1.0.3.apk` — versionCode 4，SHA-256 `ece0bb1e7bec11862d8ef7db89a9e82ee98bf56e9063a8b58c7cf33ef641a355`
- `nightdesk-1.0.4.apk` — versionCode 5，上一发布版本，大小 `6991035`，SHA-256 `906142e6abda3d193272b7f68adcb053d9cbbdd00fca40423b806e1d2979e436`
- `nightdesk-1.1.0.apk` / `nightdesk-7.apk` — versionCode 7，**当前线上版本**，大小 `55058900`，SHA-256 `01734a26365b859c831a9ba7806ff1d133b96e0993061053bb2a60ab72caf480`

## 恢复与故障排查

- 配置：`C:\Users\pixel\AppData\Local\NightDesk\settings.json`。
- 受信任设备：`C:\Users\pixel\AppData\Local\NightDesk\trusted-devices.dat`（DPAPI + 令牌哈希）。
- 异常退出恢复快照：`C:\Users\pixel\AppData\Local\NightDesk\recovery.json`。启动发现快照时不会悄悄切屏；先点击“恢复保存布局”，或通过远程面板发出恢复/点亮命令。恢复验证成功才清除快照。
- 不要在未恢复前删除虚拟屏幕、卸载驱动、强行结束 NightDesk、重启显卡驱动或改变显示器接线。Parsec VDD 依赖约 100 ms 心跳；进程崩溃或系统睡眠可能令虚拟屏幕消失。
- 若物理屏幕未恢复：在本机用 Windows 显示设置 / Win+P 重新启用物理输出；程序仍运行且可达时用快捷键或网页“点亮 / 恢复布局”。再启动 NightDesk 执行“恢复保存布局”。
- 没有活动物理显示器时拒绝“熄屏”切换，不会把其它软件的虚拟显示器误当成物理屏幕。
- 端口 17645 被占用时远程 API 启动失败，本地功能仍可用。
- 快捷键注册失败时换组合；硬件切换始终以控制台会话为准。
- 更新源异常：`systemctl status nightdesk-update-feed`、`journalctl -u nightdesk-update-feed -n 100`、`curl -fsS http://127.0.0.1:17768/healthz`。页面/APK 不一致时服务返回 503，不会暴露半发布状态。

只读诊断（不创建/删除显示器）：

```powershell
& 'C:\dev\NightDesk\artifacts\win-x64-1.5.0\NightDesk.exe' --diagnose --output 'C:\dev\NightDesk\diag-1.5.0.json'
```

## 开发 / 构建

已通过 Scoop 安装 .NET SDK **10.0.402**。Android 工具链只在 SSH 主机 `AI` 上使用。

```powershell
$dotnet = 'C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe'
& $dotnet run --project 'C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj' -c Release
& $dotnet build 'C:\dev\NightDesk\src\NightDesk\NightDesk.csproj' -c Release
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\dev\NightDesk\tools\publish-windows.ps1'
```

测试入口是无额外 NuGet 测试框架的可执行断言程序，**用 dotnet run，不是 dotnet test**。当前 203 项通过；其中桌面控制集成测试启动真实 `nx-host.exe -test-mode -source pattern -inject log`，不抓屏、不调用 SendInput，测试不改变显示配置，也不启用 Tailscale。

Android 构建与发布、更新源运维、签名密钥保管和已知限制见 `docs\HANDOFF.md`。

## 文档

- 架构、API、配对、多主机与安卓交接：[C:\dev\NightDesk\docs\HANDOFF.md](C:/dev/NightDesk/docs/HANDOFF.md)
- Windows 1.5.0 桌面控制与恢复：[C:\dev\NightDesk\docs\20261027-064503-windows-desktop-control-1.5.0-handoff.md](C:/dev/NightDesk/docs/20261027-064503-windows-desktop-control-1.5.0-handoff.md)
- go-nx 集成可行性：[C:\dev\NightDesk\docs\20261027-003801-go-nx-remote-control-integration-plan.md](C:/dev/NightDesk/docs/20261027-003801-go-nx-remote-control-integration-plan.md)
- 阶段 5 连接生命周期与恢复：[C:\dev\NightDesk\docs\20261027-033356-remote-control-stage-5-lifecycle-handoff.md](C:/dev/NightDesk/docs/20261027-033356-remote-control-stage-5-lifecycle-handoff.md)
- Android 1.1.0 发布、验证与回滚：[C:\dev\NightDesk\docs\20261027-062721-android-1.1.0-publication-handoff.md](C:/dev/NightDesk/docs/20261027-062721-android-1.1.0-publication-handoff.md)
- Windows XAML 启动崩溃修复与产物验收：[C:\dev\NightDesk\docs\20261027-061608-windows-xaml-startup-fix-handoff.md](C:/dev/NightDesk/docs/20261027-061608-windows-xaml-startup-fix-handoff.md)
- 人工验收：[C:\dev\NightDesk\docs\ACCEPTANCE.md](C:/dev/NightDesk/docs/ACCEPTANCE.md)
- 第三方说明：[C:\dev\NightDesk\docs\THIRD-PARTY-NOTICES.txt](C:/dev/NightDesk/docs/THIRD-PARTY-NOTICES.txt)
