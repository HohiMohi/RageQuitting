$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Assert([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
function Write-JsonFile([string]$Path,[object]$Value) {
    ConvertTo-Json -InputObject $Value -Depth 12 | Set-Content -LiteralPath $Path -Encoding UTF8
}
function Invoke-Exporter([string[]]$Arguments) {
    $scriptPath=Join-Path $PSScriptRoot '..\Export-UnityValidationSummary.ps1'
    $ps=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $output=& $ps -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $scriptPath @Arguments 2>&1
    $jsonLine=@($output | Where-Object { $_ -match '^\{' } | Select-Object -Last 1)
    if ($jsonLine.Count -eq 0) { return @{exit=$LASTEXITCODE;result=$null;output=($output -join "`n")} }
    return @{exit=$LASTEXITCODE;result=($jsonLine[0] | ConvertFrom-Json);output=($output -join "`n")}
}

$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$testRoot=Join-Path (Join-Path $projectRoot 'Artifacts') ('ValidationSummary_Test_'+[Guid]::NewGuid().ToString('N'))
$allowedRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'Artifacts'))
$fullTestRoot=[IO.Path]::GetFullPath($testRoot)
$junctionPath=$null
if(-not $fullTestRoot.StartsWith($allowedRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw "Unsafe test path: $fullTestRoot"}
try {
    $null=New-Item -ItemType Directory -Path $fullTestRoot
    $modulePath=Join-Path $PSScriptRoot '..\ValidationSummary.psm1'
    Import-Module $modulePath -Force
    $reportDir=Join-Path $fullTestRoot 'reports'
    $null=New-Item -ItemType Directory -Path $reportDir
    $editPath=Join-Path $reportDir 'unity-editmode-tests-report.json'
    $playPath=Join-Path $reportDir 'unity-playmode-tests-report.json'
    $analyzerPath=Join-Path $reportDir 'analyzer-report.json'
    $compilePath=Join-Path $reportDir 'unity-compile-report.json'
    $consolePath=Join-Path $reportDir 'unity-console-report.json'
    $networkPath=Join-Path $reportDir 'unity-network-smoke-report.json'
    $scenarioPath=Join-Path $reportDir 'unity-gameplay-scenario-report.json'
    Write-JsonFile $editPath @{schemaVersion=1;status='passed';summary=@{total=3;passed=2;failed=0;skipped=1;inconclusive=0}}
    Write-JsonFile $playPath @{schemaVersion=1;status='passed';summary=@{total=4;passed=4;failed=0;skipped=0;inconclusive=0}}
    Write-JsonFile $analyzerPath @{schemaVersion=1;status='passed';counts=@{total=913;blocking=913;informational=0};baseline=@{occurrences=913};diagnostics=@{new=@();currentBlocking=@(@{id='UNT0021'});resolved=@()}}
    Write-JsonFile $compilePath @{schemaVersion=1;status='passed';errors=@()}
    Write-JsonFile $consolePath @{schemaVersion=1;status='passed';total=0;returned=0;diagnostics=@{current=@();new=@()}}
    Write-JsonFile $networkPath @{schemaVersion=1;status='passed';topology='host-only';peerCount=1;remoteClientValidated=$false;transport='UnityTransport';relayFlowInvoked=$false;knownLimitations=@('Relay not exercised')}
    Write-JsonFile $scenarioPath @{schemaVersion=1;status='passed';testReport=@{summary=@{total=1;passed=1;failed=0;skipped=0;inconclusive=0}}}

    function New-Summary([string]$Name,[string]$Status='passed',[int]$ExitCode=0,[string]$Tier='Gameplay') {
        $path=Join-Path $fullTestRoot ($Name+'.json')
        $steps=@(
            @{name='analyzers';status='passed';message='Current | baseline `` counts zażółć gęślą jaźń';artifacts=@($analyzerPath)},
            @{name='compile';status='passed';message='Compile passed';artifacts=@($compilePath)},
            @{name='console-baseline';status='passed';message='Console clear';artifacts=@($consolePath)},
            @{name='editmode-tests';status='passed';message='EditMode passed';artifacts=@($editPath)},
            @{name='playmode-tests';status='passed';message='PlayMode passed';artifacts=@($playPath)},
            @{name='gameplay-scenario';status='passed';message='Scenario passed';artifacts=@($scenarioPath)},
            @{name='network-smoke';status='passed';message='Host only';artifacts=@($networkPath)}
        )
        if($Status -eq 'failed'){$steps[4].status='failed';$steps[4].message='Failed | `literal`'+[Environment]::NewLine+'next line';$ExitCode=1}
        if($Tier -eq 'Full'){$steps+=@(@{name='all-tests';status='not_available';message='Placeholder';artifacts=@()})}
        if($Tier -eq 'Fast'){$steps=@($steps|Where-Object{$_.name -ne 'playmode-tests'})}
        if($Name -eq 'Filtered'){$steps[3].status='skipped'}
        $value=@{schemaVersion=1;runId='test|run`name';requestedTier=$Tier;resolvedTier=$Tier;editModeFilter=$(if($Name -eq 'Filtered'){'Name|with`tick'}else{''});playModeFilter='';requestedScenario='';resolvedScenario='TutorialBoot';startedAtUtc='2026-10-28T10:00:00Z';completedAtUtc='2026-10-28T10:00:02Z';status=$Status;exitCode=$ExitCode;steps=$steps}
        Write-JsonFile $path $value
        return $path
    }

    $passedPath=New-Summary 'Passed'
    $passed=ConvertTo-UnityValidationMarkdown $passedPath
    Assert ($passed.Contains('Validation status:** passed (exit 0)')) 'Passed status/exit missing.'
    Assert ($passed.Contains('Duration:** 2.0 seconds')) 'ISO timestamp duration was not parsed.'
    Assert ($passed.Contains('Started (UTC):** 2026-10-28T10:00:00.0000000+00:00')) 'UTC timestamp was not rendered as ISO round-trip.'
    Assert ($passed.Contains('EditMode filter:** None (unfiltered)')) 'Empty filters were not identified as unfiltered.'
    Assert ($passed.Contains('Authoritative JSON summary:** `' + $passedPath + '`')) 'Authoritative parent summary path was omitted.'
    Assert ($passed.Contains('Step totals:** 7 passed of 7 recorded.')) 'Top-level step totals are missing or incorrect.'
    Assert ($passed.Contains('Passed 2; failed 0; skipped 1; inconclusive 0; total 3.')) 'EditMode counts missing.'
    Assert ($passed.Contains('Passed 4; failed 0; skipped 0; inconclusive 0; total 4.')) 'PlayMode counts missing.'
    Assert ($passed.Contains('913 blocking, 0 informational across 913 current diagnostics')) 'Current analyzer totals missing.'
    Assert ($passed.Contains('New analyzer diagnostics:** 0. **Baseline occurrences:** 913')) 'New vs baseline analyzer counts were conflated.'
    Assert ($passed.Contains('host-only coverage')) 'Host-only network limitation missing.'
    Assert ($passed.Contains('peer count 1; remote client validated False; relay flow invoked False')) 'Network topology fields are missing.'
    Assert ($passed.Contains('Relay not exercised')) 'Network known limitations are missing.'
    Assert ($passed.Contains('test\|run&#96;name')) 'Markdown pipe/backtick escaping failed.'
    Assert ($passed.Contains('zażółć gęślą jaźń')) 'UTF-8 Markdown lost Polish characters.'
    Assert ($passed.Contains('current diagnostics 0; new 0')) 'Empty Console diagnostic arrays were not rendered as zero.'
    Assert ($passed.Contains('Compile details:** report status passed; reported errors 0')) 'Compile error count missing.'
    Assert ($passed.Contains('Acceptance is incomplete')) 'Skipped child tests did not qualify acceptance.'

    Write-JsonFile $playPath @{schemaVersion=1;status='failed';summary=@{total=4;passed=3;failed=1;skipped=0;inconclusive=0};tests=@(@{FullName='Example.Test|Failure';Status='Failed';Message='Expected `one` | received two'})}
    $failedPath=New-Summary 'Failed' 'failed' 1
    $failed=ConvertTo-UnityValidationMarkdown $failedPath
    Assert ($failed.Contains('Validation status:** failed (exit 1)')) 'Validation failure status was not preserved.'
    Assert ($failed.Contains('Failed \| &#96;literal&#96;')) 'Newline/backtick/pipe text escaping failed.'
    Assert ($failed.Contains('<br>next line')) 'Newlines were not escaped into a single Markdown line.'
    Assert ($failed.Contains('Example.Test\|Failure')) 'Failed test identity was omitted.'
    Assert ($failed.Contains('Expected &#96;one&#96; \| received two')) 'Failed test message was not escaped.'
    Assert ($failed.Contains('Non-passed steps')) 'Non-passed list missing.'
    $statusConflictPath=New-Summary 'StatusConflict'
    $statusConflict=ConvertTo-UnityValidationMarkdown $statusConflictPath
    Assert ($statusConflict.Contains('COUNT/STATUS CONTRADICTION')) 'Parent/child test status contradiction was not surfaced.'
    Write-JsonFile $playPath @{schemaVersion=1;status='passed';summary=@{total=4;passed=4;failed=0;skipped=0;inconclusive=0}}

    $filteredPath=New-Summary 'Filtered'
    $filtered=ConvertTo-UnityValidationMarkdown $filteredPath
    Assert ($filtered.Contains('Acceptance is incomplete')) 'Filtered/skipped run was represented as complete.'
    $fastPath=New-Summary 'Fast' 'passed' 0 'Fast'
    $fast=ConvertTo-UnityValidationMarkdown $fastPath
    Assert ($fast.Contains('PlayMode tests:** Not run; not required by resolved tier Fast.')) 'Fast did not explain its intentionally absent PlayMode step.'
    Assert (-not $fast.Contains('PlayMode: test counts unavailable')) 'Fast incorrectly treated its absent PlayMode step as missing evidence.'
    $blockedPath=New-Summary 'Blocked' 'blocked' 3
    $blocked=ConvertTo-UnityValidationMarkdown $blockedPath
    Assert ($blocked.Contains('Validation status:** blocked (exit 3)')) 'Blocked validation status/exit was not preserved.'
    Assert ($blocked.Contains('Acceptance is incomplete')) 'Blocked parent status was hidden by passing child results.'
    $fullPath=New-Summary 'Full' 'passed' 0 'Full'
    $full=ConvertTo-UnityValidationMarkdown $fullPath
    Assert ($full.Contains('Full-tier checks are placeholders or unavailable')) 'Full placeholder run was represented as complete.'

    $badCountsPath=Join-Path $fullTestRoot 'BadCounts.json'
    Write-JsonFile $editPath @{schemaVersion=1;status='passed';summary=@{total=3;passed=2;failed=1;skipped=0;inconclusive=0}}
    $badCounts=ConvertTo-UnityValidationMarkdown $passedPath
    Assert ($badCounts.Contains('COUNT/STATUS CONTRADICTION')) 'Test count/status contradiction was not flagged.'
    Set-Content -LiteralPath $editPath -Value '{ malformed' -Encoding UTF8
    $malformedChild=ConvertTo-UnityValidationMarkdown $passedPath
    Assert ($malformedChild.Contains('Child report could not be parsed')) 'Malformed child report was not shown as unavailable.'
    Write-JsonFile $editPath @{schemaVersion=1;status='passed';summary=@{total=3;passed=2;failed=0;skipped=1;inconclusive=0}}
    $missingChildSummary=New-Summary 'MissingChild'
    Move-Item -LiteralPath $editPath -Destination ($editPath+'.away')
    $missingChild=ConvertTo-UnityValidationMarkdown $missingChildSummary
    Assert ($missingChild.Contains('Child report path is missing')) 'Missing child report was not explicit.'
    Move-Item -LiteralPath ($editPath+'.away') -Destination $editPath

    Write-JsonFile $editPath @{schemaVersion=1;status='passed'}
    $incompleteChild=ConvertTo-UnityValidationMarkdown $passedPath
    Assert ($incompleteChild.Contains('Unavailable: report summary missing total, passed, failed, skipped, inconclusive')) 'Incomplete child summary was not explicit.'
    Write-JsonFile $editPath @{schemaVersion=1;status='passed';summary=@{total=3;passed=2;failed=0;skipped=1;inconclusive=0}}
    Write-JsonFile $compilePath @{schemaVersion=1;status='passed'}
    $missingCompileData=ConvertTo-UnityValidationMarkdown $passedPath
    Assert ($missingCompileData.Contains('Unavailable: report errors field is missing')) 'Missing compile error evidence was reported as zero.'
    Write-JsonFile $compilePath @{schemaVersion=1;status='passed';errors=@()}

    $badSummaryPath=Join-Path $fullTestRoot 'bad-summary.json'
    Set-Content -LiteralPath $badSummaryPath -Value '{ malformed' -Encoding UTF8
    $badSummary=Invoke-Exporter @('-SummaryPath',$badSummaryPath,'-Json')
    Assert ($badSummary.exit -ne 0 -and $badSummary.result.validationStatus -eq 'unknown') 'Malformed summary did not fail generation truthfully.'

    $minimalSummaryPath=Join-Path $fullTestRoot 'minimal-failed.json'
    Write-JsonFile $minimalSummaryPath @{schemaVersion=1;status='failed';exitCode=1;steps=@(@{name='project-environment';status='passed';message='Fixture';artifacts=@()});warnings=@('CLI exception token=fixture-secret')}
    $minimalMarkdown=ConvertTo-UnityValidationMarkdown $minimalSummaryPath
    Assert ($minimalMarkdown.Contains('Analyzer totals:** Unavailable')) 'Summary without an analyzer step failed to render unavailable evidence.'
    Assert ($minimalMarkdown.Contains('token=[redacted]')) 'Preflight warning was omitted or secret-like value was not redacted.'
    Assert (-not $minimalMarkdown.Contains('fixture-secret')) 'Secret-like warning text leaked into Markdown.'

    $blockedOutput=Join-Path $reportDir 'blocked.md';$null=New-Item -ItemType Directory -Path $blockedOutput
    $failedNoWrite=Invoke-Exporter @('-SummaryPath',$failedPath,'-OutputPath',$blockedOutput,'-Json')
    Assert ($failedNoWrite.exit -ne 0 -and $failedNoWrite.result.validationStatus -eq 'failed' -and $failedNoWrite.result.validationExitCode -eq 1) 'Markdown write failure obscured the source validation failure.'
    $humanOutput=Join-Path $fullTestRoot 'human-output.md';Set-Content -LiteralPath $humanOutput -Value '# human content' -Encoding UTF8
    $humanOutputResult=Invoke-Exporter @('-SummaryPath',$passedPath,'-OutputPath',$humanOutput,'-Json')
    Assert ($humanOutputResult.exit -ne 0 -and (Get-Content -LiteralPath $humanOutput -Raw).Contains('human content')) 'Writer replaced unmarked human Markdown.'
    $agentsOutput=Join-Path $fullTestRoot 'AGENTS.md';Set-Content -LiteralPath $agentsOutput -Value '# protected' -Encoding UTF8
    $agentsResult=Invoke-Exporter @('-SummaryPath',$passedPath,'-OutputPath',$agentsOutput,'-Json')
    Assert ($agentsResult.exit -ne 0 -and (Get-Content -LiteralPath $agentsOutput -Raw).Contains('protected')) 'Writer accepted AGENTS.md as an output target.'

    $sourceRoot=Join-Path $fullTestRoot 'DocsSource';$mirrorRoot=Join-Path $fullTestRoot 'DocsMirror'
    $null=New-Item -ItemType Directory -Path $sourceRoot,$mirrorRoot
    $published=Invoke-Exporter @('-SummaryPath',$passedPath,'-Publish','-SourceRoot',$sourceRoot,'-MirrorRoot',$mirrorRoot,'-Json')
    Assert ($published.exit -eq 0 -and $published.result.publicationStatus -eq 'passed') ('Safe isolated publication failed: '+$published.output)
    $sourceDoc=Join-Path $sourceRoot 'Generated\Latest-Validation.md';$mirrorDoc=Join-Path $mirrorRoot 'Generated\Latest-Validation.md'
    $sameBytes=[Convert]::ToBase64String([IO.File]::ReadAllBytes($sourceDoc)) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($mirrorDoc))
    Assert $sameBytes 'Published source and mirror differ.'
    $sourceBytes=[IO.File]::ReadAllBytes($sourceDoc)
    $utf8=[Text.Encoding]::UTF8.GetString($sourceBytes)
    Assert ($utf8.Contains('Unity validation summary')) 'Generated UTF-8 Markdown could not be read.'

    $humanSource=Join-Path $fullTestRoot 'HumanSource';$humanMirror=Join-Path $fullTestRoot 'HumanMirror'
    $null=New-Item -ItemType Directory -Path (Join-Path $humanSource 'Generated'),$humanMirror
    $humanDoc=Join-Path $humanSource 'Generated\Latest-Validation.md';Set-Content -LiteralPath $humanDoc -Value '# Human notes' -Encoding UTF8
    $refused=Invoke-Exporter @('-SummaryPath',$passedPath,'-Publish','-SourceRoot',$humanSource,'-MirrorRoot',$humanMirror,'-Json')
    Assert ($refused.exit -ne 0 -and (Get-Content -LiteralPath $humanDoc -Raw).Contains('Human notes')) 'Human-authored publication target was overwritten.'

    $reparseSource=Join-Path $fullTestRoot 'ReparseSource';$junctionTarget=Join-Path $fullTestRoot 'JunctionTarget'
    $null=New-Item -ItemType Directory -Path $reparseSource,$junctionTarget
    $junctionPath=Join-Path $reparseSource 'Generated'
    $junctionCreated=$false
    try { $null=New-Item -ItemType Junction -Path $junctionPath -Target $junctionTarget; $junctionCreated=$true }
    catch { Write-Warning 'Reparse-point refusal test could not create a junction in this sandbox; publication code still checks every existing path component.'; $junctionPath=$null }
    if($junctionCreated){
        $reparseResult=Invoke-Exporter @('-SummaryPath',$passedPath,'-Publish','-SourceRoot',$reparseSource,'-MirrorRoot',$humanMirror,'-Json')
        Assert ($reparseResult.exit -ne 0) 'Publication through a reparse point was accepted.'
        Remove-Item -LiteralPath $junctionPath -Force
        $junctionPath=$null
    }

    $mirrorFailureSource=Join-Path $fullTestRoot 'RollbackSource';$mirrorFailureRoot=Join-Path $fullTestRoot 'MirrorFailure'
    $null=New-Item -ItemType Directory -Path (Join-Path $mirrorFailureSource 'Generated'),$mirrorFailureRoot
    $rollbackDoc=Join-Path $mirrorFailureSource 'Generated\Latest-Validation.md'
    $marker='<!-- BEGIN GENERATED UNITY VALIDATION SUMMARY -->'+[Environment]::NewLine+'<!-- END GENERATED UNITY VALIDATION SUMMARY -->'
    Set-Content -LiteralPath $rollbackDoc -Value $marker -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $mirrorFailureRoot 'Generated') -Value 'file blocks directory creation'
    $before=[Convert]::ToBase64String([IO.File]::ReadAllBytes($rollbackDoc))
    $mirrorFailure=Invoke-Exporter @('-SummaryPath',$failedPath,'-Publish','-SourceRoot',$mirrorFailureSource,'-MirrorRoot',$mirrorFailureRoot,'-Json')
    Assert ($mirrorFailure.exit -ne 0 -and $mirrorFailure.result.validationStatus -eq 'failed') 'Mirror publication failure did not remain nonzero/truthful.'
    Assert ([Convert]::ToBase64String([IO.File]::ReadAllBytes($rollbackDoc)) -ceq $before) 'Failed publication did not restore the authoritative source.'

    $overlap=Invoke-Exporter @('-SummaryPath',$passedPath,'-Publish','-SourceRoot',$sourceRoot,'-MirrorRoot',(Join-Path $sourceRoot 'child'),'-Json')
    Assert ($overlap.exit -ne 0) 'Overlapping publication roots were accepted.'
    Write-Output 'Validation summary renderer/exporter tests passed.'
}
finally {
    if($junctionPath -and (Test-Path -LiteralPath $junctionPath)){Remove-Item -LiteralPath $junctionPath -Force -ErrorAction SilentlyContinue}
    if(Test-Path -LiteralPath $fullTestRoot){
        $resolved=[IO.Path]::GetFullPath((Resolve-Path -LiteralPath $fullTestRoot).Path)
        if($resolved -eq $fullTestRoot -and $resolved.StartsWith($allowedRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){Remove-Item -LiteralPath $resolved -Recurse -Force}
    }
}
