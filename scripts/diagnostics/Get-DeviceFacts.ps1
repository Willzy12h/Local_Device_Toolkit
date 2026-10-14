# Read-only facts. Windows PowerShell 5.1; no execution-policy changes, network calls or secrets.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RequestFile)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$WarningPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
$request=Get-Content -LiteralPath $RequestFile -Raw | ConvertFrom-Json
$allowed=@('os.version','os.architecture','secureboot.enabled','tpm.ready','bitlocker.protected','codeintegrity.enforced','antivirus.active','antivirus.providers','extensions.visible','paths.enabled','faststartup.enabled','keyboard.uk','region.culture','timezone.id','power.acsleep.seconds','laps.configured','onedrive.configured','onedrive.filesondemand.configured','edge.configured','store.installed','storageSense.configured','apps.inventory','policy.longpaths','policy.extensions','policy.faststartup','policy.acsleep','policy.timezone','policy.region','policy.copilot')
if($request.schemaVersion -ne 1 -or @($request.checkIds).Count -gt 128 -or @($request.checkIds).Count -eq 0){throw 'Invalid facts request.'}
if(@($request.checkIds | Select-Object -Unique).Count -ne @($request.checkIds).Count){throw 'Duplicate checks.'}
foreach($check in $request.checkIds){if($allowed -cnotcontains $check){throw 'Unregistered check.'}}
function Read-RegistryValue([Microsoft.Win32.RegistryHive]$Hive,[string]$Path,[string]$Name){
 $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive,[Microsoft.Win32.RegistryView]::Registry64)
 try{
  $key=$base.OpenSubKey($Path,$false)
  if($null -eq $key){throw [System.IO.FileNotFoundException]::new('The setting key is absent; its effective default was not inferred.')}
  try{if($key.GetValueNames() -notcontains $Name){throw [System.IO.FileNotFoundException]::new('The setting value is absent; its effective default was not inferred.')};return $key.GetValue($Name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}finally{$key.Dispose()}
 }finally{$base.Dispose()}
}
function Has-RegistryValue([Microsoft.Win32.RegistryHive]$Hive,[string]$Path,[string]$Name){
 $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive,[Microsoft.Win32.RegistryView]::Registry64)
 try{$key=$base.OpenSubKey($Path,$false);if($null -eq $key){return $false};try{return ($key.GetValueNames() -contains $Name)}finally{$key.Dispose()}}finally{$base.Dispose()}
}
function Read-Inventory {
 $rows=New-Object 'System.Collections.Generic.List[object]'
 foreach($hive in @([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryHive]::CurrentUser)){
  foreach($view in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)){
   $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,$view)
   try{
    $root=$base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',$false)
    if($null -eq $root){continue}
    try{
     foreach($name in $root.GetSubKeyNames()){
      $key=$root.OpenSubKey($name,$false);if($null -eq $key){throw 'An inventory entry disappeared.'}
      try{
       $title=$key.GetValue('DisplayName',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
       if([string]::IsNullOrWhiteSpace($title)){continue}
       $scope=if($hive -eq [Microsoft.Win32.RegistryHive]::CurrentUser){'CurrentUser'}else{'Machine'}
       $rows.Add([pscustomobject]@{identity="$hive/$view/$name";name=[string]$title;version=$key.GetValue('DisplayVersion');publisher=$key.GetValue('Publisher');scope=$scope})
      }finally{$key.Dispose()}
     }
    }finally{$root.Dispose()}
   }finally{$base.Dispose()}
  }
 }
 foreach($app in @(Get-AppxPackage)){$rows.Add([pscustomobject]@{identity=$app.PackageFullName;name=$app.Name;version=[string]$app.Version;publisher=$app.Publisher;scope='CurrentUser'})}
 if($rows.Count -gt 1000){throw 'Inventory exceeds the supported limit; no truncated result was accepted.'}
 return ,$rows.ToArray()
}
# A fixed, read-only native power API avoids parsing localized powercfg output.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LocalToolkitPowerRead {
 [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root,out IntPtr scheme);
 [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root,ref Guid scheme,ref Guid subgroup,ref Guid setting,out uint value);
 [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
 public static uint AcSleep() {
  IntPtr pointer; uint result=PowerGetActiveScheme(IntPtr.Zero,out pointer); if(result!=0)throw new System.ComponentModel.Win32Exception((int)result);
  try {var scheme=(Guid)Marshal.PtrToStructure(pointer,typeof(Guid));var sub=new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20");var setting=new Guid("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");uint value;
   result=PowerReadACValueIndex(IntPtr.Zero,ref scheme,ref sub,ref setting,out value);if(result!=0)throw new System.ComponentModel.Win32Exception((int)result);return value;
  }finally{LocalFree(pointer);}
 }
}
'@
$facts=New-Object 'System.Collections.Generic.List[object]'
foreach($check in $request.checkIds){
 $state='Known';$value=$null;$reason='Observed locally; cloud assignment, licensing and escrow require separate verification.'
 try{
  switch -CaseSensitive ($check){
   'os.version' { $os=Get-CimInstance -ClassName Win32_OperatingSystem; $value=[string]$os.Version }
   'os.architecture' { $value=if([Environment]::Is64BitOperatingSystem){'x64'}else{'x86'} }
   'secureboot.enabled' { $value=[bool](Confirm-SecureBootUEFI) }
   'tpm.ready' { $tpm=Get-Tpm;if($null -eq $tpm.TpmReady -or $null -eq $tpm.TpmPresent){throw 'TPM response incomplete.'};$value=($tpm.TpmPresent -and $tpm.TpmReady) }
   'bitlocker.protected' { $volume=Get-BitLockerVolume -MountPoint $env:SystemDrive;if($null -eq $volume.ProtectionStatus){throw 'Protection status unavailable.'};$value=([int]$volume.ProtectionStatus -eq 1) }
   'codeintegrity.enforced' { $guard=Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard;if($null -eq $guard.CodeIntegrityPolicyEnforcementStatus){throw 'Code integrity status unavailable.'};$value=([int]$guard.CodeIntegrityPolicyEnforcementStatus -eq 2) }
   'antivirus.active' { $av=Get-MpComputerStatus;if($av.AntivirusEnabled -and $av.RealTimeProtectionEnabled){$value=$true}else{throw 'Defender is not active; third-party protection needs separate verification.'} }
   'antivirus.providers' { $value=@(Get-CimInstance -Namespace root\SecurityCenter2 -ClassName AntivirusProduct | ForEach-Object {[string]$_.displayName}) }
   'extensions.visible' { $value=((Read-RegistryValue CurrentUser 'SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'HideFileExt') -eq 0) }
   'paths.enabled' { $value=((Read-RegistryValue LocalMachine 'SYSTEM\CurrentControlSet\Control\FileSystem' 'LongPathsEnabled') -eq 1) }
   'faststartup.enabled' { $value=((Read-RegistryValue LocalMachine 'SYSTEM\CurrentControlSet\Control\Session Manager\Power' 'HiberbootEnabled') -eq 1) }
   'keyboard.uk' { $value=(@(Get-WinUserLanguageList | ForEach-Object {$_.InputMethodTips} | Where-Object {$_ -eq '0809:00000809'}).Count -gt 0) }
   'region.culture' { $value=[string](Get-Culture).Name }
   'timezone.id' { $value=[string](Get-TimeZone).Id }
   'power.acsleep.seconds' { $value=[LocalToolkitPowerRead]::AcSleep() }
   'laps.configured' { $value=Has-RegistryValue LocalMachine 'SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\LAPS' 'BackupDirectory' }
   'onedrive.configured' { $value=Has-RegistryValue CurrentUser 'SOFTWARE\Microsoft\OneDrive\Accounts\Business1' 'UserFolder' }
   'onedrive.filesondemand.configured' { $value=((Read-RegistryValue LocalMachine 'SOFTWARE\Policies\Microsoft\OneDrive' 'FilesOnDemandEnabled') -eq 1) }
   'edge.configured' { $value=Has-RegistryValue LocalMachine 'SOFTWARE\Policies\Microsoft\Edge' 'BrowserSignin' }
   'store.installed' { $value=(@(Get-AppxPackage -Name Microsoft.WindowsStore).Count -gt 0) }
   'storageSense.configured' { $value=Has-RegistryValue CurrentUser 'SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy' '01' }
   'apps.inventory' { $value=Read-Inventory }
   # These checks report the presence of the explicitly listed sources only, never "unmanaged".
   'policy.longpaths' { $value=Has-RegistryValue LocalMachine 'SOFTWARE\Policies\Microsoft\Windows\System' 'LongPathsEnabled' }
   'policy.extensions' { $value=(Has-RegistryValue CurrentUser 'SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer' 'HideFileExt') -or (Has-RegistryValue LocalMachine 'SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer' 'HideFileExt') }
   'policy.faststartup' { $value=Has-RegistryValue LocalMachine 'SOFTWARE\Policies\Microsoft\Windows\System' 'HiberbootEnabled' }
   'policy.acsleep' { $value=Has-RegistryValue LocalMachine 'SOFTWARE\Policies\Microsoft\Power\PowerSettings\238c9fa8-0aad-41ed-83f4-97be242c8f20\29f6c1db-86da-48c5-9fdb-f2b67b1f44da' 'ACSettingIndex' }
   'policy.timezone' { throw 'Automatic time zone and device-management sources require engineer verification before changing time zone.' }
   'policy.region' { throw 'Regional policy sources require engineer verification before changing language settings.' }
   'policy.copilot' { throw 'Package assignment and tenant policy require engineer verification before removing Copilot.' }
   default {throw 'Unregistered check.'}
  }
  if($null -eq $value){throw 'The probe returned no value.'}
 }catch{
  $state='Unavailable';$value=$null;$reason='The local check failed or is unsupported; no successful result was inferred.'
  $exception=$_.Exception
  while($null -ne $exception.InnerException){$exception=$exception.InnerException}
  if($exception -is [System.UnauthorizedAccessException] -or $exception -is [System.Security.SecurityException] -or $exception.HResult -eq -2147024891){$state='AccessDenied';$reason='The Windows API denied access. Request a protected read explicitly.'}
 }
 $facts.Add([pscustomobject]@{checkId=$check;state=$state;value=$value;source='Windows PowerShell 5.1 / fixed local Windows API probe';capturedAt=[DateTimeOffset]::UtcNow.ToString('o');reason=$reason})
}
[pscustomobject]@{schemaVersion=1;facts=@($facts.ToArray())} | ConvertTo-Json -Depth 12 -Compress
