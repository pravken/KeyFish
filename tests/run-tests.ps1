# End-to-end typing tests for KeyFish. They press real keys, so don't touch the keyboard or mouse while they run.
# Your replacements, word lists and settings are backed up to tests\backup first and restored at the end (or on the next run, if this one is cut short).
# Usage: powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# The tests run the build in tests\bin (build.bat with OUT=tests\bin\KeyFish.exe), which keeps its own lists next to it.
# The user's KeyFish.exe in the project folder and its files are never touched: it is only closed for the run and started again
$app = Join-Path $PSScriptRoot 'bin\KeyFish.exe'
$data = Join-Path $PSScriptRoot 'bin'
if (-not (Test-Path $app)) { throw "Build tests\bin\KeyFish.exe first: set OUT=tests\bin\KeyFish.exe and run build.bat" }
$bin = Join-Path $PSScriptRoot 'bin'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force $bin | Out-Null
& $csc /nologo /codepage:65001 /target:winexe /out:"$bin\Typer.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\Typer.cs"
& $csc /nologo /codepage:65001 /out:"$bin\BrowserTyper.exe" "$PSScriptRoot\BrowserTyper.cs"
Copy-Item "$PSScriptRoot\pw.html", "$PSScriptRoot\tx.html" $bin -Force

# The user's files and settings are backed up to tests\backup before the tests change them. The copy is on disk,
# so a run that was cut short (window closed, power lost) is undone by the next run before it starts.
# "complete" is written last: without it the copy is unfinished, and the user's own files were never touched
$files = 'replacements.txt', 'ignored-words.txt', 'learned-words.txt', 'app-layouts.txt'
$regNames = 'Typos', 'StatsTotal', 'UndoCtrlZ', 'QuietFullscreen', 'AutoCorrect', 'QuickKey', 'CorrectionTips', 'Typography', 'Hotkey6', 'IntroShown', 'StatsDays'
$backupDir = Join-Path $PSScriptRoot 'backup'

# KeyFish's settings in the registry go through reg.exe in a .cmd started by Explorer: when this script runs inside a
# sandboxed app (an MSIX-packaged terminal or assistant), its own registry writes stay in the sandbox and KeyFish,
# started by Explorer too, would never see them
function Real-Reg($lines) {
  $done = Join-Path $bin 'reg.done'
  Remove-Item $done -ErrorAction SilentlyContinue
  $script = @('@echo off') + $lines + ('echo done> "' + $done + '"')
  [IO.File]::WriteAllLines((Join-Path $bin 'reg.cmd'), [string[]]$script, (New-Object Text.UTF8Encoding $false))
  explorer.exe (Join-Path $bin 'reg.cmd')
  for ($i = 0; $i -lt 100 -and -not (Test-Path $done); $i++) { Start-Sleep -Milliseconds 100 }
  if (-not (Test-Path $done)) { throw 'reg.cmd did not run' }
}
function Set-Real($values) {
  Real-Reg ($values.GetEnumerator() | ForEach-Object {
    if ("$($_.Value)" -like 'sz:*') { "reg add HKCU\Software\KeyFish /v $($_.Key) /t REG_SZ /d ""$("$($_.Value)".Substring(3))"" /f >nul" }
    elseif ("$($_.Value)" -ne '') { "reg add HKCU\Software\KeyFish /v $($_.Key) /t REG_DWORD /d $($_.Value) /f >nul" }
    else { "reg delete HKCU\Software\KeyFish /v $($_.Key) /f >nul 2>nul" } })
}
# name=value for each, nothing after = where the value isn't set
function Get-Real($names) {
  $out = Join-Path $bin 'reg.txt'
  Real-Reg (@('type nul > "' + $out + '"') + ($names | ForEach-Object { '(reg query HKCU\Software\KeyFish /v ' + $_ + ' 2>nul | find "REG_") >> "' + $out + '"' }))
  $found = @{}
  foreach ($l in Get-Content $out) {
    if ($l -match '^\s*(\S+)\s+REG_DWORD\s+0x([0-9a-fA-F]+)') { $found[$Matches[1]] = [Convert]::ToInt32($Matches[2], 16) }
    elseif ($l -match '^\s*(\S+)\s+REG_SZ\s+(.*)$') { $found[$Matches[1]] = 'sz:' + $Matches[2].Trim() }   # text values (StatsDays) as sz:text
  }
  $names | ForEach-Object { "$_=" + $(if ($found.ContainsKey($_)) { $found[$_] } else { '' }) }
}

function Restore-Backup {
  Get-Process KeyFish -ErrorAction SilentlyContinue | Stop-Process; Start-Sleep -Milliseconds 800
  foreach ($f in $files) {
    $p = Join-Path $data $f; $saved = Join-Path $backupDir $f
    if (Test-Path $saved) { Copy-Item $saved $p -Force } elseif (Test-Path $p) { Remove-Item $p }
  }
  $values = [ordered]@{}
  foreach ($line in Get-Content (Join-Path $backupDir 'registry.txt')) { $n, $v = $line -split '=', 2; $values[$n] = $v }
  Set-Real $values
  Remove-Item $backupDir -Recurse -Force
}

if (Test-Path (Join-Path $backupDir 'complete')) { Restore-Backup; "The previous run was cut short: your files and settings were restored first" }
elseif (Test-Path $backupDir) { Remove-Item $backupDir -Recurse -Force }

New-Item -ItemType Directory -Force $backupDir | Out-Null
foreach ($f in $files) { $p = Join-Path $data $f; if (Test-Path $p) { Copy-Item $p (Join-Path $backupDir $f) } }
Get-Real $regNames | Set-Content (Join-Path $backupDir 'registry.txt') -Encoding ascii
Set-Content (Join-Path $backupDir 'complete') 'ok' -Encoding ascii

[IO.File]::WriteAllText((Join-Path $data 'replacements.txt'), "спс = спасибо`r`nсув = С уважением,\nТест`r`nln2 = C:\\new`r`n", (New-Object Text.UTF8Encoding $false))
foreach ($f in 'ignored-words.txt', 'learned-words.txt', 'app-layouts.txt') { $p = Join-Path $data $f; if (Test-Path $p) { Remove-Item $p } }
# QuickKey 1: double Shift; CorrectionTips 2: no first-fix tips over the test windows
Set-Real ([ordered]@{ Typos = 1; UndoCtrlZ = 1; QuietFullscreen = 1; AutoCorrect = 1; QuickKey = 1; Typography = 1; CorrectionTips = 2; Hotkey6 = 196689; IntroShown = 1 })   # Hotkey6: Ctrl+Shift+Q converts the clipboard
Get-Process KeyFish -ErrorAction SilentlyContinue | Stop-Process; Start-Sleep -Milliseconds 800
explorer.exe $app; Start-Sleep 5

try {
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
  @('Pause twice keeps word',  "hello ~~world~~|$w|$w ~~world ~~~~",    'hello world world ', ''),   # back to the start: nothing learned
  @('quiet in full screen',   'ghbdtn ~~~~',                              'ghbdtn ', 'full'),
  @('undo with Ctrl+Z',       'hello ~~cjkywt ~~~~%z~~~~~~',              'hello cjkywt ', ''),
  @('Ctrl+Z then a letter',   'hello ~~ljvf ~~~~%z~~~~b~~~~',             'hello ljvf b', ''),
  @('hold Ctrl, Z, release',  'hello ~~dtxth ~~~~[z~~~~]~~b~~~~',         'hello dtxth b', ''),
  @('double Shift fixes word', "hello ~~ghjcnj~~!~!$w",                   'hello просто', ''),
  @('double Shift undoes fix', "hello ~~rfrfz ~~~~!~!$w",               'hello rfrfz ', ''),   # a word no earlier test sends to Never correct
  @('typography',             'one ~~- ~~two... ~~^9c^0 ~~~~',            'one — two… © ', ''),
  @('clipboard',              'ghbdtn~~%a~~%c~~~~%^q~~~~~~%v~~~~',        'привет', '')
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
}
finally {
  # Restore everything and start KeyFish normally again (also when a test fails with an error)
  Restore-Backup
  explorer.exe (Join-Path $root 'KeyFish.exe')   # the user's own copy again
  "tests finished"
}
