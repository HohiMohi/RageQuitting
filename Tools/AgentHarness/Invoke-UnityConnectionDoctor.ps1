[CmdletBinding()]
param([string]$ProjectRoot,[switch]$Json)
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'UnityConnection.psm1') -Force
if([string]::IsNullOrWhiteSpace($ProjectRoot)){$scriptDirectory=Split-Path -Parent $MyInvocation.MyCommand.Path;$ProjectRoot=Join-Path $scriptDirectory '..\..'}
$root=try{[IO.Path]::GetFullPath($ProjectRoot)}catch{[string]$ProjectRoot}
$report=[ordered]@{schemaVersion=1;status='unknown';exitCode=3;nextAction='Inspect the diagnostics; no repair was attempted.';projectRoot=$root;unityCli=$null;descriptorPath=(Get-UnityDescriptorPath $root);diagnostics=[ordered]@{};evidence=@()}
$out=[IO.Path]::GetTempFileName();$err=[IO.Path]::GetTempFileName();$ctxOut=$null;$ctxErr=$null
try {
 if(-not (Test-Path -LiteralPath (Join-Path $root 'Assets') -PathType Container) -or -not (Test-Path -LiteralPath (Join-Path $root 'Packages\manifest.json') -PathType Leaf) -or -not (Test-Path -LiteralPath (Join-Path $root 'ProjectSettings\ProjectVersion.txt') -PathType Leaf)){$report.status='invalid_project';$report.diagnostics.error='ProjectRoot is missing required Unity project markers.';throw 'report-complete'}
 try{$cli=Resolve-UnityCliPath}catch{$report.status='invalid_cli_override';$report.diagnostics.error=$_.Exception.Message;throw 'report-complete'}
 $report.unityCli=$cli
 try{$guard=Test-UnityConnectionGuard -ProjectRoot $root}catch{$guard=[ordered]@{allowed=$false;status='restricted_context';reason='Connection guard could not inspect descriptor/process access.'}}
 $report.diagnostics.guard=$guard.status
 if(-not $guard.allowed){$classification=Get-UnityDoctorClassification -ProjectRoot $root -Guard $guard}
 elseif(-not(Test-Path -LiteralPath $cli -PathType Leaf)){$classification=[ordered]@{status='missing_cli';exitCode=3;reason="Unity CLI not found at '$cli'."}}
 else {
  $call=Invoke-UnityConnectionCli -FilePath $cli -Arguments @('status','--project-path',$root,'--format','json','--no-banner') -StdOutPath $out -StdErrPath $err -WorkingDirectory $root
  if(-not $call.available){$classification=[ordered]@{status='restricted_context';exitCode=3;reason=$call.error}}
  else {
   $envp=$null;$parseError=$null
   try{$envp=Get-Content -LiteralPath $out -Raw|ConvertFrom-Json -ErrorAction Stop}catch{$parseError=$_.Exception.Message}
   if($parseError){$classification=Get-UnityDoctorClassification -ProjectRoot $root -CliExitCode $call.exitCode -Envelope $null}
   elseif($null -eq $envp -or -not $envp.PSObject.Properties['success']){$classification=[ordered]@{status='malformed_cli_schema';exitCode=3;reason='Unity CLI status envelope is missing success.'}}
   elseif($envp.success -and $call.exitCode -eq 0){
    $ctx=$null;$ctxCall=$null;$ctxOut=[IO.Path]::GetTempFileName();$ctxErr=[IO.Path]::GetTempFileName()
    try{$ctxCall=Invoke-UnityConnectionCli -FilePath $cli -Arguments @('command','rq_validation_context','--timeout','5','--project-path',$root,'--format','json','--no-banner') -StdOutPath $ctxOut -StdErrPath $ctxErr -WorkingDirectory $root
     if($ctxCall.available -and $ctxCall.exitCode -eq 0){$ce=Get-Content -LiteralPath $ctxOut -Raw|ConvertFrom-Json -ErrorAction Stop;if($ce.success -and $ce.data.result){$ctx=$ce.data.result}}
    }catch{$ctx=$null}
    $classification=Get-UnityDoctorClassification -ProjectRoot $root -CliExitCode $call.exitCode -Envelope $envp -Context $ctx
    if($ctx){$report.diagnostics.editorContext=$ctx}
   }
   else {
    $compileEvidence=$false;$safeEvidence=$false
    $log=Join-Path $root 'Logs\Editor.log'
    if(Test-Path -LiteralPath $log -PathType Leaf){try{$lines=@(Get-Content -LiteralPath $log -Tail 300|Where-Object{$_ -match '(?i)error CS\d{4}|Scripts have compiler errors|Entering Safe Mode'});$compileEvidence=$lines.Count -gt 0;$safeEvidence=@($lines|Where-Object{$_ -match '(?i)Entering Safe Mode'}).Count -gt 0;if($compileEvidence){$report.diagnostics.compileEvidence=@($lines|Select-Object -Last 12);$report.diagnostics.compileEvidenceProvenance='Project Editor.log lines are evidence only; current-session provenance was not established.'}}catch{}}
    $descriptorExists=Test-Path -LiteralPath $report.descriptorPath -PathType Leaf
    $visible=$false
    if(-not $descriptorExists){try{$visible=@(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction Stop|Where-Object{$_.CommandLine -and (Get-UnityProjectFromCommandLine ([string]$_.CommandLine)) -and [string]::Equals((Get-UnityProjectFromCommandLine ([string]$_.CommandLine)),$root.TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)}).Count -gt 0}catch{}}
    $classification=Get-UnityDoctorClassification -ProjectRoot $root -CliExitCode $call.exitCode -Envelope $envp -DescriptorExists $descriptorExists -ProjectEditorVisible $visible -CompileEvidence $compileEvidence -SafeModeEvidence $safeEvidence
    if($safeEvidence){$report.diagnostics.safeModeLogEvidence='Safe Mode text appears in project Editor.log; this does not establish current-session state.'}
   }
  }
 }
 if($classification.reason){$report.diagnostics.reason=$classification.reason}
 $report.status=$classification.status;$report.exitCode=$classification.exitCode
} catch {
 if($_.Exception.Message -ne 'report-complete'){$report.status='unknown';$report.exitCode=3;$report.diagnostics.error=$_.Exception.Message}
} finally {$temporaryPaths=@($out,$err,$ctxOut,$ctxErr)|Where-Object{$_};if($temporaryPaths.Count){Remove-Item -LiteralPath $temporaryPaths -Force -ErrorAction SilentlyContinue}}
$next=@{invalid_project='Pass the live Unity project root containing Assets, Packages/manifest.json, and ProjectSettings/ProjectVersion.txt.';editor_identity_unverified='Verify the visible Unity Editor process and project before running Unity CLI.';editor_running_pipeline_reachable='No recovery is needed.';busy_play='Wait until Play mode ends before running validation.';busy_compile='Wait for compilation to finish before running validation.';busy_update='Wait until the Editor update/import completes before running validation.';known_compile_error='Fix confirmed current-session C# compile errors.';safe_mode_evidence_unverified='Review the Safe Mode log evidence and confirm it belongs to this Editor session.';compile_error_evidence_unverified='Review the compile log evidence and confirm it belongs to this Editor session.';no_editor='Open this project in Unity Editor, then run the doctor again.';editor_running_pipeline_unreachable='Use the explicit Tools > Agent Harness > Repair Unity CLI Connection command in the Editor.';missing_descriptor_live_editor='Use the explicit Editor repair command to republish the live descriptor.';wrong_project='Run the command from the project named by the descriptor.';pid_project_mismatch='Investigate descriptor and process identity before proceeding.';pid_not_visible='Run PowerShell with visibility to the Editor process, then retry.';restricted_process='Run in a context that can inspect the Unity Editor process.';descriptor_unreadable='Resolve descriptor file access before invoking Unity CLI.';missing_cli='Install or point RQ_UNITY_CLI_PATH at unity.exe.';invalid_cli_override='Set RQ_UNITY_CLI_PATH to an absolute path whose filename is unity.exe.';malformed_cli_json='Inspect the CLI status output; no Editor operation was attempted.';malformed_cli_schema='Inspect the CLI status envelope schema.';unknown='Inspect the supplied diagnostics; status could not be established.'}
if($next.ContainsKey([string]$report.status)){$report.nextAction=$next[[string]$report.status]}
$jsonText=ConvertTo-Json -InputObject $report -Depth 12
if($Json){[Console]::Out.WriteLine($jsonText)}else{Write-Output $jsonText}
exit ([int]$report.exitCode)
