[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RequestFile,[Parameter(Mandatory=$true)][string]$HelpersPath)
. $HelpersPath
try{
 switch -CaseSensitive ([string]$request.checkId){
  'local.storage.capacity' {
   $letter=Get-VolumeLetter;$v=Get-Volume -DriveLetter $letter[0];$details.volume=$letter;$details.freeBytes=[string]$v.SizeRemaining;$details.totalBytes=[string]$v.Size
   if($null -eq $v.SizeRemaining -or $null -eq $v.Size){throw 'Volume capacity unavailable.'}
   $current=([Math]::Round([double]$v.SizeRemaining/1GB,1)).ToString([Globalization.CultureInfo]::InvariantCulture)+' GB free'
   $required=Get-Input 'requiredBytes';if($null -ne $required){$details.requiredBytes=[string]$required}
  }
  'local.storage.health' {
   $letter=Get-VolumeLetter;$partition=Get-Partition -DriveLetter $letter[0];$disk=Get-Disk -Number $partition.DiskNumber
   $details.volume=$letter;$details.health=[string]$disk.HealthStatus;$details.healthProvider='Windows Storage disk health; this is not a universal SMART or failure prediction.'
   if([string]::IsNullOrWhiteSpace($details.health)){throw 'Disk health unavailable.'};$current='Disk reports '+$details.health
   $complete=$false;$limitations.Add('Hardware reliability counters, RAID/USB detail and media tests are not established by this observation.')
  }
  'local.hardware.identity' {
   $system=Get-CimInstance -ClassName Win32_ComputerSystem -Property Manufacturer,Model
   $bios=Get-CimInstance -ClassName Win32_BIOS -Property SMBIOSBIOSVersion,ReleaseDate
   $details.manufacturer=[string]$system.Manufacturer;$details.model=[string]$system.Model;$details.biosVersion=[string]$bios.SMBIOSBIOSVersion;$details.biosDate=if($null -ne $bios.ReleaseDate){([DateTimeOffset]$bios.ReleaseDate).ToUniversalTime().ToString('o')}else{'Unknown'}
   $current=$details.manufacturer+' '+$details.model;$limitations.Add('Version/date inventory does not establish that firmware is current or compatible.')
  }
  'local.hardware.problems' {
   $devices=@(Get-PnpDevice -PresentOnly | Where-Object {$_.Status -ne 'OK'});$returned=[Math]::Min(200,$devices.Count)
   $rows=foreach($device in @(Safe-Count $devices)){ $code='Unknown';try{$property=Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_Device_ProblemCode';$number=[int]$property.Data;$label=switch($number){0{'No reported problem'}1{'Not configured'}10{'Cannot start'}22{'Disabled'}28{'Driver not installed'}43{'Device reported a problem'}default{'Unrecognized problem'}};$code=$label+' ('+[string]$number+')'}catch{$complete=$false};[string]$device.FriendlyName+' | '+[string]$device.Class+' | '+[string]$device.Status+' | '+$code }
   $details.devices=$rows -join "`n";$details.coverage='Present devices only. Intentionally disabled devices are not automatically enabled.';$current=[string]$devices.Count+' present device(s) reporting a problem or unknown state'
  }
  'local.power.capabilities' {
   $output=& (Join-Path ([Environment]::GetFolderPath('System')) 'powercfg.exe') /a 2>$null;if($LASTEXITCODE -ne 0){throw 'Sleep information unavailable.'}
   $details.sleepStates=($output -join "`n");$current='Supported sleep information collected';$limitations.Add('Sleep capabilities are descriptive. No sleep model, hibernation or power setting was changed.')
  }
  default {throw 'Unregistered hardware check.'}
 }
 Write-Probe
}catch{Write-Unavailable}
