# NightDesk 远程控制阶段 4 恢复文档：Android 固定证书查看器

| 项目 | 内容 |
|---|---|
| 生成时间 | 2026-10-27 03:00 KST（Asia/Seoul） |
| 仓库 | `git@github.com:orangepro2001/NightDesk.git` |
| 分支 | `main` |
| 本阶段代码提交 | `6d37032cdd492d18d6bb2bed59170afee669ff89` |
| 提交标题 | `feat(android): integrate pinned low-bandwidth remote viewer` |
| 前置提交 | `2f78f1cd...`（阶段 3 恢复文档；以 `git log` 为准） |
| 本阶段结论 | Android 已具备独立的 `nightdesk-nx` 扫码、主机保存、证书 pin、QUIC/TLS 连接、tile 绘制、触摸/滚轮/键盘/Unicode 输入链路；已完成编译、单测、Lint、仪器测试和模拟器 UI 冒烟。**尚未完成真实 Windows 抓屏、SendInput 与真机 tailnet 端到端验收。** |

> **交付边界**
>
> 本阶段证明 Android 客户端可以构建、启动、解析并进入远控界面，不证明现场 Windows 显示器和输入环境已经安全可用。该传输是无损 tile 差分协议，不是 Sunshine/Parsec 一类的视频编码串流；高动态桌面、视频和游戏的体验预期应保持在“低带宽静态办公”范围。

---

## 1. 本阶段解决的核心问题

阶段 3 已让 Windows 生成包含地址、证书指纹、6 位短期配对码和屏幕尺寸的二维码，但 Android 仍只会处理 HTTPS 设备绑定。阶段 4 在同一个扫码入口中增加了协议分流：

```text
扫码 JSON
  ├─ kind = nightdesk-pair  → 原 HTTPS 管理面绑定
  └─ kind = nightdesk-nx    → go-nx 固定证书屏幕远控
```

两条链路保持隔离：

- HTTPS 管理面继续使用 `ProfileStore` 和长期设备令牌；
- NX 数据面使用 `NxProfileStore`，只保存地址、证书指纹、名称、尺寸和最近连接时间；
- 6 位 NX 配对码只通过内存 `Intent` 传给 `RemoteActivity`，不写入长期存储；
- NX 主机删除只移除 Android 本地记录，不撤销 Windows 端已保存的客户端证书信任。

---

## 2. 已完成内容

### 2.1 NX 配对载荷与主机存储

新增 `android/app/src/main/java/dev/nightdesk/android/Nx.kt`：

- `NxPairing.parse()`：
  - 只接受 `v=1`、`kind=nightdesk-nx`；
  - 严格校验 64 位十六进制证书指纹和 6 位数字配对码；
  - 拒绝已过期二维码；
  - 名称清除控制字符，最长 64 字符；
  - 宽高统一限制为 `64..16384`，与 Windows `NxPairing` 边界一致。
- `NxPairing.validateAddress()`：
  - 只接受 Tailscale CGNAT IPv4 `100.64.0.0/10` 和 `1..65535` 端口；
  - 拒绝局域网地址、回环地址、IPv6 和非法端口。
- `NxHostCodec`：
  - 最多保存 20 台主机；
  - 以证书指纹去重；
  - 保存内容不含短期配对码；
  - 数据损坏时由 `MainActivity` 清空本地 NX 列表，不影响 HTTPS 绑定数据。
- `NxGeometry`：保持宽高比、居中显示、把视图触摸坐标映射到主机像素。
- `NxText`：按 Unicode 码点拆分 UTF-8 文本，默认每段不超过 1024 字节，避免切断代理对。

`NxProfileStore` 使用独立 SharedPreferences 文件 `nx_profiles`。地址和指纹不是秘密，因此保留明文；长期客户端私钥由 gomobile 核心保存在应用私有目录 `files/nx-identity`。

### 2.2 Android 主界面接线

修改 `MainActivity.kt`：

- 原“扫描桌面二维码”统一识别两种 kind：
  - `nightdesk-pair` 走原 HTTPS 绑定流程；
  - `nightdesk-nx` 保存主机并直接打开 `RemoteActivity`。
- 新增“屏幕远控（低带宽）”区域：
  - 扫描屏幕远控二维码；
  - 列出已保存 NX 主机；
  - 打开选中主机；
  - 删除选中主机；
  - 明确提示该模式不适合视频或游戏。
- `onResume()` 重新读取 NX 主机列表，确保从远控界面返回后刷新。
- 原有 HTTPS 多主机、更新检查和令牌流程未移除。

### 2.3 Android NX 查看器

新增：

- `android/app/src/main/java/dev/nightdesk/android/RemoteActivity.kt`
- `android/app/src/main/java/dev/nightdesk/android/RemoteScreenView.kt`

`RemoteActivity` 已实现：

- 使用 `mobile.Mobile.newSession(storageDir, addr, fingerprint, code, callback)` 建立固定证书的 QUIC + TLS 1.3 会话；
- 首次扫码传入短期配对码；后续打开已保存主机时不带配对码，直接依赖客户端证书信任；
- 会话状态、错误码和输入回执显示在顶部状态栏；
- 配对码错误或主机不受信任时提示重新配对；
- 单指拖动映射鼠标移动和左键；
- 双指上下移动映射滚轮；
- “右键”按钮映射鼠标右键；
- “按键”菜单提供 Enter、Tab、Esc、Backspace、Delete、方向键、Win、Alt+Tab、Ctrl+C/V/S/Z、Ctrl+Shift+Esc；
- “键盘”按钮通过 Unicode 文本注入发送多行文字；
- 界面失焦、`ACTION_CANCEL`、切后台或退出时调用 `ReleaseAll`，并关闭会话；
- 屏幕保持常亮，但不启动前台服务；当前设计是在应用离开前台时主动断开，避免后台隐藏控制。

`RemoteScreenView` 已实现：

- 使用持久 `ARGB_8888` 位图保存完整主机帧；
- `OnFrameBegin` 只在尺寸变化时重建位图，尺寸不变时保留上一帧；
- `OnTile` 把紧密 BGRA tile 同步复制到 patch bitmap，再绘制到持久帧；
- 强制把 GDI 未定义 alpha 改为 `0xFF`，避免透明 tile；
- 按比例居中绘制主机画面，黑边区域不发送输入；
- 所有 tile 回调中的 Go 字节数组只在回调期间使用，不跨线程保存。

### 2.4 gomobile AAR 接入

新增并提交：

```text
android/app/libs/mobile.aar
```

AAR 关键信息：

| 项目 | 值 |
|---|---|
| 大小 | `21905381` 字节 |
| SHA-256 | `247a0f29ceac4d6f6505da9e358b2722a3b69e99b7fc6b28650972fea448f890` |
| ABI | `armeabi-v7a`, `arm64-v8a`, `x86`, `x86_64` |
| 来源 | AI 主机 `/home/tony/dev/nightdesk-outputs/stage4/mobile.aar` |
| Go 模块 | `github.com/orangepro2001/nightdesk/native` |

该 AAR 已实际通过 Android Debug/Release 编译、单元测试、Lint 和模拟器安装。重新执行 gomobile 可能得到不同整体 SHA-256，原因是原生 `.so` 构建非确定性与重新编译；这不代表 `classes.jar`、Manifest 或 Go API 发生变化。除非有明确修复并重新完成全部验证，否则不要随意替换当前 AAR。

---

## 3. 数据流与输入模型

### 3.1 连接与认证

```text
Windows WPF
  → NxHostService 启动 nx-host.exe
  → 生成短期 6 位码
  → 二维码携带 addr / certificate fingerprint / code / screen size
  → Android NxPairing 严格校验
  → Go mobile.Session
  → QUIC + TLS 1.3
  → 先验证主机证书 fingerprint
  → Hello 携带 code
  → Windows trust.Policy 保存客户端证书指纹
  → 后续连接直接使用持久客户端证书
```

### 3.2 屏幕更新

```text
Windows GDI 采集
  → 持久帧 + tile 差分
  → 只编码变化 tile
  → QUIC unidirectional stream
  → Android OnFrameBegin(width, height)
  → 一个或多个 OnTile(BGRA)
  → RemoteScreenView 更新持久 bitmap
```

该协议不生成 H.264/AV1 视频帧，也没有音频、硬件视频解码或动态码率编码。它的优势是静态桌面几乎不传数据、实现简单、无需视频编解码器和 GPU 驱动配合；代价是高动态画面会产生大量 tile 数据。

### 3.3 输入

| Android 操作 | 发送内容 |
|---|---|
| 单指按下并拖动 | `move` + 左键 `mouseDown/mouseUp` |
| 双指上下移动 | `wheel` |
| 顶部“右键” | `move` + 右键 down/up |
| “按键”菜单 | Win32 virtual-key down/up 与组合键 |
| “键盘”文本 | 分段 Unicode 文本注入 |

输入在工作线程串行发送；会话切换使用 generation 号丢弃旧回调，避免断开后的旧连接覆盖新界面。

---

## 4. 文件地图

```text
android/app/libs/mobile.aar                              gomobile 生成的原生核心
android/app/build.gradle.kts                             AAR 依赖与 1.1.0 / versionCode 6
android/app/src/main/AndroidManifest.xml                 RemoteActivity 声明
android/app/src/main/java/dev/nightdesk/android/Nx.kt
  - NxPairing / NxHost / NxHostCodec / NxProfileStore
  - NxGeometry / NxText
android/app/src/main/java/dev/nightdesk/android/RemoteActivity.kt
  - 连接、UI、触摸、滚轮、按键、文本、生命周期
android/app/src/main/java/dev/nightdesk/android/RemoteScreenView.kt
  - 持久帧、tile patch、BGRA 复制、居中缩放和坐标映射
android/app/src/main/java/dev/nightdesk/android/MainActivity.kt
  - 两种二维码 kind 分流、NX 主机列表和入口
android/app/src/test/java/dev/nightdesk/android/NxTest.kt
  - 配对、地址、存储、几何、文本分块 7 项单测
android/scripts/build-ai.sh
android/scripts/build-androidtest.sh
android/scripts/run-androidtest.sh
android/scripts/run-instrumented.sh
  - 支持 NIGHTDESK_ANDROID_DIR / NIGHTDESK_ANDROID_SERIAL
src/NightDesk/NxPairing.cs                               Windows 端边界同步到 64..16384
tests/NightDesk.Tests/Program.cs                         新增非 Tailscale/过小/过大断言
```

---

## 5. 构建与验证记录

### 5.1 Windows

环境：

```text
dotnet 10.0.402
C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe
```

命令：

```bash
/mnt/c/Users/pixel/scoop/apps/dotnet-sdk/10.0.402/dotnet.exe run \
  --project tests/NightDesk.Tests/NightDesk.Tests.csproj -c Release --no-restore

/mnt/c/Users/pixel/scoop/apps/dotnet-sdk/10.0.402/dotnet.exe build \
  src/NightDesk/NightDesk.csproj -c Release --nologo
```

结果：

```text
161 tests passed. No display state changed.
0 warnings / 0 errors
```

新增断言覆盖：

- 拒绝非 Tailscale 的 NX 二维码地址；
- 拒绝小于 64 像素或大于 16384 像素的屏幕尺寸。

### 5.2 Go 原生核心

在 AI 主机临时目录 `/home/tony/dev/nightdesk-native-stage4` 执行：

```bash
go test ./...
```

结果：

```text
internal/proto   ok
internal/remote  ok
internal/tile    ok
internal/trust   ok
mobile           ok
```

`gofmt -l .` 只报告两个既有测试文件存在多余空行：

```text
internal/proto/proto_test.go
internal/trust/policy_test.go
```

这不是阶段 4 改动，未顺手格式化以避免无关 diff。

### 5.3 gomobile 重新生成命令

```bash
export JAVA_HOME=/home/tony/.local/opt/jdk-17
export PATH=/home/tony/.local/opt/jdk-17/bin:/home/tony/go/bin:/usr/local/bin:/usr/bin:/bin
export ANDROID_HOME=/home/tony/Android/Sdk
export ANDROID_SDK_ROOT=/home/tony/Android/Sdk
export ANDROID_NDK_HOME=/home/tony/Android/Sdk/ndk/28.2.13676358
cd /home/tony/dev/nightdesk-native-stage4
gomobile bind \
  -target=android/arm,android/arm64,android/386,android/amd64 \
  -androidapi 29 \
  -o /tmp/mobile-stage4.aar ./mobile
```

非交互 SSH 必须显式设置 PATH；直接继承 Windows PATH 可能产生：

```text
export: not valid in this context: Files/WSL:/mnt/c/Program ...
```

### 5.4 Android 构建

源码同步目录：

```text
/home/tony/dev/nightdesk-android-stage4
```

命令：

```bash
NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-stage4 \
bash /home/tony/dev/nightdesk-android-stage4/scripts/build-ai.sh \
  -q -PnightdeskVersionCode=6 -PnightdeskVersionName=1.1.0
```

结果：`BUILD_OK`

Android 单元测试：

```text
CoreTest: 9 passed
NxTest:   7 passed
合计 16 passed，0 failures
```

Lint：

```text
0 errors, 24 warnings
```

警告主要是动态构造中文文本触发的 `SetTextI18n` 等非阻断 lint；当前 `lint.abortOnError = true` 仍未触发。

### 5.5 APK 产物

AI 主机路径：

```text
/home/tony/dev/nightdesk-android-stage4/app/build/outputs/apk/debug/app-debug.apk
/home/tony/dev/nightdesk-android-stage4/app/build/outputs/apk/release/app-release.apk
```

本机副本：

```text
C:\dev\NightDesk\artifacts\android\nightdesk-1.1.0-stage4-debug.apk
C:\dev\NightDesk\artifacts\android\nightdesk-1.1.0-stage4.apk
```

| 产物 | 大小 | SHA-256 |
|---|---:|---|
| Debug APK | `56648150` | `779b9a810fe116eae394b6b42d65d2283244bff3fc28bacd564238d5c3cedc53` |
| Release APK | `55042512` | `67f7c819e29b583bf3f4020e9ea257610ead64f117d3e9e8fd508014211740cd` |

Release 元数据：`versionCode=6`，`versionName=1.1.0`。本阶段**没有发布到 AI 更新源**；不要在未做真机验收和版本策略确认前覆盖 `latest.apk`。

### 5.6 Android 仪器测试

多主机存储回归测试：

```bash
NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-stage4 \
NIGHTDESK_ANDROID_SERIAL=emulator-5580 \
bash /home/tony/dev/nightdesk-android-stage4/scripts/run-instrumented.sh
```

结果：

```text
HostStoreInstrumentedTest: OK (3 tests)
Time: 0.875
```

### 5.7 模拟器 UI 冒烟

截图：

```text
C:\dev\NightDesk\artifacts\android\stage4-home.png
大小：242913 字节
SHA-256：381bb733d3ad9f76fced2da0c0ae7b39ea3d91500fd6e46458abde4315407088
```

已确认主界面正常显示：

- “扫描桌面二维码（绑定 / 屏幕远控）”；
- “屏幕远控（低带宽）”；
- “扫描屏幕远控二维码”；
- “打开选中主机”；
- “删除选中主机”；
- “不适合视频或游戏画面”的说明。

本阶段没有用真实 Windows 主机生成二维码，也没有在真机上完成 QUIC 连接，因此截图只覆盖 UI 接线，不覆盖数据面。

---

## 6. 安全边界

### 6.1 当前现场只读快照

2026-10-27 03:00 KST 只读检查：

```text
NightDesk PID 13816
C:\Users\pixel\Desktop\NightDesk.exe
启动时间：2026/10/27 0:07:24
%LOCALAPPDATA%\NightDesk\recovery.json：当时不存在
```

`recovery.json` 本次检查不存在，但这不等于可以安全操作显示拓扑、替换进程或启动真实采集。实际物理显示器、DPI、虚拟显示器和快照状态仍需独立复核。

### 6.2 本阶段明确没有执行

- 没有停止、重启或替换 PID 13816；
- 没有熄屏、点亮、恢复快照；
- 没有增删 VDD；
- 没有安装或卸载显示驱动；
- 没有修改显示拓扑；
- 没有在 NightDesk 本机启动真实 `nx-host.exe` 做抓屏或 SendInput；
- 没有在真实手机上连接 tailnet 数据面。

### 6.3 真实端到端验收前置条件

建议使用一台独立 Windows 测试机，至少有：

1. 一块可安全断开的物理显示器；
2. 可恢复的显示快照；
3. Tailscale 登录；
4. Windows 防火墙允许 UDP 7000（或测试所用端口）；
5. 可随时物理接触/恢复的测试机；
6. 测试前记录 `Get-CimInstance Win32_VideoController`、显示拓扑、DPI 和扩展屏位置。

不要把生产桌面的 `NightDesk.exe` 进程当作测试夹具。

---

## 7. 已知限制

### 7.1 传输与体验

- 不是视频流：使用无损 tile 差分，不包含视频编码、动态码率、帧间预测和音频。
- 静态文字/办公桌面适合；视频、游戏、滚动网页、高分辨率高刷新桌面可能带宽和 CPU 开销明显。
- 当前没有音频通路。
- 当前没有剪贴板同步和文件传输。
- 当前只有单显示器选择（由 Windows sidecar `-display` 指定），没有客户端切换多显示器。

### 7.2 生命周期

- `RemoteActivity` 在 `onStop()` 主动断开并 `ReleaseAll`，没有前台服务保活；这是为避免隐藏会话和输入残留的有意选择。
- 网络中断后没有自动重连；需要返回并重新打开主机。
- 第一次配对成功后，如果 Windows 端删除客户端信任，Android 端仍保留主机卡片，但连接会被拒绝，需要重新扫码配对。

### 7.3 输入

- 当前触摸是“拖动鼠标”，不是直接点击注入；单指按下后移动会移动指针，抬起时左键释放。
- 组合键菜单是固定集合，尚未实现完整软键盘映射和物理键盘捕获。
- Unicode 文本通过注入发送，不保证所有以管理员权限运行、受保护或 UIPI 隔离的应用接受输入。
- Windows 端尚未在真实 GDI 抓屏和 SendInput 环境验收。

### 7.4 安全

- 二维码只允许 Tailscale `100.64.0.0/10` 数据面地址，避免误把局域网/公网地址当作已验证通道。
- 证书 pin 防止中间人替换主机，但不替代 Tailscale ACL。
- 6 位码有 Windows 端 5 次失败/1 分钟锁定策略；仍需在真实网络上验证首次连接、重放和错误码体验。
- Android 本地 NX 主机列表没有加密，因为不含长期秘密；客户端私钥在应用私有目录，仍需验证备份/迁移策略。

---

## 8. 下一阶段恢复步骤

### 8.1 先在独立测试机完成 Windows 数据面验收

1. 构建 `native/cmd/nx-host` Windows x64 可执行文件；
2. 在测试机选择一块可安全断开的显示器；
3. 启动 sidecar，打开配对窗口，生成 NX 二维码；
4. 手机与测试机加入同一 tailnet；
5. 扫码，核对二维码地址、端口、SHA-256 指纹和 6 位码；
6. 验证首帧、静态画面、窗口拖动、文字输入、右键、滚轮、失焦释放和断线重连；
7. 验证 Windows 端删除信任后旧客户端被拒绝；
8. 验证错误码、过期码、锁定和防火墙拒绝路径；
9. 记录分辨率、DPI、带宽、CPU、首帧耗时和输入回执延迟；
10. 只有通过后，才考虑发布 APK 和更新源。

### 8.2 再完成 Android 生命周期与交互加固

建议按以下顺序：

1. 增加真实或合成 `RemoteActivity` instrumented smoke test；
2. 增加连接失败、重试、超时和“主机指纹变化”的 UI 状态；
3. 评估前台服务/通知保活；如果保留“切后台断开”，应在文档和 UI 中保持一致；
4. 增加剪贴板、快捷键扩展和物理键盘支持；
5. 增加多显示器列表与显示选择；
6. 根据合成/真实带宽数据决定是否需要 H.264/AV1 或自适应降采样；不要在没有基准数据前引入视频栈。

### 8.3 发布前检查

- 保持现有 Android 签名证书；
- 重新跑 Windows 161+ 测试、Android 16 单测、Lint、仪器测试；
- 生成真实设备截图和网络日志，但不得包含配对码、令牌或私钥；
- 更新 `docs/HANDOFF.md`、`README.md`、`docs/ACCEPTANCE.md`；
- 将 APK 发布到 AI 更新源前先做版本/哈希/签名校验；
- 保留上一版本 APK 和回滚说明。

---

## 9. 快速恢复命令

### 9.1 仓库状态

```bash
cd /mnt/c/dev/NightDesk
git status --short --branch
git log --oneline -10
```

### 9.2 Windows 验证

```bash
/mnt/c/Users/pixel/scoop/apps/dotnet-sdk/10.0.402/dotnet.exe run \
  --project tests/NightDesk.Tests/NightDesk.Tests.csproj -c Release --no-restore

/mnt/c/Users/pixel/scoop/apps/dotnet-sdk/10.0.402/dotnet.exe build \
  src/NightDesk/NightDesk.csproj -c Release --nologo
```

### 9.3 Android 验证

```bash
NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-stage4 \
bash /home/tony/dev/nightdesk-android-stage4/scripts/build-ai.sh \
  -q -PnightdeskVersionCode=6 -PnightdeskVersionName=1.1.0

NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-stage4 \
NIGHTDESK_ANDROID_SERIAL=emulator-5580 \
bash /home/tony/dev/nightdesk-android-stage4/scripts/run-instrumented.sh
```

### 9.4 现场只读检查

```powershell
Get-Process NightDesk -ErrorAction SilentlyContinue |
  Select-Object Id, Path, StartTime
Test-Path (Join-Path $env:LOCALAPPDATA 'NightDesk\recovery.json')
Get-CimInstance Win32_VideoController |
  Select-Object Name, DriverVersion, Status
```

在任何真实连接、熄屏、VDD 或驱动操作前，先阅读 `docs/HANDOFF.md` 的现场安全边界并确认测试机不是生产桌面。

---

## 10. 本阶段提交与推送

代码提交：

```text
6d37032cdd492d18d6bb2bed59170afee669ff89
feat(android): integrate pinned low-bandwidth remote viewer
```

本文件、`docs/HANDOFF.md` 与根目录状态文档随后作为独立的 `docs:` 提交推送。恢复时以 `git log --oneline` 和 `git remote -v` 为准，不要把本文中的 hash 当作远端最新 HEAD。

推送建议：

```bash
git push origin main
```

若本机 HTTPS 推送不稳定，可沿用阶段 3 文档中的 Git bundle 经 `AI` SSH 主机推送；不要在 AI 主机现有的非 GitHub clone 上直接修改或强推。
