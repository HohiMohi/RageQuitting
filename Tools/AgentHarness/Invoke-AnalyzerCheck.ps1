[CmdletBinding()]
param(
    [string]$ArtifactDirectory,
    [switch]$Json
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$modulePath = Join-Path $PSScriptRoot 'AnalyzerHarness.psm1'
Import-Module -Name $modulePath -Force -ErrorAction Stop
$projectRoot = Get-AHProjectRoot

if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $runId = '{0}-{1}' -f [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
    $artifactPath = Join-Path $projectRoot (Join-Path 'Artifacts\Validation\Analyzer' $runId)
}
else {
    $artifactPath = Resolve-AHPath -ProjectRoot $projectRoot -Path $ArtifactDirectory
}

$reportPath = Join-Path $artifactPath 'analyzer-report.json'
$exitCode = 1
$report = $null

try {
    $report = Invoke-AHAnalysis -ProjectRoot $projectRoot -ArtifactDirectory $artifactPath
    $report.artifacts = @($report.artifacts) + @($reportPath)
    Write-AHJsonFile -Path $reportPath -Value $report
    $exitCode = [int]$report.exitCode
}
catch {
    New-Item -ItemType Directory -Path $artifactPath -Force | Out-Null
    $report = [ordered]@{
        schemaVersion = 1
        status = 'failed'
        exitCode = 1
        startedAtUtc = [DateTime]::UtcNow.ToString('o')
        completedAtUtc = [DateTime]::UtcNow.ToString('o')
        tool = [ordered]@{ name = 'RageQuitting Analyzer Harness'; version = '1.0.0' }
        analyzer = [ordered]@{ packageId = 'Microsoft.Unity.Analyzers'; version = '1.27.0'; assemblySha256 = $null }
        counts = [ordered]@{ total = 0; blocking = 0; informational = 0; perCategory = [ordered]@{}; perId = [ordered]@{} }
        baseline = [ordered]@{ path = Join-Path $projectRoot 'Tools\AgentHarness\analyzer-baseline.json'; entries = 0; occurrences = 0 }
        diagnostics = [ordered]@{ currentBlocking = @(); currentInformational = @(); new = @(); resolved = @(); compiler = @(); loadFailures = @() }
        integrity = [ordered]@{ status = 'failed'; checks = @(); issues = @($_.Exception.Message) }
        build = [ordered]@{
            status = 'not_run'
            command = 'dotnet build "Assembly-CSharp-Editor.csproj" --no-restore --no-incremental --nologo --verbosity:minimal'
            exitCode = $null
            project = 'Assembly-CSharp-Editor.csproj'
            topLevelProject = 'Assembly-CSharp-Editor.csproj'
            scope = 'runtime+editor'
            stdout = $null
            stderr = $null
        }
        artifacts = @($reportPath)
    }
    Write-AHJsonFile -Path $reportPath -Value $report
}

if ($Json) {
    $result = [ordered]@{ reportPath = $reportPath; status = [string]$report.status; exitCode = [int]$exitCode }
    [Console]::Out.WriteLine((ConvertTo-Json -InputObject $result -Compress))
}
else {
    Write-Host ('Analyzer check: {0} (exit {1})' -f $report.status, $exitCode)
    Write-Host ('Report: {0}' -f $reportPath)
    if (@($report.diagnostics.new).Count -gt 0) {
        Write-Host ('New blocking diagnostics: {0}' -f @($report.diagnostics.new).Count)
    }
}

exit $exitCode
