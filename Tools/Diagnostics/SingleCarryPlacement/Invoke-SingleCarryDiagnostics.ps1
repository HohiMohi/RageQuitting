<#
.SYNOPSIS
Runs the fixed Single Carry diagnostics test suite with crash-recoverable define/environment changes.
.DESCRIPTION
Collect runs the two natural movement cases for each requested pair (default one pair). Verify runs
the fixed nine-case PlayMode fixture and requires six diagnostic timelines. Restore consumes a
recovery.json manifest. Tests are never automatically retried.

Changing PlayerSettings is persisted with UnityEditor.AssetDatabase.SaveAssets(). This saves every
dirty asset in the Editor. Pass -SaveDirtyAssets to acknowledge that side effect when dirty assets
exist, and save your work before capture. Dirty/unsaved scenes are refused and are never saved by
this script. An interrupted process can leave a pending recovery.json; run Restore explicitly.
#>
[CmdletBinding()]
param(
    [ValidateSet('Collect','Verify','Restore')][string]$Mode = 'Collect',
    [ValidateRange(1,20)][int]$Pairs = 1,
    [ValidateRange(30,900)][int]$TimeoutSeconds = 180,
    [string]$OutputRoot,
    [string]$RecoveryManifest,
    [string]$TestRunnerPath,
    [switch]$SaveDirtyAssets,
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$harnessRoot = Join-Path $projectRoot 'Tools\AgentHarness'
Import-Module (Join-Path $harnessRoot 'AgentHarness.psm1') -Force
Import-Module (Join-Path $harnessRoot 'UnityConnection.psm1') -Force
$cli = Resolve-UnityCliPath
$symbol = 'RQ_SINGLE_CARRY_DIAGNOSTICS'
$settingsPath = Join-Path $projectRoot 'ProjectSettings\ProjectSettings.asset'
$started = [DateTime]::UtcNow
if (-not $OutputRoot) { $OutputRoot = Join-Path $projectRoot 'Artifacts\SingleCarryDiagnostics' }
if (-not [IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot = Join-Path $projectRoot $OutputRoot }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (-not $TestRunnerPath) { $TestRunnerPath = Join-Path $harnessRoot 'Invoke-UnityPlayModeTests.ps1' }
elseif (-not [IO.Path]::IsPathRooted($TestRunnerPath)) { $TestRunnerPath = Join-Path $projectRoot $TestRunnerPath }
$lockPath = Join-Path $projectRoot 'Library\SingleCarryDiagnostics.capture.lock'
$lockStream = $null
$runnerExitCode = 1
$finalStatus = 'failed'
$manifest = $null
$manifestPath = $null
$mutationStarted = $false
$errors = [Collections.Generic.List[string]]::new()
$steps = [Collections.Generic.List[object]]::new()
$pairResults = [Collections.Generic.List[object]]::new()
$allTraces = [Collections.Generic.List[object]]::new()
$runningPair = $null

function Write-Result([string]$Status,[int]$Code) {
    $result = [ordered]@{
        schemaVersion = 1; mode = $Mode; status = $Status; exitCode = $Code
        startedAtUtc = $started.ToString('o'); completedAtUtc = [DateTime]::UtcNow.ToString('o')
        projectRoot = $projectRoot; editorPid = if ($manifest) { $manifest.editorPid } else { $null }
        target = if ($manifest) { $manifest.target } else { $null }; initialDefines = if ($manifest) { $manifest.initialDefines } else { $null }
        initialTraceDirectory = if ($manifest) { $manifest.initialTraceDirectory } else { $null }
        recoveryManifest = $manifestPath; steps = @($steps); errors = @($errors)
    }
    if ($manifestPath -and $manifest) {
        if ($Status -eq 'failed') { $manifest.runStatus = 'failed' }
        else { $manifest.runStatus = $Status }
        $manifest.lastUpdatedAtUtc = [DateTime]::UtcNow.ToString('o')
        $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    }
    if ($manifestPath) { $result | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $manifestPath) 'result.json') -Encoding UTF8 }
    if ($Json) { $result | ConvertTo-Json -Compress -Depth 16 | Write-Output }
    else { Write-Output (ConvertTo-Json -Depth 16 $result) }
    $script:runnerExitCode = $Code
}

function Invoke-UnityJson([string]$Label,[string[]]$Arguments) {
    $dir = if ($manifestPath) { Split-Path -Parent $manifestPath } else { $OutputRoot }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $prefix = '{0:D2}-{1}' -f ($steps.Count + 1), $Label
    $out = Join-Path $dir ($prefix + '.stdout.json')
    $err = Join-Path $dir ($prefix + '.stderr.txt')
    $call = Invoke-UnityConnectionCli -FilePath $cli -Arguments $Arguments -StdOutPath $out -StdErrPath $err -WorkingDirectory $projectRoot
    $obj = $null; $parseError = $null
    try { $obj = Get-Content -LiteralPath $out -Raw | ConvertFrom-Json -ErrorAction Stop }
    catch { $parseError = $_.Exception.Message }
    $steps.Add([ordered]@{ name=$Label; exitCode=$call.exitCode; available=$call.available; stdout=$out; stderr=$err; parseError=$parseError })
    if (-not $call.available -or $call.exitCode -ne 0 -or $parseError -or -not $obj -or -not $obj.success) {
        throw "Unity command '$Label' failed or returned invalid JSON. See $out and $err"
    }
    return $obj
}

function Assert-WindowsMonoPathBudget([string]$Directory) {
    # Unity's Windows Mono filesystem calls may still hit MAX_PATH even when the
    # Windows host accepts extended paths. Keep generated artifact leaves bounded.
    $maxGeneratedLeafLength = 64
    if ($Directory.Length + 1 + $maxGeneratedLeafLength -gt 259) {
        throw "Output directory leaves insufficient Windows Mono MAX_PATH budget (directory length $($Directory.Length), required leaf budget $maxGeneratedLeafLength): $Directory"
    }
}

function Get-ReadyEditorPid([string]$Label) {
    $status=Invoke-UnityJson $Label @('status','--project-path',$projectRoot,'--format','json','--no-banner')
    $items=if($status.data.instances){@($status.data.instances)}else{@($status.data)}
    foreach($item in $items){
        $path=''
        if($item.PSObject.Properties['projectPath'] -and $item.projectPath){$path=[string]$item.projectPath}
        elseif($item.PSObject.Properties['project'] -and $item.project){$path=[string]$item.project}
        if($item.PSObject.Properties['state'] -and $item.PSObject.Properties['pid'] -and $item.state -eq 'ready' -and $item.pid -and $path -and [IO.Path]::GetFullPath($path) -eq $projectRoot){return [int]$item.pid}
    }
    return $null
}

function Write-PartialIndex([string]$Status) {
    if (-not $manifest -or -not $manifest.runRoot) { return }
    $indexPath=Join-Path $manifest.runRoot 'run-index.json'
    $plannedScenarios = if ($Mode -eq 'Verify') { @('host-sync','natural-0ms','natural-100ms','stationary-100ms','missing-pose','residual-control') } else { @('remote-owner-movement-0ms','remote-owner-movement-100ms') }
    $selectedScenarioKinds = if ($Mode -eq 'Verify') { @('fixed-nine-case-fixture') } else { @('RemoteOwnerMovesAndConfirmsWith') }
    [ordered]@{schemaVersion=1;mode=$Mode;runId=$manifest.runId;updatedAtUtc=[DateTime]::UtcNow.ToString('o');status=$Status;requestedPairs=if($Mode -eq 'Verify'){1}else{$Pairs};pairs=@($pairResults);traces=@($allTraces);plannedScenarios=$plannedScenarios;selectedScenarioKinds=$selectedScenarioKinds;notes=@('Natural movement traces carry packetDelayMs metadata, which associates each run with the 0ms or 100ms configured transport-simulator case. Response result and functional test status remain separate in responseOutcomes.')} | ConvertTo-Json -Depth 24 | Set-Content -LiteralPath $indexPath -Encoding UTF8
}

function Invoke-Eval([string]$Label,[string]$Code) {
    $file = Join-Path (Split-Path -Parent $manifestPath) ($Label + '.cs')
    Set-Content -LiteralPath $file -Value $Code -Encoding UTF8
    $obj = Invoke-UnityJson $Label @('command','eval_file','--file',$file,'--timeout','30','--project-path',$projectRoot,'--format','json','--no-banner')
    $wrapper = $obj.data.result
    if ($wrapper -is [string]) { $wrapper = $wrapper | ConvertFrom-Json -ErrorAction Stop }
    if ($wrapper.PSObject.Properties['success'] -and -not $wrapper.success) { throw "Editor evaluation '$Label' failed: $($wrapper.error)" }
    if (-not $wrapper.PSObject.Properties['result']) { throw "Editor evaluation '$Label' omitted its result." }
    return $wrapper.result
}

function Assert-NoEditorErrors([string]$Label) {
    $obj=Invoke-UnityJson $Label @('command','get_console_logs','--severity','error','--limit','1000','--project-path',$projectRoot,'--format','json','--no-banner')
    $result=$obj.data.result
    if ($result -is [string]) { $result=$result | ConvertFrom-Json -ErrorAction Stop }
    if ($result.total -gt 0 -or @($result.logs).Count -gt 0) {
        throw "Unity Console reports $($result.total) error entries at $Label; details are in the saved CLI response."
    }
    return $result
}

function ConvertTo-CSharpLiteral([AllowNull()][string]$Value) {
    if ($null -eq $Value) { return 'null' }
    return ConvertTo-Json -InputObject $Value -Compress
}

function Wait-Editor([bool]$WantDiagnostics,[int]$Seconds = 150) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    $last = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $observedPid=Get-ReadyEditorPid 'editor-pid-poll'
            if (-not $observedPid) { throw 'No ready Editor currently matches the guarded project.' }
            if ($manifest -and $manifest.editorPid -ne $observedPid) {
                if (-not $manifest.editorPidChanges) { $manifest.editorPidChanges=@() }
                $manifest.editorPidChanges += [ordered]@{from=$manifest.editorPid;to=$observedPid;observedAtUtc=[DateTime]::UtcNow.ToString('o')}
                $manifest.editorPid=$observedPid
                $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
            }
            $state = Invoke-Eval 'editor-settings-poll' @"
var group = UnityEditor.BuildPipeline.GetBuildTargetGroup(UnityEditor.EditorUserBuildSettings.activeBuildTarget);
var target = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group);
var defs = UnityEditor.PlayerSettings.GetScriptingDefineSymbols(target);
var on = System.Array.Exists(defs.Split(';'), x => x == "$symbol");
System.Reflection.Assembly asm = null;
foreach (var candidate in System.AppDomain.CurrentDomain.GetAssemblies())
    if (candidate.GetName().Name == "Assembly-CSharp") { asm = candidate; break; }
var recorder = asm != null && asm.GetType("SingleCarryStateDivergenceTrace") != null;
return new { defines = defs, diagnostics = on, recorder = recorder, targetName = target.TargetName, targetGroup = group.ToString(),
 compiling = UnityEditor.EditorApplication.isCompiling, updating = UnityEditor.EditorApplication.isUpdating,
 playing = UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode };
"@
            $last = $state
            if ($manifest -and $state.targetName -ne $manifest.targetName) { throw "Active build target changed from $($manifest.targetName) to $($state.targetName) during the run." }
            $compileCall = Invoke-UnityJson 'recompile-status-poll' @('command','recompile_status','--project-path',$projectRoot,'--format','json','--no-banner')
            $compileState = $compileCall.data.result
            if ($compileState -is [string]) { $compileState = $compileState | ConvertFrom-Json -ErrorAction Stop }
            if (-not $state.compiling -and -not $state.updating -and -not $state.playing -and
                [bool]$state.diagnostics -eq $WantDiagnostics -and [bool]$state.recorder -eq $WantDiagnostics -and
                $compileState.status -eq 'completed' -and -not $compileState.failed -and @($compileState.errors).Count -eq 0) { return $state }
        } catch {
            # Domain reload can briefly sever the authenticated connection. The bounded poll is safe;
            # it only reads state and never repeats a test invocation.
        }
        Start-Sleep -Seconds 2
    }
    throw "Editor did not reach the expected idle diagnostics state ($WantDiagnostics). Last state: $(ConvertTo-Json -Compress $last)"
}

function Get-DiskDiagnosticsDefine([string]$TargetName) {
    $raw = Get-Content -LiteralPath $settingsPath -Raw
    $inside = $false
    foreach ($line in ($raw -split "`r?`n")) {
        if ($line -match '^\s*scriptingDefineSymbols:\s*$') { $inside = $true; continue }
        if ($inside -and $line -match '^\s{0,2}\S') { break }
        if ($inside -and $line -match ('^\s{4}' + [regex]::Escape($TargetName) + ':\s*(.*)$')) {
            return @($Matches[1] -split ';') -contains $symbol
        }
    }
    return $null
}

function Restore-Settings {
    if (-not $manifest) { return }
    $restoreCode = @"
var dirtyCount = 0;
foreach (var obj in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Object>()) if (UnityEditor.EditorUtility.IsDirty(obj)) dirtyCount++;
if (dirtyCount > 0 && $($SaveDirtyAssets.ToString().ToLowerInvariant()) == false)
    throw new System.InvalidOperationException("Editor has dirty assets. Re-run with -SaveDirtyAssets to acknowledge AssetDatabase.SaveAssets() saves them.");
var group = UnityEditor.BuildTargetGroup.$($manifest.targetGroup);
var target = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group);
var current = UnityEditor.PlayerSettings.GetScriptingDefineSymbols(target);
var parts = new System.Collections.Generic.List<string>(current.Split(new[]{';'}, System.StringSplitOptions.RemoveEmptyEntries));
string initial = $(ConvertTo-CSharpLiteral ([string]$manifest.initialDefines));
var introduced = $($manifest.introducedDiagnostics.ToString().ToLowerInvariant());
if (introduced) parts.RemoveAll(x => x == "$symbol");
else if (System.Array.IndexOf(initial.Split(';'), "$symbol") >= 0 && !parts.Contains("$symbol")) parts.Add("$symbol");
var restored = String.Join(";", parts);
UnityEditor.PlayerSettings.SetScriptingDefineSymbols(target, restored);
string oldEnv = $(ConvertTo-CSharpLiteral $manifest.initialTraceDirectory);
string[] ownedEnvValues = new string[] { $(@($manifest.ownedTraceDirectories | ForEach-Object { ConvertTo-CSharpLiteral ([string]$_) }) -join ',') };
string currentEnv = System.Environment.GetEnvironmentVariable("SINGLE_CARRY_TRACE_DIRECTORY", System.EnvironmentVariableTarget.Process);
if (System.Array.IndexOf(ownedEnvValues, currentEnv) >= 0)
    System.Environment.SetEnvironmentVariable("SINGLE_CARRY_TRACE_DIRECTORY", oldEnv, System.EnvironmentVariableTarget.Process);
UnityEditor.AssetDatabase.SaveAssets();
return new { defines = UnityEditor.PlayerSettings.GetScriptingDefineSymbols(target),
 traceDirectory = System.Environment.GetEnvironmentVariable("SINGLE_CARRY_TRACE_DIRECTORY", System.EnvironmentVariableTarget.Process) };
"@
    $state = Invoke-Eval 'restore-settings' $restoreCode
    $WantDiagnostics = $manifest.initialDefines.Split(';') -contains $symbol
    $null = Wait-Editor $WantDiagnostics
    $disk = Get-DiskDiagnosticsDefine ([string]$manifest.targetName)
    $steps.Add([ordered]@{name='disk-settings-check';diagnosticsDefinePresent=$disk;memoryDefines=$state.defines})
    if ($disk -ne $WantDiagnostics) { throw "Serialized ProjectSettings still disagrees with the intended diagnostics symbol state: expected $WantDiagnostics, found $disk." }
    $envRestored = $state.traceDirectory -ceq $manifest.initialTraceDirectory
    $steps.Add([ordered]@{name='editor-environment-check';restored=$envRestored;current=$state.traceDirectory;expected=$manifest.initialTraceDirectory})
    if (-not $envRestored -and $state.traceDirectory -cne $manifest.ownedTraceDirectory) {
        $errors.Add('Editor trace-directory environment changed independently; preserved the newer value during cleanup.')
    }
    $null=Assert-NoEditorErrors 'console-errors-after-restore'
}

function Validate-RunTraces([string]$Dir,[string]$ExpectedCase,[int]$ExpectedCount) {
    $files = @(Get-ChildItem -LiteralPath $Dir -Filter '*.json' -File -Recurse | Where-Object { $_.Name -notmatch 'report|index|summary|metadata' })
    $traces = @()
    foreach ($f in $files) {
        try { $d = Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json -ErrorAction Stop }
        catch { continue }
        if ($d.PSObject.Properties['records'] -and $d.PSObject.Properties['runId']) { $traces += ,([ordered]@{ path=$f.FullName; data=$d }) }
    }
    if ($traces.Count -ne $ExpectedCount) { throw "Expected $ExpectedCount trace(s) under $Dir; found $($traces.Count)." }
    foreach ($trace in $traces) {
        $data = $trace.data
        if (-not $data.runId -or $data.overflow -or @($data.records | Where-Object { $_.overflow }).Count -gt 0) { throw "Trace is missing runId or has overflow: $($trace.path)" }
        $meta = $null
        try { $meta = [string]$data.metadataJson | ConvertFrom-Json -ErrorAction Stop } catch { }
        if (-not $meta) { throw "Trace metadata is malformed or missing: $($trace.path)" }
        if ($ExpectedCase -and $meta.caseName -ne $ExpectedCase) { throw "Unexpected trace case '$($meta.caseName)' in $($trace.path)." }
        if (@($data.records | Where-Object { $_.eventKind -eq 10 }).Count -lt 1 -or @($data.records | Where-Object { $_.eventKind -eq 11 }).Count -lt 1) { throw "Trace lacks session begin/freeze evidence: $($trace.path)" }
    }
    return $traces
}

function Get-ObservedTraceFiles([string]$Dir) {
    $observed=@()
    foreach($file in Get-ChildItem -LiteralPath $Dir -Filter '*.json' -File -Recurse){
        try{$data=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json -ErrorAction Stop}catch{continue}
        if($data.PSObject.Properties['runId'] -and $data.PSObject.Properties['records']){
            $observed += [ordered]@{runId=[string]$data.runId;path=$file.FullName;overflow=[bool]$data.overflow}
        }
    }
    return $observed
}

try {
    if ($Mode -eq 'Restore') {
        if (-not $RecoveryManifest) { throw 'Restore requires -RecoveryManifest <path>.' }
        $manifestPath = [IO.Path]::GetFullPath($RecoveryManifest)
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ([IO.Path]::GetFullPath($manifest.projectRoot) -ne $projectRoot) { throw 'Recovery manifest projectRoot does not match this script project.' }
        if (-not $SaveDirtyAssets) { throw 'Restore writes PlayerSettings through AssetDatabase.SaveAssets(); pass -SaveDirtyAssets to acknowledge that all dirty assets are saved.' }
        New-Item -ItemType Directory -Path (Split-Path -Parent $lockPath) -Force | Out-Null
        $lockStream = [IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        $lockStream.SetLength(0)
        $writer = [IO.StreamWriter]::new($lockStream,[Text.Encoding]::UTF8,1024,$true)
        $writer.Write("pid=$PID mode=Restore started=$($started.ToString('o'))")
        $writer.Flush()
    } else {
        if ($Mode -eq 'Verify' -and $Pairs -ne 1) { throw 'Verify always runs the fixed nine-case fixture exactly once.' }
        if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) { throw "Unity CLI not found: $cli" }
        if (-not (Test-Path -LiteralPath $TestRunnerPath -PathType Leaf)) { throw "Test runner not found: $TestRunnerPath" }
        $runId = '{0}-{1}' -f $started.ToString('yyyyMMddTHHmmssZ'),[Guid]::NewGuid().ToString('N')
        $runRoot = Join-Path $OutputRoot $runId
        Assert-WindowsMonoPathBudget (Join-Path $runRoot 'Traces')
        $plannedPairs = if ($Mode -eq 'Verify') { 1 } else { $Pairs }
        for ($plannedPair = 1; $plannedPair -le $plannedPairs; $plannedPair++) {
            Assert-WindowsMonoPathBudget (Join-Path (Join-Path $runRoot ('Pair-{0:D2}' -f $plannedPair)) 'Traces')
        }
        New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
        $manifestPath = Join-Path $runRoot 'recovery.json'
        $manifest = [ordered]@{ schemaVersion=1; runId=$runId; runStatus='prepared'; restorationStatus='not-started'; settingsCaptured=$false; mutationAttempted=$false; projectRoot=$projectRoot; editorPid=$null; target=$null; targetGroup=$null; targetName=$null; initialDefines=$null; introducedDiagnostics=$null; initialTraceDirectory=$null; ownedTraceDirectory=$null; ownedTraceDirectories=@(); saveAssetsInvoked=$false; runRoot=$runRoot; createdAtUtc=$started.ToString('o'); lastUpdatedAtUtc=$started.ToString('o') }
        $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
        New-Item -ItemType Directory -Path (Split-Path -Parent $lockPath) -Force | Out-Null
        try { $lockStream = [IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None) }
        catch { throw 'Another Single Carry diagnostics capture owns the project lock.' }
        $lockStream.SetLength(0)
        $writer = [IO.StreamWriter]::new($lockStream,[Text.Encoding]::UTF8,1024,$true)
        $writer.Write("pid=$PID mode=$Mode started=$($started.ToString('o'))")
        $writer.Flush()
        $ctx = Invoke-UnityJson 'validation-context' @('command','rq_validation_context','--project-path',$projectRoot,'--format','json','--no-banner')
        $context = $ctx.data.result
        if ($context -is [string]) { $context = $context | ConvertFrom-Json }
        if ($context.projectPath -ne $projectRoot -or $context.isCompiling -or $context.isUpdating -or $context.isPlaying -or $context.isPlayingOrWillChangePlaymode -or $context.playModeState -ne 'Editing') { throw 'Editor project identity or idle-state precondition failed.' }
        if (@($context.openScenes | Where-Object { $_.isDirty -or -not $_.path }).Count -gt 0) { throw 'Open scenes are dirty or unsaved; refusing to save or discard them.' }
        $testStatus=Invoke-UnityJson 'test-status-before-enable' @('command','test_status','--project-path',$projectRoot,'--format','json','--no-banner')
        $testState=$testStatus.data.result
        if($testState -is [string]){$testState=$testState|ConvertFrom-Json -ErrorAction Stop}
        if($testState.status -eq 'running'){throw 'Another Unity test run is already active; refusing to change diagnostics settings.'}
        $null=Assert-NoEditorErrors 'console-errors-before-enable'
        $editorPid = Get-ReadyEditorPid 'editor-status'
        if (-not $editorPid) { throw 'Could not identify a ready Editor PID for the project.' }
        $manifest.editorPid=$editorPid
        $snapshot = Invoke-Eval 'initial-settings' @"
var group = UnityEditor.BuildPipeline.GetBuildTargetGroup(UnityEditor.EditorUserBuildSettings.activeBuildTarget);
var target = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group);
var dirty = 0;
foreach (var obj in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Object>()) if (UnityEditor.EditorUtility.IsDirty(obj)) dirty++;
return new { defines = UnityEditor.PlayerSettings.GetScriptingDefineSymbols(target), env = System.Environment.GetEnvironmentVariable("SINGLE_CARRY_TRACE_DIRECTORY", System.EnvironmentVariableTarget.Process), target = target.TargetName, targetGroup = group.ToString(), dirtyAssetObjectCount = dirty };
"@
        $manifest.initialDefines = [string]$snapshot.defines
        $manifest.settingsCaptured = $true
        $manifest.initialTraceDirectory = $snapshot.env
        $manifest.targetName = [string]$snapshot.target
        $manifest.targetGroup = [string]$snapshot.targetGroup
        $manifest.target = [string]$snapshot.target
        $manifest.introducedDiagnostics = -not (@($snapshot.defines.Split(';')) -contains $symbol)
        $manifest.dirtyAssetObjectCountBeforeSave = [int]$snapshot.dirtyAssetObjectCount
        $manifest.saveAssetsInvoked = $false
        $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
        if ($snapshot.dirtyAssetObjectCount -gt 0 -and -not $SaveDirtyAssets) {
            throw "Editor has $($snapshot.dirtyAssetObjectCount) dirty assets. Save your work first or pass -SaveDirtyAssets to acknowledge that Unity AssetDatabase.SaveAssets() saves all dirty assets."
        }
        $desired = @($snapshot.defines.Split(';') | Where-Object { $_ })
        if ($desired -notcontains $symbol) { $desired += $symbol }
        $traceDir = Join-Path $runRoot 'Traces'
        New-Item -ItemType Directory -Path $traceDir -Force | Out-Null
        $manifest.ownedTraceDirectory = $traceDir
        $manifest.ownedTraceDirectories=@($traceDir)
        $manifest.runStatus = 'enabling'
        $manifest.mutationAttempted = $true
        $manifest.restorationStatus='pending'
        $manifest.saveAssetsInvoked = $true
        $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
        $mutationStarted = $true
        $setCode = @"
var group = UnityEditor.BuildTargetGroup.$($manifest.targetGroup);
var target = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group);
UnityEditor.PlayerSettings.SetScriptingDefineSymbols(target, $(ConvertTo-CSharpLiteral ($desired -join ';')));
System.Environment.SetEnvironmentVariable("SINGLE_CARRY_TRACE_DIRECTORY", $(ConvertTo-CSharpLiteral $traceDir), System.EnvironmentVariableTarget.Process);
UnityEditor.AssetDatabase.SaveAssets();
return new { defines = UnityEditor.PlayerSettings.GetScriptingDefineSymbols(target), traceDirectory = System.Environment.GetEnvironmentVariable("SINGLE_CARRY_TRACE_DIRECTORY", System.EnvironmentVariableTarget.Process) };
"@
        $null = Invoke-Eval 'enable-diagnostics' $setCode
        $state = Wait-Editor $true
        $null=Assert-NoEditorErrors 'console-errors-after-enable'
        $diskOn = Get-DiskDiagnosticsDefine ([string]$manifest.targetName)
        if ($diskOn -ne $true) { throw 'Diagnostics define did not persist to ProjectSettings on disk after enable/compile; refusing to start tests.' }
        $manifest.runStatus = 'enabled'
        $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    }

    if ($Mode -eq 'Restore') {
        Restore-Settings
        $manifest.restorationStatus='restored'
        $finalStatus = 'restored'
        $runnerExitCode = 0
    }
    else {
        $count = if ($Mode -eq 'Verify') { 1 } else { $Pairs }
        for ($p = 1; $p -le $count; $p++) {
            $pairDir = Join-Path $manifest.runRoot ('Pair-{0:D2}' -f $p)
            New-Item -ItemType Directory -Path $pairDir -Force | Out-Null
            $filter = if ($Mode -eq 'Verify') { 'SingleCarryPlacementMovementNetworkPlayModeTests' } else { 'RemoteOwnerMovesAndConfirmsWith' }
            $pairTraceDir = Join-Path $pairDir 'Traces'
            New-Item -ItemType Directory -Path (Join-Path $pairDir 'Traces') -Force | Out-Null
            $runningPair=[ordered]@{pair=$p;status='running';filter=$filter;startedAtUtc=[DateTime]::UtcNow.ToString('o');runnerExitCode=$null;testSummary=$null;testCases=@();traceCount=0;traceRuns=@();functionalAssertionFailures=@()}
            $pairResults.Add($runningPair)
            Write-PartialIndex 'running'
            $manifest.ownedTraceDirectory=$pairTraceDir
            $manifest.ownedTraceDirectories=@($manifest.ownedTraceDirectories)+@($pairTraceDir)
            $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
            $traceLiteral = ConvertTo-CSharpLiteral $pairTraceDir
            $null = Invoke-Eval ('set-trace-directory-{0:D2}' -f $p) "System.Environment.SetEnvironmentVariable(`"SINGLE_CARRY_TRACE_DIRECTORY`", $traceLiteral, System.EnvironmentVariableTarget.Process); return true;"
            $args = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$TestRunnerPath,'-ArtifactDirectory',(Join-Path $pairDir 'Harness'),'-Filter',$filter,'-TimeoutSeconds',[string]$TimeoutSeconds,'-Json')
            & pwsh @args 1> (Join-Path $pairDir 'runner.stdout.txt') 2> (Join-Path $pairDir 'runner.stderr.txt')
            $testExit = $LASTEXITCODE
            $runnerOutput = Get-Content (Join-Path $pairDir 'runner.stdout.txt') -Raw
            $runnerJson = $null
            try { $runnerJson = $runnerOutput | ConvertFrom-Json -ErrorAction Stop } catch { }
            $harnessReport = Join-Path $pairDir 'Harness\unity-playmode-tests-report.json'
            $testReport = if (Test-Path $harnessReport) { Get-Content $harnessReport -Raw | ConvertFrom-Json } else { $null }
            $observedTraces=@(Get-ObservedTraceFiles (Join-Path $pairDir 'Traces'))
            $runningPair.observedTimelineFiles=$observedTraces
            $runningPair.traceCount=$observedTraces.Count
            foreach($observed in $observedTraces){$allTraces.Add($observed)}
            Write-PartialIndex 'running'
            if (-not $testReport) { throw "Test runner exited $testExit and produced no harness report; pair $p is infrastructure-invalid. See $(Join-Path $pairDir 'runner.stdout.txt')." }
            if ($Mode -eq 'Verify') { $traces = @(Validate-RunTraces (Join-Path $pairDir 'Traces') $null 6) }
            else { $traces = @(Validate-RunTraces (Join-Path $pairDir 'Traces') 'remote-moving-acceptance' 2) }
            $testCases = @($testReport.tests | ForEach-Object { [string]$_.FullName })
            $pairResult=$runningPair
            $pairResult.runnerExitCode=$testExit
            $pairResult.runnerResult=$runnerJson
            $pairResult.testSummary=$testReport.summary
            $pairResult.testCases=@($testReport.tests)
            $pairResult.traceCount=$traces.Count
            $pairResult.traceRuns=@($traces | ForEach-Object { $_.data.runId })
            $pairResult.functionalAssertionFailures=@($testReport.tests | Where-Object { $_.Status -eq 'Failed' } | ForEach-Object { $_.FullName })
            $pairResult.status=if($testExit -eq 0){'completed'}else{'functional-failure-or-runner-error'}
            if ($Mode -eq 'Verify') {
                if ($testReport.summary.total -ne 9 -or $testReport.summary.passed -ne 9 -or $testExit -ne 0) { throw 'Verify must execute and pass all nine test cases.' }
                $caseNames = @($traces | ForEach-Object { ([string]$_.data.metadataJson | ConvertFrom-Json).caseName })
                $expectedNames = @('host-synchronous-acceptance','remote-moving-acceptance','stationary-physical-panel-acceptance','missing-received-pose-control','residual-exceeded-refusal-control')
                foreach ($name in $expectedNames) { if ($caseNames -notcontains $name) { throw "Verify timeline missing expected case '$name'." } }
                if (@($caseNames | Where-Object { $_ -eq 'remote-moving-acceptance' }).Count -ne 2 -or $caseNames.Count -ne 6) { throw 'Verify timelines must include two natural moving cases and four distinct controls.' }
            } else {
                $expectedTests = @('SingleCarryPlacementMovementNetworkPlayModeTests.RemoteOwnerMovesAndConfirmsWithNoAddedDelay','SingleCarryPlacementMovementNetworkPlayModeTests.RemoteOwnerMovesAndConfirmsWithHundredMillisecondDelay')
                if ($testReport.summary.total -ne 2 -or @($expectedTests | Where-Object { $testCases -notcontains $_ }).Count -gt 0) { throw "Pair $p did not execute exactly the two expected natural movement cases; aborting as infrastructure-invalid." }
                $unexpectedStatuses=@($testReport.tests | Where-Object { $_.Status -notin @('Passed','Failed') })
                if($unexpectedStatuses.Count -gt 0){throw "Pair $p contains skipped or inconclusive natural movement cases; aborting as infrastructure-invalid."}
                if($testExit -ne 0 -and $pairResult.functionalAssertionFailures.Count -eq 0){throw "Pair $p runner failed without a functional assertion failure; treating as infrastructure-invalid."}
                $seenRunIds=@($traces | ForEach-Object { [string]$_.data.runId } | Sort-Object -Unique)
                if ($seenRunIds.Count -ne 2) { throw "Pair $p has duplicate or missing trace run IDs; aborting as invalid data." }
                $outcomes=@()
                $seenDelays=@()
                foreach($trace in $traces){
                    $traceMetadata=[string]$trace.data.metadataJson|ConvertFrom-Json -ErrorAction Stop
                    $delayMs=[int]$traceMetadata.packetDelayMs
                    if($delayMs -notin @(0,100) -or $seenDelays -contains $delayMs){throw "Pair $p must contain exactly one trace at 0ms and one at 100ms; observed packetDelayMs=$delayMs."}
                    $seenDelays += $delayMs
                    $records=@($trace.data.records)
                    $requestIds=@($records | Where-Object { $_.eventKind -eq 2 -and $_.requestId -gt 0 } | ForEach-Object { [string]$_.requestId } | Sort-Object -Unique)
                    if($requestIds.Count -ne 1){throw "Pair $p trace $($trace.data.runId) has ambiguous request IDs."}
                    $rid=$requestIds[0]
                    $response=@($records | Where-Object { $_.eventKind -eq 9 -and [string]$_.requestId -eq $rid })
                    $validation=@($records | Where-Object { $_.eventKind -eq 8 -and [string]$_.requestId -eq $rid })
                    if($response.Count -ne 1 -or $validation.Count -ne 1){throw "Pair $p trace $($trace.data.runId) lacks one unambiguous response and validation."}
                    $testName=if($delayMs -eq 0){$expectedTests[0]}else{$expectedTests[1]}
                    $testStatus=($testReport.tests|Where-Object FullName -eq $testName|Select-Object -First 1).Status
                    $outcomes += [ordered]@{runId=$trace.data.runId;requestId=$rid;packetDelayMs=$delayMs;testName=$testName;testStatus=$testStatus;testToTraceAssociation='packetDelayMs metadata matches the fixed test delay';responseResult=$response[0].result;validationBranch=$validation[0].branch;naturalRefusal=($response[0].result -ne 0);functionalTestFailure=($testStatus -eq 'Failed')}
                }
                if($seenDelays.Count -ne 2 -or @($seenDelays|Sort-Object -Unique).Count -ne 2){throw "Pair $p did not provide one unique trace for each planned delay."}
                $pairResult['responseOutcomes']=$outcomes
                if ($pairResult.functionalAssertionFailures.Count -gt 0) { $errors.Add("Pair $p preserved functional assertion failures: $($pairResult.functionalAssertionFailures -join '; ')") }
            }
            $pairResult.completedAtUtc=[DateTime]::UtcNow.ToString('o')
            $runningPair=$null
            Write-PartialIndex 'running'
        }
        $plannedScenarios = if ($Mode -eq 'Verify') { @('host-sync','natural-0ms','natural-100ms','stationary-100ms','missing-pose','residual-control') } else { @('remote-owner-movement-0ms','remote-owner-movement-100ms') }
        $selectedScenarioKinds = if ($Mode -eq 'Verify') { @('fixed-nine-case-fixture') } else { @('RemoteOwnerMovesAndConfirmsWith') }
        $index = [ordered]@{schemaVersion=1;mode=$Mode;runId=$manifest.runId;createdAtUtc=$started.ToString('o');pairCount=$count;pairs=@($pairResults);traces=@($allTraces);plannedScenarios=$plannedScenarios;selectedScenarioKinds=$selectedScenarioKinds;notes=@('Trace metadata and snapshot JSON are not separate trials. packetDelayMs identifies each natural movement delay. Public hooks do not confirm physical transport sends. Tick clocks remain separate; response result and functional test status are distinct.')}
        $indexPath = Join-Path $manifest.runRoot 'run-index.json'
        $index | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $indexPath -Encoding UTF8
        $manifest.runStatus='tests-completed'
        $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
        $finalStatus = 'passed'
        $runnerExitCode = 0
    }
}
catch {
    $errors.Add($_.Exception.Message)
    if ($runningPair) { $runningPair.status='infrastructure-invalid';$runningPair.error=$_.Exception.Message;$runningPair.completedAtUtc=[DateTime]::UtcNow.ToString('o') }
    Write-PartialIndex 'failed'
    $finalStatus = 'failed'
    $runnerExitCode = 1
}
finally {
    if ($Mode -ne 'Restore' -and $manifest -and $mutationStarted) {
        try {
            Restore-Settings
            $manifest.restorationStatus='restored'
            $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
            Write-PartialIndex $(if($runnerExitCode -ne 0 -or $errors.Count -gt 0){'failed-restored'}else{'passed-restored'})
        } catch {
            $errors.Add("Cleanup/restore failure: $($_.Exception.Message)")
            if ($manifestPath -and $manifest) { $manifest.restorationStatus='restore-failed'; $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8 }
            $script:runnerExitCode = 1
        }
    }
    if ($lockStream) { $lockStream.Dispose(); Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue }
}
if ($errors.Count -gt 0) { $finalStatus='failed'; $runnerExitCode=1 }
Write-Result $finalStatus $runnerExitCode
exit $runnerExitCode
