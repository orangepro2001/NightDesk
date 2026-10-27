# NX 远控侧栏、物理光标调查与双端发布交接

时间：2026-10-27 20:11 KST
仓库：`C:\dev\NightDesk`
分支：`main`
实现提交：`e75618ddb30e9f09ca5730defa74e37de73f76d5`（已推送 `origin/main`）

## 1. 本阶段目标与结果

用户反馈 NX 远控画面上下常驻半透明控件遮挡视线、妨碍交互，并要求同时确认“被控时物理屏幕鼠标指针闪烁”。本阶段完成 Android 与 Windows viewer 的画面布局改造，发布 Windows 1.6.3 / Android 1.3.3，并完成可由源码支持的光标路径审查。

**物理光标闪烁没有在隔离 Windows 主机上复现，原因尚未确定。本轮未隐藏/抑制系统指针，也未把它标记为已修复。**用户选择保持当前行为，等隔离实机复现后再决定是否改动。

## 2. 远控布局与操作方式

### Android

- 远控画面占满可用窗口；默认仅显示窄小边缘入口，点开后显示纵向侧栏，不保留常驻上下工具带。
- 侧栏集中连接状态、手势提示、键盘输入、常用键、右键、释放全部按键、重试/重新配对和断开。
- 有硬件键盘时：F1 开/关侧栏；F2 呼出手机软键盘；Esc 收起侧栏。以上本地命令不会作为普通按键转发给 Windows。
- 侧栏切换会全量释放，避免切换时残留拖动/键盘状态；关闭侧栏后恢复触摸画面控制。

### Windows viewer

- 远端画面使用完整 viewer 区域；空闲时只保留窄入口，侧栏按需展开。
- F1 只切换本机侧栏；“发送 F1 到远端”是独立操作，避免本地快捷键与远端功能冲突。
- 提供状态、全量释放、断开。失焦/关闭路径仍释放输入；侧栏开关也执行释放。

### 验证范围

- Android `RemoteControlUiInstrumentedTest` 验证默认紧凑边缘入口、F1 可发现性、侧栏展开、手势说明、键盘/常用键入口、释放按钮尺寸和触发、执行操作后折叠。
- Windows 断言验证 F1 本地切换、侧栏控件、释放入口、远端 F1 单独发送，并确保 viewer 不再通过固定顶栏/底栏保留额外行。
- Android 和 Windows 均完成对应 Release 构建；这些 UI/模拟器测试不等于真机远控的手感验收。

## 3. 被控端物理光标闪烁：代码事实与结论边界

检查位置：

- `native/internal/capture/capture_windows.go`
- `native/internal/inject/inject_windows.go`
- `tests/NightDesk.Tests/Program.cs` 中 NX capture/input 回归断言

代码路径：

1. `Screen.Grab` 将桌面像素 `BitBlt` 到内存 DC；再用 `GetCursorInfo` 获取当前系统指针、`GetIconInfo` 获取 hotspot，并由 `DrawIconEx` 绘制到同一离屏 memory DC，最后拷贝该 DIB 到远端帧。该处不会把指针图像直接画到物理桌面的 DC。
2. 输入端把远端鼠标坐标转换为绝对桌面坐标，并通过 Win32 `SendInput` 注入。因此被控 Windows 实际会话中的系统鼠标移动是当前设计的预期结果。
3. NX 源码搜索未发现 `ShowCursor`、`SetCursor` 或 `ClipCursor` 调用；当前代码路径没有显式反复隐藏/显示或裁剪系统指针的逻辑。

**能确认的：**NX 会把远端鼠标动作注入实际 Windows 会话；帧内光标合成目标是离屏缓冲区；源码中没有显式光标可见性 toggle。

**不能确认的：**用户所见的物理屏幕“闪烁”具体由何原因引起、是否为系统指针可见性跳变、频繁鼠标移动的呈现、显示器/驱动/光标主题或合成器现象。源码审查和合成输入探针无法替代物理显示器观察。

按用户选择，本轮保持原光标行为。不要为消除“闪烁”直接全局隐藏光标：这会影响本机使用者，也可能影响远端帧中采集到的系统指针。

### 隔离实机复现清单（待执行）

1. 使用独立 Windows 测试机（需活动物理显示器）与 Android 真机或另一台 Windows viewer；不在生产主机运行真实 GDI/`SendInput` 测试。
2. 记录 Windows 版本、GPU/驱动、显示器与刷新率、DPI 缩放、光标主题/尺寸，以及 NX host/viewer 版本。
3. 先观察未连接时的静止指针；再连接但不触摸；之后分别测试低频移动、连续滑动、点击/松开、停止输入和断开。
4. 同时记录物理显示器现象和 viewer 收到的帧，确认闪烁发生在哪一侧、是否与输入频率相关；必要时用外部相机录制物理屏幕，避免抓屏工具遗漏或合成系统指针造成误判。
5. 只有复现并区分原因后，才提出最小修复，并为可见性恢复、异常退出、远端帧指针与本机操作者体验补充回归和恢复验证。

## 4. 双端构建与验证记录

### Windows

- NightDesk **1.6.3 / 10603**；`ProductVersion=1.6.3+e75618ddb30e9f09ca5730defa74e37de73f76d5`。
- sidecar `nx-host 0.2.1` 未变。
- 本地目录：`C:\dev\NightDesk\artifacts\win-x64-1.6.3`。
- ZIP：`C:\dev\NightDesk\artifacts\windows-packages\nightdesk-windows-1.6.3.zip`，包含 `release.json`、`NightDesk.exe`、`nx-host.exe`。
- `NightDesk.exe`：201,926,404 bytes；SHA-256 `aa60d46e87fee8e057c7b8172340642a1959425404f4e873c8c7811ed9ecd526`。
- `nx-host.exe`：7,940,608 bytes；SHA-256 `88233ba806d9a7ebe4bda5c66a2e4c61e0d9dc9b43c48787170ca68e6087b9f0`。
- ZIP：85,184,688 bytes；SHA-256 `3214419d6ed87d95111a1c1c2e719307b8cc046ad624ceeebd09b0310243ebc7`。
- Windows 测试：215 assertions 通过，明确无显示状态改变；Release build 0 warnings / 0 errors。NX safe synthetic probe 为 4 frames、538 tiles、7/7 input receipts；测试模式不调用真实 GDI 或 `SendInput`。

### Android

- Android **1.3.3 / versionCode 12**；包名 `dev.nightdesk.android`，minSdk 29 / targetSdk 35。
- AI 构建目录：`/home/tony/dev/nightdesk-android-ui-20261027/android`。
- 本地 APK：`C:\dev\NightDesk\artifacts\android\nightdesk-1.3.3.apk`。
- APK：55,076,236 bytes；SHA-256 `61f967a0d53d2fdbcf6fde6659404216cb804db847ff801576e22c2da9108e9f`。
- 发布证书 SHA-256 保持 `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`。
- AI：36 JVM tests；Debug/Release/AndroidTest build；lint 0 errors / 23 warnings。6 项常规仪器测试（HostStore 3、MainUI 1、RemoteControl UI 1、UpdateFlow 1）通过；颜色仪器测试此前单独通过，本次 UI 改动未触碰颜色转换。
- AI emulator 最后一次 `adb install` 返回 Success，`dumpsys package` 报 `versionCode=12`；线上 UpdateFlow 确认当前版本已是最新，不应再提示下载更新。

## 5. AI 发布源复核与恢复信息

- 更新服务：`nightdesk-update-feed.service`，目录 `/home/tony/dev/nightdesk-update-feed`，监听 loopback `127.0.0.1:17768`；状态 active，healthz=`ok android=ok windows=ok`。
- Android manifest：`versionCode=12`、versionName `1.3.3`、size `55076236`、SHA-256 `61f967a0d53d2fdbcf6fde6659404216cb804db847ff801576e22c2da9108e9f`。
- Windows manifest：version `1.6.3`、versionCode `10603`、size `85184688`、SHA-256 `3214419d6ed87d95111a1c1c2e719307b8cc046ad624ceeebd09b0310243ebc7`。
- 发布后双端 HTTPS latest 下载 HEAD 为 200，`Content-Length` 与 manifest/本地 artifact 一致。
- 发布前清单备份：`manifest.json.pre-1.3.3-sidebar-20261027`、`windows-manifest.json.bak-20261027-194318`。旧 APK/ZIP 与不可变版本文件保留；不覆盖相同 versionCode/版本名的不可变产物。
- AI Android 构建/模拟器操作只在 SSH 主机 `AI` 执行；可用脚本 `scripts/run-instrumented.sh`，日志 `/home/tony/dev/nightdesk-logs/instrumented.log`，退出码文件 `/home/tony/dev/nightdesk-logs/instrumented.exit`。不要操作无关 tmux `main` 会话。

复验命令：

```powershell
# Windows 源码测试与构建；只编译/运行隔离合成测试，不操作显示拓扑或真实输入
dotnet run --project C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj -c Release
dotnet build C:\dev\NightDesk\src\NightDesk\NightDesk.csproj -c Release --no-restore
```

```bash
# SSH 主机 AI：Android 编译与仪器测试（会更新 emulator 内的应用/测试包）
cd /home/tony/dev/nightdesk-android-ui-20261027/android
NIGHTDESK_ANDROID_DIR="$PWD" bash scripts/build-ai.sh -q \
  -PnightdeskVersionCode=12 -PnightdeskVersionName=1.3.3
NIGHTDESK_ANDROID_DIR="$PWD" NIGHTDESK_UPDATE_ORIGIN=https://tony-ai-pc-1.tail9f7f1f.ts.net \
  bash scripts/run-instrumented.sh
```

若发现发布包需修复：保留本版不可变文件；增加 Windows/Android 版本号和 Android versionCode（需继续使用同一签名证书），先在隔离环境验证，再发布新版本。Android 客户端不接受版本降级；仅回滚旧 manifest 不能把已装 code 12 的设备降回 code 11。

## 6. 当前明确未验收项

- 被控 Windows 物理指针闪烁现象、真实 GDI 帧、真实 `SendInput`、显示器/DPI/多屏组合。
- Android 真机对物理 Windows 主机的 NX 配对、手势、F1/F2 硬件键、长时间稳定性。
- 双机 tailnet UDP/QUIC 实时质量，以及 Windows 生产程序自动更新安装路径。
- 生产桌面安装/替换新版。本轮没有重启正式 Desktop/NX host、变更显示拓扑、操作 VDD 或在本机执行真实鼠标注入。
