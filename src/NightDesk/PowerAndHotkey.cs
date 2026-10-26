using System.Runtime.InteropServices;

namespace NightDesk;

public sealed class SleepInhibitor : IDisposable
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private bool active;
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SetThreadExecutionState(uint flags);
    public bool IsActive => active;
    public void Set(bool enabled)
    {
        if (enabled)
        {
            if (SetThreadExecutionState(EsContinuous | EsSystemRequired) == 0) throw new InvalidOperationException("Windows 无法设置防休眠状态。");
            active = true;
        }
        else
        {
            _ = SetThreadExecutionState(EsContinuous);
            active = false;
        }
    }
    public void Dispose() => Set(false);
}

public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int Id = 0x4E44;
    private IntPtr hwnd;
    private HwndSource? source;
    private bool registered;
    public event EventHandler? Pressed;
    public void Attach(IntPtr windowHandle)
    {
        hwnd = windowHandle;
        source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("无法连接窗口消息源。");
        source.AddHook(WndProc);
    }
    public bool Register(HotkeySpec spec)
    {
        if (hwnd == IntPtr.Zero) return false;
        if (registered) Unregister();
        registered = Native.RegisterHotKey(hwnd, Id, spec.Modifiers, spec.Key);
        return registered;
    }
    public void Unregister()
    {
        if (registered) Native.UnregisterHotKey(hwnd, Id);
        registered = false;
    }
    public void Dispose()
    {
        Unregister();
        if (source is not null) source.RemoveHook(WndProc);
    }
    private IntPtr WndProc(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == Id) { handled = true; Pressed?.Invoke(this, EventArgs.Empty); }
        return IntPtr.Zero;
    }
    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
