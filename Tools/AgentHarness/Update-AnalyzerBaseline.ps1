[CmdletBinding()]
param(
    [switch]$PruneResolved,
    [switch]$AcceptNew,
    [string]$ArtifactDirectory,
    [switch]$Json
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if ([bool]$PruneResolved -eq [bool]$AcceptNew) {
    [Console]::Error.WriteLine('Specify exactly one of -PruneResolved or -AcceptNew.')
    exit 2
}

$modulePath = Join-Path $PSScriptRoot 'AnalyzerHarness.psm1'
Import-Module -Name $modulePath -Force -ErrorAction Stop
$projectRoot = Get-AHProjectRoot
$mode = if ($PruneResolved) { 'PruneResolved' } else { 'AcceptNew' }

if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $runId = '{0}-{1}' -f [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
    $artifactPath = Join-Path $projectRoot (Join-Path 'Artifacts\Validation\AnalyzerBaseline' $runId)
}
else {
    $artifactPath = Resolve-AHPath -ProjectRoot $projectRoot -Path $ArtifactDirectory
}

$analysisReportPath = Join-Path $artifactPath 'analyzer-report.json'
$updateReportPath = Join-Path $artifactPath 'baseline-update.json'
$baselinePath = Join-Path $projectRoot 'Tools\AgentHarness\analyzer-baseline.json'

try {
    $analysis = Invoke-AHAnalysis -ProjectRoot $projectRoot -ArtifactDirectory $artifactPath -IgnoreBaselineRegression
    $analysis.artifacts = @($analysis.artifacts) + @($analysisReportPath)
    Write-AHJsonFile -Path $analysisReportPath -Value $analysis

    if ([int]$analysis.exitCode -ne 0) {
        throw "Baseline was not updated because analyzer health checks failed with status '$($analysis.status)'."
    }

    $existingBaseline = Read-AHJsonFile -Path $baselinePath
    $oldEntries = @($existingBaseline.diagnostics)
    $newEntries = Update-AHBaselineEntries -Existing $oldEntries -CurrentBlocking @($analysis.diagnostics.currentBlocking) -Mode $mode
    $updatedBaseline = [ordered]@{
        schemaVersion = 1
        packageId = 'Microsoft.Unity.Analyzers'
        version = '1.27.0'
        generatedAtUtc = [DateTime]::UtcNow.ToString('o')
        diagnostics = @($newEntries)
    }
    Write-AHJsonFile -Path $baselinePath -Value $updatedBaseline

    $oldOccurrences = 0
    foreach ($entry in $oldEntries) { $oldOccurrences += [int]$entry.count }
    $newOccurrences = 0
    foreach ($entry in $newEntries) { $newOccurrences += [int]$entry.count }
    $message = if ($mode -eq 'PruneResolved') {
        'Pruned resolved or reduced blocking diagnostics; no new diagnostics were accepted.'
    }
    else {
        'Accepted current new or increased blocking diagnostics; resolved baseline entries were retained.'
    }
    $update = [ordered]@{
        schemaVersion = 1
        status = 'passed'
        exitCode = 0
        mode = $mode
        baselinePath = $baselinePath
        analysisReportPath = $analysisReportPath
        previous = [ordered]@{ entries = $oldEntries.Count; occurrences = $oldOccurrences }
        current = [ordered]@{ entries = @($newEntries).Count; occurrences = $newOccurrences }
        message = $message
    }
    Write-AHJsonFile -Path $updateReportPath -Value $update

    if ($Json) {
        [Console]::Out.WriteLine((ConvertTo-Json -InputObject ([ordered]@{ reportPath = $updateReportPath; status = 'passed'; exitCode = 0 }) -Compress))
    }
    else {
        Write-Host $message
        Write-Host ('Baseline: {0} entries / {1} occurrences' -f @($newEntries).Count, $newOccurrences)
        Write-Host ('Report: {0}' -f $updateReportPath)
    }
    exit 0
}
catch {
    New-Item -ItemType Directory -Path $artifactPath -Force | Out-Null
    $update = [ordered]@{
        schemaVersion = 1
        status = 'failed'
        exitCode = 1
        mode = $mode
        baselinePath = $baselinePath
        analysisReportPath = $(if (Test-Path -LiteralPath $analysisReportPath) { $analysisReportPath } else { $null })
        message = $_.Exception.Message
    }
    Write-AHJsonFile -Path $updateReportPath -Value $update
    if ($Json) {
        [Console]::Out.WriteLine((ConvertTo-Json -InputObject ([ordered]@{ reportPath = $updateReportPath; status = 'failed'; exitCode = 1 }) -Compress))
    }
    else {
        Write-Host ('Baseline update failed: {0}' -f $_.Exception.Message)
        Write-Host ('Report: {0}' -f $updateReportPath)
    }
    exit 1
}
