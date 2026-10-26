$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$pkg = 'C:\Users\pixel\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\NightDesk'
Get-ChildItem $pkg -Force -Recurse | Select-Object FullName,Length,LastWriteTime | Format-Table -AutoSize
$json = Get-Content (Join-Path $pkg 'settings.json') -Raw | ConvertFrom-Json
Write-Output ('RemoteEnabled=' + $json.RemoteEnabled + ' fingerprint=' + $json.ServeFingerprint + ' hotkey=' + $json.Hotkey + ' preventSleep=' + $json.PreventSleep)
$bytes = [Convert]::FromBase64String($json.EncryptedToken)
$plain = [System.Security.Cryptography.ProtectedData]::Unprotect($bytes, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
$token = [System.Text.Encoding]::UTF8.GetString($plain)
Write-Output ('token=' + $token.Substring(0,6) + '...' + $token.Substring($token.Length-4) + ' len=' + $token.Length)
$auth = 'Authorization: Bearer ' + $token
$fmt = '%{http_code}'
Write-Output '--- status via package token ---'
& curl.exe -s -o NUL -w $fmt -H $auth http://127.0.0.1:17645/api/v1/status
Write-Output ''
& curl.exe -s -H $auth http://127.0.0.1:17645/api/v1/status
Write-Output ''