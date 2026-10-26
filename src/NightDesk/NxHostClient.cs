using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace NightDesk;

/// <summary>Parameters for one nx-host sidecar process. Validation happens before any process starts.</summary>
public sealed record NxHostOptions(string Listen, string Display, int Fps, string StateDirectory)
{
    public NxHostOptions Validate()
    {
        if (!IPEndPoint.TryParse(Listen, out var endpoint) || endpoint.Port is < 1 or > 65535)
            throw new ArgumentException("监听地址必须为 IP:端口，例如 127.0.0.1:7000 或 [::1]:7000。");
        if (string.IsNullOrWhiteSpace(Display) || Display.Length > 128 || Display.Any(char.IsControl))
            throw new ArgumentException("显示器选择无效。");
        if (Fps is < 1 or > 60) throw new ArgumentException("远控帧率必须在 1–60 之间。");
        if (string.IsNullOrWhiteSpace(StateDirectory)) throw new ArgumentException("缺少 sidecar 状态目录。");
        return this;
    }
}

/// <summary>One JSON Lines event emitted by nx-host.</summary>
public sealed class NxHostEvent
{
    public string Event { get; init; } = "";
    public string? Version { get; init; }
    public int? Pid { get; init; }
    public string? Listen { get; init; }
    public string? Display { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? Dpi { get; init; }
    public string? Identity { get; init; }
    public string? Code { get; init; }
    public int? ExpiresIn { get; init; }
    public string? Addr { get; init; }
    public string? Reason { get; init; }
    public string? Message { get; init; }
    public Dictionary<string, string>? Trusted { get; init; }
    public int? Fps { get; init; }
    public bool? RequireAuth { get; init; }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static NxHostEvent Parse(string line)
    {
        if (!TryParse(line, out var value, out string error)) throw new InvalidDataException(error);
        return value;
    }

    public static bool TryParse(string line, out NxHostEvent value, out string error)
    {
        value = new NxHostEvent();
        error = "";
        try
        {
            var parsed = JsonSerializer.Deserialize<NxHostEvent>(line, Json);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Event))
            {
                error = "sidecar 事件缺少 event 字段。";
                return false;
            }
            value = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = "sidecar JSON 无效：" + ex.Message;
            return false;
        }
    }
}

public sealed record NxTrustedDevice(string Fingerprint, string Endpoint, string PairedAt)
{
    public string ShortFingerprint => Fingerprint.Length <= 16 ? Fingerprint : Fingerprint[..16] + "…";
    public string Display => ShortFingerprint + (Endpoint.Length == 0 ? "" : " · " + Endpoint);

    /// <summary>
    /// nx-host stores each trusted entry as "endpoint yyyy-MM-dd HH:mm".
    /// Addresses may contain spaces? They do not, but IPv6 brackets and ports do:
    /// splitting from the fixed-width timestamp is the only unambiguous rule.
    /// </summary>
    public static IReadOnlyList<NxTrustedDevice> From(Dictionary<string, string>? trusted)
    {
        if (trusted is null) return Array.Empty<NxTrustedDevice>();
        return trusted
            .Select(x => Create(x.Key, x.Value))
            .OrderBy(x => x.Endpoint, StringComparer.Ordinal)
            .ThenBy(x => x.PairedAt, StringComparer.Ordinal)
            .ToArray();
    }

    public static NxTrustedDevice Create(string fingerprint, string? value)
    {
        string raw = (value ?? "").Trim();
        if (raw.Length > 17)
        {
            string tail = raw[^16..];
            if (DateTime.TryParseExact(tail, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime paired))
                return new NxTrustedDevice(fingerprint, raw[..^16].Trim(), tail);
        }
        return new NxTrustedDevice(fingerprint, raw, "");
    }
}

/// <summary>Commands understood by nx-host. Kept as JSON so the WPF side has no Go dependency.</summary>
public static class NxHostProtocol
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static string Pair(int seconds = 300) => Serialize(new { cmd = "pair", seconds = Math.Clamp(seconds, 1, 3600) });
    public static string CancelPairing() => Serialize(new { cmd = "pair-cancel" });
    public static string Untrust(string fingerprint) => Serialize(new { cmd = "untrust", fingerprint });
    public static string Fps(int value) => Serialize(new { cmd = "fps", value = Math.Clamp(value, 1, 60) });
    public static string Status() => Serialize(new { cmd = "status" });
    public static string Stop() => Serialize(new { cmd = "stop" });
    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
}

/// <summary>Locates the packaged native sidecar; an environment override is available for diagnostics.</summary>
public static class NxHostLocator
{
    public static string? Resolve(string? overridePath = null)
    {
        string? configured = overridePath ?? Environment.GetEnvironmentVariable("NIGHTDESK_NX_HOST");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string full = Path.GetFullPath(configured);
            if (!File.Exists(full)) throw new FileNotFoundException("NIGHTDESK_NX_HOST 指向的文件不存在。", full);
            return full;
        }
        foreach (string candidate in Candidates())
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "nx-host.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "native", "nx-host.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "Assets", "nx-host", "nx-host.exe");
    }
}

/// <summary>
/// Owns one nx-host process, its JSON-lines protocol and a Windows Job Object.
/// The Job Object is deliberately created before the first event is consumed:
/// if NightDesk exits or crashes, the OS must also terminate the remote host.
/// </summary>
public sealed class NxHostClient : IDisposable
{
    private readonly Process process;
    private readonly NxHostOptions options;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource<NxHostEvent> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task stdoutTask;
    private readonly Task stderrTask;
    private readonly JobHandle job;
    private int finished;
    private bool disposed;

    public event Action<NxHostEvent>? EventReceived;
    public event Action<int>? Exited;
    public event Action<string>? Log;

    public int ProcessId => process.Id;

    public NxHostClient(string executable, NxHostOptions options)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("未找到 nx-host.exe。请先构建并复制到 NightDesk 输出目录。", executable);
        this.options = options.Validate();
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
        };
        foreach (string argument in BuildArguments(options)) info.ArgumentList.Add(argument);
        job = JobHandle.Create();
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 nx-host.exe。");
        }
        catch
        {
            job.Dispose();
            throw;
        }
        try
        {
            job.Assign(process);
            stdoutTask = PumpStdoutAsync();
            stderrTask = PumpStderrAsync();
            _ = WatchExitAsync();
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            process.Dispose();
            try { job.Dispose(); } catch { }
            throw;
        }
    }

    public static IReadOnlyList<string> BuildArguments(NxHostOptions options)
    {
        options.Validate();
        return new[]
        {
            "-listen", options.Listen,
            "-display", options.Display,
            "-fps", options.Fps.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-state-dir", options.StateDirectory,
            "-control", "stdio"
        };
    }

    public async Task<NxHostEvent> StartAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            return await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("nx-host 在 15 秒内未报告 ready；请检查显示器选择与日志。");
        }
    }

    public Task SendAsync(string json)
    {
        return SendRawAsync(json + "\n");
    }

    public async Task StopAsync(TimeSpan? timeout = null)
    {
        if (finished != 0) return;
        try
        {
            using var commandTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await SendRawAsync(NxHostProtocol.Stop() + "\n", commandTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            Log?.Invoke("stop command failed: " + ex.Message);
        }
        using var exitTimeout = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        try { await process.WaitForExitAsync(exitTimeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Synchronous stop used just before a display topology transaction.</summary>
    public void Stop(TimeSpan? timeout = null)
    {
        try { StopAsync(timeout ?? TimeSpan.FromSeconds(4)).GetAwaiter().GetResult(); }
        catch (Exception ex) { Log?.Invoke("synchronous stop failed: " + ex.Message); throw; }
    }

    private async Task SendRawAsync(string text, CancellationToken cancellationToken = default)
    {
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (process.HasExited) throw new IOException("nx-host 已退出。");
            await process.StandardInput.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { writeGate.Release(); }
    }

    private async Task PumpStdoutAsync()
    {
        try
        {
            while (true)
            {
                string? line = await process.StandardOutput.ReadLineAsync(lifetime.Token).ConfigureAwait(false);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!NxHostEvent.TryParse(line, out var value, out string error))
                {
                    Log?.Invoke("invalid event: " + error + " line=" + line);
                    continue;
                }
                if (value.Event == "ready") ready.TrySetResult(value);
                try { EventReceived?.Invoke(value); }
                catch (Exception ex) { Log?.Invoke("event handler failed: " + ex.Message); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log?.Invoke("stdout pump failed: " + ex.Message);
            ready.TrySetException(ex);
        }
    }

    private async Task PumpStderrAsync()
    {
        try
        {
            while (true)
            {
                string? line = await process.StandardError.ReadLineAsync(lifetime.Token).ConfigureAwait(false);
                if (line is null) break;
                if (!string.IsNullOrWhiteSpace(line)) Log?.Invoke(line);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Log?.Invoke("stderr pump failed: " + ex.Message); }
    }

    private async Task WatchExitAsync()
    {
        try { await process.WaitForExitAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log?.Invoke("wait failed: " + ex.Message); }
        Finish();
    }

    private void Finish()
    {
        if (Interlocked.Exchange(ref finished, 1) != 0) return;
        int code;
        try { code = process.ExitCode; } catch { code = -1; }
        if (!ready.Task.IsCompleted)
            ready.TrySetException(new InvalidOperationException($"nx-host 在 ready 前退出（exit={code}）。"));
        try { EventReceived?.Invoke(new NxHostEvent { Event = "exited", Message = $"exit={code}" }); } catch { }
        try { Exited?.Invoke(code); } catch { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { if (!process.HasExited) { try { process.StandardInput.Close(); } catch { } if (!process.WaitForExit(2000)) process.Kill(entireProcessTree: true); } } catch { }
        lifetime.Cancel();
        try { process.Dispose(); } catch { }
        job.Dispose();
        writeGate.Dispose();
        lifetime.Dispose();
    }
}

/// <summary>Win32 job handle that kills the sidecar if NightDesk disappears.</summary>
internal sealed class JobHandle : IDisposable
{
    private const uint ExtendedLimitInformationClass = 9;
    private const uint LimitKillOnJobClose = 0x00002000;
    private IntPtr handle;

    private JobHandle(IntPtr value) { handle = value; }

    /// <summary>Creates the job before the child exists, shrinking the orphan window to the time between spawn and assignment.</summary>
    public static JobHandle Create()
    {
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject 失败。");
        var result = new JobHandle(job);
        try
        {
            var info = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = LimitKillOnJobClose }
            };
            int size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            IntPtr memory = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, memory, false);
                if (!SetInformationJobObject(job, ExtendedLimitInformationClass, memory, (uint)size))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject 失败。");
            }
            finally { Marshal.FreeHGlobal(memory); }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public void Assign(Process process)
    {
        if (handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(JobHandle));
        if (!AssignProcessToJobObject(handle, process.Handle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject 失败；为安全起见未启动远控。");
    }

    public void Dispose()
    {
        IntPtr current = Interlocked.Exchange(ref handle, IntPtr.Zero);
        if (current != IntPtr.Zero) CloseHandle(current);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, uint infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
}
