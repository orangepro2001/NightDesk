# NightDesk 远程控制阶段 5 恢复文档：连接生命周期与可恢复失败

| 项目 | 内容 |
|---|---|
| 生成时间 | 2026-10-27 03:33 KST（Asia/Seoul） |
| 仓库 | `git@github.com:orangepro2001/NightDesk.git` |
| 分支 | `main` |
| 本阶段代码提交 | `a0746dc03b2c45859bc5bd77bd6154fbed7f32af` |
| 提交标题 | `fix(android): make remote connection recovery explicit` |
| 前置提交 | `4853a34`（清理已退出 sidecar 的陈旧状态） |
| 结论 | Android 远控连接已具备显式状态机、超时、重试、重新配对、后台释放和旧回调隔离；构建、26 项 JVM 单测、4 项仪器测试与模拟器 UI 冒烟通过。**真实 Windows 抓屏、SendInput 和真机 tailnet 端到端仍未验收。** |

> **边界声明**
>
> 本阶段完成的是 Android 侧“失败可见、恢复可控、后台不遗留输入状态”的工程化收口，不是视频串流能力，也不是现场 Windows 验收。现场显示拓扑和运行中的 NightDesk 进程仍受保护，禁止用生产桌面启动真实 `nx-host.exe` 做抓屏或输入测试。

---

## 1. 本阶段要解决的问题

阶段 4 已能把 Android 连到 Windows sidecar、绘制 tile 并发送输入，但连接失败时存在几个产品级问题：

- Go 回调、`NewSession` 返回值和 UI 状态可能互相覆盖，旧连接的错误会污染新连接。
- 握手没有前端截止时间，网络不可达时 UI 可能长期停在“正在连接”。
- 失败后只能退出重进，没有区分“可重试”“重新输入配对码”“必须重新扫码”。
- 切后台或 Activity 销毁时，必须确保鼠标按键、键盘修饰键和 QUIC 会话都被释放。
- 连接成功后保存的屏幕尺寸可能来自过期帧，或者重复写入。

阶段 5 用纯 Kotlin reducer（`NxConnectionModel`）把这些规则从 Activity 中拆出来，并让每个异步回调携带 attempt 编号。

---

## 2. 代码与行为

### 2.1 新增文件

| 文件 | 作用 |
|---|---|
| `android/app/src/main/java/dev/nightdesk/android/NxConnection.kt` | 连接阶段、失败分类、恢复动作、20 秒截止时间、旧 attempt 过滤 |
| `android/app/src/test/java/dev/nightdesk/android/NxConnectionTest.kt` | 10 项 JVM 状态机与竞态单测 |

### 2.2 修改文件

| 文件 | 改动 |
|---|---|
| `android/app/src/main/java/dev/nightdesk/android/RemoteActivity.kt` | 接入状态机；新增“重试 / 配对码”按钮；超时任务；后台暂停；输入状态释放；共享字段可见性 |

### 2.3 连接阶段

```text
Connecting ──streaming──> Streaming ──closed──> Disconnected
    │                         │
    ├──timeout/error──────────┴──────────────> Failed
    │
    └──onStop────────────────────────────────> Background
```

- `Connecting`：已创建 attempt，等待 QUIC + TLS 握手和首个 streaming 状态。
- `Streaming`：收到 `mobile.Mobile.StateStreaming`，清除配对码和截止时间。
- `Failed`：保留第一个可操作错误，不让后续 `closed` 覆盖。
- `Disconnected`：曾经 streaming 后连接关闭，可直接重试。
- `Background`：Activity 离开前台，主动关闭传输并要求用户返回后显式重试。

### 2.4 失败分类与唯一恢复动作

| 错误码 | 用户提示 | 恢复动作 |
|---|---|---|
| `bad-code` | 配对码错误或过期 | 重新输入 6 位配对码 |
| `untrusted` | 主机尚未信任此设备 | 重新配对 |
| `host-identity` | 主机证书指纹不匹配 | 必须重新扫码；不提供重试或配对码输入 |
| `identity` | 本机客户端身份不可读 | 不提供自动修复 |
| `locked` | 错误次数过多 | 可重试 |
| `replaced` | 另一台设备接管 | 可重试 |
| `timeout` | 连接超时 | 可重试 |
| `disconnected` / `link-error` | 连接断开 | 可重试 |

设计原则：**模型绝不自动重连**。自动重连会在配对码错误、证书变化或主机锁定等场景制造不可见的重试风暴；所有恢复都由用户点击触发。

### 2.5 异步竞态处理

- 每次 `begin()` 生成递增的 `attempt`（`Long`）。
- `Callback` 持有自己的 attempt；所有 UI 回调先检查 `request == generation`。
- 旧会话的 `streaming`、`error`、`closed`、frame 和 input receipt 都不会覆盖新会话。
- `session` 与 `generation` 标记为 `@Volatile`：Go 回调线程、单线程 I/O executor 和 UI 线程之间的可见性不再依赖偶然时序。
- 超时任务在 `DefaultTimeoutMillis + 500` 后执行；只有仍在 `Connecting` 且 attempt 未被替换时才生效。
- Go `NewSession` 自身有 15 秒超时；前端 20 秒截止时间用于兜底。超时后晚到的 session 会立即 `close()`，不会挂到 Activity 上。

### 2.6 生命周期与输入安全

- `onStop()`：给当前 attempt 标记 `Background`，递增 generation，调用 `closeTransport(clearScreen = true)`，释放所有按键/鼠标状态并关闭会话。
- 返回前台后状态保留为“已暂停”，必须点击“重试”才会新建连接。
- `onDestroy()`：移除超时回调、标记后台、关闭传输、关闭单线程 executor。
- `closeTransport()`：清空 `session`、左键状态、滚动状态和主指针 ID；I/O 线程执行 `releaseAll()` 后 `close()`。
- `onWindowFocusChanged(false)` 仍会立即释放按住的状态，避免切走窗口时 Windows 端残留按键。
- 只有尺寸有效且与上次不同才写 `NxProfileStore.markConnected()`。
- 状态栏只显示用户可理解的提示，不展示原始异常细节；原始 code/detail 保留在状态模型中供后续诊断扩展。

---

## 3. 验证记录

### 3.1 Windows 桌面端

```powershell
$dotnet='C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe'
& $dotnet run --project 'C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj' -c Release --no-restore
```

结果：

- **176 tests passed.**
- 输出 `No display state changed.`
- 本轮没有启动真实 `nx-host.exe`，没有抓屏，没有 SendInput，没有修改显示拓扑。

### 3.2 Android JVM 单测

```bash
NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-stage5 \
bash /home/tony/dev/nightdesk-android-stage5/scripts/build-ai.sh \
  -q -PnightdeskVersionCode=7 -PnightdeskVersionName=1.1.0
```

| 测试类 | 数量 | 结果 |
|---|---:|---|
| `CoreTest` | 9 | 通过 |
| `NxConnectionTest` | 10 | 通过 |
| `NxTest` | 7 | 通过 |
| **合计** | **26** | **全部通过** |

覆盖点：首次连接、streaming、超时边界、新 attempt 隔离旧回调、错误分类、指纹变化不可重试、错误后 closed 不覆盖、后台释放、重复错误保留首因。

### 3.3 Android Lint / Release 构建

- `lintRelease`：**0 errors / 22 warnings**。
- 警告主要是硬编码中文 UI 文案（`SetTextI18n`）等既有问题，不阻断构建。
- `assembleDebug` / `assembleRelease`：成功。
- 构建日志只有 SDK XML 版本 4 与旧命令行工具兼容性警告。

### 3.4 Android 仪器测试

设备：`emulator-5580`（Android x86_64 模拟器）。

```bash
adb -s emulator-5580 install -r app/build/outputs/apk/release/app-release.apk
adb -s emulator-5580 install -r app/build/outputs/apk/androidTest/release/app-release-androidTest.apk
adb -s emulator-5580 shell am instrument -w \
  -e class dev.nightdesk.android.HostStoreInstrumentedTest \
  dev.nightdesk.android.test/androidx.test.runner.AndroidJUnitRunner
adb -s emulator-5580 shell am instrument -w \
  -e class dev.nightdesk.android.UpdateFlowInstrumentedTest \
  -e updateOrigin https://tony-ai-pc-1.tail9f7f1f.ts.net \
  dev.nightdesk.android.test/androidx.test.runner.AndroidJUnitRunner
```

结果：

- `HostStoreInstrumentedTest: OK (3 tests)`，`1.523s`
- `UpdateFlowInstrumentedTest: OK (1 test)`，`1.146s`
- 合计 **4 项通过**。

### 3.5 模拟器 UI 冒烟

| 截图 | 内容 |
|---|---|
| `C:\dev\NightDesk\artifacts\android\stage5-home.png` | 新 APK 主界面，扫码入口、NX 主机区和 HTTPS 兼容入口可见 |
| `C:\dev\NightDesk\artifacts\android\stage5-offline-timeout.png` | 故意连接不可达的 CGNAT 地址后，显示断开提示与“重试”按钮；未接触生产主机 |

离线冒烟使用 `100.127.255.254:65534`，只验证网络失败路径。Go 层很快返回 link error，因此界面显示“连接已断开”，而不是等到 20 秒后的 `timeout`；两者都走同一个可重试状态。

---

## 4. 构建产物

本阶段产物只保留在本地 `artifacts`（Git 忽略），**没有发布到 AI 更新源**。线上仍是 versionCode 5 / 1.0.4。

| 文件 | 大小（字节） | SHA-256 |
|---|---:|---|
| `artifacts\android\nightdesk-1.1.0-stage5.apk` | `55058900` | `01734a26365b859c831a9ba7806ff1d133b96e0993061053bb2a60ab72caf480` |
| `artifacts\android\nightdesk-1.1.0-stage5-debug.apk` | `56886382` | `042c9d37dffa86c7507ee8ab358415d46eb921cd55700328e8ef393034fc08aa` |
| `artifacts\android\nightdesk-1.1.0-stage5-androidTest.apk` | `689645` | `f9d11858d818aaa073f39fc404ea716af5043c2f6093bc0454b8ef52da1b4c36` |

Release APK 元数据：

- 包名：`dev.nightdesk.android`
- versionCode：**7**
- versionName：**1.1.0**
- minSdk：**29**
- targetSdk：**35**
- 签名证书 SHA-256：`41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`
- 签名主体：`CN=NightDesk Release`

复核命令：

```bash
aapt2 dump badging app-release.apk | head -4
apksigner verify --print-certs app-release.apk
sha256sum app-release.apk
```

---

## 5. 从零恢复本阶段

### 5.1 获取代码

```bash
git clone git@github.com:orangepro2001/NightDesk.git
cd NightDesk
git checkout a0746dc03b2c45859bc5bd77bd6154fbed7f32af
```

阶段 5 的代码提交是 `a0746dc`；阶段 5 文档提交在其后，以 `git log --oneline` 为准。

### 5.2 Windows 验证

```powershell
$dotnet='C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe'
& $dotnet run --project 'C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj' -c Release --no-restore
```

预期最后一行：`176 tests passed. No display state changed.`

只读诊断（不会改显示拓扑）：

```powershell
& 'C:\dev\NightDesk\artifacts\win-x64-1.3.1\NightDesk.exe' --diagnose --output 'C:\dev\NightDesk\diag.json'
```

### 5.3 Android 交叉构建

本机没有 Android 工具链；只使用 SSH alias `AI`。

```bash
ssh AI
cd /home/tony/dev/nightdesk-android-stage5
NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-stage5 \
bash scripts/build-ai.sh -q -PnightdeskVersionCode=7 -PnightdeskVersionName=1.1.0
```

如果该目录丢失，重新把仓库的 `android/` 目录同步到 AI（排除 `.gradle`、`.kotlin`、`build`），再执行上述命令。签名材料位于 AI 的 `~/.local/share/nightdesk-signing/`，不得复制进仓库。

### 5.4 仪器测试

```bash
cd /home/tony/dev/nightdesk-android-stage5
export JAVA_HOME=/home/tony/.local/opt/jdk-17
export ANDROID_HOME=/home/tony/Android/Sdk
export PATH=$JAVA_HOME/bin:/home/tony/.local/opt/gradle-8.11.1/bin:/home/tony/Android/Sdk/platform-tools:/home/tony/Android/Sdk/build-tools/35.0.0:$PATH
export NIGHTDESK_KEYSTORE=$HOME/.local/share/nightdesk-signing/release.p12
export NIGHTDESK_STORE_PASSWORD="$(cat $HOME/.local/share/nightdesk-signing/password)"
gradle --no-daemon assembleReleaseAndroidTest -q \
  -PnightdeskVersionCode=7 -PnightdeskVersionName=1.1.0
```

然后按第 3.4 节安装并运行测试。不要打印或提交签名口令。

### 5.5 APK 校验

```powershell
Get-FileHash 'C:\dev\NightDesk\artifacts\android\nightdesk-1.1.0-stage5.apk' -Algorithm SHA256
Get-Item 'C:\dev\NightDesk\artifacts\android\nightdesk-1.1.0-stage5.apk' | Select-Object Length
```

应与第 4 节完全一致。

---

## 6. 尚未完成的真实验收

以下项目需要一台**显示器和输入可安全恢复的独立 Windows 测试机**，不能在本机 PID 13816 的生产桌面上做：

1. 在测试机部署 `NightDesk.exe` 和匹配的 `nx-host.exe`。
2. 启动一个可丢弃的 Windows 会话，运行 sidecar，生成二维码。
3. 用 Android 真机（不是模拟器）通过 tailnet 扫码。
4. 验证首帧、tile 差分、鼠标移动/点击、滚轮、Unicode 文本、组合键。
5. 验证断网、切后台、强制停止、另一台设备接管、证书变化、错误配对码。
6. 验证 Windows 端在断线后没有残留按键/鼠标按下状态。
7. 验证 GDI 抓屏在 DPI 缩放、显示器休眠、锁屏和虚拟屏下不会黑屏或崩溃。
8. 记录首帧时间、稳态带宽、CPU、内存和输入回执延迟。

### 当前明确不能声称的能力

- 不是 Sunshine/Parsec 式 H.264/HEVC/AV1 视频串流。
- 不支持音频、视频播放、游戏或高动态桌面。
- 不支持多显示器切换、剪贴板、文件传输、动态码率。
- 没有真实 Windows 抓屏/输入验收数据。
- 没有真实 Android 设备的 tailnet 验收数据。

---

## 7. 后续开发建议

在不触碰生产显示器的前提下，下一步可以继续做：

1. **RemoteActivity 仪器冒烟**：用不可达地址启动 Activity，断言出现“重试”并验证返回键不崩溃。当前用截图人工确认，未做成自动断言。
2. **输入回执可视化**：把 `onInput` 的失败原因显示在状态栏，而不是只显示“主机拒绝了输入操作”。
3. **完整键盘修饰键**：当前是固定组合键菜单 + Unicode 文本，可增加 Ctrl/Alt/Win 粘滞键。
4. **sidecar 诊断包**：把 sidecar 日志、版本、sidecar 参数和最近事件写入可导出的诊断文件，仍不包含配对码或密钥。
5. **Android 前台策略**：当前切后台即断开是安全优先设计；若未来需要后台保活，必须单独设计通知、权限、超时和输入释放。
6. **Windows 测试机**：这是端到端验收的唯一硬阻塞项；优先准备独立的物理机或可交互虚拟机。

---

## 8. 现场安全状态（必须继续遵守）

- 生产 NightDesk 进程：**PID 13816**，路径 `C:\Users\pixel\Desktop\NightDesk.exe`。
- 在物理显示器和恢复快照确认安全前，禁止：
  - 退出、重启或替换 PID 13816；
  - 熄屏、点亮、恢复显示拓扑；
  - 增删 VDD；
  - 安装或卸载显示驱动；
  - 在生产桌面运行真实 `nx-host.exe` 抓屏或 SendInput；
  - 用生产桌面做端到端测试。
- `docs/HANDOFF.md` 的现场状态优先级高于任何旧的 `task_plan.md` / `progress.md` 记录。
- Android 工具链只在 SSH alias `AI` 上使用；不要操作 tmux 会话 `main`。
- 非交互 SSH 中 adb 不在 PATH，使用 `/home/tony/Android/Sdk/platform-tools/adb`。
- 签名私钥和口令不得进入源码、日志、文档或回复。

---

## 9. 文件索引

```text
android/app/src/main/java/dev/nightdesk/android/
  NxConnection.kt          连接状态机与失败分类
  RemoteActivity.kt        远控 UI、生命周期、传输释放
android/app/src/test/java/dev/nightdesk/android/
  NxConnectionTest.kt      10 项连接状态单测
docs/
  20261027-003801-go-nx-remote-control-integration-plan.md
  20261027-012049-remote-control-stage-1-handoff.md
  20261027-014900-remote-control-stage-2-windows-handoff.md
  20261027-022517-remote-control-stage-3-windows-qr-handoff.md
  20261027-030000-remote-control-stage-4-android-viewer-handoff.md
  20261027-033356-remote-control-stage-5-lifecycle-handoff.md
  HANDOFF.md
```
