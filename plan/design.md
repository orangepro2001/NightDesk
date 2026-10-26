# NightDesk design

更新时间：2026-10-26（Asia/Seoul）

## Product scope

原生 Windows 工作台，中文 UI，C# / WPF + Win32 API，不使用 Electron。核心目标：把当前控制台会话安全地切到 Parsec 虚拟显示器以供远程控制，同时保留可验证的本地恢复路径。

## Display model

- “熄屏”不是全局广播显示器电源，而是保存当前 DisplayConfig，创建或选择 NightDesk 自有 VDD，确认虚拟输出可用后仅保留该输出。
- “点亮”恢复保存的拓扑并验证原物理源已重新活动；验证成功前不清除恢复快照。
- 程序只删除自己创建的 VDD 索引；第三方虚拟输出不能替代“存在独立活动硬件输出”的退出安全检查。
- 添加/删除虚拟显示器支持分辨率与整数刷新率；VDD 心跳约 100 ms。

## VDD provisioning

- 官方 Parsec VDD 0.45 安装器内嵌在 1.3.1 单文件 EXE 中。
- 启动探测设备接口；缺失时提取并执行 `runas` + `/S`。
- 执行前强制校验 `517256` 字节、SHA-256 `e233...5362` 和签名主体 `Parsec Cloud, Inc.`。
- 每用户仅自动尝试一次；UAC 取消或失败不会反复弹窗。UI 提供手动重试。
- 驱动已安装时只检测，绝不重装或替换。
- 自动联网下载驱动被明确排除；载荷只能来自内嵌资源或程序目录 `vdd\` 的备用文件。

## Security / remote access

- 桌面 API 仅监听 loopback，固定命令 allow-list，不接受 shell/文件/任意桌面控制。
- Tailscale Serve 提供 tailnet-only HTTPS；不启用 Funnel；拒绝覆盖其它服务的已有路由。
- QR 配对票据短期、单次、无长期令牌；设备令牌永久有效直到桌面删除绑定。
- 设备注册表只保存令牌 SHA-256，整文件 DPAPI 加密。
- 远程启用归属按 Serve 路由结构判断，历史指纹仅记录，不作为“从未配对”机器的阻塞条件。

## Android

- Kotlin、minSdk 29 / target 35、Views、ZXing。
- 最多 20 台主机，按 Tailscale origin 去重，支持保存、切换、删除；schemaVersion 3 从旧单主机格式迁移。
- Keystore AES-GCM 加密全部资料；令牌不明文落盘。
- 更新源与控制源独立；更新清单、APK 大小、SHA-256 和签名均校验后交系统安装器。
- 构建、lint、仪器测试和模拟器调试只在 SSH AI 上进行。

## Verification

- Windows 1.3.1：115 项 Release 测试通过，0 警告/0 错误，未改变显示状态。
- Android 1.0.4：多主机仪器测试 3 项通过；更新流两条分支已在 API 35 模拟器验证；线上 feed 已验证。
- 不自动化执行真实显示切换、恢复快照、VDD 增删或驱动安装。

## Deployment constraint

现场仍有 `recovery.json`，活动源只有 Parsec `\\.\DISPLAY6`，没有活动物理输出，运行中 PID 22708 为 1.2.0。1.3.1 只能保留在 artifacts，待物理输出恢复并验证快照后再受控替换。
