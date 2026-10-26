# NightDesk × go-nx 远程控制集成可行性计划

| 项目 | 内容 |
|---|---|
| 文档日期 | 2026-10-27 00:38 KST（Asia/Seoul） |
| NightDesk 版本 | 桌面 1.3.1；Android 1.0.4（versionCode 5） |
| go-nx 分析提交 | `124331bc46d8dcd281ace6886ba9413407ca5251`（2026-10-24T17:51:31+09:00） |
| go-nx 仓库 | `git@github.com:orangepro2001/go-nx.git` |
| 分析方式 | 只读源码审阅、AI 主机交叉构建/测试、合成数据基准；未触碰现场显示拓扑 |
| 结论 | Windows：**有条件可行，推荐 sidecar PoC**；Android：**技术可行但工程量大，不建议承诺短期交付**；go-nx 原程序：**不可直接嵌入** |

> **现场安全声明**
>
> 本报告形成期间没有退出、重启或替换 PID `22708`，没有执行熄屏、点亮、恢复显示快照、VDD 增删、驱动安装/卸载，也没有改变显示拓扑。当前现场仍存在恢复快照，且唯一活动显示源可能是 `\\.\DISPLAY6`（Parsec VDD），物理输出未确认恢复。任何显示相关验证必须等物理显示器恢复并人工确认后再做。
>
> 本报告中的构建、单元测试和基准均在 SSH 主机 `AI` 的临时副本中执行，不影响现场 NightDesk 进程。

---

## 0. 结论摘要

### 0.1 一句话结论

**可以把 go-nx 的“协议内核”集成进 NightDesk，但不应该把 go-nx 这个 Ebiten 桌面程序直接塞进 WPF 或 Android。**

更准确地说：

- **Windows 桌面端**：把 go-nx 精简成独立的 host 进程（sidecar），由 NightDesk 管理其生命周期、虚拟屏、鉴权和 Tailscale 网络。这个方向可行，属于中等工程，适合先做单屏 PoC。
- **Android 手机端**：go-nx 没有现成移动端代码。需要新写 Kotlin/Java 客户端，或者用 gomobile 把 Go 协议核心封装成 AAR。可以复用 QUIC 协议、tile 编码和输入语义，但不能复用 Ebiten UI、Windows 采集和 Windows 注入。工程量大，且有几个必须先验证的高风险点。
- **“Win 控 Win”现状**：go-nx 的控制端和被控端是同一个 Windows EXE，控制端依赖 Ebiten 窗口；这使它与 NightDesk 的 WPF UI、托盘、单实例和更新模型天然冲突。协议层可复用，应用层几乎不可复用。
- **与 Sunshine/Parsec 的关系**：go-nx 不是视频串流。它是低帧率、无损 tile 差分、可靠 QUIC 流的“静态桌面/工控 UI 远控”，不是 GPU 采集 + H.264/HEVC/AV1 + 低延迟视频。它适合看参数表、点按钮、改配置；不适合视频、游戏、音频或高动态画面。

### 0.2 评级

| 目标 | 评级 | 判断 |
|---|---|---|
| Windows 单虚拟屏远程查看/操作 | **有条件可行（6/10）** | 需要 sidecar 化、鉴权改造、指定显示器采集、输入坐标修正、生命周期联动；4–6 周可做 PoC |
| Windows 多虚拟屏/多显示器 | **暂不建议（3/10）** | go-nx 只抓主屏，输入坐标也不支持虚拟桌面多屏；需要改采集和注入模型 |
| Android 手机端完整远控 | **技术可行但高成本（4/10）** | 需要新客户端、QUIC/TLS、渲染、触摸、输入法、前台服务、网络切换；6–10 周才可能做出可用 PoC |
| 直接复用 go-nx EXE/DLL | **不建议（2/10）** | Ebiten、托盘、单实例、主屏采集、默认关闭鉴权均与 NightDesk 冲突 |
| 作为长期产品内核 | **有条件（5/10）** | 协议简单、可维护性尚可，但缺少版本协商、移动端、音频、剪贴板、权限分级和许可证文件 |

### 0.3 推荐路线

```text
第一阶段（必须先做）
  go-nx 协议内核 → 独立 nx-host sidecar
  NightDesk 负责：虚拟屏、恢复快照、Tailscale、设备配对、生命周期
  只支持：单个 Parsec VDD、1080p/4K 静态 UI、一个控制端、无音频

第二阶段（Windows PoC 通过后）
  Android 原生客户端 → QUIC 客户端 + tile 解码 + SurfaceView 渲染 + 触摸/输入法
  先做“能看、能点、能输入英文/数字”，再评估中文输入和后台存活

第三阶段（可选）
  多显示器、剪贴板、文件、音频、权限分级、审计、看板
```

### 0.4 明确不建议的做法

1. **不要把 go-nx 的 Ebiten 窗口嵌入 WPF**。Ebiten 是独立游戏循环/窗口，和 WPF 的 HWND、DPI、输入、托盘生命周期耦合会很糟。
2. **不要直接把 go-nx 编译成 DLL 注入 NightDesk 进程**。Go runtime、Ebiten、崩溃域、升级、杀软误报都会变成新问题。
3. **不要默认沿用 go-nx 当前鉴权**。`require_auth` 默认关闭，密码以明文保存在 `settings.json`，监听 `:7000` 所有接口；这三项在 NightDesk 场景不可接受。
4. **不要假设 Tailscale Serve 能代理 QUIC**。Serve 是 HTTP/TCP 入口，QUIC 走 UDP；数据面必须走 tailnet 直连（或另行设计 UDP 代理）。
5. **不要把 go-nx 文档里的 DDA、LZ4、xxHash 当作已实现能力**。实际代码是 GDI `BitBlt`、`compress/flate`、逐 tile `bytes.Equal`；文档是设计意图，不是当前实现。

---

## 1. 范围、假设与成功标准

### 1.1 本文要回答的问题

1. NightDesk 当前到底具备什么远程能力，边界在哪里？
2. go-nx 的真实实现是什么，和它自己的设计文档差多少？
3. 它和 Sunshine/Parsec/RDP/VNC 的本质区别是什么？
4. 能否集成到 NightDesk 的 Windows 桌面端？怎么做，代价多大？
5. 能否集成到 NightDesk 的 Android 端？需要哪些新组件，风险是什么？
6. 哪些情况下应该停止，不要继续投入？

### 1.2 不在本文范围内

- 不实现代码，不修改 NightDesk 现有远程 API 的行为。
- 不在现场做显示切换、VDD 增删、驱动安装、熄屏/点亮。
- 不承诺具体商业工期；本文的工期是工程估算，必须以 PoC 实测校正。
- 不把 go-nx 改造成游戏串流、视频会议、音频远控或云桌面产品。
- 不评估公网暴露、Funnel、端口映射到互联网的场景；默认只在 tailnet 内使用。

### 1.3 建议的成功标准（MVP）

以“工控/办公静态 UI 远控”而不是“视频串流”为标准：

| 维度 | MVP 目标 | 说明 |
|---|---|---|
| 画面 | 1080p，8–10 FPS，无损 tile | 文字、表格、按钮不能糊 |
| 输入 | 鼠标移动/左右中键/滚轮/键盘 | 中文输入列为扩展项 |
| 延迟 | tailnet 内输入回显 P95 ≤ 150 ms | 以真实网络测量为准 |
| 静止带宽 | 画面静止时接近 0 | tile 无变化不发帧 |
| 会话 | 单控制端，支持接管、释放按键 | 不支持多人同时控制 |
| 安全 | 默认拒绝、设备证书、二维码绑定、可撤销 | 不允许“默认放行” |
| 显示 | 单虚拟屏，退出/失败可恢复 | 不允许破坏 NightDesk 的恢复快照 |
| 平台 | Windows 10/11 x64 + Android 10+（minSdk 29） | 与现有 NightDesk 目标一致 |

---

## 2. NightDesk 当前架构与边界

### 2.1 Windows 桌面端

NightDesk 是 C# / WPF / Win32 / .NET 10 应用，当前版本 1.3.1。它不是远程桌面数据面，而是一个“显示拓扑 + 远程控制面”的工作台：

```text
┌──────────────────────────── NightDesk.exe（WPF，当前用户会话）────────────────────────────┐
│  MainWindow / 托盘 / 全局快捷键                                                           │
│      │                                                                                   │
│      ├── DisplayController ── Parsec VDD 客户端 ── 虚拟显示器                              │
│      │        │                                                                          │
│      │        ├── 物理显示活动检查                                                         │
│      │        ├── 显示拓扑快照 recovery.json                                               │
│      │        ├── 切换到“仅虚拟屏”                                                         │
│      │        └── 恢复 + 验证 + 清快照                                                     │
│      │                                                                                   │
│      ├── RemoteServer（Kestrel，127.0.0.1:17645）                                        │
│      │        ├── GET  /api/v1/health          （无需认证）                               │
│      │        ├── GET  /api/v1/status          （Bearer / 设备令牌）                      │
│      │        ├── POST /api/v1/pair            （一次性配对票据）                         │
│      │        ├── POST /api/v1/commands        （Bearer / 设备令牌）                      │
│      │        └── GET  /api/v1/android-update / android-apk                              │
│      │                                                                                   │
│      ├── RemoteAuth：共享令牌 + DPAPI 设备注册表 + 5 分钟配对票据                         │
│      └── TailscaleService：tailscale serve --https=443 → 127.0.0.1:17645                  │
└───────────────────────────────────────────────────────────────────────────────────────────┘
```

关键事实：

- Kestrel **只监听回环** `127.0.0.1:17645`，见 `src/NightDesk/RemoteServer.cs`。
- Tailscale Serve 把 `https://<host>.<tailnet>.ts.net/` 代理到该回环端口；未启用 Funnel，公网不可达。
- 现有远程能力是 **HTTP 控制面**：状态查询、熄屏/点亮/恢复、虚拟屏增删、Android 更新分发。
- **没有画面、音频、键鼠透传、剪贴板、文件传输**。`remote.html` 只是控制面板，不是视频/画布。
- 鉴权是 Bearer 共享令牌或 DPAPI 保存的设备令牌哈希；配对票据 5 分钟有效、一次性、最多 8 个；撤销设备立即失效。
- 显示切换有严格安全门禁：必须存在活动物理显示器、切换前保存快照、恢复验证成功才清理快照、虚拟接管期间禁止随意增删显示、恢复失败拒绝退出。

关键文件：

| 文件 | 作用 |
|---|---|
| `src/NightDesk/RemoteServer.cs` | Kestrel 监听、路由、认证中间件、命令解析 |
| `src/NightDesk/RemoteAuth.cs` | 共享令牌、设备注册表、配对票据、DPAPI 持久化 |
| `src/NightDesk/TailscaleService.cs` | Tailscale Serve 的创建/接管/关闭与归属校验 |
| `src/NightDesk/DisplayController.cs` | VDD、显示拓扑、恢复快照、安全门禁 |
| `src/NightDesk/DisplayTopology.cs` / `NativeDisplays.cs` | Win32 显示拓扑与枚举 |
| `src/NightDesk/SettingsStore.cs` | DPAPI 配置、恢复快照、原子写 |
| `src/NightDesk/MainWindow.xaml.cs` | 托盘、快捷键、远程开关、配对二维码 |
| `src/NightDesk/Models.cs` | 设置、显示器、恢复快照、远程命令模型 |
| `src/NightDesk/remote.html` | 远程控制网页（非画面串流） |

### 2.2 Android 当前能力

Android 是 Kotlin 原生应用，`minSdk 29`、`targetSdk 35`，当前版本 1.0.4（versionCode 5）。它目前是 **HTTP 控制端**，不是远程画面客户端：

- 只用 `HttpURLConnection` 访问 NightDesk 的 HTTPS API。
- 支持扫码配对、最多 20 台主机、主机切换/删除。
- 配置使用 Android Keystore AES-GCM 加密。
- 支持状态刷新、熄屏/点亮/恢复、防休眠、虚拟屏增删。
- 支持 WorkManager 更新检查、APK 校验和系统安装器。
- 权限只有 `INTERNET`、`CAMERA`、`ACCESS_NETWORK_STATE`、`POST_NOTIFICATIONS`、`REQUEST_INSTALL_PACKAGES`。
- **没有** `SurfaceView`/`TextureView`、视频解码、触摸远控、QUIC/UDP、前台服务、网络切换处理。

关键文件：

| 文件 | 作用 |
|---|---|
| `android/app/src/main/java/dev/nightdesk/android/Core.kt` | Profile/Hosts、QR 解析、HTTP API、Keystore、更新客户端 |
| `android/app/src/main/java/dev/nightdesk/android/MainActivity.kt` | 程序化 LinearLayout/ScrollView UI、扫码、主机管理 |
| `android/app/src/main/AndroidManifest.xml` | 权限与 Activity/FileProvider |
| `android/app/build.gradle.kts` | minSdk 29、target 35、签名、依赖 |

### 2.3 现有控制面与未来数据面的关系

建议保持两条面分离：

```text
控制面（已有，HTTPS + Bearer）
  手机/桌面 → NightDesk RemoteServer → 显示/电源/虚拟屏/配对/撤销
  用途：低频、可靠、需要审计的管理操作

数据面（新增，QUIC + mTLS）
  手机/桌面 → nx-host sidecar → 抓屏/编码/输入注入
  用途：高频画面与输入，不经过 Kestrel，不经过 Tailscale Serve
```

**不要把 QUIC 画面流塞进现有 HTTPS 路由**。Kestrel 的 `Listen(IPAddress.Loopback, port)` 和 Serve 的 HTTP 代理都不适合承载 UDP QUIC；混在一起会让鉴权、超时、缓冲和故障域都变复杂。

### 2.4 现场约束

当前运行中的 NightDesk 是 PID 22708 / 1.2.0，`recovery.json` 仍存在，最近只读诊断显示唯一活动源可能是 `\\.\DISPLAY6`，没有活动物理输出。本文所有设计都必须遵守：

- 不退出/重启/替换 PID 22708。
- 不执行熄屏、点亮、恢复快照。
- 不增删 VDD、不卸载/重装显示驱动。
- 不改变显示拓扑。
- 任何集成代码在合入前必须能在“无显示操作”的单元测试和 AI 主机上验证。

## 3. go-nx 的真实实现

### 3.1 代码结构与可复用边界

go-nx 当前是一个 Windows-only 的单 EXE 应用。控制端和被控端并不是两个独立程序，而是同一个 Ebiten 应用中的两个页面/角色：

- 被控端：启动时创建主屏采集、QUIC 监听和 Windows 输入注入器。
- 控制端：在同一个 Ebiten 窗口中连接远端，接收 tile 帧并用 Ebiten 绘制。
- 托盘、单实例锁、窗口隐藏、输入框、日志页、连接配置都位于 `cmd/go-nx`。
- 协议、采集、tile、注入、信任和会话逻辑位于 `internal/*`。

因此必须区分三种复用：

| 复用对象 | 判断 | 原因 |
|---|---|---|
| 协议数据结构与会话语义 | 可以高度复用 | `proto`、`tile`、`remote` 都相对独立，但需要改为公开包并增加版本协商 |
| Windows 采集与注入 | 可复用但必须改造 | 当前只面向主屏，缺少指定显示器、虚拟桌面坐标和安全桌面处理 |
| Ebiten UI、托盘、单实例和连接页 | 不应复用 | 与 WPF 和 Android 的 UI/生命周期模型不兼容 |

`internal/...` 是 Go 的私有包，外部模块不能直接导入。若要让 NightDesk 或独立的 Android 客户端复用，必须先建立公开 API 包，例如 `pkg/protocol`、`pkg/client`、`pkg/host`，或者把 go-nx 变成一个独立的协议模块。直接把 `cmd/go-nx` 作为库依赖会把 Ebiten、systray 和 Windows 入口一起带进来。

### 3.2 传输协议与会话模型

实际实现使用 `quic-go v0.64.1`，ALPN 为 `go-nx`，TLS 最低版本为 TLS 1.3，双方都提交自签证书。TLS 层要求客户端证书，但真正的信任判断发生在应用层：

- 被控端在 `remote.Serve` 中读取客户端证书 SHA-256 指纹。
- 控制端第一次连接时记录主机指纹，之后指纹变化会被拒绝。
- 没有 CA、没有 ACME、没有服务端名称校验；客户端用指纹 pin 替代传统证书链校验。

一条会话的结构如下：

```text
控制流：客户端打开的可靠双向 QUIC stream
  Hello   : pwLen u8 + password bytes（可为空）
  Ack     : seq u32
  Input   : opID u64 + kind u8 + x i32 + y i32 + arg i32
  Receipt : 主机返回 opID u64 + status u8

画面流：主机为每一帧打开一条单向 QUIC stream
  FrameHeader : seq u32 + width u16 + height u16
  TileRecord  : tx u16 + ty u16 + type u8 + length u32 + payload
```

关键行为：

- 同一时间只允许一个“已确认、未完成”的画面帧。客户端处理完并发送 `Ack(seq)` 后，主机才发送下一帧。
- 若 ACK 超时，主机会取消该帧的发送流，并把该帧的 dirty tile 合并到下一次采集结果中重新编码。这样网络变慢时会降低帧率，而不是无限积压。
- 输入带 `opID` 时会被主机去重并返回回执；`opID == 0` 表示 fire-and-forget。去重窗口是最近 1024 个操作。
- 新连接接管输入时，旧会话按住的键盘键和鼠标按钮会被释放，避免“远端断线后 Ctrl 一直按着”。
- 协议没有版本字段、能力协商、音频、剪贴板、文件、权限分级或多控制端会话。
- 帧宽高是 `uint16`，因此协议本身可以容纳 4K，但实际内存、编码和网络表现取决于实现。

这套模型很适合“低频、可靠、不能丢文字细节”的场景。它不追求视频级帧率和最低传输延迟，而是通过可靠流、ACK 和脏 tile 合并来保证画面最终一致。

### 3.3 采集与 tile 编码

当前采集实现不是设计文档中描述的 Desktop Duplication API（DDA），而是主屏 GDI：

- `capture.NewScreen()` 使用 `GetDC(0)` 和 `GetSystemMetrics(SM_CXSCREEN/SM_CYSCREEN)`。
- 进程调用 `SetProcessDPIAware()`，按物理像素创建 top-down BGRA DIB。
- 每次采集使用 `BitBlt` 从主屏复制到内存位图，包含 layered windows 标志。
- 默认 10 FPS；帧尺寸取自主屏，不能选择某个指定显示器。

编码是 64×64 BGRA tile，逐 tile 无损：

1. 若 tile 只有一种颜色，发送 4 字节纯色。
2. 若颜色数不超过 256，发送调色板 + RLE。
3. 否则使用 Go `compress/flate`（BestSpeed）压缩原始像素。
4. 差分算法是对每个 tile 的像素做 `bytes.Equal`，不是 xxHash64；没有使用 LZ4，也没有 GPU 编码。

这意味着：

- 静态文字、表格、参数面板通常非常省流量。
- 随机噪声/视频帧几乎不可压缩，1080p 首帧可接近原始 BGRA 体积。
- 每帧需要 CPU 扫描所有 tile；4K 下 tile 数约为 1920 个，CPU 成本明显高于 1080p。
- GDI 对某些硬件覆盖层、DRM、受保护内容、HDR 或特殊渲染表面可能抓不到或抓到黑屏，必须在目标 Windows/VDD 组合上实测。

### 3.4 输入模型

输入通过 Windows `SendInput` 注入，支持：

- 鼠标移动。
- 左、右、中键按下/抬起。
- 滚轮，单位为 1/120 notch。
- Windows 虚拟键码按下/抬起，附带扩展键标志。
- 输入会话接管和断线释放。

但当前实现有几个直接限制：

- 鼠标坐标是“主屏像素”，通过当前屏幕宽高归一化；没有 `MOUSEEVENTF_VIRTUALDESK`，也没有目标显示器原点。
- 不支持 Unicode 文本事件；它发送的是虚拟键码，不包含 IME 组合文本、软键盘提交文本或任意 Unicode 字符。
- 不支持剪贴板、文件、音频、触控板手势、手柄、媒体键和权限分级。
- UAC 安全桌面、登录界面、锁屏界面通常不能被普通用户进程采集或注入，这是 Windows 会话边界，不是协议能轻易解决的问题。

因此 Windows 端接入 NightDesk 时，输入坐标必须从“主屏局部坐标”改成“目标显示器局部坐标 → 虚拟桌面坐标”的明确映射；Android 端若要输入中文，协议还必须增加 Unicode/IME 事件，而不能只发虚拟键码。

### 3.5 当前信任模型

go-nx 的默认设置是：

- `require_auth` 默认关闭；在用户打开之前，任何能连到 UDP 端口的客户端都可以被允许。
- 打开后，已配对证书指纹允许；配对窗口内允许新设备；否则可回退到可选密码。
- 密码以明文形式写入 `settings.json`，不是 DPAPI、Keystore 或系统凭据保险库。
- 配对窗口默认 2 分钟；密码错误 5 次后锁定 1 分钟。
- 监听地址使用 `fmt.Sprintf(":%d", port)`，即 UDP 所有接口，默认端口 7000。

这与 NightDesk 现有安全模型不匹配。NightDesk 的 HTTPS 控制面有回环监听、Tailscale Serve、一次性配对票据、设备撤销和 DPAPI 持久化；go-nx 数据面不能以“默认放行 + 明文密码 + 全接口监听”的形式直接进入产品。集成时必须把默认拒绝、设备证书、票据过期、撤销和审计作为硬性门槛。

### 3.6 平台和工程状态

已核实的事实：

- go-nx 分析提交为 `124331bc46d8dcd281ace6886ba9413407ca5251`。
- 仓库没有 `README.md`，没有 `LICENSE`，没有 tag。
- `go.mod` 声明 Go 1.27.3，依赖 Ebiten、systray、quic-go 等。
- 在 AI 主机上，`internal/proto`、`tile`、`trust`、`profiles`、`logging`、`remote` 的测试通过。
- Windows `amd64` 交叉构建通过，生成约 25 MB 的 console EXE，说明 Windows 目标可构建。
- 交叉构建不等于真实 Windows 抓屏、输入注入、DPI、多屏和 VDD 行为已验证。

---

## 4. 与 Sunshine、Parsec、RDP、VNC 的本质差异

go-nx 不是“简化版 Sunshine”，也不是“轻量 Parsec”。它走的是一条不同的技术路线：**可靠 QUIC 流 + 无损 tile 增量 + 低帧率静态 UI 远控**。

| 维度 | go-nx 当前实现 | Sunshine/Parsec 类方案 | RDP/VNC 类方案 |
|---|---|---|---|
| 采集 | GDI 主屏 BitBlt | 通常 GPU 采集/编码链路 | 按实现不同，RDP 深度集成桌面，VNC 常见逐区域抓屏 |
| 编码 | 64×64 无损 tile、调色板/RLE/flate | 硬件 H.264/HEVC/AV1 等视频编码 | RDP 可用位图/视频编码；VNC 常用 RFB 编码 |
| 传输 | QUIC 可靠 stream | 通常低延迟 UDP/RTP 或专有传输 | RDP 自有传输；VNC 通常 TCP |
| 帧率目标 | 默认 10 FPS，静态内容优先 | 高帧率、低延迟视频 | 办公交互为主，依实现而异 |
| 画面质量 | 无损文字/UI，天然不糊 | 有损视频，码率控制 | 依编码可无损或有损 |
| 静止带宽 | 接近 0（无变化不发帧） | 仍可能有心跳/视频开销 | 通常有刷新/心跳开销 |
| 动态视频 | 很差，容易变成全屏 raw tile | 适合 | RDP 可优化，VNC 通常较弱 |
| 音频/剪贴板/文件 | 没有 | 通常有音频，其他能力依产品 | RDP/VNC 生态通常更完整 |
| 适合场景 | 参数表、工控 UI、远程点按钮、静态办公 | 游戏、视频、图形工作站 | 通用办公远程桌面 |

结论：

- 如果 NightDesk 的目标是“看参数、点按钮、改设置、低频操作”，go-nx 的 tile 方案反而有优势：文字清晰、静止时几乎不耗带宽、协议简单。
- 如果目标是“像 Parsec/Sunshine 一样看视频、做图形设计、玩游戏或传音频”，go-nx 当前实现不适合，继续优化 tile 编码也不是正确方向。
- 如果把 go-nx 包装成“通用手机远控”对外承诺，用户会自然拿它和成熟远控比较；这时工程量和体验风险会被严重低估。
---

## 5. Windows 桌面端集成方案

### 5.1 推荐架构：独立 sidecar

推荐把 go-nx 的 host 能力抽成一个独立的 `nx-host.exe`，由 NightDesk 作为父进程或受管子进程启动。不要让 WPF 直接加载 Ebiten，也不要把 Go 运行时放进 NightDesk 进程。

```text
┌───────────────────────────── NightDesk.exe（WPF）─────────────────────────────┐
│ MainWindow / 托盘 / 设置 / 配对 UI                                             │
│        │                                                                       │
│        ├── DisplayController：VDD、显示快照、恢复门禁                           │
│        ├── RemoteServer：HTTPS 控制面、设备令牌、配对票据                       │
│        └── NxHostClient：命名管道控制 + 状态事件                                │
└───────────────────────────────┬───────────────────────────────────────────────┘
                                │ 本机命名管道（仅当前用户）
┌───────────────────────────────▼───────────────────────────────────────────────┐
│ nx-host.exe（Go，无窗口）                                                       │
│  ├── 指定显示器 GDI 采集                                                         │
│  ├── tile diff / encode                                                         │
│  ├── QUIC listener + mTLS + 设备指纹白名单                                        │
│  ├── SendInput 注入（目标显示器坐标）                                             │
│  └── 结构化状态/错误日志                                                         │
└───────────────────────────────┬───────────────────────────────────────────────┘
                                │ tailnet 直连 UDP/QUIC（不经 Tailscale Serve）
                     ┌──────────▼──────────┐
                     │ Android / 另一台 PC │
                     └─────────────────────┘
```

这样划分的好处：

- Go panic、升级或崩溃不会直接杀死 NightDesk。
- sidecar 可以独立测试、独立签名、独立替换。
- NightDesk 继续拥有显示拓扑和恢复快照，sidecar 没有权限改显示配置。
- WPF 和 Android 共享同一个协议事实来源，而不是各自猜协议。

### 5.2 sidecar 的职责边界

sidecar 应该只做数据面：

| 职责 | 放在 sidecar | 放在 NightDesk |
|---|---|---|
| 抓屏、tile 编码 | 是 | 否 |
| QUIC listener/session | 是 | 否 |
| 设备证书校验 | 是（执行） | 是（决定是否授权及撤销） |
| 输入注入 | 是 | 否 |
| 显示拓扑切换 | **否** | 是 |
| 恢复快照和恢复验证 | **否** | 是 |
| Tailscale Serve/HTTP 控制 API | 否 | 是 |
| 配对票据、设备撤销、审计 | 否（只接受授权结果） | 是 |
| UI、托盘、单实例 | 否 | 是 |

sidecar 不应读取 go-nx 原来的 `%AppData%\go-nx` 配置，也不应启动自己的托盘或单实例检查。它应接受 NightDesk 指定的配置目录、监听地址、目标显示器和设备白名单；未收到明确授权时默认拒绝连接。

### 5.3 进程内 IPC 契约

建议使用 Windows 命名管道，而不是本机 TCP：

- 管道名包含当前用户 SID 或 NightDesk 会话标识，例如 `\\.\pipe\NightDesk.NxHost.<session>`。
- 使用 `PIPE_REJECT_REMOTE_CLIENTS`，DACL 只允许当前用户/SYSTEM。
- 每条消息带 `schemaVersion`、`requestId` 和 `timestamp`；MVP 可用长度前缀 JSON，后续可换 protobuf。
- sidecar 只响应本机已认证的 NightDesk 进程；管道本身不承载画面数据。

建议的最小命令集：

```text
StartSession
  displayId / deviceName / expectedWidth / expectedHeight / desktopX / desktopY
  fps / listenAddresses / certificateFingerprintAllowlist
StopSession
  reason
OpenPairingWindow
  ttlSeconds
RevokeDevice
  fingerprint
GetStatus
SetLogLevel
Shutdown
```

建议的事件集：

```text
StateChanged
  idle | starting | listening | clientConnected | clientDisconnected | stopping | faulted
PairingWindowChanged
ClientConnected / ClientDisconnected
FrameStats
  fps / bytesSent / dirtyTiles / encodeMs
Fault
  code / message / recoverable
```

所有命令都要有超时和幂等语义。NightDesk 不能在 UI 线程等待 sidecar；应使用异步状态机，并把 `starting/stopping` 显示为可诊断状态。

### 5.4 生命周期和崩溃恢复

NightDesk 是唯一生命周期管理者：

1. 用户打开远程画面功能。
2. NightDesk 确认目标显示器稳定、恢复快照状态合法、没有禁止操作的现场条件。
3. NightDesk 启动 `nx-host.exe`，通过 Job Object 设置 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`，防止 NightDesk 退出后残留 host。
4. sidecar 打开命名管道并报告 ready；NightDesk 再下发目标显示器和监听配置。
5. 会话结束或用户关闭功能时，先停止 sidecar 输入/网络，再允许 NightDesk 执行显示恢复。
6. sidecar 异常退出时，NightDesk 应阻止继续修改显示拓扑，显示明确错误并允许安全停止。

绝对不能让 sidecar 自己调用 VDD 管理、改变分辨率或切换“仅虚拟屏”。如果 sidecar 在显示恢复之前仍持有采集句柄，可能导致 VDD 删除失败、画面黑屏或恢复验证失败；这是集成中最需要测试的故障路径之一。

### 5.5 指定显示器采集与坐标修正

当前 go-nx 只抓主屏。NightDesk 集成必须先完成两项改造：

1. **采集目标**：由 NightDesk 传入目标显示器的设备名、期望宽高和桌面原点。sidecar 用显示器枚举/HMONITOR 信息重新校验，尺寸不符就拒绝启动，而不是静默抓错屏。
2. **输入映射**：客户端传来的是目标显示器局部像素；sidecar 先加上显示器桌面原点，再按虚拟桌面宽高归一化，并在 `SendInput` 中使用 `MOUSEEVENTF_VIRTUALDESK`。进程 DPI 感知应使用 per-monitor v2。

建议的 MVP 只支持：

- 一个指定显示器。
- 固定 SDR 色彩。
- 单一 DPI 缩放档位。
- 不跨屏拖拽鼠标。
- 显示器增删或分辨率变化时立即停止会话并报错。

多显示器、负坐标原点、竖屏和热插拔应作为后续阶段单独验证。即使最终 NightDesk 使用“仅虚拟屏”模式，也不能假设虚拟屏永远是主屏；必须把显示器身份和桌面坐标作为会话参数。

采集技术上，MVP 可以先把 GDI 从主屏改成目标显示器范围的 GDI 位图；如果目标 VDD 上 GDI 出现黑屏/撕裂/不完整，再评估 Windows Graphics Capture 或 Desktop Duplication。后者会引入 COM/WinRT、GPU 格式转换和更复杂的生命周期，不能和第一版 sidecar 同时做。

### 5.6 网络绑定与 Tailscale

必须去掉当前的全接口监听：

```text
错误：:7000（0.0.0.0 / ::）
正确：<Tailscale IPv4>:<UDP 端口>
      或 127.0.0.1:<UDP 端口>（仅本机控制端）
```

原因：

- NightDesk 现有 `Tailscale Serve` 只代理 HTTPS/TCP 到 `127.0.0.1:17645`，不能承载 QUIC/UDP。
- 把 UDP 7000 监听在所有接口上，会把数据面暴露给 LAN、VPN 和其他接口。
- tailnet 直连仍然需要应用层 mTLS 和设备白名单；Tailscale ACL 是纵深防御，不是替代品。

集成后应使用 NightDesk 已知的 tailnet 地址或 Tailscale LocalAPI 查询地址，明确绑定。tailnet 未启用、地址变化或端口被占用时，sidecar 应 fail closed，不要自动退回 `:7000`。

### 5.7 Windows 端安全改造清单

进入任何真实配对前，必须完成：

- [ ] `require_auth` 默认改为 true，且没有“关闭认证”的产品开关。
- [ ] sidecar 只接受 NightDesk 下发的设备证书指纹白名单。
- [ ] 移除明文密码作为数据面长期凭据；如保留，只能作为一次性恢复手段并加密存储。
- [ ] 主机证书私钥使用 DPAPI/CNG 或受 ACL 保护的 NightDesk 密钥存储，不只依赖 `%AppData%` 文件权限。
- [ ] 所有监听地址显式绑定，禁止 `:port` 通配。
- [ ] 配对窗口有短 TTL、一次性和撤销语义；撤销后立即关闭已建立的连接。
- [ ] 控制连接、输入和帧流都有上限、超时和速率限制。
- [ ] 记录连接、拒绝、配对、撤销、异常退出和显示目标，不记录键入内容。
- [ ] 安装包对 `nx-host.exe` 做签名和完整性校验；升级时先停止旧进程。

### 5.8 Windows 端工作量判断

以“单显示器、静态 UI、一个控制端、无音频/剪贴板/文件、Tailscale 直连”为 MVP，单人熟悉项目的前提下，合理估算为：

- 协议公开化和 sidecar 拆分：3–5 个工作日。
- 指定显示器采集、DPI 和输入坐标：5–8 个工作日。
- IPC、生命周期、Job Object、日志和故障恢复：3–5 个工作日。
- 鉴权/配对/撤销和网络绑定：4–6 个工作日。
- WPF 接入、状态 UI、测试和打包：4–6 个工作日。
- 真实 VDD/Windows 回归和问题修复：至少 1–2 周，取决于现场环境。

合计约 **4–6 周** 才能得到“可演示、可在受控环境使用”的 PoC。这个估算不包含多显示器、音频、剪贴板、文件传输、正式代码签名发布和长期产品化。

---

## 6. Android 手机端集成方案

### 6.1 当前 Android 端的真实起点

NightDesk Android 当前是 HTTP 控制端，不是画面客户端：

- Kotlin，`minSdk 29`，`targetSdk 35`，版本 1.0.4 / versionCode 5。
- 只用 `HttpURLConnection`，没有 QUIC、UDP、WebSocket 或原始 socket 数据面。
- 没有 `SurfaceView`、`TextureView`、视频解码、触摸远控或输入法桥接。
- 没有前台服务、网络切换处理、连接保活或远程输入会话状态机。
- 已有最多 20 台主机的配置、扫码、Keystore AES-GCM 加密和更新分发，这些可以复用配置/配对外壳，但不能复用数据面。

因此 Android 不是“加一个 Activity”就能完成，而是新增一个完整的远程画面客户端。

### 6.2 两条候选路线

| 路线 | 优点 | 缺点 | 判断 |
|---|---|---|---|
| Kotlin/Java 原生 QUIC 客户端 | UI、触摸、IME、前台服务最自然；包体小；生命周期可控 | 需要可靠的 Android QUIC + 自定义 ALPN + 双向/单向 stream 支持；TLS/Keystore 集成复杂 | 推荐作为产品主线，但必须先做传输探针 |
| GoMobile 把 go-nx 协议核心编成 AAR | 可复用 Go 的 QUIC、tile 解码和输入语义；协议一致性最好 | Go 网络/证书/移动生命周期与 Android 结合复杂；AAR/JNI 调试困难；仍需 Kotlin UI | 可作为快速 PoC 或协议参考，不应默认当作低风险方案 |

已经做过的探针只证明了基础 AAR 可以生成：

- `/tmp/go-nx-mobile-probe/probe.aar` 约 1.5 MB。
- `libgojni.so` 约 2.6 MB。
- 探针只引用 `proto`/`tile` 的常量和函数，没有验证完整 QUIC 客户端、帧解码、JNI 回调、Android 网络切换或 Keystore。

因此不能把“AAR 构建成功”写成“Android 客户端可行已证明”。

### 6.3 原生客户端的最小数据流

建议的 Android 会话状态机：

```text
Idle
  → ResolvingHost（MagicDNS / Tailscale 地址）
  → Connecting（QUIC + TLS 1.3 + ALPN=go-nx）
  → Authenticating（设备证书/票据）
  → Streaming（接收 frame stream、解码 tile、渲染、采集触摸）
  → Reconnecting（网络切换或短暂中断）
  → Closing / Error
```

接收端必须完成：

1. 维护目标显示器的完整 BGRA 帧缓冲。
2. 按 `FrameHeader` 校验尺寸；尺寸变化时重新分配并清空。
3. 解析 solid/palette/raw tile，应用脏矩形。
4. 将 BGRA 转成 Android 可绘制的 RGBA/ARGB 数据，避免每帧全屏转换和 GC 抖动。
5. 只把脏 tile 上传到渲染表面。
6. 处理 `FrameHeader.seq` 回退/重复、超大帧、截断 payload 和恶意 tile 索引。
7. 在 UI 线程之外完成网络和解码，渲染线程只消费最新帧。

MVP 可用 `SurfaceView` + `Bitmap`/Canvas，按脏矩形局部更新；如果卡顿，再评估 `TextureView`、OpenGL ES 纹理和 BGRA shader swizzle。不要一开始就引入 Vulkan 或复杂渲染管线。

### 6.4 触摸、鼠标和键盘

Android 端需要把触控语义映射到 go-nx 输入：

| Android 输入 | 建议映射 |
|---|---|
| 单指点击 | 左键 down/up |
| 单指拖动 | 鼠标移动（可配置为相对/绝对） |
| 长按 | 右键 click 或上下文菜单 |
| 双指上下/左右 | 滚轮/水平滚轮 |
| 双指缩放 | 先作为本地画面缩放，不发送到主机；后续再评估 |
| 物理/蓝牙键盘 | 虚拟键码 down/up，需处理 Meta/Ctrl/Alt/Shift |
| 软键盘英文/数字 | 可先映射常见键码 |
| 软键盘中文/Unicode | **必须扩展协议**，见下 |

当前 go-nx 的协议只有 `KindKeyDown`/`KindKeyUp` 和 Windows 虚拟键码，没有 Unicode 文本、IME 组合或提交事件。Android 的 `InputConnection.commitText()` 产生的是文本，不可能可靠地还原成某个 Windows VK。中文输入、日文输入、表情和任意 Unicode 都需要：

- 新增 `TextInput`/`TextCommit` 事件（UTF-8）。
- 主机端用 `SendInput` 的 Unicode 注入路径处理提交文本。
- 明确组合输入、退格、删除、回车上屏的边界。
- 增加协议版本协商，避免旧 host 误解新事件。

这是 Android 端的产品级门槛，不是可选优化。若只做英文快捷键和鼠标点击，可以先绕过；若 NightDesk 的目标包含中文日常输入，必须单独排期和验收。

坐标映射也要考虑：

- 画面按比例缩放、letterbox 和用户平移后的逆变换。
- 目标显示器原点和多屏虚拟桌面。
- 触摸点对应的远端像素必须在显示器范围内。
- 屏幕旋转、分屏、折叠屏和显示尺寸变化。

### 6.5 前后台生命周期与网络切换

Android 远控会话必须在前台运行，否则系统会冻结或杀死网络连接：

- 使用前台服务，通知渠道明确显示“远程控制会话进行中”，提供断开按钮。
- Android 14+ 需要合适的 `foregroundServiceType` 和权限声明；具体类型应以目标 SDK 的官方要求为准。
- 会话期间持有必要的 wake lock/网络锁，但退出后立即释放。
- 监听 `ConnectivityManager.NetworkCallback`，处理 Wi-Fi/蜂窝切换、Tailscale 重连和 DNS 变化。
- QUIC 能迁移路径时要验证迁移；不能迁移时必须明确进入 `Reconnecting`，重新握手并重新鉴权，不能假装连接仍然可用。
- 锁屏、Doze、省电模式和厂商后台限制必须实测；不能以模拟器结果代替真机结果。

现有 Android 端没有这些基础，属于新增工程。

### 6.6 证书、Keystore 与主机 pin

go-nx 的主机身份是自签证书，客户端按主机地址/名称 pin 指纹。Android 端需要解决两个不同问题：

1. **传输层**：QUIC 客户端必须支持 TLS 1.3、客户端证书、ALPN `go-nx` 和自定义证书校验。
2. **密钥保护**：设备私钥不能以明文放在 SharedPreferences 或普通文件中。

可选方案：

- **PoC**：Go/native 生成设备 EC 私钥，使用 Android Keystore 的 AES-GCM 密钥包裹后保存；运行时解包到内存。实现简单，但私钥会出现在进程内存。
- **产品化**：让 QUIC/BoringSSL 的签名路径回调 Android Keystore 的非导出私钥；安全更好，但需要 JNI/自定义 TLS 集成，工作量和兼容性风险明显更高。
- **不接受**：把设备私钥明文写入应用目录，或让所有手机共享一个固定密码。

主机 pin 应按“MagicDNS 主机名 + 主机证书 SHA-256 指纹”保存，而不是只按 IP。二维码配对可以携带主机名、指纹和一次性配对票据；票据只用于建立信任，不能替代之后的证书校验。撤销设备时，NightDesk 必须同时删除白名单并断开 active QUIC 连接。

### 6.7 QUIC 库选择的前置验证

Android 原生客户端的第一项工作不是写 UI，而是做一个最小传输探针，验证目标库是否支持：

- UDP/QUIC。
- TLS 1.3 客户端证书。
- 自定义 ALPN `go-nx`。
- 一条可靠双向 stream。
- 多条主机发起的单向 stream。
- stream 取消、ACK 后下一帧、连接迁移/重连。
- Android 10–15 真机和至少一种 Wi-Fi/蜂窝切换场景。

不建议直接基于 HTTP/3 高层 API 实现，因为 go-nx 使用的是自定义 ALPN 和原始 QUIC stream，不是普通 HTTP 请求。若现有库无法满足，正确选择是：

- 为 go-nx 写一个受控的 JNI/QUIC 客户端；
- 或把 Go 客户端封装成 AAR，并显式承担 Go/Android 生命周期成本；
- 或重新评估协议是否要改成 HTTP/3/WebTransport，但这会改变 Windows 端和协议兼容性，不能顺手做。

### 6.8 Android 端工作量判断

在 Windows sidecar 已经稳定、协议已经版本化的前提下：

- QUIC/TLS/证书探针：1–2 周。
- tile 解码、脏矩形渲染、尺寸/方向处理：1–2 周。
- 触摸、鼠标、滚轮、软键盘和物理键：1–2 周。
- 前台服务、网络切换、重连、错误恢复：1–2 周。
- 配对/Keystore/主机列表整合、测试和打包：1–2 周。
- 中文 IME、4K、真机兼容和体验打磨：另计，通常再增加数周。

因此“能看、能点、能输入英文/数字”的 PoC 约 **6–10 周**；达到可长期使用的产品级质量通常需要 **3–6 个月**，且前提是 QUIC 库和显示/输入边界都能通过前面的门禁。这个估算不是承诺，必须用探针结果校正。
---

## 7. 授权、配对、网络与 Tailscale 安全模型

### 7.1 信任边界

建议把系统分成三个明确的信任域：

```text
NightDesk 控制面
  HTTPS + Tailscale Serve + Bearer/设备令牌
  负责：状态、显示/电源命令、配对票据、撤销、审计

nx-host 数据面
  QUIC + TLS 1.3 + 客户端证书 + 主机证书 pin
  负责：画面、输入、会话接管

本地显示控制
  NightDesk 独占
  负责：VDD、拓扑、恢复快照、恢复验证
```

数据面不经过 Kestrel，也不经过 Tailscale Serve。这样做的目的不是“多一套安全系统”，而是让两类流量的生命周期、超时、吞吐和故障域分开：

- HTTPS 控制面可以低频、可重试、有明确响应。
- QUIC 数据面可以长连接、低延迟、按帧 ACK，不把画面塞进 HTTP 缓冲。
- 显示恢复只由 NightDesk 执行，sidecar 无法在恢复过程中偷偷改变拓扑。

### 7.2 推荐配对流程

1. 用户在 NightDesk 桌面端点“开启远程画面”。
2. NightDesk 检查目标显示器和恢复快照状态；显示不稳定时拒绝启动数据面。
3. NightDesk 生成一次性配对票据（短 TTL、单次使用、绑定当前用户会话和主机身份）。
4. Android/PC 控制端通过现有 HTTPS 控制面提交票据、设备显示名和设备证书指纹，或扫描包含主机名、指纹和票据的二维码。
5. NightDesk 记录设备指纹，并通过本地 IPC 向 sidecar 下发白名单；sidecar 只接受该指纹。
6. 控制端通过 tailnet 的 UDP 地址连接 sidecar，完成 TLS 1.3 握手和证书 pin。
7. NightDesk 显示“已连接设备”，提供断开/撤销。
8. 撤销时 NightDesk 先关闭 active QUIC 会话，再删除指纹，最后清理票据和 UI 状态。

关键原则：

- 二维码/票据不能替代之后的证书校验。
- 数据面密码不应是长期共享秘密；即使用密码做恢复路径，也必须加密保存并限速。
- 未配对设备默认拒绝，不提供“任何人都能连接”的 UI 开关。
- 主机证书变了要显示明确告警，不能自动接受新的 pin。
- 设备指纹应按主机名/用户可见名称显示，避免用户误配到同名设备。

### 7.3 Tailscale 的实际约束

NightDesk 现有的 `TailscaleService` 通过 `tailscale serve --https=443` 暴露 HTTP 控制面。这个能力不能直接复用为 QUIC 数据面：

- Serve 是 HTTPS/TCP 代理，QUIC 使用 UDP。
- QUIC 需要端到端 UDP 可达、正确的 MTU 和 NAT 行为；HTTP 代理不等价。
- 如果启用 Funnel，公网暴露风险会进一步放大，而本文明确不评估公网场景。

因此数据面应：

- 只在 tailnet 内使用。
- 绑定明确的 Tailscale 地址，不绑定 `0.0.0.0`。
- 依赖 tailnet ACL 做网络层限制。
- 仍然执行 mTLS、设备白名单和输入授权。
- 在 Tailscale 未运行、地址解析失败或路由不明确时 fail closed。

Android 端还应处理 MagicDNS 变化和 tailnet 重连。不能把一次 DNS 解析结果永久当作主机身份。

### 7.4 安全验收项

在 Windows PoC 进入真实环境前，至少满足：

- [ ] 默认拒绝所有未配对客户端。
- [ ] 监听地址非通配，端口可通过配置指定。
- [ ] 设备证书指纹可显示、可撤销，撤销立即断开会话。
- [ ] 主机证书 pin 有变化告警和人工确认流程。
- [ ] 配对票据短 TTL、一次性、绑定主机/用户。
- [ ] 无明文长期密码；私钥受 DPAPI/Keystore 或等价机制保护。
- [ ] 输入会话断线释放按键和鼠标按钮。
- [ ] 连接数、帧大小、tile 索引、控制消息长度都有上限。
- [ ] 失败登录/伪造指纹有速率限制和审计。
- [ ] 日志不记录键入内容、密码、完整 token 或私钥。
- [ ] 安装包签名、升级完整性校验和回滚策略明确。

---

## 8. 虚拟屏、DPI、多屏与恢复问题

### 8.1 与 NightDesk 恢复模型的冲突点

NightDesk 的显示恢复逻辑是高风险区域。go-nx 当前假设“启动时抓主屏，屏幕尺寸不变”，而 NightDesk 可能在虚拟屏接管、恢复、热插拔和 DPI 变化之间切换。冲突点包括：

- go-nx 启动时缓存宽高，尺寸变化后 `Grab` 会返回 frame size mismatch。
- 旧采集句柄可能在显示器被删除后继续存在，导致黑屏或恢复失败。
- 输入坐标基于启动时的屏幕尺寸，显示拓扑变化后会点错位置。
- 恢复快照期间如果 sidecar 仍在抓屏/注入，可能干扰恢复验证。
- 多显示器时主屏可能不是 NightDesk 想要共享的目标。

### 8.2 推荐的显示状态机

```text
Idle
  → Preparing（NightDesk 确认可恢复、目标显示器稳定）
  → HostStarting（sidecar 启动并校验目标显示器）
  → Sharing（采集/输入/QUIC 正常）
  → Stopping（停止输入和网络，关闭采集句柄）
  → Restoring（NightDesk 恢复显示，验证成功）
  → Idle
```

任何一步失败都要回到安全状态：

- 目标显示器不在预期位置/尺寸：sidecar 拒绝启动。
- 会话中拓扑变化：sidecar 立即停止采集和输入，上报 `DisplayChanged`。
- NightDesk 进入恢复流程：先关闭 sidecar，再恢复显示。
- sidecar 崩溃：NightDesk 不自动重试显示恢复，先确认系统状态。

### 8.3 DPI 和坐标测试矩阵

Windows 端至少要覆盖：

| 场景 | 检查 |
|---|---|
| 100% / 125% / 150% / 200% 缩放 | 采集尺寸和鼠标落点一致 |
| 1920×1080 / 2560×1440 / 3840×2160 | 帧尺寸、内存和编码延迟 |
| 竖屏/旋转 | 宽高变化、坐标方向 |
| 负坐标原点 | 虚拟桌面映射 |
| 目标显示器非主屏 | 采集目标、输入目标 |
| 虚拟屏热插拔 | 会话停止、恢复门禁 |
| 锁屏/UAC/安全桌面 | 明确不可用并提示，不假装成功 |
| HDR/受保护内容 | 黑屏/色彩异常可诊断 |

### 8.4 明确不能承诺的能力

即使 Windows sidecar PoC 成功，也不应默认承诺：

- UAC 安全桌面和登录界面远控。
- 锁屏状态下的画面和输入。
- DRM/受保护视频内容。
- 多用户同时控制。
- 多显示器无缝拖拽。
- 8K 或高刷新率。
- 音频、剪贴板、文件传输。

这些能力要么需要完全不同的 Windows 会话/权限模型，要么需要新协议和新采集链路，不能靠“把 tile 调小一点”解决。

---

## 9. 合成性能基准与解释

### 9.1 测试方法

在 SSH 主机 `AI` 的临时目录 `/tmp/go-nx-bench` 执行，条件如下：

- Ubuntu，Go 1.27.3。
- 12 核 Intel i7-8700，共享/有负载环境。
- `GOMAXPROCS=1`，刻意观察单核路径。
- 1920×1080 BGRA 合成图像。
- 只测 tile diff + encode。
- 不包含真实 Windows 抓屏、DIB 拷贝、QUIC 握手/传输、tile 解码、Android 渲染。
- 计时受共享主机负载影响，耗时有明显波动；字节数更稳定。

### 9.2 结果

| 场景 | 耗时范围 | 输出大小 | 解释 |
|---|---:|---:|---|
| 纯色首帧 | 22–44 ms | 6,630 B | 所有 tile 为纯色，压缩比极高 |
| UI/文本首帧 | 26–37 ms | 46,238 B | 接近静态界面，tile 结构重复 |
| 随机噪声首帧 | 102–139 ms | 8,302,560 B | 约 7.92 MiB，几乎不可压缩 |
| 无变化差分 | 3–11 ms | 无帧 | 只有逐 tile 比较，不发送画面 |
| 单 tile 更新 | 8–21 ms | 1 个 tile 的有效负载 | 适合小范围按钮/文本变化 |

### 9.3 如何解读

- 这些数字**不是端到端性能承诺**，不能直接拿来承诺手机端延迟或帧率。
- UI/文本场景的结果支持 `1080p + 8–10 FPS` 的 MVP 假设，但真实 Windows 抓屏和网络会增加成本。
- 随机噪声首帧接近 8 MiB，说明 go-nx 不适合视频/高动态内容；这是设计选择，不是 bug。
- 4K 的 tile 数约为 1080p 的 3.6 倍，CPU 比较和编码成本会显著上升，必须单独基准。
- 最终验收应在目标 Windows 机器 + 目标 VDD + 真实 tailnet 链路上测 P50/P95，而不是依赖本合成结果。

---

## 10. 分阶段实施计划与验收门禁

### 10.1 阶段 0：协议与仓库治理（2–4 天）

交付物：

- 冻结 go-nx 分析提交 `124331bc...`，建立可追溯的协议版本。
- 选择许可证并添加 `LICENSE`；补充准确的 `README.md`。
- 把可复用协议拆成公开包或独立模块，去掉 `internal` 对外部客户端的限制。
- 为 `proto`、`tile`、`remote` 增加跨语言固定向量测试，确保 Android/Windows 字节级一致。
- 增加协议版本字段或至少增加 `protocolVersion` 握手字段。

验收门禁：

- 新模块不依赖 Ebiten/systray/Windows 才能编译。
- Linux/Windows 都能跑核心测试。
- 许可证和第三方 notices 明确。

### 10.2 阶段 1：Windows sidecar 最小闭环（1–2 周）

交付物：

- 新建无窗口 `nx-host` target，不启动 Ebiten、托盘、单实例。
- 只用 `127.0.0.1` 和一个指定显示器完成 Windows 本机 loopback 会话。
- 支持指定显示器尺寸/原点参数，尺寸不符时拒绝。
- 输入坐标改为目标显示器 + 虚拟桌面映射。
- 命名管道控制、Job Object、基本状态事件。
- 默认拒绝、设备指纹白名单、显式监听地址。

验收门禁：

- Windows 上能连接、看到正确画面、点击准确位置。
- 断线后不残留鼠标按下/键盘按下。
- sidecar 退出后 NightDesk 能检测并安全停止。
- 不改变显示拓扑。

### 10.3 阶段 2：NightDesk 集成（1–2 周）

交付物：

- WPF 中的开启/停止、连接状态、配对、撤销和错误提示。
- 与 `DisplayController`、`recovery.json`、Tailscale 的生命周期联动。
- 目标显示器来自 NightDesk 的显示器模型，而不是 sidecar 自行选择。
- 结构化日志和故障事件。

验收门禁：

- 显示恢复前 sidecar 必定停止。
- 显示拓扑变化时侧车立即停止，不干扰恢复。
- 关闭 NightDesk 不留下 sidecar 进程。
- 升级/崩溃/重启不会产生重复实例。

### 10.4 阶段 3：Windows PoC 验收（1 周）

验收门禁：

- 1080p 静态 UI 稳定 30–60 分钟，P95 输入回显 ≤ 150 ms（以目标 tailnet 实测）。
- 静止时无明显持续带宽增长。
- 真实 VDD 上目标显示器采集和输入坐标通过 100%/150%/200% DPI 测试。
- 断电/网络中断/客户端强杀后输入释放、连接可恢复。
- 安全检查表全部通过。

Windows 阶段合计约 **4–6 周**。若阶段 1 的指定显示器采集或输入映射无法稳定通过，应停止，不要继续用 UI 包装未验证的数据面。

### 10.5 阶段 4：Android 传输探针（1–2 周）

交付物：

- 在目标 QUIC 库上完成 TLS 1.3、客户端证书、ALPN、双向控制流、单向帧流。
- 在至少一台 Android 10+ 真机和一个模拟器上跑通。
- 证明可以处理 stream 取消、ACK、重连和 Wi-Fi/蜂窝切换。

验收门禁：

- 不依赖 HTTP/3 高层 API 模拟原始 stream。
- 证书校验和主机 pin 可测试。
- 会话关闭后 socket/线程/前台服务都能释放。

### 10.6 阶段 5：Android 可用 PoC（3–5 周）

交付物：

- tile 解码、SurfaceView 渲染、触摸/鼠标/滚轮/物理键。
- 前台服务、通知断开、网络重连。
- 主机列表/扫码/Keystore 和现有 NightDesk 配置整合。
- 英文/数字输入可用；中文 IME 作为独立验收项。

验收门禁：

- 手机端 30 分钟会话稳定，无持续内存增长。
- 网络切换后不崩溃；不能恢复时给出明确错误。
- 中文输入若在范围内，必须通过真机测试；否则明确标为未支持。
- 手机端不会因为后台/锁屏静默掉线而无提示。

Android PoC 合计约 **6–10 周**；产品化（多机型、多网络、输入法、无障碍、长期稳定性、发布签名）通常需要数月。

### 10.7 阶段 6：可选能力（不纳入首个 PoC）

- 多显示器选择和无缝拖拽。
- 剪贴板、文件传输、音频。
- 权限分级、审计导出、双人协作。
- 硬件编码/视频模式。
- 自动更新协议的兼容矩阵。

每项都应单独评估，不应作为“顺手加上”的需求。

---

## 11. 风险登记册、Stop 条件与 Go/No-Go

### 11.1 风险登记册

| 风险 | 概率 | 影响 | 缓解措施 |
|---|---|---|---|
| go-nx 无许可证，无法合规分发 | 高 | 阻断 | 先补 LICENSE、第三方 notices、明确版权归属 |
| 指定 VDD 上 GDI 抓屏黑屏/不完整 | 中高 | 高 | 先在隔离测试机验证；必要时评估 WGC/DDA，但不混入首版 |
| 多屏/DPI 输入坐标错误 | 高 | 高 | 目标显示器参数化、虚拟桌面映射、DPI 测试矩阵 |
| 默认放行/全接口监听/明文密码 | 高 | 严重 | 默认拒绝、显式绑定、DPAPI/白名单、删除危险开关 |
| Tailscale UDP 路径不可达 | 中 | 高 | 绑定 tailnet 地址、ACL 测试、fail closed、不用 Serve 代理 |
| Android QUIC 库不支持自定义 ALPN/stream | 中高 | 阻断 Android | 先做传输探针，失败则改 JNI/GoMobile 或重新评估协议 |
| Android Keystore 无法直接给 QUIC 私钥签名 | 高 | 中高 | PoC 用 Keystore 包裹密钥；产品化再评估 JNI signer |
| 中文/IME 无法通过 VK 协议表达 | 高 | 高 | 增加 Unicode/Text 事件和协议版本，作为独立验收项 |
| 4K CPU 编码过慢 | 中 | 中高 | 目标硬件基准、降低 FPS、后续优化 tile/采集 |
| sidecar 与显示恢复互相干扰 | 中 | 严重 | sidecar 无显示权限；恢复前强制停止；故障注入测试 |
| UAC/锁屏/DRM 不可用 | 高 | 中 | 明确产品边界和用户提示，不承诺绕过系统安全边界 |
| 升级/签名/路径导致残留或重复实例 | 中 | 中 | Job Object、单实例检查、安装器先停旧进程、签名校验 |
| 协议无版本协商导致客户端错配 | 高 | 中高 | 握手版本字段、能力位、固定字节向量测试 |

### 11.2 Stop 条件

出现以下任一情况，应暂停投入并重新评估，而不是继续堆 UI：

1. 目标 VDD 上无法稳定获取正确画面，且 WGC/DDA 也无法在可接受工作量内解决。
2. 输入坐标在目标显示器/DPI 组合下无法准确落点，或会误触其他显示器。
3. sidecar 无法做到不影响 NightDesk 的显示恢复和 `recovery.json` 门禁。
4. 数据面仍然需要默认放行、明文密码或全接口监听才能工作。
5. Android 端找不到支持 go-nx stream 语义的可靠 QUIC 客户端，且 GoMobile 方案无法稳定处理生命周期。
6. 中文输入和软键盘是硬需求，但协议扩展和 Windows 注入无法通过真机验收。
7. 真实链路 P95 输入回显长期明显超过 150 ms，或网络切换后无法恢复。
8. 许可证/分发问题无法解决。

### 11.3 Go/No-Go 结论

| 决策 | 结论 |
|---|---|
| Windows 单虚拟屏、静态 UI、Tailscale 内网 sidecar PoC | **Go（有条件）**：先做 4–6 周 PoC，按门禁验收 |
| Windows 多显示器/音频/剪贴板/文件 | **暂缓**：不属于 go-nx 当前能力 |
| Android 完整远控 PoC | **Conditional Go**：Windows 协议稳定且 QUIC 探针通过后再启动，6–10 周起 |
| Android 产品级通用远控 | **No-Go（按当前方案）**：工程量、输入法、生命周期和多机型风险过高 |
| 直接复用 go-nx EXE/DLL | **No-Go**：应用层耦合不可接受 |
| 作为视频/游戏/音频串流内核 | **No-Go**：技术路线不匹配 |

---

## 12. 仓库治理、参考资料与最终建议

### 12.1 必须先处理的仓库问题

go-nx 当前缺少：

- `LICENSE`：无法判断复制、修改、分发和闭源打包的许可条件。即使作者相同，也建议明确许可证和版权声明。
- `README.md`：缺少构建、运行、协议、配置和安全说明。
- 版本 tag：集成方无法锁定兼容版本。
- 协议版本和兼容性策略：Android/Windows 客户端一旦分离升级，必须有版本矩阵。
- 准确的实现文档：现有设计文档中的 DDA、LZ4、xxHash 等描述与当前代码不一致，应以代码和测试为准更新。
- 第三方依赖 notices：quic-go、Ebiten、systray 等需要随分发包保留相应声明。

建议先建立一个独立的协议仓库/模块（或至少独立 tag），NightDesk 通过版本化依赖引用，而不是直接复制 `internal` 代码。这样 Windows 和 Android 可以共享测试向量，但不会把桌面 UI 依赖带进移动端。

### 12.2 关键源码索引

go-nx：

- `internal/proto/proto.go`：消息、FrameHeader、tile record、去重。
- `internal/remote/host.go`：QUIC 会话、ACK、帧超时和脏 tile 合并。
- `internal/remote/client.go`：控制流、帧接收和解码。
- `internal/remote/tls.go`：ALPN、TLS 1.3、mTLS、主机 pin、QUIC 参数。
- `internal/capture/capture_windows.go`：主屏 GDI 采集。
- `internal/tile/tile.go`：64×64 tile 差分和编码。
- `internal/inject/inject_windows.go`：SendInput 注入。
- `internal/trust/policy.go`：默认放行、配对和密码策略。
- `cmd/go-nx/host_windows.go`：监听 `:port`、主屏主机启动。
- `cmd/go-nx/main_windows.go`：Ebiten、托盘、单实例和 UI 入口。

NightDesk：

- `src/NightDesk/RemoteServer.cs`：现有 HTTPS 控制面。
- `src/NightDesk/RemoteAuth.cs`：设备令牌、配对票据和 DPAPI。
- `src/NightDesk/TailscaleService.cs`：Tailscale Serve。
- `src/NightDesk/DisplayController.cs`：VDD、恢复快照和安全门禁。
- `src/NightDesk/DisplayTopology.cs` / `NativeDisplays.cs`：显示拓扑。
- `src/NightDesk/SettingsStore.cs`：配置和恢复快照持久化。
- `android/app/src/main/java/dev/nightdesk/android/Core.kt`：Android 配置、HTTP 和 Keystore。
- `android/app/src/main/java/dev/nightdesk/android/MainActivity.kt`：Android UI 和交互。

### 12.3 建议的最终路线

**近期建议：先做 Windows sidecar PoC，不先做 Android。**

理由：

- Windows 侧可以复用现有 go-nx host 协议和 Windows 采集/注入，增量最小。
- NightDesk 已经拥有显示拓扑、恢复快照和 Tailscale 控制面，适合作为 sidecar 的管理者。
- Android 的 QUIC 客户端、Keystore、IME、前台服务和网络切换都是新增高风险工程，应该在协议稳定后再投入。
- 先验证“指定虚拟屏 + 正确输入 + 不影响恢复 + 默认拒绝”四个门禁，再决定是否把 go-nx 作为 NightDesk 的长期数据面。

**如果 NightDesk 的产品目标是通用、成熟、低延迟的手机远控，建议优先评估成熟的远程桌面/视频串流内核，而不是把 go-nx 扩展成通用远控。** go-nx 更适合作为一个轻量、可审计、针对静态 UI 的自定义协议；它的优势是简单、无损文字和静止时低带宽，不是视频级体验。

**一句话最终判断：**

> go-nx 可以成为 NightDesk 的 Windows 专用、单虚拟屏、静态 UI 数据面 PoC；Android 端技术上可做，但需要新客户端和大量移动端工程，当前证据不足以承诺短期产品化。直接复用 go-nx 桌面程序或把它当作 Sunshine/Parsec 替代品都不现实。

### 12.4 本次验证记录

在 AI 主机上已验证：

```bash
go test -count=1 ./internal/proto ./internal/tile ./internal/trust \
  ./internal/profiles ./internal/logging ./internal/remote

GOOS=windows GOARCH=amd64 CGO_ENABLED=0 \
go build -trimpath -o /tmp/go-nx-check.exe ./cmd/go-nx
```

结果：

- 上述核心包测试通过。
- Windows amd64 交叉构建通过。
- 生成 PE32+ Windows console 可执行文件，约 25 MB。
- GoMobile 基础 AAR 探针生成成功，但只覆盖 `proto`/`tile` 常量与函数，不代表完整 Android 客户端可行性。

未验证：

- 真实 Windows/VDD 上的指定显示器采集。
- 真实 Windows 上的 DPI/多屏坐标。
- Windows 锁屏/UAC/受保护内容。
- 真实 tailnet 上的 Android QUIC 客户端。
- Android Keystore 签名链路。
- 中文输入和软键盘。
- 4K、长时间运行和多机型兼容。

这些未验证项必须在进入产品开发前逐项转为可重复的测试和验收门禁。