# NightDesk 验收记录

更新日期：2026-10-26（Asia/Seoul）  
工作目录：`C:\dev\NightDesk`  
本轮范围：Android 多主机管理、Parsec VDD 随安装包携带并在缺失时自动安装。

## 1. 当前交付状态

| 项目 | 状态 |
|---|---|
| Windows 产物 | `C:\dev\NightDesk\artifacts\win-x64-1.3.1\NightDesk.exe`，ProductVersion `1.3.1` |
| Windows 大小 / SHA-256 | `201766660` 字节 / `04AAFCF79470AAE0CE7E2894A2E4177B9A37E220BB750470EA86B98CF394C8F0` |
| 运行中程序 | PID **22708**，ProductVersion **1.2.0**，`C:\Users\pixel\Desktop\NightDesk.exe`，尚未替换 |
| Android 产物 | `C:\dev\NightDesk\artifacts\android\nightdesk-1.0.4.apk`，versionCode **5** / versionName **1.0.4** |
| Android 大小 / SHA-256 | `6991035` 字节 / `906142e6abda3d193272b7f68adcb053d9cbbdd00fca40423b806e1d2979e436` |
| Android 签名证书 SHA-256 | `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767` |
| AI 更新服务 | `nightdesk-update-feed.service` active / enabled；`/healthz` 返回 `ok` |

### 在线地址（仅 tailnet 内可访问）

- 下载页：`https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk`
- 最新 APK：`https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/latest.apk`
- 更新清单：`https://tony-ai-pc-1.tail9f7f1f.ts.net/api/v1/android-update`
- 桌面控制页：`https://minipc-xoh4t.tail9f7f1f.ts.net/`

未启用 Tailscale Funnel。访问边界由 tailnet ACL 控制。

## 2. 自动化结果（本轮已复核）

| 检查 | 结果 | 说明 |
|---|---|---|
| Windows Release 测试 | **115 项通过** | 输出 `115 tests passed. No display state changed.`；测试未创建、删除或切换显示器 |
| Windows Release 构建 | **通过** | `0 个警告 / 0 个错误` |
| VDD 载荷测试 | **通过** | 确认 EmbeddedResource 存在、可提取、大小与 SHA-256 正确、签名主体为 `Parsec Cloud, Inc.`；篡改与缺失载荷均被拒绝 |
| Windows 只读诊断 | **通过** | `artifacts\diag-1.3.1.json` 报告 `vddPayloadEmbedded=true`、载荷 `517256` 字节、驱动版本 `0.45` |
| Android 仪器测试 | **4 项通过** | `OK (4 tests)`；其中 `HostStoreInstrumentedTest` 3 项覆盖多主机持久化、切换、删除和令牌替换 |
| Android 更新流程仪器测试 | **通过** | 使用 `-e updateOrigin https://tony-ai-pc-1.tail9f7f1f.ts.net` 运行，清单可达、版本/大小/SHA-256/路径校验通过 |
| 线上更新清单 | **通过** | HTTP 200，versionCode `5` / 1.0.4 / `6991035` 字节，SHA-256 与本地 APK 一致 |
| 线上 APK HEAD | **通过** | HTTP 200，`Content-Length: 6991035`，`application/vnd.android.package-archive` |

测试入口是不可用 `dotnet test` 替代的断言程序：

```powershell
$dotnet = 'C:\Users\pixel\scoop\apps\dotnet-sdk\current\dotnet.exe'
& $dotnet run --project 'C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj' -c Release
& $dotnet build 'C:\dev\NightDesk\src\NightDesk\NightDesk.csproj' -c Release --no-restore
```

## 3. Android 多主机管理

### 已验证实现

- 最多保存 **20 台主机**。
- 以规范化后的 Tailscale HTTPS origin 去重；同一主机重新扫码只更新令牌，不新增重复项。
- 支持保存、Spinner 切换、删除当前主机；删除活动主机后自动回落到剩余第一台。
- 配置升级为 `schemaVersion 3`，兼容旧 `schemaVersion 2` 单主机数据迁移。
- 整个主机列表 JSON 使用 Android Keystore **AES-GCM** 加密后落盘；测试确认共享偏好中不存在明文令牌。
- 切换主机后立即刷新状态、显示器和更新相关界面数据；控制源与固定更新源保持分离。

### 仪器测试覆盖

`HostStoreInstrumentedTest` 已在 `emulator-5580` 通过：

1. 两台主机持久化、切换 active、删除后回落。
2. 同一 origin 再次保存时替换令牌且不增加主机数。
3. 令牌不以明文写入 SharedPreferences。

完整 Android 测试运行时若只执行 `am instrument` 而不传入 `updateOrigin`，更新端到端用例会按设计报告 `missing updateOrigin`。正确命令必须带：

```bash
adb -s emulator-5580 shell am instrument -w \
  -e updateOrigin https://tony-ai-pc-1.tail9f7f1f.ts.net \
  dev.nightdesk.android.test/androidx.test.runner.AndroidJUnitRunner
```

### 真机仍需验收

- [ ] 在物理 Android 设备安装 1.0.4，授权相机并扫码绑定两台不同 NightDesk 主机。
- [ ] 在 Spinner 中切换两台主机，确认状态、显示器和控制命令发送到对应主机。
- [ ] 重启 Android，确认两台主机和当前选择仍保留，令牌无需重新输入。
- [ ] 删除非活动主机，确认活动主机不变；删除活动主机，确认回落到另一台并可继续控制。
- [ ] 对同一主机重新扫码，确认令牌更新但不产生重复主机。
- [ ] 验证锁屏、后台限制、Tailscale 断线/重连时的错误提示，且不会误发显示命令。

## 4. Parsec VDD 随包安装

### 实现流程

1. 程序启动时通过 `VddClient` 打开 Parsec VDD 设备接口。
2. 接口可用：视为驱动已安装，**不重装、不替换**。
3. 接口不可用：
   - 从单文件 EXE 的 EmbeddedResource 提取 `parsec-vdd-0.45.0.0.exe`；
   - 找不到内嵌载荷时，回退到程序目录 `vdd\parsec-vdd-0.45.0.0.exe`；
   - 校验大小 `517256` 字节、SHA-256 `e23332448fdaf5aa017cb308db5ef6855fac526a7ded05d80c039404126d5362`、签名主体 `Parsec Cloud, Inc.`；
   - 任一校验失败即拒绝执行；
   - 通过 `runas` 请求 UAC，并使用官方安装器 `/S` 静默安装；
   - 等待安装器退出后重新探测设备接口。
4. 每用户环境自动尝试一次；取消 UAC 或安装失败后不会每次启动重复弹窗，界面按钮可手动重试。
5. 安装失败不阻止程序打开；缺少驱动时仅不能添加或使用 VDD。

### 已验证内容

- [x] 安装器已作为 `EmbeddedResource` 打入 `NightDesk.exe`。
- [x] 单文件发布后仍能提取载荷。
- [x] 大小、SHA-256 和签名主体校验通过。
- [x] 篡改载荷不会执行。
- [x] 本机已安装驱动时走“已安装、不重装”路径。
- [x] UI 缺失驱动时显示安装按钮和手动重试入口。

### 必须补充的端到端验收

当前机器已有 Parsec VDD，且现场依赖其中一个虚拟输出，不能为了测试卸载/重装驱动。因此下列项目必须在干净 Windows 10/11 x64 虚拟机或可回滚测试机上执行：

- [ ] 未安装 Parsec VDD 的干净系统直接运行 1.3.1。
- [ ] 确认 UAC 出现，授权后安装成功，设备管理器中可见 `Parsec Virtual Display Adapter`，无需手工下载安装器。
- [ ] 拒绝 UAC 后程序仍可启动；同一次用户环境再次启动不重复弹窗；点击“安装 Parsec VDD 驱动”可重试。
- [ ] 安装器返回非零或超时后界面给出明确错误，且不进入可用状态。
- [ ] 已安装驱动的系统升级/启动 1.3.1 时不会重装或替换驱动。
- [ ] 在无网络环境运行，确认内嵌安装器仍可使用（安装器自身若需联网组件，需记录实际行为）。

## 5. 当前桌面现场安全状态

- 运行中的仍是 PID **22708 / 1.2.0**，不得强杀、退出或直接替换。
- `%LOCALAPPDATA%\NightDesk\recovery.json` 仍存在。
- 最近只读诊断显示唯一活动源为 `\\.\DISPLAY6`（Parsec VDD），活动物理输出数量为 0。
- 因此本轮**没有**执行 `off` / `on` / `toggle` / `add` / `remove` / `restore`，没有修改显示拓扑、没有卸载/重装 VDD，也没有重启桌面进程。
- 1.3.1 只保留在 artifacts，必须等物理显示恢复、快照可验证后，再按交接文档受控部署。

## 6. 人工验收清单（硬件相关，尚未执行）

在现场操作人确认有活动物理屏幕、且 `recovery.json` 可恢复后再执行。每一步失败都停止并先恢复布局，不要自动重试 `add` 或 `toggle`。

### A. 前置条件

- [ ] 退出 `ParsecVDisplay.exe` 或其它 Parsec VDD 管理器；不要让两个管理器同时运行。
- [ ] Windows 处于本机控制台用户会话，不是 RDP。
- [ ] Windows 显示设置中至少有一个活动物理输出；先运行只读诊断保存基线。
- [ ] `tailscale serve status --json` 已确认无不相关的现有绑定，或已记录其归属；NightDesk 不会覆盖其它绑定。
- [ ] 远程串流软件已准备好，现场人员知道如何从 Windows 显示设置 / Win+P 恢复输出。

### B. 虚拟显示器

- [ ] 输入受驱动支持的分辨率和整数刷新率，例如 `1920 × 1080 @ 60 Hz`，点击“保存默认值”。
- [ ] 点击“添加显示器”，确认列表出现本程序创建的索引和模式；Windows 显示设置能看到新输出。
- [ ] 添加第二个显示器，确认多个本程序拥有的索引都能显示。
- [ ] 删除一个选中的 NightDesk 显示器，确认其它显示器和物理输出仍在。
- [ ] 恢复或删除最后一个 NightDesk 显示器前，确认不会删除最后一个活动输出。

### C. 熄屏 / 点亮与恢复

- [ ] 点击“熄灭物理屏幕”：程序应先保存恢复快照，必要时自动创建虚拟显示器，再仅启用该虚拟源。
- [ ] 从远程串流端确认虚拟画面可用；无信号、待机或背光行为以显示器固件为准。
- [ ] 用界面按钮、托盘菜单和配置后的全局快捷键分别验证点亮路径。
- [ ] 点亮后确认原物理显示源恢复，确认恢复成功后恢复快照被清理。
- [ ] 在切换期间强制关闭或模拟失败只在具备现场恢复能力时测试；确认应用保留 `recovery.json` 并拒绝在未恢复时清理虚拟屏。

### D. 防休眠与快捷键

- [ ] 勾选“保持电脑唤醒”，确认 UI 状态为启用；取消后确认状态释放。
- [ ] 关闭窗口只进入托盘；从托盘选择退出，确认先恢复布局再退出。
- [ ] 将快捷键改为一个未被占用的组合，确认按键可切换；再测试冲突组合，确认原快捷键不会被静默覆盖。

### E. Tailscale HTTPS、扫码绑定与受信任设备

- [ ] 部署 1.3.1 后确认远程地址为当前节点的 `https://<node>.ts.net/`。
- [ ] 点击“扫描二维码绑定”，确认票据 5 分钟内有效且二维码不包含可见令牌。
- [ ] Android 扫码成功；桌面设备列表出现设备名与绑定时间；Android 重启后仍可控制。
- [ ] 同一二维码再次配对失败；超过 5 分钟后配对失败。
- [ ] 选择设备并“删除所选绑定”；该设备下一次请求返回 401，其它设备仍可用。
- [ ] 读取状态、执行 `on` / `off` / `restore` / `prevent-sleep`；确认无令牌、错误令牌、已撤销令牌和未知命令均被拒绝。
- [ ] 点击“关闭远程控制”，确认只有归属指纹仍一致时才删除绑定；配置被外部修改时必须拒绝并提示人工检查。
- [ ] 验收完成后再次检查 `tailscale serve status --json`；不要启用 Funnel。

### F. AI 更新源

- [x] `nightdesk-update-feed.service` active / enabled，仅监听 `127.0.0.1:17768`。
- [x] 真实 Tailscale HTTPS 下载页、manifest、latest.apk 均返回 200，版本与哈希一致。
- [x] 未启用 Funnel，其它 Serve 路由未被删除。
- [ ] 重启 AI 后确认服务自动恢复、URL 仍可用。
- [ ] 发布下一版本后确认 manifest 先保持旧值，APK 可读后再原子切换，客户端不会下载半发布文件。

## 7. 故障恢复

1. 如果点亮失败，保持 NightDesk 运行，使用“恢复保存布局”或远程 `restore`，不要先删除虚拟屏。
2. 如果启动时提示有遗留快照，先恢复保存布局；启动不会悄悄切屏。
3. 如果物理屏幕不显示，用 Windows 显示设置 / Win+P 重新启用物理输出；必要时由现场人员处理接线和驱动。
4. 只有确认恢复成功后，才退出 NightDesk 或删除它创建的虚拟显示器。
5. 如果 Serve 配置被外部程序改动，保留现状并人工核对，不要强行执行 `tailscale serve ... off`。
6. 更新源异常时先查 `systemctl status nightdesk-update-feed` 和 `journalctl`；不要手工删除 manifest 或正在使用的 APK。
7. VDD 安装失败时不要手工替换内嵌载荷；先查看 UAC、安装器退出码和设备管理器状态，再使用界面按钮重试。

## 8. 交付边界

- Android 1.0.4 已实现多主机、扫码绑定、永久设备令牌、独立更新源，并完成远端构建与仪器测试；物理真机联调仍需人工验收。
- Windows 1.3.1 已构建、测试并携带 VDD 安装器，但**尚未替换运行中的 1.2.0**。
- 真正“缺少 VDD 时自动安装”的端到端路径必须在干净 Windows 环境验收；本机只验证了载荷、校验、提取和已安装路径。
- 应用不提供任意 shell、文件访问或通用桌面控制接口。
- “熄屏”是 Windows 显示拓扑切换，不保证每种显示器的面板电源、背光或无信号提示行为。
