# Рисует значок-рыбку и собирает fish.ico (несколько размеров, PNG внутри) и превью fish-preview.png
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function Draw-Fish([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $k = $s / 64.0
    $blue = [System.Drawing.Color]::FromArgb(40, 110, 220)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $blue), 0, 0, $s - 0.5, $s - 0.5)

    $white = [System.Drawing.Brushes]::White
    # Тело и хвост
    $g.FillEllipse($white, 10 * $k, 21 * $k, 34 * $k, 22 * $k)
    $tail = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (40 * $k), (32 * $k)),
        (New-Object System.Drawing.PointF (55 * $k), (20 * $k)),
        (New-Object System.Drawing.PointF (51 * $k), (32 * $k)),
        (New-Object System.Drawing.PointF (55 * $k), (44 * $k)))
    $g.FillPolygon($white, $tail)
    # Плавник сверху
    $fin = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (22 * $k), (23 * $k)),
        (New-Object System.Drawing.PointF (32 * $k), (14 * $k)),
        (New-Object System.Drawing.PointF (35 * $k), (23 * $k)))
    $g.FillPolygon($white, $fin)
    # Глаз
    $eye = [Math]::Max(1.6, 5 * $k)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $blue), 18 * $k - $eye / 2, 29 * $k - $eye / 2, $eye, $eye)
    # Пузырьки — только на крупных размерах, на мелких они превращаются в шум
    if ($s -ge 32) {
        $pen = New-Object System.Drawing.Pen $white, ([Math]::Max(1, 2 * $k))
        $g.DrawEllipse($pen, 11 * $k, 13 * $k, 6 * $k, 6 * $k)
        $g.DrawEllipse($pen, 17 * $k, 7 * $k, 4 * $k, 4 * $k)
    }
    $g.Dispose()
    return $bmp
}

# Размер 256 храним как PNG, остальные — классическим BMP: .NET Framework не читает маленькие PNG-кадры
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

$pngs = foreach ($s in $sizes) {
    $bmp = Draw-Fish $s
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

# Формат ICO: заголовок, каталог записей, затем PNG-данные
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $root 'fish.ico'), $out.ToArray())

# Превью: крупно и в реальных размерах трея
$preview = New-Object System.Drawing.Bitmap 450, 270
$g = [System.Drawing.Graphics]::FromImage($preview)
$g.Clear([System.Drawing.Color]::FromArgb(245, 245, 245))
$g.DrawImage((Draw-Fish 256), 5, 5, 256, 256)
$x = 280
foreach ($s in 16, 24, 32, 48) {
    $g.DrawImage((Draw-Fish $s), $x, 20, $s, $s)
    $x += $s + 10
}
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(32, 32, 32))), 270, 100, 180, 70)
$x = 280
foreach ($s in 16, 24, 32, 48) {
    $g.DrawImage((Draw-Fish $s), $x, 110, $s, $s)
    $x += $s + 10
}
$g.Dispose()
$preview.Save((Join-Path $root 'tools\fish-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
