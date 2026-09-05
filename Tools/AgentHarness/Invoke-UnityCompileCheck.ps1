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
    $ArtifactDirectory = Join-Path $projectRoot (Join-Path 'Artifacts\Validation\UnityCompile' $id)
}
elseif (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $projectRoot $ArtifactDirectory
}
$ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
$reportPath = Join-Path $ArtifactDirectory 'unity-compile-report.json'
$unityCli = Join-Path $env:LOCALAPPDATA 'Unity\bin\unity.exe'
$sequence = 0
$artifacts = @()
$invocations = @()
$errors = @()
$report = [ordered]@{
    schemaVersion = 1; status = 'failed'; exitCode = 2; startedAtUtc = $started.ToString('o'); completedAtUtc = $null
    projectRoot = $projectRoot; unityCli = $unityCli; timeoutSeconds = $TimeoutSeconds
    precondition = $null; finalState = $null; errors = @(); invocations = @(); artifacts = @()
}

function Invoke-CompileCli([string]$Name, [string[]]$Arguments) {
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

function Complete-Compile([string]$Status, [int]$Code, $FinalState) {
    $report.status = $Status; $report.exitCode = $Code; $report.finalState = $FinalState
    $report.completedAtUtc = [DateTime]::UtcNow.ToString('o'); $report.errors = @($script:errors | ForEach-Object { [string]$_ })
    $report.invocations = @($script:invocations); $report.artifacts = @($script:artifacts | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { [System.IO.Path]::GetFullPath($_) } | Sort-Object -Unique) + @($reportPath)
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    if ($Json) { [Console]::Out.WriteLine((ConvertTo-Json -Compress -InputObject ([ordered]@{ reportPath = $reportPath; status = $Status; exitCode = $Code }))) }
    else { Write-Host "Unity compile check: $Status (exit $Code)"; Write-Host "Report: $reportPath" }
    exit $Code
}

try {
    if (-not (Test-Path (Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt')) -or -not (Test-Path (Join-Path $projectRoot 'Packages\manifest.json'))) {
        $errors += 'The script location does not resolve to a valid Unity project.'
        Complete-Compile 'failed' 2 $null
    }
    if (-not (Test-Path -LiteralPath $unityCli -PathType Leaf)) {
        $errors += "Unity CLI was not found at '$unityCli'."
        Complete-Compile 'not_available' 3 $null
    }

    $status = Invoke-CompileCli 'status' @('status', '--project-path', $projectRoot, '--format', 'json', '--no-banner')
    if (-not $status.execution.available -or $status.execution.exitCode -ne 0 -or $status.parseError -or -not $status.envelope.success) {
        $errors += 'A ready connected Unity Editor for this project was not available.'
        Complete-Compile 'not_available' 3 $null
    }
    $instances = if ($status.envelope.data.PSObject.Properties['instances']) { @($status.envelope.data.instances) } else { @($status.envelope.data) }
    $ready = @($instances | Where-Object {
        $path = if ($_.PSObject.Properties['projectPath']) { [string]$_.projectPath } elseif ($_.PSObject.Properties['project']) { [string]$_.project } else { '' }
        $_.state -eq 'ready' -and $path -and [System.IO.Path]::GetFullPath($path) -eq $projectRoot
    }).Count -gt 0
    if (-not $ready) { $errors += 'No ready Editor matched this exact project.'; Complete-Compile 'not_available' 3 $null }

    $contextCall = Invoke-CompileCli 'context' @('command', 'rq_validation_context', '--project-path', $projectRoot, '--format', 'json', '--no-banner')
    if (-not $contextCall.execution.available -or $contextCall.execution.exitCode -ne 0 -or $contextCall.parseError -or -not $contextCall.envelope.success -or -not $contextCall.envelope.data.result) {
        $errors += 'Unity validation context was unavailable or malformed.'
        Complete-Compile 'not_available' 3 $null
    }
    $context = $contextCall.envelope.data.result
    $report.precondition = $context
    $playState = [string]$context.playModeState
    if ([bool]$context.isCompiling -or [bool]$context.isUpdating) {
        $errors += 'Unity Editor is already compiling or updating.'
        Complete-Compile 'blocked' 3 $context
    }
    if ([bool]$context.isPlaying -or [bool]$context.isPlayingOrWillChangePlaymode -or $playState -in @('Playing', 'Paused', 'Changing')) {
        $errors += 'Unity Editor is in or entering Play Mode.'
        Complete-Compile 'blocked' 3 $context
    }

    $recompile = Invoke-CompileCli 'recompile' @('command', 'recompile', '--project-path', $projectRoot, '--format', 'json', '--no-banner')
    if (-not $recompile.execution.available -or $recompile.execution.exitCode -ne 0 -or $recompile.parseError -or -not $recompile.envelope.success) {
        $errors += 'Unity recompile command failed to start.'
        Complete-Compile 'failed' 1 $null
    }
    $initialState = $recompile.envelope.data.result
    if ($initialState -and $initialState.PSObject.Properties['status'] -and [string]$initialState.status -eq 'up_to_date') {
        Complete-Compile 'passed' 0 $initialState
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastState = $initialState
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $poll = Invoke-CompileCli 'recompile-status' @('command', 'recompile_status', '--project-path', $projectRoot, '--format', 'json', '--no-banner')
        if (-not $poll.execution.available -or $poll.execution.exitCode -ne 0 -or $poll.parseError -or -not $poll.envelope.success) {
            continue
        }
        $nested = $poll.envelope.data.result
        try {
            $state = if ($nested -is [string]) { $nested | ConvertFrom-Json -ErrorAction Stop } else { $nested }
        }
        catch { continue }
        if (-not $state) { continue }
        $lastState = $state
        $stateName = if ($state.PSObject.Properties['status']) { [string]$state.status } else { '' }
        if ($stateName -eq 'up_to_date') { Complete-Compile 'passed' 0 $state }
        if ($stateName -eq 'completed') {
            $failed = $state.PSObject.Properties['failed'] -and [bool]$state.failed
            if ($state.PSObject.Properties['errors']) { $errors += @($state.errors | ForEach-Object { [string]$_ }) }
            if ($failed) { Complete-Compile 'failed' 1 $state }
            Complete-Compile 'passed' 0 $state
        }
    }
    $errors += "Unity compilation did not complete within $TimeoutSeconds seconds."
    Complete-Compile 'blocked' 3 $lastState
}
catch {
    $errors += $_.Exception.Message
    Complete-Compile 'failed' 2 $null
}
