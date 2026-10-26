# NightDesk 远程控制阶段 6 交接：发布包完整性与 1.4.0

更新时间：2026-10-27 03:54:56（Asia/Seoul）
代码基线：<code>40355ae239d98961951ef2ca7de10f5ff1fb253a</code>
工作目录：<code>C:\dev\NightDesk</code>
远端：<code>git@github.com:orangepro2001/NightDesk.git</code>，分支 <code>main</code>

## 1. 本轮结论

- Windows 端和 Android 端的远程控制代码已经完成到当前可自动化验证的范围；阶段 5 的连接生命周期、输入释放和失败恢复均已实现。
- 发现并修复了一个会影响实际部署的发布缺陷：旧发布配置只把 <code>nx-host.exe</code> 放进构建输出，没有放进 single-file publish 输出。部署者只复制 <code>NightDesk.exe</code> 时，WPF 会找不到 sidecar，NX 远控不可用。
- 1.4.0 发布目录现在必须同时包含 <code>NightDesk.exe</code> 与 <code>nx-host.exe</code>。这两个文件是一个整体，不能只复制主程序。
- 最终 Windows 产物已在提交 <code>40355ae</code> 上重新构建；176 项 Release 测试通过，Release 构建 0 警告 / 0 错误。
- Android 阶段 5 候选已在 SSH AI 主机重跑构建、26 项 JVM 单测和 4 项仪器测试；本地候选 APK 与 AI 构建输出哈希一致。
- **真实端到端仍未验收**：本轮没有在独立 Windows 测试机上执行 GDI 首帧/差分、SendInput、真实真机 tailnet 或防火墙验收。当前生产桌面存在未验证的显示恢复快照，不能把软件测试等同于端到端远控验收。

## 2. 现场安全状态与版本事实

### 2.1 当前运行程序

- 进程：PID <b>13816</b>，路径 <code>C:\Users\pixel\Desktop\NightDesk.exe</code>。
- 启动时间：<code>2026-10-27 00:07:24</code>。
- 进程已加载的版本：<b>1.3.1</b>。
- 生产目录文件大小：<code>201766660</code> 字节；SHA-256 与 <code>artifacts\win-x64-1.3.1\NightDesk.exe</code> 完全一致。
- 生产目录中**没有** <code>nx-host.exe</code>，当前也没有运行中的 <code>nx-host</code> 进程。这正说明旧的 1.3.1 单文件发布不能直接提供 NX sidecar。
- 1.4.0 已构建但尚未替换生产程序。

### 2.2 显示恢复门禁

- <code>C:\Users\pixel\AppData\Local\NightDesk\recovery.json</code> 仍存在：<code>352</code> 字节，最后写入 <code>2026-10-27 00:10:30</code>。
- 既有只读诊断显示唯一活动源为 <code>\\.\DISPLAY6</code>（Parsec VDD），没有确认安全的物理输出。
- 在物理显示器可恢复、快照可独立验证之前，禁止：
  - 退出、重启或替换 PID 13816；
  - 执行熄屏、点亮、恢复快照；
  - 添加或删除 VDD；
  - 安装、卸载或替换显示驱动；
  - 在生产桌面启动真实 <code>nx-host.exe</code> 抓屏或 SendInput；
  - 用生产桌面做 NX 端到端测试。
- 不要因为 1.4.0 已构建就把它复制到生产目录；应先处理恢复快照并确认显示安全。

## 3. 根因与修复

### 3.1 根因：sidecar 没有进入 publish 目录

<code>NightDesk.csproj</code> 原来的 sidecar 配置只有：

    CopyToOutputDirectory=PreserveNewest

这只保证 <code>dotnet build</code> 的普通输出目录有文件，不保证 <code>dotnet publish</code> 的发布目录有文件。1.3.1 的 single-file publish 因此只生成了 <code>NightDesk.exe</code>，没有生成 <code>nx-host.exe</code>。

### 3.2 修复

<code>src/NightDesk/NightDesk.csproj</code> 现在将 sidecar 同时配置为：

    CopyToOutputDirectory=PreserveNewest
    CopyToPublishDirectory=PreserveNewest
    ExcludeFromSingleFile=true

<code>ExcludeFromSingleFile</code> 是刻意设置：sidecar 是 WPF 启动的独立进程，必须保持为单独文件，不能塞进主程序单文件包。

### 3.3 发布脚本的防回归措施

新增 <code>tools\publish-windows.ps1</code>。脚本不是简单调用 publish 后检查目录，而是：

1. 读取 csproj 的版本号。
2. publish 到一个全新的临时 staging 目录，避免旧目录里的 sidecar 掩盖“本次没有复制”的回归。
3. 断言临时目录同时存在 <code>NightDesk.exe</code> 和 <code>nx-host.exe</code>。
4. 对 <code>nx-host.exe -version</code> 做冒烟检查。
5. 复制到目标发布目录，再重新计算并打印大小和 SHA-256。
6. 在 finally 中清理已验证的 staging 路径。

因此以后发布应使用脚本，不要只手工执行一条不检查 sidecar 的 publish 命令。

### 3.4 版本断言修复

Windows 测试中的 health 版本断言不再硬编码 <code>1.3.1</code>，而是读取 <code>RemoteServer</code> 程序集版本。这样 csproj 升到 1.4.0 后测试仍然验证真实版本，不会因为版本字符串变化产生假失败。

### 3.5 sidecar 重建

<code>src\NightDesk\Assets\nx-host\nx-host.exe</code> 已通过 SSH AI 主机使用 Go <code>1.27.3</code> 交叉编译。sidecar 自身版本仍为 <code>nx-host 0.1.0</code>，与 WPF 版本独立。

## 4. 最终 Windows 产物

目录：<code>C:\dev\NightDesk\artifacts\win-x64-1.4.0</code>

| 文件 | 大小（字节） | SHA-256 |
|---|---:|---|
| <code>NightDesk.exe</code> | 201832196 | <code>74DE6CF2B134A30AD2290FBB58827BC4FDE75BB7548F3856DFCE0ED04C465D6D</code> |
| <code>nx-host.exe</code> | 7895040 | <code>FFA6491BFC193BA91B40A506EB71239CBE93EA2918C05B6C171659ADB4D4A12C</code> |
| <code>NightDesk.pdb</code> | 79552 | 仅调试符号，不是运行时必需 |

- <code>NightDesk.exe</code> ProductVersion：<code>1.4.0+40355ae239d98961951ef2ca7de10f5ff1fb253a</code>。
- <code>nx-host.exe</code> 版本输出：<code>nx-host 0.1.0</code>。
- 部署时必须把 <code>NightDesk.exe</code> 和 <code>nx-host.exe</code> 放在同一目录。
- 不要把<code>nx-host.exe</code>误认为可选调试工具；缺少它时 WPF 的 NX 远控功能不可用。

## 5. 本轮自动化验证

### 5.1 Windows

执行命令：

    C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe run --project C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj -c Release --no-restore
    C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe build C:\dev\NightDesk\src\NightDesk\NightDesk.csproj -c Release --no-restore
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\dev\NightDesk\tools\publish-windows.ps1

结果：

- <code>176 tests passed. No display state changed.</code>
- Release 构建 0 警告 / 0 错误。
- publish 脚本确认两份文件存在，sidecar 版本检查通过，并输出了上面的 SHA-256。
- 测试没有创建、删除或切换任何显示器，也没有在生产桌面启动抓屏或 SendInput。

### 5.2 Android / AI

验证目录：<code>AI:/home/tony/dev/nightdesk-android-stage5</code>。

执行命令：

    cd /home/tony/dev/nightdesk-android-stage5
    NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-stage5 \
      bash scripts/build-ai.sh -q -PnightdeskVersionCode=7 -PnightdeskVersionName=1.1.0

    NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-stage5 \
      bash scripts/build-androidtest.sh -q -PnightdeskVersionCode=7 -PnightdeskVersionName=1.1.0

    /home/tony/Android/Sdk/platform-tools/adb -s emulator-5580 install -r \
      app/build/outputs/apk/release/app-release.apk
    /home/tony/Android/Sdk/platform-tools/adb -s emulator-5580 install -r \
      app/build/outputs/apk/androidTest/release/app-release-androidTest.apk
    /home/tony/Android/Sdk/platform-tools/adb -s emulator-5580 shell am instrument -w \
      -e updateOrigin https://tony-ai-pc-1.tail9f7f1f.ts.net \
      dev.nightdesk.android.test/androidx.test.runner.AndroidJUnitRunner

结果：

- JVM：<code>CoreTest 9 + NxConnectionTest 10 + NxTest 7 = 26 passed</code>。
- 仪器测试：<code>HostStoreInstrumentedTest: OK (3 tests)</code> 与 <code>UpdateFlowInstrumentedTest: OK (1 test)</code>，合计 4 项。
- <code>emulator-5580</code> 安装和运行通过；未触碰 tmux 的 <code>main</code> 会话。
- Release、Debug、AndroidTest APK 的 AI 输出哈希与本地阶段 5 候选完全一致。

Android 候选产物：

| 文件 | 大小（字节） | SHA-256 |
|---|---:|---|
| <code>nightdesk-1.1.0-stage5.apk</code> | 55058900 | <code>01734a26365b859c831a9ba7806ff1d133b96e0993061053bb2a60ab72caf480</code> |
| <code>nightdesk-1.1.0-stage5-debug.apk</code> | 56886382 | <code>042c9d37dffa86c7507ee8ab358415d46eb921cd55700328e8ef393034fc08aa</code> |
| <code>nightdesk-1.1.0-stage5-androidTest.apk</code> | 689645 | <code>f9d11858d818aaa073f39fc404ea716af5043c2f6093bc0454b8ef52da1b4c36</code> |

### 5.3 更新源状态

- 服务：<code>nightdesk-update-feed.service</code>，active / enabled。
- 线上清单仍为 versionCode 5 / versionName 1.0.4，APK 大小 6991035，SHA-256 <code>906142e6abda3d193272b7f68adcb053d9cbbdd00fca40423b806e1d2979e436</code>。
- 阶段 5 APK 仍然只是候选，**没有发布**到线上更新源。
- 控制面和更新面保持分离；更新请求不带桌面控制令牌。

## 6. 集成架构回顾

### 6.1 Windows

- WPF 主程序负责本地 UI、显示拓扑、Tailscale HTTPS 控制面和进程生命周期。
- <code>nx-host.exe</code> 是独立 Go sidecar，通过 stdin/stdout JSON Lines IPC 与 WPF 通信。
- WPF 使用 Job Object 约束 sidecar；主程序退出或异常终止时清理 sidecar。
- sidecar 负责 GDI 单显示器采集、64×64 tile 差分、QUIC + TLS 1.3、证书固定、配对和 SendInput。
- 这不是 H.264/HEVC/AV1 视频串流，不提供音频、剪贴板、文件传输、多显示器切换或动态码率。
- Tailscale Serve 只承载控制面 HTTPS；QUIC/UDP 数据面必须由 Android 直接通过 tailnet 访问 Windows sidecar，不能假设 Serve 会代理 UDP。

### 6.2 Android

- Kotlin 原生客户端，minSdk 29 / target 35。
- 支持扫码、固定 sidecar 证书、tile 绘制、鼠标/滚轮/键盘/Unicode 输入。
- 阶段 5 的 <code>NxConnectionModel</code> 明确区分 Connecting、Streaming、Failed、Disconnected、Background。
- 20 秒前端截止时间；失败后根据分类提示重试、重新输入配对码或重新扫码。
- 后台、断线、Activity 销毁时释放输入；旧 attempt 回调不会覆盖新连接。
- 不自动重连；恢复动作必须由用户触发。

## 7. 恢复与继续开发

### 7.1 先确认现场

1. 只读确认 PID 13816 仍存在，路径和启动时间未变化。
2. 只读确认 <code>recovery.json</code> 仍存在，并在物理显示器可用后再讨论恢复。
3. 不执行任何会改变显示拓扑的操作；不要用生产桌面启动 sidecar 抓屏。
4. 读取 <code>docs\HANDOFF.md</code> 的现场安全章节，该章节优先于其它旧记录。

### 7.2 Windows 重新构建与发布

    cd C:\dev\NightDesk
    C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe run --project tests\NightDesk.Tests\NightDesk.Tests.csproj -c Release --no-restore
    C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe build src\NightDesk\NightDesk.csproj -c Release --no-restore
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\publish-windows.ps1

发布后必须确认：

    artifacts\win-x64-1.4.0\NightDesk.exe
    artifacts\win-x64-1.4.0\nx-host.exe

这两个文件都在，并且脚本的哈希输出与本文一致，才可进入受控部署。

### 7.3 Android 重新构建

    ssh AI
    cd /home/tony/dev/nightdesk-android-stage5
    NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-stage5 \
      bash scripts/build-ai.sh -q -PnightdeskVersionCode=7 -PnightdeskVersionName=1.1.0
    NIGHTDESK_ANDROID_DIR=/home/tony/dev/nightdesk-android-stage5 \
      bash scripts/build-androidtest.sh -q -PnightdeskVersionCode=7 -PnightdeskVersionName=1.1.0

签名材料位于 <code>/home/tony/.local/share/nightdesk-signing/</code>，口令不得写入源码、文档、日志、截图或聊天。只在已有的 <code>nightdesk-build</code>、<code>nightdesk-emulator</code> 会话工作，禁止触碰 <code>main</code> tmux 会话。

### 7.4 真实端到端验收入口

需要一台独立 Windows 测试机和一台 Android 真机：

1. 在测试机部署同目录的 <code>NightDesk.exe</code> 与 <code>nx-host.exe</code>。
2. 测试机使用可恢复的物理显示器和独立输入环境。
3. Windows 端生成 NX 二维码，Android 真机通过 tailnet 扫码。
4. 验证首帧、tile 差分、鼠标移动/左右键/滚轮、Unicode 输入、组合键。
5. 验证错误配对码、过期二维码、证书指纹变化、断网、后台、强制停止和重新配对。
6. 验证 sidecar 异常退出时 WPF 清除 running/ready 状态，且不残留鼠标按键或键盘修饰键。
7. 记录首帧时间、稳态带宽、CPU、内存和输入回执延迟。
8. 验证防火墙和 tailnet ACL；不要把 Tailscale Funnel 当作数据面代理。

在上述真实测试完成前，只能声称“代码、构建、单测、仪器测试和模拟器冒烟完成”，不能声称“真实远控端到端完成”。

## 8. 当前交付边界

- Windows 1.4.0：构建和自动验证完成，未替换生产 1.3.1。
- Android 1.1.0 阶段 5：候选 APK 构建、自动测试和模拟器冒烟完成，未发布。
- Android 1.0.4：仍是线上版本。
- 当前生产 1.3.1 缺少配套 <code>nx-host.exe</code>，因此不能作为完整 NX 发布包使用。
- 真实 GDI 抓屏、SendInput、真机 tailnet 和防火墙验收仍待硬件条件满足。
- 不启用 Funnel；远程访问边界仍为 tailnet ACL。
- 不把 NX 描述成 Sunshine/Parsec 类的视频串流；它是低带宽 tile 差分数据面。

## 9. 相关文档

- 当前总交接：<code>docs\HANDOFF.md</code>
- 验收清单：<code>docs\ACCEPTANCE.md</code>
- go-nx 可行性分析：<code>docs\20261027-003801-go-nx-remote-control-integration-plan.md</code>
- 阶段 5 连接生命周期：<code>docs\20261027-033356-remote-control-stage-5-lifecycle-handoff.md</code>
- 发布脚本：<code>tools\publish-windows.ps1</code>
