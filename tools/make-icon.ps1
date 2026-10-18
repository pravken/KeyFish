# Draws the fish icons and builds the .ico files (several sizes) and the fish-preview.png preview:
#   fish.ico           — blue: the app icon and the English layout in the tray
#   fish-ru.ico        — orange: the Russian layout
#   fish-blink*.ico    — inverted: the blink after an auto-fix
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$blue = [System.Drawing.Color]::FromArgb(40, 110, 220)
$orange = [System.Drawing.Color]::FromArgb(235, 120, 30)
$white = [System.Drawing.Color]::White

function Draw-Fish([int]$s, $bg, $fg) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $k = $s / 64.0
    $bgBrush = New-Object System.Drawing.SolidBrush $bg
    $fgBrush = New-Object System.Drawing.SolidBrush $fg
    $g.FillEllipse($bgBrush, 0, 0, $s - 0.5, $s - 0.5)

    # Body and tail
    $g.FillEllipse($fgBrush, 10 * $k, 21 * $k, 34 * $k, 22 * $k)
    $tail = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (40 * $k), (32 * $k)),
        (New-Object System.Drawing.PointF (55 * $k), (20 * $k)),
        (New-Object System.Drawing.PointF (51 * $k), (32 * $k)),
        (New-Object System.Drawing.PointF (55 * $k), (44 * $k)))
    $g.FillPolygon($fgBrush, $tail)
    # Top fin
    $fin = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (22 * $k), (23 * $k)),
        (New-Object System.Drawing.PointF (32 * $k), (14 * $k)),
        (New-Object System.Drawing.PointF (35 * $k), (23 * $k)))
    $g.FillPolygon($fgBrush, $fin)
    # Eye
    $eye = [Math]::Max(1.6, 5 * $k)
    $g.FillEllipse($bgBrush, 18 * $k - $eye / 2, 29 * $k - $eye / 2, $eye, $eye)
    # Bubbles only on larger sizes; on small ones they turn into noise
    if ($s -ge 32) {
        $pen = New-Object System.Drawing.Pen $fg, ([Math]::Max(1, 2 * $k))
        $g.DrawEllipse($pen, 11 * $k, 13 * $k, 6 * $k, 6 * $k)
        $g.DrawEllipse($pen, 17 * $k, 7 * $k, 4 * $k, 4 * $k)
    }
    $g.Dispose()
    return $bmp
}

# Size 256 is stored as PNG, the rest as classic BMP: .NET Framework can't read small PNG frames
function To-Dib([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([UInt32]40); $w.Write([Int32]$s); $w.Write([Int32]($s * 2))
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]0)
    $w.Write([UInt32]0); $w.Write([Int32]0); $w.Write([Int32]0); $w.Write([UInt32]0); $w.Write([UInt32]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $s)))   # маска прозрачности не нужна: есть альфа-канал
    $w.Flush()
    return , $ms.ToArray()
}

function Save-Ico([string]$name, $bg, $fg) {
    $frames = foreach ($s in $sizes) {
        $bmp = Draw-Fish $s $bg $fg
        if ($s -ge 256) {
            $ms = New-Object System.IO.MemoryStream
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $data = $ms.ToArray()
        } else {
            $data = To-Dib $bmp
        }
        $bmp.Dispose()
        , $data
    }
    # ICO format: header, directory entries, then the frame data
    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $out
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
        $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32)
        $w.Write([UInt32]$frames[$i].Length); $w.Write([UInt32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($f in $frames) { $w.Write($f) }
    $w.Flush()
    [IO.File]::WriteAllBytes((Join-Path $root $name), $out.ToArray())
}

Save-Ico 'fish.ico' $blue $white
Save-Ico 'fish-ru.ico' $orange $white
Save-Ico 'fish-blink.ico' $white $blue
Save-Ico 'fish-ru-blink.ico' $white $orange

# Preview: large, plus every variant at tray sizes on light and dark backgrounds
$variants = @(@($blue, $white), @($orange, $white), @($white, $blue), @($white, $orange))
$preview = New-Object System.Drawing.Bitmap 450, 270
$g = [System.Drawing.Graphics]::FromImage($preview)
$g.Clear([System.Drawing.Color]::FromArgb(245, 245, 245))
$g.DrawImage((Draw-Fish 256 $blue $white), 5, 5, 256, 256)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(32, 32, 32))), 270, 140, 180, 120)
$y = 20
foreach ($s in 16, 24, 32) {
    $x = 280
    foreach ($v in $variants) { $g.DrawImage((Draw-Fish $s $v[0] $v[1]), $x, $y, $s, $s); $x += 42 }
    $x = 280
    foreach ($v in $variants) { $g.DrawImage((Draw-Fish $s $v[0] $v[1]), $x, $y + 130, $s, $s); $x += 42 }
    $y += $s + 12
}
$g.Dispose()
$preview.Save((Join-Path $root 'tools\fish-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
