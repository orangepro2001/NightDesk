using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NightDesk;

// DISPLAYCONFIG_PATH_INFO=72 bytes, DISPLAYCONFIG_MODE_INFO=64 bytes on Windows x64.
// Preserve the complete native graph, including clone groups, exact timings and rotations.
public static class DisplayTopology
{
    private const int PathSize = 72, ModeSize = 64;
    private const uint QdcOnlyActivePaths = 2;
    private const uint UseSupplied = 0x20, Validate = 0x40, Apply = 0x80, AllowChanges = 0x400;
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] byte[] paths, ref uint modeCount, [Out] byte[] modes, IntPtr topologyId);
    [DllImport("user32.dll")] private static extern int SetDisplayConfig(uint pathCount, byte[] paths, uint modeCount, byte[] modes, uint flags);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(byte[] request);

    // LUID is two 32-bit integers (alignment 4), not an aligned 64-bit integer.
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct SourceInfo { public Luid Adapter; public uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)] private struct TargetInfo
    {
        public Luid Adapter;
        public uint Id, ModeIndex, Technology, Rotation, Scaling, RateNumerator, RateDenominator, ScanLine, Available, Status;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PathInfo { public SourceInfo Source; public TargetInfo Target; public uint Flags; }
    public static void VerifyLayouts()
    {
        if (Marshal.SizeOf<SourceInfo>() != 20 || Marshal.SizeOf<TargetInfo>() != 48 || Marshal.SizeOf<PathInfo>() != PathSize ||
            Marshal.OffsetOf<PathInfo>(nameof(PathInfo.Target)).ToInt32() + Marshal.OffsetOf<TargetInfo>(nameof(TargetInfo.ModeIndex)).ToInt32() != 32)
            throw new TypeLoadException("Windows DisplayConfig ABI mismatch.");
    }

    public static RecoverySnapshot Capture()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            int error = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount);
            if (error != 0) throw Error("读取拓扑大小", error);
            if (pathCount > 128 || modeCount > 512) throw new InvalidOperationException("显示拓扑超出安全上限。");
            var paths = new byte[pathCount * PathSize];
            var modes = new byte[modeCount * ModeSize];
            error = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (error == 122) continue;
            if (error != 0) throw Error("读取活动拓扑", error);
            Array.Resize(ref paths, checked((int)pathCount * PathSize));
            Array.Resize(ref modes, checked((int)modeCount * ModeSize));
            var snapshot = new RecoverySnapshot(DateTime.UtcNow, paths, modes);
            ValidateSnapshot(snapshot);
            return snapshot;
        }
        throw new InvalidOperationException("显示拓扑持续变化，请稍后重试。");
    }

    public static void ValidateSnapshot(RecoverySnapshot snapshot)
    {
        if (snapshot.Paths.Length == 0 || snapshot.Paths.Length % PathSize != 0 || snapshot.Paths.Length > 128 * PathSize ||
            snapshot.Modes.Length == 0 || snapshot.Modes.Length % ModeSize != 0 || snapshot.Modes.Length > 512 * ModeSize)
            throw new ArgumentException("显示恢复快照长度无效。");
        int modeCount = snapshot.Modes.Length / ModeSize;
        for (int i = 0; i < snapshot.Paths.Length; i += PathSize)
        {
            uint source = BitConverter.ToUInt32(snapshot.Paths, i + 12);
            uint target = BitConverter.ToUInt32(snapshot.Paths, i + 32); // 20-byte source + target mode index at +12
            if (source != uint.MaxValue && source >= modeCount || target != uint.MaxValue && target >= modeCount)
                throw new ArgumentException("显示恢复快照包含无效模式索引。");
            if (source != uint.MaxValue && BitConverter.ToUInt32(snapshot.Modes, checked((int)source * ModeSize)) != 1 ||
                target != uint.MaxValue && BitConverter.ToUInt32(snapshot.Modes, checked((int)target * ModeSize)) != 2)
                throw new ArgumentException("显示恢复快照包含错误的源/目标模式类型。");
        }
    }

    public static IReadOnlyList<string> ActiveSourceNames(RecoverySnapshot snapshot)
    {
        ValidateSnapshot(snapshot);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < snapshot.Paths.Length; i += PathSize) names.Add(SourceName(snapshot.Paths, i));
        return names.ToArray();
    }

    public static void UseOnly(string sourceName)
    {
        var current = Capture();
        var paths = new List<byte>();
        var modes = current.Modes.ToArray();
        for (int offset = 0; offset < current.Paths.Length; offset += PathSize)
        {
            if (!SourceName(current.Paths, offset).Equals(sourceName, StringComparison.OrdinalIgnoreCase)) continue;
            var path = current.Paths.AsSpan(offset, PathSize).ToArray();
            uint sourceMode = BitConverter.ToUInt32(path, 12);
            if (sourceMode == uint.MaxValue) throw new InvalidOperationException("虚拟屏幕缺少活动源模式。");
            int modeOffset = checked((int)sourceMode * ModeSize);
            if (BitConverter.ToUInt32(modes, modeOffset) != 1) throw new InvalidOperationException("虚拟屏幕源模式类型不正确。");
            // SOURCE_MODE: width, height, pixel format, POINTL position.
            Array.Clear(modes, modeOffset + 28, 8);
            paths.AddRange(path);
        }
        if (paths.Count == 0) throw new InvalidOperationException("虚拟屏幕尚未出现在 Windows 活动拓扑中；物理输出保持开启。");
        ApplySnapshot(new RecoverySnapshot(DateTime.UtcNow, paths.ToArray(), modes));
        var verified = ActiveSourceNames(Capture());
        if (verified.Count != 1 || !verified.Contains(sourceName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Windows 未确认仅虚拟屏幕模式；正在恢复原布局。");
    }

    public static void Restore(RecoverySnapshot snapshot) => ApplySnapshot(snapshot);

    private static void ApplySnapshot(RecoverySnapshot snapshot)
    {
        ValidateSnapshot(snapshot);
        uint paths = (uint)(snapshot.Paths.Length / PathSize), modes = (uint)(snapshot.Modes.Length / ModeSize);
        int error = SetDisplayConfig(paths, snapshot.Paths, modes, snapshot.Modes, UseSupplied | Validate | AllowChanges);
        if (error != 0) throw Error("验证显示拓扑", error);
        error = SetDisplayConfig(paths, snapshot.Paths, modes, snapshot.Modes, UseSupplied | Apply | AllowChanges);
        if (error != 0) throw Error("应用显示拓扑", error);
    }

    private static string SourceName(byte[] paths, int offset)
    {
        var request = new byte[84]; // DISPLAYCONFIG_SOURCE_DEVICE_NAME
        BitConverter.GetBytes(1u).CopyTo(request, 0);
        BitConverter.GetBytes(84u).CopyTo(request, 4);
        Array.Copy(paths, offset, request, 8, 8); // LUID
        Array.Copy(paths, offset + 8, request, 16, 4); // source id
        int error = DisplayConfigGetDeviceInfo(request);
        if (error != 0) throw Error("读取显示源名称", error);
        return System.Text.Encoding.Unicode.GetString(request, 20, 64).TrimEnd('\0');
    }

    private static Exception Error(string action, int error) => new InvalidOperationException($"{action}失败：{new Win32Exception(error).Message}（{error}）。RDP 会话可能无法控制控制台显示拓扑，请在本机用户会话中运行。");
}


