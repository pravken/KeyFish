# Packs KeyFish for the Microsoft Store: the exe, the manifest with the version from Version.cs, the tiles made from
# fish.ico and their resource index, into one .msix. Partner Center takes it as it is: the Store signs it.
# Usage: powershell -ExecutionPolicy Bypass -File msix\build-msix.ps1 [-Exe KeyFish.exe] [-Out obj\KeyFish.msix]
# Needs the Windows SDK (makeappx.exe, makepri.exe)
param([string]$Exe = 'KeyFish.exe', [string]$Out = 'obj\KeyFish.msix')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
Add-Type -AssemblyName System.Drawing

$sdk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.*\x64\makeappx.exe" | Sort-Object FullName | Select-Object -Last 1
if (-not $sdk) { throw 'The Windows SDK is not installed: makeappx.exe not found' }
$bin = $sdk.DirectoryName

# An SDK tool; its own output is shown only when it fails, so a failure on the build server says why
function Tool([string]$name) {
  $ErrorActionPreference = 'Continue'   # its messages on stderr are not errors of this script
  $output = & "$bin\$name" @args 2>&1
  if ($LASTEXITCODE -ne 0) { $output | ForEach-Object { Write-Host $_ }; throw "$name $($args[0]) failed with exit code $LASTEXITCODE" }
}

# The version: 2.3.0 in Version.cs is 2.3.0.0 here (the Store wants the last number 0)
$v = [regex]::Match((Get-Content Version.cs -Raw), 'AssemblyVersion\("(\d+\.\d+\.\d+)').Groups[1].Value
if (-not $v) { throw 'No version in Version.cs' }

$layout = Join-Path $root 'obj\msix'
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force "$layout\Assets" | Out-Null
Copy-Item $Exe "$layout\KeyFish.exe"
(Get-Content msix\AppxManifest.xml -Raw -Encoding utf8).Replace('{VERSION}', "$v.0") |
  Set-Content "$layout\AppxManifest.xml" -Encoding utf8

# Tiles. The icon's own small frames stay sharp; larger ones are drawn from its 256-pixel frame
function Frame([int]$size) {
  $icon = New-Object System.Drawing.Icon (Join-Path $root 'fish.ico'), $size, $size
  try { return $icon.ToBitmap() } finally { $icon.Dispose() }
}
# The 256-pixel frame is stored as a PNG, which System.Drawing.Icon passes over for a smaller frame: read it directly
function BigFrame {
  $b = [IO.File]::ReadAllBytes((Join-Path $root 'fish.ico'))
  for ($i = 0; $i -lt [BitConverter]::ToUInt16($b, 4); $i++) {
    $e = 6 + 16 * $i
    if ($b[$e] -eq 0 -and $b[$e + 1] -eq 0) {
      $ms = [IO.MemoryStream]::new($b, [BitConverter]::ToInt32($b, $e + 12), [BitConverter]::ToInt32($b, $e + 8))
      return [System.Drawing.Bitmap]::new($ms)
    }
  }
  throw 'fish.ico has no 256-pixel frame'
}
$big = BigFrame
function Save([int]$size, [string]$name) {
  $native = 16, 20, 24, 32, 40, 48, 64
  if ($native -contains $size) { $bmp = Frame $size }
  else {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'HighQuality'
    $g.DrawImage($big, 0, 0, $size, $size); $g.Dispose()
  }
  $bmp.Save("$layout\Assets\$name", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
# The fish fills about two thirds of a tile, as Windows' own tiles do
function SaveTile([int]$size, [string]$name) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'HighQuality'
  $inner = [int]($size * 0.66); $at = [int](($size - $inner) / 2)
  $g.DrawImage($big, $at, $at, $inner, $inner); $g.Dispose()
  $bmp.Save("$layout\Assets\$name", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
foreach ($s in 100, 200) {
  Save (44 * $s / 100) "Square44x44Logo.scale-$s.png"
  Save (50 * $s / 100) "StoreLogo.scale-$s.png"
  SaveTile (150 * $s / 100) "Square150x150Logo.scale-$s.png"
}
# Taskbar, Start and search take these by size, without a coloured plate behind the fish
foreach ($t in 16, 24, 32, 48, 256) {
  Save $t "Square44x44Logo.targetsize-$t.png"
  Save $t "Square44x44Logo.targetsize-${t}_altform-unplated.png"
}
$big.Dispose()

# The resource index that lets Windows pick the right tile for the scale and size
$cfg = Join-Path $root 'obj\priconfig.xml'
Tool makepri.exe createconfig /cf $cfg /dq en-US /pv 10.0.0 /o
# One index for all scales in the one package, not a separate resource package for each
$x = [xml](Get-Content $cfg -Raw)
$p = $x.resources.SelectSingleNode('packaging'); if ($p) { [void]$x.resources.RemoveChild($p) }
$x.Save($cfg)
Tool makepri.exe new /pr $layout /cf $cfg /mn "$layout\AppxManifest.xml" /of "$layout\resources.pri" /o

if (-not [IO.Path]::IsPathRooted($Out)) { $Out = Join-Path $root $Out }   # a bare name (KeyFish.msix) goes into the project folder
New-Item -ItemType Directory -Force (Split-Path $Out -Parent) | Out-Null
Tool makeappx.exe pack /d $layout /p $Out /o
"KeyFish $v.0 packed: $Out"
