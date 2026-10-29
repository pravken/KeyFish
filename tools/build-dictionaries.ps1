# Downloads the open word lists and builds data/<language>.txt ("word count" per line, lowercase, letters only).
# With -Extended it builds the extended dictionaries instead: dist/dictionaries/<language>.txt.gz with the words of the
# full lists that data/ doesn't have and that occur at least -MinCount times, and dictionaries.sha256. They are published
# as a release (tag dictionaries-N, see .github/workflows/dictionaries.yml) that KeyFish downloads when asked to.
# -Languages limits either mode to some languages (for a quick local run).
# Sources (both CC BY-SA 4.0, so data/ is distributed under the same terms):
#   FrequencyWords by Hermit Dave — https://github.com/hermitdave/FrequencyWords (every language except be)
#   Belarusian AOSP dictionary — https://codeberg.org/Anibyl/Belarusian-AOSP-dictionary (be; no frequencies)
param([switch]$Extended, [int]$MinCount = 20, [string[]]$Languages)
$ErrorActionPreference = 'Stop'
# FrequencyWords at a fixed commit, so a rebuild gives the same lists (L11 of the 2026-10 audit)
$frequencyWordsCommit = '525f9b560de45753a5ea01069454e72e9aa541c6'
$root = Split-Path $PSScriptRoot -Parent
$data = Join-Path $root 'data'
$tmp = Join-Path ([IO.Path]::GetTempPath()) 'keyfish-dictionaries'
New-Item -ItemType Directory -Force $data, $tmp | Out-Null
$utf8 = New-Object Text.UTF8Encoding $false

# Letters that make up a word in each language; anything else means the line isn't a plain word
$alphabets = @{
    en = 'a-z'
    ru = 'а-яё'
    uk = 'абвгґдеєжзиіїйклмнопрстуфхцчшщьюя'
    be = 'абвгдеёжзійклмнопрстуўфхцчшыьэюя'
    de = 'a-zäöüß'
    pl = 'a-ząćęłńóśźż'
    'sr-latn' = 'abcdefghijklmnoprstuvzčćđšž'
    bg = 'абвгдежзийклмнопрстуфхцчшщъьюя'
    mk = 'абвгдѓежзѕијклљмнњопрстќуфхцчџш'
    el = 'αβγδεζηθικλμνξοπρστυφχψωάέήίόύώϊϋΐΰς'
    he = 'אבגדהוזחטיכךלמםנןסעפףצץקרשת'
    ka = 'აბგდევზთიკლმნოპჟრსტუფქღყშჩცძწჭხჯჰ'
    fr = 'a-zàâæçéèêëîïôœùûüÿ'
    es = 'a-záéíñóúü'
    it = 'a-zàèéìíîòóùú'
    pt = 'a-záâãàçéêíóôõú'
    cs = 'a-záčďéěíňóřšťúůýž'
    tr = 'abcçdefgğhıijklmnoöprsştuüvyz'
}

function Save-List($name, $pairs) {
    $re = '^[' + $alphabets[$name] + ']+$'
    $out = New-Object Collections.Generic.List[string]
    $seen = New-Object Collections.Generic.HashSet[string]
    foreach ($p in $pairs) {
        $w = $p[0].ToLowerInvariant()
        if ($w -cmatch $re -and $seen.Add($w)) { $out.Add("$w $($p[1])") }
    }
    [IO.File]::WriteAllLines((Join-Path $data "$name.txt"), $out, $utf8)
    "{0,-8} {1} words" -f $name, $out.Count
}

function Read-FrequencyWords($lang) {
    $file = Join-Path $tmp "$lang.txt"
    Invoke-WebRequest "https://raw.githubusercontent.com/hermitdave/FrequencyWords/$frequencyWordsCommit/content/2018/$lang/${lang}_50k.txt" -OutFile $file
    foreach ($line in [IO.File]::ReadLines($file, [Text.Encoding]::UTF8)) {
        $parts = $line.Split(' ')
        if ($parts.Length -eq 2) { , @($parts[0], $parts[1]) }
    }
}

if ($Languages) { $Languages = @($Languages | ForEach-Object { $_ -split ',' }) }   # -File passes "ru,en" as one string
function Wanted($name) { return -not $Languages -or $Languages -contains $name }

# Serbian: the list is almost entirely Latin script; the Cyrillic model is the same list transliterated
$toCyr = [ordered]@{ 'lj' = 'љ'; 'nj' = 'њ'; 'dž' = 'џ'; 'a' = 'а'; 'b' = 'б'; 'c' = 'ц'; 'č' = 'ч'; 'ć' = 'ћ'; 'd' = 'д'; 'đ' = 'ђ'; 'e' = 'е'; 'f' = 'ф';
    'g' = 'г'; 'h' = 'х'; 'i' = 'и'; 'j' = 'ј'; 'k' = 'к'; 'l' = 'л'; 'm' = 'м'; 'n' = 'н'; 'o' = 'о'; 'p' = 'п'; 'r' = 'р'; 's' = 'с'; 'š' = 'ш';
    't' = 'т'; 'u' = 'у'; 'v' = 'в'; 'z' = 'з'; 'ž' = 'ж' }
$alphabets['sr-cyrl'] = 'абвгдђежзијклљмнњопрстћуфхцчџш'
function To-Cyrillic($pairs) {
    foreach ($p in $pairs) {
        $w = $p[0].ToLowerInvariant()
        if ($w -cmatch ('^[' + $alphabets['sr-latn'] + ']+$')) {
            $c = $w
            foreach ($k in $toCyr.Keys) { $c = $c.Replace($k, $toCyr[$k]) }
            , @($c, $p[1])
        }
        elseif ($w -cmatch ('^[' + $alphabets['sr-cyrl'] + ']+$')) { , @($w, $p[1]) }
    }
}

$frequencyLanguages = 'en', 'ru', 'uk', 'de', 'pl', 'bg', 'mk', 'el', 'he', 'ka', 'fr', 'es', 'it', 'pt', 'cs', 'tr'

if ($Extended) {
    $dist = Join-Path $root 'dist\dictionaries'
    New-Item -ItemType Directory -Force $dist | Out-Null
    $sums = New-Object Collections.Generic.List[string]

    # Languages with a small corpus: their built-in list already has every word seen 20 times, so rarer ones are taken
    $smallCorpus = @{ uk = 3; mk = 3; ka = 3 }

    # The full list, only the words seen at least $MinCount times (fewer for a small corpus)
    function Read-Full($lang) {
        $min = if ($smallCorpus.ContainsKey($lang)) { [Math]::Min($MinCount, $smallCorpus[$lang]) } else { $MinCount }
        $file = Join-Path $tmp "${lang}_full.txt"
        if (-not (Test-Path $file)) {
            Invoke-WebRequest "https://raw.githubusercontent.com/hermitdave/FrequencyWords/$frequencyWordsCommit/content/2018/$lang/${lang}_full.txt" -OutFile $file
        }
        foreach ($line in [IO.File]::ReadLines($file, [Text.Encoding]::UTF8)) {
            $sp = $line.IndexOf(' ')
            $count = 0
            if ($sp -gt 0 -and [int]::TryParse($line.Substring($sp + 1), [ref]$count) -and $count -ge $min) { , @($line.Substring(0, $sp), $count) }
        }
    }

    function Save-Extended($name, $pairs) {
        $re = New-Object Text.RegularExpressions.Regex ('^[' + $alphabets[$name] + ']+$')
        $have = New-Object Collections.Generic.HashSet[string]
        foreach ($line in [IO.File]::ReadLines((Join-Path $data "$name.txt"), $utf8)) { [void]$have.Add($line.Substring(0, $line.IndexOf(' '))) }
        $text = New-Object Text.StringBuilder
        $n = 0
        foreach ($p in $pairs) {
            $w = $p[0].ToLowerInvariant()
            if ($re.IsMatch($w) -and $have.Add($w)) { [void]$text.Append($w).Append(' ').Append($p[1]).Append("`n"); $n++ }
        }
        if ($n -eq 0) { "{0,-8} nothing to add" -f $name; return }   # Georgian: the full list has no more words than the built-in one
        $bytes = $utf8.GetBytes($text.ToString())
        $path = Join-Path $dist "$name.txt.gz"
        $file = [IO.File]::Create($path)
        $gz = New-Object IO.Compression.GZipStream($file, [IO.Compression.CompressionLevel]::Optimal)
        $gz.Write($bytes, 0, $bytes.Length)
        $gz.Close()
        $sums.Add((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() + "  $name.txt.gz")
        "{0,-8} +{1} words, {2:N0} KB" -f $name, $n, ((Get-Item $path).Length / 1KB)
    }

    foreach ($lang in $frequencyLanguages) { if (Wanted $lang) { Save-Extended $lang (Read-Full $lang) } }
    if ((Wanted 'sr-latn') -or (Wanted 'sr-cyrl')) {
        $sr = @(Read-Full 'sr')
        if (Wanted 'sr-latn') { Save-Extended 'sr-latn' $sr }
        if (Wanted 'sr-cyrl') { Save-Extended 'sr-cyrl' (To-Cyrillic $sr) }
    }
    [IO.File]::WriteAllLines((Join-Path $dist 'dictionaries.sha256'), $sums, $utf8)
    return
}

foreach ($lang in $frequencyLanguages) { if (Wanted $lang) { Save-List $lang (Read-FrequencyWords $lang) } }
if ((Wanted 'sr-latn') -or (Wanted 'sr-cyrl')) {
    $sr = @(Read-FrequencyWords 'sr')
    if (Wanted 'sr-latn') { Save-List 'sr-latn' $sr }
    if (Wanted 'sr-cyrl') { Save-List 'sr-cyrl' (To-Cyrillic $sr) }
}
if (-not (Wanted 'be')) { return }

# Belarusian: an AOSP keyboard word list; every word has the same weight, so each gets count 1
$gzFile = Join-Path $tmp 'be.gz'
Invoke-WebRequest 'https://codeberg.org/Anibyl/Belarusian-AOSP-dictionary/releases/download/1.0/be_wordlist_short.combined.gz' -OutFile $gzFile
$gz = New-Object IO.Compression.GZipStream([IO.File]::OpenRead($gzFile), [IO.Compression.CompressionMode]::Decompress)
$reader = New-Object IO.StreamReader($gz, [Text.Encoding]::UTF8)
$bePairs = while (($line = $reader.ReadLine()) -ne $null) {
    if ($line -match '^\s*word=([^,]+),') { , @($Matches[1], 1) }
}
$reader.Close()
Save-List 'be' $bePairs
