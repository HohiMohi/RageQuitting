[CmdletBinding()]
param(
    [string]$ArtifactDirectory,
    [ValidateRange(10, 1800)][int]$TimeoutSeconds = 240,
    [switch]$Json
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
Import-Module (Join-Path $scriptDirectory 'AgentHarness.psm1') -Force
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDirectory '..\..'))
$started = [DateTime]::UtcNow

if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $id = '{0}-{1}' -f $started.ToString('yyyyMMddTHHmmssZ'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
    $ArtifactDirectory = Join-Path $projectRoot (Join-Path 'Artifacts\Validation\UnityNetworkSmoke' $id)
}
elseif (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $projectRoot $ArtifactDirectory
}

$ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
$reportPath = Join-Path $ArtifactDirectory 'unity-network-smoke-report.json'
$testFilter = 'RageQuitting.Tests.PlayMode.NetworkSmokePlayModeTests.HostLifecycle_StartsLoadsTutorialSpawnsAndRestartsCleanly'
$report = [ordered]@{
    schemaVersion = 1
    topology = 'host-only'
    peerCount = 1
    remoteClientValidated = $false
    transport = 'UnityTransport'
    relayFlowInvoked = $false
    unityServicesInitializationIsolation = 'not_guaranteed'
    knownLimitations = @(
        'MultiplayerStartScene may have started Unity Services initialization before the test body ran.'
    )
    testFilter = $testFilter
    status = 'failed'
    exitCode = 1
    startedAtUtc = $started.ToString('o')
    completedAtUtc = $null
    testReportPath = $null
    testReport = $null
    errors = @()
    artifacts = @()
}

function Complete-NetworkSmoke([string]$Status, [int]$Code, [string[]]$Errors = @()) {
    $report.status = $Status
    $report.exitCode = $Code
    $report.completedAtUtc = [DateTime]::UtcNow.ToString('o')
    $report.errors = @($Errors | ForEach-Object { [string]$_ })
    $report.artifacts = @($report.artifacts | Where-Object { $_ -and (Test-Path -LiteralPath $_) } |
        ForEach-Object { [System.IO.Path]::GetFullPath([string]$_) } | Sort-Object -Unique) + @($reportPath)
    $report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $reportPath -Encoding UTF8

    if ($Json) {
        [Console]::Out.WriteLine((ConvertTo-Json -Compress -InputObject ([ordered]@{
            reportPath = $reportPath
            status = $Status
            exitCode = $Code
            topology = 'host-only'
        })))
    }
    else {
        Write-Host "Unity network smoke: $Status (exit $Code)"
        Write-Host "Report: $reportPath"
    }
    exit $Code
}

$playModeScript = Join-Path $scriptDirectory 'Invoke-UnityPlayModeTests.ps1'
$playModeArtifactDirectory = Join-Path $ArtifactDirectory 'playmode'
$stdout = Join-Path $ArtifactDirectory 'playmode.stdout.txt'
$stderr = Join-Path $ArtifactDirectory 'playmode.stderr.txt'
$report.artifacts = @($stdout, $stderr)

$systemRoot = if ([string]::IsNullOrWhiteSpace($env:SystemRoot)) { 'C:\Windows' } else { $env:SystemRoot }
$powerShellPath = Join-Path $systemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$arguments = @(
    '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $playModeScript,
    '-Filter', $testFilter, '-IncludeExplicit', '-ArtifactDirectory', $playModeArtifactDirectory,
    '-TimeoutSeconds', [string]$TimeoutSeconds, '-Json')
$execution = Invoke-HarnessCommand -FilePath $powerShellPath -Arguments $arguments -StdOutPath $stdout -StdErrPath $stderr -WorkingDirectory $projectRoot

if (-not $execution.available) {
    Complete-NetworkSmoke 'not_available' 3 @("PlayMode runner could not be started: $($execution.error)")
}

$envelope = $null
try {
    $text = Get-Content -LiteralPath $stdout -Raw -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace($text)) { throw 'PlayMode runner returned empty stdout.' }
    $envelope = $text | ConvertFrom-Json -ErrorAction Stop
}
catch {
    Complete-NetworkSmoke 'failed' 1 @("PlayMode runner output could not be parsed: $($_.Exception.Message)")
}

if (-not $envelope.PSObject.Properties['reportPath'] -or
    [string]::IsNullOrWhiteSpace([string]$envelope.reportPath) -or
    -not (Test-Path -LiteralPath ([string]$envelope.reportPath) -PathType Leaf)) {
    Complete-NetworkSmoke 'failed' 1 @('PlayMode runner did not return an existing test report.')
}

$report.testReportPath = [System.IO.Path]::GetFullPath([string]$envelope.reportPath)
$report.artifacts += $report.testReportPath
try {
    $report.testReport = Get-Content -LiteralPath $report.testReportPath -Raw | ConvertFrom-Json -ErrorAction Stop
}
catch {
    Complete-NetworkSmoke 'failed' 1 @("PlayMode test report could not be parsed: $($_.Exception.Message)")
}

$testReport = $report.testReport
$requiredReportProperties = @('filter', 'includeExplicit', 'status', 'exitCode', 'summary', 'errors')
$missingReportProperties = @($requiredReportProperties | Where-Object {
    -not $testReport.PSObject.Properties[$_]
})
if ($missingReportProperties.Count -gt 0) {
    Complete-NetworkSmoke 'failed' 1 @(
        "Underlying PlayMode report is missing required fields: $($missingReportProperties -join ', ').")
}
if ($null -eq $testReport.summary) {
    Complete-NetworkSmoke 'failed' 1 @('Underlying PlayMode report does not contain a test summary.')
}

$requiredSummaryProperties = @('total', 'passed', 'failed', 'skipped', 'inconclusive')
$missingSummaryProperties = @($requiredSummaryProperties | Where-Object {
    -not $testReport.summary.PSObject.Properties[$_]
})
if ($missingSummaryProperties.Count -gt 0) {
    Complete-NetworkSmoke 'failed' 1 @(
        "Underlying PlayMode report summary is missing fields: $($missingSummaryProperties -join ', ').")
}

$underlyingStatus = [string]$testReport.status
$expectedExitCode = switch ($underlyingStatus) {
    'passed' { 0 }
    'failed' { 1 }
    'blocked' { 3 }
    'not_available' { 3 }
    default { $null }
}
if ($null -eq $expectedExitCode) {
    Complete-NetworkSmoke 'failed' 1 @("Underlying PlayMode report has unknown status '$underlyingStatus'.")
}

$consistencyErrors = @()
if ([string]$testReport.filter -cne $testFilter) {
    $consistencyErrors += 'Underlying PlayMode report does not contain the exact network smoke test filter.'
}
if (-not [bool]$testReport.includeExplicit) {
    $consistencyErrors += 'Underlying PlayMode report does not confirm includeExplicit=true.'
}
if ($underlyingStatus -ne [string]$envelope.status) {
    $consistencyErrors += 'PlayMode report status does not match the runner envelope status.'
}
if ([int]$testReport.exitCode -ne [int]$expectedExitCode -or
    [int]$envelope.exitCode -ne [int]$expectedExitCode -or
    [int]$execution.exitCode -ne [int]$expectedExitCode) {
    $consistencyErrors += "PlayMode process, envelope or report exitCode does not match status '$underlyingStatus'."
}
if ($consistencyErrors.Count -gt 0) {
    Complete-NetworkSmoke 'failed' 1 $consistencyErrors
}

if ($underlyingStatus -eq 'passed') {
    $summary = $testReport.summary
    $hasExactPassingSummary =
        [int]$summary.total -eq 1 -and
        [int]$summary.passed -eq 1 -and
        [int]$summary.failed -eq 0 -and
        [int]$summary.skipped -eq 0 -and
        [int]$summary.inconclusive -eq 0
    if (-not $hasExactPassingSummary) {
        Complete-NetworkSmoke 'failed' 1 @(
            'Network smoke requires exactly total=1, passed=1, failed=0, skipped=0 and inconclusive=0.')
    }
}

Complete-NetworkSmoke $underlyingStatus ([int]$testReport.exitCode) @($testReport.errors)
