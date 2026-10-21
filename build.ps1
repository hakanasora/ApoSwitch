param(
    [string]$DotnetPath = 'dotnet',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'),
    [switch]$IncludeDeviceDebug,
    [switch]$IncludeRuntime,
    [switch]$RunChecks,
    [switch]$LiveConfigChecks
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
$OutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count) {
    throw 'Use a new or empty output directory to avoid mixing older runtimes or debug files.'
}
$appOutput = Join-Path $OutputDirectory 'app'
$debugOutput = Join-Path $OutputDirectory 'device-debug'
$docsOutput = Join-Path $OutputDirectory 'docs'
function Publish-Project([string]$project, [string]$destination) {
    & $DotnetPath publish (Join-Path $PSScriptRoot $project) -c Release -r win-x64 --self-contained false `
        -o $destination -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishReadyToRun=false -p:PublishTrimmed=false -p:DebugType=None `
        "-p:RestoreConfigFile=$(Join-Path $PSScriptRoot 'NuGet.Config')" -p:UseSharedCompilation=false -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
}
Publish-Project 'src\ApoSwitch\ApoSwitch.csproj' $appOutput
if (@(Get-ChildItem -LiteralPath $appOutput -Force).Count -ne 1 -or
    !(Test-Path -LiteralPath (Join-Path $appOutput 'ApoSwitch.exe')) -or
    (Get-Item -LiteralPath (Join-Path $appOutput 'ApoSwitch.exe')).Length -gt 2000000) {
    throw 'Production output must contain only ApoSwitch.exe, at most 2 MB.'
}
if ($IncludeDeviceDebug) {
    New-Item -ItemType Directory -Path $debugOutput -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $appOutput 'ApoSwitch.exe') -Destination $debugOutput
    foreach ($project in @('src\ApoSwitch.Debug\ApoSwitch.Debug.csproj','src\ApoSwitch.DeviceSimulator\ApoSwitch.DeviceSimulator.csproj')) {
        Publish-Project $project $debugOutput
    }
    if (@(Get-ChildItem -LiteralPath $debugOutput -Force).Count -ne 3) { throw 'Debug output must contain three standalone EXEs.' }
}
New-Item -ItemType Directory -Path (Join-Path $docsOutput 'assets') -Force | Out-Null
foreach ($file in @('README.md','README.en.md','DEVELOPMENT.md','DEVELOPMENT.en.md','LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $docsOutput
}
foreach ($file in @('ApoSwitch.svg','ApoSwitch-512.png','ApoSwitch-1024.png')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "assets\$file") -Destination (Join-Path $docsOutput 'assets')
}
if ($IncludeRuntime) {
    & (Join-Path $PSScriptRoot 'tools\Get-Runtime.ps1') -OutputDirectory (Join-Path $OutputDirectory 'runtime')
}
if ($RunChecks -or $LiveConfigChecks) {
    $checkProject = Join-Path $PSScriptRoot 'tests\ApoSwitch.Checks\ApoSwitch.Checks.csproj'
    & $DotnetPath build $checkProject -c Release "-p:RestoreConfigFile=$(Join-Path $PSScriptRoot 'NuGet.Config')" -p:UseSharedCompilation=false -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'Checks build failed.' }
    $checkDll = Join-Path $PSScriptRoot 'tests\ApoSwitch.Checks\bin\Release\net10.0-windows\ApoSwitch.Checks.dll'
    if ($LiveConfigChecks) { & $DotnetPath $checkDll --live-config }
    else { & $DotnetPath $checkDll }
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
    $assemblies = Join-Path $PSScriptRoot 'src\ApoSwitch\bin\Release\net10.0-windows\win-x64'
    & $DotnetPath $checkDll --verify-release $appOutput $assemblies
    if ($LASTEXITCODE -ne 0) { throw 'Production isolation checks failed.' }
}
Write-Host "Output: $OutputDirectory"
