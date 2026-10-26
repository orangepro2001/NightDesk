using System.Runtime.InteropServices;
using System.Text;

namespace NightDesk;

public sealed class DisplayModeNative
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int RefreshRate { get; init; }
    public int BitsPerPixel { get; init; }
}

public static class NativeDisplays
{
    private const uint Attached = 0x00000001;
    private const uint Primary = 0x00000004;
    private const uint Mirroring = 0x00000008;
    private const int CurrentSettings = -1;
    private const int FieldsPosition = 0x00000020;
    private const int FieldsBits = 0x00040000;
    private const int FieldsWidth = 0x00080000;
    private const int FieldsHeight = 0x00100000;
    private const int FieldsFrequency = 0x00400000;
    private const uint Test = 0x00000002;
    private const uint UpdateRegistry = 0x00000001;
    private const uint SetPrimary = 0x00000010;
    private const uint NoReset = 0x10000000;
    private const uint Disable = 0x02000000;
    private const int Success = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields;
        public int PositionX, PositionY, Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPixel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool EnumDisplayDevicesW(string? device, uint number, ref DisplayDevice displayDevice, uint flags);
    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool EnumDisplaySettings(string deviceName, int modeNumber, ref DevMode mode);
    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int ChangeDisplaySettings(string? deviceName, ref DevMode mode, IntPtr hwnd, uint flags, IntPtr parameter);
    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int ChangeDisplaySettings(string? deviceName, IntPtr mode, IntPtr hwnd, uint flags, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    public static void VerifyLayouts()
    {
        if (Marshal.SizeOf<DevMode>() != 220 || Marshal.SizeOf<DisplayDevice>() != 840)
            throw new TypeLoadException($"Windows display ABI mismatch (DEVMODE={Marshal.SizeOf<DevMode>()}, DISPLAY_DEVICE={Marshal.SizeOf<DisplayDevice>()}).");
    }

    public static bool HasActiveHardwareOutput(IEnumerable<MonitorInfo> monitors) =>
        monitors.Any(x => x.Active && x.IsHardware && !x.IsParsec);

    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var result = new List<MonitorInfo>();
        for (uint i = 0; i < 64; i++)
        {
            var adapter = NewDevice();
            if (!EnumDisplayDevicesW(null, i, ref adapter, 0)) break;
            bool active = (adapter.StateFlags & Attached) != 0 && (adapter.StateFlags & Mirroring) == 0;
            bool parsec = adapter.DeviceString.Contains("Parsec", StringComparison.OrdinalIgnoreCase) || adapter.DeviceId.Contains("PSCCDD", StringComparison.OrdinalIgnoreCase);
            bool hardware = adapter.DeviceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase);
            var monitor = NewDevice();
            bool childSeen = false;
            bool childActive = false;
            for (uint j = 0; j < 32; j++)
            {
                monitor = NewDevice();
                if (!EnumDisplayDevicesW(adapter.DeviceName, j, ref monitor, 0)) break;
                if (string.IsNullOrWhiteSpace(monitor.DeviceId)) continue;
                childSeen = true;
                childActive |= (monitor.StateFlags & Attached) != 0;
                if (monitor.DeviceId.Contains("PSCCDD", StringComparison.OrdinalIgnoreCase)) parsec = true;
            }
            if (childSeen) active = active && childActive;
            if (!string.IsNullOrWhiteSpace(adapter.DeviceName))
                result.Add(new MonitorInfo(adapter.DeviceName, adapter.DeviceString, adapter.DeviceId, active, parsec, hardware));
        }
        return result;
    }

    public static DisplayModeNative? Current(string name)
    {
        var mode = NewMode();
        if (!EnumDisplaySettings(name, CurrentSettings, ref mode)) return null;
        return new DisplayModeNative { Width = mode.PelsWidth, Height = mode.PelsHeight, RefreshRate = mode.DisplayFrequency, BitsPerPixel = mode.BitsPerPixel };
    }

    public static IReadOnlyList<DisplayMode> SupportedModes(string name)
    {
        var modes = new HashSet<DisplayMode>();
        for (int i = 0; i < 4096; i++)
        {
            var native = NewMode();
            if (!EnumDisplaySettings(name, i, ref native)) break;
            if (native.PelsWidth >= 640 && native.PelsHeight >= 480 && native.DisplayFrequency is >= 24 and <= 360)
                modes.Add(new DisplayMode(native.PelsWidth, native.PelsHeight, native.DisplayFrequency));
        }
        return modes.OrderByDescending(x => (long)x.Width * x.Height).ThenByDescending(x => x.RefreshRate).ToArray();
    }

    public static void ApplyVirtualMode(string deviceName, DisplayMode requested)
    {
        requested.Validate();
        var current = Current(deviceName) ?? throw new InvalidOperationException("Parsec 虚拟显示器尚未就绪。");
        var mode = NewMode();
        if (!EnumDisplaySettings(deviceName, CurrentSettings, ref mode)) throw new InvalidOperationException("无法读取虚拟显示器当前模式。");
        mode.Fields = FieldsPosition | FieldsBits | FieldsWidth | FieldsHeight | FieldsFrequency;
        // Keep extended-desktop position until topology transaction switches outputs.
        
        mode.BitsPerPixel = Math.Max(32, current.BitsPerPixel);
        mode.PelsWidth = requested.Width;
        mode.PelsHeight = requested.Height;
        mode.DisplayFrequency = requested.RefreshRate;
        if (ChangeDisplaySettings(deviceName, ref mode, IntPtr.Zero, Test, IntPtr.Zero) != Success)
            throw new InvalidOperationException($"Parsec VDD 不支持 {requested}。请选用该显示器支持的分辨率/刷新率。");
        int result = ChangeDisplaySettings(deviceName, ref mode, IntPtr.Zero, 0, IntPtr.Zero);
        if (result != Success) throw new InvalidOperationException($"无法启用虚拟显示器模式（Windows 返回 {result}）。");
    }

    private static DisplayDevice NewDevice() => new() { Size = Marshal.SizeOf<DisplayDevice>(), DeviceName = "", DeviceString = "", DeviceId = "", DeviceKey = "" };
    private static DevMode NewMode() => new() { DeviceName = "", FormName = "", Size = (short)Marshal.SizeOf<DevMode>() };
}

