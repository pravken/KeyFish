# Microsoft Store screenshots, 1920x1080: a caption above KeyFish's own window on a light background, five per listing
# language: obj\store\<1..5>-<lang>.png. The windows are drawn by tests\Preview.cs (dark theme, accent 0067C0, so the
# pictures don't depend on this computer's colors); the captions come from msix\store-listing.md.
# Usage: msix\store-screenshots.ps1 [-Lang en,ru,...]   (all nine by default; each language takes about a minute)
param([string[]]$Lang = @('en', 'ru', 'uk', 'be', 'de', 'pl', 'sr', 'fr', 'es'))
$ErrorActionPreference = 'Stop'
$Lang = $Lang -split ','   # "-Lang en,ru" through powershell -File arrives as one string
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName System.Drawing

# Preview.exe: KeyFish's sources and tests\Preview.cs, with the resources build.bat makes (obj\*.gz)
if (-not (Test-Path obj)) { & .\build.bat; if ($LASTEXITCODE) { throw 'build.bat failed' } }
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$md = "$env:WINDIR\System32\WinMetadata"
$res = @(); Get-ChildItem data\*.txt | ForEach-Object { $res += "/resource:obj\$($_.Name).gz,$($_.Name).gz" }
Get-ChildItem i18n\*.txt | ForEach-Object { $res += "/resource:i18n\$($_.Name),i18n-$($_.Name)" }
$src = ((Get-Content build.bat | Select-String 'csc.exe').Line -split ' ' | Where-Object { $_ -like '*.cs' }) + 'tests\Preview.cs'
New-Item -ItemType Directory -Force tests\bin | Out-Null
& "$fw\csc.exe" /nologo /codepage:65001 /target:winexe /main:Preview /out:tests\bin\Preview.exe `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    "/lib:$fw\WPF" /r:Accessibility.dll /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll `
    "/r:$fw\System.Runtime.dll" "/r:$fw\System.Runtime.WindowsRuntime.dll" "/r:$fw\System.Runtime.InteropServices.WindowsRuntime.dll" `
    "/r:$md\Windows.Data.winmd" "/r:$md\Windows.UI.winmd" "/r:$md\Windows.Foundation.winmd" "/r:$md\Windows.ApplicationModel.winmd" `
    @res /resource:CHANGELOG.md,changelog-en.txt /resource:CHANGELOG.ru.md,changelog-ru.txt /resource:fish.ico,fish.ico /resource:fish-ru.ico,fish-ru.ico `
    /resource:fish-blink.ico,fish-blink.ico /resource:fish-ru-blink.ico,fish-ru-blink.ico @src | Select-String -NotMatch 'warning'
if ($LASTEXITCODE) { throw 'Preview.exe does not build' }
$preview = (Resolve-Path tests\bin\Preview.exe).Path

# Captions: in each "## Store listing: … (xx-…)" section, the list "1. Title / line under it" after its captions heading
$captions = @{}
$code = $null; $inList = $false
foreach ($line in Get-Content msix\store-listing.md -Encoding UTF8) {
    if ($line -match '^## Store listing: .*\((\w\w)-[\w-]+\)') { $code = $Matches[1]; $inList = $false; $captions[$code] = @(); continue }
    if ($line -match '^## ') { $code = $null; continue }
    if ($code -and $line -match '^\*\*(Screenshot captions|Подписи к скриншотам)\*\*') { $inList = $true; continue }
    if ($code -and $inList -and $line -match '^\d\. (.+?) / (.+)$') { $captions[$code] += , @($Matches[1], $Matches[2]) }
    elseif ($inList -and $line -match '^\*\*') { $inList = $false }
}

$shots = (New-Item -ItemType Directory -Force obj\store-shots).FullName
$out = (New-Item -ItemType Directory -Force obj\store).FullName

# The correction page (0) is as tall as its cards in that language, so the window ends between two cards instead
# of cutting one. If the page changes: take it with height=1000, find where its last whole card ends, add 72
$height0 = @{ en = 660; ru = 706; uk = 691; be = 707; de = 707; pl = 691; sr = 691; fr = 707; es = 675 }
# The text the actions menu is opened for; English text for the languages that type in Latin as well
$menuText = @{ ru = 'Еще одна ошибко: он сказал \"привет\" в 2 часа' }

function Capture([string]$arguments) {
    # at=9000: the window has finished its fade-in and the background work that fills it
    $p = Start-Process $preview -ArgumentList "$arguments accent=0067C0" -Wait -PassThru
    if ($p.ExitCode) { throw "Preview.exe $arguments failed" }
}

# The menu shot has an empty window around the menu: keep the menu and its shadow only
function Crop([System.Drawing.Bitmap]$b) {
    $menu = $b.GetPixel(40, 60)   # inside the menu, left of its text
    $minX = $b.Width; $minY = $b.Height; $maxX = 0; $maxY = 0
    for ($y = 0; $y -lt $b.Height; $y++) { for ($x = 0; $x -lt $b.Width; $x++) {
        $p = $b.GetPixel($x, $y)
        if ([Math]::Abs($p.R - $menu.R) -le 2 -and [Math]::Abs($p.G - $menu.G) -le 2 -and [Math]::Abs($p.B - $menu.B) -le 2) {
            if ($x -lt $minX) { $minX = $x }; if ($y -lt $minY) { $minY = $y }; if ($x -gt $maxX) { $maxX = $x }; if ($y -gt $maxY) { $maxY = $y } } } }
    $r = New-Object System.Drawing.Rectangle ($minX - 1), ($minY - 1), ($maxX - $minX + 3), ($maxY - $minY + 3)
    return $b.Clone($r, $b.PixelFormat)
}

# A shadow of a w x h rounded rectangle: its alpha blurred with three box passes each way (close to a Gaussian),
# on a bitmap with a margin of pad pixels around the rectangle
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class StoreShadow
{
    public static Bitmap Make(int w, int h, int radius, int pad, int corner)
    {
        int W = w + 2 * pad, H = h + 2 * pad;
        var mask = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(mask))
        using (var path = new GraphicsPath())
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int d = Math.Max(2, corner * 2);
            path.AddArc(pad, pad, d, d, 180, 90); path.AddArc(pad + w - d, pad, d, d, 270, 90);
            path.AddArc(pad + w - d, pad + h - d, d, d, 0, 90); path.AddArc(pad, pad + h - d, d, d, 90, 90); path.CloseFigure();
            g.FillPath(Brushes.Black, path);
        }
        var data = mask.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var px = new byte[W * H * 4];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        var a = new float[W * H];
        for (int i = 0; i < a.Length; i++) a[i] = px[i * 4 + 3];
        int r = Math.Max(1, radius / 3);
        for (int pass = 0; pass < 3; pass++) { Box(a, W, H, r, true); Box(a, W, H, r, false); }
        for (int i = 0; i < a.Length; i++) { px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = 0; px[i * 4 + 3] = (byte)Math.Min(255, a[i] * 0.55f); }
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        mask.UnlockBits(data);
        return mask;
    }

    static void Box(float[] a, int W, int H, int r, bool horizontal)
    {
        int n = horizontal ? W : H, lines = horizontal ? H : W;
        var line = new float[n];
        float scale = 1f / (2 * r + 1);
        for (int l = 0; l < lines; l++)
        {
            for (int i = 0; i < n; i++) line[i] = a[horizontal ? l * W + i : i * W + l];
            float sum = 0;
            for (int i = -r; i <= r; i++) sum += line[Math.Min(n - 1, Math.Max(0, i))];
            for (int i = 0; i < n; i++)
            {
                a[horizontal ? l * W + i : i * W + l] = sum * scale;
                sum += line[Math.Min(n - 1, i + r + 1)] - line[Math.Max(0, i - r)];
            }
        }
    }
}
'@

# One window, light on the left and dark on the right, split along a diagonal (the themes screenshot)
function Split([string]$light, [string]$dark, [string]$to) {
    $l = New-Object System.Drawing.Bitmap $light
    $d = New-Object System.Drawing.Bitmap $dark
    $w = $l.Width; $h = $l.Height
    $o = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($o)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $g.DrawImage($l, 0, 0, $w, $h)
    $tex = New-Object System.Drawing.TextureBrush $d
    $pts = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF ($w * 0.62), 0), (New-Object System.Drawing.PointF $w, 0), (New-Object System.Drawing.PointF $w, $h), (New-Object System.Drawing.PointF ($w * 0.38), $h))
    $g.FillPolygon($tex, $pts)
    $g.Dispose(); $tex.Dispose(); $l.Dispose(); $d.Dispose()
    $o.Save($to, [System.Drawing.Imaging.ImageFormat]::Png); $o.Dispose()
}

# The picture: a gradient background, the caption, and the window as large as fits under it (never enlarged),
# with rounded corners and a soft shadow as in Windows 11
function Shot([string]$src, [string]$to, [string[]]$caption, [switch]$crop) {
    $CW = 1920; $CH = 1080
    $img = New-Object System.Drawing.Bitmap $src
    if ($crop) { $c = Crop $img; $img.Dispose(); $img = $c }
    $bmp = New-Object System.Drawing.Bitmap $CW, $CH
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $top = 220; $room = $CH - $top - 50
    $s = [Math]::Min(1.0, [Math]::Min(($CW - 200) / $img.Width, $room / $img.Height))
    $w = [int]($img.Width * $s); $h = [int]($img.Height * $s)
    $x = [int](($CW - $w) / 2); $y = $top + [int](($room - $h) / 2)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $CW, $CH), ([System.Drawing.Color]::FromArgb(218, 222, 228)), ([System.Drawing.Color]::FromArgb(238, 240, 244))
    $g.FillRectangle($bg, 0, 0, $CW, $CH)
    $fTitle = New-Object System.Drawing.Font 'Segoe UI Semibold', 46, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $fLine = New-Object System.Drawing.Font 'Segoe UI', 26, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $center = New-Object System.Drawing.StringFormat; $center.Alignment = 'Center'
    $g.DrawString($caption[0], $fTitle, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(28, 32, 40))), (New-Object System.Drawing.RectangleF 80, 64, ($CW - 160), 70), $center)
    $g.DrawString($caption[1], $fLine, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(92, 100, 112))), (New-Object System.Drawing.RectangleF 80, 138, ($CW - 160), 44), $center)
    $corner = if ($crop) { 12 } else { [int](8 * $s) }   # the menu (taken at 150 %) has rounder corners of its own
    $shadow = [StoreShadow]::Make($w, $h, 40, 120, $corner)
    $g.DrawImage($shadow, $x - 120, $y - 120 + 22)
    $shadow.Dispose()
    $clip = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $corner
    $clip.AddArc($x, $y, $d, $d, 180, 90); $clip.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $clip.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $clip.AddArc($x, $y + $h - $d, $d, $d, 90, 90); $clip.CloseFigure()
    # The window as a texture filling its rounded shape: a fill is smoothed at the edges, a clip is not
    $sized = New-Object System.Drawing.Bitmap $w, $h
    $sg = [System.Drawing.Graphics]::FromImage($sized)
    $sg.InterpolationMode = 'HighQualityBicubic'; $sg.PixelOffsetMode = 'HighQuality'
    $sg.DrawImage($img, 0, 0, $w, $h); $sg.Dispose()
    $tex = New-Object System.Drawing.TextureBrush $sized
    $tex.TranslateTransform($x, $y)
    $g.FillPath($tex, $clip)
    $tex.Dispose(); $sized.Dispose()
    # A thin light edge, as dark Windows 11 windows have
    $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(40, 255, 255, 255)), 1), $clip)
    $clip.Dispose(); $g.Dispose(); $img.Dispose()
    $bmp.Save($to, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    $to
}

foreach ($l in $Lang) {
    $c = $captions[$l]
    if (-not $c -or $c.Count -ne 5) { throw "msix\store-listing.md has no five screenshot captions for '$l'" }
    $text = if ($menuText[$l]) { $menuText[$l] } else { 'Hello  World' }
    Capture "settings 0 `"$shots\p0-$l.png`" dark $l at=9000 height=$($height0[$l])"
    Capture "textmenu `"$shots\menu-$l.png`" dark $l `"text=$text`""
    Capture "settings 7 `"$shots\p7-$l.png`" dark $l at=9000 height=640"
    Capture "settings 5 `"$shots\p5-$l.png`" dark $l at=9000"
    Capture "settings 1 `"$shots\p1-$l.png`" dark $l at=9000 height=698"
    Capture "settings 1 `"$shots\p1-$l-light.png`" light $l at=9000 height=698"
    Split "$shots\p1-$l-light.png" "$shots\p1-$l.png" "$shots\p1-$l-split.png"
    Shot "$shots\p0-$l.png" "$out\1-$l.png" $c[0]
    Shot "$shots\menu-$l.png" "$out\2-$l.png" $c[1] -crop
    Shot "$shots\p7-$l.png" "$out\3-$l.png" $c[2]
    Shot "$shots\p5-$l.png" "$out\4-$l.png" $c[3]
    Shot "$shots\p1-$l-split.png" "$out\5-$l.png" $c[4]
}
