
$ErrorActionPreference = 'Continue'
Get-Process -Name NightDesk | Select-Object Id,StartTime,Path,SessionId | Format-List
Write-Output '--- listeners ---'
Get-NetTCPConnection -State Listen -LocalPort 17645,443 | Select-Object LocalAddress,LocalPort,OwningProcess | Format-Table -AutoSize
Write-Output '--- http probe local ---'
try { $r = Invoke-WebRequest -Uri 'http://127.0.0.1:17645/api/v1/status' -UseBasicParsing -TimeoutSec 5; Write-Output ('status=' + $r.StatusCode) } catch { Write-Output ('ERR: ' + $_.Exception.Message) }
Write-Output '--- settings path ---'
Get-Item 'C:\Users\pixel\AppData\Local\NightDesk\settings.json' | Select-Object LastWriteTime | Format-List
