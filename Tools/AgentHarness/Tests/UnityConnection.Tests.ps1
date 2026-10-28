$ErrorActionPreference='Stop'
$initialHadProject=Test-Path Env:UNITY_PROJECT_PATH;$initialProject=$env:UNITY_PROJECT_PATH
$initialHadLocal=Test-Path Env:LOCALAPPDATA;$initialLocal=$env:LOCALAPPDATA
$module=Join-Path $PSScriptRoot '..\UnityConnection.psm1'
Import-Module $module -Force
$tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$root=Join-Path $tempRoot ('rq-unity-' + [guid]::NewGuid().ToString('N'))
$pipeline=Join-Path $root 'Library\Pipeline'
New-Item -ItemType Directory -Force $pipeline,(Join-Path $root 'Assets'),(Join-Path $root 'Packages'),(Join-Path $root 'ProjectSettings') | Out-Null
'{}'|Set-Content (Join-Path $root 'Packages\manifest.json');'m_EditorVersion: 6000.3.18f1'|Set-Content (Join-Path $root 'ProjectSettings\ProjectVersion.txt')
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
try {
 $realLocal=[Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
 $env:LOCALAPPDATA='Z:\redirected\appdata'
 Assert ((Resolve-UnityCliPath) -eq (Join-Path $realLocal 'Unity\bin\unity.exe')) 'CLI path followed redirected LOCALAPPDATA.'
 $override=Join-Path $root 'unity.exe'; Assert ((Resolve-UnityCliPath $override) -eq $override) 'Explicit absolute override was not honored.'
 $out=Join-Path $root 'out.txt';$err=Join-Path $root 'err.txt';$calls=0
 $hidden=Join-Path $pipeline '.unity-pipeline-port'
 @{pid=2147483000;port=7801;projectPath=$root;unityVersion='6000.3.18f1';mode='editor'}|ConvertTo-Json|Set-Content $hidden
 $call=Invoke-UnityConnectionCli -FilePath 'fixture' -Arguments @('command','recompile') -StdOutPath $out -StdErrPath $err -WorkingDirectory $root -Invoker {param($a,$o,$e)$script:calls++;0} -ProcessById {throw [IO.FileNotFoundException]::new('fixture PID absent')}
 Assert (-not $call.available -and $call.guard.status -eq 'pid_not_visible') 'Invisible PID was treated as dead or allowed.'
 Assert ($script:calls -eq 0 -and -not $call.available) 'Guard failure invoked the CLI.'
 $badProject=Join-Path $root 'not-a-unity-project';New-Item -ItemType Directory $badProject|Out-Null
 $invalid=Invoke-UnityConnectionCli -FilePath 'fixture' -Arguments @('status') -StdOutPath $out -StdErrPath $err -WorkingDirectory $badProject -Invoker {param($a,$o,$e)$script:calls++;0}
 Assert (-not $invalid.available -and $invalid.guard.status -eq 'invalid_project' -and $script:calls -eq 0) 'Invalid ProjectRoot reached CLI invocation.'
 $acl=Get-Acl -LiteralPath $hidden;$originalSddl=$acl.Sddl;$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
 $deny=New-Object Security.AccessControl.FileSystemAccessRule($sid,[Security.AccessControl.FileSystemRights]::ReadData,[Security.AccessControl.AccessControlType]::Deny)
 try{$acl.AddAccessRule($deny);Set-Acl -LiteralPath $hidden -AclObject $acl;Assert ((Test-UnityConnectionGuard $root).status -eq 'descriptor_unreadable') 'Denied descriptor access was not blocked.'}
 finally{$restore=New-Object Security.AccessControl.FileSecurity;$restore.SetSecurityDescriptorSddlForm($originalSddl);Set-Acl -LiteralPath $hidden -AclObject $restore}
 @{pid=$PID;port=7801;projectPath=(Join-Path $root 'other');unityVersion='6000.3.18f1';mode='editor'}|ConvertTo-Json|Set-Content $hidden
 Assert ((Test-UnityConnectionGuard $root).status -eq 'wrong_project') 'Wrong project descriptor was not blocked.'
 'not json'|Set-Content $hidden
 Assert ((Test-UnityConnectionGuard $root).status -eq 'descriptor_unreadable') 'Malformed descriptor was not blocked.'
 Remove-Item $hidden -Force
 $script:calls=0
 $unidentified=Invoke-UnityConnectionCli -FilePath 'fixture' -Arguments @('status') -StdOutPath $out -StdErrPath $err -WorkingDirectory $root -Invoker {param($a,$o,$e)$script:calls++;0} -EnumerateUnityProcesses { @(@{Name='Unity.exe';CommandLine=$null}) }
 Assert (-not $unidentified.available -and $unidentified.guard.status -eq 'editor_identity_unverified' -and $script:calls -eq 0) 'Unparseable visible Unity process was skipped.'
 $argsSeen=$null
 $env:UNITY_PROJECT_PATH='keep-me'
 $good=Invoke-UnityConnectionCli -FilePath 'fixture' -Arguments @('command','run_tests','--project-path',$root) -StdOutPath $out -StdErrPath $err -WorkingDirectory $root -Invoker {param($a,$o,$e)$script:argsSeen=@($a);$script:observedProject=$env:UNITY_PROJECT_PATH;'{"success":false}'|Set-Content $o;9} -EnumerateUnityProcesses { @() }
 Assert ($good.exitCode -eq 9 -and $script:argsSeen[1] -eq 'run_tests') 'CLI arguments or failure code changed.'
 Assert ($script:observedProject -eq $root -and $env:UNITY_PROJECT_PATH -eq 'keep-me') 'Unity project environment was not restored.'
 Assert ($env:LOCALAPPDATA -eq 'Z:\redirected\appdata') 'LOCALAPPDATA was not restored after child invocation.'
 $parsed=Get-Content $out -Raw|ConvertFrom-Json;Assert (-not $parsed.success) 'Malformed/failure envelope fixture was not preserved.'
 $script:calls=0
 $conflict=Invoke-UnityConnectionCli -FilePath 'fixture' -Arguments @('command','run_tests','--project-path',$root,'--project-path',(Join-Path $root 'sibling')) -StdOutPath $out -StdErrPath $err -WorkingDirectory $root -Invoker {param($a,$o,$e)$script:calls++;0} -EnumerateUnityProcesses { @() }
 Assert (-not $conflict.available -and $script:calls -eq 0) 'A later conflicting project argument retargeted the guarded call.'
 $wrapper=Join-Path $PSScriptRoot '..\Invoke-UnityCommand.ps1';$sibling=Join-Path $root 'sibling'
 $psi=New-Object Diagnostics.ProcessStartInfo;$psi.FileName=(Get-Command powershell.exe).Source
 $wrapperPath=(Resolve-Path $wrapper).Path
 $invokeScript=Join-Path $root 'invoke-wrapper.ps1'
 "& '$wrapperPath' -ProjectRoot '$root' -UnityArguments @('status','--project-path','$root','--project-path','$sibling')"|Set-Content -LiteralPath $invokeScript -Encoding UTF8
 $psi.Arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$invokeScript+'"'
 $psi.UseShellExecute=$false;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
 $proc=[Diagnostics.Process]::Start($psi);$wrapperOutput=$proc.StandardOutput.ReadToEnd();$wrapperError=$proc.StandardError.ReadToEnd();$proc.WaitForExit()
 $wrapperEnvelope=$wrapperOutput|ConvertFrom-Json
 Assert (-not $wrapperEnvelope.success -and $wrapperEnvelope.errors.Count -gt 0 -and $wrapperError) ("Command wrapper did not emit structured refusal (stdout=$wrapperOutput, stderr=$wrapperError).")
 $conflictEquals=Invoke-UnityConnectionCli -FilePath 'fixture' -Arguments @('status','--project-path='+ (Join-Path $root 'sibling')) -StdOutPath $out -StdErrPath $err -WorkingDirectory $root -Invoker {param($a,$o,$e)$script:calls++;0} -EnumerateUnityProcesses { @() }
 Assert (-not $conflictEquals.available -and $script:calls -eq 0) 'The equals-form conflicting project argument was not blocked.'
 $badFormat=Invoke-UnityConnectionCli -FilePath 'fixture' -Arguments @('status','--format=human') -StdOutPath $out -StdErrPath $err -WorkingDirectory $root -Invoker {param($a,$o,$e)$script:calls++;0} -EnumerateUnityProcesses { @() }
 Assert (-not $badFormat.available -and $script:calls -eq 0) 'The equals-form non-JSON format was not blocked.'
 $restricted=Invoke-UnityConnectionCli -FilePath 'fixture' -Arguments @('status') -StdOutPath $out -StdErrPath $err -WorkingDirectory $root -Invoker {param($a,$o,$e)$script:calls++;0} -EnumerateUnityProcesses {throw 'access denied'}
 Assert (-not $restricted.available -and $restricted.guard.status -eq 'restricted_process' -and $script:calls -eq 0) 'Restricted process visibility did not fail closed.'
 $script:calls=0
 $badJson=Invoke-UnityConnectionCli -FilePath 'fixture' -Arguments @('status') -StdOutPath $out -StdErrPath $err -WorkingDirectory $root -Invoker {param($a,$o,$e)$script:calls++;'not json'|Set-Content $o;17} -EnumerateUnityProcesses { @() }
 Assert ($badJson.exitCode -eq 17 -and $script:calls -eq 1 -and (Get-Content $out -Raw).Trim() -eq 'not json') 'CLI failure/malformed output was retried or rewritten.'
 $ready=(@{success=$true;data=@{instances=@(@{project=$root;state='ready'})};errors=@()}|ConvertTo-Json -Depth 5|ConvertFrom-Json);$ctx=(@{isPlaying=$false;isPlayingOrWillChangePlaymode=$false;isCompiling=$false;isUpdating=$false}|ConvertTo-Json|ConvertFrom-Json)
 Assert ((Get-UnityDoctorClassification $root $null 0 $ready $ctx).status -eq 'editor_running_pipeline_reachable') 'Doctor did not classify ready status.'
 Assert ((Get-UnityDoctorClassification $root $null 6 $ready $ctx).status -eq 'unknown') 'Doctor ignored a nonzero CLI exit.'
 Assert ((Get-UnityDoctorClassification $root $null 0 $null $null).status -eq 'malformed_cli_json') 'Doctor did not classify malformed JSON.'
 Assert ((Get-UnityDoctorClassification $root $null 0 @{} $null).status -eq 'malformed_cli_schema') 'Doctor did not classify malformed status schema.'
 $busy=(@{isPlaying=$false;isPlayingOrWillChangePlaymode=$false;isCompiling=$false;isUpdating=$true}|ConvertTo-Json|ConvertFrom-Json)
 Assert ((Get-UnityDoctorClassification $root $null 0 $ready $busy).status -eq 'busy_update') 'Doctor missed active Editor update.'
 $none=(@{success=$false;data=$null;errors=@(@{code='STATUS_NO_INSTANCES'})}|ConvertTo-Json -Depth 5|ConvertFrom-Json)
 Assert ((Get-UnityDoctorClassification $root $null 6 $none $null).status -eq 'no_editor') 'Doctor did not classify no Editor.'
 Assert ((Get-UnityDoctorClassification $root $null 6 $none $null -DescriptorExists $true).status -eq 'editor_running_pipeline_unreachable') 'Doctor did not distinguish a live descriptor with no reachable Pipeline.'
 'Unity connection regressions passed.'
} finally {
 if($initialHadLocal){$env:LOCALAPPDATA=$initialLocal}else{Remove-Item Env:LOCALAPPDATA -ErrorAction SilentlyContinue}
 if($initialHadProject){$env:UNITY_PROJECT_PATH=$initialProject}else{Remove-Item Env:UNITY_PROJECT_PATH -ErrorAction SilentlyContinue}
 $fullRoot=[IO.Path]::GetFullPath($root)
 if($fullRoot.StartsWith($tempRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($fullRoot) -like 'rq-unity-*'){Remove-Item -LiteralPath $fullRoot -Recurse -Force -ErrorAction SilentlyContinue}
}
