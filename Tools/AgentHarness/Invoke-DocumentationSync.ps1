[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Alias('Documents')][string[]]$Document = @(),
    [switch]$All,
    [string]$SourceRoot = 'D:\Programy\UnityProjects\RageQuitting\Documentation',
    [string]$MirrorRoot = 'C:\Users\dunia\Documents\RageQuitting',
    [string]$ArtifactDirectory,
    [switch]$Json
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$started = [DateTime]::UtcNow
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$reportPath = $null
$report = [ordered]@{
    schemaVersion = 1
    tool = 'DocumentationSync'
    status = 'failed'
    exitCode = 2
    startedAtUtc = $started.ToString('o')
    completedAtUtc = $null
    sourceRoot = $null
    mirrorRoot = $null
    selectedDocuments = @()
    copied = @()
    unchanged = @()
    wouldCopy = @()
    rejected = @()
    failed = @()
    errors = @()
    artifacts = @()
}

function Test-ContainedPath {
    param([string]$Root, [string]$Candidate)

    $rootWithSeparator = $Root.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    return $Candidate.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-PathsOverlap {
    param([string]$First, [string]$Second)

    return $First.Equals($Second, [System.StringComparison]::OrdinalIgnoreCase) -or
        (Test-ContainedPath -Root $First -Candidate $Second) -or
        (Test-ContainedPath -Root $Second -Candidate $First)
}

function Test-PathHasReparsePoint {
    param([string]$Root, [string]$Candidate)

    if (Test-Path -LiteralPath $Root) {
        $rootItem = Get-Item -LiteralPath $Root -Force
        if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { return $true }
    }
    $relative = $Candidate.Substring($Root.TrimEnd('\', '/').Length).TrimStart('\', '/')
    $current = $Root
    foreach ($part in @($relative -split '[\\/]')) {
        if ([string]::IsNullOrWhiteSpace($part)) { continue }
        $current = Join-Path $current $part
        if (-not (Test-Path -LiteralPath $current)) { break }
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { return $true }
    }
    return $false
}

function Test-ExistingAncestorHasReparsePoint {
    param([string]$Path)

    $current = $Path.TrimEnd('\', '/')
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { return $true }
        }
        $parent = [System.IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent.Equals($current, [System.StringComparison]::OrdinalIgnoreCase)) { break }
        $current = $parent
    }
    return $false
}

function Test-FilesEqual {
    param([string]$First, [string]$Second)

    $firstInfo = Get-Item -LiteralPath $First
    $secondInfo = Get-Item -LiteralPath $Second
    if ($firstInfo.Length -ne $secondInfo.Length) { return $false }

    $firstStream = [System.IO.File]::OpenRead($First)
    $secondStream = [System.IO.File]::OpenRead($Second)
    try {
        $firstBuffer = New-Object byte[] 65536
        $secondBuffer = New-Object byte[] 65536
        while ($true) {
            $firstRead = $firstStream.Read($firstBuffer, 0, $firstBuffer.Length)
            $secondRead = $secondStream.Read($secondBuffer, 0, $secondBuffer.Length)
            if ($firstRead -ne $secondRead) { return $false }
            if ($firstRead -eq 0) { return $true }
            for ($index = 0; $index -lt $firstRead; $index++) {
                if ($firstBuffer[$index] -ne $secondBuffer[$index]) { return $false }
            }
        }
    }
    finally {
        $firstStream.Dispose()
        $secondStream.Dispose()
    }
}

function Complete-Run {
    param([string]$Status, [int]$Code)

    $report.status = $Status
    $report.exitCode = $Code
    $report.completedAtUtc = [DateTime]::UtcNow.ToString('o')

    if ($reportPath) {
        try {
            $artifactPath = Split-Path -Parent $reportPath
            [System.IO.Directory]::CreateDirectory($artifactPath) | Out-Null
            $report.artifacts = @([System.IO.Path]::GetFullPath($reportPath))
            $jsonText = $report | ConvertTo-Json -Depth 12
            $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
            [System.IO.File]::WriteAllText($reportPath, $jsonText, $utf8WithoutBom)
        }
        catch {
            $report.status = 'failed'
            $report.exitCode = 2
            $report.errors = @($report.errors) + @("Could not write report: $($_.Exception.Message)")
            $report.artifacts = @()
        }
    }

    if ($Json) {
        [Console]::Out.WriteLine((ConvertTo-Json -Compress -InputObject ([ordered]@{
            reportPath = $reportPath
            status = [string]$report.status
            exitCode = [int]$report.exitCode
        })))
    }
    else {
        Write-Host ('Documentation sync: {0} (exit {1})' -f $report.status, $report.exitCode)
        foreach ($path in @($report.copied)) { Write-Host ('COPIED    {0}' -f $path) }
        foreach ($path in @($report.unchanged)) { Write-Host ('UNCHANGED {0}' -f $path) }
        foreach ($path in @($report.wouldCopy)) { Write-Host ('WHATIF    {0}' -f $path) }
        foreach ($entry in @($report.rejected)) { Write-Host ('REJECTED  {0}: {1}' -f $entry.document, $entry.reason) }
        foreach ($entry in @($report.failed)) { Write-Host ('FAILED    {0}: {1}' -f $entry.document, $entry.reason) }
        foreach ($message in @($report.errors)) { Write-Host ('ERROR     {0}' -f $message) }
        if ($reportPath) { Write-Host ('Report: {0}' -f $reportPath) }
    }
    exit ([int]$report.exitCode)
}

try {
    if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
        $runId = '{0}-{1}' -f $started.ToString('yyyyMMddTHHmmssZ'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
        $ArtifactDirectory = Join-Path $projectRoot (Join-Path 'Artifacts\Validation\DocumentationSync' $runId)
    }
    elseif (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) {
        $ArtifactDirectory = Join-Path $projectRoot $ArtifactDirectory
    }
    $ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
    $reportPath = Join-Path $ArtifactDirectory 'documentation-sync-report.json'

    if ([string]::IsNullOrWhiteSpace($SourceRoot) -or [string]::IsNullOrWhiteSpace($MirrorRoot)) {
        $report.sourceRoot = $SourceRoot
        $report.mirrorRoot = $MirrorRoot
        $report.rejected = @([ordered]@{ document = '<roots>'; reason = 'SourceRoot and MirrorRoot must not be blank.' })
        Complete-Run 'failed' 1
    }
    $SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)
    $MirrorRoot = [System.IO.Path]::GetFullPath($MirrorRoot)
    $report.sourceRoot = $SourceRoot
    $report.mirrorRoot = $MirrorRoot

    if ((Test-PathsOverlap -First $ArtifactDirectory -Second $SourceRoot) -or
        (Test-PathsOverlap -First $ArtifactDirectory -Second $MirrorRoot)) {
        $reportPath = $null
        $report.rejected = @([ordered]@{ document = '<artifactDirectory>'; reason = 'ArtifactDirectory must not overlap SourceRoot or MirrorRoot.' })
        Complete-Run 'failed' 1
    }

    if (-not (Test-Path -LiteralPath $SourceRoot -PathType Container)) {
        $report.errors = @("Source root is unavailable: '$SourceRoot'.")
        Complete-Run 'not_available' 3
    }
    if (Test-PathHasReparsePoint -Root $SourceRoot -Candidate $SourceRoot) {
        $report.rejected = @([ordered]@{ document = '<sourceRoot>'; reason = 'SourceRoot must not be a reparse point.' })
        Complete-Run 'failed' 1
    }
    if (Test-PathsOverlap -First $SourceRoot -Second $MirrorRoot) {
        $report.rejected = @([ordered]@{ document = '<roots>'; reason = 'Source and mirror roots must not overlap.' })
        Complete-Run 'failed' 1
    }
    $mirrorRootExists = Test-Path -LiteralPath $MirrorRoot -PathType Container
    if (-not $mirrorRootExists -and (Test-Path -LiteralPath $MirrorRoot)) {
        $report.rejected = @([ordered]@{ document = '<mirrorRoot>'; reason = 'MirrorRoot exists but is not a directory.' })
        Complete-Run 'failed' 1
    }
    if ($mirrorRootExists -and (Test-PathHasReparsePoint -Root $MirrorRoot -Candidate $MirrorRoot)) {
        $report.rejected = @([ordered]@{ document = '<mirrorRoot>'; reason = 'MirrorRoot must not be a reparse point.' })
        Complete-Run 'failed' 1
    }
    if (-not $mirrorRootExists -and (Test-ExistingAncestorHasReparsePoint -Path $MirrorRoot)) {
        $report.rejected = @([ordered]@{ document = '<mirrorRoot>'; reason = 'MirrorRoot cannot be created beneath a reparse point.' })
        Complete-Run 'failed' 1
    }
    if (($All -and @($Document).Count -gt 0) -or (-not $All -and @($Document).Count -eq 0)) {
        $report.rejected = @([ordered]@{ document = '<selection>'; reason = 'Specify documents or -All, but not both.' })
        Complete-Run 'failed' 1
    }

    $selected = @()
    if ($All) {
        $selected = @(Get-ChildItem -LiteralPath $SourceRoot -Filter '*.md' -File -Recurse |
            ForEach-Object { $_.FullName.Substring($SourceRoot.TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/') } |
            Sort-Object)
    }
    else {
        $seen = @{}
        foreach ($inputPath in @($Document)) {
            $display = if ($null -eq $inputPath) { '' } else { [string]$inputPath }
            if ([string]::IsNullOrWhiteSpace($display)) {
                $report.rejected += [ordered]@{ document = $display; reason = 'Document path is empty.' }
                continue
            }
            if ([System.IO.Path]::IsPathRooted($display)) {
                $report.rejected += [ordered]@{ document = $display; reason = 'Absolute paths are not allowed.' }
                continue
            }
            if ([System.IO.Path]::GetExtension($display) -ine '.md') {
                $report.rejected += [ordered]@{ document = $display; reason = 'Only .md documents are allowed.' }
                continue
            }
            try { $sourcePath = [System.IO.Path]::GetFullPath((Join-Path $SourceRoot $display)) }
            catch {
                $report.rejected += [ordered]@{ document = $display; reason = 'Document path is invalid.' }
                continue
            }
            if (-not (Test-ContainedPath -Root $SourceRoot -Candidate $sourcePath)) {
                $report.rejected += [ordered]@{ document = $display; reason = 'Document path escapes the source root.' }
                continue
            }
            $relative = $sourcePath.Substring($SourceRoot.TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/')
            if ([System.IO.Path]::GetFileName($relative) -ieq 'AGENTS.md') {
                $report.rejected += [ordered]@{ document = $display; reason = 'AGENTS.md is never synchronized.' }
                continue
            }
            if ($seen.ContainsKey($relative)) {
                $report.rejected += [ordered]@{ document = $display; reason = "Duplicate document selection resolves to '$relative'." }
                continue
            }
            $seen[$relative] = $true
            $selected += $relative
        }
        $selected = @($selected | Sort-Object)
    }

    foreach ($relative in @($selected)) {
        if ([System.IO.Path]::GetFileName($relative) -ieq 'AGENTS.md') {
            $report.rejected += [ordered]@{ document = $relative; reason = 'AGENTS.md is never synchronized.' }
        }
    }
    if (@($report.rejected).Count -gt 0) {
        $report.rejected = @($report.rejected | Sort-Object document, reason)
        Complete-Run 'failed' 1
    }

    $report.selectedDocuments = @($selected)
    $operations = @()
    foreach ($relative in $selected) {
        $sourcePath = [System.IO.Path]::GetFullPath((Join-Path $SourceRoot $relative))
        $mirrorPath = [System.IO.Path]::GetFullPath((Join-Path $MirrorRoot $relative))
        if (-not (Test-ContainedPath -Root $MirrorRoot -Candidate $mirrorPath)) {
            $report.rejected += [ordered]@{ document = $relative; reason = 'Document path escapes the mirror root.' }
            continue
        }
        if (Test-PathHasReparsePoint -Root $SourceRoot -Candidate $sourcePath) {
            $report.rejected += [ordered]@{ document = $relative; reason = 'Source path contains a reparse point.' }
            continue
        }
        if (Test-PathHasReparsePoint -Root $MirrorRoot -Candidate $mirrorPath) {
            $report.rejected += [ordered]@{ document = $relative; reason = 'Mirror path contains a reparse point.' }
            continue
        }
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            $report.rejected += [ordered]@{ document = $relative; reason = 'Source document does not exist.' }
            continue
        }
        $operations += [ordered]@{ relative = $relative; source = $sourcePath; mirror = $mirrorPath }
    }
    if (@($report.rejected).Count -gt 0) {
        $report.rejected = @($report.rejected | Sort-Object document, reason)
        Complete-Run 'failed' 1
    }

    foreach ($operation in $operations) {
        $relative = [string]$operation.relative
        $sourcePath = [string]$operation.source
        $mirrorPath = [string]$operation.mirror
        $targetExistedBefore = Test-Path -LiteralPath $mirrorPath -PathType Leaf
        if ($targetExistedBefore -and (Test-FilesEqual -First $sourcePath -Second $mirrorPath)) {
            $report.unchanged += $relative
            continue
        }

        $shouldCopy = if ($WhatIfPreference) {
            $false
        }
        else {
            $PSCmdlet.ShouldProcess($mirrorPath, "Copy authoritative documentation '$relative'")
        }
        if (-not $shouldCopy) {
            $report.wouldCopy += $relative
            continue
        }

        $targetDirectory = Split-Path -Parent $mirrorPath
        $temporaryPath = Join-Path $targetDirectory ('.documentation-sync-{0}.tmp' -f [Guid]::NewGuid().ToString('N'))
        $backupPath = Join-Path $targetDirectory ('.documentation-sync-{0}.bak' -f [Guid]::NewGuid().ToString('N'))
        $failedReplacementPath = Join-Path $targetDirectory ('.documentation-sync-{0}.failed' -f [Guid]::NewGuid().ToString('N'))
        $replacementCompleted = $false
        $newTargetCreated = $false
        $preserveBackup = $false
        try {
            New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
            [System.IO.File]::Copy($sourcePath, $temporaryPath, $false)
            if (-not (Test-FilesEqual -First $sourcePath -Second $temporaryPath)) {
                throw 'Temporary copy does not match the source bytes.'
            }
            if (Test-Path -LiteralPath $mirrorPath -PathType Leaf) {
                [System.IO.File]::Replace($temporaryPath, $mirrorPath, $backupPath)
                $replacementCompleted = $true
            }
            else {
                [System.IO.File]::Move($temporaryPath, $mirrorPath)
                $newTargetCreated = $true
            }
            if (-not (Test-FilesEqual -First $sourcePath -Second $mirrorPath)) {
                throw 'Final mirror file does not match the source bytes.'
            }
            $report.copied += $relative
        }
        catch {
            $failureReason = $_.Exception.Message
            if ($replacementCompleted -and (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
                $preserveBackup = $true
                try {
                    [System.IO.File]::Replace($backupPath, $mirrorPath, $failedReplacementPath)
                    $preserveBackup = $false
                    if (Test-Path -LiteralPath $failedReplacementPath -PathType Leaf) {
                        Remove-Item -LiteralPath $failedReplacementPath -Force -ErrorAction SilentlyContinue
                    }
                }
                catch {
                    $failureReason = '{0} Original restoration also failed: {1} Recovery backup retained at ''{2}''.' -f $failureReason, $_.Exception.Message, $backupPath
                }
            }
            elseif (-not $targetExistedBefore -and $newTargetCreated -and (Test-Path -LiteralPath $mirrorPath -PathType Leaf)) {
                try {
                    [System.IO.File]::Delete($mirrorPath)
                }
                catch {
                    $failureReason = '{0} Newly created target rollback also failed: {1}' -f $failureReason, $_.Exception.Message
                }
            }
            $report.failed += [ordered]@{ document = $relative; reason = $failureReason }
        }
        finally {
            if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
                Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
            }
            if (-not $preserveBackup -and (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
                Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
            }
            if (-not $preserveBackup -and (Test-Path -LiteralPath $failedReplacementPath -PathType Leaf)) {
                Remove-Item -LiteralPath $failedReplacementPath -Force -ErrorAction SilentlyContinue
            }
        }
    }

    $report.copied = @($report.copied | Sort-Object)
    $report.unchanged = @($report.unchanged | Sort-Object)
    $report.wouldCopy = @($report.wouldCopy | Sort-Object)
    $report.failed = @($report.failed | Sort-Object document, reason)
    if (@($report.failed).Count -gt 0) { Complete-Run 'failed' 1 }
    Complete-Run 'passed' 0
}
catch {
    $report.errors = @($report.errors) + @($_.Exception.Message)
    Complete-Run 'failed' 2
}
