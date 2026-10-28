param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$OutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$version = '10.0.12'
$manifestUrl = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$manifest = Invoke-RestMethod -Uri $manifestUrl -TimeoutSec 30
$release = $manifest.releases | Where-Object 'release-version' -eq $version | Select-Object -First 1
$file = $release.windowsdesktop.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.exe' } | Select-Object -First 1
if (!$file -or ([Uri]$file.url).Scheme -ne 'https' -or ([Uri]$file.url).Host -ne 'builds.dotnet.microsoft.com') {
    throw 'The specified official desktop runtime installer was not found.'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$path = Join-Path $OutputDirectory "windowsdesktop-runtime-$version-win-x64.exe"

function Get-InstallerVerification([string]$Candidate) {
    $hash = (Get-FileHash -LiteralPath $Candidate -Algorithm SHA512).Hash
    if ($hash -ne $file.hash) { throw [IO.InvalidDataException]::new('Official SHA512 verification failed.') }
    $signature = Get-AuthenticodeSignature -LiteralPath $Candidate
    if ($signature.Status -ne 'Valid' -or !$signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {
        throw [IO.InvalidDataException]::new('A valid Microsoft digital signature could not be verified.')
    }
    return [pscustomobject]@{ SHA512 = $hash; Signature = $signature.Status.ToString(); Signer = $signature.SignerCertificate.Subject }
}

$verification = $null
if (Test-Path -LiteralPath $path) {
    try { $verification = Get-InstallerVerification $path }
    catch [IO.InvalidDataException] {
        # A partial or invalid cache must not permanently prevent a later download from recovering.
        Remove-Item -LiteralPath $path -Force
    }
}
if ($null -eq $verification) {
    $temporary = Join-Path $OutputDirectory ('.runtime-' + [Guid]::NewGuid().ToString('N') + '.exe')
    try {
        Invoke-WebRequest -Uri $file.url -OutFile $temporary -TimeoutSec 300
        $verification = Get-InstallerVerification $temporary
        Move-Item -LiteralPath $temporary -Destination $path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}
[pscustomobject]@{
    Version=$version; Architecture='win-x64'; File=[IO.Path]::GetFileName($path)
    Source=$file.url; Manifest=$manifestUrl; SHA512=$verification.SHA512
    Signature=$verification.Signature; Signer=$verification.Signer
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'verification.json') -Encoding UTF8
Write-Host "Verified optional runtime installer (not installed): $path"
