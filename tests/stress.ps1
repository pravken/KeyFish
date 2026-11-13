# Stress test: why KeyFish sometimes doesn't fix a word. A long text from tests\corpus (Russian typed with the English
# layout on, English among it) is typed with real keys, first calmly (the baseline), then under harder conditions: fast
# typing, keys overlapping, a busy processor, an app that answers slowly, Notepad, Edge. Each run is compared word by
# word with the baseline, and each word that came out differently is shown with what KeyFish wrote about it in its
# diagnostics log (debug-log.txt: reasons only, never words). The baseline is compared with the intended text too:
# those are the detector's own misses, the same at any speed.
# Don't touch the keyboard or mouse while it runs (about a minute per run). The test build in tests\bin is used; your
# settings and lists are backed up first and restored at the end, and the KeyFish you had running is started again.
# Usage: powershell -ExecutionPolicy Bypass -File tests\stress.ps1 [-Runs normal,fast,...] [-Repeat 2] [-Sentences 30] [-Seed 1] [-Rich]
# -Rich: capitals and punctuation stay, typed with the keys they take in their own layout ("Привет, как дела?" is typed
# as the keys of "Ghbdtn? rfr ltkf&" while the English layout is on); English words keep their apostrophes
# -Typos: in about one word in six a wrong letter is typed in the middle and erased with Backspace at once
# -Lines: sentences end with Enter instead of a space (not for edge: its field is a single line)
# -ScriptDir: type the ideal.txt / script.txt found there instead (stress-langs.ps1 makes them)
# Runs: normal fast rollover burst cpu lagapp notepad edge (default: all). Report: tests\bin\stress\report.txt
param([string[]]$Runs, [int]$Repeat = 1, [int]$Sentences = 30, [int]$Seed = 1, [switch]$Rich, [switch]$Typos, [switch]$Lines, [string]$ScriptDir)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$bin = Join-Path $PSScriptRoot 'bin'
$app = Join-Path $bin 'KeyFish.exe'
$out = Join-Path $bin 'stress'
if (-not (Test-Path $app)) { throw "Build tests\bin\KeyFish.exe first: set OUT=tests\bin\KeyFish.exe and run build.bat" }
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /codepage:65001 /target:winexe /out:"$bin\StressTyper.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\StressTyper.cs"
if ($LASTEXITCODE -ne 0) { throw 'StressTyper.cs did not compile' }
Copy-Item "$PSScriptRoot\tx.html" $bin -Force
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

$all = [ordered]@{
  normal   = 'delay=55 jitter=25'
  fast     = 'delay=30 jitter=15'
  rollover = 'delay=35 jitter=15 rollover=1'
  burst    = 'delay=18 jitter=8 rollover=1'
  cpu      = 'delay=50 jitter=20'
  lagapp   = 'delay=50 jitter=20 lag=150/400'
  notepad  = 'delay=50 jitter=20 target=notepad'
  edge     = "delay=50 jitter=20 target=edge url=file:///$($bin -replace '\\', '/')/tx.html"
}
if ($Runs) { $Runs = $Runs -split ',' } else { $Runs = @($all.Keys) }
foreach ($r in $Runs) { if (-not $all.Contains($r)) { throw "Unknown run: $r (known: $($all.Keys -join ', '))" } }

# --- The text: sentences from the corpus, Russian ones typed as keys of the English layout -------------------------
# What each key types in the Russian layout, unshifted and with Shift, against the English layout's character for it
$ru = 'йцукенгшщзхъфывапролджэячсмитьбюё.ЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮЁ,!"№;%:?*()'
$en = "qwertyuiop[]asdfghjkl;'zxcvbnm,.``/QWERTYUIOP{}ASDFGHJKL:`"ZXCVBNM<>~?!@#$%^&*()"
function Keys($word) {
  if ($word -notmatch '[а-яё]') { return $word }   # English: typed as it is
  $sb = New-Object Text.StringBuilder
  foreach ($c in $word.ToCharArray()) { $i = $ru.IndexOf($c); [void]$sb.Append($(if ($i -ge 0) { $en[$i] } else { $c })) }
  $sb.ToString()
}
function Words($line) {
  if ($Rich) {
    # As written, with a mark after the word; words with digits, quotes or a Cyrillic word with Latin letters are left out
    return @($line -split '\s+' | Where-Object { $_ -cmatch "^([A-Za-z][A-Za-z'-]*|[А-Яа-яЁё][А-Яа-яЁё-]*)[,.!?:;]?$" })
  }
  # Lower case, letters only: punctuation and digits are other tests' business, and a word with them is left alone anyway
  @($line.ToLowerInvariant() -split '\s+' | ForEach-Object { $_.Trim('.,!?:;"()«»—-') } |
    Where-Object { $_ -cmatch '^([a-z]+|[а-яё]+)$' })
}
if ($ScriptDir) {
  $utf8 = New-Object Text.UTF8Encoding $false
  Copy-Item "$ScriptDir\ideal.txt", "$ScriptDir\script.txt" $out -Force
  $ideal = @((Get-Content "$out\ideal.txt" -Encoding utf8 -Raw) -split ' ')
  $typed = @('')
} else {
$rnd = New-Object Random $Seed
$ruLines = @(Get-Content "$PSScriptRoot\corpus\ru.txt" -Encoding utf8 | Where-Object { $_ -and -not $_.StartsWith('#') })
$enLines = @(Get-Content "$PSScriptRoot\corpus\en.txt" -Encoding utf8 | Where-Object { $_ -and -not $_.StartsWith('#') })
$ideal = New-Object Collections.Generic.List[string]
$typed = New-Object Collections.Generic.List[string]
for ($i = 0; $i -lt $Sentences; $i++) {
  # Mostly Russian, as the users who need KeyFish type; every third sentence English
  $line = if ($i % 3 -eq 2) { $enLines[$rnd.Next($enLines.Count)] } else { $ruLines[$rnd.Next($ruLines.Count)] }
  $ws = @(Words $line)
  for ($j = 0; $j -lt $ws.Count; $j++) {
    $k = Keys $ws[$j]
    if ($Typos -and $k.Length -ge 4 -and $rnd.Next(6) -eq 0) {
      $at = 1 + $rnd.Next($k.Length - 2)
      $k = $k.Substring(0, $at) + 'qwertyuiopasdfghjklzxcvbnm'[$rnd.Next(26)] + "`b" + $k.Substring($at)
    }
    $ideal.Add($ws[$j]); $typed.Add($k)
  }
  if ($Lines -and $typed.Count -gt 0) { $typed[$typed.Count - 1] += "`r" }
}
$utf8 = New-Object Text.UTF8Encoding $false
[IO.File]::WriteAllText("$out\ideal.txt", ($ideal -join ' '), $utf8)
[IO.File]::WriteAllText("$out\script.txt", ((($typed -join ' ') + ' ') -replace "`r ", "`r"), $utf8)
}
"text: $($ideal.Count) words"

# --- Settings, backed up as in run-tests.ps1 (a run cut short is undone by the next one) ---------------------------
$files = 'replacements.txt', 'ignored-words.txt', 'learned-words.txt', 'app-layouts.txt', 'exclusions.txt', 'no-auto-layouts.txt'
$regNames = 'AutoCorrect', 'SwitchWhileTyping', 'Typos', 'Typography', 'QuietFullscreen', 'CorrectionTips', 'IntroShown'
$backupDir = Join-Path $PSScriptRoot 'backup-stress'
# Registry through reg.exe in a .cmd started by Explorer: a sandboxed shell's own writes would stay in its sandbox
function Real-Reg($lines) {
  $done = Join-Path $bin 'reg.done'
  Remove-Item $done -ErrorAction SilentlyContinue
  $script = @('@echo off') + $lines + ('echo done> "' + $done + '"')
  [IO.File]::WriteAllLines((Join-Path $bin 'reg.cmd'), [string[]]$script, $utf8)
  explorer.exe (Join-Path $bin 'reg.cmd')
  for ($i = 0; $i -lt 100 -and -not (Test-Path $done); $i++) { Start-Sleep -Milliseconds 100 }
  if (-not (Test-Path $done)) { throw 'reg.cmd did not run' }
}
function Set-Real($values) {
  Real-Reg ($values.GetEnumerator() | ForEach-Object {
    if ("$($_.Value)" -ne '') { "reg add HKCU\Software\KeyFish /v $($_.Key) /t REG_DWORD /d $($_.Value) /f >nul" }
    else { "reg delete HKCU\Software\KeyFish /v $($_.Key) /f >nul 2>nul" } })
}
function Get-Real($names) {
  $f = Join-Path $bin 'reg.txt'
  Real-Reg (@('type nul > "' + $f + '"') + ($names | ForEach-Object { '(reg query HKCU\Software\KeyFish /v ' + $_ + ' 2>nul | find "REG_") >> "' + $f + '"' }))
  $found = @{}
  foreach ($l in Get-Content $f) { if ($l -match '^\s*(\S+)\s+REG_DWORD\s+0x([0-9a-fA-F]+)') { $found[$Matches[1]] = [Convert]::ToInt32($Matches[2], 16) } }
  $names | ForEach-Object { "$_=" + $(if ($found.ContainsKey($_)) { $found[$_] } else { '' }) }
}
function Stop-KeyFish { Get-Process KeyFish -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 800 }
function Restore-Backup {
  Stop-KeyFish
  foreach ($f in $files) {
    $p = Join-Path $bin $f; $saved = Join-Path $backupDir $f
    if (Test-Path $saved) { Copy-Item $saved $p -Force } elseif (Test-Path $p) { Remove-Item $p }
  }
  $values = [ordered]@{}
  foreach ($line in Get-Content (Join-Path $backupDir 'registry.txt')) { $n, $v = $line -split '=', 2; $values[$n] = $v }
  Set-Real $values
  $was = Get-Content (Join-Path $backupDir 'running.txt')
  Remove-Item $backupDir -Recurse -Force
  # The KeyFish that ran before: the Store one through its package, a downloaded one by its path
  if ($was -eq 'store') { explorer.exe 'shell:AppsFolder\MaximPravko.KeyFish_q2dp1ns3fa4wr!KeyFish' }
  elseif ($was -and (Test-Path $was)) { explorer.exe $was }
}

if (Test-Path (Join-Path $backupDir 'complete')) { Restore-Backup; 'The previous run was cut short: your files and settings were restored first' }
elseif (Test-Path $backupDir) { Remove-Item $backupDir -Recurse -Force }
New-Item -ItemType Directory -Force $backupDir | Out-Null
# A copy started a moment ago (by the previous test run's end) may not be up yet
for ($i = 0; $i -lt 20 -and -not (Get-Process KeyFish -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -ne $app }); $i++) { Start-Sleep -Milliseconds 500 }
$running = @(Get-Process KeyFish -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -ne $app } | Select-Object -ExpandProperty Path -Unique)
$was = if ($running | Where-Object { $_ -like '*\WindowsApps\*' }) { 'store' } elseif ($running) { $running[0] } else { '' }
Set-Content (Join-Path $backupDir 'running.txt') $was -Encoding utf8
foreach ($f in $files) { $p = Join-Path $bin $f; if (Test-Path $p) { Copy-Item $p (Join-Path $backupDir $f) } }
Get-Real $regNames | Set-Content (Join-Path $backupDir 'registry.txt') -Encoding ascii
Set-Content (Join-Path $backupDir 'complete') 'ok' -Encoding ascii

try {
  # Empty lists, not missing ones: without its files next to it the test build would keep its data in %APPDATA%\KeyFish
  foreach ($f in $files) { [IO.File]::WriteAllText((Join-Path $bin $f), '', $utf8) }
  # Typo fixes are applied only if nothing was typed after the word, so they depend on speed by design: off here
  Set-Real ([ordered]@{ AutoCorrect = 1; SwitchWhileTyping = 1; Typos = 0; Typography = 0; QuietFullscreen = 1; CorrectionTips = 2; IntroShown = 1 })
  Stop-KeyFish
  $debugLog = Join-Path $bin 'debug-log.txt'
  Set-Content $debugLog '' -Encoding utf8   # its presence turns KeyFish's diagnostics on
  explorer.exe $app; Start-Sleep 6
  # KeyFish marks the folder it keeps its data in while it runs: the log must be read from there
  if (-not (Test-Path (Join-Path $bin '~keyfish.running'))) { throw 'The test KeyFish did not start in tests\bin (no ~keyfish.running there)' }

  function Run($name, $options, $burn) {
    $o = Join-Path $out "$name.txt"
    $load = $null
    if ($burn) { $load = Start-Process "$bin\StressTyper.exe" -ArgumentList "burn 600 $burn" -PassThru; Start-Sleep 1 }
    try { Start-Process "$bin\StressTyper.exe" -ArgumentList (@('type', "`"$out\script.txt`"", "`"$o`"") + ($options -split ' ')) -Wait }
    finally { if ($load) { Stop-Process -Id $load.Id -Force -ErrorAction SilentlyContinue } }
    $text = if (Test-Path $o) { Get-Content $o -Encoding utf8 -Raw } else { 'missing' }
    "{0,-12} done{1}" -f $name, $(if ($text -like 'NOFOCUS*') { ": $text" } else { '' })
    Start-Sleep 2
  }

  Run 'baseline' 'delay=90' $null
  $pairs = @()
  for ($k = 1; $k -le $Repeat; $k++) {
    foreach ($r in $Runs) {
      $name = if ($Repeat -gt 1) { "$r-$k" } else { $r }
      Run $name $all[$r] $(if ($r -eq 'cpu') { 2 * [Environment]::ProcessorCount } else { $null })
      $pairs += "$name=$out\$name.txt"
    }
  }
  Start-Sleep 3   # the last log lines are written on KeyFish's file thread
  Copy-Item $debugLog "$out\debug-log.txt"
  & "$bin\StressTyper.exe" report "$out\ideal.txt" "$out\baseline.txt" "$out\debug-log.txt" "$out\report.txt" @pairs | Out-Null
  for ($i = 0; $i -lt 50 -and -not (Test-Path "$out\report.txt"); $i++) { Start-Sleep -Milliseconds 200 }
  Get-Content "$out\report.txt" -Encoding utf8
}
finally {
  Remove-Item (Join-Path $bin 'debug-log.txt') -ErrorAction SilentlyContinue
  Restore-Backup
  'stress test finished'
}
