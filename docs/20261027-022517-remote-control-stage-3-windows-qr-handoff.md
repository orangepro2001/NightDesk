# NightDesk 远程控制阶段 3 恢复文档：Windows 可连接配对二维码

| 项目 | 内容 |
|---|---|
| 生成时间 | 2026-10-27 02:24 KST（Asia/Seoul） |
| 仓库 | git@github.com:orangepro2001/NightDesk.git |
| 分支 | main |
| 本阶段代码提交 | `ed38b06e5a269f1b019c4f622d0fbba811d3310a` |
| 提交标题 | feat(desktop): generate pinned go-nx pairing QR |
| 前置提交 | `c7846933efb91a1ffee4ca71afcfcc2d00d9c6b3` |
| 本阶段结论 | Windows 端已能把 go-nx sidecar 的地址、证书指纹、6 位配对码和屏幕尺寸合成为手机可扫描的 `nightdesk-nx` 二维码；尚未在真实显示器上完成采集/输入验收 |

> **现场安全边界**
>
> 本阶段没有操作现场 NightDesk PID 22708，没有退出或重启该进程，没有熄屏/点亮、恢复快照、增删 VDD、安装或卸载驱动，也没有改变显示拓扑。没有在本机启动真实 `nx-host.exe` 抓屏或 SendInput。`%LOCALAPPDATA%\NightDesk\recovery.json` 仍存在时，继续遵守 `docs/HANDOFF.md`。

---

## 1. 本阶段解决的核心问题

阶段 2 的 Windows UI 只显示 6 位配对码。手机即使知道代码，也不知道：

- 应连接哪个 UDP `IP:port`；
- 应 pin 哪个主机证书 SHA-256 指纹；
- 二维码对应哪台主机与分辨率；
- 配对码何时过期。

本阶段把上述信息打包成可由 Android 扫描的 JSON 二维码，同时保留人工核对代码的路径。

~~~json
{
  "v": 1,
  "kind": "nightdesk-nx",
  "addr": "100.x.y.z:7000",
  "fingerprint": "<64 hex chars>",
  "code": "123456",
  "expires": 1790000000,
  "name": "PC-NAME",
  "width": 1920,
  "height": 1080
}
~~~

该载荷：

- 不含长期设备令牌；
- 不含客户端私钥或主机私钥；
- 6 位代码仍由 sidecar 的短期配对窗口控制；
- 手机必须先用 `fingerprint` 校验证书，再通过 TLS 1.3 连接发送 `code`；
- 代码成功使用、过期、取消配对或 sidecar 停止后失效。

---

## 2. 已完成内容

### 2.1 公共二维码渲染

新增 `src/NightDesk/QrImages.cs`：

~~~csharp
public static BitmapImage Create(string payload)
~~~

- 使用 QRCoder 的 Q 级纠错；
- PNG 先加载到内存并 `Freeze()`，避免 WPF 跨线程/流生命周期问题；
- 现有 HTTPS 设备绑定二维码和 NX 二维码共用该实现；
- 删除了 `PairingWindow` 内重复的私有 QR 生成代码。

### 2.2 NX 配对载荷

新增 `src/NightDesk/NxPairing.cs`，包含：

- `NxPairingPayload.Validate()`：IP:port、64 位十六进制指纹、6 位数字代码、未来过期时间、名称控制字符和尺寸范围校验；
- `ToJson()`：固定 `kind=nightdesk-nx`、指纹转小写；
- `ResolveAddress(listen, tailscaleIPv4)`：处理显式地址、`0.0.0.0`/`::` 和环回地址；
- `TryParseTailscaleIPv4()`：只接受 `100.64.0.0/10`；
- `WithHost()`：替换监听主机，保留端口。

**安全选择：** 环回监听不会偷偷改写为 Tailscale 地址。因为 sidecar 实际仍绑定 `127.0.0.1`，伪造一个不可连接的 QR 只会制造误判。UI 明确要求用户先停止远控、填入 Tailscale 地址、重新启用。

### 2.3 Tailscale IPv4 读取

`TailscaleService` 新增：

- `GetIPv4Async()`：读取 `tailscale status --json`；
- `ExtractIPv4(JsonElement)`：从 `Self.TailscaleIPs` 选第一个合法的 CGNAT IPv4；
- 拒绝 Tailscale IPv6 或非 `100.64/10` 地址。

原因：Tailscale Serve 只代理 HTTPS/TCP 管理面；go-nx 数据面是 QUIC/UDP。手机需直连 tailnet 的 UDP 地址，不能从 `https://*.ts.net/` 推导数据面。

### 2.4 WPF 第 05 区

新增按钮：

- **填入 Tailscale 地址**：只在 sidecar 停止时可用；保留当前端口，把主机替换为 Tailscale IPv4；
- **生成配对二维码**：只允许非环回监听；否则提示先停止、填地址、重启用。

配对事件到达后自动打开 `NxPairingWindow`，显示：

1. 二维码；
2. 主机名称；
3. 分辨率；
4. 完整连接地址；
5. 大号 6 位配对码；
6. 过期本地时间；
7. 完整 SHA-256 指纹；
8. “先 pin 指纹，再发送配对码”的安全说明。

### 2.5 测试

`tests/NightDesk.Tests/Program.cs` 新增断言：

- `nightdesk-nx` kind、地址和指纹小写；
- 显式地址保持；
- `0.0.0.0` 使用 Tailscale IPv4；
- `WithHost` 保留端口；
- 环回监听被拒绝；
- `100.64`/`100.127` 边界接受，`100.63`/`100.128` 拒绝；
- Tailscale IPv6 不作为数据面地址；
- 从 status JSON 提取 IPv4；
- 过期配对载荷拒绝。

---

## 3. 文件地图

~~~text
src/NightDesk/QrImages.cs           公共冻结 PNG -> BitmapImage
src/NightDesk/NxPairing.cs          JSON 载荷、地址解析、Tailscale IPv4 规则
src/NightDesk/NxPairingWindow.cs    完整二维码、代码、指纹和过期时间 UI
src/NightDesk/PairingWindow.cs      改用 QrImages
src/NightDesk/TailscaleService.cs   GetIPv4Async / ExtractIPv4
src/NightDesk/MainWindow.xaml       第 05 区新增 Tailscale 按钮
src/NightDesk/MainWindow.xaml.cs    配对事件 -> 二维码窗口、环回门禁
tests/NightDesk.Tests/Program.cs    NX QR/地址/Tailscale 测试
~~~

---

## 4. 验证记录

### 4.1 本机环境

~~~text
dotnet 10.0.402
path   C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe
OS     Windows，现场显示仍在恢复保护范围
~~~

### 4.2 Release 构建

~~~powershell
$dotnet = 'C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe'
& $dotnet build src/NightDesk/NightDesk.csproj -c Release -v:minimal
~~~

结果：**0 warnings / 0 errors**。

### 4.3 测试

~~~powershell
& $dotnet run --project tests/NightDesk.Tests/NightDesk.Tests.csproj -c Release --no-restore
~~~

结果：

~~~text
158 tests passed. No display state changed.
~~~

### 4.4 本阶段未验证

- 未启动真实 `nx-host.exe`，未做真实 GDI 抓屏和 SendInput。
- 未在手机上扫描本阶段生成的二维码。
- 未验证 Windows 防火墙对 tailnet UDP 的首包提示。
- 未验证 Tailscale IP/端口在 Android 上的直连与路径切换。
- 未验证主机证书轮换后旧 QR 被正确拒绝的完整端到端体验。
- 未验证非管理员运行、混合 DPI、旋转屏、受保护内容和多显示器。

---

## 5. 提交与推送

~~~text
commit ed38b06e5a269f1b019c4f622d0fbba811d3310a
feat(desktop): generate pinned go-nx pairing QR
push   c784693..ed38b06 main -> main
remote main = ed38b06e5a269f1b019c4f622d0fbba811d3310a
~~~

本机 HTTPS 远端不稳定时，使用 bundle 经 AI SSH 主机推送：

~~~powershell
$bundle = Join-Path $env:TEMP 'NightDesk-stage3.bundle'
git bundle create $bundle --all
scp $bundle AI:/tmp/NightDesk-stage3.bundle
ssh AI 'set -e; d=/tmp/NightDesk-push-stage3-$(date +%s); git clone -q /tmp/NightDesk-stage3.bundle "$d"; cd "$d"; git remote set-url origin git@github.com:orangepro2001/NightDesk.git; git push origin main'
~~~

---

## 6. 下一阶段：Android NX Viewer

Android 现有 1.0.4 只包含 HTTPS 管理面和 `nightdesk-pair` 设备绑定，不包含 QUIC 屏幕控制。下一阶段必须：

1. 在 AI 主机用 gomobile 生成 `mobile.aar`；
2. 将 AAR 放入 Android 工程并保持现有 HTTPS 流程不退化；
3. 独立解析 `kind=nightdesk-nx`；
4. 独立保存 addr/fingerprint/name，不把短期代码写入长期存储；
5. 实现 `SurfaceView` 或等价 tile 渲染；
6. 把触摸坐标从视图映射到主机像素；
7. 映射左键、右键、滚轮和软键盘 Unicode 文本；
8. 失焦、切后台、断网和退出时 `ReleaseAll`；
9. 前台服务/通知保持会话；
10. 先在 Android 模拟器验证，再考虑真机 tailnet；
11. 版本升至 1.1.0 / versionCode 6；
12. 每个可独立构建的阶段提交并写恢复文档。

Android 核心 API 已在 `c784693` 完成：

~~~go
NewSession(storageDir, hostAddr, fingerprint, pairingCode, callback)
OnState(state)
OnFrameBegin(width, height)
OnTile(x, y, w, h, pixels)
OnReceipt(opID, status)
OnError(code, message)
Session.Move/MouseDown/MouseUp/Wheel/KeyDown/KeyUp/Text/ReleaseAll/Close
~~~

`OnTile` 的 `pixels` 只在回调期间有效，Kotlin 必须同步复制。像素格式为紧密 BGRA，Android `Bitmap.copyPixelsFromBuffer` 前需要考虑字节序与 ARGB 转换。

---

## 7. 快速恢复命令

~~~powershell
cd C:\dev\NightDesk
git status --short --branch
git log --oneline -6
$dotnet = 'C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe'
& $dotnet build src/NightDesk/NightDesk.csproj -c Release -v:minimal
& $dotnet run --project tests/NightDesk.Tests/NightDesk.Tests.csproj -c Release --no-restore
git diff --check
~~~

恢复真实 Windows 现场前，必须先复核：

~~~powershell
Get-Process NightDesk -ErrorAction SilentlyContinue | Select-Object Id,Path,StartTime
Test-Path $env:LOCALAPPDATA\NightDesk\recovery.json
~~~

在物理显示器恢复并确认 `recovery.json` 已处理前，禁止启动真实 sidecar、操作显示拓扑或替换 PID 22708。
