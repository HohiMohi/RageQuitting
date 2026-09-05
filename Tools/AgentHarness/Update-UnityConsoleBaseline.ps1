[CmdletBinding()]
param(
    [switch]$PruneResolved,
    [switch]$AcceptCurrent,
    [string]$ArtifactDirectory,
    [switch]$Json
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
if ([bool]$PruneResolved -eq [bool]$AcceptCurrent) { [Console]::Error.WriteLine('Specify exactly one of -PruneResolved or -AcceptCurrent.'); exit 2 }
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
Import-Module (Join-Path $scriptDirectory 'AgentHarness.psm1') -Force
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDirectory '..\..'))
$started = [DateTime]::UtcNow
if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $id = '{0}-{1}' -f $started.ToString('yyyyMMddTHHmmssZ'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
    $ArtifactDirectory = Join-Path $projectRoot (Join-Path 'Artifacts\Validation\UnityConsoleBaseline' $id)
}
elseif (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) { $ArtifactDirectory = Join-Path $projectRoot $ArtifactDirectory }
$ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory); New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
$checkDirectory = Join-Path $ArtifactDirectory 'console-check'
$checkStdOut = Join-Path $ArtifactDirectory 'console-check.stdout.txt'; $checkStdErr = Join-Path $ArtifactDirectory 'console-check.stderr.txt'
$reportPath = Join-Path $ArtifactDirectory 'console-baseline-update.json'; $baselinePath = Join-Path $scriptDirectory 'unity-console-baseline.json'
$mode = if ($PruneResolved) { 'PruneResolved' } else { 'AcceptCurrent' }

try {
    $powerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $execution = Invoke-HarnessCommand -FilePath $powerShell -Arguments @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $scriptDirectory 'Invoke-UnityConsoleCheck.ps1'), '-ArtifactDirectory', $checkDirectory, '-IgnoreBaselineRegression', '-Json') -StdOutPath $checkStdOut -StdErrPath $checkStdErr -WorkingDirectory $projectRoot
    if (-not $execution.available -or $execution.exitCode -ne 0) { throw "Console health check failed before baseline update (exit $($execution.exitCode))." }
    $checkEnvelope = Get-Content -LiteralPath $checkStdOut -Raw | ConvertFrom-Json -ErrorAction Stop
    if ([string]$checkEnvelope.status -ne 'passed' -or -not (Test-Path -LiteralPath ([string]$checkEnvelope.reportPath))) { throw 'Console health check did not produce a healthy report.' }
    $checkReport = Get-Content -LiteralPath ([string]$checkEnvelope.reportPath) -Raw | ConvertFrom-Json -ErrorAction Stop
    $baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json -ErrorAction Stop
    $updated = @(Update-HarnessCountBaselineEntries -Existing @($baseline.diagnostics) -Current @($checkReport.diagnostics.current) -Mode $mode)
    $nextBaseline = [ordered]@{ schemaVersion = 1; generatedAtUtc = [DateTime]::UtcNow.ToString('o'); diagnostics = $updated }
    $nextBaseline | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $baselinePath -Encoding UTF8
    $oldCount = 0; foreach ($entry in @($baseline.diagnostics)) { $oldCount += [int]$entry.count }
    $newCount = 0; foreach ($entry in $updated) { $newCount += [int]$entry.count }
    $message = if ($mode -eq 'PruneResolved') { 'Pruned resolved or reduced Console errors without accepting new errors.' } else { 'Accepted current or increased Console errors without removing resolved baseline entries.' }
    $report = [ordered]@{ schemaVersion = 1; status = 'passed'; exitCode = 0; mode = $mode; startedAtUtc = $started.ToString('o'); completedAtUtc = [DateTime]::UtcNow.ToString('o'); baselinePath = $baselinePath; previous = [ordered]@{ entries = @($baseline.diagnostics).Count; occurrences = $oldCount }; current = [ordered]@{ entries = $updated.Count; occurrences = $newCount }; message = $message; checkReportPath = [string]$checkEnvelope.reportPath; artifacts = @($checkStdOut, $checkStdErr, [string]$checkEnvelope.reportPath, $reportPath) }
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    if ($Json) { [Console]::Out.WriteLine((ConvertTo-Json -Compress -InputObject ([ordered]@{ reportPath = $reportPath; status = 'passed'; exitCode = 0 }))) } else { Write-Host $message; Write-Host "Report: $reportPath" }
    exit 0
}
catch {
    $report = [ordered]@{ schemaVersion = 1; status = 'failed'; exitCode = 1; mode = $mode; startedAtUtc = $started.ToString('o'); completedAtUtc = [DateTime]::UtcNow.ToString('o'); baselinePath = $baselinePath; message = $_.Exception.Message; artifacts = @($checkStdOut, $checkStdErr, $reportPath) }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    if ($Json) { [Console]::Out.WriteLine((ConvertTo-Json -Compress -InputObject ([ordered]@{ reportPath = $reportPath; status = 'failed'; exitCode = 1 }))) } else { Write-Host "Console baseline update failed: $($_.Exception.Message)"; Write-Host "Report: $reportPath" }
    exit 1
}
