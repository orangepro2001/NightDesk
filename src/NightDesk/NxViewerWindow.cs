using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NightDesk;

/// <summary>Native WPF window for controlling another Windows NightDesk desktop.</summary>
public sealed class NxViewerWindow : Window
{
    private readonly NxViewerTarget target;
    private readonly string pairingCode;
    private readonly string fingerprint;
    private readonly string stateDirectory;
    private readonly Image screen = new()
    {
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Focusable = false,
        SnapsToDevicePixels = true
    };
    private readonly TextBlock status = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(210, 238, 154)),
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock overlay = new()
    {
        Text = "正在连接…",
        FontSize = 20,
        Foreground = new SolidColorBrush(Color.FromRgb(230, 238, 234)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        MaxWidth = 720
    };
    private readonly Button menuButton = new()
    {
        Content = "☰",
        ToolTip = "显示或隐藏远控控制栏（F1）",
        FontSize = 18,
        Padding = new Thickness(10, 4, 10, 4),
        Background = new SolidColorBrush(Color.FromArgb(224, 27, 37, 34)),
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Focusable = true
    };
    private readonly Border controlsPanel = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(244, 23, 33, 31)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(74, 98, 91)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(12),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Stretch,
        Visibility = Visibility.Collapsed
    };
    private readonly Stopwatch pointerClock = Stopwatch.StartNew();
    private readonly HashSet<Key> pressedKeys = new();
    private WriteableBitmap? bitmap;
    private NxViewerSession? session;
    private bool connected;
    private bool closing;
    private bool closeConfirmed;
    private bool controlsVisible;
    private bool suppressNextMouseUp;
    private TimeSpan lastPointerSent;

    public NxViewerWindow(NxViewerTarget target, string pairingCode, string fingerprint,
        string stateDirectory, Window owner)
    {
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        this.pairingCode = pairingCode ?? "";
        this.fingerprint = NxViewerSecurity.NormalizeFingerprint(fingerprint);
        this.stateDirectory = stateDirectory;
        Owner = owner;
        Title = "NightDesk · 桌面控制 · " + target.Address;
        Width = 1280;
        Height = 820;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(8, 12, 11));
        Foreground = new SolidColorBrush(Color.FromRgb(230, 238, 234));
        FontFamily = new FontFamily("Microsoft YaHei UI");

        var root = new Grid();
        var viewport = new Grid
        {
            Background = Brushes.Black,
            Focusable = true,
            ClipToBounds = true
        };
        viewport.Children.Add(screen);
        viewport.Children.Add(overlay);
        root.Children.Add(viewport);
        var side = new StackPanel { Width = 260 };
        side.Margin = new Thickness(12, 56, 12, 12);
        side.Children.Add(new TextBlock
        {
            Text = "远控控制",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        });
        side.Children.Add(status);
        side.Children.Add(new TextBlock
        {
            Text = "鼠标与键盘会发送到远端。F1 显示/隐藏本栏；需发送远端 F1 时使用下方专用按钮。Shift+滚轮横向滚动。窗口失焦或关闭时自动释放按键和鼠标按钮。",
            Foreground = new SolidColorBrush(Color.FromRgb(143, 167, 154)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        var sendF1 = new Button { Content = "发送 F1 到远端", Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 4, 0, 4) };
        sendF1.Click += (_, _) =>
        {
            if (!connected || session is null) return;
            session.PostKey(true, 0x70);
            session.PostKey(false, 0x70);
        };
        side.Children.Add(sendF1);
        var release = new Button { Content = "释放全部按键和鼠标", Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 4, 0, 4) };
        release.Click += (_, _) => { pressedKeys.Clear(); ReleaseInput(); };
        side.Children.Add(release);
        var close = new Button { Content = "断开并关闭", Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 4, 0, 4) };
        close.Click += (_, _) => Close();
        side.Children.Add(close);
        controlsPanel.Child = side;
        viewport.Children.Add(controlsPanel);
        viewport.Children.Add(menuButton);
        menuButton.Click += (_, _) => ToggleControls();
        Content = root;

        SetStatus("正在建立固定证书的 QUIC 连接…");
        Loaded += async (_, _) => await ConnectAsync();
        Activated += (_, _) => viewport.Focus();
        Deactivated += (_, _) => ReleaseInput();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        PreviewTextInput += OnPreviewTextInput;
        screen.MouseMove += OnMouseMove;
        screen.MouseDown += OnMouseDown;
        screen.MouseUp += OnMouseUp;
        screen.MouseWheel += OnMouseWheel;
        Closing += OnClosing;
        Closed += (_, _) => ReleaseInput();
    }

    private void ToggleControls()
    {
        controlsVisible = !controlsVisible;
        controlsPanel.Visibility = controlsVisible ? Visibility.Visible : Visibility.Collapsed;
        menuButton.Content = controlsVisible ? "×" : "☰";
        menuButton.VerticalAlignment = controlsVisible ? VerticalAlignment.Top : VerticalAlignment.Center;
        menuButton.Margin = controlsVisible ? new Thickness(0, 8, 8, 0) : new Thickness(0);
        pressedKeys.Clear();
        ReleaseInput();
    }

    public event Action<string, bool>? StatusChanged;

    private async Task ConnectAsync()
    {
        try
        {
            session = await NxViewerSession.ConnectAsync(target, pairingCode, fingerprint, stateDirectory);
            session.FrameReceived += OnFrame;
            session.Streaming += () => Dispatcher.BeginInvoke(() =>
            {
                connected = true;
                overlay.Visibility = Visibility.Collapsed;
                // The host composites its system cursor into the frame; hiding the
                // local pointer avoids two competing cursors on the remote image.
                screen.Cursor = Cursors.None;
                SetStatus($"已连接 · {session?.FrameWidth}×{session?.FrameHeight} · 主机指纹 {Short(session?.HostFingerprint)}");
            });
            session.Closed += OnSessionClosed;
            session.Start();
            SetStatus(session.WasFirstTrust
                ? $"首次信任主机证书 {Short(session.HostFingerprint)}；后续连接会固定校验。等待画面…"
                : $"证书已固定为 {Short(session.HostFingerprint)}。等待画面…");
        }
        catch (Exception ex)
        {
            overlay.Text = "连接失败\n" + ex.Message;
            SetStatus(ex.Message, failed: true);
            RemoteDiagnostics.Log("nx-viewer connect", ex);
        }
    }

    private void OnFrame(NxViewerFrameUpdate update)
    {
        try
        {
            if (Dispatcher.CheckAccess()) ApplyFrame(update);
            else Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => ApplyFrame(update)));
        }
        catch (Exception ex)
        {
            RemoteDiagnostics.Log("nx-viewer render", ex);
        }
    }

    private void ApplyFrame(NxViewerFrameUpdate update)
    {
        if (bitmap is null || bitmap.PixelWidth != update.Width || bitmap.PixelHeight != update.Height)
        {
            // GDI 32-bit DIBs leave the high byte undefined; treat the frame as
            // opaque BGRx so WPF doesn't blend captured colors with the viewer.
            bitmap = new WriteableBitmap(update.Width, update.Height, 96, 96, PixelFormats.Bgr32, null);
            screen.Source = bitmap;
        }
        int stride = update.Width * 4;
        long changedArea = update.Dirty.Sum(rect => (long)rect.Width * rect.Height);
        if (update.Sequence == 1 || changedArea * 3 > (long)update.Width * update.Height)
        {
            bitmap.WritePixels(new Int32Rect(0, 0, update.Width, update.Height), update.Pixels, stride, 0);
        }
        else
        {
            foreach (NxViewerRect rect in update.Dirty)
            {
                if (rect.Width <= 0 || rect.Height <= 0) continue;
                int offset = rect.Y * stride + rect.X * 4;
                bitmap.WritePixels(new Int32Rect(rect.X, rect.Y, rect.Width, rect.Height),
                    update.Pixels, stride, offset);
            }
        }
    }

    private void OnSessionClosed(NxViewerClose value)
    {
        Dispatcher.BeginInvoke(() =>
        {
            connected = false;
            screen.Cursor = Cursors.Arrow;
            overlay.Visibility = Visibility.Visible;
            overlay.Text = value.Message;
            SetStatus(value.Message, value.Failed);
        });
    }

    private void SetStatus(string message, bool failed = false)
    {
        status.Text = message;
        status.Foreground = new SolidColorBrush(failed
            ? Color.FromRgb(233, 179, 148)
            : Color.FromRgb(210, 238, 154));
        StatusChanged?.Invoke(message, failed);
    }

    private static string Short(string? fingerprint) =>
        string.IsNullOrEmpty(fingerprint) ? "未知" :
        fingerprint.Length <= 16 ? fingerprint : fingerprint[..16] + "…";

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            if (!e.IsRepeat) ToggleControls();
            e.Handled = true;
            return;
        }
        if (controlsVisible) return;
        if (!connected || session is null) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed) return;
        if (!pressedKeys.Add(key)) return;
        if (!ShouldSendKey(key)) return;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk <= 0) return;
        session.PostKey(true, vk);
        e.Handled = true;
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.F1)
        {
            e.Handled = true;
            return;
        }
        if (controlsVisible) return;
        if (key == Key.ImeProcessed) return;
        bool wasPressed = pressedKeys.Remove(key);
        if (!connected || session is null || !wasPressed || !ShouldSendKey(key)) return;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk <= 0) return;
        session.PostKey(false, vk);
        e.Handled = true;
    }

    private static bool ShouldSendKey(Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)
            return true;
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return true;
        if (key is >= Key.A and <= Key.Z) return false;
        if (key is >= Key.D0 and <= Key.D9) return false;
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return false;
        if (key == Key.Space) return false;
        if (key.ToString().StartsWith("Oem", StringComparison.Ordinal)) return false;
        return true;
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (controlsVisible)
        {
            e.Handled = true;
            return;
        }
        if (!connected || session is null || string.IsNullOrEmpty(e.Text)) return;
        if (e.Text.Any(char.IsControl)) return;
        session.PostText(e.Text);
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (controlsVisible) return;
        if (!connected || session is null) return;
        if (e.LeftButton != MouseButtonState.Pressed && e.MiddleButton != MouseButtonState.Pressed &&
            e.RightButton != MouseButtonState.Pressed && pointerClock.Elapsed - lastPointerSent < TimeSpan.FromMilliseconds(33))
            return;
        if (TryMap(e.GetPosition(screen), out int x, out int y))
        {
            session.PostMouseMove(x, y);
            lastPointerSent = pointerClock.Elapsed;
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (controlsVisible)
        {
            suppressNextMouseUp = true;
            ToggleControls();
            e.Handled = true;
            return;
        }
        if (!connected || session is null) return;
        int? button = ButtonNumber(e.ChangedButton);
        if (button is null || !TryMap(e.GetPosition(screen), out int x, out int y)) return;
        Focus();
        screen.CaptureMouse();
        session.PostMouseButton(true, button.Value, x, y);
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (controlsVisible || suppressNextMouseUp)
        {
            suppressNextMouseUp = false;
            e.Handled = true;
            return;
        }
        int? button = ButtonNumber(e.ChangedButton);
        if (button is null) return;
        if (TryMap(e.GetPosition(screen), out int x, out int y)) session?.PostMouseButton(false, button.Value, x, y);
        screen.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (controlsVisible)
        {
            e.Handled = true;
            return;
        }
        if (!connected || session is null || !TryMap(e.GetPosition(screen), out int x, out int y)) return;
        int notches = e.Delta / 120;
        if (notches == 0 && e.Delta != 0) notches = Math.Sign(e.Delta);
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) session.PostHWheel(notches, x, y);
        else session.PostWheel(notches, x, y);
        e.Handled = true;
    }

    private static int? ButtonNumber(MouseButton button) => button switch
    {
        MouseButton.Left => 0,
        MouseButton.Right => 1,
        MouseButton.Middle => 2,
        _ => null
    };

    private bool TryMap(Point point, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (session is null || session.FrameWidth < 1 || session.FrameHeight < 1) return false;
        if (screen.ActualWidth <= 0 || screen.ActualHeight <= 0) return false;
        double scale = Math.Min(screen.ActualWidth / session.FrameWidth, screen.ActualHeight / session.FrameHeight);
        double renderWidth = session.FrameWidth * scale;
        double renderHeight = session.FrameHeight * scale;
        double left = (screen.ActualWidth - renderWidth) / 2;
        double top = (screen.ActualHeight - renderHeight) / 2;
        if (point.X < left || point.Y < top || point.X >= left + renderWidth || point.Y >= top + renderHeight)
            return false;
        x = Math.Clamp((int)((point.X - left) / scale), 0, session.FrameWidth - 1);
        y = Math.Clamp((int)((point.Y - top) / scale), 0, session.FrameHeight - 1);
        return true;
    }

    private void ReleaseInput()
    {
        NxViewerSession? active = session;
        if (active is null) return;
        _ = active.ReleaseAllAsync().ContinueWith(task =>
            RemoteDiagnostics.Log("nx-viewer release all", task.Exception!), TaskContinuationOptions.OnlyOnFaulted);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeConfirmed) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        connected = false;
        pressedKeys.Clear();
        NxViewerSession? active = session;
        session = null;
        if (active is not null) await active.DisposeAsync();
        closeConfirmed = true;
        Close();
    }
}
