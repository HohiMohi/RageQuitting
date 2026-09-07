[CmdletBinding()]
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
    tool = 'DocumentationCheck'
    status = 'failed'
    exitCode = 2
    startedAtUtc = $started.ToString('o')
    completedAtUtc = $null
    sourceRoot = $null
    mirrorRoot = $null
    checkedDocuments = @()
    matching = @()
    missing = @()
    missingSource = @()
    missingMirror = @()
    different = @()
    targetOnly = @()
    rejected = @()
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
            New-Item -ItemType Directory -Path $artifactPath -Force | Out-Null
            $report.artifacts = @([System.IO.Path]::GetFullPath($reportPath))
            $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding UTF8
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
        Write-Host ('Documentation check: {0} (exit {1})' -f $report.status, $report.exitCode)
        foreach ($path in @($report.matching)) { Write-Host ('MATCHING  {0}' -f $path) }
        foreach ($path in @($report.missing)) { Write-Host ('MISSING   {0}' -f $path) }
        foreach ($path in @($report.different)) { Write-Host ('DIFFERENT {0}' -f $path) }
        foreach ($entry in @($report.rejected)) { Write-Host ('REJECTED  {0}: {1}' -f $entry.document, $entry.reason) }
        foreach ($path in @($report.targetOnly)) { Write-Host ('TARGET-ONLY {0}' -f $path) }
        foreach ($message in @($report.errors)) { Write-Host ('ERROR     {0}' -f $message) }
        if ($reportPath) { Write-Host ('Report: {0}' -f $reportPath) }
    }
    exit ([int]$report.exitCode)
}

try {
    if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
        $runId = '{0}-{1}' -f $started.ToString('yyyyMMddTHHmmssZ'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
        $ArtifactDirectory = Join-Path $projectRoot (Join-Path 'Artifacts\Validation\DocumentationCheck' $runId)
    }
    elseif (-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)) {
        $ArtifactDirectory = Join-Path $projectRoot $ArtifactDirectory
    }
    $ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
    $reportPath = Join-Path $ArtifactDirectory 'documentation-check-report.json'

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
    if (-not (Test-Path -LiteralPath $MirrorRoot -PathType Container)) {
        $report.errors = @("Mirror root is unavailable: '$MirrorRoot'.")
        Complete-Run 'not_available' 3
    }
    if (Test-PathsOverlap -First $SourceRoot -Second $MirrorRoot) {
        $report.rejected = @([ordered]@{ document = '<roots>'; reason = 'Source and mirror roots must not overlap.' })
        Complete-Run 'failed' 1
    }
    if (Test-PathHasReparsePoint -Root $SourceRoot -Candidate $SourceRoot) {
        $report.rejected = @([ordered]@{ document = '<sourceRoot>'; reason = 'SourceRoot must not be a reparse point.' })
        Complete-Run 'failed' 1
    }
    if (Test-PathHasReparsePoint -Root $MirrorRoot -Candidate $MirrorRoot) {
        $report.rejected = @([ordered]@{ document = '<mirrorRoot>'; reason = 'MirrorRoot must not be a reparse point.' })
        Complete-Run 'failed' 1
    }
    if ($All -and @($Document).Count -gt 0) {
        $report.rejected = @([ordered]@{ document = '<selection>'; reason = 'Specify documents or -All, not both.' })
        Complete-Run 'failed' 1
    }

    $selected = @()
    if ($All -or @($Document).Count -eq 0) {
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
            if ($seen.ContainsKey($relative)) {
                $report.rejected += [ordered]@{ document = $display; reason = "Duplicate document selection resolves to '$relative'." }
                continue
            }
            $seen[$relative] = $true
            $selected += $relative
        }
        $selected = @($selected | Sort-Object)
    }

    if (@($report.rejected).Count -gt 0) { Complete-Run 'failed' 1 }
    $report.checkedDocuments = @($selected)

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
            $report.missing += $relative
            $report.missingSource += $relative
            continue
        }
        if (-not (Test-Path -LiteralPath $mirrorPath -PathType Leaf)) {
            $report.missing += $relative
            $report.missingMirror += $relative
            continue
        }
        if (Test-FilesEqual -First $sourcePath -Second $mirrorPath) { $report.matching += $relative }
        else { $report.different += $relative }
    }

    $sourceSet = @{}
    foreach ($item in @(Get-ChildItem -LiteralPath $SourceRoot -Filter '*.md' -File -Recurse)) {
        $relative = $item.FullName.Substring($SourceRoot.TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/')
        $sourceSet[$relative] = $true
    }
    $report.targetOnly = @(Get-ChildItem -LiteralPath $MirrorRoot -Filter '*.md' -File -Recurse |
        ForEach-Object { $_.FullName.Substring($MirrorRoot.TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/') } |
        Where-Object { -not $sourceSet.ContainsKey($_) } |
        Sort-Object)

    $report.matching = @($report.matching | Sort-Object)
    $report.missing = @($report.missing | Sort-Object)
    $report.missingSource = @($report.missingSource | Sort-Object)
    $report.missingMirror = @($report.missingMirror | Sort-Object)
    $report.different = @($report.different | Sort-Object)
    $report.rejected = @($report.rejected | Sort-Object document, reason)
    if (@($report.missing).Count -gt 0 -or @($report.different).Count -gt 0 -or @($report.rejected).Count -gt 0) {
        Complete-Run 'failed' 1
    }
    Complete-Run 'passed' 0
}
catch {
    $report.errors = @($report.errors) + @($_.Exception.Message)
    Complete-Run 'failed' 2
}
