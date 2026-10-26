# NightDesk progress

更新时间：2026-10-27 08:09 KST（Asia/Seoul）
最新恢复入口：docs/20261027-080919-paginated-ui-desktop-update-1.6.0-handoff.md

## 当前交付状态

- 实现提交：`93147b357a9b8497cd2be4c67d3af99668279724`。
- Windows 1.6.0 / 10600，ZIP 已在 AI 发布；当前生产桌面未被本轮替换。
- 本地 Windows：`C:\dev\NightDesk\artifacts\win-x64-1.6.0`；NightDesk.exe 201922308 字节，SHA-256 `d5c1c09832ad80d638fe27af9e68c41b8ff4e4dd0b5dcb89faabc2559be72e5d`。
- Windows ZIP：85183345 字节，SHA-256 `de162ec69f0f9c9d2e855fc7a084b1c316b4d78284be5b131fd393ec461004cb`。
- nx-host.exe 0.2.0 未改变，7938048 字节，SHA-256 `17b420ce531270e28bef58d42dd68f5c13cac6c5fe4cbfd4fdbb20051ad35e3`。
- Android 1.2.0 / 8，55058900 字节，SHA-256 `82136d2992ff30c3bf84a5d45a02bc04bda4d38203880193883abb747100c52f`，AI 不可变文件 nightdesk-8.apk。
- Android 发布证书保持 `41d3a3e360d8892028d968d5f90bad4541ea7896a36ca08e2560b6330825b767`。

## 本轮完成

- 修复 WPF Grid.Column 缺失导致的文字/输入框重叠。
- Windows 五分页：概览、显示、远程访问、控制桌面、更新与诊断。
- Android 四页底部导航：首页、显示、远控、更新；系统栏 inset、页状态恢复和独立更新反馈。
- Windows 启动/6 小时自动检查并下载 ZIP，校验包和双 EXE；用户确认后等待正常退出再替换，保存备份。
- AI 双平台分发和原子 manifest；Windows 1.6.0、Android 1.2.0 上线。

## 验证

- Windows：210 tests passed. No display state changed. 串行 Release build 0 warnings / 0 errors。
- WPF：纯 XAML 渲染 980/1180 DIP × 5 页通过，NX host/viewer 两组三字段的列和边界互不重叠。
- 安装器：实际 helper 在隔离临时目录等待自有测试进程、替换双合成文件、保留备份、重启合成 WinExe；未安装生产程序。
- 真实 Windows 下载器：临时缓存 + 旧版本比较值，HTTPS 下载当前 ZIP，校验包和双 EXE，解包 ready 成功；未调用安装。
- Android：27 JVM tests，lint 0 errors / 22 warnings；HostStore 3 + MainUi 1 + UpdateFlow 1 = 5 instrumented tests。发布后 UpdateFlow 另跑 1 项通过。
- Feed：5 Python tests；双平台 HTTPS manifest/HEAD 200；本地/AI/线上大小和哈希一致。
- 服务 active / enabled，healthz = ok android=ok windows=ok；其它 Serve 路由和 Funnel 状态未改变。

## 安全与剩余验收

- 2026-10-27 08:07 KST 只读：Desktop NightDesk PID 23116、正式 nx-host PID 20232；100.109.77.29:7000，primary / 10 FPS。
- recovery.json 存在，352 字节，修改时间 07:03:42。PID 会变，不可据此强杀。
- 没有替换 Desktop、没有强杀/重启正式进程、没有切屏/恢复/VDD 操作、没有真实 GDI/SendInput 验收。
- 首次进入 1.6.0 仍须手动部署两个 EXE；先确认物理输出/快照恢复，再从旧版 UI 正常退出。后续更高版本自动下载，安装需确认。
- Windows ZIP 未代码签名；SHA-256/HTTPS/tailnet ACL 不等同于独立签名；helper 不提权。
- 真实双机 tailnet UDP/QUIC、GDI/SendInput、DPI/UAC、安全桌面、Android 物理真机、长期资源占用、生产自更新仍需验收。

## 恢复入口

- AI Android 构建目录：/home/tony/dev/nightdesk-android-1.2.0。
- 服务目录：/home/tony/dev/nightdesk-update-feed。
- 备份：server.py.bak-20261027-080511、manifest.json.pre-1.2.0-20261027-080511。
- 不删除旧 APK/ZIP，不重建覆盖同一已发布版本；修复提高版本号。
