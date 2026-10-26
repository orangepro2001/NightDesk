# NightDesk Windows 1.5.0：桌面控制桌面交接

更新时间：2026-10-27 06:45 KST（Asia/Seoul）
仓库：`git@github.com:orangepro2001/NightDesk.git`
功能提交：`d720ce2eb18a793b348c3be209d9dedc095bc79d`
状态：**Windows 桌面控制 Windows 已实现并通过合成主机协议、输入回执和控制窗口渲染冒烟；真实 GDI 抓屏、SendInput、tailnet UDP 和双机人工验收仍未完成。**

## 1. 结论

Windows NightDesk 现在可以直接控制另一台 Windows NightDesk，不再依赖 Android 扫码：

- 主窗口新增“06 / 桌面控制桌面”区域，可输入目标地址、6 位配对码和可选的主机证书指纹。
- 点击“打开桌面控制窗口”后，独立的 WPF 窗口显示远端 tile 画面，并把本地鼠标、滚轮、键盘和 Unicode 文本发送到远端。
- 首次配对成功后，Viewer 的客户端证书指纹会出现在目标端的受信任设备列表；后续连接不需要再次输入配对码。
- 目标主机指纹首次连接后固定保存。之后同一地址返回不同证书会失败关闭，不会静默接受新主机。
- 配对码只用于当前连接窗口，不写入 `settings.json`、日志或文档。
- 目标端“生成配对二维码”窗口新增“复制连接信息”，可直接复制地址、配对码和主机指纹到另一台桌面。

本次只新增控制端能力，没有修改 `nx-host.exe` 的采集和输入协议，也没有在真实生产桌面上新增抓屏或 `SendInput` 操作。只读检查发现 Desktop 旧候选与正式 sidecar 已在运行，处理边界见第 10 节。

## 2. 使用流程

### 2.1 目标桌面（被控端）

1. 启动同目录的 `NightDesk.exe` 与 `nx-host.exe`。
2. 在“05 / 低带宽远程控制”停止状态下，把监听地址改为目标桌面的 Tailscale IPv4 或 MagicDNS 地址，例如 `100.x.y.z:7000`。
3. 点击“启用低带宽远控”。
4. 点击“生成配对二维码”。
5. 在配对窗口点击“复制连接信息”。复制内容包含：
   - 地址；
   - 6 位一次性配对码；
   - 64 位主机证书 SHA-256 指纹。

### 2.2 控制桌面（控制端）

1. 在另一台 Windows 11 / Windows Server 2022 或更新版本的 NightDesk 中滚动到“06 / 桌面控制桌面”。
2. 粘贴目标地址。
3. 首次连接粘贴 6 位配对码；如果从目标端复制了主机指纹，也一并粘贴。
4. 点击“打开桌面控制窗口”。
5. 状态显示“已连接”后即可使用鼠标和键盘。
6. 后续连接可以留空配对码。Viewer 会使用本机固定保存的主机指纹和客户端证书身份。

如果目标端重装或更换了证书，新连接会被拒绝。确认这是预期变化后，在控制端点击“清除已固定指纹”，再重新配对。

### 2.3 地址格式

支持：

```text
100.x.y.z:7000
hostname.tailnet.ts.net:7000
[fd7a:115c:a1e0::1]:7000
```

省略端口时默认使用 UDP `7000`。不支持 `https://`、路径、查询字符串或未加方括号的 IPv6 端口写法。

## 3. 架构与数据流

```text
NightDesk WPF
  └─ NxViewerWindow
       ├─ WPF WriteableBitmap：BGRA tile 画面和脏矩形更新
       ├─ 鼠标/滚轮/键盘/Unicode 输入采集
       └─ NxViewerSession
            ├─ System.Net.Quic + TLS 1.3
            ├─ ALPN=go-nx
            ├─ 双向 control stream：Hello / ACK / Input / Text / Receipt
            ├─ 单向 frame stream：FrameHeader + tile records
            └─ 主机证书 SHA-256 pin

目标 Windows NightDesk
  └─ nx-host.exe
       ├─ 指定显示器 GDI 采集
       ├─ 64x64 BGRA tile 差分
       ├─ QUIC + mTLS
       ├─ 配对策略与客户端证书指纹信任
       └─ SendInput / Unicode 输入
```

这是控制协议复用，不是视频串流：

- 首帧后只传变化 tile；
- 单帧收到并渲染后才 ACK；
- 网络变慢时降低帧率，不无限积压帧；
- 仍然没有 H.264/HEVC/AV1、音频、剪贴板同步、文件传输或多显示器切换。

## 4. 代码边界

新增：

- `src/NightDesk/NxViewerProtocol.cs`
  - 目标地址解析；
  - 证书指纹规范化；
  - go-nx wire 编解码；
  - tile 解码；
  - 固定主机指纹存储；
  - DPAPI 保护的 Viewer 身份。
- `src/NightDesk/NxViewerSession.cs`
  - QUIC/TLS 连接；
  - Hello、ACK、Receipt；
  - 帧接收和 tile 应用；
  - 输入发送和状态释放；
  - 错误分类。
- `src/NightDesk/NxViewerWindow.cs`
  - WPF 画面窗口；
  - WriteableBitmap 脏矩形渲染；
  - 键盘/鼠标/滚轮映射；
  - 失焦和关闭释放输入。
- `tests/NightDesk.Tests/NxViewerIntegration.cs`
  - 启动真实 `nx-host.exe -test-mode -source pattern -inject log`；
  - 验证错误配对码、正确配对、首帧、下一帧、输入回执和指纹持久化。

修改：

- `MainWindow.xaml` / `.xaml.cs`：新增第 06 区、地址/配对码/指纹输入、清除固定指纹。
- `Models.cs`：新增地址和可选指纹设置；配对码不持久化。
- `SettingsStore.cs`：新增 `%LOCALAPPDATA%\NightDesk\nx-viewer`。
- `NxPairingWindow.cs`：新增桌面端“复制连接信息”。
- `NightDesk.csproj`：版本提升到 `1.5.0`。

Viewer 本地状态：

```text
%LOCALAPPDATA%\NightDesk\nx-viewer\
├─ identity.dpapi   客户端证书 PFX，经 DPAPI 保护
└─ hosts.json       地址 -> 主机证书 SHA-256
```

## 5. 安全行为

1. 目标端二维码/复制信息中的指纹是主机证书 SHA-256。
2. 控制端填写指纹时，TLS 握手阶段必须完全匹配；不匹配直接失败。
3. 未填写指纹时采用首次信任（TOFU），握手后立即保存；之后同地址指纹变化会拒绝连接。
4. 配对码错误时目标端返回 QUIC application close code `2`，控制端显示“配对码错误或已过期”。
5. 目标端未信任客户端时返回 code `1`；被新控制端接管返回 code `3`；连续错误锁定返回 code `4`。
6. 明文文本通过 `MsgText` 发送，不依赖远端键盘布局；Ctrl/Alt 快捷键使用 Windows virtual-key 发送。
7. Viewer 失焦、关闭、会话断开时发送已按下的 KeyUp 和 MouseUp，避免远端按键卡住。
8. 不持久化配对码；不记录证书私钥、配对码或剪贴板内容。

## 6. 验证结果

### 6.1 自动化

```text
203 tests passed. No display state changed.
```

其中新增真实二进制验收：

- 真实 `nx-host.exe` 使用 `-test-mode -source pattern -inject log`；
- 错误 6 位配对码被分类为 `bad-code`；
- 正确配对后收到 1920×1080 首帧；
- ACK 后收到更高 sequence 的第二帧；
- 输入操作收到远端 receipt；
- Viewer 重启后仍能读取固定主机指纹。

新增纯测试覆盖：

- 地址解析与端口边界；
- IPv6 方括号规则；
- 证书指纹规范化；
- Hello、Input、Text、Receipt 编解码；
- BGRA solid tile 解码与截断拒绝；
- Viewer 身份稳定；
- pin store 持久化和删除。

### 6.2 构建与发布

- Release 构建：**0 警告 / 0 错误**。
- 发布日期：2026-10-27。
- 发布目录：`C:\dev\NightDesk\artifacts\win-x64-1.5.0`。
- `NightDesk.exe`：`201885444` 字节，SHA-256 `50C71DADC8C1E425026CDC38C64DFEBE971E834B3E2DEE94F695F7E056EBD626`，ProductVersion `1.5.0+d720ce2eb18a793b348c3be209d9dedc095bc79d`。
- `nx-host.exe`：`7938048` 字节，SHA-256 `17B420CE531270E28BEF58D42DD68F5C13CAC6C5FE4CBFD4FDDBB20051AD35E3`，版本 `nx-host 0.2.0`。
- 两个文件必须同目录部署。

### 6.3 控制窗口冒烟

使用合成 1920×1080 动态图案启动真实 `nx-host.exe -test-mode`，由实际 `NxViewerWindow` 连接并渲染：

- 窗口显示 `已连接 · 1920×1080`；
- 画面边框、移动块和进度条可见；
- 状态区显示固定主机指纹短值；
- 自动关闭后宿主记录 viewer `EOF`，没有输入或抓屏副作用。

截图：`C:\dev\NightDesk\artifacts\win-x64-1.5.0\desktop-viewer-smoke.png`
截图 SHA-256：`330922E18899891316F9F050425E860D6E4CC750BDD62BB0B4B1F285F82278D2`

## 7. 未验收项目

以下项目没有被本次自动化或合成冒烟覆盖：

- 真实 Windows 桌面 GDI 抓屏质量；
- 真实 `SendInput` 鼠标、滚轮、组合键和 Unicode；
- 双机 tailnet 的 UDP/QUIC 连通性和 NAT/relay 行为；
- Windows UAC 安全桌面、锁屏、受保护窗口；
- DPI 缩放下的坐标一致性；
- 长时间带宽、CPU、内存、重连和输入延迟；
- 多显示器切换（当前每次连接固定一个 `nx-host` 显示器）；
- 物理 Windows 10 的 `.NET QUIC` 兼容性。

**平台限制：** WPF Viewer 使用 `System.Net.Quic`。当前官方支持平台是 Windows 11 / Windows Server 2022 及更新版本。Windows 10 上点击桌面控制会显示平台不支持错误；Android 1.1.0 不受此限制。若后续必须支持 Windows 10，应另做 Go viewer sidecar，而不是声称本实现兼容。

## 8. 构建与恢复

```powershell
$dotnet = 'C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe'
& $dotnet run --project 'C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj' -c Release
& $dotnet build 'C:\dev\NightDesk\src\NightDesk\NightDesk.csproj' -c Release
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\dev\NightDesk\tools\publish-windows.ps1'
```

发布脚本会从全新 staging 目录发布、检查两个 EXE、执行 `nx-host.exe -version`，并计算大小和 SHA-256。

## 9. 回滚

1. 保留 1.5.0 产物和 `nx-host.exe`，不要覆盖或删除。
2. 若 Viewer 有问题，可退回 `artifacts\win-x64-1.4.0` 的 `NightDesk.exe`；它仍应和 `nx-host.exe` 同目录。
3. 1.5.0 新增的 `NxViewerAddress` / `NxViewerFingerprint` 是附加 JSON 字段；旧版本没有读取它们的行为。
4. 删除 Viewer 状态会丢失本机客户端证书身份，目标端需要重新配对：
   ```text
   %LOCALAPPDATA%\NightDesk\nx-viewer
   ```
5. 不要删除目标端的 `nx-host` 状态目录；其中包含目标证书和受信任控制端指纹。

## 10. 现场安全门禁

- 2026-10-27 06:47 KST：Desktop `NightDesk.exe` PID 28504 正在运行；`nx-host.exe` PID 4444 正以 `100.109.77.29:7000`、`primary`、10 FPS 和正式状态目录监听。不要强杀、不要并发启动第二个正式 host、不要直接覆盖文件。
- `%LOCALAPPDATA%\NightDesk\recovery.json` 在 06:34 KST 存在（351 字节）；在物理输出和快照恢复独立验收前，不执行恢复、切屏或 VDD 操作。
- 当前没有活动物理输出确认时，不执行真实抓屏、SendInput、熄屏、点亮、恢复快照或 VDD 增删。
- 不在生产桌面用测试脚本绕过 UI 直接启动真实 `nx-host.exe` GDI/SendInput。
- 合成模式必须同时使用：
  ```text
  -test-mode -source pattern -inject log
  ```
  不允许省略其中任何一个。
- 真实双机验收应在可恢复显示、可中断输入的独立 Windows 测试机上进行。

## 11. 下一步人工验收

1. 两台 Windows 11 tailnet 节点分别部署 1.5.0。
2. 目标端生成配对信息，控制端粘贴地址、配对码和指纹。
3. 验证首帧、移动、点击、右键、滚轮、Ctrl 快捷键和中文输入。
4. 验证 target 端受信任设备列表只新增一个 Viewer 指纹。
5. 关闭控制窗口后确认目标端没有残留按键。
6. 重启控制端并留空配对码，确认指纹固定与客户端证书身份仍有效。
7. 手动改动目标端证书/状态目录，确认控制端拒绝并提示 “identity”。
8. 最后做 tailnet 断线、锁屏、UAC、DPI 和长时资源测试。
