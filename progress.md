# NightDesk progress

更新时间：2026-10-26（Asia/Seoul）

## 当前交付状态

### Windows 桌面端

- 版本：**1.3.1**
- 产物：`C:\dev\NightDesk\artifacts\win-x64-1.3.1\NightDesk.exe`
- 大小：`201766660` 字节
- SHA-256：`04AAFCF79470AAE0CE7E2894A2E4177B9A37E220BB750470EA86B98CF394C8F0`
- Release 测试：**115 项通过，未改变显示状态**
- Release 构建：**0 警告 / 0 错误**
- 已实现：熄屏/点亮、Parsec VDD 增删与分辨率/刷新率配置、防休眠、可修改全局快捷键、Tailscale HTTPS 远程控制、扫码配对、永久设备令牌、受信任设备管理、统一程序图标。
- 1.3.1 已把官方 Parsec VDD 安装器作为 EmbeddedResource 打入单文件 EXE。启动检测不到 VDD 时，验证载荷后以 UAC + `/S` 自动安装；每个用户环境自动尝试一次，失败后可从 UI 手动重试。驱动已安装时绝不重装或替换。

### Android 客户端

- 版本：**1.0.4**（versionCode **5**）
- 本地产物：`C:\dev\NightDesk\artifacts\android\nightdesk-1.0.4.apk`
- 大小：`6991035` 字节
- SHA-256：`906142e6abda3d193272b7f68adcb053d9cbbdd00fca40423b806e1d2979e436`
- 签名证书 SHA-256：`41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`
- 已实现多主机：最多保存 20 台主机，可按 Tailscale origin 去重、切换和删除；配置 schemaVersion 3，兼容旧单主机格式迁移；Android Keystore AES-GCM 加密，令牌不以明文落盘。
- 仪器测试：`HostStoreInstrumentedTest` 在 `emulator-5580` 上 **OK (3 tests)**，耗时 `0.778s`；完整仪器套件带 `-e updateOrigin https://tony-ai-pc-1.tail9f7f1f.ts.net` 运行 **OK (4 tests)**。
- 已发布到 AI 更新源：versionCode 5 / 1.0.4；真实 Tailscale HTTPS 清单、下载页和 APK 均已复核。

### AI 更新源

- 主机：SSH alias `AI`
- 服务：`nightdesk-update-feed.service`，`active` / `enabled`
- 监听：`127.0.0.1:17768`
- 健康检查：`http://127.0.0.1:17768/healthz` 返回 `ok`
- Tailnet 地址：
  - 下载页：`https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk`
  - 最新 APK：`https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/latest.apk`
  - 更新清单：`https://tony-ai-pc-1.tail9f7f1f.ts.net/api/v1/android-update`
- 未启用 Funnel，仅 tailnet ACL 可达。

## 现场安全状态（必须保持）

- 当前运行进程：**PID 22708**，ProductVersion **1.2.0**，路径 `C:\Users\pixel\Desktop\NightDesk.exe`。
- 1.3.1 已构建，但**尚未替换现场运行程序**。
- `%LOCALAPPDATA%\NightDesk\recovery.json` 仍存在。
- 最近只读诊断：唯一活动源为 `\\.\DISPLAY6`（Parsec VDD），没有活动物理输出。
- 在物理显示器恢复且恢复快照可安全验证之前，禁止：
  - 退出、重启或替换 PID 22708；
  - 执行熄屏、点亮、恢复快照；
  - 添加或删除 VDD 显示；
  - 卸载或重装显示驱动。
- 本轮只运行了不触碰显示拓扑的 Release 测试和构建，以及只读状态检查。

## 复核命令与结果

```powershell
$dotnet = 'C:\Users\pixel\scoop\apps\dotnet-sdk\current\dotnet.exe'
& $dotnet run --project 'C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj' -c Release --no-restore
& $dotnet build 'C:\dev\NightDesk\src\NightDesk\NightDesk.csproj' -c Release --no-restore
```

- 输出：`115 tests passed. No display state changed.`
- 构建：`0 个警告 / 0 个错误`。
- Windows 1.3.1 哈希与 ProductVersion 已复核。
- 线上 manifest 复核：versionCode 5 / 1.0.4 / 6991035 字节 / SHA-256 与本地一致。
- 下载页 HTTP 200；`latest.apk` HTTP 200，6991035 字节。

## 历史说明

- 1.2.0 的现场进程来自此前的统一图标与远程控制归属修复；该进程保持运行。
- 1.3.0 是 VDD 随包安装的中间构建，最终交付以 1.3.1 为准。
- 更早的 1.0.x / 1.1.0 构建与 Tailscale 验证记录只作为历史证据，当前状态以本文和 `README.md` 为准。

