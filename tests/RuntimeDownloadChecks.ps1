param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$testParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$testRoot = Join-Path $testParent ('ApoSwitch-RuntimeChecks-' + [Guid]::NewGuid().ToString('N'))
$runtimeScript = Join-Path $RepositoryRoot 'tools\Get-Runtime.ps1'
$installerName = 'windowsdesktop-runtime-10.0.12-win-x64.exe'
$runtimeDownloadTestState = [pscustomobject]@{
    passed = 0; fixture = ''; manifest = $null; downloadMode = 'success'
    manifestTimeout = 0; manifestCalls = 0; downloadTimeout = 0; downloadCalls = 0
    downloadTarget = $null; signatureCalls = 0
}
$failures = [Collections.Generic.List[string]]::new()

function Assert-RuntimeCheck([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw "FAIL: $Name" }
    $runtimeDownloadTestState.passed++
    Write-Output "OK: $Name"
}

# These mocks apply only to the child script invocation. No network or signature trust is used in these checks.
function Invoke-RestMethod {
    param([string]$Uri, [int]$TimeoutSec)
    $runtimeDownloadTestState.manifestTimeout = $TimeoutSec
    $runtimeDownloadTestState.manifestCalls++
    if ($runtimeDownloadTestState.downloadMode -eq 'manifest-failure') { throw [Net.WebException]::new('simulated manifest timeout') }
    return $runtimeDownloadTestState.manifest
}

function Invoke-WebRequest {
    param([string]$Uri, [string]$OutFile, [int]$TimeoutSec)
    $runtimeDownloadTestState.downloadTimeout = $TimeoutSec
    $runtimeDownloadTestState.downloadCalls++
    $runtimeDownloadTestState.downloadTarget = $OutFile
    if ($runtimeDownloadTestState.downloadMode -eq 'interrupted') {
        [IO.File]::WriteAllBytes($OutFile, [Text.Encoding]::UTF8.GetBytes('partial download'))
        throw [Net.WebException]::new('simulated interrupted download')
    }
    if ($runtimeDownloadTestState.downloadMode -eq 'wrong-hash') {
        [IO.File]::WriteAllBytes($OutFile, [Text.Encoding]::UTF8.GetBytes('wrong installer'))
    }
    else { Copy-Item -LiteralPath $runtimeDownloadTestState.fixture -Destination $OutFile }
}

function Get-AuthenticodeSignature {
    param([string]$LiteralPath)
    $runtimeDownloadTestState.signatureCalls++
    if ($runtimeDownloadTestState.downloadMode -eq 'bad-signature') {
        return [pscustomobject]@{ Status = 'NotSigned'; SignerCertificate = $null }
    }
    if ($runtimeDownloadTestState.downloadMode -eq 'missing-certificate') {
        return [pscustomobject]@{ Status = 'Valid'; SignerCertificate = $null }
    }
    if ($runtimeDownloadTestState.downloadMode -eq 'bad-publisher') {
        return [pscustomobject]@{ Status = 'Valid'; SignerCertificate = [pscustomobject]@{ Subject = 'CN=Runtime, O=Not Microsoft Corporation' } }
    }
    return [pscustomobject]@{ Status = 'Valid'; SignerCertificate = [pscustomobject]@{ Subject = 'CN=.NET, O=Microsoft Corporation' } }
}

function Reset-RuntimeMocks([string]$Mode = 'success') {
    $runtimeDownloadTestState.downloadMode = $Mode
    $runtimeDownloadTestState.manifestCalls = 0
    $runtimeDownloadTestState.downloadCalls = 0
    $runtimeDownloadTestState.signatureCalls = 0
    $runtimeDownloadTestState.manifestTimeout = 0
    $runtimeDownloadTestState.downloadTimeout = 0
    $runtimeDownloadTestState.downloadTarget = $null
}

function Invoke-ExpectedRuntimeFailure([string]$OutputPath) {
    $failed = $false
    try { & $runtimeScript -OutputDirectory $OutputPath 6>$null | Out-Null }
    catch { $failed = $true }
    Assert-RuntimeCheck $failed '下载或验证失败明确抛出错误'
}

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $runtimeDownloadTestState.fixture = Join-Path $testRoot 'fixture.exe'
    [IO.File]::WriteAllBytes($runtimeDownloadTestState.fixture, [Text.Encoding]::UTF8.GetBytes('runtime installer test fixture'))
    $fixtureHash = (Get-FileHash -LiteralPath $runtimeDownloadTestState.fixture -Algorithm SHA512).Hash
    $runtimeDownloadTestState.manifest = [pscustomobject]@{ releases = @([pscustomobject]@{
        'release-version' = '10.0.12'
        windowsdesktop = [pscustomobject]@{ files = @([pscustomobject]@{
            rid = 'win-x64'; name = 'windowsdesktop-runtime-win-x64.exe'
            url = 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.12/windowsdesktop-runtime-10.0.12-win-x64.exe'
            hash = $fixtureHash
        }) }
    }) }

    foreach ($mode in @('interrupted', 'wrong-hash', 'bad-signature', 'missing-certificate', 'bad-publisher')) {
        try {
            Reset-RuntimeMocks $mode
            $output = Join-Path $testRoot $mode
            New-Item -ItemType Directory -Path $output -Force | Out-Null
            $unrelated = Join-Path $output 'unrelated.tmp'
            [IO.File]::WriteAllText($unrelated, 'keep this file')
            Invoke-ExpectedRuntimeFailure $output
            Assert-RuntimeCheck (!(Test-Path -LiteralPath (Join-Path $output $installerName))) "$mode 不留下损坏的正式 EXE"
            Assert-RuntimeCheck (@(Get-ChildItem -LiteralPath $output -File).Count -eq 1) "$mode 清理自己的下载临时文件"
            Assert-RuntimeCheck ((Get-Content -LiteralPath $unrelated -Raw) -eq 'keep this file') "$mode 保留无关的已有文件"
        }
        catch { $failures.Add($_.Exception.Message) }
    }

    try {
        Reset-RuntimeMocks
        $output = Join-Path $testRoot 'success'
        & $runtimeScript -OutputDirectory $output 6>$null | Out-Null
        $installer = Join-Path $output $installerName
        Assert-RuntimeCheck ((Get-FileHash -LiteralPath $installer -Algorithm SHA512).Hash -eq $fixtureHash) '成功后正式文件内容与官方清单 SHA512 一致'
        Assert-RuntimeCheck ($runtimeDownloadTestState.downloadTarget -ne $installer) '下载先写唯一临时文件，不直接写正式 EXE'
        Assert-RuntimeCheck ($runtimeDownloadTestState.manifestTimeout -gt 0 -and $runtimeDownloadTestState.downloadTimeout -gt 0) '清单和安装器请求均有显式超时'
        Assert-RuntimeCheck ($runtimeDownloadTestState.downloadCalls -eq 1 -and $runtimeDownloadTestState.signatureCalls -ge 1) '成功路径下载一次并验证签名'
        $record = Get-Content -LiteralPath (Join-Path $output 'verification.json') -Raw | ConvertFrom-Json
        Assert-RuntimeCheck ($record.SHA512 -eq $fixtureHash -and $record.Signature -eq 'Valid' -and $record.Version -eq '10.0.12') '记录实际校验值、微软签名和固定运行时版本'
        Assert-RuntimeCheck (@(Get-ChildItem -LiteralPath $output -File).Count -eq 2) '成功输出仅有安装器与验证记录'
        & $runtimeScript -OutputDirectory $output 6>$null | Out-Null
        Assert-RuntimeCheck ($runtimeDownloadTestState.downloadCalls -eq 1) '已有有效缓存不重复下载'
    }
    catch { $failures.Add($_.Exception.Message) }

    try {
        Reset-RuntimeMocks 'interrupted'
        $output = Join-Path $testRoot 'invalid-cache-interrupted'
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $output $installerName), 'old interrupted download')
        Invoke-ExpectedRuntimeFailure $output
        Assert-RuntimeCheck (!(Test-Path -LiteralPath (Join-Path $output $installerName))) '损坏缓存重新下载中断后也不留下正式 EXE'
        Assert-RuntimeCheck (@(Get-ChildItem -LiteralPath $output -File).Count -eq 0) '损坏缓存重新下载中断后清理临时文件'
    }
    catch { $failures.Add($_.Exception.Message) }

    try {
        Reset-RuntimeMocks
        $output = Join-Path $testRoot 'invalid-cache'
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $output $installerName), 'old interrupted download')
        & $runtimeScript -OutputDirectory $output 6>$null | Out-Null
        Assert-RuntimeCheck ($runtimeDownloadTestState.downloadCalls -eq 1 -and (Get-FileHash -LiteralPath (Join-Path $output $installerName) -Algorithm SHA512).Hash -eq $fixtureHash) '已有损坏缓存可重新下载并恢复'
    }
    catch { $failures.Add($_.Exception.Message) }

    try {
        Reset-RuntimeMocks 'manifest-failure'
        $output = Join-Path $testRoot 'manifest-failure'
        Invoke-ExpectedRuntimeFailure $output
        Assert-RuntimeCheck ($runtimeDownloadTestState.downloadCalls -eq 0 -and !(Test-Path -LiteralPath $output)) '清单请求失败不下载或创建安装器输出'
        Assert-RuntimeCheck ($runtimeDownloadTestState.manifestTimeout -gt 0) '清单失败路径也传入显式超时'
    }
    catch { $failures.Add($_.Exception.Message) }

    if ($failures.Count) { throw ('Runtime download regressions: ' + ($failures -join '; ')) }
    Write-Output "PASS: $($runtimeDownloadTestState.passed) runtime download checks (mocked network and signature, real file hashes)"
}
finally {
    $absolute = [IO.Path]::GetFullPath($testRoot)
    $expectedPrefix = $testParent + '\ApoSwitch-RuntimeChecks-'
    if (!$absolute.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe runtime-test cleanup target.' }
    if (Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Recurse -Force }
}
