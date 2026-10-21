# Downloads the open word lists and builds data/<language>.txt ("word count" per line, lowercase, letters only).
# Sources (both CC BY-SA 4.0, so data/ is distributed under the same terms):
#   FrequencyWords by Hermit Dave — https://github.com/hermitdave/FrequencyWords (en, ru, uk, sr, de, pl)
#   Belarusian AOSP dictionary — https://codeberg.org/Anibyl/Belarusian-AOSP-dictionary (be; no frequencies)
$ErrorActionPreference = 'Stop'
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
    Invoke-WebRequest "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/$lang/${lang}_50k.txt" -OutFile $file
    foreach ($line in [IO.File]::ReadLines($file, [Text.Encoding]::UTF8)) {
        $parts = $line.Split(' ')
        if ($parts.Length -eq 2) { , @($parts[0], $parts[1]) }
    }
}

foreach ($lang in 'en', 'ru', 'uk', 'de', 'pl') { Save-List $lang (Read-FrequencyWords $lang) }

# Serbian: the list is almost entirely Latin script; the Cyrillic model is the same list transliterated
$sr = @(Read-FrequencyWords 'sr')
Save-List 'sr-latn' $sr
$toCyr = [ordered]@{ 'lj' = 'љ'; 'nj' = 'њ'; 'dž' = 'џ'; 'a' = 'а'; 'b' = 'б'; 'c' = 'ц'; 'č' = 'ч'; 'ć' = 'ћ'; 'd' = 'д'; 'đ' = 'ђ'; 'e' = 'е'; 'f' = 'ф';
    'g' = 'г'; 'h' = 'х'; 'i' = 'и'; 'j' = 'ј'; 'k' = 'к'; 'l' = 'л'; 'm' = 'м'; 'n' = 'н'; 'o' = 'о'; 'p' = 'п'; 'r' = 'р'; 's' = 'с'; 'š' = 'ш';
    't' = 'т'; 'u' = 'у'; 'v' = 'в'; 'z' = 'з'; 'ž' = 'ж' }
$alphabets['sr-cyrl'] = 'абвгдђежзијклљмнњопрстћуфхцчџш'
$cyrPairs = foreach ($p in $sr) {
    $w = $p[0].ToLowerInvariant()
    if ($w -cmatch ('^[' + $alphabets['sr-latn'] + ']+$')) {
        $c = $w
        foreach ($k in $toCyr.Keys) { $c = $c.Replace($k, $toCyr[$k]) }
        , @($c, $p[1])
    }
    elseif ($w -cmatch ('^[' + $alphabets['sr-cyrl'] + ']+$')) { , @($w, $p[1]) }
}
Save-List 'sr-cyrl' $cyrPairs

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
