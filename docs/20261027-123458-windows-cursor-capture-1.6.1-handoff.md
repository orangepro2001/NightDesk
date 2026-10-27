# Windows / Android 远控光标可见性修复交接

更新时间：2026-10-27 12:47 KST（Asia/Seoul）

## 状态

Windows NightDesk **1.6.1 / versionCode 10601** 与 go-nx host **0.2.1** 已构建、验证并发布到 AI 更新源。Android 客户端保持 1.3.0 / code 9，本次不需要 APK 更新。真实 Windows 桌面输入/抓屏视觉验收仍未做。

## 根因复核

现有 Android 路径把远控 View 的触摸点通过 letterbox 几何映射为 host 像素坐标，手势 reducer 发出 `Move`，并调用 gomobile `Session.move` 发送 `KindMouseMove`。Windows host 通过 `SendInput` 注入绝对鼠标位置。

可确认的断点在返回画面：Windows `Screen.Grab` 仅用 GDI `BitBlt` 抓桌面像素。BitBlt 源图不包含系统鼠标指针；Android `RemoteScreenView` 只绘制 host 下发的 tile，没有自己生成/绘制系统指针。因此输入命令可以到达 host，鼠标在 Windows 上移动，但 Android 显示画面里看不到位置反馈，给人的感觉就是指针不动。这里不是再改 Android 手势或坐标公式能解决的问题。

## 修复

- `native/internal/capture/capture_windows.go`：每次 `BitBlt` 后用 `GetCursorInfo` 获取可见系统指针，用 `GetIconInfo` 读取 hotspot，再以 `DrawIconEx` 合成到捕获的 monitor-local GDI DC。`GetIconInfo` 创建的 mask/color bitmap 在绘制后释放。
- 新的通用 `native/internal/cursor.DrawPosition` 将虚拟桌面 screen position 减去 monitor origin 和 cursor hotspot，适用于负坐标及非主显示器。
- 合成发生在原有 tile diff 之前，指针移动会自然导致相应 tile 变化并通过现有 QUIC 协议发送；协议及 Android 显示端无需改动。
- NightDesk 版本改为 1.6.1，sidecar 版本改为 0.2.1；保留同目录双 EXE 包装和已有“下载后需用户确认安装/重启”的更新策略。

## 验证

- AI Go host：`go test ./...`、`go vet ./...`、`GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go vet ./...` 及 Windows amd64 host/probe 构建通过。
- 纯 Go 几何单测覆盖主屏、次屏、负虚拟桌面 origin 和 hotspot。
- `nx-host 0.2.1` 发布候选通过 test-mode 安全探针：1920×1080 合成源，4 帧、538 tile 更新、7/7 输入回执、动态帧摘要；严格 `-test-mode` 使用 pattern + log，不抓 GDI，不调用 SendInput。
- Windows 桌面客户端测试：210 项通过，无显示拓扑变化；WPF Release publish 成功，sidecar 版本 smoke check 成功。
- AI Android 当前源码重新构建，Android 仪器测试 6 项通过（HostStore 3、MainUI 1、RemoteControlUI 1、UpdateFlow 1）。Android 代码未因本修复变化，设备模拟的通过不代表 Android 真机远控验收。
- AI feed：`healthz=ok android=ok windows=ok`；HTTPS Windows 清单为 1.6.1 / 10601；latest ZIP HEAD 为 HTTP 200，长度 85,184,039，与清单相同。

合成探针只能验证 QUIC 协议和 log injector 回执，不能验证真实系统指针是否出现在 GetCursorInfo/DrawIconEx 输出像素中，也不能验证物理 SendInput 是否移动了鼠标。

## 发布产物

| 文件 | 大小 | SHA-256 |
|---|---:|---|
| `NightDesk.exe` | 201,922,308 | `962dbc32ec341661e6292c68c1331803f8b4475a896d834661ca70e9410fb74a` |
| `nx-host.exe` | 7,940,608 | `88233ba806d9a7ebe4bda5c66a2e4c61e0d9dc9b43c48787170ca68e6087b9f0` |
| Windows 1.6.1 ZIP | 85,184,039 | `ae3de5075dca53b13d55d36409520c08f89fa405bb3d5fbcf1f67e624e64b7ae` |

- 本地发布目录：`C:\dev\NightDesk\artifacts\win-x64-1.6.1`
- 本地更新包：`C:\dev\NightDesk\artifacts\windows-packages\nightdesk-windows-1.6.1.zip`
- AI 不可变更新包：`/home/tony/dev/nightdesk-update-feed/windows/nightdesk-windows-1.6.1.zip`
- Android：维持 1.3.0 / code 9，SHA-256 `9e278bafb126b18963764fd829414075c30ba9ee795b39d4ad2f1db443d7aa42`。未重新发布 APK。

## 构建与恢复

Go/Windows 编译和 Android SDK 工具继续在 SSH 主机 `AI` 使用。本阶段 Go 源码副本位于 `/home/tony/dev/nightdesk-cursor-fix/native`：

```bash
cd /home/tony/dev/nightdesk-cursor-fix/native
gofmt -w internal/cursor/*.go internal/capture/capture_windows.go cmd/nx-host/main_windows.go
go test ./...
go vet ./...
GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go vet ./...
GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -trimpath -ldflags='-s -w' -o nx-host.exe ./cmd/nx-host
```

把生成的 host 放入 `src\NightDesk\Assets\nx-host\nx-host.exe`，然后在 Windows checkout 执行：

```powershell
dotnet run --project tests\NightDesk.Tests\NightDesk.Tests.csproj -c Release
powershell.exe -NoProfile -File tools\test-nx-probe.ps1
powershell.exe -NoProfile -File tools\publish-windows.ps1
powershell.exe -NoProfile -File tools\package-windows.ps1 -Version 1.6.1 -VersionCode 10601
```

不要覆盖或重发同版本不同内容的不可变包。后续修复需提高 Windows 版本号和 versionCode。

## 待隔离测试机人工验收

在独立 Windows 测试机与 Android 真机配对，验证 host 指针是否随轻扫移动、点击/长按拖拽仍正常、次屏和不同 DPI 对齐、窗口/显示器边界、指针主题/缩放及断连恢复。记录设备、DPI、显示器 origin、FPS 和输入/画面延迟。不要在生产桌面执行。生产 NightDesk/nx-host 本轮没有被替换、重启，也没有执行真实输入或显示拓扑操作。
