using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NightDesk;

public sealed record TrustedDeviceInfo(string Id, string Name, DateTimeOffset CreatedUtc, DateTimeOffset? LastSeenUtc)
{
    public string ActivityText => LastSeenUtc is DateTimeOffset seen
        ? $"最近使用 {seen.ToLocalTime():yyyy-MM-dd HH:mm}"
        : "尚未使用";
    public string CreatedText => $"绑定于 {CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
}

public sealed record PairingQr(string Payload, DateTimeOffset ExpiresUtc);
public sealed record PairingResult(string DeviceId, string DeviceName, string Token);
public sealed record RemotePairingGrant(string DeviceId, string DeviceName, string Token);

public sealed class PairingRejectedException : UnauthorizedAccessException
{
    public PairingRejectedException() : base("配对票据无效、已过期或已使用。") { }
}

internal sealed class TrustedDeviceRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? LastSeenUtc { get; set; }
}

internal sealed class TrustedDeviceFile
{
    public int SchemaVersion { get; set; } = 1;
    public List<TrustedDeviceRecord> Devices { get; set; } = [];
}

public sealed class DeviceRegistry
{
    private readonly object gate = new();
    private readonly string path;
    private readonly Func<DateTimeOffset> now;
    private readonly JsonSerializerOptions json = new() { WriteIndented = false };
    private readonly Dictionary<string, TrustedDeviceRecord> byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TrustedDeviceRecord> byHash = new(StringComparer.Ordinal);

    public DeviceRegistry(string path, Func<DateTimeOffset>? clock = null)
    {
        this.path = Path.GetFullPath(path);
        now = clock ?? (() => DateTimeOffset.UtcNow);
        Load();
    }

    public bool Authenticate(string? authorization)
    {
        string? token = Security.ReadBearerToken(authorization);
        if (token is null) return false;
        string hash = Hash(token);
        lock (gate)
        {
            if (!byHash.TryGetValue(hash, out TrustedDeviceRecord? device)) return false;
            var current = now();
            if (device.LastSeenUtc is null || current - device.LastSeenUtc.Value >= TimeSpan.FromSeconds(30))
            {
                device.LastSeenUtc = current;
                try { PersistLocked(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return true;
        }
    }

    public TrustedDeviceInfo Add(string name, string token)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("设备名称不能为空。");
        name = name.Trim();
        if (name.Length > 64 || name.Any(char.IsControl)) throw new ArgumentException("设备名称必须为 1–64 个可见字符。");
        if (token.Length is < 32 or > 256 || token.Any(char.IsWhiteSpace)) throw new ArgumentException("设备令牌格式无效。");

        string hash = Hash(token);
        var record = new TrustedDeviceRecord
        {
            Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(),
            Name = name,
            TokenHash = hash,
            CreatedUtc = now()
        };
        lock (gate)
        {
            if (byHash.ContainsKey(hash)) throw new InvalidOperationException("设备令牌重复，请重试。");
            byId.Add(record.Id, record);
            byHash.Add(hash, record);
            try { PersistLocked(); }
            catch
            {
                byId.Remove(record.Id);
                byHash.Remove(hash);
                throw;
            }
            return Info(record);
        }
    }

    public IReadOnlyList<TrustedDeviceInfo> List()
    {
        lock (gate)
            return byId.Values.Select(Info).OrderByDescending(x => x.LastSeenUtc ?? x.CreatedUtc).ToArray();
    }

    public bool Revoke(string id)
    {
        lock (gate)
        {
            if (!byId.TryGetValue(id, out TrustedDeviceRecord? record)) return false;
            byId.Remove(id);
            byHash.Remove(record.TokenHash);
            try { PersistLocked(); }
            catch
            {
                byId.Add(record.Id, record);
                byHash.Add(record.TokenHash, record);
                throw;
            }
            return true;
        }
    }

    public string StoragePath => path;

    private void Load()
    {
        if (!File.Exists(path)) return;
        TrustedDeviceFile file;
        try
        {
            string plain = Dpapi.Unprotect(File.ReadAllText(path));
            file = JsonSerializer.Deserialize<TrustedDeviceFile>(plain, json)
                ?? throw new InvalidDataException("受信任设备文件无有效内容。");
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException("无法解密受信任设备注册表。原文件保留。", ex);
        }
        if (file.SchemaVersion != 1 || file.Devices is null) throw new InvalidDataException("受信任设备注册表版本无效。");
        foreach (TrustedDeviceRecord record in file.Devices)
        {
            if (string.IsNullOrWhiteSpace(record.Id) || string.IsNullOrWhiteSpace(record.Name) ||
                record.Name.Length > 64 || record.Name.Any(char.IsControl) ||
                !IsSha256(record.TokenHash) || record.CreatedUtc == default ||
                !byId.TryAdd(record.Id, record) || !byHash.TryAdd(record.TokenHash, record))
                throw new InvalidDataException("受信任设备注册表包含重复或无效记录。");
        }
    }

    private void PersistLocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var file = new TrustedDeviceFile { Devices = byId.Values.ToList() };
        string plain = JsonSerializer.Serialize(file, json);
        string encrypted = Dpapi.Protect(plain);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, encrypted, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    private static TrustedDeviceInfo Info(TrustedDeviceRecord record) =>
        new(record.Id, record.Name, record.CreatedUtc, record.LastSeenUtc);

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

public sealed class RemoteAuthenticator
{
    public static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(5);
    private readonly Func<string> sharedToken;
    private readonly DeviceRegistry devices;
    private readonly Func<DateTimeOffset> now;
    private readonly TimeSpan pairingLifetime;
    private readonly object gate = new();
    private readonly Dictionary<string, DateTimeOffset> tickets = new(StringComparer.Ordinal);

    public RemoteAuthenticator(Func<string> sharedToken, DeviceRegistry devices,
        TimeSpan? pairingLifetime = null, Func<DateTimeOffset>? clock = null)
    {
        this.sharedToken = sharedToken;
        this.devices = devices;
        this.pairingLifetime = pairingLifetime ?? PairingLifetime;
        now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool IsAuthorized(string? authorization) =>
        Security.HasToken(authorization, sharedToken()) || devices.Authenticate(authorization);

    public PairingQr IssuePairing(string origin)
    {
        var uri = new Uri(origin, UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort ||
            uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !uri.Host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("二维码只能绑定本机 Tailscale HTTPS 地址。");

        string ticket = "ndp1_" + Security.NewToken();
        DateTimeOffset expires = now() + pairingLifetime;
        lock (gate)
        {
            foreach (var expired in tickets.Where(x => x.Value <= now()).Select(x => x.Key).ToArray()) tickets.Remove(expired);
            while (tickets.Count >= 8) tickets.Remove(tickets.MinBy(x => x.Value).Key);
            tickets.Add(ticket, expires);
        }
        string normalized = "https://" + uri.Host.ToLowerInvariant();
        string payload = JsonSerializer.Serialize(new
        {
            v = 1,
            kind = "nightdesk-pair",
            origin = normalized,
            ticket,
            expires = expires.ToUnixTimeSeconds()
        });
        return new PairingQr(payload, expires);
    }

    public PairingResult Pair(string ticket, string deviceName)
    {
        if (string.IsNullOrWhiteSpace(ticket) || ticket.Length > 128) throw new PairingRejectedException();
        lock (gate)
        {
            if (!tickets.Remove(ticket, out DateTimeOffset expires) || expires <= now())
                throw new PairingRejectedException();
        }
        string token = "ndd1_" + Security.NewToken();
        TrustedDeviceInfo device = devices.Add(deviceName, token);
        return new PairingResult(device.Id, device.Name, token);
    }

    public IReadOnlyList<TrustedDeviceInfo> Devices => devices.List();

    public bool RevokeDevice(string id) => devices.Revoke(id);
}
