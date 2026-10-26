# Generates NightDesk.ico (multi-size) plus PNG previews from one vector definition.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'src\NightDesk\Assets'
$artDir = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force -Path $outDir, $artDir | Out-Null

function Add-RoundedRect([System.Drawing.Drawing2D.GraphicsPath] $path, [single] $x, [single] $y, [single] $w, [single] $h, [single] $r) {
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
}

function Add-Star([System.Drawing.Drawing2D.GraphicsPath] $path, [single] $cx, [single] $cy, [single] $r) {
    $i = $r * 0.28
    $path.AddPolygon([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($cx, $cy - $r),
        [System.Drawing.PointF]::new($cx + $i, $cy - $i),
        [System.Drawing.PointF]::new($cx + $r, $cy),
        [System.Drawing.PointF]::new($cx + $i, $cy + $i),
        [System.Drawing.PointF]::new($cx, $cy + $r),
        [System.Drawing.PointF]::new($cx - $i, $cy + $i),
        [System.Drawing.PointF]::new($cx - $r, $cy),
        [System.Drawing.PointF]::new($cx - $i, $cy - $i)))
    $path.CloseFigure()
}

function New-NightDeskBitmap([int] $size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 1024.0

    # tile
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundedRect $tile (12 * $s) (12 * $s) (1000 * $s) (1000 * $s) (232 * $s)
    $rect = [System.Drawing.RectangleF]::new(12 * $s, 12 * $s, 1000 * $s, 1000 * $s)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.ColorTranslator]::FromHtml('#243E31'), [System.Drawing.ColorTranslator]::FromHtml('#0B1512'), 55.0)
    $g.FillPath($brush, $tile)
    $brush.Dispose()

    if ($size -ge 24) {
        $ring = New-Object System.Drawing.Drawing2D.GraphicsPath
        Add-RoundedRect $ring (24 * $s) (24 * $s) (976 * $s) (976 * $s) (222 * $s)
        $pen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#7CB85B'), (11 * $s))
        $g.DrawPath($pen, $ring)
        $pen.Dispose(); $ring.Dispose()
    }

    # moon: full disc minus offset disc
    $moon = New-Object System.Drawing.Drawing2D.GraphicsPath
    $moon.AddEllipse(250 * $s, 250 * $s, 560 * $s, 560 * $s)
    $bite = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bite.AddEllipse(430 * $s, 160 * $s, 560 * $s, 560 * $s)
    $region = [System.Drawing.Region]::new($moon)
    $region.Exclude($bite)
    $moonBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#DCF5AC'))
    $g.FillRegion($moonBrush, $region)
    $moonBrush.Dispose(); $region.Dispose(); $bite.Dispose()

    if ($size -ge 48) {
    # stars
    $star = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-Star $star (818 * $s) (310 * $s) (74 * $s)
    Add-Star $star (712 * $s) (176 * $s) (40 * $s)
    $starBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#F3F7E8'))
    $g.FillPath($starBrush, $star)
    $starBrush.Dispose(); $star.Dispose()

    # desk line
    $desk = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundedRect $desk (318 * $s) (772 * $s) (388 * $s) (54 * $s) (27 * $s)
    $deskBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#8FB872'))
    $g.FillPath($deskBrush, $desk)
    $deskBrush.Dispose(); $desk.Dispose()
    }
    $tile.Dispose(); $moon.Dispose(); $g.Dispose()
    return $bmp
}

function New-NightDeskIconBitmap([int] $size) {
    # Small sizes are supersampled from 4x so 16 px taskbar/list icons stay a clean crescent.
    if ($size -ge 40) { return (New-NightDeskBitmap $size) }
    $big = New-NightDeskBitmap ($size * 4)
    $small = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($small)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($big, 0, 0, $size, $size)
    $g.Dispose(); $big.Dispose()
    return $small
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$images = @()
foreach ($sz in $sizes) {
    $bmp = New-NightDeskIconBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,@($sz, $ms.ToArray())
    if ($sz -eq 256) { $bmp.Save((Join-Path $artDir 'icon-256.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose(); $ms.Dispose()
}
$preview = New-NightDeskBitmap 512
$preview.Save((Join-Path $artDir 'icon-512.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()

$icoPath = Join-Path $outDir 'nightdesk.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $sz = [int]$img[0]; $data = [byte[]]$img[1]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($img in $images) { $bw.Write([byte[]]$img[1]) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()
Write-Host "icon: $icoPath ($((Get-Item $icoPath).Length) bytes)"
