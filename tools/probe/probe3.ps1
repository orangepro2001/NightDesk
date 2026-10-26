
$ErrorActionPreference = 'Continue'
foreach ($u in @('https://minipc-xoh4t.tail9f7f1f.ts.net/','https://minipc-xoh4t.tail9f7f1f.ts.net/api/v1/status')) {
  try {
    $r = Invoke-WebRequest -Uri $u -UseBasicParsing -TimeoutSec 15
    Write-Output ($u + ' -> ' + $r.StatusCode + ' len=' + $r.Content.Length)
    if ($u -like '*/api/*') { Write-Output ($r.Content) }
  } catch {
    Write-Output ($u + ' -> ERR ' + $_.Exception.Message)
    if ($_.Exception.Response) { Write-Output ('status=' + [int]$_.Exception.Response.StatusCode) }
  }
}
Write-Output '--- token file? ---'
Get-ChildItem 'C:\Users\pixel\AppData\Local\NightDesk' -Force | Select-Object Name,Length | Format-Table -AutoSize
