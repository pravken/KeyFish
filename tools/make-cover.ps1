# Draws store images for KeyFish: tools/cover.png (1280x720) and tools/thumbnail.png (600x600)
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
# Take the 256x256 PNG frame straight from fish.ico (.NET can't decode it and would upscale a small frame)
$ico = [IO.File]::ReadAllBytes((Join-Path $root 'fish.ico'))
$fish = $null
for ($i = 0; $i -lt [BitConverter]::ToUInt16($ico, 4); $i++) {
    $e = 6 + 16 * $i
    if ($ico[$e] -eq 0) {
        $size = [BitConverter]::ToInt32($ico, $e + 8); $offset = [BitConverter]::ToInt32($ico, $e + 12)
        $fish = [System.Drawing.Image]::FromStream((New-Object IO.MemoryStream (, $ico[$offset..($offset + $size - 1)])))
    }
}

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.InterpolationMode = 'HighQualityBicubic'
    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(18, 32, 64)), ([System.Drawing.Color]::FromArgb(28, 72, 150)), 35
    $g.FillRectangle($bg, $rect)
    return @($bmp, $g)
}

$white = [System.Drawing.Brushes]::White
$soft = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(190, 210, 240))
$orange = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 160, 70))

# Cover
$bmp, $g = New-Canvas 1280 720
$g.DrawImage($fish, 90, 232, 256, 256)
$g.DrawString('KeyFish', (New-Object System.Drawing.Font 'Segoe UI', 72, ([System.Drawing.FontStyle]::Bold)), $white, 400, 190)
$g.DrawString('Fixes text typed in the wrong keyboard layout', (New-Object System.Drawing.Font 'Segoe UI', 26), $soft, 408, 315)
$mono = New-Object System.Drawing.Font 'Consolas', 34
$g.DrawString('ghbdtn', $mono, $soft, 410, 400); $g.DrawString('→', $mono, $orange, 590, 400); $g.DrawString('привет', $mono, $white, 650, 400)
$g.DrawString('руддщ', $mono, $soft, 410, 460); $g.DrawString('→', $mono, $orange, 590, 460); $g.DrawString('hello', $mono, $white, 650, 460)
$g.DrawString('Windows · EN ⇄ RU · free on GitHub', (New-Object System.Drawing.Font 'Segoe UI', 20), $soft, 410, 560)
$g.Dispose(); $bmp.Save((Join-Path $PSScriptRoot 'cover.png'), [System.Drawing.Imaging.ImageFormat]::Png)

# Thumbnail
$bmp, $g = New-Canvas 600 600
$g.DrawImage($fish, 172, 110, 256, 256)
$f = New-Object System.Drawing.Font 'Segoe UI', 54, ([System.Drawing.FontStyle]::Bold)
$size = $g.MeasureString('KeyFish', $f); $g.DrawString('KeyFish', $f, $white, (600 - $size.Width) / 2, 390)
$m = New-Object System.Drawing.Font 'Consolas', 26
$text = 'ghbdtn → привет'; $size = $g.MeasureString($text, $m); $g.DrawString($text, $m, $soft, (600 - $size.Width) / 2, 490)
$g.Dispose(); $bmp.Save((Join-Path $PSScriptRoot 'thumbnail.png'), [System.Drawing.Imaging.ImageFormat]::Png)
