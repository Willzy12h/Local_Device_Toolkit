[CmdletBinding()]
param([string]$RepositoryRoot=(Split-Path -Parent $PSScriptRoot))
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$registry=Get-Content -LiteralPath (Join-Path $RepositoryRoot 'scripts/registry.json') -Raw | ConvertFrom-Json
if($registry.schemaVersion -ne 1){throw 'Unsupported script registry.'}
$ids=@{}
foreach($script in $registry.scripts){
 if($ids.ContainsKey($script.id)){throw 'Duplicate script ID.'};$ids[$script.id]=$true
 if([IO.Path]::IsPathRooted($script.path) -or $script.path -match '(^|[\\/])\.\.([\\/]|$)'){throw 'Unsafe script path.'}
 $path=Join-Path (Join-Path $RepositoryRoot 'scripts') $script.path
 if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Registered script is a reparse point.'}
 $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
 if($hash -cne $script.sha256){throw "Reviewed script hash differs: $($script.id). Do not automatically repin."}
 $tokens=$null;$errors=$null
 [void][System.Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
 if(@($errors).Count -ne 0){throw "PowerShell parser rejected $($script.id): $($errors -join '; ')"}
}
Write-Output "Verified $($ids.Count) registered scripts and PowerShell syntax."
