# Chaos test (tests\ChaosTyper.cs): random words and edits typed into a test window.
#  1. "en": English words and plain edits, typed twice with the same seed — once with KeyFish stopped, once running.
#     The texts must be the same: anything else is KeyFish changing correct English or losing keys.
#  2. "mixed": Russian words typed on the English layout among English ones, with CapsLock, single Shift taps, Pause and
#     clicks; then "ghbdtn " at the end must still come out "привет " — KeyFish didn't fall over or get stuck.
#  3. KeyFish's memory, handles and threads before and after (leaks).
# Don't touch the keyboard or mouse while it runs (it moves the mouse to click into its own window).
# Usage: powershell -ExecutionPolicy Bypass -File tests\chaos.ps1 [-Ops 400] [-Seeds 1,2,3]
param([int]$Ops = 400, [string]$Seeds = "1,2,3")
$seedList = @($Seeds -split '[, ]+' | Where-Object { $_ } | ForEach-Object { [int]$_ })
$ErrorActionPreference = 'Stop'
$bin = Join-Path $PSScriptRoot 'bin'
$app = Join-Path $bin 'KeyFish.exe'
$out = Join-Path $bin 'chaos'
$utf8 = New-Object Text.UTF8Encoding $false
if (-not (Test-Path $app)) { throw "Build tests\bin\KeyFish.exe first" }
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /out:"$bin\ChaosTyper.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\ChaosTyper.cs"
if ($LASTEXITCODE) { throw 'ChaosTyper.cs did not compile' }
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

# Settings and lists backed up and restored as in stress.ps1 (registry through Explorer: a sandboxed shell's writes stay in its sandbox)
$files = 'replacements.txt', 'ignored-words.txt', 'learned-words.txt', 'app-layouts.txt', 'exclusions.txt', 'no-auto-layouts.txt'
$regNames = 'AutoCorrect', 'SwitchWhileTyping', 'Typos', 'Typography', 'QuietFullscreen', 'CorrectionTips', 'IntroShown', 'QuickKey', 'Hints'
$backupDir = Join-Path $PSScriptRoot 'backup-chaos'
$script:calls = 0
function Real-Reg($lines) {
  $n = ++$script:calls
  $done = Join-Path $bin "chaos-reg-$n.done"
  $script = @('@echo off') + $lines + ('echo done> "' + $done + '"')
  [IO.File]::WriteAllLines((Join-Path $bin "chaos-reg-$n.cmd"), [string[]]$script, $utf8)
  Start-Sleep -Milliseconds 800
  explorer.exe (Join-Path $bin "chaos-reg-$n.cmd")
  for ($i = 0; $i -lt 150 -and -not (Test-Path $done); $i++) { Start-Sleep -Milliseconds 100 }
  if (-not (Test-Path $done)) { throw 'reg.cmd did not run' }
}
function Set-Real($values) {
  Real-Reg ($values.GetEnumerator() | ForEach-Object {
    if ("$($_.Value)" -ne '') { "reg add HKCU\Software\KeyFish /v $($_.Key) /t REG_DWORD /d $($_.Value) /f >nul" }
    else { "reg delete HKCU\Software\KeyFish /v $($_.Key) /f >nul 2>nul" } })
}
function Get-Real($names) {
  $f = Join-Path $bin 'chaos-reg.txt'
  Real-Reg (@('type nul > "' + $f + '"') + ($names | ForEach-Object { '(reg query HKCU\Software\KeyFish /v ' + $_ + ' 2>nul | find "REG_") >> "' + $f + '"' }))
  $found = @{}
  foreach ($l in Get-Content $f) { if ($l -match '^\s*(\S+)\s+REG_DWORD\s+0x([0-9a-fA-F]+)') { $found[$Matches[1]] = [Convert]::ToInt32($Matches[2], 16) } }
  $names | ForEach-Object { "$_=" + $(if ($found.ContainsKey($_)) { $found[$_] } else { '' }) }
}
function Stop-KeyFish { Get-Process KeyFish -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 800 }
function Start-TestKeyFish {
  explorer.exe $app
  for ($i = 0; $i -lt 60 -and -not (Test-Path (Join-Path $bin '~keyfish.running')); $i++) { Start-Sleep -Milliseconds 250 }
  Start-Sleep 4
  if (-not (Test-Path (Join-Path $bin '~keyfish.running'))) { throw 'The test KeyFish did not start in tests\bin' }
}
function Restore-Backup {
  Stop-KeyFish
  foreach ($f in $files) { $p = Join-Path $bin $f; $s = Join-Path $backupDir $f; if (Test-Path $s) { Copy-Item $s $p -Force } elseif (Test-Path $p) { Remove-Item $p } }
  $values = [ordered]@{}
  foreach ($line in Get-Content (Join-Path $backupDir 'registry.txt')) { $n, $v = $line -split '=', 2; $values[$n] = $v }
  Set-Real $values
  $was = Get-Content (Join-Path $backupDir 'running.txt')
  Remove-Item $backupDir -Recurse -Force
  if ($was -eq 'store') { explorer.exe 'shell:AppsFolder\MaximPravko.KeyFish_q2dp1ns3fa4wr!KeyFish' } elseif ($was -and (Test-Path $was)) { explorer.exe $was }
  Remove-Item "$bin\chaos-reg*" -ErrorAction SilentlyContinue
}
function Typing($name, $seed, $mode, $extra) {
  $o = Join-Path $out "$name.txt"
  $a = @("`"$o`"", $seed, $Ops, $mode) + @($extra | Where-Object { $_ })
  Start-Process "$bin\ChaosTyper.exe" -ArgumentList $a -Wait
  Get-Content $o -Raw -Encoding utf8
}
function Usage {
  $p = Get-Process KeyFish -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $app } | Sort-Object StartTime | Select-Object -First 1
  if (-not $p) { return 'not running' }
  $p.Refresh()
  '{0:0.0} MB private, {1} handles, {2} threads, {3:0.0} s processor' -f ($p.PrivateMemorySize64 / 1MB), $p.HandleCount, $p.Threads.Count, $p.TotalProcessorTime.TotalSeconds
}

if (Test-Path (Join-Path $backupDir 'complete')) { Restore-Backup; 'The previous run was cut short: settings restored first' }
elseif (Test-Path $backupDir) { Remove-Item $backupDir -Recurse -Force }
New-Item -ItemType Directory -Force $backupDir | Out-Null
# A copy started a moment ago (by the previous test run's end) may not be up yet
for ($i = 0; $i -lt 20 -and -not (Get-Process KeyFish -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -ne $app }); $i++) { Start-Sleep -Milliseconds 500 }
$running = @(Get-Process KeyFish -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -ne $app } | Select-Object -ExpandProperty Path -Unique)
Set-Content (Join-Path $backupDir 'running.txt') $(if ($running | Where-Object { $_ -like '*\WindowsApps\*' }) { 'store' } elseif ($running) { $running[0] } else { '' }) -Encoding utf8
foreach ($f in $files) { $p = Join-Path $bin $f; if (Test-Path $p) { Copy-Item $p (Join-Path $backupDir $f) } }
Get-Real $regNames | Set-Content (Join-Path $backupDir 'registry.txt') -Encoding ascii
Set-Content (Join-Path $backupDir 'complete') 'ok' -Encoding ascii

$report = New-Object Collections.Generic.List[string]
try {
  foreach ($f in $files) { [IO.File]::WriteAllText((Join-Path $bin $f), '', $utf8) }
  # Double Shift on (the rough mode taps Shift), typos and typography off: they depend on timing by design
  Set-Real ([ordered]@{ AutoCorrect = 1; SwitchWhileTyping = 1; Typos = 0; Typography = 0; QuietFullscreen = 1; CorrectionTips = 2; IntroShown = 1; QuickKey = 1; Hints = 0 })
  $debugLog = Join-Path $bin 'debug-log.txt'

  # 1. The same English typing without and with KeyFish
  foreach ($seed in $seedList) {
    Stop-KeyFish
    $without = Typing "en-$seed-without" $seed 'en' $null
    Start-TestKeyFish
    $with = Typing "en-$seed-with" $seed 'en' $null
    if ($without -like 'NOFOCUS*' -or $with -like 'NOFOCUS*') { $report.Add("en seed ${seed}: the window lost the focus, run not counted") }
    elseif ($without -ceq $with) { $report.Add("en seed ${seed}: same text with and without KeyFish ($($with.Length) characters)") }
    else {
      $a = $without -split '\s+'; $b = $with -split '\s+'
      $diff = @(); for ($i = 0; $i -lt [Math]::Max($a.Count, $b.Count) -and $diff.Count -lt 10; $i++) { if ($a[$i] -cne $b[$i]) { $diff += "#$i '$($a[$i])' -> '$($b[$i])'" } }
      $report.Add("en seed ${seed}: DIFFERENT with KeyFish: " + ($diff -join '; '))
    }
  }

  # 2. Rough typing, then the sentinel; 3. resources before and after
  Stop-KeyFish
  Set-Content $debugLog '' -Encoding utf8
  Start-TestKeyFish
  $before = Usage
  foreach ($seed in $seedList) {
    $t = Typing "mixed-$seed" (100 + $seed) 'mixed' 'sentinel'
    $last = ($t -split "`r?`n")[-1]
    $alive = [bool](Get-Process KeyFish -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $app })
    Start-Sleep 2
    $report.Add("  resources: $(Usage)")
    $report.Add("mixed seed ${seed}: " + $(if ($t -like 'NOFOCUS*') { 'the window lost the focus' } elseif ($last -ceq 'привет ') { 'KeyFish still works after it (sentinel fixed)' } else { "SENTINEL NOT FIXED: last line '$last'" }) + $(if ($alive) { '' } else { '; KEYFISH IS NOT RUNNING' }))
  }
  Start-Sleep 3
  $report.Add("resources before: $before")
  $report.Add("resources after:  $(Usage)")
  Copy-Item $debugLog "$out\debug-log.txt" -ErrorAction SilentlyContinue
  $events = Get-Content "$out\debug-log.txt" -ErrorAction SilentlyContinue | Select-String 'error in|stuck|reinstalled|slow hook|late hook|NOT IN PLACE'
  $report.Add("diagnostics: " + $(if ($events) { ($events | ForEach-Object { $_.Line }) -join ' | ' } else { 'no errors, stuck corrections, hook losses or mangled fixes' }))
}
finally {
  Remove-Item (Join-Path $bin 'debug-log.txt') -ErrorAction SilentlyContinue
  Restore-Backup
}
$report | Set-Content "$out\report.txt" -Encoding utf8
$report
