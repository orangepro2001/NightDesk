using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace NightDesk;

/// <summary>A validated go-nx target such as 100.64.0.5:7000 or host.example:7000.</summary>
public sealed record NxViewerTarget(string Host, int Port)
{
    public const int DefaultPort = 7000;

    public string Address => Host.Contains(':', StringComparison.Ordinal) ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
    public string PinKey => Address.ToLowerInvariant();

    public static NxViewerTarget Parse(string? value)
    {
        string text = (value ?? "").Trim();
        if (text.Length == 0) throw new ArgumentException("请输入目标桌面地址，例如 100.x.y.z:7000。", nameof(value));
        if (text.Length > 255 || text.Any(char.IsControl) || text.Any(char.IsWhiteSpace))
            throw new ArgumentException("目标地址包含无效字符。", nameof(value));
        if (text.Contains('/', StringComparison.Ordinal) || text.Contains('\\', StringComparison.Ordinal) ||
            text.Contains('@', StringComparison.Ordinal) || text.Contains('?', StringComparison.Ordinal) ||
            text.Contains('#', StringComparison.Ordinal))
            throw new ArgumentException("目标地址只能填写主机名或 IP 与 UDP 端口。", nameof(value));

        string host;
        int port = DefaultPort;
        if (text[0] == '[')
        {
            int end = text.IndexOf(']');
            if (end < 2 || end + 1 >= text.Length || text[end + 1] != ':')
                throw new ArgumentException("IPv6 地址必须写成 [地址]:端口。", nameof(value));
            host = text[1..end];
            port = ParsePort(text[(end + 2)..]);
        }
        else
        {
            int first = text.IndexOf(':');
            int last = text.LastIndexOf(':');
            if (first >= 0 && first != last)
                throw new ArgumentException("IPv6 地址必须用方括号包围，例如 [fd00::1]:7000。", nameof(value));
            if (last >= 0)
            {
                host = text[..last];
                port = ParsePort(text[(last + 1)..]);
            }
            else host = text;
        }

        if (host.Length == 0 || host.Length > 253)
            throw new ArgumentException("目标主机名为空或过长。", nameof(value));
        if (IPAddress.TryParse(host, out _))
            return new NxViewerTarget(host, port);
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new ArgumentException("目标主机名无效。", nameof(value));
        return new NxViewerTarget(host.ToLowerInvariant(), port);
    }

    public async Task<IPEndPoint> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (IPAddress.TryParse(Host, out var address)) return new IPEndPoint(address, Port);
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(Host, cancellationToken).ConfigureAwait(false);
        IPAddress? selected = addresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault();
        return selected is null
            ? throw new SocketException((int)SocketError.HostNotFound)
            : new IPEndPoint(selected, Port);
    }

    private static int ParsePort(string value)
    {
        if (!int.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
            throw new ArgumentException("目标 UDP 端口必须在 1–65535 之间。");
        return port;
    }
}

public static class NxViewerSecurity
{
    public static string NormalizeFingerprint(string? value)
    {
        string text = (value ?? "").Trim().ToLowerInvariant()
            .Replace(":", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
        if (text.Length == 0) return "";
        if (text.Length != 64 || !text.All(Uri.IsHexDigit))
            throw new ArgumentException("主机证书指纹必须为 64 位十六进制字符。");
        return text;
    }

    public static string ComputeFingerprint(X509Certificate? certificate)
    {
        if (certificate is null) throw new ArgumentNullException(nameof(certificate));
        return Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())).ToLowerInvariant();
    }
}

public sealed record NxViewerRect(int X, int Y, int Width, int Height);

/// <summary>Lossless go-nx tile decoder. Input and output pixels are BGRA.</summary>
public static class NxViewerTileDecoder
{
    public const int TileSize = 64;
    private const byte TypeSolid = 0;
    private const byte TypePalette = 1;
    private const byte TypeRaw = 2;

    public static IReadOnlyList<NxViewerRect> Apply(byte[] frame, int width, int height, ReadOnlySpan<byte> data)
    {
        if (width is < 1 or > 16384 || height is < 1 or > 16384)
            throw new InvalidDataException("远控画面尺寸无效。");
        if (frame.Length != width * height * 4)
            throw new ArgumentException("画面缓冲区大小不匹配。", nameof(frame));

        var dirty = new List<NxViewerRect>();
        int offset = 0;
        while (offset < data.Length)
        {
            if (data.Length - offset < 9) throw new InvalidDataException("瓦片头被截断。");
            int tx = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            int ty = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 2, 2));
            byte type = data[offset + 4];
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 5, 4)));
            offset += 9;
            if (length < 0 || data.Length - offset < length) throw new InvalidDataException("瓦片内容被截断。");
            ReadOnlySpan<byte> payload = data.Slice(offset, length);
            offset += length;

            int x = tx * TileSize;
            int y = ty * TileSize;
            if (x >= width || y >= height) throw new InvalidDataException("瓦片索引越界。");
            int tw = Math.Min(TileSize, width - x);
            int th = Math.Min(TileSize, height - y);
            byte[] pixels = Decode(type, payload, tw * th * 4);
            for (int row = 0; row < th; row++)
            {
                int target = ((y + row) * width + x) * 4;
                pixels.AsSpan(row * tw * 4, tw * 4).CopyTo(frame.AsSpan(target, tw * 4));
            }
            dirty.Add(new NxViewerRect(x, y, tw, th));
        }
        return dirty;
    }

    private static byte[] Decode(byte type, ReadOnlySpan<byte> payload, int size)
    {
        switch (type)
        {
            case TypeSolid:
                if (payload.Length != 4) throw new InvalidDataException("纯色瓦片长度无效。");
                var solid = new byte[size];
                for (int i = 0; i < size; i += 4) payload.CopyTo(solid.AsSpan(i, 4));
                return solid;
            case TypePalette:
                return DecodePalette(payload, size);
            case TypeRaw:
                return DecodeRaw(payload, size);
            default:
                throw new InvalidDataException("未知的瓦片编码。");
        }
    }

    private static byte[] DecodePalette(ReadOnlySpan<byte> payload, int size)
    {
        if (payload.Length < 1) throw new InvalidDataException("调色板瓦片为空。");
        int colors = payload[0] + 1;
        int paletteBytes = colors * 4;
        if (payload.Length < 1 + paletteBytes || (payload.Length - 1 - paletteBytes) % 2 != 0)
            throw new InvalidDataException("调色板瓦片长度无效。");
        ReadOnlySpan<byte> palette = payload.Slice(1, paletteBytes);
        ReadOnlySpan<byte> runs = payload[(1 + paletteBytes)..];
        var pixels = new byte[size];
        int at = 0;
        for (int i = 0; i < runs.Length; i += 2)
        {
            int index = runs[i + 1];
            if (index >= colors) throw new InvalidDataException("调色板索引越界。");
            int count = runs[i] + 1;
            for (int n = 0; n < count; n++)
            {
                if (at + 4 > pixels.Length) throw new InvalidDataException("调色板像素过多。");
                palette.Slice(index * 4, 4).CopyTo(pixels.AsSpan(at, 4));
                at += 4;
            }
        }
        if (at != pixels.Length) throw new InvalidDataException("调色板像素数量不匹配。");
        return pixels;
    }

    private static byte[] DecodeRaw(ReadOnlySpan<byte> payload, int size)
    {
        using var input = new MemoryStream(payload.ToArray(), writable: false);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        var pixels = new byte[size];
        deflate.ReadExactly(pixels);
        if (deflate.ReadByte() != -1) throw new InvalidDataException("原始瓦片解压后过长。");
        return pixels;
    }
}

public static class NxViewerWire
{
    public const byte MsgHello = 1;
    public const byte MsgAck = 2;
    public const byte MsgInput = 3;
    public const byte MsgText = 4;
    public const byte MsgReceipt = 0x81;
    public const int MaxTextBytes = 1024;

    public static byte[] Hello(string password)
    {
        byte[] secret = Encoding.UTF8.GetBytes(password ?? "");
        if (secret.Length > 255) throw new ArgumentException("配对码过长。", nameof(password));
        var packet = new byte[2 + secret.Length];
        packet[0] = MsgHello;
        packet[1] = (byte)secret.Length;
        secret.CopyTo(packet, 2);
        return packet;
    }

    public static byte[] Ack(uint sequence)
    {
        var packet = new byte[5];
        packet[0] = MsgAck;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(1), sequence);
        return packet;
    }

    public static byte[] Input(ulong operation, byte kind, int x, int y, int argument)
    {
        var packet = new byte[22];
        packet[0] = MsgInput;
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(1), operation);
        packet[9] = kind;
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(10), x);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(14), y);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(18), argument);
        return packet;
    }

    public static byte[] Text(ulong operation, string text)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text ?? "");
        if (utf8.Length is < 1 or > MaxTextBytes) throw new ArgumentException("单次文本输入必须为 1–1024 字节。", nameof(text));
        var packet = new byte[11 + utf8.Length];
        packet[0] = MsgText;
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(1), operation);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(9), (ushort)utf8.Length);
        utf8.CopyTo(packet, 11);
        return packet;
    }

    public static bool TryReadReceipt(ReadOnlySpan<byte> packet, out ulong operation, out byte status)
    {
        operation = 0;
        status = 0;
        if (packet.Length != 10 || packet[0] != MsgReceipt) return false;
        operation = BinaryPrimitives.ReadUInt64LittleEndian(packet.Slice(1, 8));
        status = packet[9];
        return true;
    }
}

public static class NxViewerInputKind
{
    public const byte MouseMove = 1;
    public const byte MouseDown = 2;
    public const byte MouseUp = 3;
    public const byte Wheel = 4;
    public const byte KeyDown = 5;
    public const byte KeyUp = 6;
    public const byte WheelH = 7;
}

public static class NxViewerIdentity
{
    private static readonly object Gate = new();

    public static X509Certificate2 LoadOrCreate(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("缺少 Viewer 身份目录。", nameof(directory));
        string path = Path.Combine(directory, "identity.dpapi");
        lock (Gate)
        {
            Directory.CreateDirectory(directory);
            byte[] pfx;
            if (File.Exists(path))
            {
                string protectedText = File.ReadAllText(path, Encoding.ASCII).Trim();
                pfx = Convert.FromBase64String(Dpapi.Unprotect(protectedText));
            }
            else
            {
                pfx = CreatePfx();
                File.WriteAllText(path, Dpapi.Protect(Convert.ToBase64String(pfx)), Encoding.ASCII);
            }
            return X509CertificateLoader.LoadPkcs12(pfx, null,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }
    }

    private static byte[] CreatePfx()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=NightDesk Viewer " + Environment.MachineName,
            key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
        {
            new Oid("1.3.6.1.5.5.7.3.2"),
            new Oid("1.3.6.1.5.5.7.3.1")
        }, false));
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddYears(10));
        return certificate.Export(X509ContentType.Pfx);
    }
}

public sealed class NxViewerPinStore
{
    private readonly string path;
    private readonly Dictionary<string, string> pins;
    private readonly JsonSerializerOptions json = new() { WriteIndented = true };

    private NxViewerPinStore(string path, Dictionary<string, string> pins)
    {
        this.path = path;
        this.pins = pins;
    }

    public static NxViewerPinStore Load(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "hosts.json");
        if (!File.Exists(path)) return new NxViewerPinStore(path, new Dictionary<string, string>(StringComparer.Ordinal));
        var pins = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8))
            ?? throw new InvalidDataException("Viewer 主机指纹文件无效。");
        return new NxViewerPinStore(path, new Dictionary<string, string>(pins, StringComparer.Ordinal));
    }

    public string? Get(NxViewerTarget target) =>
        pins.TryGetValue(target.PinKey, out var fingerprint) ? fingerprint : null;

    public void Set(NxViewerTarget target, string fingerprint)
    {
        pins[target.PinKey] = NxViewerSecurity.NormalizeFingerprint(fingerprint);
        Save();
    }

    public bool Remove(NxViewerTarget target)
    {
        bool removed = pins.Remove(target.PinKey);
        if (removed) Save();
        return removed;
    }

    private void Save()
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(pins, json), new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }
}