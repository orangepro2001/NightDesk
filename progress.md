# NightDesk progress

更新时间：2026-10-27（Asia/Seoul）

## 当前交付状态

### Windows 桌面端

- 代码基线：`fff73bb2b823f52f86f8807a9f0a59c6e3283ebd`
- 候选版本：**1.4.0**
- 发布目录：`C:\dev\NightDesk\artifacts\win-x64-1.4.0`
- `NightDesk.exe`：`201832196` 字节，SHA-256 `F4C448970703BB489E3A762596A59AEA06E688ED39CC505128B01CF4B7864EF2`，ProductVersion `1.4.0+fff73bb2b823f52f86f8807a9f0a59c6e3283ebd`。
- `nx-host.exe`：`7938048` 字节，SHA-256 `17B420CE531270E28BEF58D42DD68F5C13CAC6C5FE4CBFD4FDDBB20051AD35E3`，`-version` 为 `nx-host 0.2.0`。
- 两个 EXE 必须同目录部署；sidecar 被明确排除在 single-file 包之外。
- 发布必须使用 `tools\publish-windows.ps1`。脚本发布到全新 staging 目录，断言两份文件存在并运行 sidecar 冒烟检查。
- Release 测试：**176 项通过，未改变显示状态**
- Release 构建：**0 警告 / 0 错误**
- 已实现：熄屏/点亮、Parsec VDD 增删与分辨率/刷新率配置、防休眠、可修改全局快捷键、Tailscale HTTPS 远程控制、扫码配对、永久设备令牌、受信任设备管理、统一程序图标。
- 1.4.0 已把官方 Parsec VDD 安装器作为 EmbeddedResource 打入单文件 EXE。启动检测不到 VDD 时，验证载荷后以 UAC + `/S` 自动安装；每个用户环境自动尝试一次，失败后可从 UI 手动重试。驱动已安装时绝不重装或替换。
- go-nx 远程数据面（阶段 1-7）已接入：
  - Windows 启动受 Job Object 监控的 `nx-host.exe` sidecar；
  - sidecar 采集单个显示器，发送 64×64 tile 差分；
  - 使用 QUIC + TLS 1.3、证书固定和 6 位短期配对码；
  - WPF 通过 stdin/stdout JSON 行协议管理 sidecar；
  - sidecar 异常退出后清除陈旧的 running/ready 状态；
  - 它支持静态桌面和轻量交互，不是 H.264/HEVC/AV1 视频串流。
- 阶段 7 安全端到端验收：
  - `nx-host.exe` 新增 `-test-mode`，必须同时指定 `-source pattern -inject log`，否则退出码 2；
  - 使用合成 1920×1080 动态源和日志输入，不抓屏、不调用 SendInput、不读写生产 trust store；
  - `nx-probe.exe` 走与 Android 相同的 pinned TLS、配对、QUIC 和 tile 解码路径；
  - 对发布版 `nx-host.exe` 实测：4 帧、538 tile 更新、7/7 输入回执、首末帧摘要不同、host 退出码 0；
  - Go Linux/Windows `vet`、`test`、交叉构建通过，8 个 Windows 测试二进制全部 PASS。
- 尚未验收：真实 GDI 抓屏、真实 SendInput/DPI 映射/UAC 安全桌面、显示热插拔、Android 真机 tailnet UDP/QUIC、长时稳定性和资源占用。

### Android 客户端

- 已发布版本：**1.0.4**（versionCode **5**）
- 本地产物：`C:\dev\NightDesk\artifacts\android\nightdesk-1.0.4.apk`
- 大小：`6991035` 字节
- SHA-256：`906142e6abda3d193272b7f68adcb053d9cbbdd00fca40423b806e1d2979e436`
- 签名证书 SHA-256：`41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`
- 已实现多主机：最多保存 20 台主机，可按 Tailscale origin 去重、切换和删除；配置 schemaVersion 3，兼容旧单主机格式迁移；Android Keystore AES-GCM 加密，令牌不以明文落盘。

- 阶段 5 候选：**1.1.0**（versionCode **7**，未发布）
- 产物：`C:\dev\NightDesk\artifacts\android\nightdesk-1.1.0-stage5.apk`
- 大小：`55058900` 字节
- SHA-256：`01734a26365b859c831a9ba7806ff1d133b96e0993061053bb2a60ab72caf480`
- Debug APK：`artifacts\android\nightdesk-1.1.0-stage5-debug.apk`，`56886382` 字节，SHA-256 `042c9d37dffa86c7507ee8ab358415d46eb921cd55700328e8ef393034fc08aa`。
- AndroidTest APK：`artifacts\android\nightdesk-1.1.0-stage5-androidTest.apk`，`689645` 字节，SHA-256 `f9d11858d818aaa073f39fc404ea716af5043c2f6093bc0454b8ef52da1b4c36`。
- 阶段 5 新增：
  - `NxConnectionModel` 显式连接状态机；
  - 20 秒前端截止时间和用户可见的超时错误；
  - 失败后的“重试 / 配对码 / 重新扫码”恢复动作；
  - 后台、Activity 销毁、断线时释放鼠标/滚轮/按键；
  - 旧 attempt 回调隔离，避免旧错误覆盖新连接；
  - 成功连接后才持久化屏幕尺寸。
- Android 验证：
  - JVM 单测：`CoreTest 9 + NxConnectionTest 10 + NxTest 7 = 26 passed`；
  - 仪器测试：`HostStoreInstrumentedTest: OK (3 tests)` + `UpdateFlowInstrumentedTest: OK (1 test)`；
  - Lint/Release 构建：`0 errors / 22 warnings`；
  - 模拟器 UI 冒烟：`artifacts\android\stage5-home.png`、`artifacts\android\stage5-offline-timeout.png`。

### AI 更新源

- 主机：SSH alias `AI`
- 服务：`nightdesk-update-feed.service`，`active` / `enabled`
- 监听：`127.0.0.1:17768`
- 健康检查：`http://127.0.0.1:17768/healthz` 返回 `ok`
- Tailnet 地址：
  - 下载页：`https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk`
  - 最新 APK：`https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/latest.apk`
  - 更新清单：`https://tony-ai-pc-1.tail9f7f1f.ts.net/api/v1/android-update`
- 线上仍是 versionCode 5 / 1.0.4；阶段 5 未发布。

## 现场安全状态（必须保持）

- 当前运行进程：**PID 13816**，ProductVersion **1.3.1**，路径 `C:\Users\pixel\Desktop\NightDesk.exe`，启动时间 `2026/10/27 0:07:24`。
- 1.4.0 已构建，但**尚未替换现场运行程序**。生产目录没有 `nx-host.exe`，当前没有 `nx-host` 进程，因此生产 1.3.1 不能提供完整 NX 数据面。
- `%LOCALAPPDATA%\NightDesk\recovery.json` **仍存在**（352 字节，最后写入 `2026/10/27 00:10:30`）。
- 上次只读诊断：唯一活动源为 `\\.\DISPLAY6`（Parsec VDD）；不能据此认定物理输出已经安全恢复。
- 在物理显示器恢复且恢复快照可安全验证之前，禁止：
  - 退出、重启或替换 PID 13816；
  - 执行熄屏、点亮、恢复快照；
  - 添加或删除 VDD 显示；
  - 卸载或重装显示驱动；
  - 在生产桌面运行真实 `nx-host.exe` 抓屏或 SendInput。
- 本轮只运行了不触碰显示拓扑的 Windows Release 测试，以及只读进程/文件检查。

## 复核命令与结果

```powershell
$dotnet = 'C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe'
& $dotnet run --project 'C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj' -c Release --no-restore
& $dotnet build 'C:\dev\NightDesk\src\NightDesk\NightDesk.csproj' -c Release --no-restore
```

- 当前输出：`176 tests passed. No display state changed.`
- 构建：`0 个警告 / 0 个错误`。
- Windows 1.4.0 两个 EXE 的大小、SHA-256 与 ProductVersion 已复核；发布脚本和 `nx-host.exe -version` 冒烟检查通过。
- Android stage 5 JVM/仪器测试和 APK 哈希来自 AI 验证目录 `/home/tony/dev/nightdesk-android-stage5`。
- 线上 manifest 仍是 versionCode 5 / 1.0.4，不能把阶段 5 的本地 APK 误报成已发布版本。

## 历史说明

- 1.2.0 的现场进程来自此前的统一图标与远程控制归属修复；该进程保持运行。
- 1.3.0 是 VDD 随包安装的中间构建，1.3.1 是当时生产构建，当前 Windows 交付候选为 1.4.0。
- 阶段 4 完成了固定证书查看器和基础输入；阶段 5 完成了连接失败可见、可重试和生命周期收口。
- 旧版记录中的 PID 22708 / 115 项测试 / 1.2.0 已过期，以本文和 `docs/HANDOFF.md` 为准。