using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NightDesk;

public sealed class WindowsUpdateRelease
{
    public int SchemaVersion { get; init; }
    public string PackageName { get; init; } = "";
    public string Version { get; init; } = "";
    public int VersionCode { get; init; }
    public string Arch { get; init; } = "";
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";
    public string PackagePath { get; init; } = "";
    public string Notes { get; init; } = "";

    public void Validate()
    {
        if (SchemaVersion != 1 || PackageName != DesktopUpdateService.PackageName ||
            !System.Version.TryParse(Version, out _) || VersionCode <= 0 || Arch != "win-x64" ||
            Size is <= 0 or > DesktopUpdateService.MaxPackageSize ||
            Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit) ||
            PackagePath != DesktopUpdateService.LatestPackagePath || Notes.Length > 4096)
            throw new InvalidDataException("Windows 更新清单字段无效。");
    }
}

public sealed class WindowsPackageFile
{
    public string Name { get; init; } = "";
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";
}

public sealed class WindowsPackageMetadata
{
    public int SchemaVersion { get; init; }
    public string PackageName { get; init; } = "";
    public string Version { get; init; } = "";
    public int VersionCode { get; init; }
    public List<WindowsPackageFile> Files { get; init; } = new();

    public void Validate(WindowsUpdateRelease release)
    {
        if (SchemaVersion != 1 || PackageName != release.PackageName || Version != release.Version ||
            VersionCode != release.VersionCode)
            throw new InvalidDataException("Windows 包内元数据与更新清单不一致。");
        string[] expected = { "NightDesk.exe", "nx-host.exe" };
        if (Files.Count != expected.Length || expected.Any(name => Files.Count(f => f.Name == name) != 1))
            throw new InvalidDataException("Windows 更新包必须恰好包含 NightDesk.exe 和 nx-host.exe。");
        foreach (var file in Files)
        {
            if (file.Size is <= 0 or > DesktopUpdateService.MaxPackageSize || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Windows 包内文件校验字段无效。");
        }
    }
}

public sealed record WindowsUpdateReady(WindowsUpdateRelease Release, string Directory);

public sealed class DesktopUpdateService : IDisposable
{
    public const string PackageName = "dev.nightdesk.windows";
    public const long MaxPackageSize = 300L * 1024 * 1024;
    public const string UpdateUrl = "https://tony-ai-pc-1.tail9f7f1f.ts.net/nightdesk/api/v1/windows-update";
    public const string BaseOrigin = "https://tony-ai-pc-1.tail9f7f1f.ts.net";
    public const string LatestPackagePath = "/nightdesk/windows/latest.zip";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string root;
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly Version currentVersion;

    public DesktopUpdateService(string? root = null, HttpClient? client = null, Version? currentVersion = null)
    {
        this.root = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NightDesk", "updates", "windows"));
        http = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        ownsHttp = client is null;
        this.currentVersion = currentVersion ?? ParseCurrentVersion();
        Directory.CreateDirectory(this.root);
    }

    public static Version ParseCurrentVersion()
    {
        string? informational = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        string candidate = (informational ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0").Split('+')[0];
        return System.Version.TryParse(candidate, out var version) ? version : new Version(0, 0, 0);
    }

    public Version CurrentVersion => currentVersion;
    public string CurrentVersionText => currentVersion.ToString(3);
    public string ReadyDirectory => Path.Combine(root, "ready");

    public async Task<WindowsUpdateRelease?> CheckAndDownloadAsync(IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var response = await http.GetAsync(UpdateUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 16384) throw new InvalidDataException("Windows 更新清单过大。");
        string body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        if (body.Length > 16384) throw new InvalidDataException("Windows 更新清单过大。");
        WindowsUpdateRelease release = JsonSerializer.Deserialize<WindowsUpdateRelease>(body, Json)
            ?? throw new InvalidDataException("Windows 更新清单无有效内容。");
        release.Validate();
        if (!System.Version.TryParse(release.Version, out var available) || available <= currentVersion)
        {
            ClearPending();
            return null;
        }

        WindowsUpdateReady? ready = Ready();
        if (ready is not null && ready.Release.VersionCode == release.VersionCode &&
            ready.Release.Sha256.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            return ready.Release;

        ClearPending();
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        Directory.CreateDirectory(root);
        string package = Path.Combine(root, "package.tmp");
        try
        {
            Uri uri = new(new Uri(BaseOrigin), release.PackagePath);
            using var packageResponse = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            packageResponse.EnsureSuccessStatusCode();
            long? contentLength = packageResponse.Content.Headers.ContentLength;
            if (contentLength is > 0 && contentLength != release.Size)
                throw new InvalidDataException("Windows 更新包长度与清单不一致。");
            await using (var input = await packageResponse.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var output = new FileStream(package, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[1024 * 1024];
                long total = 0;
                while (true)
                {
                    int read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    total += read;
                    if (total > MaxPackageSize || total > release.Size) throw new InvalidDataException("Windows 更新包超过限制。");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                    progress?.Report(total * 100d / release.Size);
                }
                if (total != release.Size || !Convert.ToHexString(hash.GetHashAndReset())
                        .Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Windows 更新包大小或 SHA-256 与清单不一致。");
            }
            await Task.Run(() => ExtractVerified(package, release), timeout.Token).ConfigureAwait(false);
            progress?.Report(100);
            return release;
        }
        finally
        {
            TryDelete(package);
        }
    }

    public WindowsUpdateReady? Ready()
    {
        string manifestPath = Path.Combine(ReadyDirectory, "release.json");
        if (!File.Exists(manifestPath)) return null;
        try
        {
            WindowsUpdateRelease release = JsonSerializer.Deserialize<WindowsUpdateRelease>(
                File.ReadAllText(manifestPath, Encoding.UTF8), Json) ?? throw new InvalidDataException("待安装更新清单无效。");
            release.Validate();
            if (System.Version.TryParse(release.Version, out var version) && version <= currentVersion)
            {
                ClearPending();
                return null;
            }
            foreach (string name in new[] { "NightDesk.exe", "nx-host.exe" })
                if (!File.Exists(Path.Combine(ReadyDirectory, name))) return null;
            return new WindowsUpdateReady(release, ReadyDirectory);
        }
        catch
        {
            ClearPending();
            return null;
        }
    }

    public async Task<Process> PrepareInstallAsync(CancellationToken cancellationToken = default)
    {
        WindowsUpdateReady ready = Ready() ?? throw new InvalidOperationException("没有已验证的待安装桌面包。");
        string target = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(target)) throw new InvalidOperationException("安装目录不存在。");
        string writeProbe = Path.Combine(target, ".nightdesk-update-write-test-" + Guid.NewGuid().ToString("N"));
        try { await File.WriteAllTextAsync(writeProbe, "ok", cancellationToken).ConfigureAwait(false); }
        finally { TryDelete(writeProbe); }

        await VerifyReadyFilesAsync(ready, cancellationToken).ConfigureAwait(false);
        string script = Path.Combine(root, "apply-update.ps1");
        await File.WriteAllTextAsync(script, InstallScript, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (string argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-ExecutionPolicy", "Bypass",
                     "-File", script,
                     "-TargetProcessId", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "-SourceDirectory", ready.Directory,
                     "-TargetDirectory", target
                 })
            start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("无法启动桌面更新安装器。");
    }

    private async Task VerifyReadyFilesAsync(WindowsUpdateReady ready, CancellationToken cancellationToken)
    {
        WindowsPackageMetadata metadata = JsonSerializer.Deserialize<WindowsPackageMetadata>(
            await File.ReadAllTextAsync(Path.Combine(ready.Directory, "package.json"), cancellationToken).ConfigureAwait(false), Json)
            ?? throw new InvalidDataException("待安装包元数据无效。");
        metadata.Validate(ready.Release);
        foreach (WindowsPackageFile file in metadata.Files)
        {
            string path = Path.Combine(ready.Directory, file.Name);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Size)
                throw new InvalidDataException("待安装文件大小不一致：" + file.Name);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            if (!actual.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("待安装文件 SHA-256 不一致：" + file.Name);
        }
    }

    private void ExtractVerified(string package, WindowsUpdateRelease release)
    {
        string staging = Path.Combine(root, "staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var archive = ZipFile.OpenRead(package);
            if (archive.Entries.Count != 3) throw new InvalidDataException("Windows 更新包条目数量不正确。");
            ZipArchiveEntry metadataEntry = archive.GetEntry("release.json")
                ?? throw new InvalidDataException("Windows 更新包缺少 release.json。");
            if (metadataEntry.Length is <= 0 or > 16384) throw new InvalidDataException("Windows 包内元数据大小无效。");
            using (var reader = new StreamReader(metadataEntry.Open(), Encoding.UTF8))
            {
                WindowsPackageMetadata metadata = JsonSerializer.Deserialize<WindowsPackageMetadata>(reader.ReadToEnd(), Json)
                    ?? throw new InvalidDataException("Windows 包内元数据无效。");
                metadata.Validate(release);
                foreach (WindowsPackageFile file in metadata.Files)
                {
                    ZipArchiveEntry entry = archive.GetEntry(file.Name)
                        ?? throw new InvalidDataException("Windows 更新包缺少 " + file.Name);
                    if (entry.Length != file.Size) throw new InvalidDataException("Windows 包内文件大小不一致：" + file.Name);
                    string destination = Path.Combine(staging, file.Name);
                    using var input = entry.Open();
                    using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    byte[] buffer = new byte[1024 * 1024];
                    while (true)
                    {
                        int read = input.Read(buffer, 0, buffer.Length);
                        if (read == 0) break;
                        hash.AppendData(buffer, 0, read);
                        output.Write(buffer, 0, read);
                    }
                    if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Windows 包内文件 SHA-256 不一致：" + file.Name);
                }
                File.WriteAllText(Path.Combine(staging, "package.json"), JsonSerializer.Serialize(metadata, Json), new UTF8Encoding(false));
            }
            File.WriteAllText(Path.Combine(staging, "release.json"), JsonSerializer.Serialize(release, Json), new UTF8Encoding(false));
            DeleteChildDirectory(ReadyDirectory);
            Directory.Move(staging, ReadyDirectory);
            staging = "";
        }
        finally
        {
            if (staging.Length > 0) DeleteChildDirectory(staging);
        }
    }

    private void ClearPending()
    {
        DeleteChildDirectory(ReadyDirectory);
    }

    private void DeleteChildDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        string parent = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("更新缓存路径超出预期目录。");
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public void Dispose() { if (ownsHttp) http.Dispose(); }

    private const string InstallScript = """
param(
    [Parameter(Mandatory=$true)][int]$TargetProcessId,
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [Parameter(Mandatory=$true)][string]$TargetDirectory
)
$ErrorActionPreference = 'Stop'
$logDirectory = Join-Path $env:LOCALAPPDATA 'NightDesk\updates\windows'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$log = Join-Path $logDirectory 'install.log'
function Write-Log([string]$message) {
    Add-Content -LiteralPath $log -Value ("{0:o} {1}" -f [DateTimeOffset]::Now, $message)
}
try {
    Write-Log "waiting for NightDesk pid $TargetProcessId"
    for ($i = 0; $i -lt 120; $i++) {
        if (-not (Get-Process -Id $TargetProcessId -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 500
    }
    if (Get-Process -Id $TargetProcessId -ErrorAction SilentlyContinue) {
        throw "NightDesk did not exit before update timeout"
    }
    $package = Get-Content -LiteralPath (Join-Path $SourceDirectory 'package.json') -Raw | ConvertFrom-Json
    foreach ($name in @('NightDesk.exe', 'nx-host.exe')) {
        $entry = @($package.files | Where-Object { $_.name -eq $name })
        if ($entry.Count -ne 1) { throw "Missing package metadata for $name" }
        $file = Get-Item -LiteralPath (Join-Path $SourceDirectory $name)
        if ($file.Length -ne $entry[0].size -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $entry[0].sha256) {
            throw "Staged file verification failed: $name"
        }
    }
    foreach ($name in @('NightDesk.exe', 'nx-host.exe')) {
        $source = Join-Path $SourceDirectory $name
        $target = Join-Path $TargetDirectory $name
        $newFile = Join-Path $TargetDirectory ($name + '.new')
        $backup = Join-Path $TargetDirectory ($name + '.bak-update')
        Copy-Item -LiteralPath $source -Destination $newFile -Force
        if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination $backup -Force }
    }
    try {
        foreach ($name in @('NightDesk.exe', 'nx-host.exe')) {
            Move-Item -LiteralPath (Join-Path $TargetDirectory ($name + '.new')) -Destination (Join-Path $TargetDirectory $name) -Force
        }
        Write-Log 'files replaced'
    } catch {
        Write-Log ("replace failed: " + $_.Exception.Message)
        foreach ($name in @('NightDesk.exe', 'nx-host.exe')) {
            $backup = Join-Path $TargetDirectory ($name + '.bak-update')
            if (Test-Path -LiteralPath $backup) { Move-Item -LiteralPath $backup -Destination (Join-Path $TargetDirectory $name) -Force }
        }
        throw
    }
    Start-Process -FilePath (Join-Path $TargetDirectory 'NightDesk.exe') -WorkingDirectory $TargetDirectory
    Write-Log 'restarted'
} catch {
    Write-Log ("failed: " + $_.Exception.Message)
    exit 1
}
""";
}
