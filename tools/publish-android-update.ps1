<#
.SYNOPSIS
  发布一个已签名的 NightDesk Android APK，供桌面端 /api/v1/android-update 接口分发。

.DESCRIPTION
  1. 校验 APK 存在、大小在 100 MiB 以内。
  2. 把 APK 复制成按其 versionCode 命名的不可变文件。
  3. 计算大小与 SHA-256，写出版本清单。
  4. 以原子方式替换 manifest.json，使正在运行的桌面端立即看到新版本。

  已发布的旧版本 APK 不会被删除，便于回旋。
  本脚本不接触签名私钥，仅记录 APK 的签名证书指纹（需调用方提供或从 APK 中读取）。

.EXAMPLE
  ./publish-android-update.ps1 -ApkPath ..\artifacts\android\nightdesk-1.0.1.apk -VersionCode 2 -VersionName 1.0.1 -CertificateSha256 41d3... -MinSdk 29 -Notes "首个自动更新验证版本"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ApkPath,
    [Parameter(Mandatory = $true)][ValidateRange(1, [int]::MaxValue)][int]$VersionCode,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$VersionName,
    [Parameter(Mandatory = $false)][ValidateRange(29, 35)][int]$MinSdk = 29,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$CertificateSha256,
    [Parameter(Mandatory = $false)][string]$Notes = "",
    [Parameter(Mandatory = $false)][string]$PackageName = "dev.nightdesk.android",
    [Parameter(Mandatory = $false)][string]$Destination
)

$ErrorActionPreference = "Stop"
$MaxApkSize = 100MB

if (-not (Test-Path -LiteralPath $ApkPath -PathType Leaf)) { throw "APK 不存在：$ApkPath" }
$apk = Get-Item -LiteralPath $ApkPath
if ($apk.Length -le 0) { throw "APK 为空：$($apk.FullName)" }
if ($apk.Length -gt $MaxApkSize) { throw "APK 超过 100 MiB 限制：$($apk.Length) 字节" }

if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $env:LOCALAPPDATA "NightDesk\updates\android"
}
if (-not (Test-Path -LiteralPath $Destination)) { New-Item -ItemType Directory -Path $Destination -Force | Out-Null }
$Destination = (Resolve-Path -LiteralPath $Destination).Path

$sha256 = (Get-FileHash -LiteralPath $apk.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$targetApk = Join-Path $Destination "nightdesk-$VersionCode.apk"
$manifestPath = Join-Path $Destination "manifest.json"
$tempApk = "$targetApk.tmp"
$tempManifest = "$manifestPath.tmp"

# 先落盘 APK，再原子切换 manifest；切换失败时旧清单仍然有效。
Copy-Item -LiteralPath $apk.FullName -Destination $tempApk -Force
Move-Item -LiteralPath $tempApk -Destination $targetApk -Force

$release = [ordered]@{
    schemaVersion     = 1
    packageName       = $PackageName
    versionCode       = $VersionCode
    versionName       = $VersionName
    minSdk            = $MinSdk
    size              = [long]$apk.Length
    sha256            = $sha256
    certificateSha256 = $CertificateSha256.ToLowerInvariant()
    apkPath           = "/api/v1/android-apk?versionCode=$VersionCode"
    notes             = $Notes
}
$release | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $tempManifest -Encoding utf8NoBOM

try {
    Move-Item -LiteralPath $tempManifest -Destination $manifestPath -Force
}
catch {
    Remove-Item -LiteralPath $tempManifest -ErrorAction SilentlyContinue
    throw
}

[pscustomobject]@{
    PackageName       = $PackageName
    VersionCode       = $VersionCode
    VersionName       = $VersionName
    MinSdk            = $MinSdk
    Size              = [long]$apk.Length
    Sha256            = $sha256
    CertificateSha256 = $CertificateSha256.ToLowerInvariant()
    Apk               = $targetApk
    Manifest          = $manifestPath
}
