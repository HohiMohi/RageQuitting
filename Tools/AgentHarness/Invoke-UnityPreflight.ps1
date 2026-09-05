[CmdletBinding()]
param(
    [ValidateSet('Auto', 'Fast', 'Gameplay', 'Full')]
    [string]$Tier = 'Auto',
    [Alias('Filter')]
    [string]$EditModeFilter,
    [string]$PlayModeFilter,
    [string]$Scenario,
    [switch]$AllowLowerTier,
    [switch]$KeepArtifacts,
    [switch]$Json
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDirectory '..\..'))
$modulePath = Join-Path $scriptDirectory 'AgentHarness.psm1'
Import-Module $modulePath -Force

$startedAt = [DateTime]::UtcNow
$runId = '{0}-{1}' -f $startedAt.ToString('yyyyMMddTHHmmssZ'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
$artifactRoot = Join-Path $projectRoot 'Artifacts\Validation'
$artifactDirectory = Join-Path $artifactRoot $runId
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$summaryPath = Join-Path $artifactDirectory 'summary.json'

$steps = @()
$warnings = @()
$artifacts = @()
$selectionReasons = @()
$gitBefore = $null
$gitAfter = $null
$resolvedTier = if ($Tier -eq 'Auto') { 'Fast' } else { $Tier }
$backend = 'unknown'
$finalStatus = 'failed'
$exitCode = 2

function Add-ArtifactPath([string]$Path) {
    if ($Path -and (Test-Path -LiteralPath $Path)) {
        $script:artifacts += [System.IO.Path]::GetFullPath($Path)
    }
}

function Write-Summary {
    $plainSteps = @(
        $steps | ForEach-Object {
            [ordered]@{
                name = [string]$_['name']
                status = [string]$_['status']
                message = [string]$_['message']
                artifacts = @($_['artifacts'] | ForEach-Object { [string]$_ })
            }
        }
    )

    $summary = [ordered]@{
        schemaVersion = 1
        runId = $runId
        requestedTier = $Tier
        resolvedTier = $resolvedTier
        editModeFilter = $EditModeFilter
        playModeFilter = $PlayModeFilter
        startedAtUtc = $startedAt.ToString('o')
        completedAtUtc = [DateTime]::UtcNow.ToString('o')
        status = $finalStatus
        exitCode = $exitCode
        backend = $backend
        gitBefore = $gitBefore
        gitAfter = $gitAfter
        steps = $plainSteps
        warnings = @($warnings | ForEach-Object { [string]$_ })
        artifacts = @($artifacts | ForEach-Object { [string]$_ })
        selectionReasons = @($selectionReasons | ForEach-Object { [string]$_ })
    }

    ConvertTo-Json -InputObject $summary -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    return $summary
}

function Invoke-PreflightUnityCheck {
    param(
        [Parameter(Mandatory = $true)][string]$StepName,
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string]$ChildArtifactDirectory,
        [string[]]$ExtraArguments = @()
    )

    $stdout = Join-Path $artifactDirectory ($StepName + '.stdout.txt')
    $stderr = Join-Path $artifactDirectory ($StepName + '.stderr.txt')
    $stepArtifacts = @()
    $status = 'not_available'
    $message = $null
    if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) {
        $message = "Required script was not found at '$ScriptPath'."
    }
    else {
        $systemRoot = if ([string]::IsNullOrWhiteSpace($env:SystemRoot)) { 'C:\Windows' } else { $env:SystemRoot }
        $powerShellPath = Join-Path $systemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        if (-not (Test-Path -LiteralPath $powerShellPath -PathType Leaf)) {
            $powerShellCommand = Get-Command 'powershell.exe' -ErrorAction SilentlyContinue
            $powerShellPath = if ($powerShellCommand) { [string]$powerShellCommand.Source } else { $null }
        }
        if ([string]::IsNullOrWhiteSpace($powerShellPath)) {
            $message = 'Windows PowerShell 5.1 was not available.'
        }
        else {
            $childArguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File',
                $ScriptPath, '-ArtifactDirectory', $ChildArtifactDirectory, '-Json') + @($ExtraArguments)
            $execution = Invoke-HarnessCommand -FilePath $powerShellPath -Arguments $childArguments -StdOutPath $stdout -StdErrPath $stderr -WorkingDirectory $projectRoot
            Add-ArtifactPath $stdout; Add-ArtifactPath $stderr
            $stepArtifacts += @($stdout, $stderr)
            if (-not $execution.available) {
                $message = [string]$execution.error
            }
            else {
                $text = Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue
                $envelope = $null
                try {
                    if ([string]::IsNullOrWhiteSpace($text)) { throw 'Child check returned empty stdout.' }
                    $envelope = $text | ConvertFrom-Json -ErrorAction Stop
                }
                catch {
                    $status = 'failed'; $message = "Child check output could not be parsed: $($_.Exception.Message)"
                }
                $required = @()
                if ($envelope) { $required = @(@('reportPath', 'status', 'exitCode') | Where-Object { -not $envelope.PSObject.Properties[$_] }) }
                else { $required = @('envelope') }
                if ($envelope -and $required.Count -gt 0) {
                    $status = 'failed'; $message = "Child check output is missing required fields: $($required -join ', ')."
                }
                elseif ($envelope -and $required.Count -eq 0) {
                    $reportPath = [string]$envelope.reportPath
                    if ([string]::IsNullOrWhiteSpace($reportPath) -or -not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
                        $status = 'failed'; $message = "Child check report does not exist at '$reportPath'."
                    }
                    else {
                        $reportPath = [System.IO.Path]::GetFullPath($reportPath); Add-ArtifactPath $reportPath; $stepArtifacts += $reportPath
                        try { $childReport = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -ErrorAction Stop } catch { $childReport = $null; $status = 'failed'; $message = "Child report could not be parsed: $($_.Exception.Message)" }
                        if ($childReport) {
                            foreach ($path in @($childReport.artifacts)) {
                                if (-not [string]::IsNullOrWhiteSpace([string]$path) -and (Test-Path -LiteralPath ([string]$path))) { Add-ArtifactPath ([string]$path); $stepArtifacts += [System.IO.Path]::GetFullPath([string]$path) }
                            }
                            $processExit = [int]$execution.exitCode; $reportedExit = [int]$envelope.exitCode; $reportedStatus = [string]$envelope.status; $reportStatus = [string]$childReport.status
                            if ($processExit -eq 0 -and $reportedExit -eq 0 -and $reportedStatus -eq 'passed' -and $reportStatus -eq 'passed') { $status = 'passed'; $message = "$StepName passed. See '$reportPath'." }
                            elseif ($processExit -eq 1 -and $reportedExit -eq 1 -and $reportedStatus -eq 'failed' -and $reportStatus -eq 'failed') { $status = 'failed'; $message = "$StepName failed. See '$reportPath'." }
                            elseif ($processExit -eq 3 -and $reportedExit -eq 3 -and $reportedStatus -eq 'blocked' -and $reportStatus -eq 'blocked') { $status = 'blocked'; $message = "$StepName was blocked. See '$reportPath'." }
                            elseif ($processExit -eq 3 -and $reportedExit -eq 3 -and $reportedStatus -eq 'not_available' -and $reportStatus -eq 'not_available') { $status = 'not_available'; $message = "$StepName is not available. See '$reportPath'." }
                            else { $status = 'failed'; $message = "Child check returned an inconsistent result (process=$processExit, reported=$reportedExit, envelope=$reportedStatus, report=$reportStatus)." }
                        }
                    }
                }
            }
        }
    }
    $stepArtifacts = @($stepArtifacts | Where-Object { $_ -and (Test-Path -LiteralPath ([string]$_)) } | ForEach-Object { [System.IO.Path]::GetFullPath([string]$_) } | Sort-Object -Unique)
    if ($status -eq 'not_available' -and $AllowLowerTier) { $script:warnings += "$StepName was skipped because it is unavailable: $message"; return New-HarnessStep -Name $StepName -Status 'skipped' -Message $message -Artifacts $stepArtifacts }
    return New-HarnessStep -Name $StepName -Status $status -Message $message -Artifacts $stepArtifacts
}

try {
    if (-not $KeepArtifacts -and (Test-Path -LiteralPath $artifactRoot)) {
        $retentionCutoff = [DateTime]::UtcNow.AddDays(-14)
        Get-ChildItem -LiteralPath $artifactRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -ne $artifactDirectory -and $_.LastWriteTimeUtc -lt $retentionCutoff } |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }

    $requiredFiles = @(
        (Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt'),
        (Join-Path $projectRoot 'Packages\manifest.json')
    )
    $missingFiles = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
    if ($missingFiles.Count -gt 0) {
        $steps += New-HarnessStep -Name 'project-environment' -Status 'failed' -Message ('Missing required project files: ' + ($missingFiles -join ', '))
        throw 'Unity project root verification failed.'
    }
    $steps += New-HarnessStep -Name 'project-environment' -Status 'passed' -Message "Unity project root verified at '$projectRoot'."

    $gitBefore = Get-HarnessGitSnapshot -ProjectRoot $projectRoot -ArtifactDirectory $artifactDirectory -Label 'before'
    Get-ChildItem -LiteralPath $artifactDirectory -File -Filter 'git-before-*' | ForEach-Object { Add-ArtifactPath $_.FullName }
    if (-not $gitBefore.available) {
        $steps += New-HarnessStep -Name 'git-before' -Status 'not_available' -Message $gitBefore.error
        $warnings += 'Git state was unavailable; tracked-file mutation protection is incomplete.'
    }
    else {
        $steps += New-HarnessStep -Name 'git-before' -Status 'passed' -Message "Captured Git state at $($gitBefore.head)."
    }

    $tierResolution = Resolve-HarnessTier -RequestedTier $Tier -MapPath (Join-Path $scriptDirectory 'validation-map.json') -GitSnapshot $gitBefore
    $resolvedTier = $tierResolution.tier
    $selectionReasons = @($tierResolution['reasons'] | ForEach-Object { [string]$_ })

    $analyzerScript = Join-Path $scriptDirectory 'Invoke-AnalyzerCheck.ps1'
    $analyzerDirectory = Join-Path $artifactDirectory 'analyzer'
    $analyzerStdOut = Join-Path $artifactDirectory 'analyzer-check.stdout.txt'
    $analyzerStdErr = Join-Path $artifactDirectory 'analyzer-check.stderr.txt'
    $analyzerArtifacts = @()
    $analyzerStatus = 'not_available'
    $analyzerMessage = $null

    if (-not (Test-Path -LiteralPath $analyzerScript -PathType Leaf)) {
        $analyzerMessage = "Analyzer check script was not found at '$analyzerScript'."
    }
    else {
        $systemRoot = if ([string]::IsNullOrWhiteSpace($env:SystemRoot)) { 'C:\Windows' } else { $env:SystemRoot }
        $powerShellPath = Join-Path $systemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        if (-not (Test-Path -LiteralPath $powerShellPath -PathType Leaf)) {
            $powerShellCommand = Get-Command 'powershell.exe' -ErrorAction SilentlyContinue
            $powerShellPath = if ($powerShellCommand) { [string]$powerShellCommand.Source } else { $null }
        }

        if ([string]::IsNullOrWhiteSpace($powerShellPath)) {
            $analyzerMessage = 'Windows PowerShell 5.1 was not available to launch the analyzer check.'
        }
        else {
            $analyzerResult = Invoke-HarnessCommand -FilePath $powerShellPath -Arguments @(
                '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $analyzerScript,
                '-ArtifactDirectory', $analyzerDirectory, '-Json'
            ) -StdOutPath $analyzerStdOut -StdErrPath $analyzerStdErr -WorkingDirectory $projectRoot
            Add-ArtifactPath $analyzerStdOut
            Add-ArtifactPath $analyzerStdErr
            $analyzerArtifacts += @($analyzerStdOut, $analyzerStdErr)

            if (-not $analyzerResult['available']) {
                $analyzerMessage = [string]$analyzerResult['error']
            }
            else {
                $analyzerText = Get-Content -LiteralPath $analyzerStdOut -Raw -ErrorAction SilentlyContinue
                $analyzerEnvelope = $null
                $analyzerParseError = $null
                try {
                    if ([string]::IsNullOrWhiteSpace($analyzerText)) {
                        throw 'Analyzer check returned empty stdout.'
                    }
                    $analyzerEnvelope = $analyzerText | ConvertFrom-Json
                }
                catch {
                    $analyzerParseError = $_.Exception.Message
                }

                if ($analyzerParseError) {
                    $analyzerStatus = 'failed'
                    $analyzerMessage = "Analyzer check output could not be parsed: $analyzerParseError"
                }
                elseif (@('reportPath', 'status', 'exitCode') | Where-Object { -not $analyzerEnvelope.PSObject.Properties[$_] }) {
                    $analyzerStatus = 'failed'
                    $analyzerMessage = 'Analyzer check output did not contain the required reportPath, status, and exitCode fields.'
                }
                elseif ([string]::IsNullOrWhiteSpace([string]$analyzerEnvelope.reportPath)) {
                    $analyzerStatus = 'failed'
                    $analyzerMessage = 'Analyzer check output contained an empty reportPath.'
                }
                else {
                    $analyzerReportPath = [System.IO.Path]::GetFullPath([string]$analyzerEnvelope.reportPath)
                    if (-not (Test-Path -LiteralPath $analyzerReportPath -PathType Leaf)) {
                        $analyzerStatus = 'failed'
                        $analyzerMessage = "Analyzer report was not created at '$analyzerReportPath'."
                    }
                    else {
                        Add-ArtifactPath $analyzerReportPath
                        $analyzerArtifacts += $analyzerReportPath
                        $analyzerReport = $null
                        try {
                            $analyzerReport = Get-Content -LiteralPath $analyzerReportPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
                        }
                        catch {
                            $analyzerStatus = 'failed'
                            $analyzerMessage = "Analyzer report could not be parsed: $($_.Exception.Message)"
                        }

                        $missingReportProperties = @()
                        if ($analyzerReport) {
                            $missingReportProperties = @(@('status', 'artifacts', 'build') | Where-Object { -not $analyzerReport.PSObject.Properties[$_] })
                        }
                        if ($missingReportProperties.Count -gt 0) {
                            $analyzerStatus = 'failed'
                            $analyzerMessage = "Analyzer report is missing required fields: $($missingReportProperties -join ', ')."
                        }
                        elseif ($analyzerReport) {
                            foreach ($path in @($analyzerReport.artifacts)) {
                                if (-not [string]::IsNullOrWhiteSpace([string]$path)) {
                                    Add-ArtifactPath ([string]$path)
                                    if (Test-Path -LiteralPath ([string]$path)) {
                                        $analyzerArtifacts += [System.IO.Path]::GetFullPath([string]$path)
                                    }
                                }
                            }

                            $processExit = [int]$analyzerResult['exitCode']
                            $reportedExit = [int]$analyzerEnvelope.exitCode
                            $reportedStatus = [string]$analyzerEnvelope.status
                            $reportStatus = [string]$analyzerReport.status
                            if ($processExit -eq 0 -and $reportedExit -eq 0 -and $reportedStatus -eq 'passed' -and $reportStatus -eq 'passed') {
                                $analyzerStatus = 'passed'
                                $scope = if ($analyzerReport.build.PSObject.Properties['scope']) { [string]$analyzerReport.build.scope } else { 'configured scope' }
                                $topLevelProject = if ($analyzerReport.build.PSObject.Properties['topLevelProject']) { [string]$analyzerReport.build.topLevelProject } else { [string]$analyzerReport.build.project }
                                $analyzerMessage = "Analyzer ratchet passed for $scope via $topLevelProject."
                            }
                            elseif ($processExit -eq 1 -and $reportedExit -eq 1 -and $reportedStatus -eq 'failed' -and $reportStatus -eq 'failed') {
                                $analyzerStatus = 'failed'
                                $analyzerMessage = "Analyzer ratchet failed. See '$analyzerReportPath'."
                            }
                            elseif ($processExit -eq 3 -and $reportedExit -eq 3 -and $reportedStatus -eq 'not_available' -and $reportStatus -eq 'not_available') {
                                $analyzerStatus = 'not_available'
                                $analyzerMessage = "Analyzer ratchet is not available. See '$analyzerReportPath'."
                            }
                            else {
                                $analyzerStatus = 'failed'
                                $analyzerMessage = "Analyzer check returned an inconsistent or unknown result (process=$processExit, reported=$reportedExit, status=$reportedStatus)."
                            }
                        }
                    }
                }
            }
        }
    }

    $analyzerArtifacts = @($analyzerArtifacts | Where-Object { $_ -and (Test-Path -LiteralPath ([string]$_)) } | ForEach-Object { [System.IO.Path]::GetFullPath([string]$_) } | Sort-Object -Unique)
    if ($analyzerStatus -eq 'not_available' -and $AllowLowerTier) {
        $warnings += "Analyzer check was skipped because it is unavailable: $analyzerMessage"
        $steps += New-HarnessStep -Name 'analyzers' -Status 'skipped' -Message $analyzerMessage -Artifacts $analyzerArtifacts
    }
    else {
        $steps += New-HarnessStep -Name 'analyzers' -Status $analyzerStatus -Message $analyzerMessage -Artifacts $analyzerArtifacts
    }

    $unityCli = Join-Path $env:LOCALAPPDATA 'Unity\bin\unity.exe'
    $unityStdOut = Join-Path $artifactDirectory 'unity-status.stdout.txt'
    $unityStdErr = Join-Path $artifactDirectory 'unity-status.stderr.txt'
    $connected = $false
    if (-not (Test-Path -LiteralPath $unityCli -PathType Leaf)) {
        $backend = 'not_available'
        $steps += New-HarnessStep -Name 'unity-cli-status' -Status 'not_available' -Message "Unity CLI was not found at '$unityCli'."
    }
    else {
        $unityResult = Invoke-HarnessCommand -FilePath $unityCli -Arguments @('status', '--project-path', $projectRoot, '--format', 'json', '--no-banner') -StdOutPath $unityStdOut -StdErrPath $unityStdErr -WorkingDirectory $projectRoot
        Add-ArtifactPath $unityStdOut
        Add-ArtifactPath $unityStdErr
        if (-not $unityResult['available']) {
            $backend = 'not_available'
            $steps += New-HarnessStep -Name 'unity-cli-status' -Status 'not_available' -Message ([string]$unityResult['error']) -Artifacts @($unityStdOut, $unityStdErr)
        }
        else {
            $statusText = Get-Content -LiteralPath $unityStdOut -Raw -ErrorAction SilentlyContinue
            $statusEnvelope = $null
            $parseError = $null
            try {
                if ([string]::IsNullOrWhiteSpace($statusText)) {
                    throw 'Unity CLI returned empty stdout.'
                }
                $statusEnvelope = $statusText | ConvertFrom-Json
            }
            catch {
                $parseError = $_.Exception.Message
            }

            $unityExitCode = $unityResult['exitCode']
            $knownNoEditorCodes = @('STATUS_NO_INSTANCES', 'STATUS_ALL_UNREACHABLE')
            $errorCodes = @()
            if ($statusEnvelope -and $statusEnvelope.PSObject.Properties['errors']) {
                $errorCodes = @($statusEnvelope.errors | ForEach-Object { if ($_.PSObject.Properties['code']) { [string]$_.code } })
            }
            $isKnownNoEditor = $errorCodes.Count -gt 0 -and @($errorCodes | Where-Object { $knownNoEditorCodes -contains $_ }).Count -eq $errorCodes.Count

            if ($parseError) {
                $backend = 'not_available'
                $steps += New-HarnessStep -Name 'unity-cli-status' -Status 'not_available' -Message "Unity CLI status output could not be parsed: $parseError" -Artifacts @($unityStdOut, $unityStdErr)
            }
            elseif ($unityExitCode -ne 0 -and -not $isKnownNoEditor) {
                $backend = 'not_available'
                $details = if ($errorCodes.Count -gt 0) { $errorCodes -join ', ' } else { 'no structured error code' }
                $steps += New-HarnessStep -Name 'unity-cli-status' -Status 'not_available' -Message "Unity CLI status failed with exit $unityExitCode ($details)." -Artifacts @($unityStdOut, $unityStdErr)
            }
            elseif ($isKnownNoEditor) {
                $backend = 'headless'
                $steps += New-HarnessStep -Name 'unity-cli-status' -Status 'passed' -Message "Unity CLI reported no reachable Editor ($($errorCodes -join ', '))." -Artifacts @($unityStdOut, $unityStdErr)
            }
            elseif (-not $statusEnvelope.success) {
                $backend = 'not_available'
                $steps += New-HarnessStep -Name 'unity-cli-status' -Status 'not_available' -Message 'Unity CLI returned an unsuccessful status without a recognized no-editor code.' -Artifacts @($unityStdOut, $unityStdErr)
            }
            else {
                $instances = if ($statusEnvelope.data -and $statusEnvelope.data.PSObject.Properties['instances']) { @($statusEnvelope.data.instances) } elseif ($statusEnvelope.data) { @($statusEnvelope.data) } else { @() }
                $connected = @($instances | Where-Object {
                    $instanceProject = if ($_.PSObject.Properties['projectPath']) { [string]$_.projectPath } elseif ($_.PSObject.Properties['project']) { [string]$_.project } else { $null }
                    $_.state -eq 'ready' -and $instanceProject -and ([System.IO.Path]::GetFullPath($instanceProject) -eq $projectRoot)
                }).Count -gt 0
                $backend = if ($connected) { 'connected_editor' } else { 'headless' }
                $message = if ($connected) { 'Connected Unity Editor discovered.' } else { 'Unity CLI completed successfully; no ready Editor matched this project.' }
                $steps += New-HarnessStep -Name 'unity-cli-status' -Status 'passed' -Message $message -Artifacts @($unityStdOut, $unityStdErr)
            }
        }
    }

    if ($connected) {
        $contextStdOut = Join-Path $artifactDirectory 'unity-editor-context.stdout.txt'
        $contextStdErr = Join-Path $artifactDirectory 'unity-editor-context.stderr.txt'
        $contextResult = Invoke-HarnessCommand -FilePath $unityCli -Arguments @('command', 'rq_validation_context', '--project-path', $projectRoot, '--format', 'json', '--no-banner') -StdOutPath $contextStdOut -StdErrPath $contextStdErr -WorkingDirectory $projectRoot
        Add-ArtifactPath $contextStdOut
        Add-ArtifactPath $contextStdErr

        if (-not $contextResult['available']) {
            $steps += New-HarnessStep -Name 'editor-context' -Status 'not_available' -Message ([string]$contextResult['error']) -Artifacts @($contextStdOut, $contextStdErr)
        }
        elseif ($contextResult['exitCode'] -ne 0) {
            $steps += New-HarnessStep -Name 'editor-context' -Status 'not_available' -Message "Unity Editor context command failed with exit $($contextResult['exitCode'])." -Artifacts @($contextStdOut, $contextStdErr)
        }
        else {
            $contextText = Get-Content -LiteralPath $contextStdOut -Raw -ErrorAction SilentlyContinue
            $contextEnvelope = $null
            $contextParseError = $null
            try {
                if ([string]::IsNullOrWhiteSpace($contextText)) {
                    throw 'Unity Editor context command returned empty stdout.'
                }
                $contextEnvelope = $contextText | ConvertFrom-Json
            }
            catch {
                $contextParseError = $_.Exception.Message
            }

            $hasContextResult =
                $contextEnvelope -and
                $contextEnvelope.PSObject.Properties['data'] -and
                $contextEnvelope.data -and
                $contextEnvelope.data.PSObject.Properties['result'] -and
                $null -ne $contextEnvelope.data.result
            $missingContextProperties = @()
            if ($hasContextResult) {
                $requiredContextProperties = @('openScenes', 'isCompiling', 'isUpdating', 'isPlaying', 'isPlayingOrWillChangePlaymode', 'playModeState')
                $missingContextProperties = @($requiredContextProperties | Where-Object { -not $contextEnvelope.data.result.PSObject.Properties[$_] })
            }

            if ($contextParseError) {
                $steps += New-HarnessStep -Name 'editor-context' -Status 'not_available' -Message "Unity Editor context output could not be parsed: $contextParseError" -Artifacts @($contextStdOut, $contextStdErr)
            }
            elseif (-not $contextEnvelope -or -not $contextEnvelope.PSObject.Properties['success'] -or -not $contextEnvelope.success) {
                $steps += New-HarnessStep -Name 'editor-context' -Status 'not_available' -Message 'Unity Editor context command returned an unsuccessful envelope.' -Artifacts @($contextStdOut, $contextStdErr)
            }
            elseif (-not $hasContextResult) {
                $steps += New-HarnessStep -Name 'editor-context' -Status 'not_available' -Message 'Unity Editor context command returned no data.result.' -Artifacts @($contextStdOut, $contextStdErr)
            }
            elseif ($missingContextProperties.Count -gt 0) {
                $steps += New-HarnessStep -Name 'editor-context' -Status 'not_available' -Message ("Unity Editor context result is missing required fields: " + ($missingContextProperties -join ', ') + '.') -Artifacts @($contextStdOut, $contextStdErr)
            }
            else {
                $editorContext = $contextEnvelope.data.result
                $dirtyScenes = @(
                    @($editorContext.openScenes) |
                        Where-Object { $_.isDirty } |
                        ForEach-Object {
                            if (-not [string]::IsNullOrWhiteSpace([string]$_.path)) {
                                [string]$_.path
                            }
                            elseif (-not [string]::IsNullOrWhiteSpace([string]$_.name)) {
                                "<unsaved:$([string]$_.name)>"
                            }
                            else {
                                '<unsaved scene>'
                            }
                        }
                )
                $playModeState = [string]$editorContext.playModeState
                $isPlayMode =
                    [bool]$editorContext.isPlaying -or
                    [bool]$editorContext.isPlayingOrWillChangePlaymode -or
                    $playModeState -in @('Playing', 'Paused', 'Changing')

                if ([bool]$editorContext.isCompiling -or [bool]$editorContext.isUpdating) {
                    $steps += New-HarnessStep -Name 'editor-context' -Status 'blocked' -Message 'Unity Editor is compiling or updating. Wait for it to become idle, then run validation again.' -Artifacts @($contextStdOut, $contextStdErr)
                }
                elseif ($isPlayMode) {
                    $steps += New-HarnessStep -Name 'editor-context' -Status 'blocked' -Message "Unity Editor is in or entering Play Mode ($playModeState). Exit Play Mode, then run validation again." -Artifacts @($contextStdOut, $contextStdErr)
                }
                elseif ($dirtyScenes.Count -gt 0 -and $resolvedTier -eq 'Full') {
                    $steps += New-HarnessStep -Name 'editor-context' -Status 'blocked' -Message ("Full validation refuses unsaved scene state: " + ($dirtyScenes -join ', ') + '. Save or revert those scenes manually, then run validation again.') -Artifacts @($contextStdOut, $contextStdErr)
                }
                else {
                    if ($dirtyScenes.Count -gt 0) {
                        $warnings += "Unity Editor has unsaved scene state; validation will not save it: $($dirtyScenes -join ', ')."
                    }
                    $message = if ($dirtyScenes.Count -gt 0) {
                        "Unity Editor context is idle in Edit Mode; unsaved scenes were left untouched: $($dirtyScenes -join ', ')."
                    }
                    else {
                        'Unity Editor context is idle in Edit Mode with no dirty open scenes.'
                    }
                    $steps += New-HarnessStep -Name 'editor-context' -Status 'passed' -Message $message -Artifacts @($contextStdOut, $contextStdErr)
                }
            }
        }
    }

    $contextStep = @($steps | Where-Object { [string]$_['name'] -eq 'editor-context' } | Select-Object -Last 1)
    if (-not $connected) {
        $dependentStatus = if ($AllowLowerTier) { 'skipped' } else { 'not_available' }
        $dependentMessage = 'A connected Unity Editor is required for this check.'
        if ($AllowLowerTier) { $warnings += $dependentMessage }
        $steps += New-HarnessStep -Name 'compile' -Status $dependentStatus -Message $dependentMessage
        $steps += New-HarnessStep -Name 'console-baseline' -Status $dependentStatus -Message $dependentMessage
        $steps += New-HarnessStep -Name 'editmode-tests' -Status $dependentStatus -Message $dependentMessage
    }
    elseif ($contextStep.Count -eq 0 -or [string]$contextStep[0]['status'] -eq 'not_available') {
        $dependentStatus = if ($AllowLowerTier) { 'skipped' } else { 'not_available' }
        $dependentMessage = 'Unity checks were not run because editor-context was unavailable.'
        if ($AllowLowerTier) { $warnings += $dependentMessage }
        $steps += New-HarnessStep -Name 'compile' -Status $dependentStatus -Message $dependentMessage
        $steps += New-HarnessStep -Name 'console-baseline' -Status $dependentStatus -Message $dependentMessage
        $steps += New-HarnessStep -Name 'editmode-tests' -Status $dependentStatus -Message $dependentMessage
    }
    elseif ([string]$contextStep[0]['status'] -eq 'blocked') {
        $dependentMessage = 'Unity checks were blocked because editor-context was unsafe.'
        $steps += New-HarnessStep -Name 'compile' -Status 'blocked' -Message $dependentMessage
        $steps += New-HarnessStep -Name 'console-baseline' -Status 'blocked' -Message $dependentMessage
        $steps += New-HarnessStep -Name 'editmode-tests' -Status 'blocked' -Message $dependentMessage
    }
    else {
        $compileStep = Invoke-PreflightUnityCheck -StepName 'compile' -ScriptPath (Join-Path $scriptDirectory 'Invoke-UnityCompileCheck.ps1') -ChildArtifactDirectory (Join-Path $artifactDirectory 'compile')
        $steps += $compileStep
        $consoleStep = Invoke-PreflightUnityCheck -StepName 'console-baseline' -ScriptPath (Join-Path $scriptDirectory 'Invoke-UnityConsoleCheck.ps1') -ChildArtifactDirectory (Join-Path $artifactDirectory 'console')
        $steps += $consoleStep
        $dependencyStatuses = @([string]$compileStep['status'], [string]$consoleStep['status'])
        if ($dependencyStatuses -contains 'failed') {
            $steps += New-HarnessStep -Name 'editmode-tests' -Status 'failed' -Message 'EditMode tests were not run because compile or Console validation failed.'
        }
        elseif ($dependencyStatuses -contains 'blocked') {
            $steps += New-HarnessStep -Name 'editmode-tests' -Status 'blocked' -Message 'EditMode tests were not run because compile or Console validation was blocked.'
        }
        elseif ($dependencyStatuses -contains 'not_available') {
            $steps += New-HarnessStep -Name 'editmode-tests' -Status 'not_available' -Message 'EditMode tests were not run because compile or Console validation was unavailable.'
        }
        elseif ($dependencyStatuses -contains 'skipped') {
            $message = 'EditMode tests were skipped because compile or Console validation was unavailable.'
            $warnings += $message
            $steps += New-HarnessStep -Name 'editmode-tests' -Status 'skipped' -Message $message
        }
        else {
            $testArguments = @()
            if (-not [string]::IsNullOrWhiteSpace($EditModeFilter)) { $testArguments += @('-Filter', $EditModeFilter) }
            $steps += Invoke-PreflightUnityCheck -StepName 'editmode-tests' -ScriptPath (Join-Path $scriptDirectory 'Invoke-UnityEditModeTests.ps1') -ChildArtifactDirectory (Join-Path $artifactDirectory 'editmode-tests') -ExtraArguments $testArguments
        }
    }

    # All tiers share the real quick suite. A skipped compile is unavailable,
    # never permission to execute validators against potentially stale assemblies.
    $quickDependencies = @($steps | Where-Object {
        [string]$_['name'] -in @('editor-context', 'compile', 'console-baseline')
    } | ForEach-Object { [string]$_['status'] })
    if ($quickDependencies -contains 'failed') {
        $steps += New-HarnessStep -Name 'quick-validators' -Status 'failed' -Message 'Quick validators were not run because a prerequisite failed.'
    }
    elseif ($quickDependencies -contains 'blocked') {
        $steps += New-HarnessStep -Name 'quick-validators' -Status 'blocked' -Message 'Quick validators were not run because a prerequisite was unsafe or blocked.'
    }
    elseif (-not $connected -or $quickDependencies.Count -ne 3 -or
        $quickDependencies -contains 'not_available' -or $quickDependencies -contains 'skipped') {
        $quickStatus = if ($AllowLowerTier) { 'skipped' } else { 'not_available' }
        $steps += New-HarnessStep -Name 'quick-validators' -Status $quickStatus -Message 'Quick validators require available Editor context, compilation and Console checks.'
    }
    else {
        $steps += Invoke-PreflightUnityCheck -StepName 'quick-validators' -ScriptPath (Join-Path $scriptDirectory 'Invoke-UnityQuickValidators.ps1') -ChildArtifactDirectory (Join-Path $artifactDirectory 'quick-validators')
    }

    if ($resolvedTier -in @('Gameplay', 'Full')) {
        $playDependencies = @($steps | Where-Object {
            [string]$_['name'] -in @('editor-context', 'compile', 'console-baseline', 'editmode-tests', 'quick-validators')
        } | ForEach-Object { [string]$_['status'] })
        if ($playDependencies -contains 'failed') {
            $steps += New-HarnessStep -Name 'playmode-tests' -Status 'failed' -Message 'PlayMode tests were not run because a prerequisite failed.'
        }
        elseif ($playDependencies -contains 'blocked') {
            $steps += New-HarnessStep -Name 'playmode-tests' -Status 'blocked' -Message 'PlayMode tests were not run because a prerequisite was blocked.'
        }
        elseif (-not $connected -or $playDependencies.Count -ne 5 -or
            $playDependencies -contains 'not_available' -or $playDependencies -contains 'skipped') {
            $playStatus = if ($AllowLowerTier) { 'skipped' } else { 'not_available' }
            $steps += New-HarnessStep -Name 'playmode-tests' -Status $playStatus -Message 'PlayMode tests require all existing validation prerequisites.'
        }
        else {
            $playArguments = @()
            if (-not [string]::IsNullOrWhiteSpace($PlayModeFilter)) { $playArguments += @('-Filter', $PlayModeFilter) }
            $steps += Invoke-PreflightUnityCheck -StepName 'playmode-tests' -ScriptPath (Join-Path $scriptDirectory 'Invoke-UnityPlayModeTests.ps1') -ChildArtifactDirectory (Join-Path $artifactDirectory 'playmode-tests') -ExtraArguments $playArguments
        }
    }

    $placeholderNames = @()
    if ($resolvedTier -in @('Gameplay', 'Full')) {
        $placeholderNames += @('gameplay-scenario', 'network-smoke', 'screenshots')
    }
    if ($resolvedTier -eq 'Full') {
        $placeholderNames += @('all-tests', 'scene-prefab-validation', 'expanded-multiplayer', 'coverage', 'windows-build')
    }

    foreach ($placeholder in $placeholderNames) {
        $qualifier = @()
        if ($EditModeFilter) { $qualifier += "editModeFilter=$EditModeFilter" }
        if ($PlayModeFilter) { $qualifier += "playModeFilter=$PlayModeFilter" }
        if ($Scenario) { $qualifier += "scenario=$Scenario" }
        $suffix = if ($qualifier.Count -gt 0) { ' (' + ($qualifier -join ', ') + ')' } else { '' }
        if ($AllowLowerTier) {
            $steps += New-HarnessStep -Name $placeholder -Status 'skipped' -Message "Batch 1 placeholder skipped by -AllowLowerTier$suffix."
        }
        else {
            $steps += New-HarnessStep -Name $placeholder -Status 'not_available' -Message "Batch 1 does not implement this check yet$suffix."
        }
    }

    $gitAfter = Get-HarnessGitSnapshot -ProjectRoot $projectRoot -ArtifactDirectory $artifactDirectory -Label 'after'
    Get-ChildItem -LiteralPath $artifactDirectory -File -Filter 'git-after-*' | ForEach-Object { Add-ArtifactPath $_.FullName }
    if (-not $gitAfter.available) {
        $steps += New-HarnessStep -Name 'git-after' -Status 'not_available' -Message $gitAfter.error
        $warnings += 'Final Git state was unavailable; tracked-file mutation protection is incomplete.'
    }
    else {
        $steps += New-HarnessStep -Name 'git-after' -Status 'passed' -Message 'Captured final Git state.'
    }

    $trackedChangedDuringRun = $false
    if ($gitBefore -and $gitBefore.available -and $gitAfter -and $gitAfter.available) {
        $beforeTracked = @($gitBefore['trackedChanges'] | ForEach-Object { ([string]$_).Replace('\', '/') } | Sort-Object -Unique)
        $afterTracked = @($gitAfter['trackedChanges'] | ForEach-Object { ([string]$_).Replace('\', '/') } | Sort-Object -Unique)
        $delta = @(Compare-Object -ReferenceObject $beforeTracked -DifferenceObject $afterTracked)
        $fingerprintChanged =
            $gitBefore['head'] -ne $gitAfter['head'] -or
            $gitBefore['unstagedFingerprint'] -ne $gitAfter['unstagedFingerprint'] -or
            $gitBefore['stagedFingerprint'] -ne $gitAfter['stagedFingerprint']
        if ($delta.Count -gt 0 -or $fingerprintChanged) {
            $trackedChangedDuringRun = $true
            $deltaText = ($delta | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }) -join '; '
            if ([string]::IsNullOrWhiteSpace($deltaText)) {
                $deltaText = 'tracked diff content fingerprint or HEAD changed'
            }
            $steps += New-HarnessStep -Name 'tracked-file-mutation-guard' -Status 'failed' -Message "Tracked Git state differed after the run: $deltaText"
        }
        else {
            $steps += New-HarnessStep -Name 'tracked-file-mutation-guard' -Status 'passed' -Message 'Tracked Git changes were unchanged by the run.'
        }
    }
    else {
        $steps += New-HarnessStep -Name 'tracked-file-mutation-guard' -Status 'not_available' -Message 'Could not compare tracked Git state.'
    }

    $statuses = @($steps | ForEach-Object { [string]$_['status'] })
    if ($trackedChangedDuringRun -or $statuses -contains 'failed') {
        $finalStatus = 'failed'
        $exitCode = 1
    }
    elseif ($statuses -contains 'blocked') {
        $finalStatus = 'blocked'
        $exitCode = 3
    }
    elseif ($statuses -contains 'not_available') {
        $finalStatus = 'not_available'
        $exitCode = 3
    }
    else {
        $finalStatus = 'passed'
        $exitCode = 0
    }
}
catch {
    $warnings += $_.Exception.Message
    if ($exitCode -ne 1) {
        $finalStatus = 'failed'
        $exitCode = 2
    }
}
finally {
    if ($artifacts -notcontains $summaryPath) {
        $artifacts += $summaryPath
    }
    $summary = Write-Summary
    if ($Json) {
        [Console]::Out.WriteLine((ConvertTo-Json -InputObject ([ordered]@{ summaryPath = $summaryPath; status = $summary['status']; exitCode = $summary['exitCode']; resolvedTier = $summary['resolvedTier'] }) -Compress))
    }
    else {
        Write-Host ("Unity preflight {0}: {1} (tier {2}, exit {3})" -f $runId, $summary['status'], $summary['resolvedTier'], $summary['exitCode'])
        Write-Host "Summary: $summaryPath"
        $displayReasons = @($summary['selectionReasons'] | Select-Object -First 8)
        foreach ($reason in $displayReasons) {
            Write-Host "  - $reason"
        }
        $remainingReasonCount = @($summary['selectionReasons']).Count - $displayReasons.Count
        if ($remainingReasonCount -gt 0) {
            Write-Host "  - ... and $remainingReasonCount more reason(s) in summary.json"
        }
    }
}

exit $exitCode
