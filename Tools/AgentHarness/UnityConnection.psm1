Set-StrictMode -Version 2.0

function Resolve-UnityCliPath {
    param([string]$Override = $env:RQ_UNITY_CLI_PATH)
    if ($Override) {
        if (-not [System.IO.Path]::IsPathRooted($Override)) { throw 'RQ_UNITY_CLI_PATH must be an absolute path.' }
        $path = [System.IO.Path]::GetFullPath($Override)
        if ([IO.Path]::GetFileName($path) -ine 'unity.exe') { throw 'RQ_UNITY_CLI_PATH must name unity.exe.' }
    }
    else {
        $local = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
        $path = Join-Path $local 'Unity\bin\unity.exe'
    }
    return $path
}

function Get-UnityDescriptorPath([string]$ProjectRoot) {
    Join-Path $ProjectRoot 'Library\Pipeline\.unity-pipeline-port'
}

function Test-UnityConnectionGuard {
    param([Parameter(Mandatory=$true)][string]$ProjectRoot,[scriptblock]$ProcessById,[scriptblock]$ProcessDetailsById,[scriptblock]$EnumerateUnityProcesses)
    $descriptor = Get-UnityDescriptorPath $ProjectRoot
    $result = [ordered]@{ allowed=$true; status='clear'; descriptorPath=$descriptor; reason=$null }
    if(-not (Test-Path -LiteralPath (Join-Path $ProjectRoot 'Assets') -PathType Container) -or -not (Test-Path -LiteralPath (Join-Path $ProjectRoot 'Packages\manifest.json') -PathType Leaf) -or -not (Test-Path -LiteralPath (Join-Path $ProjectRoot 'ProjectSettings\ProjectVersion.txt') -PathType Leaf)){$result.allowed=$false;$result.status='invalid_project';$result.reason='ProjectRoot is missing required Unity project markers.';return $result}
    try {
        $descriptorExists=$true
        try { $descriptorItem=Get-Item -LiteralPath $descriptor -Force -ErrorAction Stop; if($descriptorItem.PSIsContainer){throw 'Descriptor path is a directory.'} }
        catch [System.Management.Automation.ItemNotFoundException] { $descriptorExists=$false }
        catch [System.IO.FileNotFoundException] { $descriptorExists=$false }
        catch [System.IO.DirectoryNotFoundException] { $descriptorExists=$false }
        catch { $result.allowed=$false;$result.status='descriptor_unreadable';$result.reason='Pipeline descriptor access is restricted or invalid.';return $result }
        if (-not $descriptorExists) {
            # A missing descriptor is safe only when we can enumerate Unity processes and prove
            # that none owns this project. An inaccessible process table is not proof of absence.
            try{$allUnity = if($EnumerateUnityProcesses){@(& $EnumerateUnityProcesses)}else{@(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction Stop)}}
            catch{$result.allowed=$false;$result.status='restricted_process';$result.reason='Process visibility is restricted; missing descriptor cannot prove the Editor is absent.';return $result}
            foreach($candidate in $allUnity){
                if([string]$candidate.Name -ine 'Unity.exe'){continue}
                $candidateRoot = Get-UnityProjectFromCommandLine ([string]$candidate.CommandLine)
                if(-not $candidateRoot){$result.allowed=$false;$result.status='editor_identity_unverified';$result.reason='A Unity Editor process is visible but its project command line cannot be verified.';return $result}
                if($candidateRoot -and [string]::Equals($candidateRoot,[IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)){
                    $result.allowed=$false;$result.status='missing_descriptor_live_editor';$result.reason='Unity Editor for this project is visible but its authenticated descriptor is missing.';return $result
                }
            }
            return $result
        }
        $raw = [IO.File]::ReadAllText($descriptor)
        $data = $raw | ConvertFrom-Json -ErrorAction Stop
        if (-not $data.pid -or -not $data.projectPath) { throw 'Descriptor lacks required identity fields.' }
        $a = [IO.Path]::GetFullPath([string]$data.projectPath).TrimEnd('\')
        $b = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
        if (-not [string]::Equals($a,$b,[StringComparison]::OrdinalIgnoreCase)) {
            $result.allowed=$false; $result.status='wrong_project'; $result.reason='Pipeline descriptor belongs to a different project.'; return $result
        }
        try { if($ProcessById){$proc=& $ProcessById ([int]$data.pid)}else{$proc = Get-Process -Id ([int]$data.pid) -ErrorAction Stop} }
        catch {
            if ($_.Exception -is [System.ComponentModel.Win32Exception] -or $_.Exception.Message -match 'access|denied') {
                $result.allowed=$false; $result.status='restricted_process'; $result.reason='Descriptor PID visibility is restricted; refusing CLI discovery.'; return $result
            }
            $result.allowed=$false; $result.status='pid_not_visible'; $result.reason='Descriptor PID is not visible; refusing to assume it is dead.'; return $result
        }
        try {
            $owner = if($ProcessDetailsById){& $ProcessDetailsById ([int]$data.pid)}else{Get-CimInstance Win32_Process -Filter "ProcessId = $([int]$data.pid)" -ErrorAction Stop}
            if (-not $owner -or [string]$owner.Name -ine 'Unity.exe' -or -not $owner.CommandLine ) {
                $result.allowed=$false; $result.status='restricted_process'; $result.reason='Could not verify the descriptor process project ownership.'; return $result
            }
            $ownerRoot=Get-UnityProjectFromCommandLine ([string]$owner.CommandLine)
            if (-not $ownerRoot -or -not [string]::Equals($ownerRoot,$b,[StringComparison]::OrdinalIgnoreCase)) { $result.allowed=$false; $result.status='pid_project_mismatch'; $result.reason='Descriptor PID command line does not identify this exact project.'; return $result }
        } catch {
            $result.allowed=$false; $result.status='restricted_process'; $result.reason='Process command-line visibility is restricted; refusing CLI discovery.'; return $result
        }
        $result.status='descriptor_live'
        return $result
    } catch {
        $result.allowed=$false; $result.status='descriptor_unreadable'; $result.reason='Pipeline descriptor cannot be safely read or validated.'; return $result
    }
}

function Get-UnityProjectFromCommandLine([string]$CommandLine){
    if($CommandLine -notmatch '(?i)(?:^|\s)-projectPath(?:\s+|=)(?:"([^"]+)"|([^\s]+))') {return $null}
    $value=if($Matches[1]){$Matches[1]}else{$Matches[2]}
    try{return [IO.Path]::GetFullPath($value).TrimEnd('\')}catch{return $null}
}

function Get-UnityDoctorClassification {
    param([string]$ProjectRoot,[object]$Guard,[int]$CliExitCode=0,[object]$Envelope,[object]$Context,[bool]$DescriptorExists=$false,[bool]$ProjectEditorVisible=$false,[bool]$CompileEvidence=$false,[bool]$SafeModeEvidence=$false)
    $status='unknown';$reason=$null
    if($Guard -and -not $Guard.allowed){$status=[string]$Guard.status;$reason=[string]$Guard.reason}
    elseif($null -eq $Envelope){$status='malformed_cli_json';$reason='Unity CLI returned malformed JSON.'}
    elseif(-not $Envelope.PSObject.Properties['success']){$status='malformed_cli_schema';$reason='Unity CLI status envelope is missing success.'}
    elseif($CliExitCode -ne 0 -or -not $Envelope.success){
        if(-not $Envelope.PSObject.Properties['errors']){$status='malformed_cli_schema';$reason='Unity CLI envelope is missing errors.'}
        else{$codes=@($Envelope.errors|ForEach-Object{if($_.PSObject.Properties['code']){[string]$_.code}})
            if($codes -match 'NO_INSTANCES|STATUS_NO_INSTANCES'){
                if($SafeModeEvidence){$status='safe_mode_evidence_unverified'}elseif($CompileEvidence){$status='compile_error_evidence_unverified'}elseif($DescriptorExists -or $ProjectEditorVisible){$status='editor_running_pipeline_unreachable'}else{$status='no_editor'}
            }else{$status='unknown';$reason='Unity CLI status failed without a recognized no-instance result.'}
        }
    }
    elseif(-not $Envelope.PSObject.Properties['data'] -or -not $Envelope.data -or -not $Envelope.data.PSObject.Properties['instances']){$status='malformed_cli_schema';$reason='Unity CLI status envelope is missing data.instances.'}
    else{
        $root=[IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
        $matches=@($Envelope.data.instances|Where-Object{$p=if($_.PSObject.Properties['project']){[string]$_.project}elseif($_.PSObject.Properties['projectPath']){[string]$_.projectPath}else{''};$s=if($_.PSObject.Properties['state']){[string]$_.state}else{''};$p -and $s -eq 'ready' -and [string]::Equals([IO.Path]::GetFullPath($p).TrimEnd('\'),$root,[StringComparison]::OrdinalIgnoreCase)})
        if(-not $matches.Count){$status='unknown';$reason='No ready instance matched the exact project root.'}
        elseif($null -eq $Context){$status='unknown';$reason='Editor context probe was unavailable or malformed.'}
        elseif($Context.isPlaying -or $Context.isPlayingOrWillChangePlaymode){$status='busy_play'}
        elseif($Context.isCompiling){$status='busy_compile'}
        elseif($Context.isUpdating){$status='busy_update'}
        else{$status='editor_running_pipeline_reachable'}
    }
    $healthy=$status -eq 'editor_running_pipeline_reachable'
    return [ordered]@{status=$status;exitCode=if($healthy){0}else{3};reason=$reason}
}

function Invoke-UnityConnectionCli {
    param([string]$FilePath,[string[]]$Arguments,[string]$StdOutPath,[string]$StdErrPath,[string]$WorkingDirectory,[scriptblock]$Invoker,[scriptblock]$ProcessById,[scriptblock]$ProcessDetailsById,[scriptblock]$EnumerateUnityProcesses)
    $guard = Test-UnityConnectionGuard -ProjectRoot $WorkingDirectory -ProcessById $ProcessById -ProcessDetailsById $ProcessDetailsById -EnumerateUnityProcesses $EnumerateUnityProcesses
    if (-not $guard.allowed) {
        $message=[string]$guard.reason
        if($StdOutPath){@{success=$false;command='guard';data=$null;errors=@(@{code=([string]$guard.status).ToUpperInvariant();message=$message});warnings=@()}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $StdOutPath -Encoding UTF8}
        if($StdErrPath){$message|Set-Content -LiteralPath $StdErrPath -Encoding UTF8}
        return [ordered]@{ available=$false; exitCode=3; error=$guard.reason; guard=$guard }
    }
    $argsList=@($Arguments);$roots=@();$formats=@();$jsonFlags=0
    for($i=0;$i -lt $argsList.Count;$i++){
        $arg=[string]$argsList[$i]
        if($arg -ieq '--project-path'){if($i+1 -ge $argsList.Count){$roots+=''}else{$roots+=[string]$argsList[++$i]}}
        elseif($arg -imatch '^--project-path=(.*)$'){$roots+=$Matches[1]}
        elseif($arg -ieq '--format'){if($i+1 -ge $argsList.Count){$formats+=''}else{$formats+=[string]$argsList[++$i]}}
        elseif($arg -imatch '^--format=(.*)$'){$formats+=$Matches[1]}
        elseif($arg -ieq '--json'){$jsonFlags++}
    }
    if($roots.Count){foreach($target in $roots){try{$canonical=[IO.Path]::GetFullPath($target).TrimEnd('\')}catch{$canonical=''};if(-not [string]::Equals($canonical,[IO.Path]::GetFullPath($WorkingDirectory).TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)){return [ordered]@{available=$false;exitCode=3;error='Unity CLI target differs from the guarded project root.';guard=$guard}}}}
    else{$argsList+=@('--project-path',[IO.Path]::GetFullPath($WorkingDirectory))}
    if(@($formats|Where-Object{$_ -ine 'json'}).Count -or ($jsonFlags -gt 0 -and $formats.Count)){return [ordered]@{available=$false;exitCode=3;error='Unity CLI connection calls require --format json.';guard=$guard}}
    if(-not $formats.Count -and -not $jsonFlags){$argsList+=@('--format','json')}
    if (-not $Invoker -and -not (Test-Path -LiteralPath $FilePath -PathType Leaf)) { return [ordered]@{available=$false;exitCode=3;error="Unity CLI not found at '$FilePath'.";guard=$guard} }
    $old = $env:UNITY_PROJECT_PATH
    $oldLocal=$env:LOCALAPPDATA
    $actualLocal=[Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    try {
        $env:UNITY_PROJECT_PATH = [IO.Path]::GetFullPath($WorkingDirectory)
        $env:LOCALAPPDATA=$actualLocal
        Push-Location -LiteralPath $WorkingDirectory
        try {
            $ErrorActionPreference='Continue'
            if($Invoker){$code=& $Invoker $argsList $StdOutPath $StdErrPath}
            else {& $FilePath @argsList 1> $StdOutPath 2> $StdErrPath; $code=$LASTEXITCODE}
        } finally { Pop-Location }
        return [ordered]@{available=$true;exitCode=$code;error=$null;guard=$guard}
    } catch {
        return [ordered]@{available=$false;exitCode=3;error=$_.Exception.Message;guard=$guard}
    } finally { $env:UNITY_PROJECT_PATH=$old; $env:LOCALAPPDATA=$oldLocal }
}

Export-ModuleMember -Function Resolve-UnityCliPath,Get-UnityDescriptorPath,Get-UnityProjectFromCommandLine,Test-UnityConnectionGuard,Invoke-UnityConnectionCli,Get-UnityDoctorClassification
