# Signs a KeyFish release with the maintainer's key, so KeyFish installs only updates the maintainer signed
# (Updater.UpdateKey). The key lives in this Windows account's key storage and can't be exported: it never leaves this
# computer, and with -Protected Windows asks for its password at every signature.
#   -NewKey      creates the key once and prints its public half for Updater.UpdateKey
#   -PublicKey   prints the public half again
#   -Sign <exe>  writes <exe>.sig next to the file (64 bytes), after checking it against <exe>.sha256 if that is there
# A release: download KeyFish.exe and KeyFish.exe.sha256 built by GitHub Actions, check the build attestation
# (gh attestation verify KeyFish.exe --repo pravken/KeyFish), sign, and upload: gh release upload vX.Y.Z KeyFish.exe.sig
param([switch]$NewKey, [switch]$Protected, [switch]$PublicKey, [string]$Sign)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Core
$name = 'KeyFish update signing'

function Public($key) { [Convert]::ToBase64String($key.Export([Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)) }

if ($NewKey) {
    if ([Security.Cryptography.CngKey]::Exists($name)) { throw "The key '$name' already exists: -PublicKey prints it" }
    $p = New-Object Security.Cryptography.CngKeyCreationParameters
    $p.ExportPolicy = [Security.Cryptography.CngExportPolicies]::None
    $p.KeyUsage = [Security.Cryptography.CngKeyUsages]::Signing
    if ($Protected) {
        $p.UIPolicy = New-Object Security.Cryptography.CngUIPolicy([Security.Cryptography.CngUIProtectionLevels]::ForceHighProtection, 'KeyFish', 'Signs KeyFish releases')
    }
    $key = [Security.Cryptography.CngKey]::Create([Security.Cryptography.CngAlgorithm]::ECDsaP256, $name, $p)
    "Public key for Updater.UpdateKey:"
    Public $key
    return
}

if (-not [Security.Cryptography.CngKey]::Exists($name)) { throw "No key '$name' on this computer: create it with -NewKey" }
$key = [Security.Cryptography.CngKey]::Open($name)
if ($PublicKey) { Public $key; return }

if ($Sign) {
    $file = (Resolve-Path $Sign).Path
    $sums = "$file.sha256"
    if (Test-Path $sums) {
        $expected = (Get-Content $sums -Raw).Trim().Split(' ', "`t")[0].ToLowerInvariant()
        $actual = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($expected -ne $actual) { throw "$file doesn't match $sums" }
    }
    $bytes = [IO.File]::ReadAllBytes($file)
    $ecdsa = New-Object Security.Cryptography.ECDsaCng($key)
    $ecdsa.HashAlgorithm = [Security.Cryptography.CngAlgorithm]::Sha256
    $signature = $ecdsa.SignData($bytes)
    # Checked the way KeyFish checks it: with the public half alone
    $check = New-Object Security.Cryptography.ECDsaCng([Security.Cryptography.CngKey]::Import([Convert]::FromBase64String((Public $key)), [Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob))
    $check.HashAlgorithm = [Security.Cryptography.CngAlgorithm]::Sha256
    if (-not $check.VerifyData($bytes, $signature)) { throw 'The signature does not verify' }
    [IO.File]::WriteAllBytes("$file.sig", $signature)
    "Signed: $file.sig"
    return
}
'Usage: sign-release.ps1 -NewKey [-Protected] | -PublicKey | -Sign <KeyFish.exe>'
