
$ErrorActionPreference = 'Continue'
foreach ($p in @('C:\Users\pixel\AppData\Roaming\NightDesk','C:\Users\pixel\AppData\Local\NightDesk')) {
  Write-Output ('=== ' + $p)
  if (Test-Path $p) { Get-ChildItem $p -Force -Recurse | Select-Object FullName,Length,LastWriteTime | Format-Table -AutoSize } else { Write-Output 'missing' }
}
