# End-to-end typing tests for KeyFish. They press real keys, so don't touch the keyboard or mouse while they run.
# Your replacements, word lists and settings are backed up to tests\backup first and restored at the end (or on the next run, if this one is cut short).
# Usage: powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1 [-Only <name pattern>] [-Perf]   (-Only 'menu*': those alone, no browser tests)
# -Perf: KeyFish writes its diagnostics log during the run, a longer text is typed at the end, and the timings of the
# keyboard hook, the memory and the processor time of both KeyFish processes are shown
param([string]$Only, [switch]$Perf)
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
$debugLog = Join-Path $data 'debug-log.txt'
if ($Perf) { Set-Content $debugLog '' -Encoding utf8 }
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
  @('clipboard',              'ghbdtn~~%a~~%c~~~~%^q~~~~~~%v~~~~',        'привет', ''),
  # Ctrl+Shift+F2: the text actions menu. Nothing selected: the line before the cursor; the first item is picked with Enter
  @('menu: calculate',        "2^=3^84~~%^@$('_' * 15)`$~~~~~~~~~~",       '14', ''),
  @('menu: uppercase',        "hello world~~%a~~%^@$('_' * 15)`$~~~~~~~~~~", 'HELLO WORLD', ''),
  # Ctrl+Shift+V: the clipboard's text pasted, twice; a second later the user's clipboard is back, and Ctrl+V pastes it
  @('paste without formatting', "abc~~%a~~%c~~<~~%^v~~~~%^v$('~' * 25)%v~~~~", 'abcabcabc', ''),
  @('menu: closed, no change', "hello~~%^@$('_' * 15)``~~~~~~~~ ~~x~~~~",   'hello x', '')
)
$pass = 0; $total = 0
foreach ($t in $tests) {
  if ($Only -and $t[0] -notlike $Only) { continue }
  $total++
  Start-Process "$bin\Typer.exe" -ArgumentList "`"$bin\out.txt`" `"$($t[1])`" $($t[3])" -Wait
  $r = Get-Content "$bin\out.txt" -Encoding utf8 -Raw
  $ok = $r -eq $t[2]; if ($ok) { $pass++ }
  "{0} {1,-24} [{2}]" -f $(if ($ok) { 'OK  ' } else { 'FAIL' }), $t[0], ($r -replace "`r`n", '⏎')
}
$pages = "file:///$($bin -replace '\\', '/')"
if (-not $Only) { foreach ($b in @(@('browser password', 'pw', 'KFPW', 'KFPW:ghbdtn vbh'), @('browser text field', 'tx', 'KFTX', 'KFTX:привет мир'),
                 @('password after text', 'pw', 'KFPW', 'KFPW:ghbdtn vbh'))) {
  $total++
  Start-Process "$bin\BrowserTyper.exe" -ArgumentList "`"$pages/$($b[1]).html`" $($b[2]) `"ghbdtn vbh ~~~~`" `"$bin\out.txt`"" -Wait
  $r = ((Get-Content "$bin\out.txt" -Encoding utf8 -Raw) -replace ' - \[InPrivate\].*', '').Trim()
  $ok = $r -eq $b[3]; if ($ok) { $pass++ }
  "{0} {1,-24} [{2}]" -f $(if ($ok) { 'OK  ' } else { 'FAIL' }), $b[0], $r
  Start-Sleep 1
} }
"passed: $pass of $total"
if ($Perf) {
  # Processor time of both KeyFish processes while about 400 keys of mixed text are typed (words in the wrong layout
  # among them), in a plain text box and in a browser field; then the next half-minute summary is waited for
  function Cpu { $r = @{}; Get-CimInstance Win32_Process -Filter "Name='KeyFish.exe'" | Where-Object ExecutablePath -eq $app | ForEach-Object {
      $k = if ($_.CommandLine -match '--password-guard') { 'guard' } else { 'main' }; $r[$k] = ($_.KernelModeTime + $_.UserModeTime) / 10000 }; $r }
  $words = 'hello ~world ~ghbdtn ~vbh ~this ~is ~ckjdj ~a ~ntrcn ~test ~'
  $c0 = Cpu
  Start-Process "$bin\Typer.exe" -ArgumentList "`"$bin\out.txt`" `"$($words * 8)~~~~`"" -Wait
  $c1 = Cpu
  Start-Process "$bin\BrowserTyper.exe" -ArgumentList "`"$pages/tx.html`" KFTX `"$($words * 8)~~~~`" `"$bin\out.txt`"" -Wait
  $c2 = Cpu
  "text box: KeyFish {0:N0} ms, password check {1:N0} ms of processor" -f ($c1.main - $c0.main), ($c1.guard - $c0.guard)
  "browser:  KeyFish {0:N0} ms, password check {1:N0} ms of processor" -f ($c2.main - $c1.main), ($c2.guard - $c1.guard)
  $before = (Get-Content $debugLog | Where-Object { $_ -match '  perf: ' }).Count
  for ($i = 0; $i -lt 40 -and (Get-Content $debugLog | Where-Object { $_ -match '  perf: ' }).Count -eq $before; $i++) { Start-Sleep 1 }
  Get-Content $debugLog | Where-Object { $_ -match '  (perf|start): ' }
  Get-Process KeyFish | Where-Object Path -eq $app | ForEach-Object {
    "{0,-6} private {1:0.0} MB, working set {2:0.0} MB, processor {3:0.0} s" -f $_.Id, ($_.PrivateMemorySize64 / 1MB), ($_.WorkingSet64 / 1MB), $_.TotalProcessorTime.TotalSeconds }
}
}
finally {
  if ($Perf) { Remove-Item $debugLog -ErrorAction SilentlyContinue }
  # Restore everything and start KeyFish normally again (also when a test fails with an error)
  Restore-Backup
  if (Test-Path (Join-Path $root 'KeyFish.exe')) { explorer.exe (Join-Path $root 'KeyFish.exe') }   # the user's own copy again
  "tests finished"
}
