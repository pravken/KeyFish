# End-to-end typing tests for KeyFish. They press real keys, so don't touch the keyboard or mouse while they run.
# Your replacements, word lists and settings are backed up first and restored at the end.
# Usage: powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$bin = Join-Path $PSScriptRoot 'bin'
$key = 'HKCU:\Software\KeyFish'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force $bin | Out-Null
& $csc /nologo /codepage:65001 /target:winexe /out:"$bin\Typer.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\Typer.cs"
& $csc /nologo /codepage:65001 /out:"$bin\BrowserTyper.exe" "$PSScriptRoot\BrowserTyper.cs"
Copy-Item "$PSScriptRoot\pw.html", "$PSScriptRoot\tx.html" $bin -Force

# Back up the user's files and settings, then use a known setup
$files = 'replacements.txt', 'ignored-words.txt', 'learned-words.txt', 'app-layouts.txt'
$backup = @{}; foreach ($f in $files) { $p = Join-Path $root $f; $backup[$f] = if (Test-Path $p) { [IO.File]::ReadAllBytes($p) } else { $null } }
$regNames = 'Typos', 'StatsTotal', 'UndoCtrlZ', 'QuietFullscreen', 'AutoCorrect'
$regBackup = @{}; foreach ($n in $regNames) { $regBackup[$n] = (Get-ItemProperty $key -Name $n -ErrorAction SilentlyContinue).$n }
[IO.File]::WriteAllText((Join-Path $root 'replacements.txt'), "спс = спасибо`r`nсув = С уважением,\nТест`r`nln2 = C:\\new`r`n", (New-Object Text.UTF8Encoding $false))
foreach ($f in 'ignored-words.txt', 'learned-words.txt', 'app-layouts.txt') { $p = Join-Path $root $f; if (Test-Path $p) { Remove-Item $p } }
if (-not (Test-Path $key)) { New-Item $key | Out-Null }
foreach ($n in 'Typos', 'UndoCtrlZ', 'QuietFullscreen', 'AutoCorrect') { Set-ItemProperty $key -Name $n -Value 1 -Type DWord }
Get-Process KeyFish -ErrorAction SilentlyContinue | Stop-Process; Start-Sleep -Milliseconds 800
explorer.exe (Join-Path $root 'KeyFish.exe'); Start-Sleep 5

$w = '~' * 30
# name, script, expected text, extra Typer argument
$tests = @(
  @('layout',                 'ghbdtn vbh ~~hello ~~~~',                  'привет мир hello ', ''),
  @('short words fixed back', 'hello ~~f vj;yj ~~ghbdtn ~~~~',            'hello а можно привет ', ''),
  @('CapsLock',               '#^ghbdtn ~~vbh ~~~~',                      'Привет мир ', ''),
  @('undo with Pause',        'hello ~~ckjdj ~~~~|~~~~~~',                'hello ckjdj ', ''),
  @('erase and retype',       'hello ~~rjirf ~~~~<<<<<<~~~~rjirf ~~~~',   'hello rjirf ', ''),
  @('replacement',            'hello ~~cgc ~~~~',                         'hello спасибо ', ''),
  @('replacement capital',    'hello ~~^cgc ~~~~',                        'hello Спасибо ', ''),
  @('replacement two lines',  'hello ~~ced ~~~~',                         "hello С уважением,`r`nТест ", ''),
  @('replacement digit, \',   'hello ~~ln2 ~~~~',                         'hello C:\new ', ''),
  @('typos',                  'hello ~~recieve ~~adress ~~~~',            'hello receive address ', ''),
  @('transliteration',        "hello ~~privet~~^|$w",                     'hello привет', ''),
  @('learn from Pause',       "hello ~~ye~~|$w",                          'hello ну', ''),
  @('learned word',           'hello ~~ye ~~~~',                          'hello ну ', ''),
  @('quiet in full screen',   'ghbdtn ~~~~',                              'ghbdtn ', 'full'),
  @('undo with Ctrl+Z',       'hello ~~cjkywt ~~~~%z~~~~~~',              'hello cjkywt ', ''),
  @('Ctrl+Z then a letter',   'hello ~~ljvf ~~~~%z~~~~b~~~~',             'hello ljvf b', ''),
  @('hold Ctrl, Z, release',  'hello ~~dtxth ~~~~[z~~~~]~~b~~~~',         'hello dtxth b', '')
)
$pass = 0; $total = 0
foreach ($t in $tests) {
  $total++
  Start-Process "$bin\Typer.exe" -ArgumentList "`"$bin\out.txt`" `"$($t[1])`" $($t[3])" -Wait
  $r = Get-Content "$bin\out.txt" -Encoding utf8 -Raw
  $ok = $r -eq $t[2]; if ($ok) { $pass++ }
  "{0} {1,-24} [{2}]" -f $(if ($ok) { 'OK  ' } else { 'FAIL' }), $t[0], ($r -replace "`r`n", '⏎')
}
$pages = "file:///$($bin -replace '\\', '/')"
foreach ($b in @(@('browser password', 'pw', 'KFPW', 'KFPW:ghbdtn vbh'), @('browser text field', 'tx', 'KFTX', 'KFTX:привет мир'),
                 @('password after text', 'pw', 'KFPW', 'KFPW:ghbdtn vbh'))) {
  $total++
  Start-Process "$bin\BrowserTyper.exe" -ArgumentList "`"$pages/$($b[1]).html`" $($b[2]) `"ghbdtn vbh ~~~~`" `"$bin\out.txt`"" -Wait
  $r = ((Get-Content "$bin\out.txt" -Encoding utf8 -Raw) -replace ' - \[InPrivate\].*', '').Trim()
  $ok = $r -eq $b[3]; if ($ok) { $pass++ }
  "{0} {1,-24} [{2}]" -f $(if ($ok) { 'OK  ' } else { 'FAIL' }), $b[0], $r
  Start-Sleep 1
}
"passed: $pass of $total"

# Restore everything and start KeyFish normally again
foreach ($f in $files) { $p = Join-Path $root $f; if ($backup[$f]) { [IO.File]::WriteAllBytes($p, $backup[$f]) } elseif (Test-Path $p) { Remove-Item $p } }
Get-Process KeyFish -ErrorAction SilentlyContinue | Stop-Process; Start-Sleep -Milliseconds 800
foreach ($n in $regNames) { if ($null -ne $regBackup[$n]) { Set-ItemProperty $key -Name $n -Value $regBackup[$n] -Type DWord } else { Remove-ItemProperty $key -Name $n -ErrorAction SilentlyContinue } }
explorer.exe (Join-Path $root 'KeyFish.exe')
"tests finished"
