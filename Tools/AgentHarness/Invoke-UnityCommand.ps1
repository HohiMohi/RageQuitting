[CmdletBinding()]
param([Parameter(Mandatory=$true)][string[]]$UnityArguments,[string]$ProjectRoot)
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'UnityConnection.psm1') -Force
if([string]::IsNullOrWhiteSpace($ProjectRoot)){$scriptDirectory=Split-Path -Parent $MyInvocation.MyCommand.Path;$ProjectRoot=Join-Path $scriptDirectory '..\..'}
$root=[IO.Path]::GetFullPath($ProjectRoot);$cli=Resolve-UnityCliPath
$out=[IO.Path]::GetTempFileName();$err=[IO.Path]::GetTempFileName()
try {
 $result=Invoke-UnityConnectionCli -FilePath $cli -Arguments $UnityArguments -StdOutPath $out -StdErrPath $err -WorkingDirectory $root
 if(Test-Path $out){[Console]::Out.Write((Get-Content $out -Raw))}
 if(Test-Path $err){[Console]::Error.Write((Get-Content $err -Raw))}
 if(-not $result.available){
  $outText=if(Test-Path -LiteralPath $out){Get-Content -LiteralPath $out -Raw}else{''}
  if([string]::IsNullOrWhiteSpace($outText)){$failureJson=@{success=$false;command='guard';data=$null;errors=@(@{code='UNITY_CONNECTION_BLOCKED';message=[string]$result.error});warnings=@()}|ConvertTo-Json -Depth 5;[Console]::Out.WriteLine($failureJson)}
  $errText=if(Test-Path -LiteralPath $err){Get-Content -LiteralPath $err -Raw}else{''}
  if([string]::IsNullOrWhiteSpace($errText)){[Console]::Error.WriteLine([string]$result.error)}
  exit 3
 }
 exit ([int]$result.exitCode)
} finally {Remove-Item $out,$err -Force -ErrorAction SilentlyContinue}
