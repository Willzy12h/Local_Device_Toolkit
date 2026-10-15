[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RequestFile,[Parameter(Mandatory=$true)][string]$HelpersPath)
. $HelpersPath
try{
 switch -CaseSensitive ([string]$request.checkId){
  'local.os.support' {
   $key=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64)
   try{$cv=$key.OpenSubKey('SOFTWARE\Microsoft\Windows NT\CurrentVersion',$false);try{
    $details.edition=[string]$cv.GetValue('EditionID');$details.release=[string]$cv.GetValue('DisplayVersion');$details.build=[string]$cv.GetValue('CurrentBuildNumber')+'.'+[string]$cv.GetValue('UBR')
   }finally{if($null -ne $cv){$cv.Dispose()}}}finally{$key.Dispose()}
   $details.architecture=if([Environment]::Is64BitOperatingSystem){'64-bit (native architecture verified separately)'}else{'32-bit'}
   if([string]::IsNullOrWhiteSpace($details.edition) -or [string]::IsNullOrWhiteSpace($details.release)){throw 'OS identity incomplete.'}
   $current=$details.edition+' '+$details.release+'; build '+$details.build
  }
  'local.restart.reasons' {
   $reasons=New-Object 'Collections.Generic.List[string]'
   $paths=@(@('SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending','Component servicing'),@('SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired','Windows Update'))
   $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64)
   try{
    foreach($pair in $paths){try{$k=$base.OpenSubKey($pair[0],$false);if($null -ne $k){$reasons.Add($pair[1]);$k.Dispose()}}catch{$complete=$false;$limitations.Add('One restart indicator could not be read.')}}
    try{$k=$base.OpenSubKey('SYSTEM\CurrentControlSet\Control\Session Manager',$false);if($null -ne $k){try{if($k.GetValueNames() -contains 'PendingFileRenameOperations'){$reasons.Add('File rename indicator present (contents not collected)')}}finally{$k.Dispose()}}else{$complete=$false}}catch{$complete=$false;$limitations.Add('The file-rename indicator could not be read.')}
   }finally{$base.Dispose()}
   $current=[string]$reasons.Count+' local restart indicator(s)';$details.reasons=($reasons -join '; ');$details.coverage='Named local indicators only; not a universal restart detector.'
  }
  'local.update.history' {
   $count=[int](Get-Input 'count' 20);if($count -lt 1 -or $count -gt 200){throw 'Invalid history bound.'}
   $session=New-Object -ComObject Microsoft.Update.Session
   try{$searcher=$session.CreateUpdateSearcher();try{
    $total=$searcher.GetTotalHistoryCount();$take=[Math]::Min($total,$count);$rows=New-Object 'Collections.Generic.List[string]'
    if($take -gt 0){$history=$searcher.QueryHistory(0,$take);try{foreach($entry in $history){$rows.Add(([DateTimeOffset]$entry.Date).ToUniversalTime().ToString('o')+' | '+[string]$entry.Title+' | result '+[string]$entry.ResultCode+' | HRESULT '+[string]$entry.HResult)}}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($history)}}
    $returned=$take;$current=[string]$take+' local update-history record(s)';$details.updates=$rows -join "`n";$details.coverage='Local history only. No online update search, download or install.'
    if($total -gt $take){$complete=$false;$limitations.Add('Older update-history records were not collected.')}
   }finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($searcher)}}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($session)}
  }
  'local.recovery.winre' {
   $output=& (Join-Path ([Environment]::GetFolderPath('System')) 'reagentc.exe') /info 2>$null
   if($LASTEXITCODE -ne 0){throw 'Recovery information unavailable.'};$text=$output -join "`n"
   if($text -notmatch '(?m)^\s*Windows RE status:\s*(Enabled|Disabled)\s*$'){throw 'Recovery output language or format is not recognized.'}
   $details.winreStatus=$Matches[1];$current='Windows recovery: '+$Matches[1]
   if($text -match '(?m)^\s*Windows RE location:\s*(.*)$'){$details.winreLocation=$Matches[1].Trim()}
   $limitations.Add('Recovery registration is not a successful recovery test.')
  }
  'local.bitlocker.state' {
   $letter=Get-VolumeLetter;$volume=Get-CimInstance -Namespace 'root\CIMV2\Security\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -Filter ("DriveLetter = '"+$letter+"'")
   if($null -eq $volume){throw 'Selected volume encryption state unavailable.'}
   $conversion=Invoke-CimMethod -InputObject $volume -MethodName GetConversionStatus;Check-Return $conversion
   $protection=Invoke-CimMethod -InputObject $volume -MethodName GetProtectionStatus;Check-Return $protection
   $protectors=Invoke-CimMethod -InputObject $volume -MethodName GetKeyProtectors -Arguments @{KeyProtectorType=[uint32]0};Check-Return $protectors
   $ids=New-Object 'Collections.Generic.List[string]'
   foreach($id in @($protectors.VolumeKeyProtectorID)){if([string]::IsNullOrWhiteSpace($id)){continue};$type=Invoke-CimMethod -InputObject $volume -MethodName GetKeyProtectorType -Arguments @{VolumeKeyProtectorID=[string]$id};Check-Return $type;$ids.Add([string]$id+'; type '+[string]$type.KeyProtectorType)}
   $conversionName=switch([int]$conversion.ConversionStatus){0{'Fully decrypted'}1{'Fully encrypted'}2{'Encryption in progress'}3{'Decryption in progress'}4{'Encryption paused'}5{'Decryption paused'}default{'Unknown conversion state'}}
   $details.volume=$letter;$details.conversion=$conversionName+' ('+[string]$conversion.ConversionStatus+')';$details.protection=switch([int]$protection.ProtectionStatus){0{'Off'}1{'On'}default{'Unknown'}}
   $details.suspended='Not established separately; protection Off can have several causes.';$details.protectors=$ids -join "`n";$details.recoveryVerification='Not externally verified. No recovery password or external key was retrieved.'
   $current='Protection '+$details.protection+'; '+$details.conversion
  }
  default {throw 'Unregistered readiness check.'}
 }
 Write-Probe
}catch{Write-Unavailable}
