[CmdletBinding()]
param(
    [string]$ArtifactDirectory,
    [string]$Filter,
    [ValidateRange(10, 1800)][int]$TimeoutSeconds = 180,
    [switch]$IncludeExplicit,
    [switch]$Json,
    [ValidateSet('EditMode', 'PlayMode')][string]$Mode = 'EditMode'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
Import-Module (Join-Path $scriptDirectory 'AgentHarness.psm1') -Force
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDirectory '..\..'))
$started = [DateTime]::UtcNow
$pipelineMode = if ($Mode -eq 'PlayMode') { 'playmode' } else { 'editor' }
$modeSlug = $Mode.ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $id = '{0}-{1}' -f $started.ToString('yyyyMMddTHHmmssZ'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
    $ArtifactDirectory = Join-Path $projectRoot (Join-Path ("Artifacts\Validation\Unity" + $Mode) $id)
}
elseif (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $projectRoot $ArtifactDirectory
}
$ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
$reportPath = Join-Path $ArtifactDirectory ("unity-" + $modeSlug + "-tests-report.json")
$unityCli = Join-Path $env:LOCALAPPDATA 'Unity\bin\unity.exe'
$sequence = 0
$artifacts = @()
$invocations = @()
$errors = @()
$report = [ordered]@{
    schemaVersion = 1
    status = 'failed'
    exitCode = 1
    startedAtUtc = $started.ToString('o')
    completedAtUtc = $null
    projectRoot = $projectRoot
    unityCli = $unityCli
    mode = $Mode
    filter = $Filter
    includeExplicit = [bool]$IncludeExplicit
    timeoutSeconds = $TimeoutSeconds
    precondition = $null
    postcondition = $null
    sceneRestore = [ordered]@{ required = $false; attempted = $false; succeeded = $false }
    summary = [ordered]@{ total = 0; passed = 0; failed = 0; skipped = 0; inconclusive = 0 }
    tests = @()
    result = $null
    errors = @()
    invocations = @()
    artifacts = @()
}

function Invoke-TestCli([string]$Name, [string[]]$Arguments) {
    if ($Arguments[0] -eq 'command' -and $Arguments -notcontains '--timeout') { $Arguments += @('--timeout', '5') }
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
    $record = [ordered]@{
        name = $Name
        exitCode = $execution.exitCode
        available = [bool]$execution.available
        stdout = [System.IO.Path]::GetFullPath($stdout)
        stderr = [System.IO.Path]::GetFullPath($stderr)
        parseError = $parseError
    }
    $script:invocations += $record
    return [ordered]@{ execution = $execution; envelope = $envelope; parseError = $parseError; record = $record }
}

function Complete-Tests([string]$Status, [int]$Code) {
    $report.status = $Status
    $report.exitCode = $Code
    $report.completedAtUtc = [DateTime]::UtcNow.ToString('o')
    $report.errors = @($script:errors | ForEach-Object { [string]$_ })
    $report.invocations = @($script:invocations)
    $report.artifacts = @($script:artifacts |
        Where-Object { Test-Path -LiteralPath $_ } |
        ForEach-Object { [System.IO.Path]::GetFullPath($_) } |
        Sort-Object -Unique) + @([System.IO.Path]::GetFullPath($reportPath))
    $report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    if ($Json) {
        [Console]::Out.WriteLine((ConvertTo-Json -Compress -InputObject ([ordered]@{
            reportPath = [System.IO.Path]::GetFullPath($reportPath)
            status = $Status
            exitCode = $Code
        })))
    }
    else {
        Write-Host "Unity $Mode tests: $Status (exit $Code)"
        Write-Host "Report: $reportPath"
    }
    exit $Code
}

function Test-HealthyCall($Call) {
    return $Call.execution.available -and $Call.execution.exitCode -eq 0 -and
        -not $Call.parseError -and $Call.envelope -and [bool]$Call.envelope.success
}

function Get-ResultFromCall($Call) {
    if (-not (Test-HealthyCall $Call) -or -not $Call.envelope.data -or
        -not $Call.envelope.data.PSObject.Properties['result']) { return $null }
    $value = $Call.envelope.data.result
    if ($value -is [string]) {
        try { return $value | ConvertFrom-Json -ErrorAction Stop } catch { return $null }
    }
    return $value
}

function Get-ContextFromCall($Call) {
    $value = Get-ResultFromCall $Call
    if (-not $value) { return $null }
    $required = @('projectPath', 'openScenes', 'activeScenePath', 'isCompiling', 'isUpdating',
        'isPlaying', 'isPlayingOrWillChangePlaymode', 'playModeState')
    if (@($required | Where-Object { -not $value.PSObject.Properties[$_] }).Count -gt 0) { return $null }
    if ([IO.Path]::GetFullPath([string]$value.projectPath) -ne $projectRoot) { return $null }
    foreach ($scene in @($value.openScenes)) {
        if (@(@('path', 'isLoaded', 'isDirty', 'isActive') |
            Where-Object { -not $scene.PSObject.Properties[$_] }).Count -gt 0) { return $null }
    }
    return $value
}

function Test-IdleEditor($Context) {
    return $Context -and -not $Context.isCompiling -and -not $Context.isUpdating -and
        -not $Context.isPlaying -and -not $Context.isPlayingOrWillChangePlaymode -and
        $Context.playModeState -eq 'Editing'
}

function Get-SceneFingerprint($Context) {
    return ConvertTo-Json -Compress -Depth 6 -InputObject ([ordered]@{
        activeScenePath = $Context.activeScenePath
        scenes = @($Context.openScenes | Select-Object path, isLoaded, isActive)
    })
}

function Test-SameSceneSetup($Before, $After) {
    return $Before -and $After -and
        (Get-SceneFingerprint $Before) -ceq (Get-SceneFingerprint $After)
}

function Wait-StableEditMode {
    $deadline = [DateTime]::UtcNow.AddSeconds([Math]::Min(60, $TimeoutSeconds))
    $stable = 0
    $previous = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        $call = Invoke-TestCli 'wait-editmode' @(
            'command', 'rq_validation_context', '--project-path', $projectRoot, '--format', 'json', '--no-banner')
        $current = Get-ContextFromCall $call
        if (Test-IdleEditor $current) {
            $stable = if ($previous -and (Test-SameSceneSetup $previous $current)) { $stable + 1 } else { 1 }
            if ($stable -ge 3) { return $current }
            $previous = $current
        }
        else { $stable = 0; $previous = $null }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Restore-SceneSetup($Context) {
    $report.sceneRestore.attempted = $true
    # Restore order, unloaded scenes and active scene together. Escape C# verbatim literals.
    $entries = @($Context.openScenes | ForEach-Object {
        $scenePath = ([string]$_.path).Replace('"', '""')
        $loaded = ([bool]$_.isLoaded).ToString().ToLowerInvariant()
        $active = ([bool]$_.isActive).ToString().ToLowerInvariant()
        'new UnityEditor.SceneManagement.SceneSetup { path = @"' + $scenePath +
            '", isLoaded = ' + $loaded + ', isActive = ' + $active + ' }'
    })
    $code = @"
if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode ||
    UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating)
    throw new System.InvalidOperationException("Editor is not idle.");
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
        throw new System.InvalidOperationException("Refusing to discard dirty scene state.");
UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(
    new UnityEditor.SceneManagement.SceneSetup[] { ENTRIES });
return true;
"@
    $codePath = Join-Path $ArtifactDirectory 'restore-scenes.cs'
    $code.Replace('ENTRIES', ($entries -join ',')) | Set-Content -LiteralPath $codePath -Encoding UTF8
    $script:artifacts += $codePath
    $restore = Invoke-TestCli 'restore-scenes' @(
        'command', 'eval_file', '--file', $codePath, '--timeout', '30',
        '--project-path', $projectRoot, '--format', 'json', '--no-banner')
    $evaluation = Get-ResultFromCall $restore
    return $evaluation -and $evaluation.success -eq $true -and $evaluation.result -eq $true
}

try {
    if (-not (Test-Path (Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt')) -or
        -not (Test-Path (Join-Path $projectRoot 'Packages\manifest.json'))) {
        $errors += 'The script location does not resolve to a valid Unity project.'
        Complete-Tests 'failed' 1
    }
    if (-not (Test-Path -LiteralPath $unityCli -PathType Leaf)) {
        $errors += "Unity CLI was not found at '$unityCli'."
        Complete-Tests 'not_available' 3
    }

    $status = Invoke-TestCli 'status' @('status', '--project-path', $projectRoot, '--format', 'json', '--no-banner')
    if (-not (Test-HealthyCall $status)) {
        $errors += 'A ready connected Unity Editor for this project was not available.'
        Complete-Tests 'not_available' 3
    }
    $instances = if ($status.envelope.data.PSObject.Properties['instances']) {
        @($status.envelope.data.instances)
    }
    else { @($status.envelope.data) }
    $ready = @($instances | Where-Object {
        $path = if ($_.PSObject.Properties['projectPath']) { [string]$_.projectPath }
            elseif ($_.PSObject.Properties['project']) { [string]$_.project } else { '' }
        $_.state -eq 'ready' -and $path -and [System.IO.Path]::GetFullPath($path) -eq $projectRoot
    }).Count -gt 0
    if (-not $ready) {
        $errors += 'No ready Editor matched this exact project.'
        Complete-Tests 'not_available' 3
    }

    $contextCall = Invoke-TestCli 'context-before' @(
        'command', 'rq_validation_context', '--project-path', $projectRoot, '--format', 'json', '--no-banner')
    $context = Get-ContextFromCall $contextCall
    if (-not $context) {
        $errors += 'Unity validation context was unavailable or malformed.'
        Complete-Tests 'not_available' 3
    }
    $report.precondition = $context
    $playState = [string]$context.playModeState
    if ([bool]$context.isCompiling -or [bool]$context.isUpdating) {
        $errors += 'Unity Editor is compiling or updating.'
        Complete-Tests 'blocked' 3
    }
    if ([bool]$context.isPlaying -or [bool]$context.isPlayingOrWillChangePlaymode -or
        $playState -in @('Playing', 'Paused', 'Changing')) {
        $errors += 'Unity Editor is in or entering Play Mode.'
        Complete-Tests 'blocked' 3
    }
    $loadedScenes = @($context.openScenes | Where-Object { [bool]$_.isLoaded })
    $unsafeScenes = @($context.openScenes | Where-Object {
        [bool]$_.isDirty -or [string]::IsNullOrWhiteSpace([string]$_.path)
    })
    if ($unsafeScenes.Count -gt 0 -or $loadedScenes.Count -eq 0) {
        $errors += "$Mode tests require clean, saved open scenes because Unity Test Runner temporarily replaces the scene setup."
        Complete-Tests 'blocked' 3
    }

    if (-not (Test-IdleEditor $context)) {
        $errors += 'Tests require an idle EditMode Editor.'
        Complete-Tests 'blocked' 3
    }
    $priorCall = Invoke-TestCli 'test-status-before' @(
        'command', 'test_status', '--project-path', $projectRoot, '--format', 'json', '--no-banner')
    $prior = Get-ResultFromCall $priorCall
    if (-not $prior) {
        $errors += 'Could not verify that another Pipeline test run is not in progress.'
        Complete-Tests 'not_available' 3
    }
    if ($prior.status -eq 'running') {
        $errors += 'Another Pipeline test run is in progress.'
        Complete-Tests 'blocked' 3
    }

    $result = $null
    $executionStatus = $null
    $acceptedAsync = $false
    try {
        $async = if ($Mode -eq 'PlayMode') { 'true' } else { 'false' }
        $arguments = @(
            'command', 'run_tests', '--mode', $pipelineMode, '--filter_type', 'testName',
            '--async_tests', $async, '--timeout', [string]$TimeoutSeconds,
            '--project-path', $projectRoot, '--format', 'json', '--no-banner')
        if (-not [string]::IsNullOrWhiteSpace($Filter)) { $arguments += @('--filter', $Filter) }
        if ($IncludeExplicit) { $arguments += @('--include_explicit', 'true') }
        $testCall = Invoke-TestCli 'run-tests' $arguments
        $startResult = Get-ResultFromCall $testCall
        $report['startResult'] = $startResult
        if (-not (Test-HealthyCall $testCall)) {
            $codes = if ($testCall.envelope -and $testCall.envelope.PSObject.Properties['errors']) {
                @($testCall.envelope.errors | ForEach-Object { $_.code })
            } else { @() }
            $executionStatus = if (-not $testCall.execution.available -or
                $codes -contains 'COMMAND_NOT_FOUND' -or $codes -contains 'UNKNOWN_COMMAND') { 'not_available' } else { 'failed' }
            $errors += 'run_tests did not return an acknowledged result. It will not be reissued.'
        }
        elseif (-not $startResult -or ($startResult.PSObject.Properties['Success'] -and -not $startResult.Success)) {
            $executionStatus = 'failed'
            $errors += 'Pipeline rejected test execution; see startResult and raw artifacts.'
        }
        elseif ($Mode -eq 'PlayMode') {
            if ($startResult.Result -ne 'running' -or $startResult.Mode -ne 'PlayMode') {
                throw 'Pipeline did not acknowledge an asynchronous PlayMode run.'
            }
            $acceptedAsync = $true
            $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
            while ([DateTime]::UtcNow -lt $deadline) {
                Start-Sleep -Milliseconds 500
                $poll = Invoke-TestCli 'test-status' @(
                    'command', 'test_status', '--project-path', $projectRoot, '--format', 'json', '--no-banner')
                $state = Get-ResultFromCall $poll
                # Temporary transport failures during domain reload are expected.
                if (-not $state) { continue }
                $report['lastTestStatus'] = $state
                if ($state.status -eq 'completed') { $result = $state; break }
                if ($state.status -in @('error', 'cancelled')) {
                    $result = $state
                    $executionStatus = 'failed'
                    $errors += "Pipeline tests ended with status '$($state.status)'."
                    break
                }
                if ($state.status -notin @('running', 'no_tests')) { throw 'Unknown Pipeline test status.' }
            }
            if (-not $result) {
                $executionStatus = 'blocked'
                $errors += "Tests did not return results within $TimeoutSeconds seconds."
            }
        }
        else { $result = $startResult }
    }
    catch {
        $executionStatus = 'failed'
        $errors += $_.Exception.Message
    }
    finally {
        $report.result = $result
        if ($acceptedAsync -and -not $result) {
            # Only cancel a run acknowledged as ours; never retry run_tests.
            $cancel = Invoke-TestCli 'cancel-tests' @(
                'command', 'cancel_tests', '--project-path', $projectRoot, '--format', 'json', '--no-banner')
            $report['cancellation'] = $cancel.envelope
            $stop = Invoke-TestCli 'stop-owned-playmode' @(
                'command', 'editor_stop', '--project-path', $projectRoot, '--format', 'json', '--no-banner')
            $report['stopPlayMode'] = $stop.envelope
        }
        $afterContext = Wait-StableEditMode
        $report.postcondition = $afterContext
        if (-not $afterContext) {
            $errors += 'Editor did not return to stable EditMode; scene restoration was not attempted.'
            if ($executionStatus -ne 'failed') { $executionStatus = 'blocked' }
        }
        elseif (@($afterContext.openScenes | Where-Object { $_.isDirty }).Count -gt 0) {
            $errors += 'Tests left dirty scenes; refusing to save or discard their contents.'
            $executionStatus = 'failed'
        }
        else {
            $report.sceneRestore.required = -not (Test-SameSceneSetup $context $afterContext)
            $report.sceneRestore.succeeded = if ($report.sceneRestore.required) {
                Restore-SceneSetup $context
            } else { $true }
            $finalContext = Wait-StableEditMode
            $report.postcondition = $finalContext
            if (-not $report.sceneRestore.succeeded -or -not (Test-SameSceneSetup $context $finalContext) -or
                @($finalContext.openScenes | Where-Object { $_.isDirty }).Count -gt 0) {
                $errors += 'Original scene setup and clean state could not be verified after tests.'
                $executionStatus = 'failed'
            }
        }
    }
    if ($executionStatus) {
        $code = if ($executionStatus -eq 'failed') { 1 } else { 3 }
        Complete-Tests $executionStatus $code
    }
    if (-not $result -or -not $result.PSObject.Properties['Summary'] -or -not $result.PSObject.Properties['Results']) {
        $errors += 'Unity run_tests result is missing Summary or Results.'
        Complete-Tests 'failed' 1
    }
    $summary = $result.Summary
    foreach ($field in @('Total', 'Passed', 'Failed', 'Skipped', 'Inconclusive')) {
        if (-not $summary.PSObject.Properties[$field]) {
            $errors += "Unity test Summary is missing '$field'."
            Complete-Tests 'failed' 1
        }
    }
    $report.result = $result
    $report.tests = @($result.Results)
    $report.summary = [ordered]@{
        total = [int]$summary.Total
        passed = [int]$summary.Passed
        failed = [int]$summary.Failed
        skipped = [int]$summary.Skipped
        inconclusive = [int]$summary.Inconclusive
    }
    if ($report.summary.total -eq 0) {
        $errors += 'Unity Test Runner discovered zero tests for the requested filter.'
        Complete-Tests 'failed' 1
    }
    if ($report.summary.failed -gt 0 -or $report.summary.inconclusive -gt 0) {
        $errors += "Unity $Mode tests failed or were inconclusive (failed=$($report.summary.failed), inconclusive=$($report.summary.inconclusive))."
        Complete-Tests 'failed' 1
    }
    Complete-Tests 'passed' 0
}
catch {
    $errors += $_.Exception.Message
    Complete-Tests 'failed' 1
}
