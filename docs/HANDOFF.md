# NightDesk 交接文档

更新时间：2026-10-27（Asia/Seoul）
目标平台：Windows x64（.NET 10 / WPF）+ Android（Kotlin，minSdk 29 / target 35）
当前实现：Windows 1.6.1 / nx-host 0.2.1 / Android 1.3.1；Go/Android 实现提交 `4a9930bbadb9b58a873307088914881636b71ac3`
最新阶段交接：`docs\20261027-134609-android-mouse-release-coordinate-1.3.1-handoff.md`

## 1. 当前交付状态

### Windows 桌面端 1.6.0 历史发布基线

- 发布目录：`C:\dev\NightDesk\artifacts\win-x64-1.6.0`
- `NightDesk.exe`：`201922308` 字节，SHA-256 `D5C1C09832AD80D638FE27AF9E68C41B8FF4E4DD0B5DCB89FAABC2559BE72E5D`，ProductVersion `1.6.0+93147b357a9b8497cd2be4c67d3af99668279724`；Windows ZIP 大小 `85183345`，SHA-256 `DE162EC69F0F9C9D2E855FC7A084B1C316B4D78284BE5B131FD393EC461004CB`。
- `nx-host.exe`：`7938048` 字节，SHA-256 `17B420CE531270E28BEF58D42DD68F5C13CAC6C5FE4CBFD4FDDBB20051AD35E3`，`-version` 输出 `nx-host 0.2.0`。
- 这两个文件必须同目录部署；`nx-host.exe` 是独立 Go sidecar，刻意不并入 single-file 主程序。
- Release 测试：**210 项通过**，输出 `No display state changed.`；包含布局、更新清单、实际 HTTPS 下载与真实 test-mode NX 集成验证。
- Release 构建：**0 警告 / 0 错误**
- 发布必须使用 `tools\publish-windows.ps1`。脚本从全新 staging 目录发布、断言两份文件都存在、执行 sidecar 冒烟检查，再计算大小与 SHA-256。
- 已实现：熄屏/点亮、VDD 增删与分辨率/刷新率、防休眠、可修改全局快捷键、Tailscale HTTPS 远程控制、扫码配对、永久设备令牌、受信任设备管理、统一图标、随包 VDD 自动安装，以及 Windows 桌面控制 Windows。
- WPF 五分页为概览、显示、远程访问、控制桌面、更新与诊断；Windows 控制端位于“控制桌面”页，支持目标地址、6 位配对码、可选主机指纹、固定主机 pin 和独立 WPF 控制窗口；失焦/关闭释放输入。依赖 `System.Net.Quic`，当前要求 Windows 11 / Server 2022 或更新版本。
- 控制窗口代码：`NxViewerProtocol.cs`、`NxViewerSession.cs`、`NxViewerWindow.cs`；本地 Viewer 状态在 `%LOCALAPPDATA%\NightDesk\nx-viewer`。
- 远程控制新增 go-nx 低带宽数据面：Windows 启动受监控的 `nx-host.exe` sidecar，生成含证书指纹和短期配对码的二维码；Android 原生查看器可扫码、固定证书并绘制 tile。阶段 5 已完成连接状态机、超时/重试/重新配对、后台释放输入和旧回调隔离；构建、单测、仪器测试和模拟器冒烟通过。阶段 7 新增 `nx-probe` 安全端到端验收：用与 Android 相同的 pinned TLS / 配对 / QUIC / tile 解码路径，在 1920×1080 合成动态源上验证 4 帧、538 个 tile 更新和 7/7 输入回执，不抓屏、不调用 SendInput。**真实 GDI 抓屏、SendInput 与真机 tailnet 仍未验收**。详见 `docs/20261027-041844-remote-control-stage-7-safe-e2e-probe-handoff.md`。

### Windows 1.6.1 当前光标可见性修复

- 更新包已发布到 AI 更新源：1.6.1 / 10601，ZIP 85,184,039 字节，SHA-256 `ae3de5075dca53b13d55d36409520c08f89fa405bb3d5fbcf1f67e624e64b7ae`；host 0.2.1。
- 根因：Android touch → `Session.move` → `KindMouseMove` → Windows `SendInput` 已有路径；但 Windows GDI `BitBlt` 捕获不包含系统 cursor，Android 只画接收到的帧 tiles，用户因此看不到远端指针。
- `Screen.Grab` 现在通过 `GetCursorInfo` / `GetIconInfo` / `DrawIconEx` 把 host cursor 合成进抓屏帧。合成发生在 tile diff 之前，远端指针位置更新将随相关 tile 到达手机和 Windows viewer。协议及 APK 均无需修改。
- Go 单测/vet 与 Windows amd64 交叉构建通过；合成 log-only QUIC 探针通过，210 项 Windows suite 通过；AI 更新清单与 HTTPS HEAD 已复核。
- Windows 1.6.1 发布当时 Android 为 1.3.0 / code 9，因此该 Windows 捕获修复本身不要求重发 APK；之后确认的鼠标释放坐标问题已由 Android 1.3.1 修复，见下一节。
- **仍未通过真实设备验收**：物理 Windows `SendInput`、实时 GDI 指针像素、Android 真机/双机 tailnet、不同 DPI/UAC。未修改生产桌面进程或显示器状态。

### Android 1.3.1 鼠标释放坐标修复（versionCode 10）

- 修复松开鼠标按钮、全量释放或滚轮输入后指针跳回左上角的问题。Go session 现在保存最近一次成功发送的鼠标位置，并在共享输入锁下将其附带到 MouseDown、MouseUp、Wheel 和 ReleaseAll 的 MouseUp 消息。
- 根因是这些消息原先仅携带按钮/滚轮参数，坐标默认为 `(0,0)`；Windows 注入端会按鼠标消息坐标定位，再执行对应动作。
- 回归测试先验证旧行为确实复位到 `(0,0)`，修复后验证常规按下/抬起、滚轮与 `ReleaseAll` 均保留非零坐标。
- AI Go：`go test ./...`、`go test -race ./mobile`、`go vet ./...`、Windows amd64 cross-vet 通过。gomobile AAR 已重建并用于 Android 构建。
- Android：36 JVM tests、Release/Debug/AndroidTest build、lint 通过；6 项仪器测试通过。发布后在模拟器从 Android 1.3.0/code 9 通过线上更新源下载、哈希校验并升级至 1.3.1/code 10。
- APK：55,076,236 字节，SHA-256 `11496d15cd997cff45e37637a828548a41c396fdfeb403d948839424eb3f0311`；AAR SHA-256 `bfa20670742caace05c940fd68fc790b3bf49b6a431d8f2268712936d08f22ad`。沿用原 Android 签名证书。
- AI 更新源已发布 code 10；manifest、HTTPS HEAD、实际 APK 下载与本地/AI 哈希均验证通过，服务健康 `ok android=ok windows=ok`。
- 真实 Windows 输入未在生产桌面执行；Android 真机 + 隔离 Windows host 的 `SendInput`、DPI 和屏幕边缘行为仍需现场验收。详细恢复命令见 `docs\20261027-134609-android-mouse-release-coordinate-1.3.1-handoff.md`。

### Android 1.3.0 已发布版本（versionCode 9）

- AI 不可变文件 `nightdesk-9.apk`；本地产物 `C:\dev\NightDesk\artifacts\android\nightdesk-1.3.0.apk`。
- 大小 55075280 字节；SHA-256 `9e278bafb126b18963764fd829414075c30ba9ee795b39d4ad2f1db443d7aa42`。
- 发布证书保持 `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`。
- 在 1.2.0 的四分页和安全连接流程上，远控手势调整为轻点单击、单指滑动移动光标、长按后拖动、双指滚动；增加常驻“释放”按钮、取消/失焦/退后台释放和系统栏安全区。
- 36 JVM tests：CoreTest 10 + NxConnectionTest 10 + NxTest 7 + RemoteGestureTest 9；lint 0 errors / 23 warnings。
- 6 instrumented tests：HostStore 3 + MainUi 1 + RemoteControlUI 1 + UpdateFlow 1；发布后模拟器从 code 8 更新到 code 9 的长度、SHA-256、签名和安装验证通过。
- 1.2.0 / code 8 及更早版本仍作为历史 APK 保留。真实 Android 手机连接 Windows 后的 GDI / SendInput 仍需独立测试机验收。
- 1.1.0、1.0.4 产物与历史清单保留；Android 不会接受自动降级。
- 物理真机、真实 GDI/SendInput、双机 tailnet UDP/QUIC 仍未验收。


### Android 1.0.4 历史版本（归档）

- 产物：`C:\dev\NightDesk\artifacts\android\nightdesk-1.0.4.apk`
- versionCode：**5**
- 大小：`6991035` 字节
- SHA-256：`906142e6abda3d193272b7f68adcb053d9cbbdd00fca40423b806e1d2979e436`
- 签名证书 SHA-256：`41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`
- 支持最多 20 台主机保存、切换、删除；schemaVersion 3；Keystore AES-GCM。
- 保留在 AI 更新源，供旧客户端下载和清单回滚参考；Android 客户端不会接受降级。

### 在线地址（仅 tailnet）

- 下载页：`https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk`
- Windows ZIP：`https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/windows/latest.zip`
- Windows manifest：`https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/api/v1/windows-update`
- 最新 APK：`https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/latest.apk`
- 更新清单：`https://tony-ai-pc-1.tail9f7f1f.ts.net/api/v1/android-update`
- 版本下载：`https://tony-ai-pc-1.tail9f7f1f.ts.net/api/v1/android-apk?versionCode=10`
- 桌面控制：`https://minipc-xoh4t.tail9f7f1f.ts.net/`

未启用 Funnel；访问边界由 tailnet ACL 控制。

## 2. 现场安全状态（最高优先级）

- 2026-10-27 08:07 KST 只读检查：Desktop NightDesk PID 23116、正式 nx-host PID 20232 正在运行；监听 100.109.77.29:7000，primary / 10 FPS，使用正式状态目录。
- 恢复快照存在，352 字节，修改时间 07:03:42。物理输出和快照恢复尚未独立验收。
- 本轮没有替换 Desktop 文件、强杀/重启正式进程、切屏、恢复快照、增删 VDD 或执行真实抓屏/SendInput。
- PID 会随用户操作变化，不要据此强杀进程。首次升级 1.6.0 必须确认现场可恢复后通过 UI 正常退出，再部署同目录两个 EXE。
- 在恢复未验证前，不切屏、不恢复快照、不操作 VDD、不运行第二个正式 host。合成测试必须使用 -test-mode -source pattern -inject log。


## 3. 需求与实现映射

| 需求 | 实现 | 状态 |
|---|---|---|
| 一键关闭当前显示屏 | `DisplayController.cs`、`DisplayTopology.cs` | 已完成 |
| 添加/删除虚拟显示器，可配分辨率刷新率 | `VddClient.cs`、`MainWindow.xaml(.cs)` | 已完成 |
| 勾选期间不休眠 | `PowerAndHotkey.cs` | 已完成 |
| 熄屏后启用虚拟显示器供远控 | `DisplayController.cs` 拓扑切换 | 已完成 |
| 可修改的熄屏/点亮快捷键 | `HotkeySpec.cs`、`PowerAndHotkey.cs` | 已完成 |
| Tailscale HTTPS 远程控制 | `RemoteServer.cs`、`TailscaleService.cs`、`remote.html` | 已完成 |
| Android 开发计划写入交接文档 | 本文第 11 节 | 已完成 |
| Android 多主机管理 | `Core.kt`、`MainActivity.kt` | 已完成 |
| 无 VDD 时自动安装，安装包携带驱动 | `VddInstaller.cs`、EmbeddedResource | 已完成 |
| Windows go-nx sidecar 与监控选择 | `NxHostService.cs`、`NxHostClient.cs`、`native/cmd/nx-host` | 已实现，真实采集待验收 |
| Android NX 扫码/固定证书/tile 查看器 | `Nx.kt`、`RemoteActivity.kt`、`RemoteScreenView.kt`、`mobile.aar` | 已构建，真实端到端待验收 |
| NX 连接生命周期与可恢复失败 | `NxConnection.kt`、`RemoteActivity.kt` | 已完成，真实端到端待验收 |
| NX 音频、剪贴板、多显示器切换 | 未开始 | 未实现 |

## 4. 目录与关键文件

```text
C:\dev\NightDesk\
  src\NightDesk\
    MainWindow.xaml(.cs)        WPF UI、托盘、操作编排
    DisplayController.cs        熄屏/点亮/恢复
    DisplayTopology.cs          DisplayConfig 快照与应用
    VddClient.cs                Parsec VDD IOCTL 与心跳
    VddInstaller.cs             内嵌驱动检测、校验、提权安装
    NativeDisplays.cs           显示器枚举与硬件判定
    PowerAndHotkey.cs           防休眠、全局热键
    HotkeySpec.cs               热键解析/校验
    RemoteServer.cs             loopback HTTP API / 网页
    TailscaleService.cs         Serve 路由读取、启用、关闭
    RemoteAuth.cs               配对票据、设备令牌、设备注册表
    PairingWindow.cs            HTTPS 绑定二维码窗口
    NxPairingWindow.cs          NX 地址/指纹/代码二维码窗口
    NxHostService.cs            nx-host sidecar 生命周期
    NxHostClient.cs             JSON-line IPC 与进程作业对象
    NxPairing.cs                NX 配对载荷与 Tailscale 地址校验
    QrImages.cs                 公共二维码渲染
    SettingsStore.cs            DPAPI 配置与恢复快照
    Assets\nightdesk.ico        Windows 图标
    Assets\vdd\...exe           Parsec VDD 官方安装器
  android\                      Kotlin 客户端源码（含 libs/mobile.aar）
  native\                       go-nx Windows sidecar / gomobile 核心
  tests\NightDesk.Tests\        Windows Release 断言测试
  docs\                         交接、验收、第三方说明
  artifacts\                    发布产物与诊断
  plan\                         实施计划与 Android 设计
```

## 5. Parsec VDD 随包安装

### 流程

1. 程序启动时调用 `VddClient` 打开 VDD 设备接口。
2. 接口可用：认为驱动已安装，不做任何安装或替换。
3. 接口不可用：
   - 从程序集提取 `NightDesk.Assets.vdd.parsec-vdd-0.45.0.0.exe` 到 `%LOCALAPPDATA%\NightDesk\driver\`；
   - 找不到内嵌载荷时，回退到程序目录 `vdd\parsec-vdd-0.45.0.0.exe`；
   - 校验大小、SHA-256、签名主体；
   - 通过 `runas` 请求 UAC，并以 `/S` 静默安装；
   - 等待退出后重新探测设备接口。
4. 自动安装每个用户环境只尝试一次；失败不会每次启动重复弹窗。
5. 界面保留“安装 Parsec VDD 驱动”手动重试按钮。

### 固定校验值

- 文件：`parsec-vdd-0.45.0.0.exe`
- 大小：`517256` 字节
- SHA-256：`e23332448fdaf5aa017cb308db5ef6855fac526a7ded05d80c039404126d5362`
- 签名主体：`CN="Parsec Cloud, Inc."`
- 来源：`https://builds.parsec.app/vdd/parsec-vdd-0.45.0.0.exe`
- 不使用 GitHub 上未签名的第三方 setup。

### 已验证 / 未验证

- 已验证：内嵌资源存在、大小、SHA-256、签名主体、提取、篡改拒绝、缺失拒绝、已安装检测。
- 未验证：真正驱动缺失环境中的完整 UAC + 静默安装。本机驱动已安装；需要干净 Windows 机器/VM 做人工验收。
- 已安装时绝不卸载、重装或替换现有驱动。

## 6. 显示切换与恢复

- 熄屏流程：保存当前拓扑 -> 创建或选择 NightDesk 自有 VDD -> 应用并验证虚拟输出 -> 只保留虚拟输出。
- 点亮流程：读取恢复快照 -> 应用原拓扑 -> 验证原物理显示源重新活动 -> 成功后清除快照。
- 恢复失败时保留 `recovery.json`，程序拒绝清理虚拟屏或正常退出。
- 程序只删除自己创建的 VDD 索引；第三方虚拟输出不能替代“独立活动硬件输出”的退出检查。
- “熄屏”是 Windows 拓扑断开，不是全局显示器电源广播；面板背光/无信号提示由显示器固件决定。
- 仅支持控制台用户会话；RDP 会话下不执行切屏。

## 7. Tailscale 远程控制与配对

- 桌面 API 只监听 `127.0.0.1:17645`；Tailscale Serve 提供 tailnet-only HTTPS。
- 固定命令 allow-list：status、off、on、restore、prevent-sleep、VDD add/remove；不提供 shell、文件访问或任意桌面输入。
- Serve 归属按路由结构判断：当前节点、`443/HTTPS`、`/` 代理到 `127.0.0.1:17645`、无额外路由、无 Funnel。
- “从未配对过”的机器不再因历史指纹不匹配而报错；指纹仅记录。
- QR 票据：`ndp1_` + 43 字符，5 分钟有效、单次使用、最多 8 个未使用票据。
- 二维码 JSON 不含长期令牌。
- `POST /api/v1/pair` 签发 `ndd1_` 每设备令牌；永久有效直到桌面端删除绑定。
- 设备注册表 `trusted-devices.dat`：只存令牌 SHA-256，整文件 DPAPI 加密；桌面端可查看并逐个删除绑定。
- 删除绑定后，该设备下一次请求立即 401；共享令牌轮换不影响设备令牌。

## 8. Android 多主机管理

### 数据模型

```kotlin
data class Profile(controlOrigin, controlToken, updateOrigin)
data class HostProfile(id, label, profile, addedAt)
data class Hosts(items: List<HostProfile>, activeId: String?)
```

- `id` 为规范化 Tailscale origin，一台主机唯一。
- 最多 20 台。
- 同一 origin 再次绑定只更新令牌，不产生重复项。
- 删除当前主机后回落到第一台剩余主机；删除最后一台会清空存储。
- `schemaVersion 3` 存储主机列表；`schemaVersion 2` 单主机数据自动迁移。
- SharedPreferences 只保存 Keystore AES-GCM 加密后的 base64 数据；令牌不以明文落盘。

### UI 行为

- Spinner 显示已保存主机，`●` 标记当前主机。
- 切换主机立即刷新状态。
- 扫码绑定或手动连接都会 upsert 当前主机。
- “删除当前主机”需确认，删除后更新选择器。

## 9. 双平台更新服务（AI）

Windows 清单为 /nightdesk/api/v1/windows-update，ZIP 为 /nightdesk/windows/latest.zip。1.6.0 启动及每 6 小时自动检查下载，用户确认后等待正常退出再替换双 EXE，保留 .bak-update；旧版首次需手动升级。Windows ZIP 未代码签名，SHA-256 不替代独立签名。


- SSH alias：`AI`
- 服务：`nightdesk-update-feed.service`
- 监听：`127.0.0.1:17768`
- 状态：`active` / `enabled`
- 健康检查：`curl -fsS http://127.0.0.1:17768/healthz` -> `ok android=ok windows=ok`
- Tailnet 路径：
  - `/nightdesk`：下载页
  - `/nightdesk/latest.apk`：最新 APK
  - `/api/v1/android-update`：更新清单
  - `/api/v1/android-apk`：按版本下载
- 发布顺序：APK 复制 -> 版本别名 -> manifest 最后原子替换。
- 服务按 manifest 校验大小与 SHA-256；不一致时 503，不暴露半发布文件。
- Android 启动即检查、每 6 小时 WorkManager 检查、手动检查。
- 下载后验证包名、versionCode/versionName、minSdk、大小、SHA-256、签名证书，再交系统安装器；不静默安装。
- 更新源与控制源独立，更新请求不带控制令牌。

## 10. 构建、测试与发布

### Windows

```powershell
$dotnet = 'C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe'
& $dotnet run --project 'C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj' -c Release --no-restore
& $dotnet build 'C:\dev\NightDesk\src\NightDesk\NightDesk.csproj' -c Release --no-restore
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\dev\NightDesk\tools\publish-windows.ps1'
```

- 测试入口是无额外 NuGet 测试框架的可执行断言程序；用 `dotnet run`，不是 `dotnet test`。
- 当前输出：`210 tests passed. No display state changed.`
- Release 构建：0 警告 / 0 错误。

### Android / AI

- Windows 不安装 Android SDK、Gradle、adb、模拟器。
- 最新 Android 源码与构建目录：`AI:/home/tony/dev/nightdesk-android-1.3.0`；旧 `stage5` 目录仅为历史验证快照，不用于本阶段恢复。
- 构建/测试只在 tmux 会话 `nightdesk-build`、`nightdesk-emulator`。
- **`main` tmux 会话属于其它工作，禁止读写。**
- 非交互 SSH 的 PATH 可能没有 adb；使用 `/home/tony/Android/Sdk/platform-tools/adb`。
- 本机无 `/dev/kvm`，AVD 为软件渲染。

```bash
# Android 1.3.0 release APK（使用隔离的本阶段源码目录）
NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-1.3.0 \
bash /home/tony/dev/nightdesk-android-1.3.0/scripts/build-ai.sh \
  -q -PnightdeskVersionCode=9 -PnightdeskVersionName=1.3.0

# AndroidTest APK
NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-1.3.0 \
bash /home/tony/dev/nightdesk-android-1.3.0/scripts/build-androidtest.sh -q \
  -PnightdeskVersionCode=9 -PnightdeskVersionName=1.3.0

## 完整仪器测试（含远控 UI 和独立更新源下载校验）
NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-1.3.0 \
NIGHTDESK_UPDATE_ORIGIN=https://tony-ai-pc-1.tail9f7f1f.ts.net \
  bash /home/tony/dev/nightdesk-android-1.3.0/scripts/run-instrumented.sh
```

### 签名

- 发布密钥：`/home/tony/.local/share/nightdesk-signing/release.p12`
- 口令：同目录 `password`，0600
- 证书 SHA-256：`41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`
- 后续版本必须保持同一签名；密钥与口令不得进入源码树、日志、截图或聊天。需要离线备份整个签名目录。

## 11. Android 后续开发计划

1.3.0 已发布，四分页、系统栏布局、更新页和基础远控手势已完成；真实端到端验收仍未完成。后续建议：

1. **1.3.0 真机验收**：在独立 Windows 测试机验证点按、移动、长按拖拽、双指滚动和强制释放，并检查真实 GDI / SendInput、DPI、断网、后台和 tailnet。
2. **RemoteActivity 仪器冒烟**：自动断言不可达地址会进入可重试状态；保持当前“后台即断开”的安全策略。
3. **NX 输入**：剪贴板、完整软键盘/物理键盘、组合键和受保护应用边界。
4. **主机体验**：自定义备注、在线/离线徽标、最近成功时间、排序和导入/导出。
5. **工程化**：target SDK 升级、CI、依赖锁定、签名托管、崩溃上报（需先明确隐私边界）。
6. **分发**：继续 tailnet 侧载；若扩大范围，再评估 Play Store、隐私政策和版本兼容矩阵。

## 12. 受控部署与人工验收

部署 1.6.0 前必须满足：

1. 有活动物理输出，且现场人员可恢复显示。
2. `recovery.json` 可成功恢复并验证。
3. 关闭其它 VDD 管理器，如 ParsecVDisplay。
4. 先运行只读诊断并保存基线。
5. 若仍有 NightDesk 进程，通过 UI 正常退出，让其恢复布局并清理自有虚拟屏；**不要强杀正在运行的 NightDesk**。
6. 备份正式程序目录，再把 `NightDesk.exe` 与 `nx-host.exe` 一起替换到同一目录；不要只复制主程序。
7. 启动新版，验收本地 UI、Tailscale 地址、远程页面、扫码配对、设备删除、真实熄屏/点亮、快捷键和 VDD 增删。

详细清单见 `C:\dev\NightDesk\docs\ACCEPTANCE.md`。

## 13. 当前交付边界

- Windows 1.6.0 已发布，210 项测试、布局、HTTPS 下载与隔离安装 helper 验证通过；Desktop 旧进程仍在运行，部署须先正常退出并确认显示恢复，再同时替换两个 EXE。
- Android 1.3.0（versionCode 9）已发布到 AI 更新源，下载/哈希/签名和模拟器更新流程已验证；真实 GDI、SendInput、DPI/UAC、Android 真机和 tailnet UDP/QUIC 仍需人工验收。
- VDD 随包自动安装已实现；真正缺驱动环境的端到端安装未在本机执行。
- 应用不提供任意 shell 或文件访问接口；go-nx 屏幕远控是经配对和证书固定保护的独立数据面，不等同于通用桌面控制。
- 不启用 Tailscale Funnel；远程访问只允许 tailnet。

