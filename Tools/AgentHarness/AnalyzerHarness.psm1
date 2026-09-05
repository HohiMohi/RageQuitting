Set-StrictMode -Version 2.0

$script:AnalyzerHarnessVersion = '1.0.0'

function Get-AHProjectRoot {
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
}

function Resolve-AHPath {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot, [string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $Path))
}

function Read-AHJsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    return Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
}

function Write-AHJsonFile {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)]$Value)

    $directory = Split-Path -Parent $Path
    if ($directory) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $json = ConvertTo-Json -InputObject $Value -Depth 12
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

function Get-AHStringHash {
    param([Parameter(Mandatory = $true)][string]$Value)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
        return (($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join '')
    }
    finally {
        $sha.Dispose()
    }
}

function ConvertTo-AHProjectPath {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot, [Parameter(Mandatory = $true)][string]$Path)

    $candidate = $Path.Trim().Trim('"')
    try {
        $fullPath = if ([System.IO.Path]::IsPathRooted($candidate)) {
            [System.IO.Path]::GetFullPath($candidate)
        }
        else {
            [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $candidate))
        }

        $rootWithSeparator = $ProjectRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
        if ($fullPath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $fullPath.Substring($rootWithSeparator.Length).Replace('\', '/').ToLowerInvariant()
        }
        return $fullPath.Replace('\', '/').ToLowerInvariant()
    }
    catch {
        return $candidate.Replace('\', '/').ToLowerInvariant()
    }
}

function ConvertTo-AHMessage {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot, [Parameter(Mandatory = $true)][string]$Message)

    $normalized = [regex]::Replace($Message.Trim(), '\s+', ' ')
    $escapedRoot = [regex]::Escape($ProjectRoot.TrimEnd('\', '/'))
    $normalized = [regex]::Replace($normalized, $escapedRoot, '<project>', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    return $normalized.Replace('\', '/').ToLowerInvariant()
}

function New-AHFingerprint {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Message
    )

    return Get-AHStringHash -Value (($Id.ToUpperInvariant(), $Path, $Message) -join '|')
}

function New-AHCheck {
    param([string]$Name, [bool]$Passed, [string]$Message)
    return [ordered]@{ name = $Name; passed = $Passed; message = $Message }
}

function Get-AHCountSum {
    param([object[]]$Entries = @())

    $sum = 0
    foreach ($entry in @($Entries)) {
        $sum += [int]$entry.count
    }
    return [int]$sum
}

function Test-AHIntegrity {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)

    $checks = @()
    $issues = @()
    $manifest = $null
    $catalog = $null
    $baseline = $null
    $paths = [ordered]@{
        projectVersion = Join-Path $ProjectRoot 'ProjectSettings\ProjectVersion.txt'
        packageManifest = Join-Path $ProjectRoot 'Packages\manifest.json'
        analyzerManifest = Join-Path $ProjectRoot 'Tools\AgentHarness\analyzer-package.json'
        catalog = Join-Path $ProjectRoot 'Tools\AgentHarness\analyzer-rules.json'
        baseline = Join-Path $ProjectRoot 'Tools\AgentHarness\analyzer-baseline.json'
        assembly = Join-Path $ProjectRoot 'Assets\Analyzers\Microsoft.Unity.Analyzers.dll'
        ruleset = Join-Path $ProjectRoot 'Assets\Default.ruleset'
        runtimeProject = Join-Path $ProjectRoot 'Assembly-CSharp.csproj'
        editorProject = Join-Path $ProjectRoot 'Assembly-CSharp-Editor.csproj'
    }

    foreach ($entry in $paths.GetEnumerator()) {
        $exists = Test-Path -LiteralPath $entry.Value -PathType Leaf
        $check = New-AHCheck -Name ("file:{0}" -f $entry.Key) -Passed $exists -Message $(if ($exists) { $entry.Value } else { "Missing required file: $($entry.Value)" })
        $checks += $check
        if (-not $exists) { $issues += $check.message }
    }

    if ($issues.Count -eq 0) {
        try { $manifest = Read-AHJsonFile -Path $paths.analyzerManifest } catch { $issues += "Invalid analyzer-package.json: $($_.Exception.Message)" }
        try { $catalog = Read-AHJsonFile -Path $paths.catalog } catch { $issues += "Invalid analyzer-rules.json: $($_.Exception.Message)" }
        try { $baseline = Read-AHJsonFile -Path $paths.baseline } catch { $issues += "Invalid analyzer-baseline.json: $($_.Exception.Message)" }
    }

    if ($manifest) {
        $manifestShape =
            [string]$manifest.packageId -eq 'Microsoft.Unity.Analyzers' -and
            [string]$manifest.version -eq '1.27.0' -and
            -not [string]::IsNullOrWhiteSpace([string]$manifest.packageSha256) -and
            -not [string]::IsNullOrWhiteSpace([string]$manifest.assemblySha256)
        $checks += New-AHCheck -Name 'manifest-shape' -Passed $manifestShape -Message $(if ($manifestShape) { 'Analyzer package manifest is complete.' } else { 'Analyzer package manifest is incomplete or has an unexpected version.' })
        if (-not $manifestShape) { $issues += 'Analyzer package manifest is incomplete or has an unexpected version.' }

        if (Test-Path -LiteralPath $paths.assembly -PathType Leaf) {
            $actualHash = (Get-FileHash -LiteralPath $paths.assembly -Algorithm SHA256).Hash.ToLowerInvariant()
            $hashMatches = $actualHash -eq ([string]$manifest.assemblySha256).ToLowerInvariant()
            $checks += New-AHCheck -Name 'assembly-sha256' -Passed $hashMatches -Message "Expected $($manifest.assemblySha256); actual $actualHash."
            if (-not $hashMatches) { $issues += 'Analyzer DLL SHA-256 does not match analyzer-package.json.' }
        }
    }

    $ruleById = @{}
    if ($catalog) {
        foreach ($rule in @($catalog.rules)) {
            $id = [string]$rule.id
            if (-not $ruleById.ContainsKey($id)) { $ruleById[$id] = $rule }
        }

        $expectedIds = @(1..43 | ForEach-Object { 'UNT{0:D4}' -f $_ })
        $actualIds = @($ruleById.Keys | Sort-Object)
        $missingIds = @($expectedIds | Where-Object { -not $ruleById.ContainsKey($_) })
        $extraIds = @($actualIds | Where-Object { $expectedIds -notcontains $_ })
        $duplicateCount = @($catalog.rules).Count - $actualIds.Count
        $catalogComplete = $missingIds.Count -eq 0 -and $extraIds.Count -eq 0 -and $duplicateCount -eq 0
        $checks += New-AHCheck -Name 'catalog-completeness' -Passed $catalogComplete -Message "Rules=$($actualIds.Count), missing=$($missingIds -join ','), extra=$($extraIds -join ','), duplicates=$duplicateCount."
        if (-not $catalogComplete) { $issues += 'Analyzer rule catalog is not exactly UNT0001 through UNT0043.' }

        $allowedCategories = @('Performance', 'Correctness', 'Type Safety', 'Readability')
        $policyMismatch = @($catalog.rules | Where-Object {
            $expectedPolicy = if ($_.category -in @('Correctness', 'Type Safety')) { 'blockOnNew' } else { 'informational' }
            $_.category -notin $allowedCategories -or [string]$_.policy -ne $expectedPolicy
        })
        $policyValid = $policyMismatch.Count -eq 0
        $checks += New-AHCheck -Name 'catalog-policy' -Passed $policyValid -Message $(if ($policyValid) { 'All catalog categories and policies are valid.' } else { 'Catalog contains invalid category or policy assignments.' })
        if (-not $policyValid) { $issues += 'Analyzer rule catalog contains invalid category or policy assignments.' }
    }

    if ($catalog -and (Test-Path -LiteralPath $paths.ruleset -PathType Leaf)) {
        try {
            [xml]$rulesetXml = Get-Content -LiteralPath $paths.ruleset -Raw
            $rulesetRules = @($rulesetXml.SelectNodes("//*[local-name()='Rule']"))
            $rulesetById = @{}
            foreach ($node in $rulesetRules) { $rulesetById[[string]$node.Id] = [string]$node.Action }
            $rulesetIssues = @()
            foreach ($id in $ruleById.Keys) {
                $expectedAction = if ([string]$ruleById[$id].policy -eq 'blockOnNew') { 'Warning' } else { 'Info' }
                if (-not $rulesetById.ContainsKey($id) -or $rulesetById[$id] -ne $expectedAction) {
                    $rulesetIssues += "$id expected $expectedAction"
                }
            }
            foreach ($id in $rulesetById.Keys) {
                if (-not $ruleById.ContainsKey($id)) { $rulesetIssues += "$id is not in analyzer-rules.json" }
            }
            $rulesetValid = $rulesetIssues.Count -eq 0 -and $rulesetRules.Count -eq 43
            $checks += New-AHCheck -Name 'ruleset-policy' -Passed $rulesetValid -Message $(if ($rulesetValid) { 'Default.ruleset contains 43 correctly classified UNT rules.' } else { $rulesetIssues -join '; ' })
            if (-not $rulesetValid) { $issues += 'Default.ruleset does not match analyzer-rules.json.' }
        }
        catch {
            $checks += New-AHCheck -Name 'ruleset-policy' -Passed $false -Message $_.Exception.Message
            $issues += "Default.ruleset is invalid XML: $($_.Exception.Message)"
        }
    }

    if ($baseline) {
        $baselineValid = [int]$baseline.schemaVersion -eq 1 -and [string]$baseline.packageId -eq 'Microsoft.Unity.Analyzers' -and [string]$baseline.version -eq '1.27.0'
        $baselineEntriesValid = @($baseline.diagnostics | Where-Object {
            [string]::IsNullOrWhiteSpace([string]$_.fingerprint) -or [int]$_.count -lt 1 -or [string]$_.category -notin @('Correctness', 'Type Safety')
        }).Count -eq 0
        $baselineValid = $baselineValid -and $baselineEntriesValid
        $checks += New-AHCheck -Name 'baseline-shape' -Passed $baselineValid -Message $(if ($baselineValid) { 'Analyzer baseline schema is valid.' } else { 'Analyzer baseline contains invalid metadata or non-blocking entries.' })
        if (-not $baselineValid) { $issues += 'Analyzer baseline is invalid.' }
    }

    foreach ($projectEntry in @(
        [ordered]@{ key = 'runtime'; path = $paths.runtimeProject; name = 'Assembly-CSharp.csproj' },
        [ordered]@{ key = 'editor'; path = $paths.editorProject; name = 'Assembly-CSharp-Editor.csproj' }
    )) {
        if (-not (Test-Path -LiteralPath $projectEntry.path -PathType Leaf)) { continue }
        try {
            [xml]$projectXml = Get-Content -LiteralPath $projectEntry.path -Raw
            $analyzerNodes = @($projectXml.SelectNodes("//*[local-name()='Analyzer']"))
            $targetNodes = @($analyzerNodes | Where-Object {
                $include = [string]$_.Include
                [System.IO.Path]::GetFileName($include.Replace('/', '\')) -ieq 'Microsoft.Unity.Analyzers.dll'
            })
            $localAssemblyPath = [System.IO.Path]::GetFullPath($paths.assembly)
            $localNodes = @($targetNodes | Where-Object {
                $resolved = Resolve-AHPath -ProjectRoot $ProjectRoot -Path ([string]$_.Include)
                $resolved -and $resolved -eq $localAssemblyPath
            })
            $analyzerReferencesValid = $targetNodes.Count -eq 1 -and $localNodes.Count -eq 1
            $checks += New-AHCheck -Name ("project-{0}-analyzer-reference" -f $projectEntry.key) -Passed $analyzerReferencesValid -Message "$($projectEntry.name): Microsoft.Unity.Analyzers references=$($targetNodes.Count), local=$($localNodes.Count)."
            if (-not $analyzerReferencesValid) { $issues += "$($projectEntry.name) must contain exactly one project-local Microsoft.Unity.Analyzers reference and no external copy." }

            $rulesetNodes = @($projectXml.SelectNodes("//*[local-name()='CodeAnalysisRuleSet']"))
            $localRulesetPath = [System.IO.Path]::GetFullPath($paths.ruleset)
            $matchingRulesets = @($rulesetNodes | Where-Object { (Resolve-AHPath -ProjectRoot $ProjectRoot -Path ([string]$_.InnerText)) -eq $localRulesetPath })
            $rulesetImported = $rulesetNodes.Count -eq 1 -and $matchingRulesets.Count -eq 1
            $checks += New-AHCheck -Name ("project-{0}-ruleset-reference" -f $projectEntry.key) -Passed $rulesetImported -Message "$($projectEntry.name): ruleset references=$($rulesetNodes.Count), local Default.ruleset=$($matchingRulesets.Count)."
            if (-not $rulesetImported) { $issues += "$($projectEntry.name) must import exactly one project-local Assets/Default.ruleset and no external ruleset." }
        }
        catch {
            $checks += New-AHCheck -Name ("project-{0}-xml" -f $projectEntry.key) -Passed $false -Message $_.Exception.Message
            $issues += "$($projectEntry.name) is invalid XML: $($_.Exception.Message)"
        }
    }

    return [ordered]@{
        healthy = $issues.Count -eq 0
        checks = @($checks)
        issues = @($issues | ForEach-Object { [string]$_ })
        manifest = $manifest
        catalog = $catalog
        baseline = $baseline
        paths = $paths
        ruleById = $ruleById
    }
}

function Invoke-AHDotnetBuild {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$ArtifactDirectory
    )

    $stdoutPath = Join-Path $ArtifactDirectory 'dotnet-build.stdout.txt'
    $stderrPath = Join-Path $ArtifactDirectory 'dotnet-build.stderr.txt'
    $projectName = [System.IO.Path]::GetFileName($ProjectPath)
    $commandText = "dotnet build `"$projectName`" --no-restore --no-incremental --nologo --verbosity:minimal"
    $dotnetCommand = Get-Command 'dotnet' -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) {
        [System.IO.File]::WriteAllText($stdoutPath, '', [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($stderrPath, 'dotnet was not found on PATH.' + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
        return [ordered]@{ available = $false; exitCode = $null; command = $commandText; stdoutPath = $stdoutPath; stderrPath = $stderrPath; error = 'dotnet was not found on PATH.' }
    }

    Push-Location -LiteralPath $ProjectRoot
    try {
        $oldPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            & $dotnetCommand.Source 'build' $ProjectPath '--no-restore' '--no-incremental' '--nologo' '--verbosity:minimal' 1> $stdoutPath 2> $stderrPath
            $exitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $oldPreference
        }
    }
    finally {
        Pop-Location
    }

    return [ordered]@{ available = $true; exitCode = [int]$exitCode; command = $commandText; stdoutPath = $stdoutPath; stderrPath = $stderrPath; error = $null }
}

function Get-AHBuildDiagnostics {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)]$RuleById,
        [Parameter(Mandatory = $true)][string[]]$Paths
    )

    $lines = @()
    foreach ($path in $Paths) {
        if (Test-Path -LiteralPath $path -PathType Leaf) { $lines += @(Get-Content -LiteralPath $path -ErrorAction SilentlyContinue) }
    }

    $pattern = [regex]'^(?<file>.+)\((?<line>\d+),(?<column>\d+)\):\s*\S+\s+(?<id>(?:UNT|AD|CS)\d{4})\s*:\s*(?<message>.*)$'
    $seenLocations = @{}
    $untDiagnostics = @()
    $compilerDiagnostics = @()

    foreach ($lineText in $lines) {
        $match = $pattern.Match([string]$lineText)
        if (-not $match.Success) { continue }

        $id = $match.Groups['id'].Value.ToUpperInvariant()
        $message = [regex]::Replace($match.Groups['message'].Value, '\s+\[[^\]]+\.csproj\]\s*$', '')
        $path = ConvertTo-AHProjectPath -ProjectRoot $ProjectRoot -Path $match.Groups['file'].Value
        $normalizedMessage = ConvertTo-AHMessage -ProjectRoot $ProjectRoot -Message $message
        $lineNumber = [int]$match.Groups['line'].Value
        $columnNumber = [int]$match.Groups['column'].Value
        $locationKey = ($id, $path, $lineNumber, $columnNumber, $normalizedMessage) -join '|'
        if ($seenLocations.ContainsKey($locationKey)) { continue }
        $seenLocations[$locationKey] = $true

        if ($id -like 'UNT*') {
            $rule = if ($RuleById.ContainsKey($id)) { $RuleById[$id] } else { $null }
            $category = if ($rule) { [string]$rule.category } else { 'Unknown' }
            $policy = if ($rule) { [string]$rule.policy } else { 'blockOnNew' }
            $untDiagnostics += [ordered]@{
                fingerprint = New-AHFingerprint -Id $id -Path $path -Message $normalizedMessage
                id = $id
                path = $path
                line = $lineNumber
                column = $columnNumber
                message = $normalizedMessage
                category = $category
                policy = $policy
            }
        }
        else {
            $compilerDiagnostics += [ordered]@{ id = $id; path = $path; line = $lineNumber; column = $columnNumber; message = $normalizedMessage }
        }
    }

    $aggregates = @{}
    foreach ($diagnostic in $untDiagnostics) {
        $fingerprint = [string]$diagnostic.fingerprint
        if (-not $aggregates.ContainsKey($fingerprint)) {
            $aggregates[$fingerprint] = [ordered]@{
                fingerprint = $fingerprint
                id = [string]$diagnostic.id
                path = [string]$diagnostic.path
                message = [string]$diagnostic.message
                category = [string]$diagnostic.category
                policy = [string]$diagnostic.policy
                count = 0
                locations = @()
            }
        }
        $aggregates[$fingerprint].count = [int]$aggregates[$fingerprint].count + 1
        $aggregates[$fingerprint].locations += [ordered]@{ line = [int]$diagnostic.line; column = [int]$diagnostic.column }
    }

    $loadFailureLines = @($lines | Where-Object { $_ -match '\bCS8032\b|\bAD\d{4}\b|(?i:analy[sz]er).*(?i:failed|failure|exception|could not be loaded|cannot be loaded)' } | Sort-Object -Unique)
    return [ordered]@{
        current = @($aggregates.Values | Sort-Object id, path, fingerprint)
        compilerDiagnostics = @($compilerDiagnostics)
        loadFailureLines = @($loadFailureLines | ForEach-Object { [string]$_ })
        rawUntCount = $untDiagnostics.Count
    }
}

function Compare-AHBaseline {
    param([object[]]$CurrentBlocking = @(), [object[]]$BaselineEntries = @())

    $currentMap = @{}
    foreach ($entry in @($CurrentBlocking)) { $currentMap[[string]$entry.fingerprint] = $entry }
    $baselineMap = @{}
    foreach ($entry in @($BaselineEntries)) { $baselineMap[[string]$entry.fingerprint] = $entry }
    $newDiagnostics = @()
    $resolvedDiagnostics = @()

    foreach ($fingerprint in $currentMap.Keys) {
        $current = $currentMap[$fingerprint]
        $baselineCount = if ($baselineMap.ContainsKey($fingerprint)) { [int]$baselineMap[$fingerprint].count } else { 0 }
        if ([int]$current.count -gt $baselineCount) {
            $newDiagnostics += [ordered]@{
                fingerprint = $fingerprint; id = [string]$current.id; path = [string]$current.path; message = [string]$current.message
                category = [string]$current.category; baselineCount = $baselineCount; currentCount = [int]$current.count; delta = [int]$current.count - $baselineCount
            }
        }
    }

    foreach ($fingerprint in $baselineMap.Keys) {
        $baseline = $baselineMap[$fingerprint]
        $currentCount = if ($currentMap.ContainsKey($fingerprint)) { [int]$currentMap[$fingerprint].count } else { 0 }
        if ($currentCount -lt [int]$baseline.count) {
            $resolvedDiagnostics += [ordered]@{
                fingerprint = $fingerprint; id = [string]$baseline.id; path = [string]$baseline.path; message = [string]$baseline.message
                category = [string]$baseline.category; baselineCount = [int]$baseline.count; currentCount = $currentCount; delta = [int]$baseline.count - $currentCount
            }
        }
    }

    return [ordered]@{ new = @($newDiagnostics | Sort-Object id, path, fingerprint); resolved = @($resolvedDiagnostics | Sort-Object id, path, fingerprint) }
}

function New-AHBaselineRecord {
    param([Parameter(Mandatory = $true)]$Diagnostic, [int]$Count = -1)
    return [ordered]@{
        fingerprint = [string]$Diagnostic.fingerprint
        id = [string]$Diagnostic.id
        path = [string]$Diagnostic.path
        message = [string]$Diagnostic.message
        category = [string]$Diagnostic.category
        count = if ($Count -ge 0) { $Count } else { [int]$Diagnostic.count }
    }
}

function Update-AHBaselineEntries {
    param(
        [object[]]$Existing = @(),
        [object[]]$CurrentBlocking = @(),
        [Parameter(Mandatory = $true)][ValidateSet('PruneResolved', 'AcceptNew')][string]$Mode
    )

    $existingMap = @{}
    foreach ($entry in @($Existing)) { $existingMap[[string]$entry.fingerprint] = $entry }
    $currentMap = @{}
    foreach ($entry in @($CurrentBlocking)) { $currentMap[[string]$entry.fingerprint] = $entry }
    $result = @()

    if ($Mode -eq 'PruneResolved') {
        foreach ($fingerprint in $existingMap.Keys) {
            if (-not $currentMap.ContainsKey($fingerprint)) { continue }
            $count = [Math]::Min([int]$existingMap[$fingerprint].count, [int]$currentMap[$fingerprint].count)
            if ($count -gt 0) { $result += New-AHBaselineRecord -Diagnostic $currentMap[$fingerprint] -Count $count }
        }
    }
    else {
        foreach ($fingerprint in $existingMap.Keys) { $result += New-AHBaselineRecord -Diagnostic $existingMap[$fingerprint] }
        $resultMap = @{}
        foreach ($entry in $result) { $resultMap[[string]$entry.fingerprint] = $entry }
        foreach ($fingerprint in $currentMap.Keys) {
            if ($resultMap.ContainsKey($fingerprint)) {
                $resultMap[$fingerprint].count = [Math]::Max([int]$resultMap[$fingerprint].count, [int]$currentMap[$fingerprint].count)
            }
            else {
                $entry = New-AHBaselineRecord -Diagnostic $currentMap[$fingerprint]
                $result += $entry
                $resultMap[$fingerprint] = $entry
            }
        }
    }

    return @($result | Sort-Object fingerprint)
}

function Invoke-AHAnalysis {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$ArtifactDirectory,
        [switch]$IgnoreBaselineRegression
    )

    New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
    $started = [DateTime]::UtcNow
    $integrity = Test-AHIntegrity -ProjectRoot $ProjectRoot
    $artifacts = @()
    $report = [ordered]@{
        schemaVersion = 1
        status = 'failed'
        exitCode = 1
        startedAtUtc = $started.ToString('o')
        completedAtUtc = $null
        tool = [ordered]@{ name = 'RageQuitting Analyzer Harness'; version = $script:AnalyzerHarnessVersion }
        analyzer = [ordered]@{ packageId = 'Microsoft.Unity.Analyzers'; version = $null; assemblySha256 = $null }
        counts = [ordered]@{ total = 0; blocking = 0; informational = 0; perCategory = [ordered]@{}; perId = [ordered]@{} }
        baseline = [ordered]@{ path = Join-Path $ProjectRoot 'Tools\AgentHarness\analyzer-baseline.json'; entries = 0; occurrences = 0 }
        diagnostics = [ordered]@{ currentBlocking = @(); currentInformational = @(); new = @(); resolved = @(); compiler = @(); loadFailures = @() }
        integrity = [ordered]@{ status = $(if ($integrity.healthy) { 'passed' } else { 'failed' }); checks = @($integrity.checks); issues = @($integrity.issues) }
        build = [ordered]@{
            status = 'not_run'
            command = 'dotnet build "Assembly-CSharp-Editor.csproj" --no-restore --no-incremental --nologo --verbosity:minimal'
            exitCode = $null
            project = 'Assembly-CSharp-Editor.csproj'
            topLevelProject = 'Assembly-CSharp-Editor.csproj'
            scope = 'runtime+editor'
            stdout = $null
            stderr = $null
        }
        artifacts = @()
    }

    if ($integrity.manifest) {
        $report.analyzer.version = [string]$integrity.manifest.version
        $report.analyzer.assemblySha256 = [string]$integrity.manifest.assemblySha256
    }
    if ($integrity.baseline) {
        $report.baseline.entries = @($integrity.baseline.diagnostics).Count
        $report.baseline.occurrences = Get-AHCountSum -Entries @($integrity.baseline.diagnostics)
    }

    if (-not $integrity.healthy) {
        $report.completedAtUtc = [DateTime]::UtcNow.ToString('o')
        return $report
    }

    $build = Invoke-AHDotnetBuild -ProjectRoot $ProjectRoot -ProjectPath $integrity.paths.editorProject -ArtifactDirectory $ArtifactDirectory
    $report.build.command = $build.command
    $report.build.exitCode = $build.exitCode
    $report.build.stdout = $build.stdoutPath
    $report.build.stderr = $build.stderrPath
    $artifacts += @($build.stdoutPath, $build.stderrPath)
    if (-not $build.available) {
        $report.status = 'not_available'
        $report.exitCode = 3
        $report.build.status = 'not_available'
        $report.integrity.issues += [string]$build.error
        $report.artifacts = @($artifacts)
        $report.completedAtUtc = [DateTime]::UtcNow.ToString('o')
        return $report
    }

    $parsed = Get-AHBuildDiagnostics -ProjectRoot $ProjectRoot -RuleById $integrity.ruleById -Paths @($build.stdoutPath, $build.stderrPath)
    $blocking = @($parsed.current | Where-Object { $_.policy -eq 'blockOnNew' })
    $informational = @($parsed.current | Where-Object { $_.policy -eq 'informational' })
    $comparison = Compare-AHBaseline -CurrentBlocking $blocking -BaselineEntries @($integrity.baseline.diagnostics)
    $allCurrent = @($parsed.current)
    $perCategory = [ordered]@{}
    foreach ($category in @('Correctness', 'Type Safety', 'Performance', 'Readability', 'Unknown')) {
        $count = Get-AHCountSum -Entries @($allCurrent | Where-Object { $_.category -eq $category })
        if ($count -gt 0 -or $category -ne 'Unknown') { $perCategory[$category] = $count }
    }
    $perId = [ordered]@{}
    foreach ($id in @($allCurrent | ForEach-Object { [string]$_.id } | Sort-Object -Unique)) {
        $perId[$id] = Get-AHCountSum -Entries @($allCurrent | Where-Object { $_.id -eq $id })
    }

    $report.counts.total = Get-AHCountSum -Entries $allCurrent
    $report.counts.blocking = Get-AHCountSum -Entries $blocking
    $report.counts.informational = Get-AHCountSum -Entries $informational
    $report.counts.perCategory = $perCategory
    $report.counts.perId = $perId
    $report.diagnostics.currentBlocking = @($blocking)
    $report.diagnostics.currentInformational = @($informational)
    $report.diagnostics.new = @($comparison.new)
    $report.diagnostics.resolved = @($comparison.resolved)
    $report.diagnostics.compiler = @($parsed.compilerDiagnostics)
    $report.diagnostics.loadFailures = @($parsed.loadFailureLines)

    if ($build.exitCode -ne 0) {
        $report.build.status = 'failed'
        $report.status = 'failed'
        $report.exitCode = 1
    }
    elseif (@($parsed.loadFailureLines).Count -gt 0) {
        $report.build.status = 'failed'
        $report.status = 'failed'
        $report.exitCode = 1
    }
    elseif (-not $IgnoreBaselineRegression -and @($comparison.new).Count -gt 0) {
        $report.build.status = 'passed'
        $report.status = 'failed'
        $report.exitCode = 1
    }
    else {
        $report.build.status = 'passed'
        $report.status = 'passed'
        $report.exitCode = 0
    }

    $report.artifacts = @($artifacts)
    $report.completedAtUtc = [DateTime]::UtcNow.ToString('o')
    return $report
}

Export-ModuleMember -Function Get-AHProjectRoot, Resolve-AHPath, Read-AHJsonFile, Write-AHJsonFile, Test-AHIntegrity, Get-AHBuildDiagnostics, Compare-AHBaseline, Update-AHBaselineEntries, Invoke-AHAnalysis
