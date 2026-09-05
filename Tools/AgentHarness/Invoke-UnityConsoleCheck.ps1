[CmdletBinding()]
param(
    [string]$ArtifactDirectory,
    [switch]$Json,
    [ValidateRange(1, 1000)][int]$Limit = 1000,
    [switch]$IgnoreBaselineRegression
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
Import-Module (Join-Path $scriptDirectory 'AgentHarness.psm1') -Force
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDirectory '..\..'))
$started = [DateTime]::UtcNow
if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $id = '{0}-{1}' -f $started.ToString('yyyyMMddTHHmmssZ'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
    $ArtifactDirectory = Join-Path $projectRoot (Join-Path 'Artifacts\Validation\UnityConsole' $id)
}
elseif (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) { $ArtifactDirectory = Join-Path $projectRoot $ArtifactDirectory }
$ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
$reportPath = Join-Path $ArtifactDirectory 'unity-console-report.json'
$stdout = Join-Path $ArtifactDirectory 'get-console-logs.stdout.txt'
$stderr = Join-Path $ArtifactDirectory 'get-console-logs.stderr.txt'
$baselinePath = Join-Path $scriptDirectory 'unity-console-baseline.json'
$unityCli = Join-Path $env:LOCALAPPDATA 'Unity\bin\unity.exe'
$report = [ordered]@{
    schemaVersion = 1; status = 'failed'; exitCode = 2; startedAtUtc = $started.ToString('o'); completedAtUtc = $null
    projectRoot = $projectRoot; limit = $Limit; total = 0; returned = 0; truncated = $false
    baseline = [ordered]@{ path = $baselinePath; entries = 0; occurrences = 0 }
    diagnostics = [ordered]@{ current = @(); new = @(); resolved = @() }
    invocation = [ordered]@{ exitCode = $null; stdout = [System.IO.Path]::GetFullPath($stdout); stderr = [System.IO.Path]::GetFullPath($stderr) }
    errors = @(); artifacts = @()
}

function Complete-Console([string]$Status, [int]$Code, [string[]]$Errors = @()) {
    $report.status = $Status; $report.exitCode = $Code; $report.completedAtUtc = [DateTime]::UtcNow.ToString('o')
    $report.errors = @($Errors | ForEach-Object { [string]$_ }); $report.artifacts = @($stdout, $stderr, $reportPath | ForEach-Object { [System.IO.Path]::GetFullPath($_) })
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    if ($Json) { [Console]::Out.WriteLine((ConvertTo-Json -Compress -InputObject ([ordered]@{ reportPath = $reportPath; status = $Status; exitCode = $Code }))) }
    else { Write-Host "Unity Console check: $Status (exit $Code)"; Write-Host "Report: $reportPath" }
    exit $Code
}

try {
    if (-not (Test-Path (Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt')) -or -not (Test-Path (Join-Path $projectRoot 'Packages\manifest.json'))) { Complete-Console 'failed' 2 @('Invalid Unity project root.') }
    if (-not (Test-Path -LiteralPath $unityCli -PathType Leaf)) { Complete-Console 'not_available' 3 @("Unity CLI was not found at '$unityCli'.") }
    if (-not (Test-Path -LiteralPath $baselinePath -PathType Leaf)) { Complete-Console 'failed' 1 @('unity-console-baseline.json is missing.') }
    $baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json -ErrorAction Stop
    if ([int]$baseline.schemaVersion -ne 1) { Complete-Console 'failed' 1 @('Unsupported Unity Console baseline schema.') }
    $report.baseline.entries = @($baseline.diagnostics).Count
    $baselineOccurrences = 0; foreach ($entry in @($baseline.diagnostics)) { $baselineOccurrences += [int]$entry.count }; $report.baseline.occurrences = $baselineOccurrences

    $execution = Invoke-HarnessCommand -FilePath $unityCli -Arguments @('command', 'get_console_logs', '--severity', 'error', '--limit', [string]$Limit, '--project-path', $projectRoot, '--format', 'json', '--no-banner') -StdOutPath $stdout -StdErrPath $stderr -WorkingDirectory $projectRoot
    $report.invocation.exitCode = $execution.exitCode
    if (-not $execution.available) { Complete-Console 'not_available' 3 @([string]$execution.error) }
    $text = Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrWhiteSpace($text)) { Complete-Console 'not_available' 3 @('Unity CLI returned empty stdout.') }
    try { $envelope = $text | ConvertFrom-Json -ErrorAction Stop } catch { Complete-Console 'not_available' 3 @("Unity Console output could not be parsed: $($_.Exception.Message)") }
    if ($execution.exitCode -ne 0 -or -not $envelope.success -or -not $envelope.data.result) { Complete-Console 'not_available' 3 @('Unity Console command was unsuccessful or returned no result.') }
    $result = $envelope.data.result
    foreach ($field in @('total', 'returned', 'logs')) { if (-not $result.PSObject.Properties[$field]) { Complete-Console 'failed' 1 @("Unity Console result is missing '$field'.") } }
    $report.total = [int]$result.total; $report.returned = [int]$result.returned
    $logCount = @($result.logs).Count
    if ($report.returned -ne $logCount -or $report.total -lt $report.returned) { Complete-Console 'failed' 1 @("Unity Console result is incomplete or inconsistent (total=$($report.total), returned=$($report.returned), logs=$logCount).") }
    $report.truncated = $report.total -ge $Limit -or $report.returned -ge $Limit
    if ($report.truncated) { Complete-Console 'failed' 1 @("Unity Console sample may be truncated (total=$($report.total), returned=$($report.returned), limit=$Limit).") }
    $current = @(Get-HarnessConsoleDiagnostics -ProjectRoot $projectRoot -Logs @($result.logs))
    $comparison = Compare-HarnessCountBaseline -Current $current -Baseline @($baseline.diagnostics)
    $report.diagnostics.current = $current; $report.diagnostics.new = @($comparison.new); $report.diagnostics.resolved = @($comparison.resolved)
    if (-not $IgnoreBaselineRegression -and @($comparison.new).Count -gt 0) { Complete-Console 'failed' 1 @('Unity Console contains new or increased error diagnostics.') }
    Complete-Console 'passed' 0 @()
}
catch { Complete-Console 'failed' 2 @($_.Exception.Message) }
