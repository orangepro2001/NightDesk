# Android 鼠标释放坐标修复交接

更新时间：2026-10-27 13:46 KST（Asia/Seoul）

## 交付结论

已复现并修复：Android 触摸移动光标后，点击/拖动结束或滚轮操作会将 Windows 指针送回左上角。根因是 Android Go session 发出的鼠标按下、抬起、滚轮及全量释放消息没有带上位置，坐标因此默认为 `(0,0)`；Windows 注入端对这些消息也按其坐标定位，故操作结束时发生复位。

修复已包含在 Android 1.3.1（versionCode 10）并发布至 AI 更新源。Go 输入核心和 AAR 已推送至 `origin/main`，实现提交：`4a9930bbadb9b58a873307088914881636b71ac3`。Windows 桌面 EXE / nx-host 不需要更新，本修复仅更新 Android 内嵌的 Go 控制端。

**边界：**协议集成回归测试、Android 模拟器构建/更新均通过；尚未在隔离 Windows 测试机 + Android 真机上执行真实 `SendInput` 验收，因此不能将模拟器结果表述为真实鼠标验收。

## 根因与代码改动

- Android 输入由 `android/app/src/main/java/dev/nightdesk/android/RemoteActivity.kt` 经 gomobile AAR 调用 `native/mobile/session.go`，QUIC 消息进入 Windows host 后由 `native/internal/inject/inject_windows.go` 注入。
- `Session.Move(x,y)` 原先发送坐标；鼠标按下、抬起、滚轮及 `ReleaseAll()` 的鼠标抬起消息只带操作类型和按钮/滚轮参数，`X/Y` 为零值。
- Windows 注入函数对鼠标消息使用消息中的绝对坐标。因此松开按键/滚轮消息把指针定位至 `(0,0)`，屏幕坐标上即左上角。
- `Session` 现在线程安全地保存最近一次**成功发送**的鼠标坐标。位置移动、鼠标按钮、滚轮和释放操作使用同一个输入互斥锁；按钮、滚轮以及 `ReleaseAll()` 都发送缓存坐标。移动发送失败时不覆盖已保存位置。
- 回归测试先在未修复代码上失败：`MouseUp` 与 `ReleaseAll` 均收到 `(0,0)`，而测试预期分别为 `(10,20)` 与 `(23,34)`；实现后相关测试通过。
- `native/mobile/session_test.go` 覆盖移动后 MouseDown、MouseUp、Wheel 及释放全部输入时的坐标保留。

## 构建、验证与发布

所有 Go/Android SDK/JDK/Gradle/adb/模拟器工作均在 SSH 主机 `AI` 上执行。

### Go 核心

AI 源码副本：`/home/tony/dev/nightdesk-cursor-fix/native`

- `go test ./...`：通过。
- `go test -race ./mobile`：通过。
- `go vet ./...`：通过。
- `GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go vet ./...`：通过。
- Windows amd64 `nx-host` 编译：通过；本次不修改 host 发布产物。

### AAR 与 Android

- `gomobile bind -target=android/arm,android/arm64,android/386,android/amd64 -androidapi 29` 构建成功。
- 提交的 `android/app/libs/mobile.aar`：21,920,592 字节，SHA-256 `bfa20670742caace05c940fd68fc790b3bf49b6a431d8f2268712936d08f22ad`。
- AI Android 工作目录：`/home/tony/dev/nightdesk-android-1.3.0`；Go AAR 已复制到其 `app/libs/mobile.aar`。
- Android Release/Debug/AndroidTest 构建、36 JVM tests、lint（0 errors）通过。
- 全套仪器测试：6 项通过（HostStore 3、MainUI 1、RemoteControlUI 1、UpdateFlow 1）。
- 发布后更新验证：模拟器从 code 9 安装基线，经线上更新流程下载并校验 code 10 APK，再安装成功；最终 `versionCode=10`、`versionName=1.3.1`。

### 已发布 APK

| 项目 | 值 |
|---|---|
| 包 / 版本 | `dev.nightdesk.android` / `1.3.1` / code `10` |
| minSdk / targetSdk | `29` / `35` |
| 大小 | `55,076,236` 字节 |
| SHA-256 | `11496d15cd997cff45e37637a828548a41c396fdfeb403d948839424eb3f0311` |
| 签名证书 SHA-256 | `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767` |
| 本地产物 | `C:\dev\NightDesk\artifacts\android\nightdesk-1.3.1.apk`（忽略文件，不入 Git） |
| AI 不可变 APK | `/home/tony/dev/nightdesk-update-feed/nightdesk-10.apk` |
| 版本别名 | `/home/tony/dev/nightdesk-update-feed/nightdesk-1.3.1.apk` → `nightdesk-10.apk` |
| 清单备份 | `/home/tony/dev/nightdesk-update-feed/manifest.json.pre-1.3.1-mouse-coordinate-20261027-1346` |

Tailnet HTTPS 更新源的 manifest 为 code 10，`/nightdesk/latest.apk` 与版本化 APK HEAD 均返回 HTTP 200，长度与清单匹配。线上下载文件、AI 不可变 APK 和本地构建产物 SHA-256 一致。更新服务健康状态：`ok android=ok windows=ok`。

## 恢复与复建

1. Windows workspace：`C:\dev\NightDesk`；分支 `main`。核心实现提交 `4a9930bbadb9b58a873307088914881636b71ac3` 已推送；本交接文档和验收索引由后续文档提交补齐。
2. AI Go 验证副本：`/home/tony/dev/nightdesk-cursor-fix/native`。同步仓库 `native/mobile/session.go`、`native/mobile/session_test.go` 后执行 Go 检查。
3. 重新生成 AAR 时，AI 环境设 `JAVA_HOME=/home/tony/.local/opt/jdk-17`、`ANDROID_HOME=/home/tony/Android/Sdk`、`ANDROID_NDK_HOME=$ANDROID_HOME/ndk/28.2.13676358`，并把 `/home/tony/go/bin`、JDK bin 及系统工具目录加入 PATH。输出到临时/新版本文件，先校验再替换仓库 AAR：

   ```bash
   cd /home/tony/dev/nightdesk-cursor-fix/native
   gomobile bind -target=android/arm,android/arm64,android/386,android/amd64 \
     -androidapi 29 -o /tmp/mobile.aar ./mobile
   ```

4. AI Android build path 为 `/home/tony/dev/nightdesk-android-1.3.0`。复制更新后的 AAR 和 `android/app/build.gradle.kts` 后，构建命令：

   ```bash
   NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-1.3.0 \
     bash /home/tony/dev/nightdesk-android-1.3.0/scripts/build-ai.sh \
     -q -PnightdeskVersionCode=10 -PnightdeskVersionName=1.3.1
   ```

5. 签名密钥仍为 `/home/tony/.local/share/nightdesk-signing/release.p12`；不要复制/打印密码或将其写入日志、文档、源码。
6. 发布前检查线上 code 和目标不可变文件不存在，备份现有 manifest，再以 `tools/ai-update-feed/publish.py` 的既有发布器更新 code 10；不要覆盖任何已发布版本。`nightdesk-9.apk` 与发布前 manifest 备份均保留。
7. 不要在生产 Windows 桌面启动真实 GDI/SendInput 测试，也不要改显示器拓扑或重启正式桌面进程。真实远控验收需使用隔离 Windows 机器与 Android 手机。

## 待做真机验收

- [ ] Android 真机连接隔离 Windows host，轻点后确认光标停留在目标位置。
- [ ] 长按拖动后抬手，确认释放左键时光标不跳到左上角、没有残留按键。
- [ ] 右键/中键释放、滚轮、失焦/切后台/断线时全量释放均保持最后位置且不会复位。
- [ ] 检查不同 DPI、次屏/负坐标与屏幕边缘行为。
- [ ] 记录 Android 机型、Windows 版本、DPI、连接方式、输入延迟及视频证据；不得在本机生产桌面上验收真实 SendInput。
