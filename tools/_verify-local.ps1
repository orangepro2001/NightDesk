$ErrorActionPreference = "Stop"
$s = Get-Content "$env:LOCALAPPDATA\NightDesk\settings.json" -Raw | ConvertFrom-Json
$tb = [Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($s.EncryptedToken), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
$token = [Text.Encoding]::UTF8.GetString($tb)
$h = @{ Authorization = "Bearer $token" }
$m = Invoke-WebRequest "http://127.0.0.1:17645/api/v1/android-update" -Headers $h
Write-Output ("manifest={0}" -f $m.StatusCode)
Write-Output $m.Content
$out = Join-Path $env:TEMP "nightdesk-apk-check.apk"
Invoke-WebRequest "http://127.0.0.1:17645/api/v1/android-apk?versionCode=2" -Headers $h -OutFile $out
$hash = (Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output ("apk-sha256={0}" -f $hash)
Write-Output ("apk-size={0}" -f (Get-Item $out).Length)
Remove-Item -LiteralPath $out -Force
