# Closed original-user MSIX package read/install adapter. Never dispatched to the administrator worker.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RequestFile)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$WarningPreference='Stop'
if($PSVersionTable.PSVersion.Major -ne 5){throw 'Windows PowerShell 5.1 is required.'}
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
$item=Get-Item -LiteralPath $RequestFile -Force
if($item.Length -gt 65536 -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Invalid bounded request file.'}
$request=Get-Content -LiteralPath $RequestFile -Raw -Encoding UTF8 | ConvertFrom-Json
$fields=@('schemaVersion','action','originalUserSid','name','publisher','familyName','version','entryPoint','dependencies')
if(@($request.PSObject.Properties).Count -ne $fields.Count -or @($request.PSObject.Properties | Where-Object {$fields -cnotcontains $_.Name}).Count -ne 0 -or $request.schemaVersion -ne 1 -or @('Read','Install') -cnotcontains $request.action){throw 'Unregistered MSIX request.'}
$sid=([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
if($sid -cne $request.originalUserSid -or $request.name -cnotmatch '^[A-Za-z0-9.-]{3,50}$' -or [string]::IsNullOrWhiteSpace($request.publisher) -or $request.familyName -cnotmatch '^[A-Za-z0-9.-]{3,50}_[a-z0-9]{13}$'){throw 'Invalid original user or approved package identity.'}
if($request.action -ceq 'Read'){
 if($null -ne $request.entryPoint -or @($request.dependencies).Count -ne 0){throw 'Read requests cannot contain installer media.'}
 $packages=@(Appx\Get-AppxPackage -Name $request.name | Where-Object {$_.Name -ceq $request.name})
 if($packages.Count -gt 1){throw 'Multiple installed package versions are ambiguous.'}
 $present=$packages.Count -eq 1;$identity=$null;$version=$null
 if($present){
  $p=$packages[0]
  if($p.Publisher -cne $request.publisher -or $p.PackageFamilyName -cne $request.familyName -or @('X64','Neutral') -cnotcontains [string]$p.Architecture -or [string]$p.Status -cne 'Ok'){throw 'Installed package publisher, architecture or status does not match.'}
  $identity=[string]$p.PackageFamilyName;$version=[string]$p.Version
 }
 [pscustomobject][ordered]@{schemaVersion=1;originalUserSid=$sid;available=$true;present=$present;productIdentity=$identity;version=$version;capturedAt=[DateTimeOffset]::UtcNow.ToString('o')} | ConvertTo-Json -Depth 5 -Compress
 exit 0
}
function Confirm-LocalPackage([string]$Path){
 if(-not [IO.Path]::IsPathRooted($Path) -or $Path.StartsWith('\\') -or $Path -notmatch '\.(msix|appx|msixbundle|appxbundle)$'){throw 'Only approved staged local packages are permitted.'}
 $current=[IO.Path]::GetFullPath($Path)
 while($null -ne $current){$part=Get-Item -LiteralPath $current -Force;if(($part.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Reparse package paths are refused.'};$parent=[IO.Path]::GetDirectoryName($current);if($parent -ceq $current){break};$current=$parent}
}
if(@($request.dependencies).Count -gt 255){throw 'Too many dependency packages.'}
Confirm-LocalPackage $request.entryPoint
foreach($dependency in @($request.dependencies)){Confirm-LocalPackage ([string]$dependency)}
if(([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value -cne $request.originalUserSid){throw 'The original user changed before deployment.'}
# No -AllUsers, developer mode, registration override, force shutdown, downgrade or unsigned-package bypass.
if(@($request.dependencies).Count -eq 0){Appx\Add-AppxPackage -Path $request.entryPoint -ErrorAction Stop}
else{Appx\Add-AppxPackage -Path $request.entryPoint -DependencyPath ([string[]]@($request.dependencies)) -ErrorAction Stop}
exit 0
