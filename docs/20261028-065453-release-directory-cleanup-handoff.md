# NightDesk 发行目录清理交接

更新时间：2026-10-28 06:54（Asia/Seoul）

范围：本机 `C:\dev\NightDesk\artifacts` 与 SSH 主机 `AI:/home/tony/dev/nightdesk-update-feed`
目的：仅保留当前最新的 Windows / Android 正式分发包；保留测试证据、源码、服务脚本、清单备份和当前版本。

## 1. 当前正式版本

| 平台 | 版本 | 文件 | 大小 | SHA-256 |
|---|---:|---|---:|---|
| Windows | 1.6.3 / 10603 | `nightdesk-windows-1.6.3.zip` | 85,184,688 | `3214419d6ed87d95111a1c1c2e719307b8cc046ad624ceeebd09b0310243ebc7` |
| Android | 1.3.3 / 12 | `nightdesk-12.apk` | 55,076,236 | `61f967a0d53d2fdbcf6fde6659404216cb804db847ff801576e22c2da9108e9f` |

Windows ZIP 内的可执行文件仍为：

- `NightDesk.exe`：201,926,404 字节，SHA-256 `aa60d46e87fee8e057c7b8172340642a1959425404f4e873c8c7811ed9ecd526`
- `nx-host.exe`：7,940,608 字节，SHA-256 `88233ba806d9a7ebe4bda5c66a2e4c61e0d9dc9b43c48787170ca68e6087b9f0`

## 2. 已完成的清理

### 本机

已删除旧的最终发行 APK：

- `artifacts\android\nightdesk-1.0.0.apk`
- `artifacts\android\nightdesk-1.0.1.apk`
- `artifacts\android\nightdesk-1.0.2.apk`
- `artifacts\android\nightdesk-1.0.3.apk`
- `artifacts\android\nightdesk-1.0.4.apk`
- `artifacts\android\nightdesk-1.2.0.apk`
- `artifacts\android\nightdesk-1.3.0.apk`
- `artifacts\android\nightdesk-1.3.1.apk`
- `artifacts\android\nightdesk-1.3.2.apk`

已删除旧 Windows ZIP：

- `artifacts\windows-packages\nightdesk-windows-1.6.0.zip`
- `artifacts\windows-packages\nightdesk-windows-1.6.1.zip`
- `artifacts\windows-packages\nightdesk-windows-1.6.2.zip`
- `artifacts\cursor-fix\nightdesk-windows-1.6.1.zip`（重复副本）

已删除旧的正式发布输出目录：

- `artifacts\win-x64`（ProductVersion 1.0.0）
- `artifacts\win-x64-new`（ProductVersion 1.1.0）
- `artifacts\win-x64-1.2.0`、`win-x64-1.3.0`、`win-x64-1.3.1`
- `artifacts\win-x64-1.4.0`、`win-x64-1.5.0`、`win-x64-1.6.0`、`win-x64-1.6.1`、`win-x64-1.6.2`

本机最终发行产物现在只剩：

- `artifacts\android\nightdesk-1.3.3.apk`
- `artifacts\windows-packages\nightdesk-windows-1.6.3.zip`
- `artifacts\win-x64-1.6.3\`（Windows 1.6.3 发布目录）

保留未删除：`stage/debug/androidTest` APK、截图、XML/日志、`nx-go-tests`、`cursor-fix\live-update-check`、UI 验证目录以及 `win-x64-1.4.0-check*` / `win-x64-1.4.0-precommit-check`。这些是测试或恢复材料，不是正式分发版本。

### AI 更新源

已删除 Android 旧版本文件及版本别名：

- 不可变文件：`nightdesk-2.apk`、`nightdesk-3.apk`、`nightdesk-4.apk`、`nightdesk-5.apk`、`nightdesk-7.apk`、`nightdesk-8.apk`、`nightdesk-9.apk`、`nightdesk-10.apk`、`nightdesk-11.apk`
- 版本别名：`nightdesk-1.0.1.apk`、`nightdesk-1.0.2.apk`、`nightdesk-1.0.3.apk`、`nightdesk-1.0.4.apk`、`nightdesk-1.1.0.apk`、`nightdesk-1.2.0.apk`、`nightdesk-1.3.0.apk`、`nightdesk-1.3.1.apk`、`nightdesk-1.3.2.apk`

已删除 Windows 旧 ZIP：

- `/home/tony/dev/nightdesk-update-feed/windows/nightdesk-windows-1.6.0.zip`
- `/home/tony/dev/nightdesk-update-feed/windows/nightdesk-windows-1.6.1.zip`
- `/home/tony/dev/nightdesk-update-feed/windows/nightdesk-windows-1.6.2.zip`

AI 更新源现在只保留正式分发文件：

- `nightdesk-12.apk`
- `nightdesk-1.3.3.apk -> nightdesk-12.apk`（当前版本兼容别名）
- `windows/nightdesk-windows-1.6.3.zip`

保留未删除：`manifest.json`、`windows-manifest.json`、全部 manifest/server 备份、`server.py`、发布脚本、systemd service 和 `__pycache__`。服务没有重启或改路由。

## 3. 验证结果

- AI `nightdesk-update-feed.service`：`active`。
- AI loopback：`curl http://127.0.0.1:17768/healthz` 返回 `ok android=ok windows=ok`。
- Tailscale Serve：`/nightdesk/healthz` 返回 HTTP 200；根路径 `/healthz` 不属于该 Serve 映射，返回 404 是预期行为。
- 当前 Windows manifest、Android manifest、当前包大小和 SHA-256 全部匹配。
- 当前 HTTPS 下载：Windows latest ZIP、Android latest APK、Android `nightdesk-1.3.3.apk` 均 HTTP 200，HEAD 长度分别为 85,184,688、55,076,236、55,076,236 字节；Windows ZIP 与 Android APK 的完整 GET 均成功。源文件及本地保留包的 SHA-256 均与 manifest 匹配。
- 旧版本定向地址已不再提供：`nightdesk-1.3.2.apk` 与 `nightdesk-windows-1.6.2.zip` 均 HTTP 404。
- 本机按版本命名的正式发行文件只剩 2 个：Android 1.3.3 APK 与 Windows 1.6.3 ZIP。

## 4. 恢复与后续发布

1. 正常更新只使用 `/nightdesk/latest.apk`、`/nightdesk/windows/latest.zip` 及对应 manifest；当前客户端无需改配置。
2. 旧版本 URL 现在返回 404。历史交接文档中的旧路径是历史记录，不代表文件仍存在。
3. `manifest.json.pre-*` / `windows-manifest.json.bak-*` 只保存历史清单元数据，**不包含已删除的 APK/ZIP**；仅恢复清单不能恢复旧二进制。
4. 如需回滚，必须从源码重新构建，或从外部备份取回完整 APK/ZIP，先校验签名、大小和 SHA-256，再使用现有发布脚本以临时文件原子切换清单。不要把旧 manifest 单独切回生产源。
5. 后续发布仍须递增 Android `versionCode` 与 Windows `version` / `versionCode`，保留当前签名证书，并在发布完成后按本文件的清单、SHA-256、healthz、HTTPS HEAD/GET 流程复核。现有发布脚本不会自动删除旧包；新版本验证通过后需再次按正式版本文件名清理旧分发，避免同时保留多版。

## 5. 现场边界

本次只清理本机未跟踪的发行产物和 AI 更新源中的版本文件，没有替换或重启生产 NightDesk，没有切换显示拓扑、操作 VDD、执行真实 GDI 抓屏或 `SendInput`。NX 物理光标闪烁仍按上一份交接文档保持“待隔离实机复现”的结论。
