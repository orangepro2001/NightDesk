using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace NightDesk;

public sealed record NxViewerFrameUpdate(
    uint Sequence, int Width, int Height, IReadOnlyList<NxViewerRect> Dirty, byte[] Pixels);

public sealed record NxViewerClose(string Code, string Message, bool Failed);

/// <summary>
/// One desktop-to-desktop go-nx viewer session. QUIC/TLS and tile decoding live
/// here; the WPF window owns rendering and translates local input events.
/// Frame callbacks run synchronously on the receive loop, so callers must copy
/// or render the pixels before returning and must not retain the array.
/// </summary>
public sealed class NxViewerSession : IAsyncDisposable
{
    private const int MaxUpdateBytes = 64 << 20;
    private readonly QuicConnection connection;
    private readonly QuicStream control;
    private readonly X509Certificate2 identity;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly object inputGate = new();
    private readonly HashSet<int> heldKeys = new();
    private readonly HashSet<int> heldButtons = new();
    private byte[]? frame;
    private int width;
    private int height;
    private int lastX;
    private int lastY;
    private long nextOperation;
    private int started;
    private int disposed;
    private int closeRaised;

    private NxViewerSession(QuicConnection connection, QuicStream control, X509Certificate2 identity,
        string hostFingerprint, bool firstTrust)
    {
        this.connection = connection;
        this.control = control;
        this.identity = identity;
        HostFingerprint = hostFingerprint;
        WasFirstTrust = firstTrust;
    }

    public event Action<NxViewerFrameUpdate>? FrameReceived;
    public event Action<ulong, bool>? ReceiptReceived;
    public event Action? Streaming;
    public event Action<NxViewerClose>? Closed;

    public string HostFingerprint { get; }
    public bool WasFirstTrust { get; }
    public int FrameWidth => Volatile.Read(ref width);
    public int FrameHeight => Volatile.Read(ref height);

    public static async Task<NxViewerSession> ConnectAsync(NxViewerTarget target, string pairingCode,
        string expectedFingerprint, string stateDirectory, CancellationToken cancellationToken = default)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (!QuicConnection.IsSupported)
            throw new PlatformNotSupportedException("当前系统不支持 System.Net.Quic。桌面控制需要 Windows 11 / Server 2022 或更新版本；Android 客户端不受影响。");
        pairingCode = (pairingCode ?? "").Trim();
        if (pairingCode.Length > 0 && (pairingCode.Length != 6 || !pairingCode.All(char.IsAsciiDigit)))
            throw new ArgumentException("配对码必须为 6 位数字；已配对设备可留空。", nameof(pairingCode));
        string requested = NxViewerSecurity.NormalizeFingerprint(expectedFingerprint);
        var pins = NxViewerPinStore.Load(stateDirectory);
        string? saved = pins.Get(target);
        if (requested.Length > 0 && saved is not null &&
            !string.Equals(requested, saved, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("输入的主机指纹与本地已保存指纹不一致。确认主机未重装后，先清除保存的主机固定。");

        string expected = requested.Length > 0 ? requested : saved ?? "";
        IPEndPoint endpoint = await target.ResolveAsync(cancellationToken).ConfigureAwait(false);
        X509Certificate2? identity = null;
        QuicConnection? connection = null;
        QuicStream? control = null;
        try
        {
            identity = NxViewerIdentity.LoadOrCreate(stateDirectory);
            string observed = "";
            var tls = new SslClientAuthenticationOptions
            {
                TargetHost = "nightdesk",
                ApplicationProtocols = new List<SslApplicationProtocol> { new("go-nx") },
                ClientCertificates = new X509CertificateCollection { identity },
                EnabledSslProtocols = SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                {
                    try { observed = NxViewerSecurity.ComputeFingerprint(certificate); }
                    catch { return false; }
                    return expected.Length == 0 || string.Equals(observed, expected, StringComparison.OrdinalIgnoreCase);
                }
            };
            var options = new QuicClientConnectionOptions
            {
                RemoteEndPoint = endpoint,
                ClientAuthenticationOptions = tls,
                IdleTimeout = TimeSpan.FromSeconds(45),
                KeepAliveInterval = TimeSpan.FromSeconds(5),
                DefaultCloseErrorCode = 0,
                DefaultStreamErrorCode = 0,
                MaxInboundBidirectionalStreams = 16,
                MaxInboundUnidirectionalStreams = 64
            };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            connection = await QuicConnection.ConnectAsync(options, timeout.Token).ConfigureAwait(false);
            if (observed.Length != 64)
                throw new AuthenticationException("目标主机未提供可校验的证书。");
            if (expected.Length > 0 && !string.Equals(observed, expected, StringComparison.OrdinalIgnoreCase))
                throw new AuthenticationException("目标主机证书指纹不匹配。");

            bool firstTrust = saved is null;
            if (saved is null || requested.Length > 0) pins.Set(target, observed);
            control = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, timeout.Token)
                .ConfigureAwait(false);
            byte[] hello = NxViewerWire.Hello(pairingCode);
            await control.WriteAsync(hello, timeout.Token).ConfigureAwait(false);
            await control.FlushAsync(timeout.Token).ConfigureAwait(false);
            return new NxViewerSession(connection, control, identity, observed, firstTrust);
        }
        catch
        {
            if (control is not null) await control.DisposeAsync().ConfigureAwait(false);
            if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
            identity?.Dispose();
            throw;
        }
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref started, 1) != 0) return;
        _ = RunReceiveLoopAsync();
        _ = RunReceiptLoopAsync();
    }

    private async Task RunReceiveLoopAsync()
    {
        string code = "disconnected";
        string message = "远控会话已结束。";
        bool failed = false;
        try
        {
            await FrameLoopAsync(lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            failed = true;
            (code, message) = Classify(ex);
            RemoteDiagnostics.Log("nx-viewer receive loop", ex);
        }
        if (Volatile.Read(ref closeRaised) == 0)
            Closed?.Invoke(new NxViewerClose(code, message, failed));
    }

    private async Task FrameLoopAsync(CancellationToken cancellationToken)
    {
        uint lastSequence = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await using QuicStream stream = await connection.AcceptInboundStreamAsync(cancellationToken).ConfigureAwait(false);
            byte[] update = await ReadLimitedAsync(stream, MaxUpdateBytes, cancellationToken).ConfigureAwait(false);
            if (update.Length < 8) continue;
            uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(update.AsSpan(0, 4));
            int frameWidth = BinaryPrimitives.ReadUInt16LittleEndian(update.AsSpan(4, 2));
            int frameHeight = BinaryPrimitives.ReadUInt16LittleEndian(update.AsSpan(6, 2));
            if (sequence <= lastSequence || frameWidth < 1 || frameHeight < 1) continue;

            if (frame is null || width != frameWidth || height != frameHeight)
            {
                frame = new byte[frameWidth * frameHeight * 4];
                Volatile.Write(ref width, frameWidth);
                Volatile.Write(ref height, frameHeight);
            }
            IReadOnlyList<NxViewerRect> dirty =
                NxViewerTileDecoder.Apply(frame, frameWidth, frameHeight, update.AsSpan(8));
            lastSequence = sequence;
            FrameReceived?.Invoke(new NxViewerFrameUpdate(sequence, frameWidth, frameHeight, dirty, frame));
            if (sequence == 1) Streaming?.Invoke();
            await WritePacketAsync(NxViewerWire.Ack(sequence), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunReceiptLoopAsync()
    {
        try { await ReceiptLoopAsync(lifetime.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { RemoteDiagnostics.Log("nx-viewer receipt loop", ex); }
    }
    private async Task ReceiptLoopAsync(CancellationToken cancellationToken)
    {
        byte[] packet = new byte[10];
        while (!cancellationToken.IsCancellationRequested)
        {
            await control.ReadExactlyAsync(packet, cancellationToken).ConfigureAwait(false);
            if (!NxViewerWire.TryReadReceipt(packet, out ulong operation, out byte status)) continue;
            ReceiptReceived?.Invoke(operation, status == 1);
        }
    }

    public void PostMouseMove(int x, int y)
    {
        lock (inputGate) { lastX = x; lastY = y; }
        QueueInput(NxViewerInputKind.MouseMove, x, y, 0, receipt: false);
    }

    public void PostMouseButton(bool down, int button, int x, int y)
    {
        lock (inputGate)
        {
            lastX = x;
            lastY = y;
            if (down) heldButtons.Add(button);
            else heldButtons.Remove(button);
        }
        QueueInput(down ? NxViewerInputKind.MouseDown : NxViewerInputKind.MouseUp, x, y, button, receipt: true);
    }

    public void PostWheel(int delta, int x, int y)
    {
        lock (inputGate) { lastX = x; lastY = y; }
        QueueInput(NxViewerInputKind.Wheel, x, y, delta, receipt: false);
    }

    public void PostHWheel(int delta, int x, int y)
    {
        lock (inputGate) { lastX = x; lastY = y; }
        QueueInput(NxViewerInputKind.WheelH, x, y, delta, receipt: false);
    }

    public void PostKey(bool down, int virtualKey)
    {
        lock (inputGate)
        {
            if (down) heldKeys.Add(virtualKey);
            else heldKeys.Remove(virtualKey);
        }
        QueueInput(down ? NxViewerInputKind.KeyDown : NxViewerInputKind.KeyUp, 0, 0, virtualKey, receipt: true);
    }

    public void PostText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        ulong operation = NextOperation();
        byte[] packet = NxViewerWire.Text(operation, text);
        _ = WritePacketAsync(packet, lifetime.Token).ContinueWith(task =>
            RemoteDiagnostics.Log("nx-viewer text send: " + RemoteDiagnostics.Describe(task.Exception!)),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    /// <summary>Test hook: sends a receipt-bearing input and returns its operation ID.</summary>
    public ulong SendInputAsync(byte kind, int x = 0, int y = 0, int argument = 0)
    {
        ulong operation = NextOperation();
        byte[] packet = NxViewerWire.Input(operation, kind, x, y, argument);
        _ = WritePacketAsync(packet, lifetime.Token);
        return operation;
    }

    public async Task ReleaseAllAsync()
    {
        int[] keys;
        int[] buttons;
        int x;
        int y;
        lock (inputGate)
        {
            keys = heldKeys.ToArray();
            buttons = heldButtons.ToArray();
            heldKeys.Clear();
            heldButtons.Clear();
            x = lastX;
            y = lastY;
        }
        foreach (int key in keys)
        {
            try { await SendInputNowAsync(NxViewerInputKind.KeyUp, 0, 0, key).ConfigureAwait(false); }
            catch (Exception ex) { RemoteDiagnostics.Log("nx-viewer release key", ex); }
        }
        foreach (int button in buttons)
        {
            try { await SendInputNowAsync(NxViewerInputKind.MouseUp, x, y, button).ConfigureAwait(false); }
            catch (Exception ex) { RemoteDiagnostics.Log("nx-viewer release button", ex); }
        }
    }

    private ulong NextOperation()
    {
        ulong value = (ulong)Interlocked.Increment(ref nextOperation);
        return value == 0 ? (ulong)Interlocked.Increment(ref nextOperation) : value;
    }

    private void QueueInput(byte kind, int x, int y, int argument, bool receipt)
    {
        ulong operation = receipt ? NextOperation() : 0;
        byte[] packet = NxViewerWire.Input(operation, kind, x, y, argument);
        _ = WritePacketAsync(packet, lifetime.Token).ContinueWith(task =>
            RemoteDiagnostics.Log("nx-viewer input send: " + RemoteDiagnostics.Describe(task.Exception!)),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    private async Task SendInputNowAsync(byte kind, int x, int y, int argument)
    {
        byte[] packet = NxViewerWire.Input(0, kind, x, y, argument);
        await WritePacketAsync(packet, lifetime.Token).ConfigureAwait(false);
    }

    private async Task WritePacketAsync(byte[] packet, CancellationToken cancellationToken)
    {
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await control.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
            await control.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { writeGate.Release(); }
    }

    private static async Task<byte[]> ReadLimitedAsync(QuicStream stream, int limit, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[64 * 1024];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > limit) throw new InvalidDataException("远控更新超过大小上限。");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static (string Code, string Message) Classify(Exception ex)
    {
        if (ex is QuicException quic)
        {
            long code = quic.ApplicationErrorCode ?? 0;
            return code switch
            {
                1 => ("untrusted", "目标桌面不信任此设备。请在目标端生成配对码后重新连接。"),
                2 => ("bad-code", "配对码错误或已过期。"),
                3 => ("replaced", "另一个控制端接管了目标桌面。"),
                4 => ("locked", "配对失败次数过多，目标端已暂时锁定；请稍后再试。"),
                _ => ("disconnected", "远控连接已断开：" + quic.Message)
            };
        }
        if (ex is AuthenticationException) return ("identity", "目标主机证书校验失败：" + ex.Message);
        if (ex is SocketException) return ("network", "无法连接目标桌面：" + ex.Message);
        if (ex is InvalidDataException) return ("protocol", "远控数据格式错误：" + ex.Message);
        return ("error", RemoteDiagnostics.Describe(ex));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Interlocked.Exchange(ref closeRaised, 1);
        await ReleaseAllAsync().ConfigureAwait(false);
        lifetime.Cancel();
        try { await control.DisposeAsync().ConfigureAwait(false); } catch { }
        try { await connection.DisposeAsync().ConfigureAwait(false); } catch { }
        identity.Dispose();
        lifetime.Dispose();
        writeGate.Dispose();
    }
}