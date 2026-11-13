# Stress test in other languages: for each extra language, its layout is added to English and Russian for the run (the
# Windows language list is put back as it was at the end, or by the next run if this one is cut short), and sentences in
# all three are typed with the keys of their own layouts while the window starts in English: German typed on the
# English layout comes out "Ich wei- nicht", Greek "Ets; rkei", and KeyFish has to put each right. Words are frequent
# words from data\<lang>.txt; English and Russian sentences come from tests\corpus. The report is stress.ps1's.
# Usage: powershell -ExecutionPolicy Bypass -File tests\stress-langs.ps1 [-Langs de,fr,...] [-Sentences 24] [-Runs normal,fast] [-Seed 3]
# -NoRussian: for the run the list is English and the extra language only (a Greek user in Cyprus has no Russian); the
# sentences are in those two languages
# -Bare: the extra language's words without accents, as Greek is often written in chats and in capitals
param([string[]]$Langs, [int]$Sentences = 24, [string[]]$Runs = @('normal'), [int]$Seed = 3, [switch]$NoRussian, [switch]$Bare)
$ErrorActionPreference = 'Stop'
$bin = Join-Path $PSScriptRoot 'bin'
$out = Join-Path $bin 'stress-langs'
$utf8 = New-Object Text.UTF8Encoding $false
# Language tag, input method (language id : KLID), KLID, the word list
$known = [ordered]@{
  uk = @('uk-UA', '0422:00020422', '00020422', 'uk')
  be = @('be-BY', '0423:00000423', '00000423', 'be')
  de = @('de-DE', '0407:00000407', '00000407', 'de')
  fr = @('fr-FR', '040C:0000040C', '0000040C', 'fr')
  es = @('es-ES', '0C0A:0000040A', '0000040A', 'es')
  pl = @('pl-PL', '0415:00000415', '00000415', 'pl')
  el = @('el-GR', '0408:00000408', '00000408', 'el')
  bg = @('bg-BG', '0402:00030402', '00030402', 'bg')
  cs = @('cs-CZ', '0405:00000405', '00000405', 'cs')
}
if ($Langs) { $Langs = $Langs -split ',' } else { $Langs = @($known.Keys) }
foreach ($l in $Langs) { if (-not $known.Contains($l)) { throw "Unknown language: $l (known: $($known.Keys -join ', '))" } }
if ($Runs.Count -eq 1) { $Runs = $Runs[0] -split ',' }
New-Item -ItemType Directory -Force $out | Out-Null

# The language list is changed by a PowerShell started through Explorer: from inside a sandboxed app the change would
# stay in the sandbox. Each call waits for it to finish
$script:realCalls = 0
function Real-PS([string]$body) {
  Start-Sleep -Milliseconds 1500
  # A file of its own each time: Explorer may pass over the same file started again a moment later
  $n = ++$script:realCalls
  $done = Join-Path $bin "langs-$n.done"; Remove-Item $done -ErrorAction SilentlyContinue
  $ps1 = Join-Path $bin "langs-$n.ps1"
  [IO.File]::WriteAllText($ps1, "`$ErrorActionPreference = 'Stop'`r`ntry {`r`n$body`r`n} catch { `$_ | Out-File '$bin\langs.err' }`r`n'done' | Out-File '$done'", (New-Object Text.UTF8Encoding $true))
  [IO.File]::WriteAllText((Join-Path $bin "langs-$n.cmd"), "@powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$ps1`"", $utf8)
  explorer.exe (Join-Path $bin "langs-$n.cmd")
  # Adding a language can take Windows a minute or more
  for ($i = 0; $i -lt 1800 -and -not (Test-Path $done); $i++) { Start-Sleep -Milliseconds 100 }
  if (-not (Test-Path $done)) { throw 'the language list script did not run' }
  if (Test-Path "$bin\langs.err") { $e = Get-Content "$bin\langs.err" -Raw; Remove-Item "$bin\langs.err"; throw "language list: $e" }
}
$saved = Join-Path $PSScriptRoot 'backup-langs.json'
function Restore-Langs {
  if (-not (Test-Path $saved)) { return }
  Real-PS @"
`$saved = @(Get-Content '$saved' -Raw | ConvertFrom-Json | ForEach-Object { `$_ })
`$list = New-WinUserLanguageList `$saved[0].LanguageTag
`$list[0].InputMethodTips.Clear(); foreach (`$t in `$saved[0].InputMethodTips) { `$list[0].InputMethodTips.Add(`$t) }
for (`$i = 1; `$i -lt `$saved.Count; `$i++) {
  `$list.Add(`$saved[`$i].LanguageTag); `$list[`$i].InputMethodTips.Clear(); foreach (`$t in `$saved[`$i].InputMethodTips) { `$list[`$i].InputMethodTips.Add(`$t) }
}
Set-WinUserLanguageList `$list -Force
"@
  Remove-Item $saved
}
function Set-Langs($extra) {
  Real-PS @"
`$saved = @(Get-Content '$saved' -Raw | ConvertFrom-Json | ForEach-Object { `$_ })
`$list = New-WinUserLanguageList `$saved[0].LanguageTag
`$list[0].InputMethodTips.Clear(); foreach (`$t in `$saved[0].InputMethodTips) { `$list[0].InputMethodTips.Add(`$t) }
for (`$i = 1; `$i -lt `$saved.Count; `$i++) {
  if ('$NoRussian' -eq 'True' -and `$saved[`$i].LanguageTag -like 'ru*') { continue }
  `$list.Add(`$saved[`$i].LanguageTag); `$n = `$list.Count - 1; `$list[`$n].InputMethodTips.Clear(); foreach (`$t in `$saved[`$i].InputMethodTips) { `$list[`$n].InputMethodTips.Add(`$t) }
}
`$list.Add('$($extra[0])'); `$list[`$list.Count - 1].InputMethodTips.Clear(); `$list[`$list.Count - 1].InputMethodTips.Add('$($extra[1])')
Set-WinUserLanguageList `$list -Force
"@
  Start-Sleep 2
  # Only the layouts of this run stay loaded in the session: one left over from the previous language (or Russian with
  # -NoRussian) would take part in KeyFish's choice
  $ids = (Tip-Ids @($orig | Where-Object { -not ($NoRussian -and $_.LanguageTag -like 'ru*') } | ForEach-Object { $_.InputMethodTips })) + (Tip-Ids @($extra[1]))
  Unload-Others $ids
}

Add-Type -Namespace KF -Name Kbd -MemberDefinition '[DllImport("user32.dll")] public static extern int GetKeyboardLayoutList(int n, IntPtr[] l); [DllImport("user32.dll")] public static extern bool UnloadKeyboardLayout(IntPtr h); [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr LoadKeyboardLayout(string id, uint f);'
# Unloads the session's layouts whose language id isn't among these
# The language ids of keyboard layout tips ("0409:00000409"); IME tips ({GUID}) aren't layouts and are skipped
# A layout of the session is kept if its language id (the HKL's low word) is a tip's language ("0809" in
# "0809:00000409") or its KLID's language
function Tip-Ids($tips) { @($tips | Where-Object { $_ -match '^[0-9A-Fa-f]{4}:[0-9A-Fa-f]{8}$' } | ForEach-Object { [Convert]::ToInt32(($_ -split ':')[0], 16); [Convert]::ToInt32(($_ -split ':')[1].Substring(4), 16) }) }
function Unload-Others($ids) {
  $list = New-Object IntPtr[] 64; $n = [KF.Kbd]::GetKeyboardLayoutList(64, $list)
  for ($i = 0; $i -lt $n; $i++) { $h = $list[$i].ToInt64(); if ((($h -shr 28) -band 0xF) -ne 0xE -and $ids -notcontains [int]($h -band 0xFFFF)) { [void][KF.Kbd]::UnloadKeyboardLayout($list[$i]); 'unloaded layout {0:X8}' -f $h } }
}

if (Test-Path $saved) { Restore-Langs; 'The previous run was cut short: the language list was put back first' }
# The list as it is now, read in the real session
Real-PS "`$l = Get-WinUserLanguageList; ConvertTo-Json -Depth 3 -InputObject @(foreach (`$x in `$l) { [pscustomobject]@{ LanguageTag = `$x.LanguageTag; InputMethodTips = @(`$x.InputMethodTips) } }) | Out-File '$saved' -Encoding utf8"
$orig = @(Get-Content $saved -Raw | ConvertFrom-Json | ForEach-Object { $_ })
"languages now: " + (($orig | ForEach-Object { "$($_.LanguageTag) [$($_.InputMethodTips -join ',')]" }) -join '; ')
if (-not ($orig | Where-Object { $_.InputMethodTips -contains '0409:00000409' }) -or -not ($orig | Where-Object { $_.InputMethodTips -contains '0419:00000419' })) {
  Remove-Item $saved; throw 'This test expects the English (US) and Russian layouts to be installed' }

# Sentences: English and Russian from the corpus, the extra language from its 4000 most frequent words
function Corpus($lang) { @(Get-Content "$PSScriptRoot\corpus\$lang.txt" -Encoding utf8 | Where-Object { $_ -and -not $_.StartsWith('#') }) }
function Clean($line) { @($line.ToLowerInvariant() -split '\s+' | ForEach-Object { $_.Trim('.,!?:;"()«»—-') } | Where-Object { $_ -match '^\p{L}+$' }) }
$en = Corpus 'en'; $ru = Corpus 'ru'
$summary = @()
try {
  foreach ($code in $Langs) {
    $k = $known[$code]
    $words = @(Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) "data\$($k[3]).txt") -Encoding utf8 -TotalCount 6000 |
      ForEach-Object { ($_ -split '\s')[0] } | Where-Object { $_ -match '^\p{L}{2,}$' } | Select-Object -First 4000)
    $rnd = New-Object Random ($Seed + $code.GetHashCode() % 1000)
    $ideal = New-Object Collections.Generic.List[string]
    $script = New-Object Text.StringBuilder "#v2`n"
    for ($i = 0; $i -lt $Sentences; $i++) {
      # Half in the extra language, the rest English and Russian in turn
      if ($i % 2 -eq 0) { $ws = @(1..(4 + $rnd.Next(5)) | ForEach-Object { $words[[int][Math]::Floor([Math]::Pow($rnd.NextDouble(), 2) * $words.Count)] }); $klid = $k[2] }
      elseif ($i % 4 -eq 1 -or $NoRussian) { $ws = Clean $en[$rnd.Next($en.Count)]; $klid = '00000409' }
      else { $ws = Clean $ru[$rnd.Next($ru.Count)]; $klid = '00000419' }
      if ($ws.Count -eq 0) { continue }
      if ($Bare -and $klid -eq $k[2]) { $ws = @($ws | ForEach-Object { ($_.Normalize([Text.NormalizationForm]::FormD) -replace '[\u0301\u0308]', '').Normalize([Text.NormalizationForm]::FormC) }) }
      foreach ($w in $ws) { $ideal.Add($w) }
      # A Latin word in a Cyrillic or Greek sentence (jira, github) is typed with the English keys, as people do
      foreach ($w in $ws) { $wk = if ($w -match '^[a-z]+$' -and $klid -in '00000419','00020422','00000423','00030402','00000408') { '00000409' } else { $klid }; [void]$script.Append("$wk`t$w `n") }
    }
    $dir = Join-Path $out $code
    New-Item -ItemType Directory -Force $dir | Out-Null
    [IO.File]::WriteAllText("$dir\ideal.txt", ($ideal -join ' '), $utf8)
    [IO.File]::WriteAllText("$dir\script.txt", $script.ToString(), $utf8)

    Set-Langs $k
    Start-Sleep 2
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\stress.ps1" -Runs ($Runs -join ',') -ScriptDir $dir | Out-File "$dir\run.txt" -Encoding utf8
    Copy-Item "$bin\stress\*" $dir -Force
    $lines = Get-Content "$dir\report.txt" -Encoding utf8
    $det = $lines | Where-Object { $_ -match 'intended words came out differently' }
    $res = $lines | Where-Object { $_ -match '^\w+: \d+/\d+ ' }
    $summary += "$code ($($ideal.Count) words): detector: $det; " + ($res -join '; ')
  }
}
finally {
  try { Restore-Langs }
  catch { Write-Warning ('THE LANGUAGE LIST WAS NOT PUT BACK: ' + $_ + ' — run this script again to restore it (tests\backup-langs.json)') }
  Start-Sleep 3   # Explorer passes over a second script started right after another
  try { Real-PS "foreach (`$x in (Get-WinUserLanguageList)) { `$x.LanguageTag + ' ' + (`$x.InputMethodTips -join ',') } | Out-File '$bin\langs-after.txt'"
        'languages after: ' + ((Get-Content "$bin\langs-after.txt") -join '; ') } catch { 'languages after: could not be read' }
  Remove-Item "$bin\langs-*" -ErrorAction SilentlyContinue
  # The user's layouts loaded again (one taken out for the run, Russian with -NoRussian, comes back), and the ones the
  # test typed with that aren't in the user's list go: they would stay loaded until sign-out, and KeyFish's Pause would
  # cycle through them
  foreach ($tip in @($orig | ForEach-Object { $_.InputMethodTips } | Where-Object { $_ -match ':[0-9A-Fa-f]{8}$' })) { [void][KF.Kbd]::LoadKeyboardLayout(($tip -split ':')[1], 0) }
  Unload-Others (Tip-Ids @($orig | ForEach-Object { $_.InputMethodTips }))
}
$summary
