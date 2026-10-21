param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$version = '10.0.12'
$manifestUrl = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$manifest = Invoke-RestMethod -Uri $manifestUrl
$release = $manifest.releases | Where-Object 'release-version' -eq $version | Select-Object -First 1
$file = $release.windowsdesktop.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.exe' } | Select-Object -First 1
if (!$file -or ([Uri]$file.url).Scheme -ne 'https' -or ([Uri]$file.url).Host -ne 'builds.dotnet.microsoft.com') {
    throw 'The specified official desktop runtime installer was not found.'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$path = Join-Path $OutputDirectory ([IO.Path]::GetFileName(([Uri]$file.url).AbsolutePath))
if (!(Test-Path -LiteralPath $path)) { Invoke-WebRequest -Uri $file.url -OutFile $path }
$hash = (Get-FileHash -LiteralPath $path -Algorithm SHA512).Hash
if ($hash -ne $file.hash) { throw 'Official SHA512 verification failed.' }
$signature = Get-AuthenticodeSignature -LiteralPath $path
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
    throw 'A valid Microsoft digital signature could not be verified.'
}
[pscustomobject]@{
    Version=$version; Architecture='win-x64'; File=[IO.Path]::GetFileName($path)
    Source=$file.url; Manifest=$manifestUrl; SHA512=$hash
    Signature=$signature.Status.ToString(); Signer=$signature.SignerCertificate.Subject
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'verification.json') -Encoding UTF8
Write-Host "Verified optional runtime installer (not installed): $path"
