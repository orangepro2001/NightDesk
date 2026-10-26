namespace NightDesk;

public enum NxHostState
{
    Stopped,
    Starting,
    Running,
    Faulted
}

/// <summary>
/// Owns the optional nx-host sidecar for the lifetime of the WPF window.
/// All start/stop paths are serialized; every display-topology operation must
/// call <see cref="StopForTopologyChangeAsync"/> first and only restart the
/// sidecar after the topology is stable.
/// </summary>
public sealed class NxHostService : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private NxHostClient? client;
    private NxHostOptions? options;
    private NxHostEvent? ready;
    private IReadOnlyList<NxTrustedDevice> trusted = Array.Empty<NxTrustedDevice>();
    private string? connectedAddress;
    private string? lastError;
    private string? pairingCode;
    private DateTimeOffset pairingExpiresAt;
    private int disposed;

    public event Action<NxHostEvent>? EventReceived;
    public event Action<string>? Log;
    public event Action? Changed;

    public NxHostState State { get; private set; } = NxHostState.Stopped;
    public bool IsRunning => Volatile.Read(ref client) is not null;
    public NxHostOptions? RunningOptions => options;
    public NxHostEvent? ReadyEvent => ready;
    public IReadOnlyList<NxTrustedDevice> Trusted => trusted;
    public string? ConnectedAddress => connectedAddress;
    public string? LastError => lastError;
    public string? PairingCode => pairingCode;
    public DateTimeOffset PairingExpiresAt => pairingExpiresAt;

    public async Task<NxHostEvent> StartAsync(NxHostOptions requested, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (client is not null && ready is not null) return ready;

            string executable = NxHostLocator.Resolve()
                ?? throw new FileNotFoundException("未找到 nx-host.exe。请将 sidecar 放到程序目录的 nx-host.exe 或 Assets/nx-host/nx-host.exe。");
            State = NxHostState.Starting;
            lastError = null;
            RaiseChanged();

            var next = new NxHostClient(executable, requested);
            next.EventReceived += OnEvent;
            next.Log += message => Log?.Invoke(message);
            next.Exited += code =>
            {
                bool owned = ReferenceEquals(Volatile.Read(ref client), next);
                if (owned)
                {
                    State = NxHostState.Stopped;
                    connectedAddress = null;
                    pairingCode = null;
                    if (code != 0) lastError = $"nx-host 已退出（exit={code}）。";
                    RaiseChanged();
                }
            };

            client = next;
            options = requested;
            try
            {
                ready = await next.StartAsync(cancellationToken).ConfigureAwait(false);
                State = NxHostState.Running;
                RaiseChanged();
                return ready;
            }
            catch
            {
                client = null;
                ready = null;
                options = null;
                next.Dispose();
                State = NxHostState.Faulted;
                RaiseChanged();
                throw;
            }
        }
        finally { gate.Release(); }
    }

    /// <summary>Stops the sidecar but keeps no automatic restart intent; the caller decides.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Interlocked.Exchange(ref client, null);
            if (current is null) { State = NxHostState.Stopped; return; }
            try { await current.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (Exception ex) { Log?.Invoke("nx-host stop: " + ex.Message); }
            finally
            {
                current.Dispose();
                ready = null;
                options = null;
                connectedAddress = null;
                pairingCode = null;
                State = NxHostState.Stopped;
                RaiseChanged();
            }
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Stops the sidecar and returns true when it was running, so a caller can
    /// restart it only after a successful topology transaction.
    /// </summary>
    public async Task<bool> StopForTopologyChangeAsync()
    {
        bool wasRunning = IsRunning;
        if (wasRunning) await StopAsync().ConfigureAwait(false);
        return wasRunning;
    }

    public async Task SendAsync(string json, CancellationToken cancellationToken = default)
    {
        var current = Volatile.Read(ref client) ?? throw new InvalidOperationException("远控 sidecar 未运行。");
        await current.SendAsync(json).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task PairAsync(int seconds = 300) => SendAsync(NxHostProtocol.Pair(seconds));
    public Task CancelPairingAsync() => SendAsync(NxHostProtocol.CancelPairing());
    public Task UntrustAsync(string fingerprint) => SendAsync(NxHostProtocol.Untrust(fingerprint));
    public Task SetFpsAsync(int fps) => SendAsync(NxHostProtocol.Fps(fps));
    public Task RefreshStatusAsync() => SendAsync(NxHostProtocol.Status());

    private void OnEvent(NxHostEvent value)
    {
        switch (value.Event)
        {
            case "ready":
                ready = value;
                break;
            case "status":
                if (value.Trusted is not null) trusted = NxTrustedDevice.From(value.Trusted);
                connectedAddress = value.Addr;
                pairingCode = null;
                pairingExpiresAt = default;
                break;
            case "pairing":
                pairingCode = string.IsNullOrEmpty(value.Code) ? null : value.Code;
                pairingExpiresAt = pairingCode is null
                    ? default
                    : DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, value.ExpiresIn ?? 300));
                break;
            case "connected":
                connectedAddress = value.Addr;
                break;
            case "disconnected":
                connectedAddress = null;
                break;
            case "denied":
                lastError = "已拒绝连接：" + (value.Reason ?? "untrusted");
                break;
            case "error":
                lastError = value.Message ?? "sidecar 报告错误。";
                break;
            case "exited":
                connectedAddress = null;
                break;
        }
        EventReceived?.Invoke(value);
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); } catch (Exception ex) { Log?.Invoke("service change handler failed: " + ex.Message); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { StopAsync().GetAwaiter().GetResult(); } catch (Exception ex) { Log?.Invoke("dispose stop failed: " + ex.Message); }
        gate.Dispose();
    }
}
