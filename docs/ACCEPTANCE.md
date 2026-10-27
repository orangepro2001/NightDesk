# NightDesk 验收记录

更新日期：2026-10-27（Asia/Seoul）
工作目录：`C:\dev\NightDesk`
本轮范围：复核移动端远控光标不可见问题并修复 Windows GDI 捕获端；Windows 1.6.1 已发布。Android 手势与释放体验维持 1.3.0。

## 1. 当前交付状态

| 项目 | 状态 |
|---|---|
| 最新实现提交 | `ba61db096fc631d02e0399559c8b909b6d519706` |
| Windows | 1.6.1 / 10601，AI ZIP 已发布；生产桌面未替换 |
| Windows ZIP | 85184039 字节，SHA-256 `ae3de5075dca53b13d55d36409520c08f89fa405bb3d5fbcf1f67e624e64b7ae` |
| Windows EXE | 201922308 字节，SHA-256 `962dbc32ec341661e6292c68c1331803f8b4475a896d834661ca70e9410fb74a` |
| nx-host | 0.2.1，7940608 字节，SHA-256 `88233ba806d9a7ebe4bda5c66a2e4c61e0d9dc9b43c48787170ca68e6087b9f0` |
| Android | 1.3.0 / 9，55075280 字节，SHA-256 `9e278bafb126b18963764fd829414075c30ba9ee795b39d4ad2f1db443d7aa42` |
| Android 证书 | `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767` |
| 服务 | active / enabled；healthz = ok android=ok windows=ok |

[双平台下载页](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk) · [Windows ZIP](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/windows/latest.zip) · [Android APK](https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/latest.apk)

完整恢复说明见 `C:\dev\NightDesk\docs\20261027-123458-windows-cursor-capture-1.6.1-handoff.md`。

## 2. 自动化结果（本轮已复核）

| 检查 | 结果 | 边界 |
|---|---|---|
| Windows Release | 210 项通过；0 warnings / 0 errors | 无显示状态改变 |
| WPF 布局 | 980/1180 DIP × 五页通过；两组三字段独立列 | 纯 XAML，无应用服务 |
| 更新下载 | 真实 HTTPS -> manifest -> 85MB ZIP -> 双 EXE 哈希/解包通过 | 临时缓存，不安装生产程序 |
| 安装 helper | 等待测试进程、替换双合成文件、保留备份、重启合成 WinExe 通过 | 临时目录，不是真实升级 |
| Feed | 5 Python tests 通过 | Android 兼容、Windows 路由、前缀代理、双平台页面 |
| Android JVM | 36 项通过 | CoreTest 10 + NxConnectionTest 10 + NxTest 7 + RemoteGestureTest 9 |
| Android lint/build | 0 errors / 23 warnings；release/debug/AndroidTest 构建通过 | AI 主机 |
| Android 仪器 | OK (6 tests) | HostStore 3 + MainUi 1 + RemoteControlUI 1 + UpdateFlow 1 |
| Android 手势 | 点按/移动/长按拖动/双指滚动/取消/强制释放覆盖通过 | 模拟器与 JVM；非真实 Windows 输入验收 |
| 线上清单/HEAD | Windows 1.6.1 / Android 1.3.0；HTTP 200，长度/哈希一致 | 更新源发布，不代表真实远控验收 |
| VDD 载荷 | 原有大小/签名/提取/篡改拒绝断言通过 | 不执行真实驱动安装 |

Windows 没有 Authenticode 签名；HTTPS + SHA-256 不替代独立发布签名。生产升级、真实 GDI/SendInput、DPI/UAC、物理 Android 和双机 tailnet 仍待人工验收。

### 2.1 移动远控光标复核（Windows 1.6.1）

- 已追踪 Android `mapToHost` → 手势 `Move` → gomobile `Session.move` → QUIC `KindMouseMove` → Windows `SendInput`，坐标链路可到达输入端。
- 找到并修复可见反馈断点：Windows GDI `BitBlt` 不采集系统鼠标指针，而 Android 只显示接收的帧。现在 host 通过 `GetCursorInfo` / `GetIconInfo` / `DrawIconEx` 在发送前合成指针，再由现有 tile-diff/QUIC 送到 Android 和 Windows viewer。
- Go geometry 单测覆盖 primary/secondary/negative monitor origin 与 hotspot。AI 上 Go tests/vet 和 Windows/amd64 cross-build 通过；Windows 210 项测试通过；test-mode 合成协议探针 4 帧、538 tiles、7/7 receipts 通过。
- Windows 1.6.1 updater 已从真实 HTTPS feed 下载 ZIP、检查清单大小/SHA-256、解包并 staging 两个 EXE；**仅 staging，没有安装或重启**。线上 Windows manifest 为 1.6.1/10601，ZIP HEAD 200、长度匹配。
- Android 不需要升级；APK 保持 1.3.0/code 9。Android 仪器套件 6 项本次复跑通过。
- 真实 Windows cursor 像素 / 物理 SendInput / Android 真机及 DPI/UAC 仍需独立 Windows 测试机与 Android 真机。合成 `-inject log` 只证明协议操作回执，不是操作系统输入验收。


## 3. go-nx 远程控制（阶段 1-7）

### 3.1 已实现（真实抓屏/注入仍需独立测试机验收）

- Windows 以独立 `nx-host.exe` sidecar 提供数据面，WPF 通过 stdin/stdout JSON 行协议管理；sidecar 受 Job Object 约束，异常退出后清除陈旧的 running / ready 状态。
- sidecar 具备单显示器 GDI 采集与 **64 × 64 tile 差分**路径；不是 H.264/HEVC/AV1 视频编码。**真实 GDI 抓屏尚未验收。**
- 传输使用 **QUIC + TLS 1.3**，Android 固定主机证书指纹；配对使用 6 位短期配对码，二维码包含 `nightdesk-nx` 载荷、Tailscale IPv4 地址和指纹。
- Windows 输入具备 `SendInput` 路径（鼠标移动、按键、滚轮、虚拟键和 Unicode 文本）。**真实 SendInput、DPI 映射与 UAC/安全桌面行为尚未验收。**
- `nx-host -test-mode` 强制要求 `-source pattern -inject log`，否则退出码 2；使用临时 identity/trust 目录，不打开 GDI、不调用 SendInput。
- `nx-probe.exe` 使用与 Android 相同的 pinned TLS、配对码、QUIC 和 tile 解码路径；对发布版 `nx-host.exe` 实测 1920×1080、4 帧、538 tile 更新、7/7 输入回执、首末帧摘要不同、host 退出码 0。
- Android `NxConnectionModel` 提供显式连接阶段（Connecting / Streaming / Failed / Disconnected / Background）、20 秒前端截止时间、失败分类与唯一恢复动作。
- 失败处理：`bad-code` / `untrusted` 引导重新配对；`host-identity` 要求重新扫码；其余网络错误可重试；**绝不自动重连**。
- 生命周期：切后台、Activity 销毁、断线时释放鼠标按键、滚轮、键盘修饰键和 QUIC 会话；旧 attempt 的 `streaming` / `error` / `closed` / frame / input 回执不会覆盖新会话。
- Android 1.3.0：轻点抬手后发送一次左键点击；普通单指滑动仅移动鼠标；长按后才开始拖动；双指滚动；拖动抬手落在黑边仍释放；取消、失焦、后台及“释放”按钮执行全量释放。自动化手势测试通过，但真实 Windows SendInput 仍未验收。

### 3.2 自动化覆盖

- Go Linux：`go vet ./...`、`go test ./...` 通过；Windows 交叉 `vet` / `build` 通过，8 个交叉编译测试二进制在 Windows 本机全部 PASS。
- `nx-probe` 合成源端到端：4 帧、538 tile 更新、动态摘要、7/7 输入回执，全程不抓屏、不调用 SendInput。
- `NxConnectionTest` 10 项：连接成功、超时、错误分类、重试、旧 attempt 隔离、后台、断开、持久化时机等。
- `NxTest` 7 项：NX 二维码载荷解析、地址/指纹校验等。
- Windows 210 项测试覆盖 NX 配对载荷、Viewer 地址/指纹/wire/tile、真实 test-mode 主机连接、Tailscale 地址校验、sidecar 状态清理、VDD 载荷校验和显示快照等；不触碰显示拓扑、不调用真实 SendInput。

### 3.3 尚未验收（硬阻塞）

以下项目**必须在独立 Windows 测试机 + Android 真机上执行**，不能使用本机生产桌面：

- [ ] 真实 GDI 抓屏首帧、tile 差分、画面完整性与长期稳定性。
- [ ] 真实 `SendInput`：鼠标移动/点击、滚轮、Unicode、组合键、修饰键释放。
- [ ] Android 真机通过 tailnet 扫码连接，验证证书固定、错误配对码、二维码过期、另一台设备接管。
- [ ] 断网、切后台、强制停止、Windows sidecar 崩溃后的恢复行为。
- [ ] Android 真机验证轻点仅点击、单指滑动不产生框选、长按拖动及抬手释放；用“释放”按钮和系统手势取消确认没有残留按键。
- [ ] DPI 缩放、锁屏、显示器休眠、虚拟屏下的抓屏结果。
- [ ] 记录首帧时间、稳态带宽、CPU、内存、输入回执延迟。

### 3.4 明确不支持

- 不是 Sunshine / Parsec 式视频串流，不支持音频、视频播放、游戏或高动态桌面。
- 不支持多显示器切换、剪贴板、文件传输、动态码率和硬件编码。

## 4. Android 多主机管理

### 已验证实现

- 最多保存 **20 台主机**。
- 以规范化后的 Tailscale HTTPS origin 去重；同一主机重新扫码只更新令牌，不新增重复项。
- 支持保存、Spinner 切换、删除当前主机；删除活动主机后自动回落到剩余第一台。
- 配置升级为 `schemaVersion 3`，兼容旧 `schemaVersion 2` 单主机数据迁移。
- 整个主机列表 JSON 使用 Android Keystore **AES-GCM** 加密后落盘；测试确认共享偏好中不存在明文令牌。
- 切换主机后立即刷新状态、显示器和更新相关界面数据；控制源与固定更新源保持分离。

### 仪器测试覆盖

`HostStoreInstrumentedTest` 已在 `emulator-5580` 通过：

1. 两台主机持久化、切换 active、删除后回落。
2. 同一 origin 再次保存时替换令牌且不增加主机数。
3. 令牌不以明文写入 SharedPreferences。

完整 Android 测试运行时若只执行 `am instrument` 而不传入 `updateOrigin`，更新端到端用例会按设计报告 `missing updateOrigin`。正确命令见第 2 节。

### 真机仍需验收

- [ ] 在物理 Android 设备安装 1.0.4，授权相机并扫码绑定两台不同 NightDesk 主机。
- [ ] 在 Spinner 中切换两台主机，确认状态、显示器和控制命令发送到对应主机。
- [ ] 重启 Android，确认两台主机和当前选择仍保留，令牌无需重新输入。
- [ ] 删除非活动主机，确认活动主机不变；删除活动主机，确认回落到另一台并可继续控制。
- [ ] 对同一主机重新扫码，确认令牌更新但不产生重复主机。
- [ ] 验证锁屏、后台限制、Tailscale 断线/重连时的错误提示，且不会误发显示命令。

## 5. Parsec VDD 随包安装

### 实现流程

1. 程序启动时通过 `VddClient` 打开 Parsec VDD 设备接口。
2. 接口可用：视为驱动已安装，**不重装、不替换**。
3. 接口不可用：
   - 从单文件 EXE 的 EmbeddedResource 提取 `parsec-vdd-0.45.0.0.exe`；
   - 找不到内嵌载荷时，回退到程序目录 `vdd\parsec-vdd-0.45.0.0.exe`；
   - 校验大小 `517256` 字节、SHA-256 `e23332448fdaf5aa017cb308db5ef6855fac526a7ded05d80c039404126d5362`、签名主体 `Parsec Cloud, Inc.`；
   - 任一校验失败即拒绝执行；
   - 通过 `runas` 请求 UAC，并使用官方安装器 `/S` 静默安装；
   - 等待安装器退出后重新探测设备接口。
4. 每用户环境自动尝试一次；取消 UAC 或安装失败后不会每次启动重复弹窗，界面按钮可手动重试。
5. 安装失败不阻止程序打开；缺少驱动时仅不能添加或使用 VDD。

### 已验证内容

- [x] 安装器已作为 `EmbeddedResource` 打入 `NightDesk.exe`。
- [x] 单文件发布后仍能提取载荷。
- [x] 大小、SHA-256 和签名主体校验通过。
- [x] 篡改载荷不会执行。
- [x] 本机已安装驱动时走“已安装、不重装”路径。
- [x] UI 缺失驱动时显示安装按钮和手动重试入口。

### 必须补充的端到端验收

当前机器已有 Parsec VDD，且现场依赖其中一个虚拟输出，不能为了测试卸载/重装驱动。因此下列项目必须在干净 Windows 10/11 x64 虚拟机或可回滚测试机上执行：

- [ ] 未安装 Parsec VDD 的干净系统直接运行 1.6.0。
- [ ] 确认 UAC 出现，授权后安装成功，设备管理器中可见 `Parsec Virtual Display Adapter`，无需手工下载安装器。
- [ ] 拒绝 UAC 后程序仍可启动；同一次用户环境再次启动不重复弹窗；点击“安装 Parsec VDD 驱动”可重试。
- [ ] 安装器返回非零或超时后界面给出明确错误，且不进入可用状态。
- [ ] 已安装驱动的系统升级/启动 1.6.0 时不会重装或替换驱动。
- [ ] 在无网络环境运行，确认内嵌安装器仍可使用（安装器自身若需联网组件，需记录实际行为）。

## 6. 当前桌面现场安全状态

- 2026-10-27 08:07 KST 只读检查：Desktop NightDesk PID 23116、正式 nx-host PID 20232 正在运行；监听 100.109.77.29:7000，primary / 10 FPS，使用正式状态目录。
- 恢复快照存在，352 字节，修改时间 07:03:42。物理输出和快照恢复尚未独立验收。
- 本轮没有替换 Desktop 文件、强杀/重启正式进程、切屏、恢复快照、增删 VDD 或执行真实抓屏/SendInput。
- PID 会随用户操作变化，不要据此强杀进程。首次升级 1.6.0 必须确认现场可恢复后通过 UI 正常退出，再部署同目录两个 EXE。
- 在恢复未验证前，不切屏、不恢复快照、不操作 VDD、不运行第二个正式 host。合成测试必须使用 -test-mode -source pattern -inject log。


## 7. 人工验收清单（硬件相关，尚未执行）

在现场操作人确认有活动物理屏幕、且 `recovery.json` 可恢复后再执行。每一步失败都停止并先恢复布局，不要自动重试 `add` 或 `toggle`。

### A. 前置条件

- [ ] 退出 `ParsecVDisplay.exe` 或其它 Parsec VDD 管理器；不要让两个管理器同时运行。
- [ ] Windows 处于本机控制台用户会话，不是 RDP。
- [ ] Windows 显示设置中至少有一个活动物理输出；先运行只读诊断保存基线。
- [ ] `tailscale serve status --json` 已确认无不相关的现有绑定，或已记录其归属；NightDesk 不会覆盖其它绑定。
- [ ] 远程串流软件已准备好，现场人员知道如何从 Windows 显示设置 / Win+P 恢复输出。

### B. 虚拟显示器

- [ ] 输入受驱动支持的分辨率和整数刷新率，例如 `1920 × 1080 @ 60 Hz`，点击“保存默认值”。
- [ ] 点击“添加显示器”，确认列表出现本程序创建的索引和模式；Windows 显示设置能看到新输出。
- [ ] 添加第二个显示器，确认多个本程序拥有的索引都能显示。
- [ ] 删除一个选中的 NightDesk 显示器，确认其它显示器和物理输出仍在。
- [ ] 恢复或删除最后一个 NightDesk 显示器前，确认不会删除最后一个活动输出。

### C. 熄屏 / 点亮与恢复

- [ ] 点击“熄灭物理屏幕”：程序应先保存恢复快照，必要时自动创建虚拟显示器，再仅启用该虚拟源。
- [ ] 从远程串流端确认虚拟画面可用；无信号、待机或背光行为以显示器固件为准。
- [ ] 用界面按钮、托盘菜单和配置后的全局快捷键分别验证点亮路径。
- [ ] 点亮后确认原物理显示源恢复，确认恢复成功后恢复快照被清理。
- [ ] 在切换期间强制关闭或模拟失败只在具备现场恢复能力时测试；确认应用保留 `recovery.json` 并拒绝在未恢复时清理虚拟屏。

### D. 防休眠与快捷键

- [ ] 勾选“保持电脑唤醒”，确认 UI 状态为启用；取消后确认状态释放。
- [ ] 关闭窗口只进入托盘；从托盘选择退出，确认先恢复布局再退出。
- [ ] 将快捷键改为一个未被占用的组合，确认按键可切换；再测试冲突组合，确认原快捷键不会被静默覆盖。

### E. Tailscale HTTPS、扫码绑定与受信任设备

- [ ] 部署 1.6.0 后确认远程地址为当前节点的 `https://<node>.ts.net/`。
- [ ] 点击“扫描二维码绑定”，确认票据 5 分钟内有效且二维码不包含可见令牌。
- [ ] Android 扫码成功；桌面设备列表出现设备名与绑定时间；Android 重启后仍可控制。
- [ ] 同一二维码再次配对失败；超过 5 分钟后配对失败。
- [ ] 选择设备并“删除所选绑定”；该设备下一次请求返回 401，其它设备仍可用。
- [ ] 读取状态、执行 `on` / `off` / `restore` / `prevent-sleep`；确认无令牌、错误令牌、已撤销令牌和未知命令均被拒绝。
- [ ] 点击“关闭远程控制”，确认只有归属指纹仍一致时才删除绑定；配置被外部修改时必须拒绝并提示人工检查。
- [ ] 验收完成后再次检查 `tailscale serve status --json`；不要启用 Funnel。

### F. AI 更新源

- [x] `nightdesk-update-feed.service` active / enabled，仅监听 `127.0.0.1:17768`。
- [x] 真实 Tailscale HTTPS 下载页、manifest、latest.apk 均返回 200，版本与哈希一致。
- [x] 未启用 Funnel，其它 Serve 路由未被删除。
- [ ] 重启 AI 后确认服务自动恢复、URL 仍可用。
- [x] 1.3.0 发布时确认 APK 先落盘、版本别名次之、manifest 最后原子切换；服务仅在大小与 SHA-256 匹配时提供下载。旧版 1.2.0 / code 8 APK 保留。

### G. NX 远程控制端到端（需独立测试机与真机）

- [ ] 在独立的 Windows 11 / Server 2022 测试机部署同目录的 `NightDesk.exe` 与 `nx-host.exe`（1.6.0），确认 sidecar 可启动且有日志。
- [ ] 桌面“扫描二维码绑定”生成 NX 配对码，Android 真机通过 tailnet 扫码。
- [ ] 确认首帧、tile 差分、滚动、点击、右键、组合键和 Unicode 文本可用。
- [ ] 验证错误配对码、二维码过期、证书指纹变化、主机锁定、另一台设备接管和断网重连。
- [ ] 验证切后台、锁屏、强制停止后鼠标/按键状态被释放。
- [ ] 验证 sidecar 异常退出后 WPF 清除 running / ready 状态，并可重新启动。
- [ ] 记录首帧时间、稳态带宽、CPU、内存和输入回执延迟。
- [ ] 在第二台 Windows 11 NightDesk 第 06 区输入目标地址、配对码和主机指纹，确认控制窗口显示首帧。
- [ ] 验证鼠标移动/左键/右键/中键/滚轮、Ctrl/Alt 组合键和中文 Unicode 输入。
- [ ] 关闭控制窗口、切走焦点和强制断网后，确认目标端没有残留按键或鼠标按钮。
- [ ] 重启控制端并留空配对码，确认固定主机指纹和客户端身份仍可连接；更换目标证书时必须失败关闭。
- [ ] 在 Windows 10 上确认显示明确的 `.NET QUIC` 平台不支持错误，不尝试静默降级。- [ ] 确认不执行任何音频、剪贴板、文件传输或多显示器切换测试，因为这些能力未实现。

## 8. 故障恢复

1. 如果点亮失败，保持 NightDesk 运行，使用“恢复保存布局”或远程 `restore`，不要先删除虚拟屏。
2. 如果启动时提示有遗留快照，先恢复保存布局；启动不会悄悄切屏。
3. 如果物理屏幕不显示，用 Windows 显示设置 / Win+P 重新启用物理输出；必要时由现场人员处理接线和驱动。
4. 只有确认恢复成功后，才退出 NightDesk 或删除它创建的虚拟显示器。
5. 如果 Serve 配置被外部程序改动，保留现状并人工核对，不要强行执行 `tailscale serve ... off`。
6. 更新源异常时先查 `systemctl status nightdesk-update-feed` 和 `journalctl`；不要手工删除 manifest 或正在使用的 APK。
7. VDD 安装失败时不要手工替换内嵌载荷；先查看 UAC、安装器退出码和设备管理器状态，再使用界面按钮重试。
8. NX 连接失败时先看 Android 的失败分类：`host-identity` 必须重新扫码，`bad-code` / `untrusted` 重新配对，其它网络错误再重试；不要通过反复自动重连绕过。
9. sidecar 异常退出时，先确认 WPF 已清除状态，再检查 `nx-host.exe` 日志、证书文件和防火墙；不要在生产桌面直接抓屏排查。

## 9. 交付边界

- Android 1.3.0（versionCode 9）已在 1.2.0 多主机、扫码绑定、永久设备令牌、独立更新源和 go-nx 远控基础上，加入不误触框选的点按/移动/长按拖动手势、双指滚动和常驻全量释放操作；36 项 JVM、6 项仪器测试、构建、签名与线上更新验证通过。
- Android 1.3.0 已发布到 AI 更新源，APK 大小、SHA-256、签名及模拟器从 1.2.0 更新的流程已验证；真实 Windows 抓屏、SendInput、DPI/UAC、Android 真机手势和 tailnet UDP/QUIC **未验收**。
- Windows 1.6.1 / 10601 与 nx-host 0.2.1 已发布。捕获端将 Windows 系统指针绘入每帧，使既有移动输入位置可见；真实桌面抓屏像素、SendInput、DPI/UAC 和真机远控仍未验收。`Desktop\NightDesk.exe` 仍运行现有版本，本轮未替换；部署时必须将 ZIP 中两个 EXE 放在同一目录。
- 真正“缺少 VDD 时自动安装”的端到端路径必须在干净 Windows 环境验收；本机只验证了载荷、校验、提取和已安装路径。
- go-nx 屏幕远控是经配对和证书固定保护的独立数据面，不是通用远程桌面，不等同于 Sunshine/Parsec 视频串流。
- 应用不提供任意 shell、文件访问或通用桌面控制接口。
- “熄屏”是 Windows 显示拓扑切换，不保证每种显示器的面板电源、背光或无信号提示行为。
