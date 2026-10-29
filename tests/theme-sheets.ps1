# Theme sheets: for every look in Skins.cs one picture with Settings (the Look card), About, the aquarium and the
# tray icons, in the dark and the light theme, to see a look whole before shipping it: tests\bin\sheets\<number>.png.
# Builds tests\bin\Preview.exe first. Usage: tests\theme-sheets.ps1 [-lang ru|en]
param([string]$lang = 'ru')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName System.Drawing

# Preview.exe: KeyFish's sources and tests\Preview.cs, with the resources build.bat makes (obj\*.gz)
if (-not (Test-Path obj)) { & .\build.bat; if ($LASTEXITCODE) { throw 'build.bat failed' } }
$res = @(); Get-ChildItem data\*.txt | ForEach-Object { $res += "/resource:obj\$($_.Name).gz,$($_.Name).gz" }
Get-ChildItem i18n\*.txt | ForEach-Object { $res += "/resource:i18n\$($_.Name),i18n-$($_.Name)" }
$src = ((Get-Content build.bat | Select-String 'csc.exe').Line -split ' ' | Where-Object { $_ -like '*.cs' }) + 'tests\Preview.cs'
New-Item -ItemType Directory -Force tests\bin\sheets | Out-Null
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /main:Preview /out:tests\bin\Preview.exe `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    "/lib:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF" /r:Accessibility.dll /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll `
    @res /resource:CHANGELOG.md,changelog-en.txt /resource:CHANGELOG.ru.md,changelog-ru.txt /resource:fish.ico,fish.ico /resource:fish-ru.ico,fish-ru.ico `
    /resource:fish-blink.ico,fish-blink.ico /resource:fish-ru-blink.ico,fish-ru-blink.ico @src | Select-String -NotMatch 'warning'
if ($LASTEXITCODE) { throw 'Preview.exe does not build' }

$tmp = 'tests\bin\sheets\parts'
New-Item -ItemType Directory -Force $tmp | Out-Null
$numbers = [regex]::Matches((Get-Content Skins.cs -Raw), 'Number = (\d+)') | ForEach-Object { [int]$_.Groups[1].Value }
foreach ($n in $numbers) {
    # Each window waits for its own Preview.exe to finish, one at a time
    $shots = @()
    foreach ($theme in 'dark', 'light') {
        $row = @()
        foreach ($what in 'settings', 'about', 'aquarium') {
            $out = "$tmp\$n-$theme-$what.png"
            $a = if ($what -eq 'settings') { @('settings', '0', $out, $theme, $lang, "skin=$n", 'scroll=340', 'at=1500') } else { @($what, $out, $theme, $lang, "skin=$n") }
            Start-Process tests\bin\Preview.exe -ArgumentList $a -Wait
            $row += $out
        }
        $shots += , $row
    }
    $icons = "$tmp\$n-icons.png"
    Start-Process tests\bin\Preview.exe -ArgumentList @('icons', $icons, "skin=$n") -Wait

    # One sheet: the dark row, the light row, the icons; the windows at 60 %
    $k = 0.6; $gap = 16
    $images = foreach ($row in $shots) { , @(foreach ($f in $row) { [Drawing.Image]::FromFile((Resolve-Path $f).Path) }) }
    $iconImage = [Drawing.Image]::FromFile((Resolve-Path $icons).Path)
    $rowW = $gap; $rowH = 0
    foreach ($im in $images[0]) { $rowW += [int]($im.Width * $k) + $gap; $rowH = [Math]::Max($rowH, [int]($im.Height * $k)) }
    $sheet = New-Object Drawing.Bitmap ([int][Math]::Max($rowW, $iconImage.Width + 2 * $gap)), ([int]($gap * 4 + $rowH * 2 + $iconImage.Height))
    $g = [Drawing.Graphics]::FromImage($sheet)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.Clear([Drawing.Color]::FromArgb(128, 128, 128))
    $y = $gap
    foreach ($row in $images) {
        $x = $gap
        foreach ($im in $row) { $w = [int]($im.Width * $k); $h = [int]($im.Height * $k); $g.DrawImage($im, $x, $y, $w, $h); $x += $w + $gap }
        $y += $rowH + $gap
    }
    $g.DrawImage($iconImage, $gap, $y, $iconImage.Width, $iconImage.Height)
    $g.Dispose()
    $sheet.Save((Join-Path (Resolve-Path tests\bin\sheets) "$n.png"), [Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose(); $iconImage.Dispose(); $images | ForEach-Object { $_ | ForEach-Object { $_.Dispose() } }
    "tests\bin\sheets\$n.png"
}
