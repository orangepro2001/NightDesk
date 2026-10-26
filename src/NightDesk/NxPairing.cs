using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NightDesk;

/// <summary>
/// Payload scanned by the Android NX viewer. It deliberately carries the host
/// certificate fingerprint and the short-lived pairing code, but never a
/// long-term secret. The Android client must pin the fingerprint before it
/// sends the code.
/// </summary>
public sealed partial record NxPairingPayload(
    string Addr, string Fingerprint, string Code, long Expires, string Name, int Width, int Height)
{
    public const int Version = 1;
    public const string Kind = "nightdesk-nx";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void Validate()
    {
        if (!IPEndPoint.TryParse(Addr, out _))
            throw new ArgumentException("远控地址必须为 IP:端口。", nameof(Addr));
        if (Fingerprint.Length != 64 || !Fingerprint.All(Uri.IsHexDigit))
            throw new ArgumentException("主机证书指纹必须为 64 位十六进制。", nameof(Fingerprint));
        if (!CodePattern().IsMatch(Code))
            throw new ArgumentException("配对码必须为 6 位数字。", nameof(Code));
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Expires <= now) throw new ArgumentException("配对二维码已过期。", nameof(Expires));
        if (Name.Length > 64 || Name.Any(char.IsControl))
            throw new ArgumentException("主机名称无效。", nameof(Name));
        if (Width is < 1 or > 65535 || Height is < 1 or > 65535)
            throw new ArgumentException("主机分辨率无效。", nameof(Width));
    }

    public string ToJson()
    {
        Validate();
        return JsonSerializer.Serialize(new
        {
            v = Version,
            kind = Kind,
            addr = Addr,
            fingerprint = Fingerprint.ToLowerInvariant(),
            code = Code,
            expires = Expires,
            name = Name,
            width = Width,
            height = Height
        }, Json);
    }

    /// <summary>
    /// Turns the sidecar's bind address into an address a phone can dial.
    /// A loopback bind cannot be reached remotely; an unspecified bind is
    /// replaced with this PC's Tailscale IPv4 address.
    /// </summary>
    public static string ResolveAddress(string listen, string? tailscaleIPv4)
    {
        if (!IPEndPoint.TryParse(listen, out var endpoint))
            throw new ArgumentException("监听地址必须为 IP:端口。", nameof(listen));
        if (IPAddress.IsLoopback(endpoint.Address))
            throw new ArgumentException(
                "远控 sidecar 正在监听环回地址，手机无法连接。请先停止远控，点击“填入 Tailscale 地址”后重新启用。",
                nameof(listen));
        if (endpoint.Address.Equals(IPAddress.Any) || endpoint.Address.Equals(IPAddress.IPv6Any))
        {
            if (!TryParseTailscaleIPv4(tailscaleIPv4, out var address))
                throw new ArgumentException("无法取得本机 Tailscale IPv4 地址，不能生成可连接的二维码。", nameof(tailscaleIPv4));
            return new IPEndPoint(address, endpoint.Port).ToString();
        }
        return endpoint.ToString();
    }

    public static bool TryParseTailscaleIPv4(string? value, out IPAddress address)
    {
        address = IPAddress.None;
        if (!IPAddress.TryParse(value, out var parsed) || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;
        byte[] bytes = parsed.GetAddressBytes();
        // Tailscale uses the 100.64.0.0/10 carrier-grade NAT range.
        if (bytes[0] != 100 || bytes[1] < 64 || bytes[1] > 127) return false;
        address = parsed;
        return true;
    }

    public static string WithHost(string listen, string host)
    {
        if (!IPEndPoint.TryParse(listen, out var endpoint))
            throw new ArgumentException("监听地址必须为 IP:端口。", nameof(listen));
        if (!IPAddress.TryParse(host, out var address))
            throw new ArgumentException("主机地址无效。", nameof(host));
        return new IPEndPoint(address, endpoint.Port).ToString();
    }

    [GeneratedRegex("^[0-9]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
