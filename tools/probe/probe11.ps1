$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$json = Get-Content 'C:\Users\pixel\AppData\Local\NightDesk\settings.json' -Raw | ConvertFrom-Json
$bytes = [Convert]::FromBase64String($json.EncryptedToken)
$plain = [System.Security.Cryptography.ProtectedData]::Unprotect($bytes, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
$token = [System.Text.Encoding]::UTF8.GetString($plain)
Write-Output ('RAW=[' + $token + ']')
$codes = ($token.ToCharArray() | ForEach-Object { [int][char]$_ }) -join ','
Write-Output ('CODES=' + $codes)
$sha = [System.Security.Cryptography.SHA256]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($token))
Write-Output ('SHA=' + ([System.BitConverter]::ToString($sha).Replace('-','').ToLowerInvariant()))
Write-Output '--- trusted devices plaintext ---'
$dev = Get-Content 'C:\Users\pixel\AppData\Local\NightDesk\trusted-devices.dat' -Raw
$devPlain = [System.Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($dev), $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
Write-Output ([System.Text.Encoding]::UTF8.GetString($devPlain))