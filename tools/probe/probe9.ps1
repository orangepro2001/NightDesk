
$ErrorActionPreference = 'SilentlyContinue'
Get-ChildItem -Path 'C:\Users\pixel' -Recurse -Filter 'settings.json' -Force -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like '*NightDesk*' } | Select-Object FullName,Length,LastWriteTime | Format-Table -AutoSize
Write-Output '--- desktop dir NightDesk* files ---'
Get-ChildItem 'C:\Users\pixel\Desktop' -Force | Where-Object { $_.Name -like '*NightDesk*' -or $_.Name -like '*.json' -or $_.Name -like '*.log' } | Select-Object Name,Length,LastWriteTime | Format-Table -AutoSize
Write-Output '--- recent files touched by pid 22708? ---'
Get-ChildItem 'C:\Users\pixel\AppData' -Recurse -Filter '*.json' -Force -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt (Get-Date).AddHours(-2) } | Select-Object FullName,LastWriteTime | Format-Table -AutoSize
