# Publishes the Windows desktop build and verifies the nx-host sidecar.
# The sidecar must sit next to NightDesk.exe: it is deliberately excluded from
# the single-file bundle so the WPF process can launch it as a child process.
[CmdletBinding()]
param(
    [string]$Dotnet = 'C:\Users\pixel\scoop\apps\dotnet-sdk\10.0.402\dotnet.exe',
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\NightDesk\NightDesk.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw "Project not found: $project" }

[xml]$xml = Get-Content -LiteralPath $project
$version = @($xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) { throw 'Cannot read <Version> from NightDesk.csproj' }
if (-not $OutDir) { $OutDir = Join-Path $root "artifacts\win-x64-$version" }
$OutDir = [IO.Path]::GetFullPath($OutDir)

# Publish to a fresh staging directory so a stale nx-host.exe in OutDir can
# never hide a regression where publish stops copying the sidecar.
$staging = Join-Path ([IO.Path]::GetTempPath()) ("nightdesk-publish-" + [guid]::NewGuid().ToString('N'))
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$stagingFull = [IO.Path]::GetFullPath($staging)
if (-not $stagingFull.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe staging path: $stagingFull"
}

try {
    & $Dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $staging
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    $stagedExe = Join-Path $staging 'NightDesk.exe'
    $stagedSidecar = Join-Path $staging 'nx-host.exe'
    foreach ($file in @($stagedExe, $stagedSidecar)) {
        if (-not (Test-Path -LiteralPath $file)) {
            throw "Missing publish output: $file (nx-host.exe must NOT be bundled into the single-file EXE)"
        }
    }

    $sidecarVersion = & $stagedSidecar -version
    if ($LASTEXITCODE -ne 0 -or $sidecarVersion -notmatch '^nx-host ') {
        throw "nx-host.exe failed its -version smoke check: $sidecarVersion"
    }

    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
    foreach ($name in @('NightDesk.exe', 'nx-host.exe')) {
        Copy-Item -LiteralPath (Join-Path $staging $name) -Destination (Join-Path $OutDir $name) -Force
    }
    $stagedPdb = Join-Path $staging 'NightDesk.pdb'
    if (Test-Path -LiteralPath $stagedPdb) {
        Copy-Item -LiteralPath $stagedPdb -Destination (Join-Path $OutDir 'NightDesk.pdb') -Force
    }

    $exe = Join-Path $OutDir 'NightDesk.exe'
    $sidecar = Join-Path $OutDir 'nx-host.exe'
    foreach ($file in @($exe, $sidecar)) {
        if (-not (Test-Path -LiteralPath $file)) { throw "Missing published file: $file" }
        $item = Get-Item -LiteralPath $file
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        '{0}  {1} bytes  SHA-256 {2}' -f $item.Name, $item.Length, $hash
    }
    'ProductVersion: {0}' -f (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
    'nx-host version: {0}' -f $sidecarVersion
    'Published to: {0}' -f $OutDir
    'Deploy NightDesk.exe together with nx-host.exe from this directory.'
}
finally {
    if (Test-Path -LiteralPath $stagingFull) {
        Remove-Item -LiteralPath $stagingFull -Recurse -Force
    }
}
