# Android 移动远控体验 1.3.0 交接与恢复

更新时间：2026-10-27 09:47 KST（Asia/Seoul）

状态：Android 1.3.0（versionCode 9）已构建、测试并发布到 AI 更新源。真实手机连接 Windows 主机后的 GDI/SendInput 仍需单独验收。

## 本阶段修复

旧行为在每次 `ACTION_DOWN` 立即发送左键按下，手指轻微移动也变成 Windows 拖选；如果抬手落在远程画面的黑色留边上，坐标映射返回空值，代码可能跳过 `MouseUp`。远控页面没有用户可见的全量释放按钮。

现在 Android 远控遵循明确的触摸操作：

| 手势 | 远端输入 |
|---|---|
| 轻点 | 抬手时发送一次平衡的左键点击 |
| 单指滑动 | 只移动鼠标；没有 MouseDown，不会意外框选 |
| 系统长按时间后继续移动 | 开始左键拖动；抬手结束拖动 |
| 第二指按下 | 取消尚未完成的轻点；已有拖动先释放，再切到双指滚动 |
| OS 取消、触摸离开、窗口失焦、页面进后台/销毁、传输关闭 | 释放全部鼠标按钮和按键 |
| 底栏“释放” | 立即释放全部输入；拖动中也可用它强制结束 |

底栏提示会在拖动期间改为“正在拖动 · 抬手结束 · 也可点击‘释放’强制松开”。按键组合及右键发送也增加 `finally` 释放兜底。系统状态栏和导航栏 inset 保护了上下工具栏触控区域。

实现位置：

- `android/app/src/main/java/dev/nightdesk/android/NxTouchGesture.kt`：无 Android 依赖的纯 Kotlin 手势 reducer。
- `android/app/src/main/java/dev/nightdesk/android/RemoteTouchControls.kt`：常驻说明和可访问的释放按钮。
- `android/app/src/main/java/dev/nightdesk/android/RemoteActivity.kt`：MotionEvent、长按计时、双指滚动和输入队列适配。
- `android/app/src/test/java/dev/nightdesk/android/RemoteGestureTest.kt`：9 项手势/释放回归测试。
- `android/app/src/androidTest/java/dev/nightdesk/android/RemoteControlUiInstrumentedTest.kt`：检查提示、按钮尺寸和按钮动作，不连接远端主机。
- `android/scripts/run-instrumented.sh`：把远控 UI 测试纳入常规仪器测试。

## 验证结果

- JVM：36 项通过（Core 10、Connection 10、NX 7、Gesture 9）。
- Android lint：0 errors、23 warnings；Release/debug/AndroidTest 构建成功。
- 模拟器仪器测试：6 项通过（HostStore 3、MainUI 1、远控操作栏 1、更新流程 1）。
- 发布后更新验证：模拟器临时安装 1.2.0 / code 8，通过真实 HTTPS 更新源下载 1.3.0，校验 APK 长度、SHA-256 和 Android 签名，再安装回 1.3.0 / code 9。
- AI 更新服务：active；healthz=`ok android=ok windows=ok`。已发布 APK/manifest 的长度、哈希与客户端更新测试一致。
- 未在生产 Windows 桌面执行抓屏、SendInput、按键、显示器拓扑变化或进程重启。

## 产物与分发

| 字段 | 值 |
|---|---|
| 包名 | `dev.nightdesk.android` |
| versionName / versionCode | `1.3.0` / `9` |
| minSdk / targetSdk | `29` / `35` |
| 大小 | `55,075,280` 字节 |
| SHA-256 | `9e278bafb126b18963764fd829414075c30ba9ee795b39d4ad2f1db443d7aa42` |
| 签名证书 SHA-256 | `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767` |
| 本地 APK | `C:\dev\NightDesk\artifacts\android\nightdesk-1.3.0.apk` |
| AI APK | `/home/tony/dev/nightdesk-update-feed/nightdesk-9.apk` |
| AI 版本别名 | `nightdesk-1.3.0.apk` → `nightdesk-9.apk` |
| AI 清单备份 | `/home/tony/dev/nightdesk-update-feed/manifest.json.pre-1.3.0-20261027-0945` |

旧版 `nightdesk-8.apk` 和此前的历史 APK 均保留。更新服务只接受高于已安装版本的更新；把 feed 清单回滚到 code 8 不会让已经安装 code 9 的设备自动降级。

## AI 构建恢复

Android SDK/JDK/Gradle/adb/模拟器只在 SSH 主机 `AI` 上使用。已验证源码副本：`/home/tony/dev/nightdesk-android-1.3.0`。从仓库重新同步时，把 `android/app/src`、Gradle 文件和 `android/scripts` 同步进该目录；旧目录 `/home/tony/dev/nightdesk-android-stage5` 不作为本阶段源码。

```bash
NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-1.3.0 \
  bash /home/tony/dev/nightdesk-android-1.3.0/scripts/build-ai.sh \
  -q -PnightdeskVersionCode=9 -PnightdeskVersionName=1.3.0

NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-1.3.0 \
  bash /home/tony/dev/nightdesk-android-1.3.0/scripts/build-androidtest.sh \
  -q -PnightdeskVersionCode=9 -PnightdeskVersionName=1.3.0

NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-1.3.0 \
NIGHTDESK_UPDATE_ORIGIN=https://tony-ai-pc-1.tail9f7f1f.ts.net \
  bash /home/tony/dev/nightdesk-android-1.3.0/scripts/run-instrumented.sh
```

发布密钥在 `/home/tony/.local/share/nightdesk-signing/`，必须保留同一证书。口令不得进入日志、源码、文档或聊天。只使用已拥有的 `nightdesk-build` 与 `nightdesk-emulator`；禁止读写不相关的 tmux `main` 会话。

不要覆盖同版本 APK。后续发布必须递增 versionCode，并保持签名证书不变。当前线上清单为 code 9（已在 AI 主机复核）；`manifest.json.pre-1.3.0-20261027-0945` 是发布前 code 8 备份，不要在正常状态下恢复它。只有明确需要回滚时，先备份当前 `manifest.json`，将该备份写入临时文件后原子 `mv` 回 `manifest.json`，再检查 `healthz` 和更新清单。不要删除 `nightdesk-9.apk`。

## 仍需真机验收

在隔离的 Windows 测试机和 Android 真机配对后，检查轻点只单击、普通滑动只移动光标、长按拖拽、两指滚动、右键/键盘组合、抬手黑边、系统手势取消、来电/切后台和“释放”按钮。确认 Windows 端没有残留鼠标按钮或修饰键。记录机型、DPI、帧率和输入延迟；不要在生产桌面执行此验收。
