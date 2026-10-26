using System.Security.Cryptography;
using System.Text.Json;

namespace NightDesk;

public sealed record AndroidRelease(int SchemaVersion, string PackageName, long VersionCode, string VersionName,
    int MinSdk, long Size, string Sha256, string CertificateSha256, string ApkPath, string Notes);

// Each release has immutable bytes; publishing switches only the small manifest atomically.
public sealed class AndroidUpdateFeed
{
    public const long MaxApkSize = 100 * 1024 * 1024;
    private readonly string root;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public AndroidUpdateFeed(string? root = null) => this.root = root ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NightDesk", "updates", "android");

    public AndroidRelease? Read()
    {
        var path = Path.Combine(root, "manifest.json");
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("更新清单过大。");
        var release = JsonSerializer.Deserialize<AndroidRelease>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("更新清单无效。");
        if (release.SchemaVersion != 1 || release.PackageName != "dev.nightdesk.android" || release.VersionCode <= 0 ||
            string.IsNullOrWhiteSpace(release.VersionName) || release.VersionName.Length > 80 || release.MinSdk is < 29 or > 35 ||
            release.Size is <= 0 or > MaxApkSize || release.Sha256 is null || release.Sha256.Length != 64 ||
            !release.Sha256.All(Uri.IsHexDigit) || release.CertificateSha256 is null || release.CertificateSha256.Length != 64 ||
            !release.CertificateSha256.All(Uri.IsHexDigit) || release.ApkPath != $"/api/v1/android-apk?versionCode={release.VersionCode}" ||
            release.Notes is null || release.Notes.Length > 4096)
            throw new InvalidDataException("更新清单字段无效。");
        return release;
    }

    public FileStream OpenVerified(AndroidRelease release)
    {
        var path = Path.Combine(root, $"nightdesk-{release.VersionCode}.apk");
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (stream.Length != release.Size || !Convert.ToHexString(SHA256.HashData(stream)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新 APK 与清单不一致。");
            stream.Position = 0;
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
}
