Set-StrictMode -Version 2.0

function Invoke-HarnessCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory = $true)][string]$StdOutPath,
        [Parameter(Mandatory = $true)][string]$StdErrPath,
        [string]$WorkingDirectory
    )

    $result = [ordered]@{
        available = $true
        exitCode = $null
        error = $null
    }

    try {
        if ($WorkingDirectory) {
            Push-Location -LiteralPath $WorkingDirectory
        }

        try {
            $previousErrorActionPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try {
                & $FilePath @Arguments 1> $StdOutPath 2> $StdErrPath
                $result.exitCode = $LASTEXITCODE
            }
            finally {
                $ErrorActionPreference = $previousErrorActionPreference
            }
        }
        finally {
            if ($WorkingDirectory) {
                Pop-Location
            }
        }
    }
    catch {
        $result.available = $false
        $result.error = $_.Exception.Message
        $_.Exception.ToString() | Set-Content -LiteralPath $StdErrPath -Encoding UTF8
        if (-not (Test-Path -LiteralPath $StdOutPath)) {
            New-Item -ItemType File -Path $StdOutPath -Force | Out-Null
        }
    }

    return $result
}

function ConvertTo-HarnessPathArray {
    param([object[]]$Paths)

    return @(
        $Paths |
            Where-Object { $null -ne $_ -and -not [string]::IsNullOrWhiteSpace([string]$_) } |
            ForEach-Object { ([string]$_).Trim().Replace('\', '/') } |
            Sort-Object -Unique
    )
}

function Get-HarnessFileFingerprint {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-HarnessGitSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$ArtifactDirectory,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $commands = @(
        @{ Name = 'status'; Arguments = @('status', '--porcelain=v1', '--untracked-files=all') },
        @{ Name = 'head'; Arguments = @('rev-parse', 'HEAD') },
        @{ Name = 'branch'; Arguments = @('rev-parse', '--abbrev-ref', 'HEAD') },
        @{ Name = 'unstaged-names'; Arguments = @('diff', '--name-only') },
        @{ Name = 'staged-names'; Arguments = @('diff', '--cached', '--name-only') },
        @{ Name = 'untracked'; Arguments = @('ls-files', '--others', '--exclude-standard') },
        @{ Name = 'unstaged-diff'; Arguments = @('diff', '--no-ext-diff', '--binary', '--') },
        @{ Name = 'staged-diff'; Arguments = @('diff', '--cached', '--no-ext-diff', '--binary', '--') }
    )

    $outputs = @{}
    foreach ($command in $commands) {
        $stdout = Join-Path $ArtifactDirectory ("git-{0}-{1}.stdout.txt" -f $Label, $command.Name)
        $stderr = Join-Path $ArtifactDirectory ("git-{0}-{1}.stderr.txt" -f $Label, $command.Name)
        $execution = Invoke-HarnessCommand -FilePath 'git' -Arguments $command.Arguments -StdOutPath $stdout -StdErrPath $stderr -WorkingDirectory $ProjectRoot
        if (-not $execution.available -or $execution.exitCode -ne 0) {
            return [ordered]@{
                available = $false
                error = if ($execution.error) { $execution.error } else { "git $($command.Name) exited with code $($execution.exitCode)." }
                head = $null
                branch = $null
                status = @()
                trackedChanges = @()
                changedPaths = @()
                unstagedFingerprint = $null
                stagedFingerprint = $null
            }
        }

        $outputs[$command.Name] = @(Get-Content -LiteralPath $stdout -ErrorAction SilentlyContinue)
    }

    $tracked = ConvertTo-HarnessPathArray -Paths @($outputs['unstaged-names'] + $outputs['staged-names'])
    $untracked = ConvertTo-HarnessPathArray -Paths @($outputs.untracked)
    $changed = ConvertTo-HarnessPathArray -Paths @($tracked + $untracked)
    $unstagedDiffPath = Join-Path $ArtifactDirectory ("git-{0}-unstaged-diff.stdout.txt" -f $Label)
    $stagedDiffPath = Join-Path $ArtifactDirectory ("git-{0}-staged-diff.stdout.txt" -f $Label)

    return [ordered]@{
        available = $true
        error = $null
        head = [string]($outputs.head | Select-Object -First 1)
        branch = [string]($outputs.branch | Select-Object -First 1)
        status = @($outputs.status | ForEach-Object { [string]$_ })
        trackedChanges = @($tracked | ForEach-Object { [string]$_ })
        changedPaths = @($changed | ForEach-Object { [string]$_ })
        unstagedFingerprint = Get-HarnessFileFingerprint -Path $unstagedDiffPath
        stagedFingerprint = Get-HarnessFileFingerprint -Path $stagedDiffPath
    }
}

function Resolve-HarnessTier {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RequestedTier,
        [Parameter(Mandatory = $true)][string]$MapPath,
        [object]$GitSnapshot
    )

    if ($RequestedTier -ne 'Auto') {
        return [ordered]@{
            tier = $RequestedTier
            reasons = @("Tier '$RequestedTier' was requested explicitly.")
        }
    }

    $fallbackReason = $null
    if (-not $GitSnapshot -or -not $GitSnapshot.available) {
        $fallbackReason = 'Git changes could not be determined; Auto conservatively fell back to Fast.'
    }
    elseif (@($GitSnapshot.changedPaths).Count -eq 0) {
        $fallbackReason = 'No changed paths were reported by Git; Auto conservatively fell back to Fast.'
    }

    if ($fallbackReason) {
        return [ordered]@{ tier = 'Fast'; reasons = @([string]$fallbackReason) }
    }

    if (-not (Test-Path -LiteralPath $MapPath)) {
        return [ordered]@{
            tier = 'Fast'
            reasons = @('validation-map.json is missing; Auto conservatively fell back to Fast.')
        }
    }

    try {
        $map = Get-Content -LiteralPath $MapPath -Raw | ConvertFrom-Json
    }
    catch {
        return [ordered]@{
            tier = 'Fast'
            reasons = @('validation-map.json could not be parsed; Auto conservatively fell back to Fast.')
        }
    }

    $rank = @{ Fast = 1; Gameplay = 2; Full = 3 }
    $selectedTier = if ($map.defaultTier -and $rank.ContainsKey([string]$map.defaultTier)) { [string]$map.defaultTier } else { 'Fast' }
    $reasons = @()

    foreach ($path in @($GitSnapshot.changedPaths)) {
        $normalizedPath = ([string]$path).Replace('\', '/')
        $matched = $false
        foreach ($mapping in @($map.mappings)) {
            foreach ($pattern in @($mapping.patterns)) {
                if ($normalizedPath -like ([string]$pattern)) {
                    $matched = $true
                    $candidate = [string]$mapping.tier
                    if ($rank.ContainsKey($candidate) -and $rank[$candidate] -gt $rank[$selectedTier]) {
                        $selectedTier = $candidate
                    }
                    $reasons += "$normalizedPath matched '$($mapping.name)' ($candidate): $($mapping.reason)"
                    break
                }
            }
            if ($matched) {
                break
            }
        }

        if (-not $matched) {
            $reasons += "$normalizedPath had no explicit mapping; defaulted to $selectedTier."
        }
    }

    return [ordered]@{ tier = [string]$selectedTier; reasons = @($reasons | ForEach-Object { [string]$_ }) }
}

function New-HarnessStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][ValidateSet('passed', 'failed', 'skipped', 'blocked', 'not_available')][string]$Status,
        [string]$Message,
        [string[]]$Artifacts = @()
    )

    return [ordered]@{
        name = $Name
        status = $Status
        message = $Message
        artifacts = @($Artifacts)
    }
}

function ConvertTo-HarnessConsoleText {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [AllowEmptyString()][string]$Text
    )

    if ([string]::IsNullOrWhiteSpace($Text)) { return '' }
    $normalized = $Text.Replace('\', '/')
    $escapedRoot = [regex]::Escape($ProjectRoot.TrimEnd('\', '/').Replace('\', '/'))
    $normalized = [regex]::Replace($normalized, $escapedRoot, '<project>', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $normalized = [regex]::Replace($normalized, '(?i)((?:<project>/|assets/|packages/)[^\r\n():]+?\.[a-z0-9]+)\(\d+(?:,\d+)?\)', '$1(<line>)')
    $normalized = [regex]::Replace($normalized, '(?i)((?:<project>/|assets/|packages/)[^\r\n:]+?\.[a-z0-9]+):line\s+\d+', '$1:<line>')
    $normalized = [regex]::Replace($normalized, '(?i)((?:<project>/|assets/|packages/)[^\r\n:]+?\.[a-z0-9]+):\d+(?::\d+)?', '$1:<line>')
    return [regex]::Replace($normalized.Trim(), '[ \t]+', ' ')
}

function Get-HarnessConsoleDiagnostics {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [object[]]$Logs = @()
    )

    $aggregates = @{}
    foreach ($log in @($Logs)) {
        $message = if ($log -and $log.PSObject.Properties['message']) { [string]$log.message } elseif ($log -and $log.PSObject.Properties['condition']) { [string]$log.condition } else { [string]$log }
        $stackTrace = if ($log -and $log.PSObject.Properties['stackTrace']) { [string]$log.stackTrace } elseif ($log -and $log.PSObject.Properties['stacktrace']) { [string]$log.stacktrace } else { '' }
        $type = if ($log -and $log.PSObject.Properties['type']) { [string]$log.type } elseif ($log -and $log.PSObject.Properties['logType']) { [string]$log.logType } else { 'Error' }
        $normalizedMessage = ConvertTo-HarnessConsoleText -ProjectRoot $ProjectRoot -Text $message
        $normalizedStack = ConvertTo-HarnessConsoleText -ProjectRoot $ProjectRoot -Text $stackTrace
        $identity = ($type.ToLowerInvariant(), $normalizedMessage, $normalizedStack) -join '|'
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($identity)
            $fingerprint = (($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join '')
        }
        finally {
            $sha.Dispose()
        }

        if (-not $aggregates.ContainsKey($fingerprint)) {
            $sample = if ([string]::IsNullOrWhiteSpace($stackTrace)) { $message } else { $message + [Environment]::NewLine + $stackTrace }
            if ($sample.Length -gt 2000) { $sample = $sample.Substring(0, 2000) }
            $aggregates[$fingerprint] = [ordered]@{
                fingerprint = $fingerprint
                count = 0
                sample = $sample
                normalizedMessage = $normalizedMessage
            }
        }
        $aggregates[$fingerprint].count = [int]$aggregates[$fingerprint].count + 1
    }

    return @($aggregates.Values | Sort-Object fingerprint)
}

function Compare-HarnessCountBaseline {
    param([object[]]$Current = @(), [object[]]$Baseline = @())

    $currentMap = @{}
    foreach ($entry in @($Current)) { $currentMap[[string]$entry.fingerprint] = $entry }
    $baselineMap = @{}
    foreach ($entry in @($Baseline)) { $baselineMap[[string]$entry.fingerprint] = $entry }
    $new = @()
    $resolved = @()
    foreach ($fingerprint in $currentMap.Keys) {
        $currentEntry = $currentMap[$fingerprint]
        $baselineCount = if ($baselineMap.ContainsKey($fingerprint)) { [int]$baselineMap[$fingerprint].count } else { 0 }
        if ([int]$currentEntry.count -gt $baselineCount) {
            $new += [ordered]@{ fingerprint = $fingerprint; baselineCount = $baselineCount; currentCount = [int]$currentEntry.count; delta = [int]$currentEntry.count - $baselineCount; sample = [string]$currentEntry.sample }
        }
    }
    foreach ($fingerprint in $baselineMap.Keys) {
        $baselineEntry = $baselineMap[$fingerprint]
        $currentCount = if ($currentMap.ContainsKey($fingerprint)) { [int]$currentMap[$fingerprint].count } else { 0 }
        if ($currentCount -lt [int]$baselineEntry.count) {
            $resolved += [ordered]@{ fingerprint = $fingerprint; baselineCount = [int]$baselineEntry.count; currentCount = $currentCount; delta = [int]$baselineEntry.count - $currentCount; sample = [string]$baselineEntry.sample }
        }
    }
    return [ordered]@{ new = @($new | Sort-Object fingerprint); resolved = @($resolved | Sort-Object fingerprint) }
}

function Update-HarnessCountBaselineEntries {
    param(
        [object[]]$Existing = @(),
        [object[]]$Current = @(),
        [Parameter(Mandatory = $true)][ValidateSet('PruneResolved', 'AcceptCurrent')][string]$Mode
    )

    $existingMap = @{}
    foreach ($entry in @($Existing)) { $existingMap[[string]$entry.fingerprint] = $entry }
    $currentMap = @{}
    foreach ($entry in @($Current)) { $currentMap[[string]$entry.fingerprint] = $entry }
    $result = @()
    if ($Mode -eq 'PruneResolved') {
        foreach ($fingerprint in $existingMap.Keys) {
            if (-not $currentMap.ContainsKey($fingerprint)) { continue }
            $count = [Math]::Min([int]$existingMap[$fingerprint].count, [int]$currentMap[$fingerprint].count)
            if ($count -gt 0) { $result += [ordered]@{ fingerprint = $fingerprint; count = $count; sample = [string]$currentMap[$fingerprint].sample } }
        }
    }
    else {
        foreach ($fingerprint in $existingMap.Keys) {
            $result += [ordered]@{ fingerprint = $fingerprint; count = [int]$existingMap[$fingerprint].count; sample = [string]$existingMap[$fingerprint].sample }
        }
        $resultMap = @{}
        foreach ($entry in $result) { $resultMap[[string]$entry.fingerprint] = $entry }
        foreach ($fingerprint in $currentMap.Keys) {
            if ($resultMap.ContainsKey($fingerprint)) {
                if ([int]$currentMap[$fingerprint].count -gt [int]$resultMap[$fingerprint].count) {
                    $resultMap[$fingerprint].count = [int]$currentMap[$fingerprint].count
                    $resultMap[$fingerprint].sample = [string]$currentMap[$fingerprint].sample
                }
            }
            else {
                $entry = [ordered]@{ fingerprint = $fingerprint; count = [int]$currentMap[$fingerprint].count; sample = [string]$currentMap[$fingerprint].sample }
                $result += $entry
                $resultMap[$fingerprint] = $entry
            }
        }
    }
    return @($result | Sort-Object fingerprint)
}

Export-ModuleMember -Function Invoke-HarnessCommand, Get-HarnessGitSnapshot, Resolve-HarnessTier, New-HarnessStep, ConvertTo-HarnessConsoleText, Get-HarnessConsoleDiagnostics, Compare-HarnessCountBaseline, Update-HarnessCountBaselineEntries
