[CmdletBinding()]
param(
    [string]$ArtifactDirectory,
    [string]$Filter,
    [ValidateRange(10, 1800)][int]$TimeoutSeconds = 180,
    [switch]$IncludeExplicit,
    [switch]$Json
)

& (Join-Path $PSScriptRoot 'Invoke-UnityEditModeTests.ps1') @PSBoundParameters -Mode PlayMode
exit $LASTEXITCODE
