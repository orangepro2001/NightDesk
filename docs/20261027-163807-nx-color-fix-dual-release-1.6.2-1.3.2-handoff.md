# NX 远控颜色修复与双端发布交接

更新时间：2026-10-27 16:38 KST（Asia/Seoul）
仓库：`C:\dev\NightDesk`
分支：`main`（提交 `f4c95c5` 已推送到 `origin/main`）
范围：NX Android / Windows viewer 像素格式修复；Windows 1.6.2 / Android 1.3.2 构建、分发、校验和恢复说明。

## 交付结论

- 已定位并修复 NX Android 画面红蓝通道错位，以及 WPF viewer 将 GDI DIB 未定义 alpha 当透明度的问题。
- Go frame/tile 线上协议保持 BGRA 不变，不需要升级 `nx-host` 或 gomobile AAR。
- Windows 1.6.2 / 10602、Android 1.3.2 / code 11 已在 AI feed 发布。双端 manifest、HTTPS HEAD 长度、完整 GET 大小与 SHA-256 均一致；此前发布的 APK/ZIP 均保留。
- Windows 211 项断言、Release build、真实更新器隔离缓存下载验证和合成 NX probe 通过。Android 36 JVM tests、lint、Release/Debug/AndroidTest build、6 项常规 instrumented tests，以及额外 1 项颜色仪器测试通过；模拟器从 code 10 经线上更新下载并安装 code 11。
- 本轮没有在生产桌面执行真实 GDI/SendInput、替换或重启生产程序、改变显示器拓扑；Android emulator 的结果不等同于物理手机验收。

## 根因与修复

### Android 红蓝通道互换

Go `tile.Frame`、QUIC viewer callback、NX tile decoder 的字节序均为 BGRA。Android `RemoteScreenView.applyTile` 将 BGRA `ByteArray` 原样传给 Android `ARGB_8888` Bitmap 的 raw pixel buffer；AI emulator 的已知颜色回归实测：纯红 BGRA `[0, 0, 255, 0]` 原来被绘制成蓝色，确认是 Android Bitmap buffer 的通道打包约定不同，而不是 Go tile 编码或画面内容反色。

修复在既有 alpha 清理循环中同时做 B/R 互换并将 alpha 设为 255，再把结果直接拷贝到 Bitmap。这样保持单次 per-pixel pass，没有增加第二次整帧读取/写入；协议内部仍为 BGRA。删除原 `ByteOrder.nativeOrder()` 设置，避免把 raw byte copy 误解为 ByteBuffer 多字节数值读写。

新增 `RemoteScreenColorInstrumentedTest`，在 AI Android emulator 用 4×1 的已知像素条覆盖红、绿、蓝和非对称 RGB，并给前三个像素 alpha=0 / 非 255 值，验证 Bitmap 颜色与不透明绘制。修复前第一像素明确失败（期望红、实际蓝），修复后通过。

### WPF 对 GDI alpha 的误解释

Windows host 的 GDI 32-bit DIB 高字节未定义。Android 原有路径已将其清为 opaque，但 WPF viewer 直接以 `PixelFormats.Bgra32` 消费该字节，使垃圾 alpha 参与混合，可能产生变暗/透明/颜色失真。

WPF viewer 改用 `PixelFormats.Bgr32`：保留 B/G/R 字节，并忽略不作为颜色通道的第 4 字节。不改 tile payload、不扫描/复制整帧，也不触碰 GDI 捕获实现。Windows 测试增加像素格式回归断言，旧 `Bgra32` 选择在 Red 阶段失败，改用 `Bgr32` 后通过。

## 代码改动

- `android/app/src/main/java/dev/nightdesk/android/RemoteScreenView.kt`：BGRA → Android Bitmap packed layout 的 B/R 交换；同一循环设置 alpha=255；移除 ByteBuffer byte-order 设置。
- `android/app/src/androidTest/java/dev/nightdesk/android/RemoteScreenColorInstrumentedTest.kt`：新增颜色/alpha 仪器回归测试。
- `src/NightDesk/NxViewerWindow.cs`：viewer bitmap 从 `PixelFormats.Bgra32` 切换到 `PixelFormats.Bgr32`。
- `tests/NightDesk.Tests/Program.cs`：增加 WPF viewer 像素格式静态回归检查；增加可选 `-live-update` 入口，使用隔离临时缓存验证真实 feed 下载与 package staging，不触发安装。
- 版本：`src/NightDesk/NightDesk.csproj` = 1.6.2；`android/app/build.gradle.kts` = 1.3.2 / code 11。
- 没有变更 `native/`、Android `mobile.aar`、Windows sidecar 版本或协议格式。

## 构建与验证

### Windows 本机

工作目录：`C:\dev\NightDesk`

```powershell
dotnet run --project tests\NightDesk.Tests\NightDesk.Tests.csproj -c Release --no-restore
dotnet run --project tests\NightDesk.Tests\NightDesk.Tests.csproj -c Release --no-restore -- -live-update
powershell.exe -NoProfile -File tools\test-nx-probe.ps1
powershell.exe -NoProfile -File tools\publish-windows.ps1
powershell.exe -NoProfile -File tools\package-windows.ps1 -Version 1.6.2 -VersionCode 10602
```

结果：211 assertions passed / `No display state changed.`；Release build 0 warnings / 0 errors；`nx-host 0.2.1` sidecar 冒烟通过；safe NX probe 4 frames、538 tiles、7/7 receipts；release ZIP 恰含 `release.json`、`NightDesk.exe`、`nx-host.exe`，所有 package member SHA-256 匹配。`-live-update` 从真实 HTTPS feed 下载 1.6.2 并校验、stage 两份 EXE 到临时目录，明确没有执行安装。

### AI 主机 Android / Go

- Android 隔离工作目录：`/home/tony/dev/nightdesk-android-1.3.0`。
- Feed：`/home/tony/dev/nightdesk-update-feed`；服务 `nightdesk-update-feed.service`，loopback port 17768。
- Android emulator：`emulator-5580`（AI 主机）。
- Android 构建：`NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-1.3.0 bash /home/tony/dev/nightdesk-android-1.3.0/scripts/build-ai.sh`。
- AndroidTest 构建：`NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-1.3.0 bash /home/tony/dev/nightdesk-android-1.3.0/scripts/build-androidtest.sh`。
- 已在 emulator 安装 Release APK 与 AndroidTest APK；新增颜色类单独通过；既有 `run-instrumented.sh` 的 HostStore(3)、MainUi(1)、RemoteControlUI(1)、UpdateFlow(1) 全部通过。UpdateFlow 参数须提供 `updateOrigin=https://tony-ai-pc-1.tail9f7f1f.ts.net`。
- Android 更新验证：先通过 HTTPS 下载已发布 code 10 到模拟器，再执行 UpdateFlow 从 code 10 检查 code 11、下载并核验长度/SHA/签名，随后从 HTTPS 下载真实 APK 并安装；`dumpsys package` 显示 `versionCode=11`、`versionName=1.3.2`。
- Android: 36 JVM tests；lint 0 errors / 23 warnings；debug/release/AndroidTest build 成功。Android signer fingerprint 保持 `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`。
- Go 在 AI 副本 `/home/tony/dev/nightdesk-cursor-fix/native`：`go test ./...`、`go vet ./...`、`GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go vet ./...` 和 Windows cross-build 成功。本次未改 Go 源码。

## 发布产物与在线位置

| 平台 | 版本 | 大小 | SHA-256 | AI 不可变文件 |
|---|---:|---:|---|---|
| Windows ZIP | 1.6.2 / 10602 | 85,184,082 | `9856a7c03b9dff7b5ed89fbf106a597add1ec983a8f1b43db340fbfd13694717` | `/home/tony/dev/nightdesk-update-feed/windows/nightdesk-windows-1.6.2.zip` |
| Windows EXE | 1.6.2 | 201,922,308 | `cc986ac254941ed6029a46d5ae65978d5233e82904471e1145670edcf067403f` | ZIP 内 `NightDesk.exe` |
| nx-host | 0.2.1 | 7,940,608 | `88233ba806d9a7ebe4bda5c66a2e4c61e0d9dc9b43c48787170ca68e6087b9f0` | ZIP 内 `nx-host.exe` |
| Android APK | 1.3.2 / 11 | 55,076,236 | `1f5fc894074d468f92b6f19f0fbea64be1f7c55c1df696544690d630a0206118` | `/home/tony/dev/nightdesk-update-feed/nightdesk-11.apk` |

- Android version alias `/home/tony/dev/nightdesk-update-feed/nightdesk-1.3.2.apk` 指向 `nightdesk-11.apk`。
- Android manifest 备份：`/home/tony/dev/nightdesk-update-feed/manifest.json.pre-nx-colors-1.3.2-20261027-1625`。
- Windows manifest 发布器自动生成备份：`/home/tony/dev/nightdesk-update-feed/windows-manifest.json.bak-20261027-162723`。
- Local Windows outputs：`C:\dev\NightDesk\artifacts\win-x64-1.6.2`、`C:\dev\NightDesk\artifacts\windows-packages\nightdesk-windows-1.6.2.zip`。
- Local Android APK：`C:\dev\NightDesk\artifacts\android\nightdesk-1.3.2.apk`（忽略文件，不入 Git）。
- 在线 HTTPS：`/nightdesk/api/v1/windows-update` 与 `/api/v1/android-update`；下载 `/nightdesk/windows/latest.zip` 与 `/nightdesk/latest.apk`。
- 发布后两个 manifest 的 GET/HEAD 均为 200；完整 GET 的大小和 SHA-256 与本地、AI immutable file 一致；服务 health=`ok android=ok windows=ok`。

## 复建与恢复

1. 保持已发布 immutable 文件；不要用不同内容覆盖 `nightdesk-11.apk` 或 `nightdesk-windows-1.6.2.zip`。继续发布必须提升 Android versionCode / Windows versionCode 与 version。
2. Android 重新构建仅在 AI 主机操作。复用既有 `build-ai.sh`，注意脚本负责加载持久签名环境；不得复制/输出 `/home/tony/.local/share/nightdesk-signing` 的私钥或密码。
3. Windows 使用既有 `publish-windows.ps1` 和 `package-windows.ps1`，先验证新版本输出路径不存在。部署时必须同目录放置 `NightDesk.exe` 和 `nx-host.exe`。
4. 若下一次发布需回滚 manifest，保留 immutable 文件，并从本交接记录列出的 `manifest.json.pre-*` / `windows-manifest.json.bak-*` 复制恢复。更新 manifest 会影响 tailnet 中客户端发现的版本，不删除任何 APK/ZIP。
5. 这两个包不代表已经更新生产安装。生产 Windows 当前运行程序未替换或重启；Android emulator 只用于验证 code 11。真实升级仍由用户在客户端确认。

## 未完成的现场验收

- Android 真机 + 隔离 Windows host：检查真实红/绿/蓝、灰阶、壁纸/文本、透明 cursor theme、缩放及长时间渲染。
- Windows 独立测试机：确认 GDI DIB 实际色彩、WPF Bgr32 呈现、不同 DPI/HDR/ICC profile 与 secondary monitor。
- 真实双机 tailnet QUIC、Windows `SendInput`、UAC/安全桌面、实际用户确认后的生产自更新仍未验收。
- 本轮没有执行生产 GDI/SendInput、切换拓扑、增删 VDD、强杀或重启生产 NightDesk/nx-host。
