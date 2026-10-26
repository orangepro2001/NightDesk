# NightDesk 远程控制阶段 2 恢复文档：Windows WPF 集成

| 项目 | 内容 |
|---|---|
| 生成时间 | 2026-10-27 01:49 KST（Asia/Seoul） |
| 仓库 | git@github.com:orangepro2001/NightDesk.git |
| 分支 | main |
| 阶段 2 代码提交 | 871b23a1f14277aa914c2bb6d38aba266465e544 |
| 提交标题 | feat(desktop): integrate nx-host low-bandwidth remote control |
| 前置提交 | ebb71b36e1bae0ade1fa7634c43902bfe65b3f16（nx-host sidecar） |
| 本阶段结论 | Windows 桌面端已完成 sidecar 生命周期、UI、鉴权/配对、设置保存和安全门禁；尚未做现场显示器上的真实采集/输入验收 |

> **现场安全边界**
>
> 本阶段没有操作现场 NightDesk PID 22708，没有退出/重启该进程，没有熄屏、点亮、恢复显示快照，没有增删 VDD、安装/卸载驱动或改变显示拓扑。没有在本机启动真实 nx-host 做抓屏或 SendInput。`%LOCALAPPDATA%\NightDesk\recovery.json` 仍存在时，继续遵守 `docs/HANDOFF.md` 的现场安全规则。

---

## 1. 本阶段完成了什么

Windows WPF 端已经把阶段 1 的 headless `nx-host.exe` 作为可选 sidecar 接入，形成：

~~~text
NightDesk.exe (WPF)
  ├─ 现有显示控制 / Tailscale HTTPS 管理面
  └─ NxHostService
       └─ nx-host.exe  ← stdin/stdout JSON Lines 管理协议
            ├─ GDI 采集指定 Windows 显示器
            ├─ QUIC + TLS 1.3（UDP，ALPN=go-nx）
            ├─ 64×64 BGRA tile 差分
            └─ SendInput 鼠标 / 键盘 / Unicode 文本
~~~

实现采用“父进程 + 独立 Go sidecar”，不是在 WPF 进程内嵌 Go，也没有把 Go DLL 暴露给 .NET。这样隔离了崩溃、运行时和 Win32 生命周期，同时保留 WPF 对显示拓扑的唯一所有权。

已实现：

1. `NxHostClient` 启动、读取 stdout 事件、向 stdin 写 JSON 命令，并设置启动 ready 超时。
2. `NxHostService` 串行化 start/stop，暴露 `Stopped/Starting/Running/Faulted` 状态。
3. Windows Job Object `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`；NightDesk 异常消失时 sidecar 一同终止。
4. 正常停止先发送 `{"cmd":"stop"}`，超时后终止进程树。
5. Windows 本地设置增加低带宽远控字段，默认关闭。
6. WPF 第 05 区加入启用/停止、显示器、UDP 监听地址、帧率、配对码、连接状态、受信任设备和解除信任。
7. 配对、刷新状态、解除信任和帧率命令均有确定性 JSON 序列化测试。
8. `nx-host.exe` 以 `Content` 方式复制到 WPF 输出根目录；定位优先级支持环境变量、程序目录和 Assets 路径。
9. 显示拓扑操作前先停止 sidecar，操作完成且原设置仍启用时再启动，避免采集目标在拓扑变化中失效。
10. 退出 NightDesk 时先停止 nx-host，再执行显示恢复和 API 清理。
11. 远控启动失败会回滚 `NxHostEnabled`，避免下次启动反复失败却没有明确状态。
12. 现有 Tailscale HTTPS 控制面保持独立，没有改变其监听、Serve 路由或设备令牌语义。

---

## 2. 文件地图

~~~text
src/NightDesk/
├── NxHostClient.cs        sidecar 进程、JSON Lines、ready/stop、Job Object
├── NxHostService.cs       串行状态机、事件归并、UI 命令封装
├── Models.cs              AppSettings 的 NxHost* 字段
├── MainWindow.xaml        第 05 区 UI
├── MainWindow.xaml.cs     启动/停止、拓扑门禁、退出清理、诊断
├── SettingsStore.cs       settings.json 持久化位置
├── NightDesk.csproj       nx-host 输出复制
└── Assets/nx-host/nx-host.exe
tests/NightDesk.Tests/
├── Program.cs             选项、协议、事件、生命周期断言
└── FakeNxHost.cs          测试用 stdin/stdout sidecar
~~~

---

## 3. sidecar 定位与启动约束

### 3.1 定位顺序

`NxHostLocator.Resolve()` 支持：

1. 环境变量 `NIGHTDESK_NX_HOST`；指定但文件不存在时直接失败，不静默回退。
2. `AppContext.BaseDirectory/nx-host.exe`。
3. `AppContext.BaseDirectory/Assets/nx-host/nx-host.exe`。

本地测试可使用：

~~~powershell
$env:NIGHTDESK_NX_HOST = 'C:\dev\NightDesk\src\NightDesk\Assets\nx-host\nx-host.exe'
~~~

### 3.2 进程参数

~~~text
nx-host.exe -listen 127.0.0.1:7000 -display primary -fps 10
            -state-dir %LOCALAPPDATA%\NightDesk\nx-host -control stdio
~~~

参数在进程创建前校验：

- `Listen` 必须是带合法端口的 `IP:port`。
- `Display` 非空、不超过 128 字符、无控制字符。
- FPS 为 1–60。
- 状态目录非空。

生产跨机连接应监听 Tailscale 地址，例如 `100.x.y.z:7000`。Tailscale Serve 是 HTTP/TCP 管理层，不代理 QUIC/UDP；Android 数据面必须直连 tailnet 的 UDP 地址。

---

## 4. 父子进程生命周期

### 4.1 启动

1. `NxHostService.StartAsync` 获取串行 gate。
2. 解析可执行文件，创建 `NxHostClient`。
3. 先创建并配置 Windows Job Object，再 `Process.Start`；两者之间仍有很小时间窗。
4. 子进程加入 Job 成功后才继续。
5. 从 stdout 按行读取事件，直到收到 `event=ready`；超时或提前退出均使启动失败。
6. `ready` 后才把服务状态设为 `Running`。

### 4.2 正常停止

1. 尝试写 `{"cmd":"stop"}`。
2. 关闭 stdin；sidecar 将 stdin EOF 也视为父进程离开。
3. 最多等待 5 秒。
4. 超时则 `Kill(entireProcessTree: true)`。
5. 释放 Job、stdout/stderr pump、CancellationTokenSource 和 Process。

### 4.3 崩溃保护

Job handle 以 `KILL_ON_JOB_CLOSE` 创建。NightDesk 进程无论正常还是异常结束，只要最后一个 Job handle 关闭，Windows 就终止 nx-host。该机制防止 WPF 崩溃后留下仍可接收屏幕和注入输入的孤儿远控进程。

### 4.4 已知边界

- “创建 Job”与“子进程加入 Job”之间仍有一个极短窗口；不是零窗口原子操作。
- 如果 NightDesk 被以更高权限强制终止，仍需人工确认没有遗留 nx-host。
- 现有安全核心是默认鉴权 + 监听 tailnet；Job Object 是生命周期防护，不是权限隔离。

---

## 5. 显示拓扑门禁

WPF 是显示拓扑的唯一所有者。所有会改变显示的路径都经过 `WithTopologyAsync`：

1. 记录启动前 `nxHost.IsRunning`。
2. 如果正在运行，先停止 nx-host。
3. 执行熄屏、点亮、恢复、增删 VDD 等操作。
4. 在 `finally` 中，只有原设置 `NxHostEnabled=true` 时才重启。
5. 重启失败会显示错误，但不会回滚已完成的显示拓扑。

覆盖路径包括：

- 主窗口熄屏/点亮按钮。
- 全局快捷键。
- 托盘菜单。
- 恢复保存布局。
- VDD 安装。
- Tailscale HTTPS API 的 `on/off/toggle/add/remove/restore`。
- 退出流程（先停 sidecar，再恢复显示）。

nx-host 自身绝不能修改显示器拓扑；它只在当前目标显示存在且尺寸稳定时采集。

---

## 6. UI 与设置

### 6.1 设置字段

`AppSettings` 新增：

| 字段 | 默认值 | 说明 |
|---|---|---|
| NxHostEnabled | false | 默认不采集、不监听 |
| NxHostListen | 127.0.0.1:7000 | UDP 监听 |
| NxHostDisplay | primary | primary / 索引 / `\\.\DISPLAYn` |
| NxHostFps | 10 | 采集帧率 |

设置仍通过现有 `settings.json` + DPAPI 令牌机制保存。本阶段没有把设备证书私钥暴露给 .NET。

### 6.2 第 05 区行为

- 未运行：可编辑监听地址、显示器和 FPS。
- 运行中：参数输入锁定；可生成配对码、刷新设备、解除设备信任。
- 配对码显示剩余秒数；sidecar 返回空 code 时关闭倒计时。
- 状态区显示分辨率、帧率、监听地址、当前控制端。
- 受信任设备显示证书指纹前缀、来源地址和配对时间。
- 错误统一进入状态栏与诊断 JSON。

### 6.3 状态同步

- `NxHostService.Changed` 通过 WPF Dispatcher 回到 UI 线程。
- `EventReceived` 用于显示 connected/disconnected/denied/error。
- 诊断面板包含 `nxHost.state/running/options/ready/connected/trusted/lastError`。
- 不把配对码或令牌写入持久化诊断文件。

---

## 7. 安全语义

1. sidecar 默认 `RequireAuth=true`；发布 UI 不提供 `-no-auth`。
2. 未知证书默认拒绝。
3. 配对码 6 位、5 分钟、一次只绑定一个新设备。
4. 错误非空 secret 计数；连续 5 次锁定 1 分钟。
5. 已配对设备直接按证书指纹放行，不依赖 IP。
6. 解除信任后下一次连接拒绝；如果该设备正连接，当前会话可能仍持续到断开，后续需评估是否立即踢下线。
7. UTF-8 文本上限 1024 字节；未知消息类型拒绝。
8. sidecar stdout/stderr 与 remote diagnostics 不记录设备私钥。
9. 现有 HTTPS API 和 QUIC 数据面是两套独立鉴权，不应混用令牌。

---

## 8. 验证记录

### 8.1 执行环境

- Windows 本机，不启动真实 nx-host，不使用真实显示器/输入。
- .NET SDK 显式路径：`C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe`。
- 测试项目是可执行断言工程，实际入口不是 `dotnet test`。

### 8.2 Release 测试

~~~powershell
$dotnet = 'C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe'
& $dotnet run --project tests/NightDesk.Tests/NightDesk.Tests.csproj -c Release --no-restore
~~~

结果：

~~~text
144 tests passed. No display state changed.
~~~

覆盖本阶段的关键断言包括：

- NxHostOptions 地址/端口/显示器/FPS 校验。
- 精确启动参数。
- ready 事件 JSON 解析与缺字段拒绝。
- pair/fps/untrust 命令的 clamp 和序列化。
- trusted IPv4/IPv6 地址 + 固定宽度时间解析。
- sidecar 环境变量定位、缺失文件拒绝。
- FakeNxHost 启动、配对事件、stop 后退出。
- NxHostService Running/Stopped 状态。
- sidecar 在 ready 前退出时启动失败。

### 8.3 Release 构建

~~~powershell
& $dotnet build src/NightDesk/NightDesk.csproj -c Release --nologo
~~~

结果：**0 warnings / 0 errors**。

### 8.4 打包二进制

~~~text
path   src/NightDesk/Assets/nx-host/nx-host.exe
size   7890944 bytes
sha256 6219CBCA36E405BE571A5A1BC86EB141B5B68C6C7BD2A722028878EC0B0C5E94
~~~

### 8.5 diff 检查

`git diff --check` 通过。

### 8.6 尚未验证

- 未在真实 Windows 桌面启动 nx-host.exe。
- 未验证真实 GDI 抓屏、显示器设备名选择、混合 DPI、旋转屏。
- 未验证真实 SendInput、Unicode 文本、扩展键和断开释放。
- 未验证发布目录中 sidecar 的定位/ACL。
- 未验证 Tailscale UDP 直连、MTU、路径切换。
- 未做 Android 到 Windows 的端到端会话。
- 未在隔离 Windows 机上验证 WPF 崩溃时 Job Object 实际杀掉子进程。

必须如实视为“代码与模拟生命周期已验证，现场数据面未验收”。

---

## 9. 提交与推送

阶段 2 提交：

~~~text
871b23a1f14277aa914c2bb6d38aba266465e544
feat(desktop): integrate nx-host low-bandwidth remote control
~~~

本机 GitHub SSH 认证失败：

~~~text
git@github.com: Permission denied (publickey)
~~~

因此通过 SSH AI 主机中转 bundle 推送：

~~~powershell
git bundle create $env:TEMP/NightDesk-stage2.bundle --all
scp $env:TEMP/NightDesk-stage2.bundle AI:/tmp/NightDesk-stage2.bundle
ssh AI 'd=/tmp/NightDesk-push-$(date +%s); git clone /tmp/NightDesk-stage2.bundle "$d" >/dev/null && cd "$d" && git remote set-url origin git@github.com:orangepro2001/NightDesk.git && git push origin main'
~~~

推送后确认远端 main 指向上述提交。

---

## 10. 恢复现场后的 Windows 验收顺序

在人工确认物理显示器恢复、`recovery.json` 已处理、PID 22708 不再需要保留之后，才进行以下步骤。不要先启动真实远控再检查显示状态。

1. 只运行 `nx-host.exe -version`，确认二进制可执行。
2. 在明确指定的显示器上以 `-control none` 启动，只观察 ready，不连接客户端。
3. 确认目标显示器名称和尺寸正确；不要使用 `primary` 猜测现场。
4. 断开真实网络，只允许本机回环；使用参考客户端做合成 tile 回环。
5. 测试一次鼠标移动到屏幕四角，确认落在目标显示器而不是主屏。
6. 测试按键按下后强制断网，确认主机释放按键。
7. 测试 Unicode 文本，确认不依赖键盘布局。
8. 验证中文输入法场景；必要时 Android 侧发送 `MsgText`，而不是逐个 VK。
9. 再绑定 tailnet UDP 地址，做手机端连接。
10. 最后测试显示拓扑切换：确认 sidecar 先停、拓扑完成后再启。

---

## 11. 已知限制与后续改动

### 11.1 远控协议

- 单 viewer 控制，后连接者接管。
- 无音频、剪贴板、文件传输、多屏同时查看。
- GDI 位图 tile 面向文字/静态 UI；不适合视频、游戏、HDR 或受保护内容。
- 无帧队列，网络慢时依赖 tile 累积和下一帧重传。
- `fps` 命令没有在 UI 暴露运行中调节入口（API 已存在）。
- 解除信任不会立即关闭现有已认证会话。

### 11.2 Windows 集成

- 默认监听 127.0.0.1，不能直接给手机使用；用户需正确填写 tailnet IP。
- UI 目前只显示配对码，不生成包含地址、指纹和代码的二维码。
- sidecar 身份指纹只在状态事件/诊断中，没有独立复制按钮。
- 尚未实现防火墙规则自动配置；首次 UDP 监听可能弹窗。
- 尚未验证非管理员运行、服务化或开机自启。
- 未改变现有 1.3.1 发布产物；现场仍运行 1.2.0。

### 11.3 下一阶段 Android 建议

Android 应复用经过测试的 Go QUIC/tile 客户端核心，经 GoMobile AAR 暴露给 Kotlin：

1. 在 `native/mobile` 实现可绑定 API，生成/加载 client identity。
2. 连接时显式传入 host 地址和主机证书指纹，首次配对必须由二维码/人工核对，不能 TOFU 静默信任。
3. 接收 tile 并交给 `SurfaceView`/Bitmap 渲染。
4. 触摸坐标从视图映射到主机帧像素，关闭会话时发送鼠标/按键释放。
5. 文本输入走 `MsgText`，特殊键走 VK。
6. 前台服务保持会话，通知栏提供断开。
7. 先在模拟器验证解码/输入编码，再在真机 tailnet 验收。
8. 每个可构建阶段单独提交和写恢复文档。

---

## 12. 快速恢复命令

~~~powershell
cd C:\dev\NightDesk
git status --short --branch
git log --oneline -5
$dotnet = 'C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe'
& $dotnet run --project tests/NightDesk.Tests/NightDesk.Tests.csproj -c Release --no-restore
& $dotnet build src/NightDesk/NightDesk.csproj -c Release --nologo
Get-FileHash src\NightDesk\Assets\nx-host\nx-host.exe -Algorithm SHA256
~~~

若手工测试 sidecar，必须设置 `NIGHTDESK_NX_HOST`，并确认不会触碰仍在恢复中的显示输出。
