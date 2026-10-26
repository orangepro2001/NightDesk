# 多主机管理 + VDD 随包安装 —— 交付记录（2026-10-26）

## 状态

**已完成。** Windows 1.3.1 与 Android 1.0.4 均已构建并通过对应验证。

## 需求

1. Android 支持多台主机（多 Profile）管理：保存、切换、删除。
2. 桌面端在缺少 Parsec VDD 时自动安装驱动；安装包内携带驱动安装器。

## 关键事实（已核实）

- 官方 Parsec VDD 安装器：`https://builds.parsec.app/vdd/parsec-vdd-0.45.0.0.exe`
  - 大小：`517256` 字节
  - SHA-256：`e23332448fdaf5aa017cb308db5ef6855fac526a7ded05d80c039404126d5362`
  - 签名主体：`CN="Parsec Cloud, Inc."`
  - 支持静默安装 `/S`
- GitHub 上的 ParsecVDisplay setup 为未签名文件，不采用。
- 本机驱动已安装（`ROOT\DISPLAY\0001`，oem45.inf），因此本机只验证“检测到已安装”、内嵌载荷、哈希/签名、提取和拒绝路径，不执行卸载/重装。

## Android 实现结果

- `Core.kt`
  - `HostProfile(id, label, profile, addedAt)`、`Hosts(items, activeId)`。
  - `HostsCodec` schemaVersion **3**，兼容 schemaVersion 2 单 Profile 迁移。
  - 最多 20 台主机；按 `controlOrigin` 去重；支持 upsert、setActive、remove。
  - Keystore AES-GCM 加密整个 Hosts JSON。
- `MainActivity.kt`
  - Spinner 选择主机并自动刷新状态。
  - 扫码绑定 / 手动连接会 upsert 为当前主机。
  - “删除当前主机”，删除后回落到第一台剩余主机。
- 仪器测试
  - `HostStoreInstrumentedTest: OK (3 tests)`，`0.778s`，`emulator-5580`。
  - 覆盖多主机持久化与切换、同源令牌替换、明文令牌不落盘。

## 桌面实现结果

- `src/NightDesk/VddInstaller.cs`
  - `IsDriverInstalled()` 通过 `VddClient` 设备接口探测。
  - 载荷优先级：EmbeddedResource -> 程序目录 `vdd\parsec-vdd-0.45.0.0.exe`。
  - 执行前校验大小、SHA-256、签名主体。
  - 提取到 `%LOCALAPPDATA%\NightDesk\driver\`，`runas` + `/S` 安装，等待退出后重新探测。
  - 每用户自动尝试一次；手动按钮可重试。
- `src/NightDesk/NightDesk.csproj`
  - `Assets\vdd\parsec-vdd-0.45.0.0.exe` 作为 `EmbeddedResource`，单文件发布仍内置。
- `MainWindow.xaml(.cs)`
  - 启动检测；缺失时提示并自动安装；失败时可手动重试。
- 测试
  - 内嵌载荷存在、可提取、大小/哈希/签名匹配。
  - 篡改载荷和缺失载荷均被拒绝执行。

## 版本与产物

- Windows：**1.3.1**
  - `C:\dev\NightDesk\artifacts\win-x64-1.3.1\NightDesk.exe`
  - 大小 `201766660`
  - SHA-256 `04AAFCF79470AAE0CE7E2894A2E4177B9A37E220BB750470EA86B98CF394C8F0`
  - 115 项 Release 测试通过，0 警告 / 0 错误。
- Android：**1.0.4 / versionCode 5**
  - `C:\dev\NightDesk\artifacts\android\nightdesk-1.0.4.apk`
  - 大小 `6991035`
  - SHA-256 `906142e6abda3d193272b7f68adcb053d9cbbdd00fca40423b806e1d2979e436`
  - 已发布到 AI 更新源。

## 风险与现场约束

- 运行中进程为 PID **22708** / ProductVersion **1.2.0**；1.3.1 未替换现场程序。
- `recovery.json` 仍存在；唯一活动源为 Parsec `\\.\DISPLAY6`，无活动物理输出。
- 禁止退出/重启桌面、执行显示切换、恢复快照或 VDD 增删。
- 真正“驱动缺失时自动安装”的端到端验收需要干净 Windows 机器/VM；本机不能安全卸载现有驱动。
