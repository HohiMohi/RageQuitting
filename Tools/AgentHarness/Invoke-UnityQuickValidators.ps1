[CmdletBinding()]
param(
    [string]$ArtifactDirectory,
    [switch]$Json,
    [ValidateRange(10, 1800)][int]$TimeoutSeconds = 180
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
Import-Module (Join-Path $scriptDirectory 'AgentHarness.psm1') -Force
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDirectory '..\..'))
$started = [DateTime]::UtcNow
if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $id = '{0}-{1}' -f $started.ToString('yyyyMMddTHHmmssZ'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
    $ArtifactDirectory = Join-Path $projectRoot (Join-Path 'Artifacts\Validation\UnityQuick' $id)
}
elseif (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $projectRoot $ArtifactDirectory
}
$ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
$reportPath = Join-Path $ArtifactDirectory 'unity-quick-validators-report.json'
$unityCli = Join-Path $env:LOCALAPPDATA 'Unity\bin\unity.exe'
$sequence = 0
$artifacts = @()
$invocations = @()
$errors = @()
$report = [ordered]@{
    schemaVersion = 1; status = 'failed'; exitCode = 1; startedAtUtc = $started.ToString('o'); completedAtUtc = $null
    projectRoot = $projectRoot; unityCli = $unityCli; timeoutSeconds = $TimeoutSeconds
    precondition = $null; finalState = $null; errors = @(); invocations = @(); artifacts = @()
}

function Invoke-QuickCli([string]$Name, [string[]]$Arguments) {
    $script:sequence++
    $prefix = '{0:D3}-{1}' -f $script:sequence, $Name
    $stdout = Join-Path $ArtifactDirectory ($prefix + '.stdout.txt')
    $stderr = Join-Path $ArtifactDirectory ($prefix + '.stderr.txt')
    $execution = Invoke-HarnessCommand -FilePath $unityCli -Arguments $Arguments -StdOutPath $stdout -StdErrPath $stderr -WorkingDirectory $projectRoot
    $script:artifacts += @($stdout, $stderr)
    $text = if (Test-Path -LiteralPath $stdout) { Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue } else { '' }
    $envelope = $null
    $parseError = $null
    try {
        if ([string]::IsNullOrWhiteSpace($text)) { throw 'Unity CLI returned empty stdout.' }
        $envelope = $text | ConvertFrom-Json -ErrorAction Stop
    }
    catch { $parseError = $_.Exception.Message }
    $record = [ordered]@{ name = $Name; exitCode = $execution.exitCode; available = [bool]$execution.available; stdout = [System.IO.Path]::GetFullPath($stdout); stderr = [System.IO.Path]::GetFullPath($stderr); parseError = $parseError }
    $script:invocations += $record
    return [ordered]@{ execution = $execution; envelope = $envelope; parseError = $parseError; record = $record }
}

function Complete-Quick([string]$Status, [int]$Code, $FinalEditorState) {
    $report.status = $Status; $report.exitCode = $Code; $report.finalState = $FinalEditorState
    $report.completedAtUtc = [DateTime]::UtcNow.ToString('o'); $report.errors = @($script:errors | ForEach-Object { [string]$_ })
    $report.invocations = @($script:invocations); $report.artifacts = @($script:artifacts | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { [System.IO.Path]::GetFullPath($_) } | Sort-Object -Unique) + @($reportPath)
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    if ($Json) { [Console]::Out.WriteLine((ConvertTo-Json -Compress -InputObject ([ordered]@{ reportPath = $reportPath; status = $Status; exitCode = $Code }))) }
    else { Write-Host "Unity quick-validators check: $Status (exit $Code)"; Write-Host "Report: $reportPath" }
    exit $Code
}

function Test-HealthyCall($Call) {
    return $Call.execution.available -and $Call.execution.exitCode -eq 0 -and
        -not $Call.parseError -and $Call.envelope -and [bool]$Call.envelope.success
}

function Get-VerifiedContext($Call) {
    if (-not (Test-HealthyCall $Call)) { throw 'Validation context command failed.' }
    $context = $Call.envelope.data.result
    $required = @('projectPath', 'openScenes', 'activeScenePath', 'isCompiling', 'isUpdating',
        'isPlaying', 'isPlayingOrWillChangePlaymode', 'playModeState')
    if (-not $context -or @($required | Where-Object { -not $context.PSObject.Properties[$_] }).Count -gt 0) {
        throw 'Validation context is malformed.'
    }
    if ([IO.Path]::GetFullPath([string]$context.projectPath) -ne $projectRoot) {
        throw 'Validation context belongs to a different project.'
    }
    foreach ($scene in @($context.openScenes)) {
        if (@(@('handle', 'path', 'isLoaded', 'isDirty', 'isActive') |
            Where-Object { -not $scene.PSObject.Properties[$_] }).Count -gt 0) {
            throw 'Validation scene context is malformed.'
        }
    }
    return $context
}

function Get-SceneFingerprint($Context) {
    return ConvertTo-Json -Compress -Depth 6 -InputObject ([ordered]@{
        activeScenePath = $Context.activeScenePath
        scenes = @($Context.openScenes | Select-Object handle, path, isLoaded, isDirty, isActive)
    })
}

$after = $null
try {
    if (-not (Test-Path (Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt')) -or
        -not (Test-Path (Join-Path $projectRoot 'Packages\manifest.json'))) {
        throw 'The script location does not resolve to a valid Unity project.'
    }
    if (-not (Test-Path -LiteralPath $unityCli -PathType Leaf)) {
        $errors += "Unity CLI was not found at '$unityCli'."
        Complete-Quick 'not_available' 3 $null
    }

    $status = Invoke-QuickCli 'status' @('status', '--project-path', $projectRoot, '--format', 'json', '--no-banner')
    if (-not (Test-HealthyCall $status)) {
        $errors += 'A ready connected Unity Editor was not available.'
        Complete-Quick 'not_available' 3 $null
    }
    $instances = if ($status.envelope.data.PSObject.Properties['instances']) {
        @($status.envelope.data.instances)
    } else { @($status.envelope.data) }
    $ready = @($instances | Where-Object {
        $path = if ($_.PSObject.Properties['projectPath']) { [string]$_.projectPath }
            elseif ($_.PSObject.Properties['project']) { [string]$_.project } else { '' }
        $_.state -eq 'ready' -and $path -and [IO.Path]::GetFullPath($path) -eq $projectRoot
    }).Count -gt 0
    if (-not $ready) {
        $errors += 'No ready Editor matched this exact project.'
        Complete-Quick 'not_available' 3 $null
    }

    $contextCall = Invoke-QuickCli 'context-before' @(
        'command', 'rq_validation_context', '--project-path', $projectRoot, '--format', 'json', '--no-banner')
    if (-not (Test-HealthyCall $contextCall)) {
        $errors += 'Unity validation context was unavailable.'
        Complete-Quick 'not_available' 3 $null
    }
    $context = Get-VerifiedContext $contextCall
    $report.precondition = $context
    if ($context.isCompiling -or $context.isUpdating -or $context.isPlaying -or
        $context.isPlayingOrWillChangePlaymode -or $context.playModeState -ne 'Editing') {
        $errors += 'Quick validators require an idle EditMode Editor.'
        Complete-Quick 'blocked' 3 $null
    }

    $run = Invoke-QuickCli 'quick-validators' @(
        'command', 'rq_quick_validators', '--timeout', [string]$TimeoutSeconds,
        '--project-path', $projectRoot, '--format', 'json', '--no-banner')
    $afterCall = Invoke-QuickCli 'context-after' @(
        'command', 'rq_validation_context', '--project-path', $projectRoot, '--format', 'json', '--no-banner')
    $after = Get-VerifiedContext $afterCall
    $report['postcondition'] = $after
    $report['sceneStateUnchanged'] = (Get-SceneFingerprint $context) -ceq (Get-SceneFingerprint $after)
    if (-not $report.sceneStateUnchanged) {
        throw 'Quick validators changed the open scenes, active scene, loaded state or dirty flags. No scenes were saved or reloaded.'
    }
    if (-not (Test-HealthyCall $run)) {
        $codes = if ($run.envelope -and $run.envelope.PSObject.Properties['errors']) {
            @($run.envelope.errors | ForEach-Object { $_.code })
        } else { @() }
        if (-not $run.execution.available -or $codes -contains 'COMMAND_NOT_FOUND' -or $codes -contains 'UNKNOWN_COMMAND') {
            $errors += 'rq_quick_validators is not available.'
            Complete-Quick 'not_available' 3 $after
        }
        throw 'rq_quick_validators returned an unsuccessful or malformed response; see raw CLI artifacts.'
    }
    $result = $run.envelope.data.result
    $report['result'] = $result
    $required = @('schemaVersion', 'status', 'checks', 'total', 'passed', 'failed', 'blocked', 'durationMs')
    if (-not $result -or @($required | Where-Object { -not $result.PSObject.Properties[$_] }).Count -gt 0) {
        throw 'Quick validators result is missing required fields.'
    }
    $checks = @($result.checks)
    $expectedNames = @('FoundationConcreteFailureProbe', 'PlayerConcreteTrapProbe')
    if ($result.schemaVersion -ne 1 -or $checks.Count -ne 2 -or $result.total -ne 2) {
        throw 'Expected schema version 1 with exactly two quick validator checks.'
    }
    foreach ($check in $checks) {
        if (@(@('name', 'status', 'message', 'durationMs') |
            Where-Object { -not $check.PSObject.Properties[$_] }).Count -gt 0) {
            throw 'A quick validator check is missing required fields.'
        }
        if ($check.status -notin @('passed', 'failed', 'blocked') -or
            [string]::IsNullOrWhiteSpace([string]$check.message) -or [double]$check.durationMs -lt 0) {
            throw 'A quick validator check contains an invalid result.'
        }
    }
    if (@(Compare-Object $expectedNames @($checks | ForEach-Object { [string]$_.name })).Count -gt 0) {
        throw 'Quick validator names do not match the registered suite.'
    }
    foreach ($state in @('passed', 'failed', 'blocked')) {
        if ([int]$result.$state -ne @($checks | Where-Object { $_.status -eq $state }).Count) {
            throw 'Quick validator summary counts are inconsistent.'
        }
    }
    $expectedStatus = if ($result.failed -gt 0) { 'failed' }
        elseif ($result.blocked -gt 0) { 'blocked' } else { 'passed' }
    if ($result.status -ne $expectedStatus) { throw 'Quick validator aggregate status is inconsistent.' }
    $code = if ($expectedStatus -eq 'passed') { 0 } elseif ($expectedStatus -eq 'failed') { 1 } else { 3 }
    Complete-Quick $expectedStatus $code $after
}
catch {
    $errors += $_.Exception.Message
    Complete-Quick 'failed' 1 $after
}
