# End-to-end typing tests for KeyFish. They press real keys, so don't touch the keyboard or mouse while they run.
# Your replacements, word lists and settings are backed up to tests\backup first and restored at the end (or on the next run, if this one is cut short).
# Usage: powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1 [-Only <name pattern>] [-Perf] [-Big]   (-Only 'menu*': those alone, no browser tests)
# -Big: the extended set too (about 45 more scenarios: layouts, switching while typing, replacements, typography, hotkeys, the menu)
# -Perf: KeyFish writes its diagnostics log during the run, a longer text is typed at the end, and the timings of the
# keyboard hook, the memory and the processor time of both KeyFish processes are shown
param([string]$Only, [switch]$Perf, [switch]$Big)
$ErrorActionPreference = 'Stop'
$userCopy = ''   # the KeyFish to start again at the end, found below
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
$files = 'replacements.txt', 'ignored-words.txt', 'learned-words.txt', 'app-layouts.txt', 'exclusions.txt', 'no-auto-layouts.txt'
$regNames = 'Typos', 'StatsTotal', 'UndoCtrlZ', 'QuietFullscreen', 'AutoCorrect', 'QuickKey', 'CorrectionTips', 'Typography', 'Hotkey6', 'IntroShown', 'StatsDays', 'SwitchWhileTyping', 'Hints'
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
# The KeyFish running now is started again at the end: the Store one through its package, a downloaded one by its path
$running = @(Get-Process KeyFish -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -ne $app } | Select-Object -ExpandProperty Path -Unique)
$userCopy = if ($running | Where-Object { $_ -like '*\WindowsApps\*' }) { 'store' } elseif ($running) { $running[0] } else { Join-Path $root 'KeyFish.exe' }
Set-Content (Join-Path $backupDir 'complete') 'ok' -Encoding ascii

[IO.File]::WriteAllText((Join-Path $data 'replacements.txt'), "спс = спасибо`r`nсув = С уважением,\nТест`r`nln2 = C:\\new`r`nxdt = {date}`r`nxcur = (a{cursor}b)`r`nxclip = {clipboard}`r`nxsm = ok 🙂👍🏽`r`n", (New-Object Text.UTF8Encoding $false))
foreach ($f in 'ignored-words.txt', 'learned-words.txt', 'app-layouts.txt', 'no-auto-layouts.txt') { $p = Join-Path $data $f; if (Test-Path $p) { Remove-Item $p } }
# Rules by the test window's title (Typer.exe title=…)
[IO.File]::WriteAllText((Join-Path $data 'exclusions.txt'), "title: KF-off`r`ntitle: KF-hot* = hotkeys`r`n", (New-Object Text.UTF8Encoding $false))
# QuickKey 1: double Shift; CorrectionTips 2: no first-fix tips over the test windows
# Hints: a word KeyFish isn't sure of is shown at the caret, and Pause fixes it
Set-Real ([ordered]@{ Typos = 1; SwitchWhileTyping = 1; UndoCtrlZ = 1; QuietFullscreen = 1; AutoCorrect = 1; QuickKey = 1; Typography = 1; CorrectionTips = 2; Hotkey6 = 196689; IntroShown = 1; Hints = 1 })   # Hotkey6: Ctrl+Shift+Q converts the clipboard
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
  @('switch while typing',    'hello ~~ghbdtnbr~~~~~~',                   'hello приветик', ''),   # no space: switched at the 4th–6th letter
  @('switch erased, back',    'hello ~~ghbdtnbr~~~~<<<<<<<<~~~~a1 ~~~~',  'hello a1 ', ''),   # erased at once: the layout goes back
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
  @('double Shift fixes word', "hello ~~jyf~~!~!$w",                      'hello она', ''),   # three letters: not switched while typing
  @('double Shift undoes fix', "hello ~~rfrfz ~~~~!~!$w",               'hello rfrfz ', ''),   # a word no earlier test sends to Never correct
  @('typography',             'one ~~- ~~two... ~~^9c^0 ~~~~',            'one — two… © ', ''),
  @('clipboard',              'ytn~~%a~~%c~~~~%^q~~~~~~%v~~~~',           'нет', ''),   # three letters: not switched while typing
  # Ctrl+Shift+F2: the text actions menu. Nothing selected: the line before the cursor; the first item is picked with Enter
  @('menu: calculate',        "2^=3^84~~%^@$('_' * 15)`$~~~~~~~~~~",       '14', ''),
  @('menu: uppercase',        "hello world~~%a~~%^@$('_' * 15)`$~~~~~~~~~~", 'HELLO WORLD', ''),
  # Ctrl+Shift+V: the clipboard's text pasted, twice; a second later the user's clipboard is back, and Ctrl+V pastes it
  @('paste without formatting', "abc~~%a~~%c~~<~~%^v~~~~%^v$('~' * 25)%v~~~~", 'abcabcabc', ''),
  @('menu: closed, no change', "hello~~%^@$('_' * 15)``~~~~~~~~ ~~x~~~~",   'hello x', '')
)
# The extended set (-Big). Words undone here go to Never correct, so each such word is used once and after the tests
# that need it fixed
$m = '%^@' + ('_' * 15)   # the text actions menu, waited for
$e = '~' * 10
$bigTests = @(
  # Text selected and erased, or erased back to a word's start: the next phrase is fixed whole, its first letter too
  @('x: Ctrl+A, Backspace',   'hello ~~%a~~<~~f z ;le ~~~~',              'а я жду ', ''),   # no '[' here: in the script it holds Ctrl
  @('x: Shift+Home, erase',   'hello world~~ ~~^(~~<~~f vj;yj ~~~~',      'а можно ', ''),
  @('x: two words erased',    'hello big world ~~<<<<<<<<<<~~f vj;yj ~~~~', 'hello а можно ', ''),
  @('x: erased into a word',  'hello world ~~<<<~~ghbdtn ~~~~',           'hello worghbdtn ', ''),   # the caret is inside "world": left as typed
  # Hotkeys where KeyFish is off go on to the app (Shift+F3 is Find previous in editors); double Shift sends nothing there
  @('x: off: hotkey passed on', 'hello~~^&~~~~',                          'hello[Shift+F3]', 'title=KF-off;keys'),
  @('x: hotkey kept to act',  'hello~~%a~~^&~~~~',                        'Hello', 'keys'),
  @('x: off: double Shift',   "hello~~!~!$w",                             'hello', 'title=KF-off;keys'),
  @('x: hotkeys only: dbl Shift', "hello~~!~!$w",                         'руддщ', 'title=KF-hotkeys;keys'),
  # A case of its own stays; inverted capitals with Caps Lock on are put right, a name too, and Caps Lock goes off
  @('x: eSIM stays',          'e^s^i^m ~~^o^auth ~~~~',                   'eSIM OAuth ', ''),
  @('x: Caps Lock, a name',   '#^kowalski ~~~~',                          'Kowalski ', ''),
  # Layouts
  @('x: phrase',              'z k.,k. ghjuhfvvbhjdfnm ~~~~',             'я люблю программировать ', ''),
  @('x: Russian goes on',     'ghbdtn ~~rfr ltkf ~~~~',                   'привет как дела ', ''),
  @('x: English in ru',       'ghbdtn ~~computer ~~~~',                   'привет computer ', ''),
  @('x: , and . as letters',  'j,sxyj ~~k.,jdm ~~~~',                     'обычно любовь ', ''),
  @('x: ; as a letter',       'kj;m ~~~~',                                'ложь ', ''),
  @('x: comma after word',    'ghbdtn, ~~vbh ~~~~',                       'привет, мир ', ''),
  @('x: ru comma key',        'rfr ltkf^/ ~~~~',                          'как дела, ', ''),   # Shift+/ is the comma in Russian
  # Modes by the window title (exclusions.txt above)
  @('x: title, off',          'hello ~~ghbdtn ~~~~',                      'hello ghbdtn ', 'title=KF-off'),
  @('x: title, off: hotkeys', 'hello~~%a~~%^ ~~~~~~',                     'hello', 'title=KF-off'),
  @('x: title, hotkeys only', 'hello ~~ghbdtn ~~~~',                      'hello ghbdtn ', 'title=KF-hotkeys'),
  @('x: hotkeys only: hotkey','hello~~%a~~%^ ~~~~~~',                     'руддщ', 'title=KF-hotkeys'),
  # Russian taken out of automatic fixes (no-auto-layouts.txt, written for this test only); the hotkey still converts
  @('x: no auto into ru',     'hello ~~ghbdtn ~~~~',                      'hello ghbdtn ', 'noauto'),
  @('x: no auto, hotkey',     'ghbdtn~~%a~~%^ ~~~~~~',                    'привет', 'noauto'),
  # Placeholders in replacements
  @('x: {date}',              'hello ~~xdt ~~~~',                         "hello $((Get-Date).ToString('d')) ", ''),
  @('x: {cursor}',            'hello ~~xcur ~~z~~~~',                     'hello (azb) ', ''),
  @('x: {clipboard}',         'qwe~~%a~~%c~~xclip ~~~~',                  'qwe ', ''),
  @('x: ; at the end is ж',   'a1 ~~yj; ~~~~',                            'a1 нож ', ''),   # a1: a warm-up word that sets no language
  @('x: ж after English',     'hello ~~yj; ~~~~',                         'hello нож ', ''),   # "yj" is not in the extended dictionary
  @('x: capital',             '^ghbdtn ~~~~',                             'Привет ', ''),
  @('x: all capitals',        '^g^h^b^d^t^n ~~~~',                        'ПРИВЕТ ', ''),
  @('x: Shift held long, ru', 'hello ~~^g^hbdtn ~~~~',                    'hello Привет ', ''),
  @('x: Shift held long, en', '^h^ello ~~~~',                             'Hello ', ''),
  @('x: CapsLock, English',   '#^hello ~~~~',                             'Hello ', ''),
  @('x: short words first',   'f ns ult ~~~~',                            'а ты где ', ''),
  @('x: one letter first',    'f tckb ~~~~',                              'а если ', ''),
  @('x: whole phrase back',   'f z f jy f vs vjcrdf ~~~~',                'а я а он а мы москва ', ''),   # more than three words before the one fixed
  @('x: English stays',       '^i use ghbdtn ~~~~',                        'I use привет ', ''),   # real words of the typed language end the phrase
  # Typing on drops the hint: Pause then converts the last word as usual, not the hinted one. Before "hint taken", which
  # teaches KeyFish the word
  @('x: emoji replacement',  'hello ~~xsm ~~~~',                         'hello ok 🙂👍🏽 ', ''),   # characters beyond U+FFFF, as surrogate pairs
  @('x: emoji, Pause',       'hello ~~xsm ~~~~|~~~~~~',                  'hello xsm ', ''),   # taking it back erases an emoji as one character
  @('x: hint passed by',      "hello ~~dfot ~~ye~~|$w",                  'hello dfot ну', ''),
  @('x: hint taken',          'hello ~~dfot ~~~~|~~~~~~',                 'hello ваще ', ''),   # left alone (not sure), the hint is taken with Pause
  @('x: two spaces, no retro', 'hello ~~f  ~~ghjuhfvvf ~~~~',            'hello f  программа ', ''),   # a double space ends the chain of words fixed back
  @('x: year in a sentence',  'd 2026 ujle ~~~~',                         'в 2026 году ', ''),
  @('x: Backspace in word',   'ghbdtx<n ~~~~',                            'привет ', ''),
  @('x: Enter ends a word',   'ghbdtn$~~~~',                              "привет`r`n", ''),
  # Left alone
  @('x: English sentence',    'the quick brown fox ~~~~',                 'the quick brown fox ', ''),
  @('x: tech words',          'git push nginx mysql ~~~~',                'git push nginx mysql ', ''),
  @('x: link',                'www.google.com ~~~~',                      'www.google.com ', ''),
  @('x: digits',              'abc123 x86 ~~~~',                          'abc123 x86 ', ''),
  # Switching while typing
  @('x: switch, long word',   'hello ~~ghjuhfvvf ~~~~',                   'hello программа ', ''),
  @('x: switch, sentence',    'ghjuhfvvf hf,jnftn ~~~~',                  'программа работает ', ''),
  @('x: switch to English',   'ghbdtn ~~keyboard ~~~~',                   'привет keyboard ', ''),
  @('x: switch, Pause',       'hello ~~ghjuhfvvf ~~~~|~~~~~~',            'hello ghjuhfvvf ', ''),
  @('x: switch, Ctrl+Z',      'hello ~~rjvgm.nth ~~~~%z~~~~~~',           'hello rjvgm.nth ', ''),
  # Undo and learning
  @('x: undone stays',        'hello ~~ckjdjv ~~~~|~~~~~~ckjdjv ~~~~',    'hello ckjdjv ckjdjv ', ''),
  @('x: fix erased, retyped', 'hello ~~gjxnf ~~~~<<<<<<~~~~gjxnf ~~~~',   'hello gjxnf ', ''),
  # Replacements
  @('x: replacement in ru',   'ghbdtn ~~cgc ~~~~',                        'привет спасибо ', ''),
  @('x: replacement CAPS',    'hello ~~^c^g^c ~~~~',                      'hello Спасибо ', ''),
  @('x: replacement, Enter',  'hello ~~cgc$~~~~',                         "hello спасибо`r`n", ''),
  @('x: replacement, Pause',  'hello ~~cgc ~~~~|~~~~~~',                  'hello cgc ', ''),
  # Typography and typos
  @('x: (tm) and (r)',        'one ~~^9tm^0 ~~^9r^0 ~~~~',                'one ™ ® ', ''),
  @('x: double hyphen',       'one ~~-- ~~two ~~~~',                      'one — two ', ''),
  @('x: hyphen first',        '- ~~one ~~~~',                             '- one ', ''),
  @('x: ellipsis',            'wait... ~~~~',                             'wait… ', ''),
  @('x: typo',                'hello ~~becuase ~~~~',                     'hello because ', ''),
  @('x: transliteration',     "hello ~~spasibo~~^|$w",                    'hello спасибо', ''),
  # Hotkeys on selected text
  @('x: selection layout',    'hello~~%a~~%^ ~~~~~~',                     'руддщ', ''),
  @('x: selection case',      'hello~~%a~~^&~~~~~~',                      'Hello', ''),
  @('x: selection case x2',   'hello~~%a~~^&~~~~~~^&~~~~~~',              'HELLO', ''),
  @('x: line before cursor',  'hello world~~*|~~~~~~',                    'руддщ цщкдв', ''),
  # The text actions menu
  @('x: menu append result',  ('2^=3^84~~' + $m + '}$' + $e),             '2+3*4 = 14', ''),
  @('x: menu lowercase',      ('^hello ^world~~%a~~' + $m + '}$' + $e),   'hello world', ''),
  @('x: menu sentence case',  ('hello world~~%a~~' + $m + '}}$' + $e),    'Hello world', ''),
  @('x: menu each word',      ('hello world~~%a~~' + $m + '}}}$' + $e),   'Hello World', ''),
  @('x: menu layout',         ('hello~~%a~~' + $m + '}}}}$' + $e),        'руддщ', ''),
  @('x: menu transliterate',  ('privet~~%a~~' + $m + '}}}}}$' + $e),      'привет', '')
)
if ($Big) { $tests += $bigTests }
$pass = 0; $total = 0
foreach ($t in $tests) {
  if ($Only -and $t[0] -notlike $Only) { continue }
  $total++
  $noAuto = Join-Path $data 'no-auto-layouts.txt'
  if ($t[3] -eq 'noauto') { [IO.File]::WriteAllText($noAuto, "4190419`r`n"); Start-Sleep -Milliseconds 2500 }   # KeyFish reads it within 2 s
  Start-Process "$bin\Typer.exe" -ArgumentList "`"$bin\out.txt`" `"$($t[1])`" $(if ($t[3] -ne 'noauto') { $t[3] })" -Wait
  if ($t[3] -eq 'noauto') { Remove-Item $noAuto; Start-Sleep -Milliseconds 2500 }
  $r = Get-Content "$bin\out.txt" -Encoding utf8 -Raw
  $ok = $r -ceq $t[2]; if ($ok) { $pass++ }   # -ceq: the case is part of the result (Hello, HELLO, eSIM)
  "{0} {1,-24} [{2}]" -f $(if ($ok) { 'OK  ' } else { 'FAIL' }), $t[0], ($r -replace "`r`n", '⏎')
}
$pages = "file:///$($bin -replace '\\', '/')"
if (-not $Only) { foreach ($b in @(@('browser password', 'pw', 'KFPW', 'KFPW:ghbdtn vbh'), @('browser text field', 'tx', 'KFTX', 'KFTX:привет мир'),
                 @('password after text', 'pw', 'KFPW', 'KFPW:ghbdtn vbh'))) {
  $total++
  Start-Process "$bin\BrowserTyper.exe" -ArgumentList "`"$pages/$($b[1]).html`" $($b[2]) `"ghbdtn vbh ~~~~`" `"$bin\out.txt`"" -Wait
  $r = ((Get-Content "$bin\out.txt" -Encoding utf8 -Raw) -replace ' - \[InPrivate\].*', '').Trim()
  $ok = $r -ceq $b[3]; if ($ok) { $pass++ }
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
  if ($userCopy -eq 'store') { explorer.exe 'shell:AppsFolder\MaximPravko.KeyFish_q2dp1ns3fa4wr!KeyFish' }   # the user's own copy again
  elseif (Test-Path $userCopy) { explorer.exe $userCopy }
  "tests finished"
}
