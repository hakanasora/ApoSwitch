param(
    [string]$DotnetPath = 'dotnet',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\app'),
    [switch]$IncludeDeviceDebug,
    [switch]$RunChecks,
    [switch]$LiveConfigChecks
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count) {
    throw 'Use a new or empty output directory to avoid stale debug files in production.'
}
$debugOutput = $OutputDirectory + '-device-debug'
if ($IncludeDeviceDebug -and (Test-Path -LiteralPath $debugOutput) -and @(Get-ChildItem -LiteralPath $debugOutput -Force).Count) {
    throw 'Use a new or empty debug output directory.'
}
function Publish-Project([string]$project, [string]$destination) {
    & $DotnetPath publish (Join-Path $PSScriptRoot $project) -c Release -r win-x64 --self-contained true `
        -o $destination -p:PublishSingleFile=false -p:DebugType=None `
        "-p:RestoreConfigFile=$(Join-Path $PSScriptRoot 'NuGet.Config')" -p:UseSharedCompilation=false -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
}
Publish-Project 'src\ApoSwitch\ApoSwitch.csproj' $OutputDirectory
foreach ($file in @('README.md','README.en.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $OutputDirectory -Force }
New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'assets') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\ApoSwitch-512.png') -Destination (Join-Path $OutputDirectory 'assets') -Force
if (Test-Path -LiteralPath (Join-Path $OutputDirectory 'ApoSwitch.DeviceDebug.dll')) {
    throw 'Production output contains a debug module.'
}
if ($IncludeDeviceDebug) {
    foreach ($project in @('src\ApoSwitch\ApoSwitch.csproj','src\ApoSwitch.Debug\ApoSwitch.Debug.csproj',
            'src\ApoSwitch.DeviceSimulator\ApoSwitch.DeviceSimulator.csproj')) {
        Publish-Project $project $debugOutput
    }
    foreach ($file in @('README.md','README.en.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $debugOutput -Force }
    New-Item -ItemType Directory -Path (Join-Path $debugOutput 'assets') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\ApoSwitch-512.png') -Destination (Join-Path $debugOutput 'assets') -Force
    Write-Host "Debug output: $debugOutput"
}
if ($RunChecks -or $LiveConfigChecks) {
    $checkProject = Join-Path $PSScriptRoot 'tests\ApoSwitch.Checks\ApoSwitch.Checks.csproj'
    & $DotnetPath build $checkProject -c Release "-p:RestoreConfigFile=$(Join-Path $PSScriptRoot 'NuGet.Config')" -p:UseSharedCompilation=false -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'Checks build failed.' }
    $checkDll = Join-Path $PSScriptRoot 'tests\ApoSwitch.Checks\bin\Release\net10.0-windows\ApoSwitch.Checks.dll'
    if ($LiveConfigChecks) { & $DotnetPath $checkDll --live-config }
    else { & $DotnetPath $checkDll }
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
    & $DotnetPath $checkDll --verify-release $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Production isolation checks failed.' }
}
Write-Host "Production output: $OutputDirectory"
