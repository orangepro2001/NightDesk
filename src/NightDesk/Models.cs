namespace NightDesk;

public sealed record DisplayMode(int Width, int Height, int RefreshRate)
{
    public DisplayMode Validate()
    {
        if (Width is < 640 or > 7680 || Height is < 480 or > 4320 || RefreshRate is < 24 or > 360)
            throw new ArgumentException("分辨率或刷新率超出安全范围。");
        return this;
    }

    public override string ToString() => $"{Width} × {Height} @ {RefreshRate} Hz";
}

public sealed record MonitorInfo(string DeviceName, string Description, string DeviceId, bool Active, bool IsParsec, bool IsHardware);
public sealed record VirtualDisplayInfo(int Index, string DeviceName, string Mode);
public sealed record SavedMonitor(string DeviceName, int Width, int Height, int BitsPerPixel, int RefreshRate, int X, int Y, int Orientation, bool Primary);
public sealed record RecoverySnapshot(DateTime CreatedUtc, byte[] Paths, byte[] Modes);
public sealed record RemoteCommand(string Name, int? Width = null, int? Height = null, int? RefreshRate = null, int? Index = null, bool? Enabled = null);

public sealed class AppSettings
{
    public string EncryptedToken { get; set; } = "";
    public string Hotkey { get; set; } = "Ctrl+Alt+F10";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int RefreshRate { get; set; } = 60;
    public bool PreventSleep { get; set; }
    public bool RemoteEnabled { get; set; }
    public string ServeFingerprint { get; set; } = "";
}

public static class Security
{
    public static string NewToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string? ReadBearerToken(string? authorization)
    {
        const string prefix = "Bearer ";
        if (authorization is null || !authorization.StartsWith(prefix, StringComparison.Ordinal)) return null;
        string token = authorization[prefix.Length..];
        if (token.Length is < 32 or > 256 || token.Any(char.IsWhiteSpace)) return null;
        return token;
    }

    public static bool HasToken(string? authorization, string token)
    {
        string? supplied = ReadBearerToken(authorization);
        if (supplied is null || supplied.Length != token.Length) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(supplied), System.Text.Encoding.ASCII.GetBytes(token));
    }
}

