
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$json = Get-Content 'C:\Users\pixel\AppData\Local\NightDesk\settings.json' -Raw | ConvertFrom-Json
$bytes = [Convert]::FromBase64String($json.EncryptedToken)
$plain = [System.Security.Cryptography.ProtectedData]::Unprotect($bytes, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
$token = [System.Text.Encoding]::UTF8.GetString($plain)
Write-Output ('token len=' + $token.Length)
$h = @{ Authorization = 'Bearer ' + $token }
try {
  $r = Invoke-WebRequest -Uri 'http://127.0.0.1:17645/api/v1/status' -Headers $h -UseBasicParsing -TimeoutSec 10
  Write-Output ('status=' + $r.StatusCode)
  Write-Output $r.Content
} catch {
  Write-Output ('ERR ' + $_.Exception.Message)
}
