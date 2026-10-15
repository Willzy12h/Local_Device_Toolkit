# Fixed current-user operations only. Never used by the administrator worker.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RequestFile)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$WarningPreference='Stop'
if($PSVersionTable.PSVersion.Major -ne 5){throw 'Windows PowerShell 5.1 is required.'}
if((Get-Item -LiteralPath $RequestFile).Length -gt 65536){throw 'Request exceeds the supported size.'}
$request=Get-Content -LiteralPath $RequestFile -Raw -Encoding UTF8 | ConvertFrom-Json
if($request.schemaVersion -ne 1 -or @($request.PSObject.Properties).Count -ne 4 -or @('ReadRegion','ApplyRegion','ReadConsumerCopilot','RemoveConsumerCopilot') -cnotcontains $request.action){throw 'Unregistered current-user request.'}
$sid=([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
if($sid -cne $request.originalUserSid){throw 'The original user SID does not match this Windows process.'}
function Read-Region {
 $override=Get-WinDefaultInputMethodOverride
 $tip=if($null -eq $override -or [string]::IsNullOrWhiteSpace([string]$override.InputMethodTip)){$null}else{[string]$override.InputMethodTip}
 $languages=@(Get-WinUserLanguageList | ForEach-Object {
  if($null -eq $_.Handwriting -or $null -eq $_.Spellchecking){throw 'Language preferences were incomplete.'}
  [pscustomobject][ordered]@{languageTag=[string]$_.LanguageTag;inputMethodTips=@($_.InputMethodTips | ForEach-Object {[string]$_});handwriting=[bool]$_.Handwriting;spellchecking=[bool]$_.Spellchecking}
 })
 return [pscustomobject][ordered]@{culture=[string](Get-Culture).Name;homeGeoId=[int](Get-WinHomeLocation).GeoId;defaultInputMethodOverride=$tip;languages=$languages}
}
function Get-ConsumerPackages {
 $packages=@(Get-AppxPackage -Name Microsoft.Copilot | Sort-Object PackageFullName)
 foreach($package in $packages){
  if($package.Name -cne 'Microsoft.Copilot' -or $package.PackageFamilyName -cne 'Microsoft.Copilot_8wekyb3d8bbwe' -or $package.PublisherId -cne '8wekyb3d8bbwe'){throw 'The consumer package identity is ambiguous; nothing can be removed.'}
 }
 return ,$packages
}
function Read-ConsumerCopilot {
 $packages=Get-ConsumerPackages
 return [pscustomobject][ordered]@{packages=@($packages | ForEach-Object {
  [pscustomobject][ordered]@{packageFullName=[string]$_.PackageFullName;packageFamilyName=[string]$_.PackageFamilyName;publisherId=[string]$_.PublisherId;version=[string]$_.Version}
 })}
}
function Confirm-PolicySources {
 $paths=if($request.action -eq 'ApplyRegion'){@('SOFTWARE\Policies\Microsoft\Control Panel\International','SOFTWARE\Policies\Microsoft\MUI\Settings','SOFTWARE\Microsoft\PolicyManager\current\device\TimeLanguageSettings')}else{@('SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot','SOFTWARE\Microsoft\PolicyManager\current\device\WindowsAI')}
 foreach($hive in @([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryHive]::CurrentUser)){
  $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,[Microsoft.Win32.RegistryView]::Registry64)
  try{foreach($path in $paths){$key=$base.OpenSubKey($path,$false);if($null -ne $key){try{if($key.ValueCount -gt 0 -or $key.SubKeyCount -gt 0){throw 'A related management policy source is present.'}}finally{$key.Dispose()}}}}finally{$base.Dispose()}
 }
}
function Confirm-Before($Current){
 if($null -eq $request.expectedBefore){throw 'Mutation requires an exact reviewed before state.'}
 if(($Current | ConvertTo-Json -Depth 12 -Compress) -cne ($request.expectedBefore | ConvertTo-Json -Depth 12 -Compress)){throw 'The current-user settings changed before the operation.'}
}
$value=$null;$completed=$false;$reason='The operation did not complete; inspect the device before retrying.'
try{
 switch -CaseSensitive ($request.action){
  'ReadRegion' {if($null -ne $request.expectedBefore){throw 'Read requests cannot contain mutation inputs.'};$value=Read-Region}
  'ReadConsumerCopilot' {if($null -ne $request.expectedBefore){throw 'Read requests cannot contain mutation inputs.'};$value=Read-ConsumerCopilot}
  'ApplyRegion' {
   Confirm-PolicySources
   Confirm-Before (Read-Region)
   $languages=Get-WinUserLanguageList
   $uk=@($languages | Where-Object {$_.LanguageTag -ieq 'en-GB'})
   if($uk.Count -eq 0){$languages.Add((New-WinUserLanguageList -Language en-GB)[0]);$uk=@($languages | Where-Object {$_.LanguageTag -ieq 'en-GB'})}
   if($uk.Count -ne 1){throw 'The UK language entry is ambiguous.'}
   if($uk[0].InputMethodTips -notcontains '0809:00000809'){$uk[0].InputMethodTips.Add('0809:00000809')}
   # International cmdlets may warn about a new sign-in; fresh readback, not warning text, determines success.
   Set-WinUserLanguageList -LanguageList $languages -Force -WarningAction SilentlyContinue
   Set-WinDefaultInputMethodOverride -InputTip '0809:00000809' -WarningAction SilentlyContinue
   Set-Culture -CultureInfo en-GB -WarningAction SilentlyContinue
   Set-WinHomeLocation -GeoId 242 -WarningAction SilentlyContinue
   # Never change system locale, welcome/default-user profiles or remove existing languages.
  }
  'RemoveConsumerCopilot' {
   Confirm-PolicySources
   Confirm-Before (Read-ConsumerCopilot)
   foreach($package in (Get-ConsumerPackages)){Remove-AppxPackage -Package $package.PackageFullName}
   # No AllUsers/provisioning removal. Microsoft 365/Office packages never match this identity.
  }
 }
 $completed=$true;$reason='Fixed operation completed; mutations still require an independent fresh readback.'
}catch{$value=$null}
$result=[pscustomobject][ordered]@{schemaVersion=1;action=[string]$request.action;originalUserSid=$sid;completed=$completed;value=$value;capturedAt=[DateTimeOffset]::UtcNow.ToString('o');reason=$reason}
$json=$result | ConvertTo-Json -Depth 16 -Compress
$bytes=(New-Object Text.UTF8Encoding($false)).GetBytes($json)
if($bytes.Length -gt 1048576){throw 'Result exceeds the supported limit.'}
$file=[IO.File]::Open($RequestFile+'.result.json',[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
try{$file.Write($bytes,0,$bytes.Length);$file.Flush($true)}finally{$file.Dispose()}
if(-not $completed){exit 1}
