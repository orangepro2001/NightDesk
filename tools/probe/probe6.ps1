
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$json = Get-Content 'C:\Users\pixel\AppData\Local\NightDesk\settings.json' -Raw | ConvertFrom-Json
$bytes = [Convert]::FromBase64String($json.EncryptedToken)
$plain = [System.Security.Cryptography.ProtectedData]::Unprotect($bytes, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
$token = [System.Text.Encoding]::UTF8.GetString($plain)
Write-Output ('token=' + $token.Substring(0,6) + '...' + $token.Substring($token.Length-4) + ' len=' + $token.Length)
$auth = 'Authorization: Bearer ' + $token
Write-Output '--- curl status ---'
& curl.exe -s -o NUL -w "%{http_code}" -H $auth http://127.0.0.1:17645/api/v1/status
Write-Output ''
Write-Output '--- curl body ---'
& curl.exe -s -H $auth http://127.0.0.1:17645/api/v1/status
Write-Output ''
Write-Output '--- routes probe (404 vs 401) ---'
& curl.exe -s -o NUL -w "%{http_code} /api/v1/nope" -H $auth http://127.0.0.1:17645/api/v1/nope
Write-Output ''
& curl.exe -s -o NUL -w "%{http_code} /api/v1/status (no auth)" http://127.0.0.1:17645/api/v1/status
Write-Output ''
