$ErrorActionPreference = "Stop"
$s = Get-Content "$env:LOCALAPPDATA\NightDesk\settings.json" -Raw | ConvertFrom-Json
$tb = [Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($s.EncryptedToken), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
$token = [Text.Encoding]::UTF8.GetString($tb)
$h = @{ Authorization = "Bearer $token" }
$base = "https://minipc-xoh4t.tail9f7f1f.ts.net"
$m = Invoke-WebRequest "$base/api/v1/android-update" -Headers $h -UseBasicParsing
Write-Output ("ts-manifest={0}" -f $m.StatusCode)
Write-Output $m.Content
$out = Join-Path $env:TEMP "nightdesk-apk-ts.apk"
Invoke-WebRequest "$base/api/v1/android-apk?versionCode=2" -Headers $h -OutFile $out
$hash = (Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output ("ts-apk-sha256={0}" -f $hash)
Write-Output ("ts-apk-size={0}" -f (Get-Item $out).Length)
Remove-Item -LiteralPath $out -Force
