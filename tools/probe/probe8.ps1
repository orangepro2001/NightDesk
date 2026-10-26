
Add-Type -AssemblyName System.Drawing
$ico = New-Object System.Drawing.Icon('C:\dev\NightDesk\src\NightDesk\Assets\nightdesk.ico')
Write-Output ('default size=' + $ico.Width + 'x' + $ico.Height)
$bmp = $ico.ToBitmap()
$bmp.Save('C:\dev\NightDesk\tools\probe\icon_default.png', [System.Drawing.Imaging.ImageFormat]::Png)
$big = New-Object System.Drawing.Icon('C:\dev\NightDesk\src\NightDesk\Assets\nightdesk.ico', 256, 256)
$big.ToBitmap().Save('C:\dev\NightDesk\tools\probe\icon_256.png', [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output ('big=' + $big.Width + 'x' + $big.Height)
