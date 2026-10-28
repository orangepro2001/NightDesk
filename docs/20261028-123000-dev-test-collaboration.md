# NightDesk 开发与测试协同指南

更新时间：2026-10-28（Asia/Seoul）
适用版本：Windows 1.6.4 / nx-host 0.2.2 / Android 1.3.4
相关：[README](../README.md) · [交接文档](HANDOFF.md) · [验收清单](ACCEPTANCE.md)

## 0. 一句话说清分工

**本机负责「改」和「验」，AI 主机负责「编」和「跑」，GitHub CI 负责在真 Windows 上「守门」。三处都只认 GitHub `main`。**

| 能力 | 本机 MINIPC-XOH4T（Windows + WSL） | AI 主机 tony-ai-pc-1（Linux） | GitHub CI |
|---|---|---|---|
| 代码编辑、评审、agent 交互 | **主** | 可选（nvim/tmux） | — |
| Windows 编译 | 应急兜底 | **主**（交叉编译，约 15 秒） | 主（用于出正式 artifact） |
| Windows 全部断言（1.6.4 为 216 项） | 应急兜底 | **不可** | **主** |
| Go 单元测试 / vet / Windows 交叉构建 | 可 | **主** | 未启用 |
| Android 构建 / 单元测试 / lint | 应急兜底 | **主** | 主（debug） |
| Android 仪器测试（模拟器） | 不可 | **主** | 不可（runner 无 KVM） |
| Android release 签名 | 不可 | **主**（签名密钥在 AI 主机） | 不可 |
| 真实 GDI / VDD / SendInput / 显示拓扑 | **唯一可做** | 不可 | 不可 |
| Android 真机 | **唯一可做** | 不可 | 不可 |
| 更新源发布 | — | **主** | — |

本机看起来「只做两件事」，但这两件事恰好是不可替代的：**真实桌面环境**和**人的判断**。详见 §1.3。

## 1. 职责边界

### 1.1 AI 主机的既有职责

- 唯一的日常编译机：Linux 上交叉编译 `win-x64`，产出 `NightDesk.exe` + `nx-host.exe`。
- Go 侧全量验证：`go test ./...`、`go vet ./...`、`GOOS=windows` 交叉构建、`gomobile bind` 重建 AAR。
- Android 全链路：单元测试、lint、debug/release APK（用仓库外的固定签名密钥）、模拟器仪器测试。
- 更新源宿主：`nightdesk-update-feed.service` 与 `publish.py` / `publish_windows.py`。

### 1.2 GitHub CI 的职责

- **在真 Windows 运行时上跑全部断言**，这是 AI 主机和其他任何 Linux 机器都做不到的。
- 产出可直接下载做人工验收的更新 ZIP（`release.json` + 两个 EXE）。
- Android 单元测试、lint、debug 构建，防止只在 AI 主机通过而 CI 环境不通过的隐性依赖。

### 1.3 本机（当前主机）的职责——仍然不可替代

1. **人工验收的唯一真实环境**：这台机器就是要跑 NightDesk 的 Windows 桌面，真实 GDI 抓屏、Parsec VDD、`SendInput`、DPI/多屏、tailnet 延迟，只有在这里才是真的。
2. **生产被控端**：正式实例运行在本机，任何显示拓扑、VDD 增删、恢复快照的操作只能在这里、且在人工在场时做。
3. **人机接口**：编辑器、浏览器下载 artifact、以及 WSL 里的 agent 会话都在这台机器上。
4. **应急闸门**：CI 排队/失败/断网时，本机 `dotnet run` 一次就能定位是不是环境问题，而不是靠猜。

**已经从本机移走的**：日常 Windows 编译、日常跑 216 断言、日常 Android 构建。这些不再占用本机。

## 2. 克隆与真相源

唯一真相：GitHub `orangepro2001/NightDesk` 的 `main`（公开仓库）。

| 位置 | 路径 | 用途 |
|---|---|---|
| 本机 WSL | `/home/pixel/dev/NightDesk` | 当前工作区、agent 会话 |
| 本机 Windows | `C:\dev\NightDesk` | 应急兜底：本地跑测试、本地起应用 |
| AI 主机 | `/home/tony/dev/NightDesk` | **编译与验证的标准检出** |

规则：

- 改动只经由 `commit → push → pull` 传播。**禁止**在三个克隆之间互相 `cp -r`、`rsync` 源码目录。
- 三处都不允许长期保留未推送的提交；AI 主机上的检出必须始终等于 `origin/main`。
- 核对一致性（任意一处执行，把三处都列出来对比）：

```bash
git -C /home/pixel/dev/NightDesk log -1 --format='%H %s'
git -C /mnt/c/dev/NightDesk    log -1 --format='%H %s'
ssh AI 'git -C /home/tony/dev/NightDesk log -1 --format="%H %s"'
```

三个 SHA 相同才算「都保持在最新版本」。

## 3. 为什么 Windows 验证必须放在 CI / 真 Windows 上

这一节是实测结论，不是推断。在 AI 主机（Linux，dotnet SDK 10.0.402）上：

| 尝试 | 结果 |
|---|---|
| `dotnet build`（`EnableWindowsTargeting`） | 通过，0 warning / 0 error |
| `dotnet publish -r win-x64 --self-contained` | 通过，产出可用 PE32+ 程序 |
| 直接运行测试程序 | 失败：缺少 `Microsoft.WindowsDesktop.App` 框架，**该框架只有 Windows 版本** |
| 改 runtimeconfig 只留 .NET Core + AspNetCore 再跑 | 跑到 **42 条断言**后失败：`DllNotFoundException: crypt32.dll`（`SettingsStore` 的 DPAPI 加密） |
| 交叉执行 `nx-host.exe` | 不可行：集成测试要真正启动这个 Windows PE |

另外：AI 主机 **无 `/dev/kvm`，CPU 无 vmx/svm**，开不了 Windows 虚拟机；`wine` 虽在但没装 .NET Desktop Runtime，WPF 在 wine 上跑通也不代表真机行为。

**结论：不要试图在 Linux 上「绕开」Windows 断言，那是把验证变成假装。正确的做法是让 GitHub 的 `windows-latest` 来跑。** 仓库是公开库，标准 runner 不消耗配额。

## 4. GitHub CI 使用说明

工作流：`.github/workflows/verify.yml`

触发：推送到 `main`、任意 PR、以及 Actions 页面的 *Run workflow*（`workflow_dispatch`）。

### 4.1 `windows` 作业做什么

1. `git config --global core.autocrlf false`——两台开发机都用 `core.autocrlf=input`（工作树 LF），先钉住换行，避免 CI 与本地基线不一致。
2. 按 `global.json` 安装 .NET SDK `10.0.402`。
3. `dotnet build tests/NightDesk.Tests -c Release -warnaserror`——**警告即错误**，对应验收要求的 “0 warnings / 0 errors”。
4. `dotnet run --project tests/NightDesk.Tests -c Release --no-build`——跑全部断言（1.6.4 阶段 216 项）。日志里每条 `PASS:` 和最后的 `N tests passed.` 就是证据。

   2026-10-28 在本仓库首次实跑的结果：`216 tests passed. No display state changed.`，
   紧接的发布步骤打印 `ProductVersion: 1.6.4+<提交号>` 与 `nx-host version: nx-host 0.2.2`。
   **整个 Windows 作业约 84 秒**。
5. `tools/publish-windows.ps1 -Dotnet dotnet`——自包含发布，并**真正执行 `nx-host.exe -version`**（这是 Linux 上做不到的 smoke check）。
6. `tools/package-windows.ps1`——按 csproj 的 `<Version>` 自动算 versionCode，打出更新 ZIP。
7. 上传 artifact `nightdesk-windows-<版本>`。

### 4.2 `android` 作业做什么

JDK 17 + Android SDK 35 + Gradle 8.11.1（仓库没有 wrapper），执行
`testDebugUnitTest lintRelease assembleDebug`，上传单元测试报告与 debug APK。

维护注意：必须用 `android-actions/setup-android@v4`。v3 会执行已被 Google 移除的 legacy
`sdkmanager tools` 包并在 Gradle 之前失败（2026-10-28 实测踩到）。

**不签名 release APK**：签名密钥留在 AI 主机，可安装的 release APK 由 AI 主机产出（见 §7.4）。

### 4.3 CI 明确做不到的事

| 事项 | 原因 | 替代 |
|---|---|---|
| `-live-update` 断言（从线上更新源下载并校验） | runner 在公网，进不了 tailnet | AI 主机或本机执行 |
| 真实显示器 / Parsec VDD / `SendInput` / DPI-UAC | 无真实桌面与会话 | 本机人工验收 |
| Android 仪器测试 | runner 无 KVM，模拟器起不来 | AI 主机 `emulator-5580` |
| 签名发布 APK | 需要密钥 | AI 主机 `android/scripts/build-ai.sh` |

### 4.4 用 CI 结果做人工验收（本机操作）

```bash
# 方式一：有 gh 的机器（AI 主机已认证）
gh run list --repo orangepro2001/NightDesk --workflow verify --limit 5
gh run download --repo orangepro2001/NightDesk -n nightdesk-windows-1.6.4 -D /tmp/nd-ci

# 方式二：浏览器
# https://github.com/orangepro2001/NightDesk/actions  -> 打开最新 run -> Artifacts
```

拿到 ZIP 后解压，得到 `NightDesk.exe`、`nx-host.exe`、`release.json`。**两个 EXE 必须同目录**。替换前按 §8.6 的现场安全前置条件确认。

## 5. 一次性准备

### 5.1 AI 主机依赖（已就位，实测版本）

| 组件 | 版本 / 路径 |
|---|---|
| OS | Ubuntu，kernel 7.0.0-38-generic，i7-8700（12 线程），磁盘余量 1.7T |
| .NET SDK | `10.0.402`（`~/.dotnet`，与 `global.json` 一致） |
| Go | `1.27.3`（`/usr/local/bin/go`） |
| gomobile / gobind | `~/go/bin` |
| JDK | 17（`~/.local/opt/jdk-17`） |
| Gradle | 8.11.1（`~/.local/opt/gradle-8.11.1`，仓库无 wrapper） |
| Android SDK | `~/Android/Sdk`：platform 35、build-tools 35.0.0、NDK 28.2.13676358、emulator 37.2.12 |
| 签名密钥 | `~/.local/share/nightdesk-signing/release.p12`，证书 SHA-256 `41d3a3e3…` |
| 模拟器 | AVD `nightdesk-api35`，序列号 `emulator-5580`，`-accel off`（无 KVM，纯软件模拟，慢但可用） |
| 更新源 | `nightdesk-update-feed.service`（systemd，active） |
| 缺失 | `pwsh`（所以打包用 Python 版，见 §7.2）、`/dev/kvm`、wine 的 .NET Desktop |

### 5.2 AI 主机的检出

`/home/tony/dev/NightDesk` 已按正式克隆建立，等于 `origin/main`。仓库内脚本的默认路径已指向它
（`android/scripts/*.sh` 的 `NIGHTDESK_ANDROID_DIR` 默认值）。旧的 `/home/tony/dev/nightdesk-android`
等临时目录**不再是构建输入**，可忽略或删除。

### 5.3 本机与 CI

- 本机无需安装任何新工具；需要跑本地测试时用已有的 `C:\dev\NightDesk` + Windows 侧 .NET SDK。
- CI 无需任何配置或密钥。

## 6. 日常开发流程（推荐）

```
本机改代码 ──commit/push──> GitHub main ──自动触发──> CI: Windows 216 断言 + 打包
                                   │                      └──> artifact 可下载
                                   └── AI 主机 sync.sh ──> 交叉编译候选包 / Android 全量验证
本机下载 artifact ──> 人工验收 ──> 反馈 / 合并 / 发版
```

1. **改代码**（本机）：编辑、`git diff` 自检。
2. **快速自检**（AI 主机，1–3 分钟）：
   ```bash
   ssh AI 'bash /home/tony/dev/NightDesk/tools/ai/sync.sh'
   ssh AI 'bash /home/tony/dev/NightDesk/tools/ai/verify.sh'
   ```
   `verify.sh` = Go 测试/vet + Windows 交叉构建 + Android 单元/lint/构建。
3. **推送**：`git push` → CI 自动跑 Windows 全量断言并产出 ZIP。
4. **拿候选包**：CI green 后从 artifact 下载，或直接用 AI 主机的 `artifacts/win-x64-<版本>`。
5. **人工验收**（本机，见 §8.6）。
6. **合并 / 发版**（见 §9）。

只在改 Android UI 时，第 2 步可以只跑
`ssh AI 'NIGHTDESK_ANDROID_DIR=/home/tony/dev/NightDesk/android bash /home/tony/dev/NightDesk/android/scripts/build-ai.sh'`，
再把仪器测试跑上（§8.5）。只在改 Go 时，第 2 步只跑 `cd native && go test ./... && go vet ./...`。

## 7. 如何编译

### 7.1 Windows（AI 主机交叉编译，日常用这个）

```bash
ssh AI 'bash /home/tony/dev/NightDesk/tools/ai/build-windows.sh'
```

它做的事：读 `NightDesk.csproj` 的 `<Version>`、算出 versionCode、`dotnet publish -r win-x64
--self-contained -p:EnableWindowsTargeting=true`、校验两个 EXE 都在、调用
`tools/ai/package_windows.py` 打出与 `package-windows.ps1` 同构的 ZIP。

产物：

- `artifacts/win-x64-<版本>/NightDesk.exe`（约 202 MB，单文件自包含）
- `artifacts/win-x64-<版本>/nx-host.exe`（约 7.9 MB，**故意不打进单文件**，必须与主程序同目录）
- `artifacts/windows-packages/nightdesk-windows-<版本>.zip`

原理与边界：`EnableWindowsTargeting` 让 Linux 上的 SDK 下载 Windows 目标包并**编译**，但**不能运行**。
所以这里拿不到 `ProductVersion`、也跑不了 `nx-host.exe -version` 的 smoke check——那两步在 CI 的
`publish-windows.ps1` 里做。

**产物不等价（重要）**：同一提交下，Linux 交叉编译与 Windows 原生编译得到的 `NightDesk.exe` 不是同一个
二进制。1.6.4 实测：Linux 交叉 201,924,801 字节，CI/Windows 原生 201,926,404 字节（后者与当时线上发布的
1.6.4 包内清单完全一致）。两者的 `VS_VERSION_INFO` / `ProductVersion` / `FileVersion` 资源都存在，功能等价，
但字节不同（嵌入的 PDB 路径与构建元数据不同）；Windows 原生两次构建之间同样字节不同。

所以：**不要跨构建比较哈希或文件大小**。正式发布包一律以 CI（真 Windows）产物为准，manifest 里的
`size`/`sha256` 必须由该次实际产物重新计算。

### 7.2 Windows（CI，出正式 artifact）

见 §4.1。CI 与 §7.1 的唯一区别是：CI 在真 Windows 上跑同一个 `publish-windows.ps1`，因此多出
sidecar 可执行性检查；ZIP 由 `package-windows.ps1` 生成。两个打包器都产出同样的三条目 ZIP，任何漂移会在
`publish_windows.py` 校验时暴露。

### 7.3 Windows（本机兜底，应急）

```powershell
cd C:\dev\NightDesk
.\tools\publish-windows.ps1
.\tools\package-windows.ps1 -Version 1.6.4 -VersionCode 10604
```

仅在 CI 不可用、或需要本地调试 Windows-only 失败时使用。

### 7.4 Android

```bash
ssh AI 'bash /home/tony/dev/NightDesk/android/scripts/build-ai.sh'
```

产物：`android/app/build/outputs/apk/release/app-release.apk`（签名 release）与 `debug/app-debug.apk`。
release 签名用 `~/.local/share/nightdesk-signing/release.p12`（PKCS12，别名 `nightdesk`），证书 SHA-256 必须
仍是 `41d3a3e3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`——**换签名会导致已安装设备无法升级**。

### 7.5 Go sidecar（`nx-host.exe`）

```bash
ssh AI 'cd /home/tony/dev/NightDesk/native && go test ./... && go vet ./...'
ssh AI 'cd /home/tony/dev/NightDesk/native && GOOS=windows GOARCH=amd64 go build -trimpath -ldflags="-s -w" -o /tmp/nx-host.exe ./cmd/nx-host'
```

注意：`src/NightDesk/Assets/nx-host/nx-host.exe` **是入库的二进制**，Windows 构建直接用它。只有改了
`native/` 里会影响 host 的代码，才需要重新交叉编译并**提交**这个新二进制；不要悄悄替换。

### 7.6 gomobile AAR（Android 原生核心）

```bash
ssh AI 'cd /home/tony/dev/NightDesk/native && gomobile bind -target=android -androidapi 29 \
  -o /tmp/mobile.aar ./mobile'
```

`android/app/libs/mobile.aar` 同样入库。**只有改了 `native/mobile` 或它依赖的协议/tile/输入代码才需要重建**，
重建后需重跑 §8.4 与 §8.5 全部 Android 验证再提交。AAR 本身不可字节复现（原生 `.so` 编译非确定性），
不要用哈希比对判断是否需要更新。

## 8. 如何测试

### 8.1 测试矩阵

| 测试 | 在哪跑 | 命令 | 期望 | 耗时 |
|---|---|---|---|---|
| Windows 全部断言（1.6.4 为 216 项） | **CI** / 本机兜底 | CI 自动；本机 `dotnet run --project tests/NightDesk.Tests -c Release` | 最后一行 `216 tests passed.` | CI 约 1 分钟 |
| Go 单元 + vet | AI 主机 | `cd native && go test ./... && go vet ./...` | 9 个包 `ok`，vet 无输出 | 约 10 秒 |
| Windows 交叉构建 | AI 主机 | `tools/ai/build-windows.sh` | 两个 EXE + ZIP 与各自 SHA-256 | 约 15 秒 |
| Android 单元测试 | AI / CI | `build-ai.sh` 内含 `testDebugUnitTest` | 46 个测试，0 失败 | 2–7 分钟（含 lint/打包；与模拟器争 CPU 时会接近上限） |
| Android lint | AI / CI | `build-ai.sh` 内含 `lintRelease` | 0 error（当前 22 warning） | 同上 |
| Android 仪器测试 | AI 主机 | `android/scripts/run-instrumented.sh` | 4 个类 6 个测试 OK | 约 1 分钟 |
| Android 仪器测试构建 | AI 主机 | `android/scripts/build-androidtest.sh` | `ANDROIDTEST_OK` | 约 10 秒 |
| 线上更新流程 | AI 主机 | 见 §8.5 的 `UpdateFlowInstrumentedTest` | 从更新源下载并校验版本 | 约 1 秒 |
| 真实桌面行为 | **本机** | 人工 | 见 §8.6 | — |

### 8.2 Windows 断言（最重要的一条）

- 在 CI 日志里搜索 `tests passed.`，数字应当是 216（改动测试后以实际为准），且构建步骤必须 0 warning。
- 本机兜底执行前先 `git pull`，确认与 AI 主机同一提交。
- 该套件是**只读**的：不切换显示拓扑、不抓真实屏幕、不调用 `SendInput`；`nx-host` 以 `-test-mode -source pattern
  -inject log` 启动。仍然要求现场已确认可以安全运行（见 §8.6 的前置条件）。

### 8.3 Go

```bash
ssh AI 'cd /home/tony/dev/NightDesk/native && go test ./... && go test -race ./... && go vet ./...'
```

需要竞态验证时加 `-race`（AI 主机有完整工具链）。

### 8.4 Android 单元测试与 lint

含在 `build-ai.sh` 里。要单独看报告：

```bash
ssh AI 'cat /home/tony/dev/NightDesk/android/app/build/test-results/testDebugUnitTest/*.xml | grep -o "tests=\"[0-9]*\"" | head'
```

lint 报告：`android/app/build/reports/lint-results-release.html`。

### 8.5 Android 仪器测试（模拟器）

```bash
# 1) 确认模拟器在跑
ssh AI '~/Android/Sdk/platform-tools/adb devices'

# 2) 构建 release + androidTest APK
ssh AI 'bash /home/tony/dev/NightDesk/android/scripts/build-ai.sh'
ssh AI 'bash /home/tony/dev/NightDesk/android/scripts/build-androidtest.sh'

# 3) 安装并跑 4 个类
ssh AI 'bash /home/tony/dev/NightDesk/android/scripts/run-instrumented.sh'
```

日志落在 AI 主机 `/home/tony/dev/nightdesk-logs/instrumented.log`，退出码在同目录 `instrumented.exit`。
模拟器是纯软件模拟（无 KVM），冷启动很慢；失败先看进程是否还在：

```bash
ssh AI 'pgrep -af "emulator.*nightdesk-api35" || echo "emulator not running"'
```

`UpdateFlowInstrumentedTest` 需要 tailnet 更新源，脚本已通过 `-e updateOrigin
https://tony-ai-pc-1.tail9f7f1f.ts.net` 传入；**这条不能上 CI**。

### 8.6 人工验收清单（本机）

前置（缺一不可）：确认当前活动物理输出、DPI/VDD 状态、显示快照可恢复。**在确认之前不要切屏、不要恢复快照、
不要增删 VDD。**

1. 解压 CI/AI 产出的 ZIP，确认 `NightDesk.exe` 与 `nx-host.exe` 同目录。
2. 通过应用自身流程退出旧进程，再替换文件（不要强杀、不要覆盖运行中的文件）。
3. 启动，检查「概览 / 显示 / 远程访问 / 控制桌面 / 更新与诊断」五页是否正常。
4. 远控：从另一台设备连接，确认画面、输入、F1 侧栏、断线释放。
5. 更新：确认版本号与 `nx-host.exe -version` 输出符合预期。
6. 记录：版本、SHA-256、观察到的现象、是否重现历史问题（如被控端物理光标闪烁）。
7. 反馈时请附上 CI run 链接或 artifact 名，便于定位到具体提交。

### 8.7 失败排查顺序

| 现象 | 先查 |
|---|---|
| CI windows 作业红 | 日志里第一条 `FAIL:` 或编译 warning；`dotnet` 版本是否被 `global.json` 钉住 |
| CI android 作业红 | `lintRelease` 的 error 项；SDK 35 是否安装成功 |
| AI 主机构建失败 | `git status` 是否脏、`external disk` 余量、`NIGHTDESK_ANDROID_DIR` 指向 |
| 仪器测试卡住 | 模拟器是否存活、`adb devices` 是否 `device`、是否 `-accel off` 太慢 |
| 两侧 SHA 不一致 | 先 `tools/ai/sync.sh`，再核对 §2 的三处 SHA |

## 9. 发布流程

1. AI 主机 `tools/ai/sync.sh` 确认等于 `origin/main` 且工作树干净。
2. CI green（Windows 216 断言 + 打包通过）。
3. 用 CI 产物或 AI 主机产物更新更新源：

```bash
# Windows（校验 + 原子发布，内部会检查 ZIP 三条目与哈希）
ssh AI 'cd /home/tony/dev/nightdesk-update-feed && python3 publish_windows.py \
  --zip <path>/nightdesk-windows-<版本>.zip --version <版本> --version-code <编码> --notes "<说明>"'

# Android
ssh AI 'cd /home/tony/dev/nightdesk-update-feed && python3 publish.py --apk <path>/app-release.apk ...'
```

4. 健康检查：

```bash
ssh AI 'curl -sS https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/healthz'
```

期望 `ok android=ok windows=ok`，再核对 manifest 的 `size` / `sha256` 与实际下载一致。

## 10. 保持最新

```bash
# AI 主机（拒绝脏树，ff-only）
ssh AI 'bash /home/tony/dev/NightDesk/tools/ai/sync.sh'

# 本机 WSL / Windows
git -C /home/pixel/dev/NightDesk pull --ff-only
git -C /mnt/c/dev/NightDesk pull --ff-only
```

三处 SHA 必须一致（见 §2）。**不要**在 AI 主机上直接改代码；那里只接受来自 `main` 的提交。
如果 AI 主机确实有本地改动，`sync.sh` 会拒绝并打印出来——先判断是不是该提交的实验残留。

## 11. 已知限制

| 限制 | 影响 | 应对 |
|---|---|---|
| Linux 上不能运行 Windows 断言 | AI 主机无法给出 Windows 结论 | CI `windows-latest` 为准；本机兜底 |
| AI 主机无 KVM / 无 vmx | 开不了 Windows 虚拟机；Android 模拟器只能纯软件 | 仪器测试在 `emulator-5580` 上跑，接受慢 |
| AI 主机无 `pwsh` | `tools/*.ps1` 不能直接跑 | 打包用 `tools/ai/package_windows.py`；发布脚本是 Python |
| Android / Windows 构建不可字节复现 | 同一提交两次构建 SHA-256 不同（Android 尺寸也可能相同；Windows EXE 交叉编译与原生编译大小差约 1.6 KB） | 用版本号 + `ProductVersion` + 安装行为判定；发布清单必须由当次产物重算 |
| gomobile AAR 不可字节复现 | 同 §7.6 | 只在 Go 侧改动后重建，并重跑全部 Android 验证 |
| `nx-host.exe` / `mobile.aar` 入库 | 二进制与源码可能不同步 | 改 Go 后必须重建并提交，别只改源码 |
| CI 进不了 tailnet | `-live-update`、私网服务不能上 CI | 放在 AI 主机或本机执行 |
| 仓库公开 | 任何提交、artifact 都对外可见 | 密钥、令牌、诊断文件绝不入库（`.gitignore` 已覆盖 `settings.json`、`diagnostics*.json`） |

## 12. 实测记录

在 AI 主机 `tony-ai-pc-1` 上的实测（2026-10-28）：

| 步骤 | 结果 |
|---|---|
| `git clone` 全库 | 11.8 秒，39 MB 工作树 |
| `dotnet build`（Windows 目标，Release） | 8.5 秒，0 warning / 0 error |
| `dotnet publish -r win-x64 --self-contained` | 15.2 秒，`NightDesk.exe` 201,924,801 字节，`nx-host.exe` 7,943,168 字节 |
| `go test ./...` + `go vet ./...` | 9 个包全 `ok`，vet 无输出 |
| Windows 交叉构建 `nx-host.exe` | 1.9 秒 |
| `gomobile bind` | 17.8 秒，AAR 21,900,947 字节 |
| `build-ai.sh`（单元 + lint + debug + release） | 1 分 51 秒；46 单元测试通过；lint 0 error / 22 warning |
| `run-instrumented.sh`（4 类 6 测试） | 全部 OK，含 `UpdateFlowInstrumentedTest` 通过线上更新源校验 |
| release APK | versionCode 13 / 1.3.4，55,112,556 字节，签名证书 SHA-256 与线上一致 |

同一天用 `tools/ai/verify.sh` 一条命令跑完整链路（Go + Windows 交叉构建 + Android）也全绿；其中 Android 阶段
耗时 **7 分 04 秒**——当时 `nightdesk-api35` 模拟器正在运行并占用 6 个核。纯软件模拟的模拟器与 Gradle 抢 CPU 是
这台机器的常态，排期时按 7 分钟估。

### 12.1 GitHub CI 首次实跑（2026-10-28，公开仓库标准 runner）

| 作业 | 结果 |
|---|---|
| Windows build, tests and package | **1 分 09 秒**：`-warnaserror` 构建通过、`216 tests passed. No display state changed.`、自包含发布、更新 ZIP、artifact 上传 |
| Android build, unit tests and lint | **3 分 26 秒**（其中 Gradle 阶段 2 分 46 秒）：单元测试、lint、debug APK |
| 产物对照 | 线上 1.6.4 的 `NightDesk.exe` 201,926,404 字节 / `47b54a4d…`；CI 本次 201,926,404 字节 / `a0e6898f…`；AI 主机交叉编译 201,924,801 字节 / `57e43825…` |

三份产物功能等价且都带完整版本资源，但字节不同——印证 §7.1 的「产物不等价」。

## 13. 变更记录

- 2026-10-28：建立本指南；AI 主机改为正式克隆 `/home/tony/dev/NightDesk`；新增
  `.github/workflows/verify.yml`、`tools/ai/{sync,build-windows,verify}.sh`、`tools/ai/package_windows.py`；
  `android/scripts/*.sh` 默认目录改指正式克隆。
