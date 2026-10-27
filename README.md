# NightDesk · 屏幕与远程工作台

原生 Windows WPF/Win32 工作台 + Kotlin Android 客户端，不使用 Electron。提供显示拓扑切换、Parsec VDD 管理、Tailscale HTTPS 管理、go-nx 低带宽无损 tile 远控和双平台更新分发。

## 当前版本（2026-10-28 KST）

- Windows **1.6.3** / versionCode **10603**，sidecar **nx-host 0.2.1**；NX 远控改为可折叠侧栏，不再常驻遮挡画面的上下控制条。
- Android **1.3.3** / versionCode **12**；NX 远控改为全屏画面与边缘侧栏，支持 F1 菜单、F2 手机键盘及全量释放。
- 两份更新均已发布到 AI 服务。仅 tailnet 可达，未启用 Funnel。
- 本机与 AI 更新源的正式发行目录现在只保留 Windows 1.6.3 与 Android 1.3.3；测试 APK、截图、日志、清单备份和恢复材料继续保留。旧版本定向下载地址已返回 404，latest 更新地址不变。
- Windows 215 项断言、Release 构建 0 warnings / 0 errors；Android 36 项 JVM 测试、仪器/UI 与更新验证、lint 0 errors / 23 warnings 通过。模拟器已安装 Android code 12。
- 被控端物理光标闪烁尚未在隔离 Windows 主机上复现；本轮未改光标显示策略。真实 GDI/SendInput、DPI/UAC、双机 UDP/QUIC 和 Android 真机体验仍需现场验收。

## 下载与首次升级

- [双平台下载页](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk)
- [Windows 1.6.3 ZIP](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/windows/latest.zip)
- [Windows 更新清单](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/api/v1/windows-update)
- [Android 1.3.3 APK](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/latest.apk)
- [Android 更新清单](https://tony-ai-pc-1.tail9f7f1f.ts.net/api/v1/android-update)

发行目录清理、保留清单、SHA-256 和回滚边界见：[2026-10-28 发行目录清理交接](C:/dev/NightDesk/docs/20261028-065453-release-directory-cleanup-handoff.md)。

旧 Windows 客户端没有新更新器，第一次进入 1.6.0 需手动部署 ZIP 中的 **NightDesk.exe 与 nx-host.exe 两个文件**。必须同目录；不能只换主程序。1.6.0 之后启动时和每 6 小时自动检查并下载更高版本，安装前由用户确认“安装并重启”。不静默安装、不强杀主程序。helper 保留 .bak-update，目标目录需要可写。已有 1.6.0 及之后版本可下载 1.6.3 更新。

## 功能在哪

| Windows 分页 | 内容 |
|---|---|
| 概览 | 状态、熄屏/点亮、恢复、刷新、防休眠 |
| 显示 | 分辨率/刷新率、虚拟显示器增删、VDD 驱动、快捷键 |
| 远程访问 | Tailscale HTTPS、扫码绑定、设备撤销、本机 NX 被控 |
| 控制桌面 | 输入远端地址、配对码、指纹，控制另一台 Windows |
| 更新与诊断 | 桌面自动下载、安装并重启、诊断、退出 |

Android 主界面使用常驻底部导航：**首页 / 显示 / 远控 / 更新**。NX 会话使用全屏画面和窄边缘菜单入口；点开后显示侧栏。硬件键盘 **F1** 开关侧栏，**F2** 呼出手机输入法，Esc 收起；打开/关闭菜单前会释放远端输入。Windows viewer 同样以 F1 开关侧栏，远端 F1 有单独发送按钮。

## Windows → Windows / Android → Windows

1. 被控机在“远程访问”页启用低带宽远控，监听其 Tailscale IPv4:端口（例如 100.x.y.z:7000）。
2. 生成配对二维码；Android 可扫码，桌面端可复制地址、6 位配对码和主机证书指纹。
3. 控制桌面在“控制桌面”页输入这些信息并连接。首次成功后保存主机指纹；之后证书变化会拒绝连接。已配对设备可留空配对码。
4. 失焦、断开或关闭时释放输入。WPF 控制窗口当前要求 Windows 11 / Server 2022+ 的 System.Net.Quic。

被控 Windows 主机的物理系统指针可能随 `SendInput` 移动；抓帧时 `DrawIconEx` 只绘制到离屏缓冲区。代码中未发现显式隐藏/显示指针调用，但“物理屏幕闪烁”尚未实机复现，不能仅凭源码判定原因；当前保持原策略，待隔离 Windows 主机复现后再决定是否修改。

NX 是面向文字/静态桌面的 QUIC + TLS 1.3 + 64×64 tile 数据面，不是视频串流。没有音频、视频编解码、剪贴板同步、文件传输或多显示器热切换。数据面走 tailnet UDP，Tailscale Serve 不能代理它。

HTTPS 管理面与 NX 数据面独立：HTTPS 使用扫码获得的独立长期令牌；撤销设备后下一次请求立即失败。服务只管理归属明确的 Serve 路由，不覆盖其它服务。

## 现场安全

历史记录：2026-10-27 08:07 KST 曾只读观察到正式 NightDesk/NX host 和恢复快照；PID 与显示状态可能已变化，不可作为当前状态或终止指令。本阶段未操作生产进程、恢复快照或显示拓扑；实际部署前必须重新只读检查现场。

- 在确认活动物理输出、DPI/VDD 和快照可恢复之前，不切屏、不恢复快照、不增删 VDD、不运行真实抓屏/SendInput。
- 旧版本首次进入 1.6.0 时，先通过旧应用 UI 正常退出并确认恢复，再手动部署同目录两个 EXE；后续通过应用提供的更新流程。不要直接覆盖运行文件或强杀。
- “熄屏”指保存并切换 Windows 显示拓扑，不保证所有显示器的背光/无信号提示行为。其它软件的虚拟屏不能冒充安全物理输出。
- Parsec VDD 安装器随包携带，执行前核对大小、SHA-256 和 Parsec Cloud 签名；已安装时不重装。

## 产物与完整性

| 产物 | 大小 | SHA-256 |
|---|---:|---|
| NightDesk.exe 1.6.3 | 201926404 | `aa60d46e87fee8e057c7b8172340642a1959425404f4e873c8c7811ed9ecd526` |
| nx-host.exe 0.2.1 | 7940608 | `88233ba806d9a7ebe4bda5c66a2e4c61e0d9dc9b43c48787170ca68e6087b9f0` |
| Windows ZIP 1.6.3 | 85184688 | `3214419d6ed87d95111a1c1c2e719307b8cc046ad624ceeebd09b0310243ebc7` |
| Android APK 1.3.3 | 55076236 | `61f967a0d53d2fdbcf6fde6659404216cb804db847ff801576e22c2da9108e9f` |

Windows ZIP 目前无 Authenticode 签名；HTTPS/tailnet ACL 和 SHA-256 一致性校验不替代独立代码签名。Android 保持原发布证书 `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`。

本地：`C:\dev\NightDesk\artifacts\win-x64-1.6.3`、`C:\dev\NightDesk\artifacts\windows-packages\nightdesk-windows-1.6.3.zip`、`C:\dev\NightDesk\artifacts\android\nightdesk-1.3.3.apk`。

## 开发与恢复

Windows 使用 .NET SDK 10.0.402。测试入口是可执行断言程序，使用 dotnet run，不是 dotnet test。

```powershell
dotnet run --project C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj -c Release
dotnet run --project C:\dev\NightDesk\tools\layout-smoke\layout-smoke.csproj -c Release -- C:\dev\NightDesk
powershell.exe -NoProfile -File C:\dev\NightDesk\tools\publish-windows.ps1
powershell.exe -NoProfile -File C:\dev\NightDesk\tools\package-windows.ps1 -Version 1.6.3 -VersionCode 10603
powershell.exe -NoProfile -File C:\dev\NightDesk\tools\test-desktop-update-install.ps1
```

Android SDK/JDK/Gradle/adb/emulator/签名只在 SSH 主机 AI 使用；当前构建目录 `/home/tony/dev/nightdesk-android-ui-20261027/android`。不触碰其它工作 tmux 会话。

- 配置/快照：`%LOCALAPPDATA%\NightDesk`。
- 桌面更新缓存：`%LOCALAPPDATA%\NightDesk\updates\windows`；安装日志 install.log。
- AI 更新服务：nightdesk-update-feed.service；healthz 返回 ok android=ok windows=ok。

## 文档

- [远控侧栏、光标闪烁调查与双端 1.6.3/1.3.3 恢复说明](C:/dev/NightDesk/docs/20261027-201142-nx-sidebar-cursor-review-1.6.3-1.3.3-handoff.md)
- [发行目录清理与最新分发保留](C:/dev/NightDesk/docs/20261028-065453-release-directory-cleanup-handoff.md)
- [NX 颜色修复、双端 1.6.2/1.3.2 发布与恢复说明](C:/dev/NightDesk/docs/20261027-163807-nx-color-fix-dual-release-1.6.2-1.3.2-handoff.md)
- [本轮远控光标修复、Windows 1.6.1 与恢复说明](C:/dev/NightDesk/docs/20261027-123458-windows-cursor-capture-1.6.1-handoff.md)
- [分页 UI、更新分发、验收、回滚与现场边界](C:/dev/NightDesk/docs/20261027-080919-paginated-ui-desktop-update-1.6.0-handoff.md)
- [综合交接](C:/dev/NightDesk/docs/HANDOFF.md)
- [验收记录](C:/dev/NightDesk/docs/ACCEPTANCE.md)
- [第三方说明](C:/dev/NightDesk/docs/THIRD-PARTY-NOTICES.txt)
