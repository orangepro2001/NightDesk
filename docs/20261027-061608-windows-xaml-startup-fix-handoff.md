# NightDesk Windows XAML 启动故障修复交接

更新时间：2026-10-27 06:16 KST  
修复提交：`090b5c1ecd7803ba717b97b378c37206ef36fdd1`  
范围：Windows WPF 桌面端启动失败；Android 与 `nx-host.exe` 数据面未改动。

## 1. 现象

启动 Windows 桌面端时只出现错误框：

> 启动失败，未更改显示设置。  
> 向类型为 `System.Windows.Controls.UIElementCollection` 的集合中添加值引发了异常。

该错误发生在 `MainWindow.InitializeComponent()` 加载 XAML 期间，早于设置加载、Tailscale Serve、远程 API、显示拓扑或 NX sidecar 启动，因此不会执行熄屏/点亮或显示恢复操作。

## 2. 根因

`src/NightDesk/MainWindow.xaml` 第 73 行包含字面量 `` `r`n ``：

```xml
<Button x:Name="NxTailscaleButton" .../>`r`n      <Button x:Name="NxPairButton" .../>
```

WPF 把 `` `r`n `` 当作 `WrapPanel.Children` 的文本节点，尝试把非 `UIElement` 内容加入 `UIElementCollection`，于是抛出截图中的异常。

这是误把 PowerShell 的 CRLF 转义文本写进 XAML，并非显示驱动、VDD、Tailscale 或 NX 协议问题。XAML 编译器可以生成 BAML，但运行时实例化窗口才暴露该错误。

## 3. 修复

将字面量替换为真实换行：

```xml
<Button x:Name="NxTailscaleButton" Click="NxTailscale_Click" Content="填入 Tailscale 地址" Style="{StaticResource Secondary}"/>
<Button x:Name="NxPairButton" Click="NxPair_Click" Content="生成配对二维码" Style="{StaticResource Secondary}" IsEnabled="False"/>
```

同时在 `tests/NightDesk.Tests/Program.cs` 增加回归断言：读取 `MainWindow.xaml`，拒绝再次出现字面量 `` `r`n ``。该测试不创建窗口、不改变显示拓扑、不访问网络。

## 4. 验证结果

执行命令：

```powershell
C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe run `
  --project C:\dev\NightDesk\tests\NightDesk.Tests\NightDesk.Tests.csproj `
  -c Release --no-restore

C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe build `
  C:\dev\NightDesk\src\NightDesk\NightDesk.csproj `
  -c Release --no-restore

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File C:\dev\NightDesk\tools\publish-windows.ps1
```

结果：

- Windows Release 测试：**177 项通过**，输出 `177 tests passed. No display state changed.`。
- Release 构建：**0 警告 / 0 错误**。
- 发布脚本：全新 staging 目录、两个 EXE 完整性检查、`nx-host.exe -version` 检查均通过。
- 最终候选启动冒烟：`NightDesk.exe --smoke` 退出码 **0**，耗时约 **10.36 秒**。
- 冒烟结束后没有残留 `NightDesk` 或 `nx-host` 进程。
- 冒烟没有启用真实 `nx-host` 抓屏，没有调用 SendInput，没有执行熄屏、点亮、恢复布局或 VDD 增删。

## 5. 修复后候选产物

目录：`C:\dev\NightDesk\artifacts\win-x64-1.4.0`

| 文件 | 大小 | SHA-256 | 版本 |
|---|---:|---|---|
| `NightDesk.exe` | `201832196` | `C2A7944FE0661350895DA12C3F712F1E40B46AAFFF73738C1B639F7F3138B67C` | `1.4.0+090b5c1ecd7803ba717b97b378c37206ef36fdd1` |
| `nx-host.exe` | `7938048` | `17B420CE531270E28BEF58D42DD68F5C13CAC6C5FE4CBFD4FDDBB20051AD35E3` | `nx-host 0.2.0` |

`nx-host.exe` 哈希与修复前候选一致，说明本次只修改了 WPF 主程序与测试。

## 6. 修复前文件与部署状态

2026-10-27 06:16 KST 检查：

- 没有 `NightDesk` 或 `nx-host` 进程运行。
- `C:\Users\pixel\Desktop\NightDesk.exe` 仍是修复前的 1.4.0 候选：
  - 大小：`201832196` 字节；
  - SHA-256：`F4C448970703BB489E3A762596A59AEA06E688ED39CC505128B01CF4B7864EF2`；
  - ProductVersion：`1.4.0+fff73bb2b823f52f86f8807a9f0a59c6e3283ebd`；
  - 同目录已有 `nx-host.exe`。
- 该 Desktop EXE 会复现本次启动错误，**不得启动或继续部署**。
- `%LOCALAPPDATA%\NightDesk\recovery.json` 当时不存在；这不等同于显示拓扑已经安全。
- 最近一次只读诊断仍只确认 `\\.\DISPLAY6`（Parsec VDD），未确认活动物理输出。

## 7. 受控部署步骤

本机显示环境仍需按 `docs\HANDOFF.md` 的安全门禁处理。具备独立显示恢复能力后：

1. 若仍有 NightDesk 进程，先通过 UI 正常退出；不要强杀。
2. 备份 `C:\Users\pixel\Desktop` 中现有 `NightDesk.exe` 与 `nx-host.exe`。
3. 同时复制修复后目录中的两个文件到同一部署目录，不要只替换主程序。
4. 先运行 `NightDesk.exe --smoke` 或受控人工启动，确认主窗口出现且没有启动错误框。
5. 再执行只读诊断，复核物理输出、DPI、VDD 状态与恢复快照，之后才进行真实远程控制验收。
6. 真实 GDI 抓屏、SendInput、UAC 安全桌面和 Android 真机 tailnet 仍需独立设备验收。

## 8. 回滚

若修复候选启动异常：

1. 不执行显示切换、VDD 增删或驱动操作。
2. 关闭新进程，恢复第 2 步备份的两个 EXE；但不要在已知旧版会启动失败时反复启动旧 EXE。
3. 保留 `%LOCALAPPDATA%\NightDesk\settings.json`、`trusted-devices.dat` 与任何 `recovery.json`。
4. 记录错误框原文、发生时间和当前活动显示源，再重新发布验证。

代码回滚点为修复前提交 `68c2eae91d897e2e58dd9ff9e8711451937cd606`，但该提交包含损坏的 XAML，只适用于取证，不应部署。