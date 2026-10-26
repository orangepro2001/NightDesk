using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NightDesk;

public sealed class VddClient : IDisposable
{
    private const string InterfaceGuid = "{00b41627-04c4-429e-a26e-0265cf50c8fa}";
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareRead = 1;
    private const uint ShareWrite = 2;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x80;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint IoctlAdd = 0x22E004;
    private const uint IoctlRemove = 0x22A008;
    private const uint IoctlUpdate = 0x22A00C;
    private const uint IoctlVersion = 0x22E010;
    private readonly object gate = new();
    private readonly HashSet<int> owned = new();
    private SafeFileHandle? handle;
    private Timer? keepAlive;
    private bool disposed;

    public bool IsOpen => handle is { IsInvalid: false, IsClosed: false };
    public IReadOnlyCollection<int> OwnedIndices { get { lock (gate) return owned.ToArray(); } }

    public bool IsOtherManagerRunning() => Process.GetProcessesByName("ParsecVDisplay").Any(p =>
    {
        try { return !p.HasExited && p.Id != Environment.ProcessId; }
        catch { return false; }
    });

    public void EnsureOpen()
    {
        ThrowIfDisposed();
        lock (gate)
        {
            if (IsOpen) return;
            handle = OpenDeviceInterface(InterfaceGuid) ?? throw new InvalidOperationException("找不到 Parsec VDD 设备接口；请先安装并启用 Parsec Virtual Display Driver。");
            if (!IoControl(IoctlVersion, null, out var version, 1500))
            {
                handle.Dispose(); handle = null;
                throw new InvalidOperationException("无法访问 Parsec VDD 控制接口；请关闭其它虚拟显示管理器后重试。");
            }
            Version = $"{(version >> 16) & 0xffff}.{version & 0xffff}";
        }
    }

    public string Version { get; private set; } = "unknown";

    public int AddDisplay()
    {
        ThrowIfDisposed();
        if (IsOtherManagerRunning()) throw new InvalidOperationException("检测到 ParsecVDisplay 正在运行。请先关闭它，避免两个管理器同时控制虚拟显示器。");
        EnsureOpen();
        lock (gate)
        {
            if (!IoControl(IoctlAdd, null, out var index, 5000) || index < 0)
                throw new InvalidOperationException("Parsec VDD 创建虚拟显示器失败。");
            owned.Add(index);
            EnsureKeepAlive();
            return index;
        }
    }

    /// <summary>接管由上一个 NightDesk 实例创建、驱动仍在保留的显示器（重启后保持同一虚拟屏）。</summary>
    public void AdoptDisplay(int index)
    {
        ThrowIfDisposed();
        if (index is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(index));
        EnsureOpen();
        lock (gate)
        {
            if (!IoControl(IoctlUpdate, null, out int _, 1500))
                throw new InvalidOperationException("Parsec VDD 未响应，无法接管现有虚拟显示器。");
            owned.Add(index);
            EnsureKeepAlive();
        }
    }

    public void RemoveDisplay(int index)
    {
        ThrowIfDisposed();
        lock (gate)
        {
            if (!owned.Contains(index)) throw new InvalidOperationException("该虚拟显示器不是 NightDesk 创建的，拒绝删除。");
            if (!IsOpen || !IoControl(IoctlRemove, new byte[] { 0, (byte)(index & 0xff) }, out int ignoredResult, 1500))
                throw new InvalidOperationException("Parsec VDD 删除虚拟显示器失败。");
            owned.Remove(index);
            if (owned.Count == 0) StopKeepAlive();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            StopKeepAlive();
            handle?.Dispose(); handle = null;
            owned.Clear();
        }
    }

    private void EnsureKeepAlive()
    {
        keepAlive ??= new Timer(timerState =>
        {
            lock (gate)
            {
                if (disposed || owned.Count == 0 || !IsOpen) return;
                _ = IoControl(IoctlUpdate, null, out int _, 500);
            }
        }, null, 0, 100);
    }

    private void StopKeepAlive()
    {
        keepAlive?.Dispose(); keepAlive = null;
    }

    private bool IoControl(uint code, byte[]? input, out int result, int timeoutMs)
    {
        result = 0;
        if (handle is null || handle.IsInvalid) return false;
        var overlapped = new Native.Overlapped { EventHandle = IntPtr.Zero };
        IntPtr eventHandle = Native.CreateEvent(IntPtr.Zero, true, false, null);
        if (eventHandle == IntPtr.Zero) return false;
        overlapped.EventHandle = eventHandle;
        IntPtr inPtr = Marshal.AllocHGlobal(32);
        IntPtr outPtr = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Span<byte> inputBuffer = stackalloc byte[32]; inputBuffer.Clear();
            if (input is not null) input.AsSpan(0, Math.Min(input.Length, 32)).CopyTo(inputBuffer);
            Marshal.Copy(inputBuffer.ToArray(), 0, inPtr, 32);
            Marshal.WriteInt32(outPtr, 0);
            _ = Native.DeviceIoControl(handle, code, inPtr, 32, outPtr, sizeof(int), IntPtr.Zero, ref overlapped);
            bool complete = Native.GetOverlappedResultEx(handle, ref overlapped, out uint ignoredBytes, (uint)timeoutMs, false);
            if (!complete)
            {
                _ = Native.CancelIoEx(handle, ref overlapped);
                _ = Native.GetOverlappedResult(handle, ref overlapped, out uint ignoredBytesAfterCancel, true);
                return false;
            }
            result = Marshal.ReadInt32(outPtr);
            return true;
        }
        finally
        {
            Native.CloseHandle(eventHandle);
            Marshal.FreeHGlobal(inPtr); Marshal.FreeHGlobal(outPtr);
        }
    }

    private static SafeFileHandle? OpenDeviceInterface(string interfaceGuid)
    {
        var guid = Guid.Parse(interfaceGuid);
        IntPtr info = Native.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, Native.DigcfPresent | Native.DigcfDeviceInterface);
        if (info == Native.InvalidHandleValue) return null;
        try
        {
            for (uint i = 0; ; i++)
            {
                var data = new Native.DeviceInterfaceData { CbSize = Marshal.SizeOf<Native.DeviceInterfaceData>() };
                if (!Native.SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref guid, i, ref data)) break;
                Native.SetupDiGetDeviceInterfaceDetail(info, ref data, IntPtr.Zero, 0, out var needed, IntPtr.Zero);
                if (needed <= 0) continue;
                IntPtr detail = Marshal.AllocHGlobal(needed);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!Native.SetupDiGetDeviceInterfaceDetail(info, ref data, detail, needed, out int ignoredRequiredSize, IntPtr.Zero)) continue;
                    string? path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    var opened = Native.CreateFile(path, GenericRead | GenericWrite, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, FileAttributeNormal | FileFlagOverlapped, IntPtr.Zero);
                    if (opened is { IsInvalid: false }) return opened;
                    opened?.Dispose();
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
            return null;
        }
        finally { Native.SetupDiDestroyDeviceInfoList(info); }
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(VddClient));
    }

    private static class Native
    {
        public const uint DigcfPresent = 2, DigcfDeviceInterface = 16;
        public static readonly IntPtr InvalidHandleValue = new(-1);
        [StructLayout(LayoutKind.Sequential)] public struct DeviceInterfaceData { public int CbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] public struct Overlapped { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr EventHandle; }
        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)] public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref DeviceInterfaceData deviceInterfaceData);
        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref DeviceInterfaceData data, IntPtr detail, int detailSize, out int requiredSize, IntPtr deviceInfoData);
        [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string? name);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, int inputSize, IntPtr output, int outputSize, IntPtr bytesReturned, ref Overlapped overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetOverlappedResultEx(SafeFileHandle device, ref Overlapped overlapped, out uint bytes, uint milliseconds, bool alertable);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetOverlappedResult(SafeFileHandle device, ref Overlapped overlapped, out uint bytes, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CancelIoEx(SafeFileHandle device, ref Overlapped overlapped);
    }
}




