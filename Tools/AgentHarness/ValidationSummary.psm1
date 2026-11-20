Set-StrictMode -Version 2.0

function Get-ValidationProperty {
    param([AllowNull()][object]$Object,[string]$Name)
    if ($null -ne $Object -and $Object.PSObject.Properties[$Name]) { return $Object.PSObject.Properties[$Name].Value }
    return $null
}

function ConvertTo-ValidationMarkdownText {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return 'Unavailable' }
    $text = [string]$Value
    $text = [regex]::Replace($text,'(?i)(password|token|secret|api[_-]?key)(\s*[:=]\s*)([^\s,;]+)','$1$2[redacted]')
    $text = $text.Replace("`r`n", '<br>').Replace("`n", '<br>').Replace("`r", '<br>')
    $text = $text.Replace('|', '\|').Replace('`', '&#96;')
    return $text
}

function Get-ValidationChildReport {
    param([object]$Step, [string]$ExpectedFileName)
    $matches = @()
    foreach ($path in @(Get-ValidationProperty $Step 'artifacts')) {
        if (-not [string]::IsNullOrWhiteSpace([string]$path) -and
            [IO.Path]::GetFileName([string]$path) -ieq $ExpectedFileName) {
            $matches += [string]$path
        }
    }
    foreach ($path in $matches) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        try {
            $report = Get-Content -LiteralPath $path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            if ($report -and $report.PSObject.Properties['schemaVersion']) {
                $stepStatus=[string](Get-ValidationProperty $Step 'status')
                $reportStatus=[string](Get-ValidationProperty $report 'status')
                $statusConflict=$stepStatus -in @('passed','failed') -and $reportStatus -in @('passed','failed') -and $stepStatus -ne $reportStatus
                return [pscustomobject]@{ report=$report; path=$path; error=$null; statusConflict=$statusConflict }
            }
            return [pscustomobject]@{ report=$null; path=$path; error='Child report is missing schemaVersion.'; statusConflict=$false }
        }
        catch {
            return [pscustomobject]@{ report=$null; path=$path; error=('Child report could not be parsed: ' + $_.Exception.Message); statusConflict=$false }
        }
    }
    $reported = @(Get-ValidationProperty $Step 'artifacts' | Where-Object { [IO.Path]::GetFileName([string]$_) -ieq $ExpectedFileName })
    if ($reported.Count -gt 0) {
        return [pscustomobject]@{ report=$null; path=[string]$reported[0]; error='Child report path is missing.'; statusConflict=$false }
    }
    return [pscustomobject]@{ report=$null; path=$null; error='Child report was not listed in the validation summary.'; statusConflict=$false }
}

function Get-ValidationStep {
    param([object]$Summary, [string]$Name)
    return ,@($Summary.steps | Where-Object { [string](Get-ValidationProperty $_ 'name') -eq $Name } | Select-Object -First 1)
}

function Get-ValidationTestCounts {
    param([object]$ReportInfo,[string]$StepStatus)
    if (-not $ReportInfo.report) { return [pscustomobject]@{ text=('Unavailable: ' + $ReportInfo.error); contradiction=$false; skipped=$null; inconclusive=$null } }
    $summary = Get-ValidationProperty $ReportInfo.report 'summary'
    $required = @('total', 'passed', 'failed', 'skipped', 'inconclusive')
    $missing = @($required | Where-Object { -not $summary -or -not $summary.PSObject.Properties[$_] })
    if ($missing.Count -gt 0) {
        return [pscustomobject]@{ text=('Unavailable: report summary missing ' + ($missing -join ', ')); contradiction=$false; skipped=$null; inconclusive=$null }
    }
    $values = @{}
    foreach ($key in $required) {
        $number = 0L
        if (-not [long]::TryParse([string]$summary.$key, [ref]$number) -or $number -lt 0) {
            return [pscustomobject]@{ text=('Unavailable: invalid ' + $key + ' count'); contradiction=$false; skipped=$null; inconclusive=$null }
        }
        $values[$key] = $number
    }
    $sum = $values.passed + $values.failed + $values.skipped + $values.inconclusive
    $reportStatus = [string](Get-ValidationProperty $ReportInfo.report 'status')
    $statusContradiction = $reportStatus -in @('passed','failed') -and $StepStatus -in @('passed','failed') -and $reportStatus -ne $StepStatus
    $detailedFailed = 0
    $detailedInconclusive = 0
    if ($ReportInfo.report.PSObject.Properties['tests']) {
        $detailedFailed = @($ReportInfo.report.tests | Where-Object { [string](Get-ValidationProperty $_ 'Status') -in @('Failed','failed') }).Count
        $detailedInconclusive = @($ReportInfo.report.tests | Where-Object { [string](Get-ValidationProperty $_ 'Status') -in @('Inconclusive','inconclusive') }).Count
    }
    $contradiction = $sum -ne $values.total -or
        ($reportStatus -eq 'passed' -and $values.failed -gt 0) -or
        ($StepStatus -eq 'passed' -and $values.failed -gt 0) -or
        $statusContradiction -or
        $detailedFailed -gt $values.failed -or
        $detailedInconclusive -gt $values.inconclusive
    $suffix = if ($contradiction) { ' **COUNT/STATUS CONTRADICTION:** categories sum to ' + $sum + '; child status ' + $reportStatus + '; parent step status ' + $StepStatus + '.' } else { '' }
    $text = 'Passed {0}; failed {1}; skipped {2}; inconclusive {3}; total {4}.{5}' -f $values.passed, $values.failed, $values.skipped, $values.inconclusive, $values.total, $suffix
    return [pscustomobject]@{ text=$text; contradiction=$contradiction; skipped=$values.skipped; inconclusive=$values.inconclusive }
}

function Get-ValidationDuration {
    param([object]$Summary)
    $start = ConvertTo-ValidationUtcInstant (Get-ValidationProperty $Summary 'startedAtUtc')
    $end = ConvertTo-ValidationUtcInstant (Get-ValidationProperty $Summary 'completedAtUtc')
    if ($null -eq $start -or $null -eq $end -or $end -lt $start) {
        return 'Unavailable'
    }
    return (($end - $start).TotalSeconds.ToString('N1',[Globalization.CultureInfo]::InvariantCulture) + ' seconds')
}

function ConvertTo-ValidationUtcInstant {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [DateTimeOffset]) { return $Value.ToUniversalTime() }
    if ($Value -is [DateTime]) {
        $date = [DateTime]$Value
        if ($date.Kind -eq [DateTimeKind]::Unspecified) { $date = [DateTime]::SpecifyKind($date,[DateTimeKind]::Utc) }
        else { $date = $date.ToUniversalTime() }
        return [DateTimeOffset]::new($date)
    }
    $parsed = [DateTimeOffset]::MinValue
    $styles = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
    if ([DateTimeOffset]::TryParse([string]$Value,[Globalization.CultureInfo]::InvariantCulture,$styles,[ref]$parsed)) { return $parsed.ToUniversalTime() }
    return $null
}

function Format-ValidationUtcTimestamp {
    param([AllowNull()][object]$Value)
    $instant=ConvertTo-ValidationUtcInstant $Value
    if($null -eq $instant){return 'Unavailable'}
    return $instant.ToString('o',[Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-UnityValidationMarkdown {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)][string]$SummaryPath)

    $resolvedPath = [IO.Path]::GetFullPath($SummaryPath)
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) { throw "Summary file was not found: '$resolvedPath'." }
    try { $summary = Get-Content -LiteralPath $resolvedPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop }
    catch { throw "Summary JSON could not be parsed: $($_.Exception.Message)" }
    if (-not $summary -or -not $summary.PSObject.Properties['schemaVersion'] -or [int]$summary.schemaVersion -ne 1) {
        throw 'Unsupported or missing validation summary schemaVersion; expected schema 1.'
    }
    foreach ($field in @('status','exitCode','steps')) {
        if (-not $summary.PSObject.Properties[$field]) { throw "Validation summary is missing required field '$field'." }
    }
    if ($summary.steps -isnot [System.Array]) { throw 'Validation summary steps must be an array.' }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('<!-- BEGIN GENERATED UNITY VALIDATION SUMMARY -->')
    $lines.Add('# Unity validation summary')
    $lines.Add('')
    $lines.Add(('**Validation status:** {0} (exit {1})' -f (ConvertTo-ValidationMarkdownText $summary.status), (ConvertTo-ValidationMarkdownText $summary.exitCode)))
    $lines.Add(('**Run:** `{0}`' -f (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $summary 'runId'))))
    $lines.Add(('**Authoritative JSON summary:** `{0}`' -f (ConvertTo-ValidationMarkdownText $resolvedPath)))
    $passedStepCount=@($summary.steps|Where-Object{[string](Get-ValidationProperty $_ 'status') -eq 'passed'}).Count
    $lines.Add(('**Step totals:** {0} passed of {1} recorded.' -f $passedStepCount,@($summary.steps).Count))
    $lines.Add(('**Tier:** requested {0}; resolved {1}' -f (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $summary 'requestedTier')), (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $summary 'resolvedTier'))))
    $lines.Add(('**Started (UTC):** {0}' -f (Format-ValidationUtcTimestamp (Get-ValidationProperty $summary 'startedAtUtc'))))
    $lines.Add(('**Completed (UTC):** {0}' -f (Format-ValidationUtcTimestamp (Get-ValidationProperty $summary 'completedAtUtc'))))
    $lines.Add(('**Duration:** {0}' -f (Get-ValidationDuration $summary)))
    $editFilter = Get-ValidationProperty $summary 'editModeFilter'
    $playFilter = Get-ValidationProperty $summary 'playModeFilter'
    $editFilterText=if($null -eq $editFilter){'Unavailable'}elseif([string]::IsNullOrWhiteSpace([string]$editFilter)){'None (unfiltered)'}else{ConvertTo-ValidationMarkdownText $editFilter}
    $playFilterText=if($null -eq $playFilter){'Unavailable'}elseif([string]::IsNullOrWhiteSpace([string]$playFilter)){'None (unfiltered)'}else{ConvertTo-ValidationMarkdownText $playFilter}
    $lines.Add(('**EditMode filter:** {0}' -f $editFilterText))
    $lines.Add(('**PlayMode filter:** {0}' -f $playFilterText))
    $lines.Add(('**Scenario:** requested {0}; resolved {1}' -f (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $summary 'requestedScenario')), (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $summary 'resolvedScenario'))))
    $lines.Add('')

    $incompleteReasons = @()
    $evidenceLimitations=@()
    $testEvidence=@{}
    if ([string]$summary.status -ne 'passed') { $incompleteReasons += ('validation run recorded status ' + [string]$summary.status) }
    if ($editFilter -or $playFilter) { $incompleteReasons += 'one or more test suites were filtered' }
    foreach ($testSpec in @(@{name='editmode-tests';title='EditMode'},@{name='playmode-tests';title='PlayMode'})) {
        $testStepRecord=Get-ValidationStep $summary $testSpec.name
        $testNotRequired=$testSpec.name -eq 'playmode-tests' -and [string](Get-ValidationProperty $summary 'resolvedTier') -eq 'Fast' -and $testStepRecord.Count -eq 0
        $testReportInfo=if($testStepRecord.Count -gt 0){Get-ValidationChildReport $testStepRecord[0] ('unity-'+$testSpec.name+'-report.json')}else{[pscustomobject]@{report=$null;path=$null;error='Validation step is missing.';statusConflict=$false}}
        $testStepStatus=if($testStepRecord.Count -gt 0){[string](Get-ValidationProperty $testStepRecord[0] 'status')}else{'missing'}
        $testCounts=if($testNotRequired){[pscustomobject]@{text='Not run; not required by resolved tier Fast.';contradiction=$false;skipped=0;inconclusive=0;notRequired=$true}}else{Get-ValidationTestCounts $testReportInfo $testStepStatus}
        $testEvidence[$testSpec.name]=@{step=$testStepRecord;info=$testReportInfo;counts=$testCounts;title=$testSpec.title}
        if($testNotRequired){ }
        elseif($null -eq $testCounts.skipped){$evidenceLimitations+=($testSpec.title+': test counts unavailable');$incompleteReasons+=($testSpec.title+' test counts unavailable')}
        elseif($testCounts.skipped -gt 0){$incompleteReasons+=($testSpec.title+' has '+$testCounts.skipped+' skipped tests');$evidenceLimitations+=($testSpec.title+' skipped '+$testCounts.skipped+' tests')}
        if($testNotRequired){ }
        elseif($null -eq $testCounts.inconclusive){$evidenceLimitations+=($testSpec.title+': inconclusive count unavailable');$incompleteReasons+=($testSpec.title+' inconclusive count unavailable')}
        elseif($testCounts.inconclusive -gt 0){$incompleteReasons+=($testSpec.title+' has '+$testCounts.inconclusive+' inconclusive tests');$evidenceLimitations+=($testSpec.title+' inconclusive '+$testCounts.inconclusive+' tests')}
        if(-not $testNotRequired -and -not $testReportInfo.report){$evidenceLimitations+=($testSpec.title+': '+$testReportInfo.error);$incompleteReasons+=($testSpec.title+' report unavailable')}
        if($testCounts.contradiction){$evidenceLimitations+=($testSpec.title+': child report count/status contradiction')}
    }
    foreach ($step in @($summary.steps)) {
        $stepStatus=[string](Get-ValidationProperty $step 'status')
        if ($stepStatus -in @('skipped','not_available','blocked')) { $incompleteReasons += ("step '{0}' is {1}" -f (Get-ValidationProperty $step 'name'), $stepStatus) }
    }
    $fullPlaceholders = @($summary.steps | Where-Object { [string](Get-ValidationProperty $_ 'name') -in @('all-tests','scene-prefab-validation','expanded-multiplayer','coverage','windows-build') -and [string](Get-ValidationProperty $_ 'status') -ne 'passed' })
    if ([string](Get-ValidationProperty $summary 'resolvedTier') -eq 'Full' -and $fullPlaceholders.Count -gt 0) { $incompleteReasons += 'Full-tier checks are placeholders or unavailable' }
    if ($incompleteReasons.Count -gt 0) {
        $lines.Add('> **Acceptance is incomplete:** ' + (ConvertTo-ValidationMarkdownText (($incompleteReasons | Sort-Object -Unique) -join '; ')) + '.')
    }
    else {
        $lines.Add('> All checks represented by this run are recorded below. Manual acceptance is not represented by this report.')
    }
    $lines.Add('')

    $warnings=Get-ValidationProperty $summary 'warnings'
    if($null -ne $warnings -and @($warnings).Count -gt 0){
        $lines.Add('## Preflight warnings')
        $lines.Add('')
        $shownWarnings=@($warnings|Select-Object -First 20)
        foreach($warning in $shownWarnings){
            $warningText=[string]$warning
            if($warningText.Length -gt 1000){$warningText=$warningText.Substring(0,1000)+'...'}
            $lines.Add('- '+(ConvertTo-ValidationMarkdownText $warningText))
        }
        if(@($warnings).Count -gt $shownWarnings.Count){$lines.Add(('- {0} additional warning(s) omitted.' -f (@($warnings).Count-$shownWarnings.Count)))}
        $lines.Add('')
    }

    $lines.Add('## Validation steps')
    $lines.Add('')
    foreach ($step in @($summary.steps)) {
        $lines.Add(('### {0} — {1}' -f (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $step 'name')), (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $step 'status'))))
        $lines.Add('')
        $lines.Add((ConvertTo-ValidationMarkdownText (Get-ValidationProperty $step 'message')))
        $lines.Add('')
        $sourceReports = @(Get-ValidationProperty $step 'artifacts' | Where-Object { [IO.Path]::GetExtension([string]$_) -ieq '.json' })
        if ($sourceReports.Count -gt 0) {
            $lines.Add('Source report paths:')
            foreach ($path in $sourceReports) { $lines.Add('- `' + (ConvertTo-ValidationMarkdownText $path) + '`') }
            $lines.Add('')
        }
    }

    $nonPassed = @($summary.steps | Where-Object { [string](Get-ValidationProperty $_ 'status') -ne 'passed' })
    $lines.Add('## Non-passed steps')
    $lines.Add('')
    if ($nonPassed.Count -eq 0) { $lines.Add('- None.') }
    else {
        foreach ($step in $nonPassed) {
            $lines.Add(('- **{0} — {1}:** {2}' -f (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $step 'name')), (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $step 'status')), (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $step 'message'))))
        }
    }
    $lines.Add('')

    $lines.Add('## Test and diagnostic details')
    $lines.Add('')
    foreach ($testStep in @(@{name='editmode-tests';title='EditMode'}, @{name='playmode-tests';title='PlayMode'})) {
        $evidence=$testEvidence[$testStep.name]
        $info=$evidence.info
        $counts=$evidence.counts
        $lines.Add(('**{0} tests:** {1}' -f $testStep.title, (ConvertTo-ValidationMarkdownText $counts.text)))
        if ($info.report) {
            $testFailures=@()
            if ($info.report.PSObject.Properties['tests']) {
                $testFailures=@($info.report.tests | Where-Object { [string](Get-ValidationProperty $_ 'Status') -in @('Failed','Inconclusive','failed','inconclusive') } | Select-Object -First 10)
            }
            if ($testFailures.Count -gt 0) {
                $lines.Add('')
                $lines.Add(('**{0} failed or inconclusive test details (up to 10):**' -f $testStep.title))
                foreach($test in $testFailures) {
                    $name=Get-ValidationProperty $test 'FullName'
                    if(-not $name){$name=Get-ValidationProperty $test 'name'}
                    $message=[string](Get-ValidationProperty $test 'Message')
                    if($message.Length -gt 500){$message=$message.Substring(0,500)+'...'}
                    $lines.Add(('- `{0}` — {1}: {2}' -f (ConvertTo-ValidationMarkdownText $name),(ConvertTo-ValidationMarkdownText (Get-ValidationProperty $test 'Status')),(ConvertTo-ValidationMarkdownText $message)))
                }
            }
            elseif ($counts.text -match 'failed [1-9][0-9]*|inconclusive [1-9][0-9]*') {
                $evidenceLimitations += ($testStep.title + ': detailed failed/inconclusive test records are unavailable')
            }
        }
    }

    $analyzerStep = Get-ValidationStep $summary 'analyzers'
    $analyzerInfo = if ($analyzerStep.Count -gt 0) { Get-ValidationChildReport $analyzerStep[0] 'analyzer-report.json' } else { [pscustomobject]@{report=$null;path=$null;error='Analyzer step is missing.'} }
    if ($analyzerInfo.report) {
        $ar = $analyzerInfo.report
        $counts = Get-ValidationProperty $ar 'counts'
        $diagnostics = Get-ValidationProperty $ar 'diagnostics'
        $newCount = if ($diagnostics -and $diagnostics.PSObject.Properties['new']) { @($diagnostics.PSObject.Properties['new'].Value).Count } else { $null }
        $current = if ($counts -and $counts.PSObject.Properties['blocking'] -and $counts.PSObject.Properties['informational'] -and $counts.PSObject.Properties['total']) { '{0} blocking, {1} informational across {2} current diagnostics' -f $counts.blocking, $counts.informational, $counts.total } else { 'Unavailable: current diagnostic totals are missing.' }
        $newText = if ($null -ne $newCount) { [string]$newCount } else { 'Unavailable: diagnostics.new is missing.' }
        $baselineInfo = Get-ValidationProperty $ar 'baseline'
        $baseline = if ($baselineInfo -and $baselineInfo.PSObject.Properties['occurrences']) { [string]$baselineInfo.occurrences } else { 'Unavailable' }
        $lines.Add(('**Analyzer current totals:** {0}.' -f (ConvertTo-ValidationMarkdownText $current)))
        $lines.Add(('**New analyzer diagnostics:** {0}. **Baseline occurrences:** {1}. These are distinct measures.' -f (ConvertTo-ValidationMarkdownText $newText), (ConvertTo-ValidationMarkdownText $baseline)))
    }
    else { $lines.Add(('**Analyzer totals:** Unavailable: {0}' -f (ConvertTo-ValidationMarkdownText $analyzerInfo.error))) }
    if(-not $analyzerInfo.report){$evidenceLimitations += ('Analyzer: '+$analyzerInfo.error)}
    if((Get-ValidationProperty $analyzerInfo 'statusConflict')){$evidenceLimitations += 'Analyzer: child report status disagrees with the parent step status.'}

    foreach ($diagnosticSpec in @(@{step='compile';file='unity-compile-report.json';title='Compile'},@{step='console-baseline';file='unity-console-report.json';title='Console'})) {
        $step = Get-ValidationStep $summary $diagnosticSpec.step
        $info = if ($step.Count -gt 0) { Get-ValidationChildReport $step[0] $diagnosticSpec.file } else { [pscustomobject]@{report=$null;path=$null;error='Validation step is missing.'} }
        if (-not $info.report) { $lines.Add(('**{0} details:** Unavailable: {1}' -f $diagnosticSpec.title, (ConvertTo-ValidationMarkdownText $info.error))); $evidenceLimitations += ($diagnosticSpec.title+': '+$info.error); continue }
        if($info.statusConflict){$evidenceLimitations += ($diagnosticSpec.title+': child report status disagrees with the parent step status')}
        if ($diagnosticSpec.step -eq 'compile') {
            $errorValues=Get-ValidationProperty $info.report 'errors'
            $errorCount = if ($info.report.PSObject.Properties['errors']) { @($errorValues).Count } else { 'Unavailable: report errors field is missing' }
            $lines.Add(('**Compile details:** report status {0}; reported errors {1}.' -f (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $info.report 'status')), $errorCount))
        }
        else {
            $console = $info.report
            $consoleDiagnostics=Get-ValidationProperty $console 'diagnostics'
            $currentCount = if ($consoleDiagnostics -and $consoleDiagnostics.PSObject.Properties['current']) { @($consoleDiagnostics.PSObject.Properties['current'].Value).Count } else { $null }
            $newConsoleCount = if ($consoleDiagnostics -and $consoleDiagnostics.PSObject.Properties['new']) { @($consoleDiagnostics.PSObject.Properties['new'].Value).Count } else { $null }
            $currentText = if ($null -ne $currentCount) { [string]$currentCount } else { 'Unavailable' }
            $newConsoleText = if ($null -ne $newConsoleCount) { [string]$newConsoleCount } else { 'Unavailable' }
            $totalText = if ($console.PSObject.Properties['total']) { [string]$console.total } else { 'Unavailable' }
            $lines.Add(('**Console details:** report status {0}; returned {1} of {2}; current diagnostics {3}; new {4}.' -f (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $console 'status')), (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $console 'returned')), (ConvertTo-ValidationMarkdownText $totalText), $currentText, $newConsoleText))
        }
    }

    $networkStep = Get-ValidationStep $summary 'network-smoke'
    if ($networkStep.Count -gt 0) {
        $networkInfo = Get-ValidationChildReport $networkStep[0] 'unity-network-smoke-report.json'
        $lines.Add('')
        if (-not $networkInfo.report) { $lines.Add(('**Network smoke:** Unavailable: {0}' -f (ConvertTo-ValidationMarkdownText $networkInfo.error))); $evidenceLimitations += ('Network smoke: '+$networkInfo.error) }
        else {
            $nr = $networkInfo.report
            if($networkInfo.statusConflict){$evidenceLimitations += 'Network smoke: child report status disagrees with the parent step status.'}
            $topology = Get-ValidationProperty $nr 'topology'
            $remoteValidated = Get-ValidationProperty $nr 'remoteClientValidated'
            $limitations = if ($nr.PSObject.Properties['knownLimitations']) { @($nr.PSObject.Properties['knownLimitations'].Value) -join '; ' } else { 'Unavailable: field missing' }
            $lines.Add(('**Network smoke topology:** {0}; peer count {1}; remote client validated {2}; relay flow invoked {3}.' -f (ConvertTo-ValidationMarkdownText $topology), (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $nr 'peerCount')), (ConvertTo-ValidationMarkdownText $remoteValidated), (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $nr 'relayFlowInvoked'))))
            $lines.Add(('**Transport:** {0}. **Known limitations:** {1}' -f (ConvertTo-ValidationMarkdownText (Get-ValidationProperty $nr 'transport')), (ConvertTo-ValidationMarkdownText $limitations)))
            if ([string]$topology -eq 'host-only' -or $remoteValidated -ne $true) {
                $lines.Add('> This run provides host-only coverage; it does not establish successful remote-client validation.')
            }
        }
    }

    $lines.Add('')
    $lines.Add('## Evidence limitations')
    $lines.Add('')
    if($evidenceLimitations.Count -eq 0){$lines.Add('- None detected in the referenced child reports.')}
    else{foreach($limitation in @($evidenceLimitations|Sort-Object -Unique)){$lines.Add('- '+(ConvertTo-ValidationMarkdownText $limitation))}}

    $lines.Add('')
    $lines.Add('<!-- END GENERATED UNITY VALIDATION SUMMARY -->')
    return ($lines -join "`r`n") + "`r`n"
}

function Write-UnityValidationMarkdown {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)][string]$SummaryPath,[Parameter(Mandatory=$true)][string]$OutputPath)
    $markdown = ConvertTo-UnityValidationMarkdown -SummaryPath $SummaryPath
    $destination = [IO.Path]::GetFullPath($OutputPath)
    if ([IO.Path]::GetExtension($destination) -ine '.md' -or [IO.Path]::GetFileName($destination) -ieq 'AGENTS.md') {
        throw 'Markdown output must use a .md extension and may not target AGENTS.md.'
    }
    if ([string]::Equals($destination,[IO.Path]::GetFullPath($SummaryPath),[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Markdown output path may not replace its source summary.'
    }
    $directory = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Output directory does not exist: '$directory'." }
    $cursor=$directory
    while($cursor){
        if(Test-Path -LiteralPath $cursor){$item=Get-Item -LiteralPath $cursor -Force;if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "Markdown output path contains a reparse point: '$cursor'."}}
        $parent=Split-Path -Parent $cursor
        if(-not $parent -or $parent -eq $cursor){break}
        $cursor=$parent
    }
    if (Test-Path -LiteralPath $destination) {
        $destinationItem=Get-Item -LiteralPath $destination -Force
        if (($destinationItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Markdown output target is a reparse point: '$destination'." }
    }
    if (Test-Path -LiteralPath $destination -PathType Leaf) {
        $existing=Get-Content -LiteralPath $destination -Raw -ErrorAction Stop
        if (-not $existing.Contains('<!-- BEGIN GENERATED UNITY VALIDATION SUMMARY -->') -or -not $existing.Contains('<!-- END GENERATED UNITY VALIDATION SUMMARY -->')) {
            throw "Refusing to replace non-generated Markdown: '$destination'."
        }
    }
    $temporary = Join-Path $directory ('.validation-summary-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporary, $markdown, [Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $destination -PathType Leaf) {
            $backup = Join-Path $directory ('.validation-summary-' + [Guid]::NewGuid().ToString('N') + '.bak')
            try { [IO.File]::Replace($temporary, $destination, $backup); Remove-Item -LiteralPath $backup -Force }
            catch { if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue }; throw }
        }
        else { [IO.File]::Move($temporary, $destination) }
    }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue } }
    return $destination
}

Export-ModuleMember -Function ConvertTo-UnityValidationMarkdown,Write-UnityValidationMarkdown
