
$ErrorActionPreference = 'Continue'
Write-Output '--- sessions ---'
& quser 2>&1 | Out-String
Write-Output '--- all nightdesk processes ---'
Get-CimInstance Win32_Process -Filter "Name = 'NightDesk.exe'" | Select-Object ProcessId,SessionId,ParentProcessId,ExecutablePath,CreationDate | Format-Table -AutoSize
Write-Output '--- explorer sessions ---'
Get-Process -Name explorer | Select-Object Id,SessionId,StartTime | Format-Table -AutoSize
