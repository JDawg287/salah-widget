# Generates the logo/icon PNG assets with System.Drawing. (Widget-picker screenshots are real captures.)
# Run with Windows PowerShell or PowerShell 7 on Windows:  .\scripts\New-Assets.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\SalahWidget\Assets'
New-Item -ItemType Directory -Force $out | Out-Null

$gold = [System.Drawing.Color]::FromArgb(255, 232, 178, 62)
$teal = [System.Drawing.Color]::FromArgb(255, 22, 110, 104)

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)
    return $bmp, $g
}

function Draw-Crescent($g, [float]$x, [float]$y, [float]$size, $color) {
    $outer = New-Object System.Drawing.Drawing2D.GraphicsPath
    $outer.AddEllipse($x, $y, $size, $size)
    $inner = New-Object System.Drawing.Drawing2D.GraphicsPath
    $inner.AddEllipse($x + $size * 0.28, $y - $size * 0.06, $size * 0.9, $size * 0.9)
    $region = New-Object System.Drawing.Region $outer
    $region.Exclude($inner)
    $brush = New-Object System.Drawing.SolidBrush $color
    $g.FillRegion($brush, $region)

    # Five-pointed star inside the crescent's opening
    $cx = $x + $size * 0.72; $cy = $y + $size * 0.40; $r = $size * 0.14
    $pts = for ($i = 0; $i -lt 10; $i++) {
        $rad = if ($i % 2 -eq 0) { $r } else { $r * 0.42 }
        $a = -[Math]::PI / 2 + $i * [Math]::PI / 5
        New-Object System.Drawing.PointF ([float]($cx + $rad * [Math]::Cos($a))), ([float]($cy + $rad * [Math]::Sin($a)))
    }
    $g.FillPolygon($brush, [System.Drawing.PointF[]]$pts)
}

function Save-Icon([string]$name, [int]$size, [bool]$tile) {
    $bmp, $g = New-Canvas $size $size
    if ($tile) {
        $g.FillEllipse((New-Object System.Drawing.SolidBrush $teal), 0, 0, $size - 1, $size - 1)
        Draw-Crescent $g ($size * 0.18) ($size * 0.18) ($size * 0.64) $gold
    } else {
        Draw-Crescent $g ($size * 0.06) ($size * 0.06) ($size * 0.88) $gold
    }
    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

Save-Icon 'WidgetIcon.png' 96 $false
Save-Icon 'Square44x44Logo.png' 44 $true
Save-Icon 'Square150x150Logo.png' 150 $true
Save-Icon 'StoreLogo.png' 50 $true

Get-ChildItem $out | Select-Object Name, Length
