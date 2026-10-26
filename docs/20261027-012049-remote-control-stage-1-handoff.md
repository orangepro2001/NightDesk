# NightDesk 远程控制阶段 1 恢复文档：nx-host sidecar

| 项目 | 内容 |
|---|---|
| 生成时间 | 2026-10-27 01:20:49 KST（Asia/Seoul） |
| 仓库 | git@github.com:orangepro2001/NightDesk.git |
| 分支 | main |
| 阶段 1 代码提交 | ebb71b36e1bae0ade1fa7634c43902bfe65b3f16 |
| 提交标题 | feat(native): add monitor-aware nx-host sidecar |
| 派生来源 | go-nx 124331bc46d8dcd281ace6886ba9413407ca5251 |
| Go 模块 | github.com/orangepro2001/nightdesk/native |
| 当前结论 | 阶段 1 完成；Windows sidecar 可构建、协议单测通过；尚未接入 WPF / Android |

> **现场安全边界**
>
> 本阶段没有操作现场 NightDesk PID 22708，没有退出/重启它，没有熄屏、点亮、恢复显示快照，没有增删 VDD、安装/卸载驱动或改变显示拓扑。所有 Go 测试、竞态检测和 Windows 交叉编译均在 SSH AI 主机的源码副本中完成。当前 recovery.json 与物理显示状态仍需人工确认；在确认前不得在本机启动 nx-host 做真实采集或输入测试。

---

## 1. 本阶段做了什么

阶段 1 把 go-nx 中可复用的远控“协议内核”改造成 NightDesk 可监管的独立 Windows sidecar，命名为 nx-host。它不包含 Ebiten 窗口、托盘、单实例逻辑，也不决定 NightDesk 的虚拟屏生命周期。

已实现：

1. 无窗口 Windows 控制台程序，由 WPF 后续通过 stdin/stdout 管理。
2. QUIC + TLS 1.3 服务端，ALPN 固定为 go-nx。
3. 默认开启设备白名单鉴权，不再沿用 go-nx 的默认放行。
4. 显示器选择：主屏、索引或 \.\DISPLAYn 设备名。
5. 单个被选显示器的 GDI BitBlt 采集，不做拓扑修改。
6. 64×64 BGRA 无损 tile 差分、solid/palette/deflate-raw 三种记录。
7. 控制流：Hello、Ack、Input、Text、Receipt。
8. 鼠标移动/按键/滚轮、Windows 虚拟键、Unicode 文本提交。
9. 多显示器下把显示器局部坐标映射到虚拟桌面绝对坐标。
10. 输入会话接管/断开时释放仍按下的键和鼠标按钮。
11. 显示器拓扑变化检测：捕获目标消失或尺寸变化时干净停止会话。
12. JSON Lines 管理协议、配对码、设备解除信任、帧率调整、状态事件。
13. stdin EOF 自动退出，避免 WPF 崩溃后遗留可被远程控制的孤儿进程。
14. 文本长度和未知消息类型的边界检查；错误配对码也计入锁定计数。

本阶段**没有**实现：

- WPF 启动/停止 sidecar、UI、Job Object、发布打包。
- Android 客户端、QUIC 依赖、tile 渲染、触摸/输入法、前台服务。
- 音频、剪贴板、文件传输、多控制端协作、权限分级。
- DDA/DXGI、HDR、受保护内容、指定虚拟屏、多显示器同时采集。
- 自动生成移动端证书或二维码配对流程。
- 在真实 Windows 桌面上做端到端采集和输入验证。

---

## 2. 文件地图

~~~text
native/
├── go.mod
├── go.sum
├── cmd/nx-host/main_windows.go
└── internal/
    ├── capture/capture_windows.go
    ├── inject/inject_windows.go
    ├── proto/proto.go
    ├── proto/proto_test.go
    ├── remote/client.go
    ├── remote/host.go
    ├── remote/input.go
    ├── remote/remote_test.go
    ├── remote/tls.go
    ├── tile/tile.go
    ├── tile/tile_test.go
    ├── trust/policy.go
    ├── trust/policy_test.go
    ├── trust/trust.go
    └── trust/trust_test.go
~~~

职责：

| 路径 | 职责 |
|---|---|
| native/cmd/nx-host | Windows 可执行入口、参数解析、JSON Lines 管理、会话事件 |
| native/internal/capture | 枚举显示器、选择显示器、GDI 抓屏、虚拟桌面坐标 |
| native/internal/inject | SendInput 鼠标/键盘/Unicode 注入 |
| native/internal/proto | 线上消息、帧头、操作去重 |
| native/internal/remote | QUIC/TLS、host serve 循环、client 参考实现、输入所有权 |
| native/internal/tile | BGRA tile 差分、编解码 |
| native/internal/trust | 本机证书身份、证书指纹白名单、配对与锁定策略 |

native/internal/remote/client.go 目前是桌面侧参考客户端，依赖本地文件系统保存 host pin，不能直接作为 Android 客户端交付。

---

## 3. nx-host 启动参数

~~~text
nx-host.exe -listen 127.0.0.1:7000 -display primary -fps 10 -state-dir "%LOCALAPPDATA%\NightDesk\nx-host" -control stdio
~~~

| 参数 | 默认值 | 说明 |
|---|---|---|
| -listen | 127.0.0.1:7000 | QUIC UDP 监听地址。生产接入 Tailscale 时应使用 tailnet 地址，不要无鉴权监听公网。 |
| -display | primary | primary、从 0 开始的显示器索引，或 \.\DISPLAY6。 |
| -fps | 10 | 采集帧率；运行时可调整到 1–60。 |
| -state-dir | %LOCALAPPDATA%\NightDesk\nx-host | identity.pem 与 trusted.json 的目录。 |
| -pair | false | 启动时立即打开 5 分钟配对窗口。 |
| -no-auth | false | 开发专用；生产禁止使用。 |
| -control | stdio | stdio 输出 JSON Lines；none 输出人类可读日志。 |
| -version | false | 输出 nx-host 0.1.0 后退出。 |

当前程序只会在 Windows 构建；所有采集和注入文件均带 Windows build tag。

---

## 4. 管理协议（WPF 与 sidecar）

### 4.1 传输

- WPF 通过 Process.StandardInput 与 Process.StandardOutput 通信。
- stdout：每行一个 JSON 对象；必须按行读取，不能读到 EOF 才解析。
- stderr：人类日志，不建议作为 UI 状态来源。
- stdin：每行一个 JSON 命令；stdin 关闭即请求 sidecar 停止。
- 所有事件和命令字段为 UTF-8 JSON。

### 4.2 启动事件

~~~json
{
  "event": "ready",
  "version": "0.1.0",
  "pid": 1234,
  "listen": "127.0.0.1:7000",
  "display": "\\.\DISPLAY6",
  "width": 1920,
  "height": 1080,
  "dpi": 96,
  "identity": "sha256-hex",
  "fps": 10,
  "requireAuth": true
}
~~~

ready 之后才表示监听器与采集器已准备就绪。UI 必须检查这个事件，不能依据进程“已启动”就宣称远控可用。

### 4.3 事件类型

| event | 关键字段 | 含义 |
|---|---|---|
| ready | version,pid,listen,display,width,height,dpi,identity,fps,requireAuth | 初始化完成 |
| pairing | code,expiresIn | 新配对码；code 为空表示取消 |
| connected | addr,identity | 新控制端已通过鉴权并成为当前输入所有者 |
| disconnected | addr,reason | 会话结束；可能是正常关闭或被新控制端接管 |
| denied | addr,reason | 拒绝连接；reason 为 untrusted、bad-code 或 locked |
| status | fps,trusted,requireAuth,listen,display,width,height,addr,identity,message | 命令响应/状态 |
| error | message | 可恢复错误或命令错误 |
| stopped | reason | 监听器退出 |

trusted 是以证书指纹为键、值为“来源地址 + 配对时间”的 JSON 对象。

### 4.4 命令

~~~json
{"cmd":"pair","seconds":300}
{"cmd":"pair-cancel"}
{"cmd":"untrust","fingerprint":"sha256-hex"}
{"cmd":"fps","value":15}
{"cmd":"status"}
{"cmd":"stop"}
~~~

注意：当前命令字段使用 value（不是 fps），seconds 省略时默认 300 秒。命令错误通过 event:error 返回，不会让进程崩溃。

---

## 5. QUIC / TLS 协议

### 5.1 传输基础

- QUIC：quic-go v0.64.1
- TLS：最低 TLS 1.3
- ALPN：go-nx
- 双方使用自签名 ECDSA P-256 证书。
- 主机使用证书 SHA-256 指纹作为设备身份；白名单保存的是 viewer 证书指纹。
- viewer 端参考实现使用“首次见到的 host 指纹”pin 模型；Android 必须实现等价的本机持久化，不能关闭证书校验。

### 5.2 流布局

~~~text
client -> host: 一条双向 control stream
  - 首消息必须是 Hello
  - 后续可发送 Ack / Input / Text

host -> client: 每条帧一条单向 stream
  - 8 字节 FrameHeader
  - 0..N 个 tile record，读到 EOF

host -> client: control stream 上的 Receipt
~~~

- 帧必须按 host 打开流的顺序接收。
- 客户端解码成功后回 Ack；host 未收到 Ack 会取消该帧流并把未确认 tile 合并到下一帧。
- 这是可靠、单帧在途、背压式传输，不建立视频帧队列。

### 5.3 客户端到主机消息

| 类型 | 值 | 布局 |
|---|---:|---|
| MsgHello | 1 | type u8, pwLen u8, password[bytes] |
| MsgAck | 2 | type u8, seq u32 |
| MsgInput | 3 | type u8, opID u64, kind u8, x i32, y i32, arg i32 |
| MsgText | 4 | type u8, opID u64, len u16, utf8[len] |

整数均为 little-endian。MaxTextLen = 1024 字节。

输入种类：

| kind | 名称 | 参数 |
|---:|---|---|
| 1 | MouseMove | x/y 为被采集显示器局部 BGRA 像素坐标 |
| 2 | MouseDown | arg: 0 左、1 右、2 中 |
| 3 | MouseUp | 同上 |
| 4 | Wheel | arg 为 1/120 滚轮刻度 |
| 5 | KeyDown | arg 为 Windows VK，0x100 位表示扩展键 |
| 6 | KeyUp | 同上 |

opID 非零时主机使用 1024 项去重缓存；重复操作只重发 receipt，不重复注入。当前参考客户端输入和文本使用 opID = 0，移动端如需可靠重试应分配非零 opID。

### 5.4 主机到客户端消息

| 类型 | 值 | 布局 |
|---|---:|---|
| MsgReceipt | 0x81 | type u8, opID u64, status u8 |

status：1 成功，2 失败。

### 5.5 帧头和 tile

FrameHeader 固定 8 字节：

~~~text
seq u32 | width u16 | height u16
~~~

tile record：

~~~text
tx u16 | ty u16 | type u8 | payloadLen u32 | payload
~~~

- tile 固定 64×64，边缘裁剪。
- 像素为 top-down BGRA，stride width*4。
- type=0：solid，payload 4 字节 BGRA。
- type=1：palette，n-1 u8，n*4 调色板，随后 run-1 u8, index u8。
- type=2：deflate 压缩的 raw BGRA。
- 客户端必须检查 tile 索引、payload 长度、帧尺寸上限，不能直接相信网络输入。

---

## 6. 鉴权与配对

默认行为：

1. 已信任指纹直接放行。
2. 配对窗口打开时，正确配对码可信任一个新设备；一次配对只接受一个设备。
3. 有永久密码时，正确密码可登录但不写入白名单。
4. 错误且非空的 secret 计入失败；连续 5 次锁定 1 分钟。
5. 已配对设备不受锁定影响。
6. 空 secret 且无有效配对/密码时返回 untrusted。
7. 配对码错误也计数，避免六位码成为无限在线猜测口。

配对码由 crypto/rand 生成，六位十进制，默认有效 300 秒。

trusted.json 至少应包含以下概念：

~~~json
{
  "certificate-fingerprint-hex": "100.x.x.x:port 2026-10-27 01:20"
}
~~~

文件写入使用 0600 语义；Windows 上实际 ACL 仍需 WPF 安装器/目录权限进一步加固。

---

## 7. 输入坐标与多显示器

go-nx 原实现围绕主屏。本阶段改为：

1. capture.Displays() 枚举显示器，记录每个显示器的虚拟桌面 X/Y/W/H。
2. viewer 发送的是被采集显示器内部坐标，从 (0,0) 开始。
3. inject.NewDisplay 加上显示器原点，再按虚拟桌面尺寸归一化为 MOUSEEVENTF_ABSOLUTE | VIRTUALDESK。
4. 注入前 clamp 到显示器边界。
5. 每个会话独占输入所有权；新会话接管前释放旧会话仍按下的键/按钮。

这解决“抓副屏却点到主屏”的基础问题，但没有解决多显示器同时预览、跨屏拖动、每屏独立 DPI 缩放的完整产品问题。

---

## 8. 构建、测试与验证记录

### 8.1 AI 主机

~~~text
host: tony-ai-pc-1 (Ubuntu)
Go:   1.27.3
path: /home/tony/dev/nightdesk-native
~~~

同步方式：

~~~bash
rsync -az --delete native/ AI:/home/tony/dev/nightdesk-native/
~~~

### 8.2 已执行命令

~~~bash
ssh AI 'cd /home/tony/dev/nightdesk-native && /usr/local/go/bin/go test -race ./...'
ssh AI 'cd /home/tony/dev/nightdesk-native && /usr/local/go/bin/go test ./...'
ssh AI 'cd /home/tony/dev/nightdesk-native && GOOS=windows GOARCH=amd64 /usr/local/go/bin/go vet ./...'
ssh AI 'cd /home/tony/dev/nightdesk-native && GOOS=windows GOARCH=amd64 /usr/local/go/bin/go build ./...'
~~~

结果：

~~~text
ok native/internal/proto
ok native/internal/remote
ok native/internal/tile
ok native/internal/trust
~~~

race 检测同样全部通过；Windows amd64 vet/build 无输出并以 0 退出。

本阶段新增并通过的测试：

- TestTextRoundTrip
- TestTextTooLong
- TestPairingCode
- TestPairingCodeWrong

原有测试继续覆盖 Hello/Ack/Input、receipt、帧头、去重、tile 编解码、会话、身份持久化、密码、锁定和设备接管。

### 8.3 尚未执行的验证

- 未在真实 Windows 主机运行 nx-host.exe。
- 未做真实 GDI 抓屏、真实 SendInput、真实 QUIC 握手。
- 未做 Tailscale ULA/地址切换和 MTU 实测。
- 未做 WPF 打包后 sidecar 路径/权限验证。
- 未做 Android 构建和真机联调。

这些缺口不能由 Linux 单测替代，必须等现场显示器安全恢复后在本机或隔离 Windows 环境完成。

---

## 9. 恢复现场后的第一步验证

在**人工确认物理显示器恢复、recovery.json 已妥善处理、NightDesk PID 22708 不再需要保留**之后，才可考虑以下验证。不要一上来就启动真实远控。

建议顺序：

1. 在隔离目录运行 nx-host.exe -version。
2. 运行 nx-host.exe -display 明确设备名 -control none，确认能看到监听和显示器信息。
3. 使用合成 tile.Frame 做 host/client 本机回环测试，不发真实输入。
4. 在明确选中的虚拟屏上做只读抓屏；先禁用输入或使用 stdio 后立即 stop。
5. 用测试 viewer 做一次鼠标移动，确认坐标落在目标屏而不是主屏。
6. 再测试键盘、Unicode、滚轮、断开释放按键。
7. 最后才接入 WPF 生命周期和 Tailscale。

任何测试前都必须先确认不会触碰需要恢复的物理输出。

---

## 10. 已知限制与风险

### 10.1 功能性

- 没有音频、剪贴板、文件传输、多屏同时查看。
- 画面是 GDI 位图 tile，不是 H.264/AV1；不适合视频、游戏、高动态画面。
- 静态 UI 是主场景；10 FPS 是默认值，不是硬性上限。
- 当前只能一个 viewer 有效控制，后连接者接管；没有协作会话。
- 键盘仅覆盖 Windows VK 和 Unicode；组合键、IME、物理布局差异仍需真机验证。
- Android 侧没有任何现成 UI 或传输代码。

### 10.2 安全

- -no-auth 必须在发布构建和 UI 中不可达。
- sidecar 监听 127.0.0.1 只适合本机测试；跨机必须走 Tailscale/tailnet，且仍保留证书鉴权。
- Tailscale Serve 不代理 UDP/QUIC；不要把它当 QUIC 数据面入口。
- 当前没有设备显示名称、二维码、配对审计、会话录制或权限分级。
- WPF 必须使用 Windows Job Object 绑定 sidecar，防止 NightDesk 异常退出后留下远控进程。
- 显示恢复流程必须先停止 sidecar，再恢复拓扑；sidecar 本身绝不能改拓扑。

### 10.3 兼容性

- 仅 Windows amd64 已验证。
- ARM64 Windows 未验证。
- 多 DPI、混合缩放、旋转显示器未真机验证。
- GDI 无法捕获受保护内容、部分 HDR/独占全屏内容。
- 显示器热插拔仅通过周期性 Valid() 检测，不是系统事件驱动。

---

## 11. 提交与推送记录

阶段 1 代码提交：

~~~text
ebb71b36e1bae0ade1fa7634c43902bfe65b3f16
feat(native): add monitor-aware nx-host sidecar
~~~

推送方式：本地 origin 为 HTTPS，当前环境无法稳定直推，因此使用 Git bundle 传到 AI 主机，再由 AI 主机通过 SSH remote 推送到 GitHub。

~~~bash
git bundle create /tmp/NightDesk.bundle --all
scp /tmp/NightDesk.bundle AI:/tmp/NightDesk.bundle
ssh AI 'rm -rf /tmp/NightDesk-push && git clone /tmp/NightDesk.bundle /tmp/NightDesk-push && cd /tmp/NightDesk-push && git remote set-url origin git@github.com:orangepro2001/NightDesk.git && git push origin main'
~~~

推送后核对：

~~~text
local  ebb71b36e1bae0ade1fa7634c43902bfe65b3f16
remote ebb71b36e1bae0ade1fa7634c43902bfe65b3f16
~~~

---

## 12. 下一阶段入口：Windows WPF

下一阶段建议只做 Windows 集成，不同时开 Android：

1. 新增 NxHostClient：启动 nx-host.exe、按行解析 stdout、发送 JSON 命令、处理退出/超时。
2. 新增 NxHostService：设置参数、选择显示器、状态机、事件转发、幂等 start/stop。
3. 用 Windows Job Object JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE 绑定子进程。
4. 将 native/cmd/nx-host 构建产物复制到 WPF 输出并验证发布目录。
5. 在 MainWindow.xaml 独立区域加入：启用开关、显示器选择、监听地址、FPS、配对码、设备列表、连接状态、错误信息。
6. 与现有显示恢复门禁联动：显示操作前停止 sidecar；恢复完成且用户确认后再允许启动。
7. 不修改现有 RemoteServer、PairingWindow、DeviceRegistry 的语义；Nx 远控使用独立命名和设置。
8. 构建 WPF、运行无 UI 的 sidecar 参数测试；现场真实采集测试留到安全确认后。

阶段 1 完成标准已达到：协议内核可独立构建、默认拒绝未授权 viewer、能选择指定显示器、输入坐标可映射、生命周期可由父进程控制、Windows 交叉编译通过。
