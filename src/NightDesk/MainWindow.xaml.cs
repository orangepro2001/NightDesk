using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using Forms = System.Windows.Forms;

namespace NightDesk;

public partial class MainWindow : Window
{
    private readonly SettingsStore store;
    private readonly AppSettings settings;
    private readonly DisplayController controller;
    private readonly DeviceRegistry deviceRegistry;
    private readonly RemoteAuthenticator authentication;
    private readonly GlobalHotkey hotkey = new();
    private readonly Forms.NotifyIcon tray;
    private readonly NxHostService nxHost = new();
    private readonly System.Windows.Threading.DispatcherTimer nxTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private RemoteServer? server;
    private TailscaleService? tailscale;
    private string remoteUrl = "";
    private bool exiting;
    private bool operationRunning;
    private bool initialized;
    private string remoteError = "";

    public MainWindow(SettingsStore store, AppSettings settings)
    {
        InitializeComponent();
        this.store = store; this.settings = settings;
        deviceRegistry = new DeviceRegistry(store.LocalDevicesPath);
        authentication = new RemoteAuthenticator(() => store.Token, deviceRegistry);
        controller = new DisplayController(store, settings);
        controller.Changed += UpdateState;
        WidthBox.Text = settings.Width.ToString(); HeightBox.Text = settings.Height.ToString(); RefreshBox.Text = settings.RefreshRate.ToString();
        HotkeyBox.Text = settings.Hotkey;
        NxListenBox.Text = settings.NxHostListen; NxDisplayBox.Text = settings.NxHostDisplay; NxFpsBox.Text = settings.NxHostFps.ToString();
        nxHost.Changed += () => Dispatcher.BeginInvoke(RefreshNxUi);
        nxHost.Log += message => RemoteDiagnostics.Log("nx-host: " + message);
        nxHost.EventReceived += value => Dispatcher.BeginInvoke(() => HandleNxEvent(value));
        nxTimer.Tick += (_, _) => { if (nxHost.PairingCode is null) nxTimer.Stop(); RefreshNxUi(); };
        tray = new Forms.NotifyIcon { Text = "NightDesk · 屏幕与远程工作台", Icon = LoadAppIcon(), Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 NightDesk", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("熄屏 / 点亮", null, async (_, _) => await Dispatcher.InvokeAsync(() => RunAsync(() => WithTopologyAsync(() => controller.SetRemoteModeAsync(!controller.IsRemoteMode)))).Task.Unwrap());
        menu.Items.Add("恢复保存布局", null, async (_, _) => await Dispatcher.InvokeAsync(() => RunAsync(() => WithTopologyAsync(() => { controller.RestoreSaved(); return Task.CompletedTask; }))).Task.Unwrap());
        menu.Items.Add("退出", null, async (_, _) => await Dispatcher.InvokeAsync(ExitAsync).Task.Unwrap());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        SourceInitialized += (_, _) =>
        {
            hotkey.Attach(new WindowInteropHelper(this).Handle);
            hotkey.Pressed += async (_, _) => await RunAsync(() => WithTopologyAsync(() => controller.SetRemoteModeAsync(!controller.IsRemoteMode)));
            if (!hotkey.Register(HotkeySpec.Parse(settings.Hotkey))) SetStatus("全局快捷键注册失败（可能已被占用），请在界面中更改。", true);
        };
        Loaded += async (_, _) => await InitializeAsync();
        Closing += OnClosing;
    }

    private async Task InitializeAsync()
    {
        try
        {
            if (settings.PreventSleep) controller.SetPreventSleep(true);
            if (controller.TryAdoptRemoteState(out string adoptionNote) && adoptionNote.Length > 0) SetStatus(adoptionNote);
            try { tailscale = new TailscaleService(); remoteUrl = await tailscale.GetHttpsUrlAsync(); UrlBox.Text = remoteUrl; }
            catch (Exception ex) { UrlBox.Text = "Tailscale 不可用：" + ex.Message; }
            if (settings.RemoteEnabled)
            {
                // 幂等：接管已有的 NightDesk Serve 路由，或在本机尚未绑定时新建。失败只报告，设置保持原样，下次启动继续尝试。
                try { await EnableRemoteAsync(); }
                catch (Exception ex)
                {
                    remoteError = ex.Message;
                    RemoteDiagnostics.Log("startup enable failed", ex);
                    SetStatus("远程控制自动启用失败：" + ex.Message, true);
                }
            }
            if (!VddInstaller.IsDriverInstalled() && VddInstaller.HasEmbeddedPayload && !VddInstaller.AutoInstallAttempted)
            {
                VddInstaller.MarkAutoInstallAttempted();
                SetStatus("未检测到 Parsec VDD 驱动，正在自动安装随包携带的官方安装器（需要管理员授权）…");
                var installed = await VddInstaller.InstallAsync();
                SetStatus(installed.Message, !installed.Installed);
            }
            if (settings.NxHostEnabled)
            {
                try
                {
                    await StartNxHostCoreAsync();
                    SetStatus("低带宽远控已按设置自动启动。");
                }
                catch (Exception ex)
                {
                    RemoteDiagnostics.Log("nx-host auto start failed", ex);
                    SetStatus("低带宽远控自动启动失败（保留启用设置，下次启动重试）：" + ex.Message, true);
                }
            }
            if (controller.HasRecoveryFile && !controller.IsRemoteMode) SetStatus("检测到上次未恢复的显示布局，请点击“恢复保存布局”。", true);
            initialized = true;
            UpdateState();
        }
        catch (Exception ex) { initialized = true; Report(ex); }
    }

    private void ShowFromTray() { Show(); WindowState = WindowState.Normal; Activate(); }

    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            string? path = Environment.ProcessPath;
            if (path is not null)
            {
                var embedded = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (embedded is not null) return embedded;
            }
        }
        catch { }
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/nightdesk.ico"));
            if (resource is not null) { using (resource.Stream) return new System.Drawing.Icon(resource.Stream); }
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    private void UpdateState()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(UpdateState); return; }
        ModeLabel.Text = controller.IsRemoteMode ? "● 虚拟显示接管" : "● 本地显示";
        ToggleButton.Content = controller.IsRemoteMode ? "点亮物理屏幕 ←" : "熄灭物理屏幕 →";
        SleepBox.IsChecked = controller.PreventSleep;
        int? selection = (DisplayList.SelectedItem as VirtualDisplayInfo)?.Index;
        DisplayList.ItemsSource = controller.Displays;
        if (selection is int index) DisplayList.SelectedItem = controller.Displays.FirstOrDefault(x => x.Index == index);
        if (DisplayList.SelectedItem is null && DisplayList.Items.Count > 0) DisplayList.SelectedIndex = 0;
        RemoteLabel.Text = server is not null ? "已启用 · Tailscale HTTPS + 长期设备令牌" : string.IsNullOrEmpty(remoteError) ? "远程控制未启用" : "远程控制未启用 · " + remoteError;
        RemoteButton.Content = server is not null ? "关闭远程控制" : "启用 HTTPS 远程控制";
        RefreshDriverState();
        DiagnosticsBox.Text = JsonSerializer.Serialize(new
        {
            displays = controller.Status(),
            remote = new { enabled = server is not null, url = remoteUrl, port = RemoteServer.DefaultPort, serveFingerprint = settings.ServeFingerprint, lastError = remoteError, log = RemoteDiagnostics.LogPath },
            nxHost = new { state = nxHost.State.ToString(), running = nxHost.IsRunning, options = nxHost.RunningOptions, ready = nxHost.ReadyEvent, connected = nxHost.ConnectedAddress, trusted = nxHost.Trusted, lastError = nxHost.LastError }
        }, new JsonSerializerOptions { WriteIndented = true });
        RefreshDevices();
        RefreshNxUi();
    }

    private void RefreshDriverState()
    {
        bool installed = VddInstaller.IsDriverInstalled();
        InstallDriverButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        InstallDriverButton.IsEnabled = !installed && !operationRunning;
        InstallDriverButton.Content = VddInstaller.HasEmbeddedPayload ? "安装 Parsec VDD 驱动" : "安装器未随包（放到 vdd 目录）";
        DriverHint.Text = installed
            ? (Process.GetProcessesByName("ParsecVDisplay").Length > 0
                ? "检测到 ParsecVDisplay 正在运行：请先关闭它再添加或切换虚拟屏幕。"
                : "已检测到 Parsec VDD 驱动；仅管理本程序创建的显示器。")
            : "未检测到 Parsec VDD 驱动。点击上方按钮将以管理员权限静默安装随包携带的官方安装器（Parsec Cloud, Inc. 签名，SHA-256 校验通过才会执行）。";
    }

    private async void InstallDriver_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        SetStatus("正在安装 Parsec VDD 驱动（需要管理员授权）…");
        VddInstallResult result = default!;
        await WithTopologyAsync(async () => result = await VddInstaller.InstallAsync());
        SetStatus(result.Message, !result.Installed);
    });

    private void RefreshDevices()
    {
        string? selectedId = (TrustedDeviceList.SelectedItem as TrustedDeviceInfo)?.Id;
        IReadOnlyList<TrustedDeviceInfo> devices = authentication.Devices;
        TrustedDeviceList.ItemsSource = devices;
        if (selectedId is not null) TrustedDeviceList.SelectedItem = devices.FirstOrDefault(x => x.Id == selectedId);
        DeviceCountLabel.Text = devices.Count == 0 ? "尚无已绑定设备" : $"已绑定 {devices.Count} 台设备 · 令牌永久有效，删除绑定后立即失效";
        PairQrButton.IsEnabled = server is not null && remoteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        RevokeDeviceButton.IsEnabled = TrustedDeviceList.SelectedItem is TrustedDeviceInfo;
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(error ? "#E9B394" : "#ACBCAF")!;
    }
    private void Report(Exception ex) { SetStatus(ex.Message, true); }
    private async Task RunAsync(Func<Task> operation)
    {
        if (operationRunning || !initialized) { SetStatus("正在初始化或处理其它操作，请稍后重试。", true); return; }
        operationRunning = true;
        try { SetStatus("正在处理…"); await operation(); SetStatus("操作完成。"); }
        catch (Exception ex) { Report(ex); }
        finally { operationRunning = false; UpdateState(); }
    }
    private DisplayMode ReadMode()
    {
        if (!int.TryParse(WidthBox.Text, out int width) || !int.TryParse(HeightBox.Text, out int height) || !int.TryParse(RefreshBox.Text, out int rate))
            throw new ArgumentException("宽度、高度和刷新率必须为整数。");
        return new DisplayMode(width, height, rate).Validate();
    }
    private void SaveMode()
    {
        var mode = ReadMode(); settings.Width = mode.Width; settings.Height = mode.Height; settings.RefreshRate = mode.RefreshRate; store.Save(settings);
    }
    private async void Toggle_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => { if (!controller.IsRemoteMode) SaveMode(); await WithTopologyAsync(() => controller.SetRemoteModeAsync(!controller.IsRemoteMode)); });
    private async void Add_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => { var mode = ReadMode(); SaveMode(); await WithTopologyAsync(() => controller.AddAsync(mode)); });
    private async void Remove_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => { if (DisplayList.SelectedItem is not VirtualDisplayInfo item) throw new ArgumentException("请选择本程序创建的显示器。"); await WithTopologyAsync(() => controller.RemoveAsync(item.Index)); });
    private async void Restore_Click(object sender, RoutedEventArgs e) => await RunAsync(() => WithTopologyAsync(() => { controller.RestoreSaved(); return Task.CompletedTask; }));
    private void Refresh_Click(object sender, RoutedEventArgs e) { try { UpdateState(); SetStatus("状态已刷新。"); } catch (Exception ex) { Report(ex); } }
    private void SaveMode_Click(object sender, RoutedEventArgs e) { try { SaveMode(); SetStatus("默认显示模式已保存。"); } catch (Exception ex) { Report(ex); } }
    private void Sleep_Click(object sender, RoutedEventArgs e) { try { controller.SetPreventSleep(SleepBox.IsChecked == true); SetStatus("防休眠状态已更新。"); } catch (Exception ex) { Report(ex); UpdateState(); } }
    private void Hotkey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var spec = HotkeySpec.Parse(HotkeyBox.Text); var old = HotkeySpec.Parse(settings.Hotkey);
            if (!hotkey.Register(spec)) { _ = hotkey.Register(old); throw new InvalidOperationException("快捷键已被其它程序占用，原快捷键已尝试恢复。"); }
            settings.Hotkey = spec.Text; HotkeyBox.Text = spec.Text; store.Save(settings); SetStatus("快捷键已更新：" + spec.Text);
        }
        catch (Exception ex) { Report(ex); }
    }
    private void CopyToken_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(store.Token); SetStatus("令牌已复制。请只粘贴到可信的远程面板，勿转发。"); }
        catch (Exception ex) { Report(ex); }
    }
    private void RotateToken_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "重置令牌会立即断开现有远程客户端。继续？", "重置访问令牌", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { store.RotateToken(settings); SetStatus("兼容令牌已重置；已绑定设备不受影响，可在设备列表中单独删除。"); } catch (Exception ex) { Report(ex); }
    }
    private async Task StartApiAsync()
    {
        if (server is not null) return;
        // 快速重启时旧实例的监听可能尚未释放：短暂重试，避免把“正在退出”误判成端口冲突。
        Exception? last = null;
        for (int attempt = 1; attempt <= 8; attempt++)
        {
            var next = new RemoteServer(RemoteServer.DefaultPort, authentication, () => Dispatcher.Invoke(controller.Status), command => Dispatcher.InvokeAsync(() => ExecuteRemoteAsync(command)).Task.Unwrap());
            try { await next.StartAsync(); server = next; return; }
            catch (RemotePortInUseException ex)
            {
                last = ex;
                await next.DisposeAsync();
                if (attempt < 8) await Task.Delay(400);
            }
            catch (Exception ex) { RemoteDiagnostics.Log("bind failed", ex); throw; }
        }
        // 只有旧版本的 NightDesk 残留监听才会让健康检查/旧版页面识别成 NightDesk，据此给出可执行的处置提示。
        bool stale = await RemoteServer.IsNightDeskListeningAsync(RemoteServer.DefaultPort);
        RemoteDiagnostics.Log(stale ? "bind failed: stale NightDesk listener" : "bind failed: port owned by another program", last!);
        throw new InvalidOperationException(stale
            ? $"本地端口 {RemoteServer.DefaultPort} 仍被一个旧版 NightDesk 监听占用（旧版本启用失败后残留的监听）。请从系统托盘选择“退出 NightDesk”，重新启动后再启用远程控制。"
            : $"本地端口 {RemoteServer.DefaultPort} 已被其它程序占用，远程 API 无法启动；请释放该端口后重试。", last);
    }    private async void PairQr_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (server is null) await EnableRemoteAsync();
        if (string.IsNullOrWhiteSpace(remoteUrl) || !remoteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("尚未取得本机 Tailscale HTTPS 地址。");
        PairingQr pairing = authentication.IssuePairing(remoteUrl);
        new PairingWindow(pairing, remoteUrl, this).ShowDialog();
        RefreshDevices();
        SetStatus("二维码已关闭；扫码成功后设备会出现在受信任设备列表中。");
    });
    private void RefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        try { RefreshDevices(); SetStatus("受信任设备列表已刷新。"); }
        catch (Exception ex) { Report(ex); }
    }
    private void DeviceList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RevokeDeviceButton.IsEnabled = TrustedDeviceList.SelectedItem is TrustedDeviceInfo;
    }
    private void RevokeDevice_Click(object sender, RoutedEventArgs e)
    {
        if (TrustedDeviceList.SelectedItem is not TrustedDeviceInfo device) { SetStatus("请选择要删除绑定的设备。", true); return; }
        if (MessageBox.Show(this, $"删除“{device.Name}”的绑定？该设备会立即失去远程控制权限。", "删除受信任设备",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            if (!authentication.RevokeDevice(device.Id)) throw new InvalidOperationException("设备绑定已不存在。");
            RefreshDevices();
            SetStatus($"已删除“{device.Name}”的绑定；后续请求将返回 401。");
        }
        catch (Exception ex) { Report(ex); }
    }
    private async Task<object> ExecuteRemoteAsync(RemoteCommand command)
    {
        if (operationRunning || !initialized) throw new InvalidOperationException("应用正在处理其它操作，请稍后重试。");
        operationRunning = true;
        try
        {
            switch (command.Name)
            {
                case "off": await WithTopologyAsync(() => controller.SetRemoteModeAsync(true)); break;
                case "on": await WithTopologyAsync(() => controller.SetRemoteModeAsync(false)); break;
                case "toggle": await WithTopologyAsync(() => controller.SetRemoteModeAsync(!controller.IsRemoteMode)); break;
                case "add": await WithTopologyAsync(() => controller.AddAsync(new DisplayMode(command.Width!.Value, command.Height!.Value, command.RefreshRate!.Value))); break;
                case "remove": await WithTopologyAsync(() => controller.RemoveAsync(command.Index!.Value)); break;
                case "prevent-sleep": controller.SetPreventSleep(command.Enabled!.Value); break;
                case "restore": await WithTopologyAsync(() => { controller.RestoreSaved(); return Task.CompletedTask; }); break;
                default: throw new ArgumentException("未知命令。");
            }
            return new { ok = true, message = "操作已完成。", status = controller.Status() };
        }
        finally { operationRunning = false; UpdateState(); }
    }
    private async void Remote_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (server is not null) { await DisableRemoteAsync(); return; }
        await EnableRemoteAsync();
    });

    /// <summary>启用远程控制：先保证本地 API 监听，再绑定 Serve；Serve 失败时回滚监听，绝不留半启动状态。</summary>
    private async Task EnableRemoteAsync()
    {
        if (server is not null) return;
        tailscale ??= new TailscaleService();
        remoteUrl = await tailscale.GetHttpsUrlAsync(); UrlBox.Text = remoteUrl;
        await StartApiAsync();
        ServeBinding binding;
        try { binding = await tailscale.EnsureServeAsync(); }
        catch (Exception ex)
        {
            await StopApiAsync();
            RemoteDiagnostics.Log("serve binding failed", ex);
            throw;
        }
        settings.ServeFingerprint = binding.Fingerprint; settings.RemoteEnabled = true; store.Save(settings);
        remoteError = "";
        RemoteDiagnostics.Log($"remote enabled url={remoteUrl} port={RemoteServer.DefaultPort} createdRoute={binding.Created} fingerprint={binding.Fingerprint[..12]}");
    }

    private async Task DisableRemoteAsync()
    {
        if (server is null) return;
        if (tailscale is null) throw new InvalidOperationException("无法核查 Tailscale Serve 归属，远程监听保持运行。");
        await tailscale.DisableAsync();
        await StopApiAsync();
        settings.RemoteEnabled = false; settings.ServeFingerprint = ""; store.Save(settings);
        remoteError = "";
        RemoteDiagnostics.Log("remote disabled by user");
    }

    private async Task StopApiAsync()
    {
        if (server is null) return;
        var current = server; server = null;
        await current.DisposeAsync();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true; Hide();
        tray.ShowBalloonTip(1800, "NightDesk", "已驻留系统托盘，快捷键和已启用的远程控制继续运行。", Forms.ToolTipIcon.Info);
    }
    public async Task ExitAsync()
    {
        if (operationRunning || !initialized) { SetStatus("请等待初始化或当前操作结束后退出。", true); return; }
        operationRunning = true;
        try
        {
            await nxHost.StopAsync();
            nxHost.Dispose();
            controller.Dispose();
            await StopApiAsync();
            hotkey.Dispose(); tray.Visible = false; tray.Dispose();
            exiting = true; Close(); Application.Current.Shutdown();
        }
        catch (Exception ex) { ShowFromTray(); Report(new InvalidOperationException("未退出：恢复显示或清理虚拟屏幕失败。" + ex.Message, ex)); }
        finally { operationRunning = false; }
    }
    // ---------------------------------------------------------------------
    // go-nx low-bandwidth remote control
    // ---------------------------------------------------------------------

    /// <summary>
    /// Stops the sidecar before any display topology transaction and restarts it
    /// only after the topology is stable. A restart failure never rolls back the
    /// display operation the user requested.
    /// </summary>
    private async Task WithTopologyAsync(Func<Task> operation)
    {
        bool restart = nxHost.IsRunning;
        if (restart)
        {
            await nxHost.StopAsync();
            SetStatus("显示拓扑即将变化，已暂停低带宽远控。");
        }
        try { await operation(); }
        finally
        {
            if (restart && settings.NxHostEnabled)
            {
                try
                {
                    await StartNxHostCoreAsync();
                    SetStatus("显示操作完成，低带宽远控已按原设置恢复。");
                }
                catch (Exception ex)
                {
                    RemoteDiagnostics.Log("nx-host restart after topology change failed", ex);
                    SetStatus("显示操作已完成，但低带宽远控未能恢复：" + ex.Message, true);
                }
            }
        }
    }

    private NxHostOptions ReadNxOptions()
    {
        if (!int.TryParse(NxFpsBox.Text.Trim(), out int fps)) throw new ArgumentException("远控帧率必须为整数。");
        return new NxHostOptions(NxListenBox.Text.Trim(), NxDisplayBox.Text.Trim(), fps, store.LocalNxHostDirectory).Validate();
    }

    private async Task StartNxHostCoreAsync()
    {
        var options = new NxHostOptions(settings.NxHostListen, settings.NxHostDisplay, settings.NxHostFps, store.LocalNxHostDirectory).Validate();
        await nxHost.StartAsync(options);
    }

    private void RefreshNxUi()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(RefreshNxUi); return; }
        bool running = nxHost.IsRunning;
        NxEnableButton.Content = running ? "停止低带宽远控" : "启用低带宽远控";
        NxPairButton.IsEnabled = running;
        NxRefreshButton.IsEnabled = running;
        NxUntrustButton.IsEnabled = running && NxTrustedList.SelectedItem is NxTrustedDevice;
        NxListenBox.IsEnabled = !running;
        NxDisplayBox.IsEnabled = !running;
        NxFpsBox.IsEnabled = !running;

        var ready = nxHost.ReadyEvent;
        NxStatusLabel.Text = !running
            ? (nxHost.State == NxHostState.Faulted ? "远控启动失败" : "远控未启用")
            : ready is null ? "远控正在启动…" :
                $"运行中 · {ready.Width}×{ready.Height} @ {ready.Fps ?? settings.NxHostFps} FPS · {ready.Listen}";
        NxPeerLabel.Text = running
            ? string.IsNullOrEmpty(nxHost.ConnectedAddress) ? "无客户端连接 · 等待已配对设备" : "已连接：" + nxHost.ConnectedAddress
            : string.IsNullOrEmpty(nxHost.LastError) ? "无客户端连接" : "上次错误：" + nxHost.LastError;

        if (nxHost.PairingCode is { } code)
        {
            int seconds = (int)Math.Max(0, (nxHost.PairingExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
            NxCodeLabel.Text = $"配对码 {code} · 剩余 {seconds} 秒";
            if (seconds == 0) nxTimer.Stop();
        }
        else NxCodeLabel.Text = "尚未生成配对码";

        string? selected = (NxTrustedList.SelectedItem as NxTrustedDevice)?.Fingerprint;
        NxTrustedList.ItemsSource = nxHost.Trusted;
        if (selected is not null) NxTrustedList.SelectedItem = nxHost.Trusted.FirstOrDefault(x => x.Fingerprint == selected);
        NxUntrustButton.IsEnabled = running && NxTrustedList.SelectedItem is NxTrustedDevice;
    }

    private void HandleNxEvent(NxHostEvent value)
    {
        switch (value.Event)
        {
            case "ready":
                SetStatus($"低带宽远控已就绪：{value.Width}×{value.Height} @ {value.Fps} FPS，监听 {value.Listen}。");
                break;
            case "pairing":
                if (!string.IsNullOrEmpty(value.Code))
                {
                    nxTimer.Start();
                    SetStatus($"配对码 {value.Code} 已生成，{value.ExpiresIn ?? 300} 秒内有效；仅能绑定一台新设备。");
                }
                else SetStatus("配对窗口已关闭。");
                break;
            case "connected":
                SetStatus($"低带宽远控已连接：{value.Addr}。输入注入现在掌握在该客户端手中。");
                break;
            case "disconnected":
                SetStatus("低带宽远控客户端已断开，已释放其按键和鼠标按钮。");
                break;
            case "denied":
                SetStatus("已拒绝远控连接：" + (value.Reason ?? "untrusted"), true);
                break;
            case "error":
                SetStatus("远控 sidecar 错误：" + value.Message, true);
                break;
            case "exited":
                SetStatus("远控 sidecar 已退出。", true);
                break;
        }
        RefreshNxUi();
    }

    private async void NxEnable_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (nxHost.IsRunning)
        {
            await nxHost.StopAsync();
            settings.NxHostEnabled = false; store.Save(settings);
            SetStatus("低带宽远控已停止，不再采集屏幕或注入输入。");
            return;
        }

        var options = ReadNxOptions();
        settings.NxHostEnabled = true;
        settings.NxHostListen = options.Listen;
        settings.NxHostDisplay = options.Display;
        settings.NxHostFps = options.Fps;
        store.Save(settings);
        try
        {
            var ready = await nxHost.StartAsync(options);
            SetStatus($"低带宽远控已启动：{ready.Width}×{ready.Height} @ {ready.Fps} FPS，监听 {ready.Listen}。请生成配对码并用手机连接。");
        }
        catch
        {
            settings.NxHostEnabled = false; store.Save(settings);
            throw;
        }
    });

    private async void NxPair_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (!nxHost.IsRunning) throw new InvalidOperationException("请先启用低带宽远控。");
        await nxHost.PairAsync(300);
        SetStatus("正在打开 5 分钟配对窗口…");
    });

    private async void NxRefresh_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (!nxHost.IsRunning) throw new InvalidOperationException("低带宽远控未运行。");
        await nxHost.RefreshStatusAsync();
        SetStatus("已请求远控设备状态。");
    });

    private void NxDeviceList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        NxUntrustButton.IsEnabled = nxHost.IsRunning && NxTrustedList.SelectedItem is NxTrustedDevice;
    }

    private async void NxUntrust_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (NxTrustedList.SelectedItem is not NxTrustedDevice device) throw new InvalidOperationException("请选择要解除信任的设备。");
        if (MessageBox.Show(this, $"解除证书 {device.ShortFingerprint} 的远控信任？该设备的下一次连接会被拒绝。", "解除远控设备",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await nxHost.UntrustAsync(device.Fingerprint);
        SetStatus($"已请求解除 {device.ShortFingerprint} 的信任。");
    });

    private async void Exit_Click(object sender, RoutedEventArgs e) => await ExitAsync();
}





