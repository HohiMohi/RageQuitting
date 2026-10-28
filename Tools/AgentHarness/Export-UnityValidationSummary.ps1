[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SummaryPath,
    [string]$OutputPath,
    [switch]$Publish,
    [string]$SourceRoot = 'D:\Programy\UnityProjects\RageQuitting\Documentation',
    [string]$MirrorRoot = 'C:\Users\dunia\Documents\RageQuitting',
    [switch]$Json
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$modulePath = Join-Path $scriptDirectory 'ValidationSummary.psm1'
$syncScript = Join-Path $scriptDirectory 'Invoke-DocumentationSync.ps1'
$summaryPathResolved = [IO.Path]::GetFullPath($SummaryPath)
$markdownPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) { Join-Path (Split-Path -Parent $summaryPathResolved) 'summary.md' } else { [IO.Path]::GetFullPath($OutputPath) }
$validationStatus = 'unknown'
$validationExitCode = $null
$exitCode = 1
$resultStatus = 'failed'
$publicationStatus = 'not_requested'
$publicationMessage = $null
$markerStart = '<!-- BEGIN GENERATED UNITY VALIDATION SUMMARY -->'
$markerEnd = '<!-- END GENERATED UNITY VALIDATION SUMMARY -->'

function Test-PathOverlap([string]$First,[string]$Second) {
    $a=[IO.Path]::GetFullPath($First).TrimEnd('\','/')
    $b=[IO.Path]::GetFullPath($Second).TrimEnd('\','/')
    return [string]::Equals($a,$b,[StringComparison]::OrdinalIgnoreCase) -or
        $a.StartsWith($b + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
        $b.StartsWith($a + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
}

function Test-ContainedPath([string]$Root,[string]$Candidate) {
    $base=[IO.Path]::GetFullPath($Root).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    $path=[IO.Path]::GetFullPath($Candidate)
    return $path.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoReparsePath([string]$Root,[string]$Candidate) {
    if (-not (Test-ContainedPath $Root $Candidate)) { throw "Target escapes its root: '$Candidate'." }
    $rootPath=[IO.Path]::GetFullPath($Root)
    $candidatePath=[IO.Path]::GetFullPath($Candidate)
    $ancestor=$rootPath
    while($ancestor){
        if(Test-Path -LiteralPath $ancestor){$item=Get-Item -LiteralPath $ancestor -Force;if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "Publication root path contains a reparse point: '$ancestor'."}}
        $parent=Split-Path -Parent $ancestor
        if(-not $parent -or $parent -eq $ancestor){break}
        $ancestor=$parent
    }
    $current=$rootPath
    $relative=$candidatePath.Substring($rootPath.TrimEnd('\','/').Length).TrimStart('\','/')
    $paths=@($rootPath)
    foreach($part in @($relative -split '[\\/]')) { if($part){$current=Join-Path $current $part;$paths+=$current} }
    foreach($path in $paths) {
        if (Test-Path -LiteralPath $path) {
            $item=Get-Item -LiteralPath $path -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Publication path contains a reparse point: '$path'." }
        }
    }
}

function Test-GeneratedDocument([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    $text=Get-Content -LiteralPath $Path -Raw -ErrorAction Stop
    if (-not $text.Contains($markerStart) -or -not $text.Contains($markerEnd)) {
        throw "Refusing to replace existing human-authored document without generated markers: '$Path'."
    }
}

function Restore-File([string]$Path,[string]$Backup,[bool]$Existed) {
    if ($Existed -and (Test-Path -LiteralPath $Backup -PathType Leaf)) {
        $restoreTemp=$Path + '.restore-' + [Guid]::NewGuid().ToString('N')
        [IO.File]::Copy($Backup,$restoreTemp,$false)
        if (Test-Path -LiteralPath $Path -PathType Leaf) { [IO.File]::Replace($restoreTemp,$Path,$null) } else { [IO.File]::Move($restoreTemp,$Path) }
        Remove-Item -LiteralPath $Backup -Force -ErrorAction SilentlyContinue
    }
    elseif (-not $Existed -and (Test-Path -LiteralPath $Path -PathType Leaf)) { Remove-Item -LiteralPath $Path -Force }
}

try {
    Import-Module $modulePath -Force
    if (-not (Test-Path -LiteralPath $summaryPathResolved -PathType Leaf)) { throw "Summary file was not found: '$summaryPathResolved'." }
    $summary = Get-Content -LiteralPath $summaryPathResolved -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    $validationStatus = if ($summary.PSObject.Properties['status']) { [string]$summary.status } else { 'unknown' }
    $validationExitCode = if ($summary.PSObject.Properties['exitCode']) { [int]$summary.exitCode } else { $null }
    $markdown = ConvertTo-UnityValidationMarkdown -SummaryPath $summaryPathResolved

    $docSource = $null
    $docMirror = $null
    if ($Publish) {
        if ([string]::IsNullOrWhiteSpace($SourceRoot) -or [string]::IsNullOrWhiteSpace($MirrorRoot)) { throw 'SourceRoot and MirrorRoot are required for publication.' }
        $SourceRoot=[IO.Path]::GetFullPath($SourceRoot)
        $MirrorRoot=[IO.Path]::GetFullPath($MirrorRoot)
        if (-not (Test-Path -LiteralPath $SourceRoot -PathType Container)) { throw "SourceRoot must already exist: '$SourceRoot'." }
        if (Test-PathOverlap $SourceRoot $MirrorRoot) { throw 'SourceRoot and MirrorRoot must not overlap.' }
        $docRelative='Generated\Latest-Validation.md'
        $docSource=[IO.Path]::GetFullPath((Join-Path $SourceRoot $docRelative))
        $docMirror=[IO.Path]::GetFullPath((Join-Path $MirrorRoot $docRelative))
        $outputOverlapsSource=Test-PathOverlap $markdownPath $SourceRoot
        $outputOverlapsMirror=Test-PathOverlap $markdownPath $MirrorRoot
        if ($outputOverlapsSource -or $outputOverlapsMirror) { throw 'OutputPath must remain outside both documentation roots during publication.' }
        Assert-NoReparsePath $SourceRoot $docSource
        Assert-NoReparsePath $MirrorRoot $docMirror
        Test-GeneratedDocument $docSource
        Test-GeneratedDocument $docMirror
    }

    $null = Write-UnityValidationMarkdown -SummaryPath $summaryPathResolved -OutputPath $markdownPath
    if ($Publish) {
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $docSource) -Force
        $backup=$docSource + '.backup-' + [Guid]::NewGuid().ToString('N')
        $hadSource=Test-Path -LiteralPath $docSource -PathType Leaf
        $mirrorBackup=Join-Path (Split-Path -Parent $summaryPathResolved) ('mirror-backup-'+[Guid]::NewGuid().ToString('N')+'.tmp')
        $hadMirror=Test-Path -LiteralPath $docMirror -PathType Leaf
        if ($hadSource) { [IO.File]::Copy($docSource,$backup,$false) }
        if ($hadMirror) { [IO.File]::Copy($docMirror,$mirrorBackup,$false) }
        $temp=$null
        try {
            $temp=$docSource + '.tmp-' + [Guid]::NewGuid().ToString('N')
            [IO.File]::WriteAllText($temp,$markdown,[Text.UTF8Encoding]::new($false))
            if ($hadSource) { [IO.File]::Replace($temp,$docSource,$null) } else { [IO.File]::Move($temp,$docSource) }
            $powershell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
            $syncArgs=@('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$syncScript,'-Document',$docRelative,'-SourceRoot',$SourceRoot,'-MirrorRoot',$MirrorRoot,'-ArtifactDirectory',(Join-Path (Split-Path -Parent $summaryPathResolved) 'documentation-sync'),'-Json')
            $syncOutput=& $powershell @syncArgs 2>&1
            $syncProcessExit=$LASTEXITCODE
            $syncEnvelope=@($syncOutput | Where-Object { $_ -match '^\{' } | ForEach-Object { try { $_ | ConvertFrom-Json -ErrorAction Stop } catch { $null } } | Where-Object { $_ }) | Select-Object -Last 1
            if ($syncProcessExit -ne 0 -or -not $syncEnvelope -or [int]$syncEnvelope.exitCode -ne 0 -or [string]$syncEnvelope.status -ne 'passed') {
                throw ('Documentation sync did not confirm parity (process exit ' + $syncProcessExit + '): ' + (($syncOutput | ForEach-Object { [string]$_ }) -join ' '))
            }
            $sourceBytes=[Convert]::ToBase64String([IO.File]::ReadAllBytes($docSource))
            $mirrorBytes=if(Test-Path -LiteralPath $docMirror -PathType Leaf){[Convert]::ToBase64String([IO.File]::ReadAllBytes($docMirror))}else{$null}
            if ($null -eq $mirrorBytes -or $sourceBytes -cne $mirrorBytes) {
                throw 'Published mirror does not match the authoritative generated Markdown.'
            }
            Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $mirrorBackup -Force -ErrorAction SilentlyContinue
            $publicationStatus='passed'
            $publicationMessage="Published and verified $docRelative in both documentation roots."
        }
        catch {
            $primaryError=$_.Exception.Message
            $restoreErrors=@()
            try { Restore-File $docSource $backup $hadSource } catch { $restoreErrors += ('source restore: '+$_.Exception.Message) }
            try { Restore-File $docMirror $mirrorBackup $hadMirror } catch { $restoreErrors += ('mirror restore: '+$_.Exception.Message) }
            if($temp -and (Test-Path -LiteralPath $temp)){Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue}
            if($restoreErrors.Count -gt 0){
                throw ($primaryError+'; rollback incomplete: '+($restoreErrors -join '; ')+'; retained backups: '+$backup+', '+$mirrorBackup)
            }
            throw $primaryError
        }
    }
    $resultStatus='passed'
    $exitCode=0
}
catch {
    $publicationStatus=if($Publish){'failed'}else{'not_requested'}
    $publicationMessage=$_.Exception.Message
    $resultStatus='failed'
    $exitCode=1
}

$result=[ordered]@{
    success=($exitCode -eq 0)
    status=$resultStatus
    exitCode=$exitCode
    validationStatus=$validationStatus
    validationExitCode=$validationExitCode
    summaryPath=$summaryPathResolved
    markdownSummaryPath=if(Test-Path -LiteralPath $markdownPath -PathType Leaf){$markdownPath}else{$null}
    publicationStatus=$publicationStatus
    publicationMessage=$publicationMessage
}
if ($Json) { [Console]::Out.WriteLine((ConvertTo-Json -Compress -InputObject $result)) }
else { Write-Output (ConvertTo-Json -InputObject $result -Depth 5) }
exit $exitCode
