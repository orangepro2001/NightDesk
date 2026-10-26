# NightDesk 交接文档

更新时间：2026-10-27（Asia/Seoul）
目标平台：Windows x64（.NET 10 / WPF）+ Android（Kotlin，minSdk 29 / target 35）
当前代码基线：`fff73bb2b823f52f86f8807a9f0a59c6e3283ebd`（阶段 7 安全端到端探针）
最新阶段交接：`docs\20261027-041844-remote-control-stage-7-safe-e2e-probe-handoff.md`

## 1. 当前交付状态

### Windows 桌面端 1.4.0 候选

- 发布目录：`C:\dev\NightDesk\artifacts\win-x64-1.4.0`
- `NightDesk.exe`：`201832196` 字节，SHA-256 `F4C448970703BB489E3A762596A59AEA06E688ED39CC505128B01CF4B7864EF2`，ProductVersion `1.4.0+fff73bb2b823f52f86f8807a9f0a59c6e3283ebd`。
- `nx-host.exe`：`7938048` 字节，SHA-256 `17B420CE531270E28BEF58D42DD68F5C13CAC6C5FE4CBFD4FDDBB20051AD35E3`，`-version` 输出 `nx-host 0.2.0`。
- 这两个文件必须同目录部署；`nx-host.exe` 是独立 Go sidecar，刻意不并入 single-file 主程序。
- Release 测试：**176 项通过**，输出 `No display state changed.`
- Release 构建：**0 警告 / 0 错误**
- 发布必须使用 `tools\publish-windows.ps1`。脚本从全新 staging 目录发布、断言两份文件都存在、执行 sidecar 冒烟检查，再计算大小与 SHA-256。
- 已实现：熄屏/点亮、VDD 增删与分辨率/刷新率、防休眠、可修改全局快捷键、Tailscale HTTPS 远程控制、扫码配对、永久设备令牌、受信任设备管理、统一图标、随包 VDD 自动安装。
- 远程控制新增 go-nx 低带宽数据面：Windows 启动受监控的 `nx-host.exe` sidecar，生成含证书指纹和短期配对码的二维码；Android 原生查看器可扫码、固定证书并绘制 tile。阶段 5 已完成连接状态机、超时/重试/重新配对、后台释放输入和旧回调隔离；构建、单测、仪器测试和模拟器冒烟通过。阶段 7 新增 `nx-probe` 安全端到端验收：用与 Android 相同的 pinned TLS / 配对 / QUIC / tile 解码路径，在 1920×1080 合成动态源上验证 4 帧、538 个 tile 更新和 7/7 输入回执，不抓屏、不调用 SendInput。**真实 GDI 抓屏、SendInput 与真机 tailnet 仍未验收**。详见 `docs/20261027-041844-remote-control-stage-7-safe-e2e-probe-handoff.md`。

### Android 1.1.0 阶段 5 候选（未发布，取代阶段 4）

- 产物：`C:\dev\NightDesk\artifacts\android\nightdesk-1.1.0-stage5.apk`
- versionCode：**7**，versionName：**1.1.0**
- 大小：`55058900` 字节
- SHA-256：`01734a26365b859c831a9ba7806ff1d133b96e0993061053bb2a60ab72caf480`
- Debug APK：`artifacts\android\nightdesk-1.1.0-stage5-debug.apk`，`56886382` 字节，SHA-256 `042c9d37dffa86c7507ee8ab358415d46eb921cd55700328e8ef393034fc08aa`。
- AndroidTest APK：`artifacts\android\nightdesk-1.1.0-stage5-androidTest.apk`，`689645` 字节，SHA-256 `f9d11858d818aaa073f39fc404ea716af5043c2f6093bc0454b8ef52da1b4c36`。
- 新增 `NxConnectionModel`：显式连接阶段、20 秒前端截止时间、失败分类、重试/重新配对/重新扫码、后台暂停、旧 attempt 回调隔离；`session` / `generation` 使用 `@Volatile`。
- Android 单测：`CoreTest 9 + NxConnectionTest 10 + NxTest 7 = 26 passed`；Lint：`0 errors / 22 warnings`。
- 仪器测试：`HostStoreInstrumentedTest: OK (3 tests)` + `UpdateFlowInstrumentedTest: OK (1 test)`，`emulator-5580`。
- 模拟器 UI 冒烟：`artifacts\android\stage5-home.png`、`artifacts\android\stage5-offline-timeout.png`。
- **尚未用真实 Windows 主机、真实 GDI 抓屏、SendInput 或真机 tailnet 验收；未发布到 AI 更新源。**

### Android 1.0.4 已发布版本

- 产物：`C:\dev\NightDesk\artifacts\android\nightdesk-1.0.4.apk`
- versionCode：**5**
- 大小：`6991035` 字节
- SHA-256：`906142e6abda3d193272b7f68adcb053d9cbbdd00fca40423b806e1d2979e436`
- 签名证书 SHA-256：`41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`
- 支持最多 20 台主机保存、切换、删除；schemaVersion 3；Keystore AES-GCM。
- 已发布到 AI 更新源。

### 在线地址（仅 tailnet）

- 下载页：`https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk`
- 最新 APK：`https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/latest.apk`
- 更新清单：`https://tony-ai-pc-1.tail9f7f1f.ts.net/api/v1/android-update`
- 桌面控制：`https://minipc-xoh4t.tail9f7f1f.ts.net/`

未启用 Funnel；访问边界由 tailnet ACL 控制。

## 2. 现场安全状态（最高优先级）

- 2026-10-27 03:00 KST 只读检查：当前运行进程 **PID 13816**，路径 `C:\Users\pixel\Desktop\NightDesk.exe`，启动时间 `2026/10/27 0:07:24`。
- `%LOCALAPPDATA%\NightDesk\recovery.json` **仍存在**（352 字节，最后写入 `2026/10/27 00:10:30`）；这不等于显示拓扑已经安全，仍需独立复核物理输出、DPI、VDD 和快照。
- 1.4.0 已构建，但**尚未替换现场程序**；生产目录还缺少 `nx-host.exe`。
- 在物理显示器恢复且恢复快照可安全验证之前，禁止：
  - 退出、重启或替换 PID 13816；
  - 执行熄屏、点亮、恢复快照；
  - 添加或删除 VDD 显示；
  - 卸载/重装显示驱动；
  - 在生产桌面启动真实 `nx-host.exe` 做抓屏或 SendInput 测试。
- 新版本和阶段 5 APK 可保留在 artifacts；部署必须等现场恢复条件满足。

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

## 9. Android 更新服务（AI）

- SSH alias：`AI`
- 服务：`nightdesk-update-feed.service`
- 监听：`127.0.0.1:17768`
- 状态：`active` / `enabled`
- 健康检查：`curl -fsS http://127.0.0.1:17768/healthz` -> `ok`
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
- 当前输出：`176 tests passed. No display state changed.`
- Release 构建：0 警告 / 0 错误。

### Android / AI

- Windows 不安装 Android SDK、Gradle、adb、模拟器。
- 源码：`AI:/home/tony/dev/nightdesk-android`；阶段 5 验证目录：`AI:/home/tony/dev/nightdesk-android-stage5`。
- 构建/测试只在 tmux 会话 `nightdesk-build`、`nightdesk-emulator`。
- **`main` tmux 会话属于其它工作，禁止读写。**
- 非交互 SSH 的 PATH 可能没有 adb；使用 `/home/tony/Android/Sdk/platform-tools/adb`。
- 本机无 `/dev/kvm`，AVD 为软件渲染。

```bash
# 阶段 5 release APK（使用同步目录）
NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-stage5 \
bash /home/tony/dev/nightdesk-android-stage5/scripts/build-ai.sh \
  -q -PnightdeskVersionCode=7 -PnightdeskVersionName=1.1.0

# AndroidTest APK（需要构建脚本导出的签名环境变量）
gradle --no-daemon assembleReleaseAndroidTest -q \
  -PnightdeskVersionCode=7 -PnightdeskVersionName=1.1.0

# 在已启动的 emulator-5580 上运行多主机仪器测试
adb -s emulator-5580 shell am instrument -w \
  -e class dev.nightdesk.android.HostStoreInstrumentedTest \
  dev.nightdesk.android.test/androidx.test.runner.AndroidJUnitRunner

# 完整仪器测试（含独立更新源下载校验，必须传入 updateOrigin）
adb -s emulator-5580 shell am instrument -w \
  -e updateOrigin https://tony-ai-pc-1.tail9f7f1f.ts.net \
  dev.nightdesk.android.test/androidx.test.runner.AndroidJUnitRunner
```

### 签名

- 发布密钥：`/home/tony/.local/share/nightdesk-signing/release.p12`
- 口令：同目录 `password`，0600
- 证书 SHA-256：`41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`
- 后续版本必须保持同一签名；密钥与口令不得进入源码树、日志、截图或聊天。需要离线备份整个签名目录。

## 11. Android 后续开发计划

1.0.4 已满足 HTTPS 多主机、扫码绑定、长期令牌和自动更新；1.1.0 阶段 5 已完成 go-nx 固定证书查看器和连接生命周期收口，但仍需真实端到端验收。后续建议：

1. **阶段 5 真机验收**：在独立 Windows 测试机验证 GDI 抓屏、SendInput、tile 画面、失焦释放、防火墙和 tailnet。
2. **RemoteActivity 仪器冒烟**：自动断言不可达地址会进入可重试状态；保持当前“后台即断开”的安全策略。
3. **NX 输入**：剪贴板、完整软键盘/物理键盘、组合键和受保护应用边界。
4. **主机体验**：自定义备注、在线/离线徽标、最近成功时间、排序和导入/导出。
5. **工程化**：target SDK 升级、CI、依赖锁定、签名托管、崩溃上报（需先明确隐私边界）。
6. **分发**：继续 tailnet 侧载；若扩大范围，再评估 Play Store、隐私政策和版本兼容矩阵。

## 12. 受控部署与人工验收

部署 1.4.0 前必须满足：

1. 有活动物理输出，且现场人员可恢复显示。
2. `recovery.json` 可成功恢复并验证。
3. 关闭其它 VDD 管理器，如 ParsecVDisplay。
4. 先运行只读诊断并保存基线。
5. 通过旧版 UI 正常退出，让其恢复布局并清理自有虚拟屏；**绝不强杀 PID 13816**。
6. 备份正式程序目录，再把 `NightDesk.exe` 与 `nx-host.exe` 一起替换到同一目录；不要只复制主程序。
7. 启动新版，验收本地 UI、Tailscale 地址、远程页面、扫码配对、设备删除、真实熄屏/点亮、快捷键和 VDD 增删。

详细清单见 `C:\dev\NightDesk\docs\ACCEPTANCE.md`。

## 13. 当前交付边界

- Windows 1.4.0 已构建和测试，但未替换生产中的 1.3.1；生产目录缺少 `nx-host.exe`，当前生产版本不能提供完整 NX 数据面。
- Android 1.0.4 已发布；Android 1.1.0 阶段 5 候选已构建但未发布，真实端到端远控仍需人工验收。
- VDD 随包自动安装已实现；真正缺驱动环境的端到端安装未在本机执行。
- 应用不提供任意 shell 或文件访问接口；go-nx 屏幕远控是经配对和证书固定保护的独立数据面，不等同于通用桌面控制。
- 不启用 Tailscale Funnel；远程访问只允许 tailnet。

