# NightDesk · 屏幕与远程工作台

原生 Windows WPF/Win32 工作台 + Kotlin Android 客户端，不使用 Electron。提供显示拓扑切换、Parsec VDD 管理、Tailscale HTTPS 管理、go-nx 低带宽无损 tile 远控和双平台更新分发。

## 当前版本（2026-10-27 KST）

- Windows **1.6.2** / versionCode **10602**，sidecar **nx-host 0.2.1**；修复 NX viewer 将 GDI 未定义 alpha 当透明通道导致画面颜色异常。
- Android **1.3.2** / versionCode **11**；修复 NX 远控红蓝通道错位，并忽略 GDI 未定义 alpha；沿用原签名证书。
- 两份更新均已发布到 AI 服务。仅 tailnet 可达，未启用 Funnel。
- Windows 211 项断言、NX 颜色格式回归、实时更新器隔离 staging、安装器隔离验证通过；Go tests/vet、Windows amd64 交叉构建和 4 帧/538 tile/7 输入回执合成探针通过。Android 36 项 JVM 测试、6 项常规仪器测试及独立色条仪器测试通过，lint 0 errors / 23 warnings。
- **这些结果不代表真实 GDI/SendInput、DPI/UAC、双机 UDP/QUIC 或物理真机验收通过。**

## 下载与首次升级

- [双平台下载页](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk)
- [Windows 1.6.2 ZIP](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/windows/latest.zip)
- [Windows 更新清单](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/api/v1/windows-update)
- [Android 1.3.2 APK](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/latest.apk)
- [Android 更新清单](https://tony-ai-pc-1.tail9f7f1f.ts.net/api/v1/android-update)

旧 Windows 客户端没有新更新器，第一次进入 1.6.0 需手动部署 ZIP 中的 **NightDesk.exe 与 nx-host.exe 两个文件**。必须同目录；不能只换主程序。1.6.0 之后启动时和每 6 小时自动检查并下载更高版本，安装前由用户确认“安装并重启”。不静默安装、不强杀主程序。helper 保留 .bak-update，目标目录需要可写。已有 1.6.0/1.6.1 客户端可下载 1.6.2 更新。

## 功能在哪

| Windows 分页 | 内容 |
|---|---|
| 概览 | 状态、熄屏/点亮、恢复、刷新、防休眠 |
| 显示 | 分辨率/刷新率、虚拟显示器增删、VDD 驱动、快捷键 |
| 远程访问 | Tailscale HTTPS、扫码绑定、设备撤销、本机 NX 被控 |
| 控制桌面 | 输入远端地址、配对码、指纹，控制另一台 Windows |
| 更新与诊断 | 桌面自动下载、安装并重启、诊断、退出 |

Android 使用常驻底部导航：**首页 / 显示 / 远控 / 更新**。更新状态独立显示，未绑定电脑也可使用默认更新源；处理 Android 状态栏/导航栏遮挡，并保存旋转前的选中页。

## Windows → Windows / Android → Windows

1. 被控机在“远程访问”页启用低带宽远控，监听其 Tailscale IPv4:端口（例如 100.x.y.z:7000）。
2. 生成配对二维码；Android 可扫码，桌面端可复制地址、6 位配对码和主机证书指纹。
3. 控制桌面在“控制桌面”页输入这些信息并连接。首次成功后保存主机指纹；之后证书变化会拒绝连接。已配对设备可留空配对码。
4. 失焦、断开或关闭时释放输入。WPF 控制窗口当前要求 Windows 11 / Server 2022+ 的 System.Net.Quic。

NX 是面向文字/静态桌面的 QUIC + TLS 1.3 + 64×64 tile 数据面，不是视频串流。没有音频、视频编解码、剪贴板同步、文件传输或多显示器热切换。数据面走 tailnet UDP，Tailscale Serve 不能代理它。

HTTPS 管理面与 NX 数据面独立：HTTPS 使用扫码获得的独立长期令牌；撤销设备后下一次请求立即失败。服务只管理归属明确的 Serve 路由，不覆盖其它服务。

## 现场安全

08:07 KST 只读检查：Desktop NightDesk PID 23116、正式 nx-host PID 20232 正在运行；监听 100.109.77.29:7000，primary / 10 FPS。恢复快照存在（352 字节）。本轮没有替换或重启它们。PID 会变化，不能作为后续终止指令。

- 在确认活动物理输出、DPI/VDD 和快照可恢复之前，不切屏、不恢复快照、不增删 VDD、不运行真实抓屏/SendInput。
- 先通过旧应用 UI 正常退出并确认恢复，才能手动部署 1.6.0；不要直接覆盖运行文件或强杀。
- “熄屏”指保存并切换 Windows 显示拓扑，不保证所有显示器的背光/无信号提示行为。其它软件的虚拟屏不能冒充安全物理输出。
- Parsec VDD 安装器随包携带，执行前核对大小、SHA-256 和 Parsec Cloud 签名；已安装时不重装。

## 产物与完整性

| 产物 | 大小 | SHA-256 |
|---|---:|---|
| NightDesk.exe 1.6.2 | 201922308 | `cc986ac254941ed6029a46d5ae65978d5233e82904471e1145670edcf067403f` |
| nx-host.exe 0.2.1 | 7940608 | `88233ba806d9a7ebe4bda5c66a2e4c61e0d9dc9b43c48787170ca68e6087b9f0` |
| Windows ZIP 1.6.2 | 85184082 | `9856a7c03b9dff7b5ed89fbf106a597add1ec983a8f1b43db340fbfd13694717` |
| Android APK 1.3.2 | 55076236 | `1f5fc894074d468f92b6f19f0fbea64be1f7c55c1df696544690d630a0206118` |

Windows ZIP 目前无 Authenticode 签名；HTTPS/tailnet ACL 和 SHA-256 一致性校验不替代独立代码签名。Android 保持原发布证书 `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`。

本地：`C:\dev\NightDesk\artifacts\win-x64-1.6.2`、`C:\dev\NightDesk\artifacts\windows-packages\nightdesk-windows-1.6.2.zip`、`C:\dev\NightDesk\artifacts\android\nightdesk-1.3.2.apk`。

## 开发与恢复

Windows 使用 .NET SDK 10.0.402。测试入口是可执行断言程序，使用 dotnet run，不是 dotnet test。

```powershell
dotnet run --project C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj -c Release
dotnet run --project C:\dev\NightDesk\tools\layout-smoke\layout-smoke.csproj -c Release -- C:\dev\NightDesk
powershell.exe -NoProfile -File C:\dev\NightDesk\tools\publish-windows.ps1
powershell.exe -NoProfile -File C:\dev\NightDesk\tools\package-windows.ps1 -Version 1.6.2 -VersionCode 10602
powershell.exe -NoProfile -File C:\dev\NightDesk\tools\test-desktop-update-install.ps1
```

Android SDK/JDK/Gradle/adb/emulator/签名只在 SSH 主机 AI 使用；当前构建目录 `/home/tony/dev/nightdesk-android-1.3.0`。不触碰其它工作 tmux 会话。

- 配置/快照：`%LOCALAPPDATA%\NightDesk`。
- 桌面更新缓存：`%LOCALAPPDATA%\NightDesk\updates\windows`；安装日志 install.log。
- AI 更新服务：nightdesk-update-feed.service；healthz 返回 ok android=ok windows=ok。

## 文档

- [NX 颜色修复、双端 1.6.2/1.3.2 发布与恢复说明](C:/dev/NightDesk/docs/20261027-163807-nx-color-fix-dual-release-1.6.2-1.3.2-handoff.md)
- [本轮远控光标修复、Windows 1.6.1 与恢复说明](C:/dev/NightDesk/docs/20261027-123458-windows-cursor-capture-1.6.1-handoff.md)
- [分页 UI、更新分发、验收、回滚与现场边界](C:/dev/NightDesk/docs/20261027-080919-paginated-ui-desktop-update-1.6.0-handoff.md)
- [综合交接](C:/dev/NightDesk/docs/HANDOFF.md)
- [验收记录](C:/dev/NightDesk/docs/ACCEPTANCE.md)
- [第三方说明](C:/dev/NightDesk/docs/THIRD-PARTY-NOTICES.txt)
