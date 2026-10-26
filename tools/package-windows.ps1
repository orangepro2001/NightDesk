# Creates the immutable Windows update ZIP consumed by the AI feed.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][int]$VersionCode,
    [string]$SourceDir,
    [string]$OutZip
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be x.y.z' }
if ($VersionCode -le 0) { throw 'VersionCode must be positive' }
if (-not $SourceDir) { $SourceDir = Join-Path $root "artifacts\win-x64-$Version" }
$SourceDir = [IO.Path]::GetFullPath($SourceDir)
if (-not (Test-Path -LiteralPath $SourceDir)) { throw "Source directory not found: $SourceDir" }
if (-not $OutZip) { $OutZip = Join-Path $root "artifacts\windows-packages\nightdesk-windows-$Version.zip" }
$OutZip = [IO.Path]::GetFullPath($OutZip)
$outDir = Split-Path -Parent $OutZip
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$required = @('NightDesk.exe', 'nx-host.exe')
foreach ($name in $required) {
    $path = Join-Path $SourceDir $name
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing $path" }
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$staging = Join-Path ([IO.Path]::GetTempPath()) ("nightdesk-windows-package-" + [guid]::NewGuid().ToString('N'))
$stagingFull = [IO.Path]::GetFullPath($staging)
if (-not $stagingFull.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging path' }
New-Item -ItemType Directory -Path $stagingFull | Out-Null
try {
    foreach ($name in $required) {
        Copy-Item -LiteralPath (Join-Path $SourceDir $name) -Destination (Join-Path $stagingFull $name)
    }
    $files = foreach ($name in $required) {
        $item = Get-Item -LiteralPath (Join-Path $stagingFull $name)
        [ordered]@{
            name = $name
            size = $item.Length
            sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $metadata = [ordered]@{
        schemaVersion = 1
        packageName = 'dev.nightdesk.windows'
        version = $Version
        versionCode = $VersionCode
        files = @($files)
    }
    [IO.File]::WriteAllText((Join-Path $stagingFull 'release.json'), ($metadata | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $OutZip) { Remove-Item -LiteralPath $OutZip -Force }
    Compress-Archive -LiteralPath @(
        (Join-Path $stagingFull 'release.json'),
        (Join-Path $stagingFull 'NightDesk.exe'),
        (Join-Path $stagingFull 'nx-host.exe')
    ) -DestinationPath $OutZip -CompressionLevel Optimal
    $item = Get-Item -LiteralPath $OutZip
    '{0} {1} bytes SHA-256 {2}' -f $item.FullName, $item.Length, (Get-FileHash -LiteralPath $OutZip -Algorithm SHA256).Hash
}
finally {
    if (Test-Path -LiteralPath $stagingFull) { Remove-Item -LiteralPath $stagingFull -Recurse -Force }
}
