$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$testRoot = Join-Path (Join-Path $projectRoot 'Artifacts') ('ValidationWorkflow_Test_' + [Guid]::NewGuid().ToString('N'))
$fullTestRoot = [IO.Path]::GetFullPath($testRoot)
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Artifacts'))
if (-not $fullTestRoot.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use test path outside fixture root: $fullTestRoot"
}
$originalOverride = $env:RQ_UNITY_CLI_PATH
$originalLocalAppData = $env:LOCALAPPDATA
$originalAnalyzerStatus = $env:RQ_TEST_ANALYZER_STATUS
$originalUnitySentinel = $env:RQ_TEST_UNITY_SENTINEL
try {
    $null = New-Item -ItemType Directory -Path $fullTestRoot
    $fixture = Join-Path $fullTestRoot 'Project'
    $null = New-Item -ItemType Directory -Path $fixture
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..') -Destination (Join-Path $fixture 'Tools\AgentHarness') -Recurse
    $agentHarness = Join-Path $fixture 'Tools\AgentHarness'
    $null = New-Item -ItemType Directory -Path (Join-Path $fixture 'Artifacts\Validation') -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $fixture 'Packages') -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $fixture 'Assets') -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $fixture 'ProjectSettings') -Force
    Set-Content -LiteralPath (Join-Path $fixture 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 6000.0.0f1'
    Set-Content -LiteralPath (Join-Path $fixture 'Packages\manifest.json') -Value '{"dependencies":{}}'
    $null = New-Item -ItemType File -Path (Join-Path $fullTestRoot 'unity.exe')
    $env:RQ_TEST_UNITY_SENTINEL = Join-Path $fullTestRoot 'unity-invocations.txt'
    $connectionModule = Join-Path $agentHarness 'UnityConnection.psm1'
    $connectionSource = [IO.File]::ReadAllText($connectionModule)
    $guardMarker = '    $guard = Test-UnityConnectionGuard'
    $mockGuard = @'
    if ([IO.Path]::GetFileName($FilePath) -ieq 'unity.exe') {
        Add-Content -LiteralPath $env:RQ_TEST_UNITY_SENTINEL -Value ($Arguments -join ' ')
        @{success=$false;command='status';data=$null;errors=@(@{code='STATUS_NO_INSTANCES';message='Fixture reports no Editor'});warnings=@()} |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $StdOutPath -Encoding UTF8
        Set-Content -LiteralPath $StdErrPath -Value 'Fixture mock; executable intentionally not launched.'
        return [ordered]@{available=$true;exitCode=3;error=$null;guard=@{allowed=$false;status='no_editor';reason='Fixture mock'}}
    }
'@
    if (-not $connectionSource.Contains($guardMarker)) { throw 'Unity connection instrumentation point was not found.' }
    $connectionSource = $connectionSource.Replace($guardMarker, $mockGuard + "`r`n" + $guardMarker)
    [IO.File]::WriteAllText($connectionModule, $connectionSource, [Text.UTF8Encoding]::new($false))
    Push-Location $fixture
    try {
        git init -q
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $fixture '.git') -PathType Container)) { throw 'Fixture git init did not create its own repository.' }
        git -C $fixture config user.email test@example.invalid
        if ($LASTEXITCODE -ne 0) { throw 'Could not configure fixture repository email.' }
        git -C $fixture config user.name 'Harness Test'
        if ($LASTEXITCODE -ne 0) { throw 'Could not configure fixture repository name.' }
        Set-Content -LiteralPath (Join-Path $fixture 'fixture.txt') -Value 'fixture'
        git -C $fixture add fixture.txt
        if ($LASTEXITCODE -ne 0) { throw 'Could not stage fixture file.' }
        git -C $fixture commit -qm 'fixture'
        if ($LASTEXITCODE -ne 0) { throw 'Could not commit fixture file.' }
    }
    finally { Pop-Location }

    $fakeAnalyzer = @'
param([string]$ArtifactDirectory,[switch]$Json)
$null=New-Item -ItemType Directory -Path $ArtifactDirectory -Force
$reportPath=Join-Path $ArtifactDirectory 'report.json'
$status=$env:RQ_TEST_ANALYZER_STATUS
$exit=if($status -eq 'passed'){0}elseif($status -eq 'not_available'){3}elseif($status -eq 'malformed'){0}else{1}
if($status -eq 'malformed'){[Console]::Out.WriteLine('{malformed');exit 0}
$report=[ordered]@{status=$status;artifacts=@();build=[ordered]@{scope='fixture';topLevelProject='fixture'}}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8
[ordered]@{reportPath=$reportPath;status=$status;exitCode=$exit} | ConvertTo-Json -Compress
exit $exit
'@
    Set-Content -LiteralPath (Join-Path $agentHarness 'Invoke-AnalyzerCheck.ps1') -Value $fakeAnalyzer -Encoding UTF8
    # The copied connection module records attempts and returns a harmless no-editor
    # result. The dummy file only satisfies Test-Path and is never launched.
    $env:RQ_UNITY_CLI_PATH = Join-Path $fullTestRoot 'unity.exe'
    $preflight = Join-Path $agentHarness 'Invoke-UnityPreflight.ps1'

    function Invoke-Fixture([string]$AnalyzerStatus, [switch]$AllowLowerTier) {
        Remove-Item -LiteralPath $env:RQ_TEST_UNITY_SENTINEL -Force -ErrorAction SilentlyContinue
        $env:RQ_TEST_ANALYZER_STATUS = $AnalyzerStatus
        $args = @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$preflight,'-Tier','Gameplay','-Json')
        if ($AllowLowerTier) { $args += '-AllowLowerTier' }
        $output = & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') @args 2>&1
        $result = $output | Where-Object { $_ -match '^\{' } | Select-Object -Last 1 | ConvertFrom-Json
        $summary = Get-Content -LiteralPath $result.summaryPath -Raw | ConvertFrom-Json
        return @{ result=$result; summary=$summary }
    }

    $passed = Invoke-Fixture 'passed'
    Assert (@($passed.summary.steps | Where-Object name -eq 'unity-cli-status').Count -eq 1) ("Passed analyzer did not proceed to the Unity CLI stage: status={0}, steps={1}, warnings={2}" -f $passed.summary.status, (($passed.summary.steps | ForEach-Object { $_.name + '=' + $_.status }) -join '; '), ($passed.summary.warnings -join '; '))
    Assert (@(Get-Content -LiteralPath $env:RQ_TEST_UNITY_SENTINEL).Count -gt 0) 'Passed analyzer did not produce a Unity invocation sentinel.'
    Assert (($passed.summary.steps | Where-Object name -eq 'analyzers').status -eq 'passed') 'Passed analyzer status was not recorded.'

    foreach ($allow in @($false,$true)) {
        $failed = Invoke-Fixture 'failed' -AllowLowerTier:$allow
        Assert ($failed.result.exitCode -eq 1) 'Analyzer failure did not preserve exit code 1.'
        $dependent = @($failed.summary.steps | Where-Object name -in @('unity-cli-status','editor-context','compile','console-baseline','editmode-tests','quick-validators','playmode-tests','gameplay-scenario','network-smoke'))
        Assert ($dependent.Count -eq 9) 'Failed analyzer did not record all Gameplay dependent steps.'
        Assert (@($dependent | Where-Object status -ne 'blocked').Count -eq 0) 'A Unity dependent step was not blocked after analyzer failure.'
        Assert (-not (Test-Path -LiteralPath $env:RQ_TEST_UNITY_SENTINEL)) 'Unity CLI invocation sentinel appeared after analyzer failure.'
        Assert (@($failed.summary.steps | Where-Object name -eq 'git-after').Count -eq 1) 'Blocked path omitted final Git snapshot.'
    }

    $malformed = Invoke-Fixture 'malformed'
    Assert ($malformed.result.exitCode -eq 1) 'Malformed analyzer result did not fail closed with exit 1.'
    Assert (@($malformed.summary.steps | Where-Object { $_.name -eq 'unity-cli-status' -and $_.status -eq 'blocked' }).Count -eq 1) 'Malformed analyzer output allowed Unity checks to proceed.'
    Assert (-not (Test-Path -LiteralPath $env:RQ_TEST_UNITY_SENTINEL)) 'Unity CLI invocation sentinel appeared after malformed analyzer output.'

    $unavailable = Invoke-Fixture 'not_available'
    Assert ($unavailable.result.exitCode -eq 3 -and $unavailable.result.status -eq 'not_available') ("Strict unavailable analyzer did not return not_available / exit 3: status={0} exit={1} warnings={2}" -f $unavailable.result.status, $unavailable.result.exitCode, ($unavailable.summary.warnings -join '; '))
    Assert (-not (Test-Path -LiteralPath $env:RQ_TEST_UNITY_SENTINEL)) 'Unity CLI invocation sentinel appeared after strict unavailable analyzer.'
    Assert (($unavailable.summary.steps | Where-Object name -eq 'compile').status -eq 'not_available') 'Strict unavailable analyzer did not mark compile unavailable.'
    $diagnostic = Invoke-Fixture 'not_available' -AllowLowerTier
    Assert (($diagnostic.summary.steps | Where-Object name -eq 'analyzers').status -eq 'skipped') 'Lower-tier unavailable analyzer was not marked skipped.'
    Assert (@($diagnostic.summary.steps | Where-Object name -eq 'unity-cli-status').Count -eq 1) 'Lower-tier unavailable analyzer did not continue diagnostically.'
    Assert (@(Get-Content -LiteralPath $env:RQ_TEST_UNITY_SENTINEL).Count -gt 0) 'Diagnostic unavailable analyzer did not produce a Unity invocation sentinel.'

    Write-Output 'Validation workflow analyzer gate tests passed.'
}
finally {
    if ($null -eq $originalOverride) { Remove-Item Env:RQ_UNITY_CLI_PATH -ErrorAction SilentlyContinue } else { $env:RQ_UNITY_CLI_PATH = $originalOverride }
    if ($null -eq $originalLocalAppData) { Remove-Item Env:LOCALAPPDATA -ErrorAction SilentlyContinue } else { $env:LOCALAPPDATA = $originalLocalAppData }
    if ($null -eq $originalAnalyzerStatus) { Remove-Item Env:RQ_TEST_ANALYZER_STATUS -ErrorAction SilentlyContinue } else { $env:RQ_TEST_ANALYZER_STATUS = $originalAnalyzerStatus }
    if ($null -eq $originalUnitySentinel) { Remove-Item Env:RQ_TEST_UNITY_SENTINEL -ErrorAction SilentlyContinue } else { $env:RQ_TEST_UNITY_SENTINEL = $originalUnitySentinel }
    if (Test-Path -LiteralPath $fullTestRoot) {
        $resolved = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $fullTestRoot).Path)
        if ($resolved.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and $resolved -eq $fullTestRoot) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
