# NightDesk 分页 UI 与双平台更新发布交接

更新时间：2026-10-27 08:09 KST（Asia/Seoul）

实现提交：`93147b357a9b8497cd2be4c67d3af99668279724`。

**Windows 1.6.0 与 Android 1.2.0 已发布到 AI 更新源。生产桌面没有被本轮替换或重启。**

## 1. UI 改动与问题根因

旧桌面表单的部分 StackPanel 没有指定 Grid.Column，导致监听地址、显示器、帧率及 Viewer 连接字段叠在第一列。现已补齐列分配，并加入源码断言与实际布局边界检查。

Windows 页面：

| 页面 | 功能 |
|---|---|
| 概览 | 当前状态、熄屏/点亮、恢复、刷新、防休眠 |
| 显示 | 分辨率、刷新率、虚拟显示器、驱动、快捷键 |
| 远程访问 | HTTPS 管理、扫码绑定、本机 NX 被控、受信任设备 |
| 控制桌面 | 输入另一台电脑的地址、配对码、指纹 |
| 更新与诊断 | 自动下载状态、安装并重启、诊断、退出 |

Android 使用常驻底部四页导航：首页、显示、远控、更新。统一卡片和输入间距、48dp 导航按钮；处理状态栏/导航栏 inset；保存旋转前的选中页。更新状态显示在更新页，未绑定电脑也可以检查独立更新源。

## 2. 下载地址（仅 tailnet）

- 双平台页面：[更新源](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk)
- Windows ZIP：[最新桌面包](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/windows/latest.zip)
- Windows 清单：[windows-update](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/api/v1/windows-update)
- Android APK：[最新 APK](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/latest.apk)
- Android 清单：[android-update](https://tony-ai-pc-1.tail9f7f1f.ts.net/api/v1/android-update)

服务仍监听 AI 的 `127.0.0.1:17768`；使用已有 /nightdesk Serve 路由，不修改其它路由、不启用 Funnel。兼容代理移除 /nightdesk 前缀后的请求路径。大文件使用流式发送，不整体加载到服务内存。

## 3. 桌面自动更新流程

1. 1.6.0 启动后检查一次，此后每 6 小时检查。
2. 有更高版本时自动下载 ZIP，缓存到 `%LOCALAPPDATA%\NightDesk\updates\windows`。
3. 核对包名、版本、架构、长度、ZIP SHA-256、包内元数据、两个 EXE 的大小和 SHA-256。
4. 校验通过后显示“安装并重启”；不静默安装。
5. 用户确认后再次校验文件并启动隐藏 PowerShell helper。
6. 主程序按现有流程停止 sidecar、恢复/清理显示并正常退出。helper 等待最多 60 秒，未退出则放弃安装，不强杀主程序。
7. helper 再核对暂存文件，保存 `.bak-update`，替换 NightDesk.exe 和 nx-host.exe；替换失败时尝试恢复备份；成功后启动新版。

**1.5.0 及更早版本没有此更新器，首次进入 1.6.0 仍需手动部署 ZIP 中两个 EXE。** 之后的更高版本才会由新更新器自动下载。

Windows 产物目前没有 Authenticode 签名（本轮检测结果 NotSigned）。HTTPS、tailnet ACL 与 SHA-256 提供传输和一致性保障，不等同于独立签名发布；服务器信任仍是更新信任链的一部分。helper 不自动提权，安装目录必须可写。

## 4. 产物标识

| 产物 | 大小（字节） | SHA-256 |
|---|---:|---|
| NightDesk.exe 1.6.0 | 201922308 | `d5c1c09832ad80d638fe27af9e68c41b8ff4e4dd0b5dcb89faabc2559be72e5d` |
| nx-host.exe 0.2.0 | 7938048 | `17b420ce531270e28bef58d42dd68f5c13cac6c5fe4cbfd4fdbb20051ad35e3` |
| Windows ZIP 1.6.0 | 85183345 | `de162ec69f0f9c9d2e855fc7a084b1c316b4d78284be5b131fd393ec461004cb` |
| Android APK 1.2.0 | 55058900 | `82136d2992ff30c3bf84a5d45a02bc04bda4d38203880193883abb747100c52f` |

Windows ProductVersion：`1.6.0+93147b357a9b8497cd2be4c67d3af99668279724`；versionCode 10600。
Android：`dev.nightdesk.android`，versionCode 8，minSdk 29 / targetSdk 35。
Android 签名证书 SHA-256 保持为 `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`。

本地产物：
- `C:\dev\NightDesk\artifacts\win-x64-1.6.0`
- `C:\dev\NightDesk\artifacts\windows-packages\nightdesk-windows-1.6.0.zip`
- `C:\dev\NightDesk\artifacts\android\nightdesk-1.2.0.apk`

## 5. 验证证据

- Windows Release：210 tests passed. No display state changed.
- Windows 串行 Release 构建：0 warnings / 0 errors。
- 纯 XAML smoke：980/1180 DIP × 5 页；NX host/viewer 各三字段互不重叠，宽高均非零。不启动应用服务或显示操作。
- 安装 helper：实际脚本在临时目录等待自有测试进程、替换两个合成文件、保留备份、重新启动合成 WinExe。不是生产升级测试。
- Windows 实际下载器：以隔离缓存和旧版本比较值从真实 HTTPS 下载 1.6.0，校验 ZIP、解压、校验两个 EXE 并形成 ready；未调用安装。
- AI Android：CoreTest 10 + NxConnectionTest 10 + NxTest 7 = 27 JVM tests；lint 0 errors / 22 warnings。
- Android 仪器：HostStore 3 + MainUi 1 + UpdateFlow 1 = OK (5 tests)。发布后 UpdateFlow 再跑 OK (1 test)。
- Python feed：5 tests，包含 Android 兼容路由、Windows manifest/ZIP、双平台页面、代理前缀兼容。
- HTTPS 清单版本/大小/哈希与本地、AI 产物一致；latest.apk 与 latest.zip HEAD 均 HTTP 200。
- 服务 active / enabled；healthz 返回 `ok android=ok windows=ok`。

布局证据目录：`C:\dev\NightDesk\artifacts\ui-1.6.0`、`C:\dev\NightDesk\artifacts\android`。截图是界面/合成证据，不证明真实显示控制通过。

## 6. 构建与发布恢复

Windows 构建/测试：

```powershell
dotnet run --project C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj -c Release
dotnet run --project C:\dev\NightDesk\tools\layout-smoke\layout-smoke.csproj -c Release -- C:\dev\NightDesk
powershell.exe -NoProfile -File C:\dev\NightDesk\tools\publish-windows.ps1
powershell.exe -NoProfile -File C:\dev\NightDesk\tools\package-windows.ps1 -Version 1.6.0 -VersionCode 10600
powershell.exe -NoProfile -File C:\dev\NightDesk\tools\test-desktop-update-install.ps1
```

不要重建并覆盖已发布的相同版本 ZIP；发布脚本拒绝同版本不同内容。修复应提高版本号。

Android 构建目录：`AI:/home/tony/dev/nightdesk-android-1.2.0`。
使用 build-ai.sh 与 build-androidtest.sh，参数为 `-PnightdeskVersionCode=8 -PnightdeskVersionName=1.2.0`。所有 SDK、签名、Gradle、adb、emulator 操作仅在 AI 上进行，不触碰别人的 main tmux 会话。

服务目录：`AI:/home/tony/dev/nightdesk-update-feed`。
Windows 不可变文件：`windows/nightdesk-windows-1.6.0.zip`；Android 不可变文件：`nightdesk-8.apk`。
发布顺序：包先落盘校验，manifest 最后同目录原子切换；新 server.py 在两份清单存在后重启服务。

本轮备份：
- `server.py.bak-20261027-080511`
- `manifest.json.pre-1.2.0-20261027-080511`

回滚服务时可恢复备份 server.py 和 Android manifest 后重启；旧 Windows manifest 在首次 Windows 发布前不存在。撤回 Windows 更新时可暂时移开 windows-manifest.json（客户端显示更新不可用）；保留 ZIP/APK 供审计。恢复清单必须写临时文件后原子替换。Android 不允许自动降级。

## 7. 当前现场边界

08:07 KST 只读复核：Desktop NightDesk PID 23116、正式 nx-host PID 20232；监听 `100.109.77.29:7000`，primary / 10 FPS，生产状态目录。恢复快照存在，352 字节，修改时间 07:03:42。PID 会随用户操作变化，不要硬编码去终止它们。

本轮没有替换 Desktop 文件，没有强杀/重启正式进程，没有执行熄屏/点亮/恢复快照/VDD 操作。部署前须确认物理显示可恢复，通过 UI 正常退出旧程序，再同时更新两个 EXE。

仍未验收：生产现场自更新、真实 GDI/SendInput、DPI/UAC/安全桌面、双机 tailnet UDP/QUIC、Android 物理真机、长期资源占用。不要把发布成功、合成输入或 UI 测试写成这些项目已经通过。
