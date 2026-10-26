# NightDesk 远程控制阶段 7：安全端到端探针与验收交接

- 记录时间：2026-10-27 04:18（Asia/Seoul）
- 仓库：`git@github.com:orangepro2001/NightDesk.git`
- 分支：`main`
- 代码提交：`fff73bb2b823f52f86f8807a9f0a59c6e3283ebd`
- 上一阶段：`docs/20261027-035456-remote-control-stage-6-packaging-handoff.md`

## 0. 一句话结论

go-nx 远控集成到 NightDesk 的 Windows 桌面端和 Android 端是**可行且已落地**的，不是理论方案。本轮新增的 `nx-probe` 使用与 Android 客户端相同的 pinned TLS、配对码、QUIC 和 tile 解码路径，在完全不接触桌面的合成源上验证了首帧、连续动态帧、tile 差分和 7/7 输入回执。

仍未完成、也不能声称完成的是：**独立 Windows 测试机上的真实 GDI 抓屏与 SendInput 验收**，以及 **Android 真机通过 tailnet 的端到端验收**。当前生产桌面存在显示恢复门禁，不能用于这两项测试。

## 1. 可行性判断（如实报告）

### 1.1 与 Sunshine / Parsec 的本质区别

| 维度 | Sunshine / Parsec 类方案 | NightDesk + go-nx |
|---|---|---|
| 画面编码 | H.264 / HEVC / AV1 视频编码 | BGRA 无损 tile 差分（64×64） |
| 传输语义 | 视频帧，可丢帧、可降码率 | 单个 tile 更新，逐帧确认，慢链路自动降帧率而不是堆积 |
| 带宽模型 | 面向视频/游戏，动态码率 | 面向静态或低动态桌面，带宽随变化区域变化 |
| 音频 | 通常支持 | 不支持 |
| 剪贴板 / 文件传输 | 通常支持 | 不支持 |
| 多显示器 | 通常支持 | 当前只采集一个显示器 |
| 动态画面能力 | 强 | 弱；大范围持续变化时带宽和 CPU 会明显上升 |
| 安全模型 | 多种 | TLS 1.3 + 证书固定 + 6 位短期配对码 + 受信设备库 |

因此它不是“Sunshine 替代品”，而是**低带宽、可审计、适合远程办公/查看/轻量操作的无损桌面通道**。把它包装成视频串流会误导用户。

### 1.2 适合的场景

- 远程查看静态桌面、终端、文档、代码编辑器；
- 低动态的鼠标键盘操作；
- tailnet 内网、带宽有限或需要严格证书固定的环境；
- 需要“无音频、无文件传输”的最小攻击面。

### 1.3 不适合的场景

- 游戏、视频播放、3D 渲染等持续大范围变化；
- 需要音频、剪贴板、文件拖拽、多显示器切换；
- 高延迟公网环境下的流畅交互（QUIC 能工作，但体验受 tile 模型限制）。

### 1.4 集成结论

- Windows 端：`nx-host.exe` 作为独立 Go sidecar，由 WPF 通过 stdin/stdout JSON Lines 管理，已接入发布包与 Job Object 生命周期。
- Android 端：原生 Kotlin 查看器，已接入扫码配对、固定证书、触摸/滚轮/键盘/Unicode 输入、连接状态机和后台释放输入。
- 两端共用同一套协议语义；本轮又用 `nx-probe` 证明了协议核心可以在不依赖 WPF / Android UI 的情况下独立验收。

## 2. 本阶段新增能力

### 2.1 `nx-host` 0.2.0 安全测试模式

新增参数：

```text
-test-mode            隔离的验收模式
-source pattern      合成 1920×1080 动态画面（不抓屏）
-inject log          记录输入但不调用 SendInput
```

安全护栏（缺一不可）：

1. `-test-mode` 必须同时指定 `-source pattern -inject log`，否则进程以退出码 2 拒绝启动。
2. `-source pattern` 或 `-inject log` 只要有一个不是测试组合，也必须带 `-test-mode`。
3. 测试模式默认使用临时 identity / trust 目录，退出时删除；不会读写生产身份和受信设备库。
4. 测试模式绝不打开 GDI 采集，也绝不调用 SendInput；`ready` / `status` 事件会明确携带 `testMode`、`source`、`inputMode`。
5. `tools/test-nx-probe.ps1` 在启动前先执行“`-test-mode` 单独使用必须被拒绝”的负向检查。

### 2.2 合成画面源 `native/internal/synth`

- 1920×1080 BGRA 动态图案；
- 静态渐变背景 + 移动色块 + 循环进度条；
- 首帧包含全部 tile，后续帧只改变少量 tile，能同时验证全量首帧和增量差分；
- 单元测试覆盖最小尺寸、尺寸不匹配、连续帧变化和静态背景不变。

### 2.3 输入记录器 `native/internal/logsink`

- 实现 `remote.Injector` 和 `remote.TextInjector`；
- 输入仍然走完整的协议解码、校验、去重和回执链路；
- 只记录 `op/kind/x/y/arg` 或文本，不触碰操作系统输入；
- 单元测试覆盖鼠标、滚轮、键盘、Unicode 文本和非法输入类型。

### 2.4 `nx-probe` 验收客户端

- 使用与 Android 相同的 `remote.DialPinned`：证书固定、配对码、QUIC 握手；
- 接收并解码 tile，统计帧数、tile 更新数、首帧耗时和整帧 SHA-256；
- `-input` 发送鼠标移动/左键按下/左键抬起/滚轮/按键按下/按键抬起/Unicode 文本共 7 个操作，并校验 7 个 `executed` 回执；
- 校验连续帧摘要必须不同，防止“静态画面冒充动态源”；
- `-json` 输出机器可读结果，且不再混入 `OK` 文本行；
- 单元测试使用真实 loopback QUIC host，覆盖成功路径、错误指纹和错误配对码。

### 2.5 `tools/test-nx-probe.ps1`

一键完成：

1. 负向安全检查；
2. 启动 `nx-host.exe -test-mode -source pattern -inject log`；
3. 读取 `ready` / `pairing` 事件，校验测试模式字段；
4. 运行 `nx-probe.exe`（4 帧 + 7 个输入）；
5. 断言 1920×1080、4 帧、动态摘要、7/7 回执；
6. 发送 `{"cmd":"stop"}` 并确认 host 退出码为 0。

### 2.6 Android 仪器测试脚本修复

`android/scripts/run-instrumented.sh` 之前只运行 `HostStoreInstrumentedTest`；`UpdateFlowInstrumentedTest` 需要显式传入 `-e updateOrigin <tailnet origin>`，单独运行会报 `missing updateOrigin`。现在脚本会：

- 安装 release APK 与 androidTest APK；
- 运行 `HostStoreInstrumentedTest`（3 项）；
- 以 `NIGHTDESK_UPDATE_ORIGIN`（默认 `https://tony-ai-pc-1.tail9f7f1f.ts.net`）运行 `UpdateFlowInstrumentedTest`（1 项）；
- 任一测试失败即返回非零。

## 3. 验证记录

### 3.1 Go（Linux 与 Windows）

在 SSH AI 主机 `/home/tony/dev/nightdesk-s7/native` 执行：

```bash
gofmt -l ./cmd ./internal
go vet ./...
go test ./...
GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go vet ./...
GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build ./...
```

结果：全部通过。`gofmt -l` 只列出仓库中原本就未格式化的两个历史测试文件（`internal/proto/proto_test.go`、`internal/trust/policy_test.go`），本阶段新增/修改文件均已格式化。

交叉编译 8 个 Windows 测试二进制并在 Windows 本机执行：

| 测试二进制 | 结果 |
|---|---|
| `internal_synth.test.exe` | PASS |
| `internal_logsink.test.exe` | PASS |
| `internal_probe.test.exe` | PASS（真实 loopback QUIC） |
| `internal_proto.test.exe` | PASS |
| `internal_tile.test.exe` | PASS |
| `internal_trust.test.exe` | PASS |
| `internal_remote.test.exe` | PASS |
| `mobile.test.exe` | PASS |

> 说明：`GOOS=windows go test` 直接在 Linux 上执行会得到 `exec format error`，这是预期行为；本轮改为 `go test -c` 交叉编译后再到 Windows 执行。

### 3.2 Windows 桌面端

```powershell
C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe run --project C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj -c Release --no-restore
C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe build C:\dev\NightDesk\src\NightDesk\NightDesk.csproj -c Release --no-restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\dev\NightDesk\tools\publish-windows.ps1
```

结果：

- **176 项 Release 测试通过**，输出 `176 tests passed. No display state changed.`；
- Release 构建 **0 警告 / 0 错误**；
- 发布脚本在全新 staging 目录中确认 `NightDesk.exe` 与 `nx-host.exe` 同时存在，并执行 `nx-host.exe -version`；
- `nx-host 0.2.0` 版本检查通过。

### 3.3 安全端到端探针（本轮关键证据）

对**发布目录中的真实 `nx-host.exe`** 运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\dev\NightDesk\tools\test-nx-probe.ps1 `
  -HostExe C:\dev\NightDesk\artifacts\win-x64-1.4.0\nx-host.exe
```

结果：

| 指标 | 值 |
|---|---|
| 连接 | `connected=true` |
| 画面 | `1920×1080` |
| 首帧 | `179 ms` |
| 帧数 | `4` |
| tile 更新 | `538` |
| 输入发送 | `7` |
| 输入回执 | `7` |
| 回执失败 | `0` |
| 首帧/末帧摘要 | 不同（证明画面在动） |
| 总耗时 | `441 ms` |
| host 退出码 | `0` |

安全性质：整个过程使用合成源和日志输入，没有抓取桌面，没有调用 SendInput，没有读写生产 trust store，也没有修改显示拓扑。

### 3.4 Android

在 AI 主机 `emulator-5580` 上：

- `testDebugUnitTest --rerun-tasks`：**26 项通过，0 失败**；
- `HostStoreInstrumentedTest`：**3 项通过**；
- `UpdateFlowInstrumentedTest`：**1 项通过**（带 `updateOrigin` 参数）；
- 共 **4 项仪器测试通过**；
- release / debug APK 哈希与阶段 5 完全一致，说明本轮 Android 代码未发生回归性变化。

### 3.5 产物与哈希

Windows 发布目录：`C:\dev\NightDesk\artifacts\win-x64-1.4.0`

| 文件 | 大小 | SHA-256 | 版本 |
|---|---:|---|---|
| `NightDesk.exe` | `201832196` | `F4C448970703BB489E3A762596A59AEA06E688ED39CC505128B01CF4B7864EF2` | `1.4.0+fff73bb2b823f52f86f8807a9f0a59c6e3283ebd` |
| `nx-host.exe` | `7938048` | `17B420CE531270E28BEF58D42DD68F5C13CAC6C5FE4CBFD4FDDBB20051AD35E3` | `nx-host 0.2.0` |

验收工具：`C:\dev\NightDesk\tools\nx-probe.exe`

| 文件 | 大小 | SHA-256 |
|---|---:|---|
| `nx-probe.exe` | `7563264` | `F58091A7F5154D3742570E9360EA6C9E9ED6F963D212D563D666886134F45A76` |

Android 阶段 5 候选（未发布，本轮复核哈希不变）：

| 文件 | 大小 | SHA-256 |
|---|---:|---|
| `nightdesk-1.1.0-stage5.apk` | `55058900` | `01734a26365b859c831a9ba7806ff1d133b96e0993061053bb2a60ab72caf480` |
| `nightdesk-1.1.0-stage5-debug.apk` | `56886382` | `042c9d37dffa86c7507ee8ab358415d46eb921cd55700328e8ef393034fc08aa` |
| `nightdesk-1.1.0-stage5-androidTest.apk` | `689645` | `f9d11858d818aaa073f39fc404ea716af5043c2f6093bc0454b8ef52da1b4c36` |

## 4. 未完成事项与阻塞

### 4.1 真实 GDI 抓屏 / SendInput（阻塞）

本轮只验证到“真实 host 二进制 + 合成源 + 日志输入”。以下仍未验证：

- `capture.Open` 对真实物理显示器的抓取；
- 显示器热插拔、分辨率变化后的 `checkedSource` 失效处理；
- `inject.NewDisplay` 的坐标映射、DPI 缩放；
- 真实 SendInput 的鼠标、滚轮、虚拟键和 Unicode 文本；
- UAC / 安全桌面下的行为；
- 长时间运行的帧率、内存和句柄稳定性。

原因：生产桌面 PID **13816**（1.3.1）仍处于显示恢复门禁中，`recovery.json` 存在，最近只读诊断显示唯一活动源是 `\\.\DISPLAY6`（Parsec VDD）。在生产桌面运行真实抓屏或 SendInput 可能造成不可接受的显示/输入风险。

**需要在独立 Windows 测试机（有可恢复的物理显示器）上完成，不能拿生产机替代。**

### 4.2 Android 真机 tailnet（阻塞）

- 模拟器只验证了 UI、状态机、存储和更新流程；
- 尚未验证真机通过 tailnet 直连 Windows sidecar 的 UDP/QUIC 路径；
- 尚未验证移动网络切换、Wi-Fi/蜂窝切换、后台恢复和长时间会话。

### 4.3 Android 1.1.0 尚未发布

- 线上更新源仍是 **1.0.4 / versionCode 5**；
- 1.1.0 阶段 5 候选已构建、单测和仪器测试通过，但没有发布到更新源；
- 在没有真机 tailnet 验收前，不建议把 1.1.0 推给普通用户。

### 4.4 Windows 1.4.0 尚未部署

- 生产仍是 1.3.1，且生产目录没有 `nx-host.exe`；
- 1.4.0 已可发布，但必须先完成显示恢复验证和独立测试机验收；
- 部署时必须把 `NightDesk.exe` 与 `nx-host.exe` 放在同一目录。

## 5. 恢复步骤（从干净 clone 开始）

### 5.1 本地仓库

```powershell
git clone git@github.com:orangepro2001/NightDesk.git C:\dev\NightDesk
cd C:\dev\NightDesk
git log --oneline -5
```

本阶段基线应为 `fff73bb`（或其后继提交）。

### 5.2 同步 Go 源码到 AI 主机

Windows 本机没有安装 Go；Go 的格式化、测试和 Windows 交叉编译都在 AI 主机完成：

```powershell
scp -r C:\dev\NightDesk\native AI:/home/tony/dev/nightdesk-s7/
```

### 5.3 AI 主机验证

```bash
cd /home/tony/dev/nightdesk-s7/native
gofmt -l ./cmd ./internal
go vet ./...
go test ./...
GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go vet ./...
GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build ./...
```

重新生成 Windows 二进制：

```bash
mkdir -p ../out
GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -trimpath -ldflags='-s -w' -o ../out/nx-host.exe ./cmd/nx-host
GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -trimpath -ldflags='-s -w' -o ../out/nx-probe.exe ./cmd/nx-probe
sha256sum ../out/nx-host.exe ../out/nx-probe.exe
```

拷回仓库：

```powershell
scp AI:/home/tony/dev/nightdesk-s7/out/nx-host.exe C:\dev\NightDesk\src\NightDesk\Assets\nx-host\nx-host.exe
scp AI:/home/tony/dev/nightdesk-s7/out/nx-probe.exe C:\dev\NightDesk\tools\nx-probe.exe
```

### 5.4 安全探针

```powershell
cd C:\dev\NightDesk
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\test-nx-probe.ps1
```

期望最后输出 `PASS: 4 frames, ... 7/7 input receipts, dynamic content.` 且 host 退出码为 0。

### 5.5 Windows 构建与发布

```powershell
C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe run --project C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj -c Release --no-restore
C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe build C:\dev\NightDesk\src\NightDesk\NightDesk.csproj -c Release --no-restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\dev\NightDesk\tools\publish-windows.ps1
```

### 5.6 Android 构建与测试（AI 主机）

```bash
NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-stage5 \
  bash scripts/build-ai.sh -q -PnightdeskVersionCode=7 -PnightdeskVersionName=1.1.0

cd /home/tony/dev/nightdesk-android-stage5
export JAVA_HOME=/home/tony/.local/opt/jdk-17
export ANDROID_HOME=/home/tony/Android/Sdk
export ANDROID_SDK_ROOT=$ANDROID_HOME
/home/tony/.local/opt/gradle-8.11.1/bin/gradle --no-daemon -q testDebugUnitTest --rerun-tasks
NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-stage5 \
  NIGHTDESK_ANDROID_SERIAL=emulator-5580 \
  bash scripts/run-instrumented.sh
```

## 6. 真实环境验收步骤（条件具备后）

1. 准备独立 Windows 测试机，带可恢复的物理显示器；不要使用生产桌面。
2. 在同一目录部署 `NightDesk.exe` 与 `nx-host.exe`。
3. 启动 NightDesk，启用 NX 远控，生成配对二维码。
4. 先用 `nx-probe` 对真实 host 运行（不带 `-test-mode`），确认真实 GDI 首帧和连续帧。
5. 再用 `nx-probe -input` 验证真实 SendInput；在测试机上准备可观察的输入目标。
6. Android 真机通过 tailnet 扫码连接，验证触摸、滚轮、键盘、中文输入。
7. 验证错误配对码、过期二维码、证书变化、断网重连、后台恢复和强制停止。
8. 验证防火墙只允许预期的 UDP 端口，Tailscale Serve 不代理 QUIC/UDP。
9. 记录真实首帧耗时、帧率、带宽和 CPU/内存占用。

## 7. 本阶段变更文件

新增：

- `native/internal/synth/source.go`
- `native/internal/synth/source_test.go`
- `native/internal/logsink/sink.go`
- `native/internal/logsink/sink_test.go`
- `native/internal/probe/probe.go`
- `native/internal/probe/probe_test.go`
- `native/cmd/nx-probe/main.go`
- `tools/test-nx-probe.ps1`
- `tools/nx-probe.exe`（AI 交叉编译产物）

修改：

- `native/cmd/nx-host/main_windows.go`（0.2.0、test mode、source/inject 接口、host 字段）
- `src/NightDesk/Assets/nx-host/nx-host.exe`（0.2.0）
- `android/scripts/run-instrumented.sh`（同时运行两组仪器测试并传入 updateOrigin）

## 8. 回滚

- 单独回滚本阶段：`git revert fff73bb`。
- 回滚时必须同时恢复 `src/NightDesk/Assets/nx-host/nx-host.exe` 到 0.1.0，避免源码与随包二进制不一致。
- 回滚 Android 脚本不影响已发布 APK。

## 9. 安全红线（必须保留）

- 生产进程 PID **13816**、路径 `C:\Users\pixel\Desktop\NightDesk.exe`、版本 **1.3.1**，在恢复快照独立验证前不得退出、重启或替换。
- 不得在生产桌面执行熄屏、点亮、恢复快照、VDD 增删、显示驱动安装/卸载。
- 不得在生产桌面运行真实 `nx-host.exe` 抓屏或 SendInput。
- 不得把 1.4.0 直接复制到生产目录。
- 测试模式只能使用 `-source pattern -inject log`；任何试图在测试模式打开 GDI 或 SendInput 的组合都必须被拒绝。
