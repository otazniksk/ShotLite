# Signs the installer for the in-app update; UpdateInstaller.cs verifies the result with the public key.
#   $env:UPDATE_SIGNING_KEY = PEM private key (GitHub secret of the "release" environment)
#   pwsh installer/sign-update.ps1 out/ShotLite-install.exe 1.2.0
# Output: <file>.sig, a base64 ECDSA P-256 signature of "ShotLite update <version>\n" + SHA-256 of the file.
param(
    [Parameter(Mandatory)][string]$File,
    [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference = 'Stop'

if (-not $env:UPDATE_SIGNING_KEY) { throw 'UPDATE_SIGNING_KEY is not set' }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' is not X.Y.Z" }

$path = (Resolve-Path $File).ProviderPath
$key = [System.Security.Cryptography.ECDsa]::Create()
try {
    $key.ImportFromPem($env:UPDATE_SIGNING_KEY)
    $hash = [System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($path))
    $prefix = [System.Text.Encoding]::UTF8.GetBytes("ShotLite update $Version`n")
    [byte[]]$message = $prefix + $hash
    $signature = $key.SignData($message, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    [System.IO.File]::WriteAllText("$path.sig", [Convert]::ToBase64String($signature))
}
finally {
    $key.Dispose()
}
Write-Host "Signed $File for version $Version"
