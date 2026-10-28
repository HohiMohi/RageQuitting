$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0

function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}

$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$allowedRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'Artifacts'))
$testRoot=[IO.Path]::GetFullPath((Join-Path $allowedRoot ('DocumentationCheck_Test_'+[Guid]::NewGuid().ToString('N'))))
if(-not $testRoot.StartsWith($allowedRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw "Unsafe fixture path: $testRoot"}
$envNames=@('RQ_DOC_CHECK_DENY_ROOT_SCAN','RQ_DOC_CHECK_SOURCE_ROOT','RQ_DOC_CHECK_MIRROR_ROOT')
$originalEnv=@{}
foreach($name in $envNames){$originalEnv[$name]=if(Test-Path -LiteralPath ("Env:"+$name)){[pscustomobject]@{exists=$true;value=[Environment]::GetEnvironmentVariable($name)}}else{[pscustomobject]@{exists=$false;value=$null}}}
try{
    $null=New-Item -ItemType Directory -Path $testRoot
    $sourceRoot=Join-Path $testRoot 'source'
    $mirrorRoot=Join-Path $testRoot 'mirror'
    $artifactRoot=Join-Path $testRoot 'artifacts'
    $harnessRoot=Join-Path $testRoot 'Tools\AgentHarness'
    $null=New-Item -ItemType Directory -Path $sourceRoot,$mirrorRoot,$artifactRoot,$harnessRoot
    $null=New-Item -ItemType Directory -Path (Join-Path $sourceRoot 'Experiments\Tools\DeepBump\prepared\deepbump\dependencies\bin') -Force
    Set-Content -LiteralPath (Join-Path $sourceRoot 'Selected.md') -Value 'same content'
    Set-Content -LiteralPath (Join-Path $mirrorRoot 'Selected.md') -Value 'same content'
    Set-Content -LiteralPath (Join-Path $sourceRoot 'Experiments\Tools\DeepBump\prepared\deepbump\dependencies\bin\unselected.md') -Value 'source only'
    Set-Content -LiteralPath (Join-Path $mirrorRoot 'MirrorOnly.md') -Value 'target only'
    Set-Content -LiteralPath (Join-Path $sourceRoot 'Different.md') -Value 'source version'
    Set-Content -LiteralPath (Join-Path $mirrorRoot 'Different.md') -Value 'mirror version'

    $checkScript=Join-Path $harnessRoot 'Invoke-DocumentationCheck.ps1'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\Invoke-DocumentationCheck.ps1') -Destination $checkScript
    $scriptText=[IO.File]::ReadAllText($checkScript)
    $marker='Set-StrictMode -Version 2.0'
    $scanGuard=@'
function Get-ChildItem {
    [CmdletBinding()]
    param([string]$LiteralPath,[string]$Path,[string]$Filter,[switch]$File,[switch]$Directory,[switch]$Recurse,[switch]$Force)
    $target=$LiteralPath
    if([string]::IsNullOrWhiteSpace($target)){$target=$Path}
    if($env:RQ_DOC_CHECK_DENY_ROOT_SCAN -eq '1' -and $Recurse -and $target -in @($env:RQ_DOC_CHECK_SOURCE_ROOT,$env:RQ_DOC_CHECK_MIRROR_ROOT)){
        throw "Fixture denied global root listing: $target"
    }
    $forward=@{}
    foreach($key in $PSBoundParameters.Keys){$forward[$key]=$PSBoundParameters[$key]}
    Microsoft.PowerShell.Management\Get-ChildItem @forward
}
'@
    if(-not $scriptText.Contains($marker)){throw 'Could not instrument the copied documentation checker.'}
    $scriptText=$scriptText.Replace($marker,$marker+"`r`n"+$scanGuard)
    [IO.File]::WriteAllText($checkScript,$scriptText,[Text.UTF8Encoding]::new($false))

    $env:RQ_DOC_CHECK_DENY_ROOT_SCAN='1'
    $env:RQ_DOC_CHECK_SOURCE_ROOT=$sourceRoot
    $env:RQ_DOC_CHECK_MIRROR_ROOT=$mirrorRoot
    $powershell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    function Invoke-Check([string]$Name,[string[]]$Selection=@()){
        $artifact=Join-Path $artifactRoot $Name
        $arguments=@('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$checkScript,'-SourceRoot',$sourceRoot,'-MirrorRoot',$mirrorRoot,'-ArtifactDirectory',$artifact,'-Json')+$Selection
        $output=& $powershell @arguments 2>&1
        $code=$LASTEXITCODE
        $jsonLine=@($output|Where-Object{$_ -match '^\{' }|Select-Object -Last 1)
        if($jsonLine.Count -eq 0){throw "Documentation check returned no JSON ($Name, exit $code): $($output -join ' ')"}
        $envelope=$jsonLine[0]|ConvertFrom-Json
        $report=Get-Content -LiteralPath $envelope.reportPath -Raw|ConvertFrom-Json
        return @{code=$code;envelope=$envelope;report=$report;output=$output}
    }

    $selected=Invoke-Check 'selected' @('-Document','Selected.md')
    Assert ($selected.code -eq 0 -and $selected.report.status -eq 'passed') 'Selected-document check traversed an unselected root or failed.'
    Assert ($selected.report.targetOnlyScope -eq 'not_scanned_selected_documents') 'Selected check did not identify target-only scope as unscanned.'
    Assert (@($selected.report.targetOnly).Count -eq 0) 'Selected check reported target-only files it did not scan.'
    Assert (@($selected.report.matching) -contains 'Selected.md') 'Selected matching document was not checked.'

    $selectedMissing=Invoke-Check 'selected-missing' @('-Document','Missing.md')
    Assert ($selectedMissing.code -eq 1 -and $selectedMissing.report.status -eq 'failed') 'Selected missing document did not fail with exit 1.'
    Assert (@($selectedMissing.report.missing) -contains 'Missing.md') 'Selected missing document was not reported.'
    Assert ($selectedMissing.report.targetOnlyScope -eq 'not_scanned_selected_documents') 'Selected missing report lost its scan scope.'

    $selectedDifferent=Invoke-Check 'selected-different' @('-Document','Different.md')
    Assert ($selectedDifferent.code -eq 1 -and $selectedDifferent.report.status -eq 'failed') 'Selected different document did not fail with exit 1.'
    Assert (@($selectedDifferent.report.different) -contains 'Different.md') 'Selected content mismatch was not reported.'

    $allDenied=Invoke-Check 'all-denied' @('-All')
    Assert ($allDenied.code -eq 2 -and $allDenied.report.status -eq 'failed') 'All-mode root enumeration failure was hidden.'
    Assert ($allDenied.report.targetOnlyScope -eq 'all') 'All-mode failure did not identify all-root scan scope.'
    Assert (@($allDenied.report.errors|Where-Object{$_ -match 'Fixture denied global root listing'}).Count -gt 0) 'All-mode enumeration failure was not recorded.'

    $defaultDenied=Invoke-Check 'default-denied'
    Assert ($defaultDenied.code -eq 2 -and $defaultDenied.report.targetOnlyScope -eq 'all') 'Default all-document scan behavior was changed or its failure hidden.'

    $env:RQ_DOC_CHECK_DENY_ROOT_SCAN='0'
    $all=Invoke-Check 'all'
    Assert ($all.code -eq 1 -and $all.report.status -eq 'failed') 'All mode failed to report existing source/mirror differences.'
    Assert ($all.report.targetOnlyScope -eq 'all') 'All mode did not report all-root scan scope.'
    Assert (@($all.report.targetOnly) -contains 'MirrorOnly.md') 'All mode did not enumerate target-only documents.'
    Assert (@($all.report.missing) -contains 'Experiments/Tools/DeepBump/prepared/deepbump/dependencies/bin/unselected.md') 'All mode omitted an unselected source document mismatch.'

    $default=Invoke-Check 'default'
    Assert ($default.code -eq 1 -and $default.report.targetOnlyScope -eq 'all') 'Default scan did not preserve all-document semantics.'
    Assert (@($default.report.targetOnly) -contains 'MirrorOnly.md') 'Default scan did not enumerate target-only documents.'
    Write-Output 'Documentation check selection-scope tests passed.'
}
finally{
    foreach($name in $envNames){$saved=$originalEnv[$name];if($saved.exists){[Environment]::SetEnvironmentVariable($name,$saved.value)}else{[Environment]::SetEnvironmentVariable($name,$null)}}
    if(Test-Path -LiteralPath $testRoot){
        $resolved=[IO.Path]::GetFullPath((Resolve-Path -LiteralPath $testRoot).Path)
        if($resolved -eq $testRoot -and $resolved.StartsWith($allowedRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){Remove-Item -LiteralPath $resolved -Recurse -Force}
    }
}
