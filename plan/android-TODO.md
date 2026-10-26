# Android / 配对 / 更新源交付 TODO

更新时间：2026-10-27（Asia/Seoul）

## 已完成

1. [done] 记录本地 / AI 进程、工具链与拓扑状态；既有 `main` tmux 全程未触碰。
2. [done] 桌面扫码配对：5 分钟单次票据、二维码窗口、`/api/v1/pair`。
3. [done] 每设备永久令牌、DPAPI + 哈希设备注册表、受信任设备列表、删除绑定立即 401。
4. [done] Kotlin 二维码扫描、配置加密、控制源 / 更新源分离。
5. [done] 自动检查 / 下载 / 系统安装器流程、WorkManager 每 6 小时、APK 完整性与签名固定。
6. [done] 更新服务迁到 AI：systemd + loopback 17768 + Tailscale Serve 独立路径 + 原子发布脚本。
7. [done] 仅在 AI 自建 tmux 会话中构建、lint、测试与软件 AVD 调试；本机未安装 Android 工具链。
8. [done] 多主机管理：最多 20 台、按 origin 去重、切换、删除、schemaVersion 3 迁移、Keystore AES-GCM。
9. [done] 仪器测试 `HostStoreInstrumentedTest: OK (3 tests)`，0.778 秒，`emulator-5580`。
10. [done] 发布 1.0.4（versionCode 5，6991035 bytes，SHA-256 `906142e6abda3d193272b7f68adcb053d9cbbdd00fca40423b806e1d2979e436`）。
11. [done] 真实 Tailscale HTTPS 验证下载页、manifest、latest.apk 与哈希。
12. [done] 更新 README、HANDOFF、ACCEPTANCE、计划、findings 与 progress。
13. [done] go-nx 阶段 5 候选：固定证书 tile 查看器、触摸/滚轮/键盘/Unicode 输入、连接状态机、20 秒超时、失败分类、后台释放输入和旧回调隔离。
14. [done] 在 AI 的 `nightdesk-android-stage5` 目录重跑 Release / Debug / AndroidTest 构建；26 项 JVM 单测和 4 项仪器测试通过，哈希与本地候选一致。
15. [done] 记录阶段 5 APK 哈希和 1.4.0 Windows sidecar 发布要求；线上更新源仍保持 1.0.4 / versionCode 5，未误发布候选。

## 仍需人工验收

- 在可恢复的物理显示器和已处理 `recovery.json` 后部署 Windows 1.4.0；必须把 `NightDesk.exe` 与 `nx-host.exe` 放在同一目录，再验收扫码 / 设备管理 / 撤销。
- 物理 Android 真机：扫码、相机权限、控制、锁屏、后台限制、系统安装器，以及 go-nx 真实 tailnet 连接。
- 有活动物理显示器且关闭其它 VDD 管理器后，验证真实 VDD 增删与拓扑切换。
- 第二台 tailnet 设备与 ACL 验收；独立 Windows 测试机验证 GDI 抓屏、SendInput、tile 差分、断线/后台释放输入和防火墙。
- AI 重启后更新源自动恢复。
- 真实端到端完成前，不得把 1.1.0 阶段 5 候选描述为已验收或已发布。
- 在真正缺少 VDD 的干净 Windows 环境验证随包自动安装（本机驱动已安装，只验证了检测/载荷/校验路径）。

## 后续增强（非本次交付阻塞项）

- 主机自定义备注、健康状态徽标、最近成功时间。
- 每主机默认显示模式编辑 API。
- 可选 Quick Settings tile / 小组件。
- target SDK 升级、分发策略、签名托管与隐私政策。
