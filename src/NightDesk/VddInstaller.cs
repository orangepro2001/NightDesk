using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NightDesk;

public sealed record VddInstallResult(bool Installed, string Message);

/// <summary>Locates, verifies and runs the bundled official Parsec VDD installer.</summary>
public static class VddInstaller
{
    public const string PayloadFileName = "parsec-vdd-0.45.0.0.exe";
    public const long PayloadSize = 517256;
    public const string PayloadSha256 = "e23332448fdaf5aa017cb308db5ef6855fac526a7ded05d80c039404126d5362";
    private const string ExpectedSigner = "Parsec Cloud, Inc.";

    public static bool IsDriverInstalled()
    {
        try
        {
            using var probe = new VddClient();
            probe.EnsureOpen();
            return true;
        }
        catch { return false; }
    }

    public static string DefaultCacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NightDesk", "driver");

    public static bool HasEmbeddedPayload => EmbeddedResourceName() is not null;

    /// <summary>Set after the first automatic start-up attempt so a declined UAC prompt is not repeated every launch.</summary>
    public static bool AutoInstallAttempted => File.Exists(Path.Combine(DefaultCacheDirectory, "auto-install.attempted"));

    public static void MarkAutoInstallAttempted()
    {
        try
        {
            Directory.CreateDirectory(DefaultCacheDirectory);
            File.WriteAllText(Path.Combine(DefaultCacheDirectory, "auto-install.attempted"), DateTimeOffset.UtcNow.ToString("O"));
        }
        catch { }
    }

    /// <summary>Extracted bundled installer, or a side-by-side override in &lt;app&gt;\vdd\.</summary>
    public static string? FindPayload(string? cacheDirectory = null)
    {
        string? extracted = ExtractEmbeddedPayload(cacheDirectory ?? DefaultCacheDirectory);
        if (extracted is not null) return extracted;
        string side = Path.Combine(AppContext.BaseDirectory, "vdd", PayloadFileName);
        return File.Exists(side) ? side : null;
    }

    /// <summary>Null when the payload is the pinned, correctly signed installer; otherwise the reason.</summary>
    public static string? ValidatePayload(string path)
    {
        if (!File.Exists(path)) return "找不到驱动安装器文件。";
        if (new FileInfo(path).Length != PayloadSize) return $"驱动安装器大小不符（{new FileInfo(path).Length} 字节）。";
        string actual;
        using (var stream = File.OpenRead(path)) actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (actual != PayloadSha256) return "驱动安装器 SHA-256 与内置值不符，已拒绝执行。";
        try
        {
            // The loader API has no replacement for reading the Authenticode certificate out of a signed PE file.
#pragma warning disable SYSLIB0057
            using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            if (!certificate.Subject.Contains(ExpectedSigner, StringComparison.OrdinalIgnoreCase))
                return "驱动安装器签名主体不是 Parsec Cloud, Inc.，已拒绝执行。";
        }
        catch (CryptographicException)
        {
            return "驱动安装器没有有效的数字签名，已拒绝执行。";
        }
        return null;
    }

    public static async Task<VddInstallResult> InstallAsync(TimeSpan? timeout = null, string? cacheDirectory = null)
    {
        string? payload = FindPayload(cacheDirectory);
        if (payload is null)
            return new(false, $"安装包内未携带 Parsec VDD 安装器；请将 {PayloadFileName} 放到程序目录的 vdd 文件夹后重试。");
        string? invalid = ValidatePayload(payload);
        if (invalid is not null) return new(false, invalid);
        if (IsDriverInstalled()) return new(true, "Parsec VDD 驱动已安装。");

        Process process;
        try
        {
            process = Process.Start(new ProcessStartInfo(payload, "/S") { UseShellExecute = true, Verb = "runas" })
                ?? throw new InvalidOperationException("无法启动驱动安装器。");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new(false, "已取消管理员授权，未安装驱动。");
        }

        using (process)
        {
            using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(5));
            try { await process.WaitForExitAsync(cancellation.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                return new(false, "驱动安装超时，已结束安装进程。");
            }
            if (process.ExitCode != 0) return new(false, $"驱动安装器返回错误码 {process.ExitCode}。");
        }

        for (int attempt = 0; attempt < 30; attempt++)
        {
            if (IsDriverInstalled()) return new(true, "Parsec VDD 驱动安装成功。");
            await Task.Delay(1000);
        }
        return new(false, "安装器已结束，但未检测到 Parsec VDD 设备。请重启电脑后重试。");
    }

    private static string? EmbeddedResourceName() =>
        typeof(VddInstaller).Assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                                    name.Contains("parsec-vdd", StringComparison.OrdinalIgnoreCase));

    private static string? ExtractEmbeddedPayload(string directory)
    {
        string? name = EmbeddedResourceName();
        if (name is null) return null;
        string target = Path.Combine(directory, PayloadFileName);
        try
        {
            Directory.CreateDirectory(directory);
            if (File.Exists(target) && ValidatePayload(target) is null) return target;
            using Stream? resource = typeof(VddInstaller).Assembly.GetManifestResourceStream(name);
            if (resource is null) return null;
            string pending = target + ".tmp";
            using (var output = File.Create(pending))
            {
                resource.CopyTo(output);
                output.Flush(true);
            }
            File.Move(pending, target, true);
            return target;
        }
        catch { return null; }
    }
}
