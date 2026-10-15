[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RequestFile,[Parameter(Mandatory=$true)][string]$HelpersPath)
. $HelpersPath
try{
 switch -CaseSensitive ([string]$request.checkId){
  'local.defender.protection' {
   $mp=Get-MpComputerStatus;$mode=$mp.PSObject.Properties['AMRunningMode'];if($null -eq $mode){throw 'Defender mode unavailable.'}
   $details.mode=[string]$mode.Value;$details.protectionEnabled=[string]($mp.AntivirusEnabled -and $mp.RealTimeProtectionEnabled);$current=$details.mode+'; protection '+$details.protectionEnabled
   $limitations.Add('Passive mode and third-party product names do not establish active protection. No security setting was changed.')
  }
  'local.defender.signatures' {
   $mp=Get-MpComputerStatus;$details.signatureVersion=[string]$mp.AntivirusSignatureVersion
   if($null -eq $mp.AntivirusSignatureLastUpdated){throw 'Signature time unavailable.'};$details.signatureTime=([DateTimeOffset]$mp.AntivirusSignatureLastUpdated).ToUniversalTime().ToString('o')
   $preference=Get-MpPreference;$order=$preference.PSObject.Properties['SignatureFallbackOrder'];if($null -eq $order){throw 'Update source policy unavailable.'}
   $details.sourceOrder=if([string]::IsNullOrWhiteSpace([string]$order.Value)){'Not configured; documented Windows default fallback may apply.'}else{[string]$order.Value}
   $current='Signature '+$details.signatureVersion+'; '+$details.signatureTime;$limitations.Add('Installed signature age does not establish whether a newer update exists. No update was requested.')
  }
  'local.time.status' {
   $service=Get-Service -Name W32Time;$details.serviceState=[string]$service.Status
   $system=[Environment]::GetFolderPath('System');$source=& (Join-Path $system 'w32tm.exe') /query /source 2>$null;if($LASTEXITCODE -ne 0){throw 'Time source unavailable.'}
   $details.source=($source -join ' ').Trim();$details.lastSync='Unknown';$output=& (Join-Path $system 'w32tm.exe') /query /status 2>$null
   if($LASTEXITCODE -eq 0 -and ($output -join "`n") -match '(?m)^Last Successful Sync Time:\s*(.+)$'){$details.lastSync=$Matches[1].Trim()}else{$complete=$false;$limitations.Add('Last successful sync time was not established in this output language/format.')}
   $current='Time service '+$details.serviceState+'; source '+$details.source;$limitations.Add('Local status is not an independent clock accuracy measurement. No time resync or source change was made.')
  }
  'local.print.spooler' {
   $service=Get-CimInstance -ClassName Win32_Service -Filter "Name='Spooler'" -Property State,StartMode
   if($null -eq $service){throw 'Spooler state unavailable.'};$details.serviceState=[string]$service.State;$details.startupMode=[string]$service.StartMode;$current='Print Spooler: '+$details.serviceState
  }
  'local.print.queue' {
   $name=[string](Get-Input 'printerName');if([string]::IsNullOrWhiteSpace($name) -or $name.Length -gt 256){throw 'Choose a printer.'}
   $printers=@(Get-Printer | Where-Object {$_.Name -ceq $name});if($printers.Count -ne 1){throw 'Printer identity unavailable or ambiguous.'};$printer=$printers[0]
   if([string]$printer.Type -eq 'Connection' -and -not [bool](Get-Input 'allowRemoteRead' $false)){$limitations.Add('This is a shared queue. Explicitly confirm network access before reading it.');throw 'Remote queue not approved.'}
   $jobs=@(Get-PrintJob -PrinterName $printer.Name);$returned=[Math]::Min(200,$jobs.Count)
   $details.printer=[string]$printer.Name;$details.jobs=(@(Safe-Count $jobs | ForEach-Object {'Job '+[string]$_.ID+' | '+[string]$_.JobStatus}) -join "`n")
   $details.coverage='Selected queue only. Document names and job owners were not collected into evidence.';$current=[string]$jobs.Count+' job(s) in selected queue'
  }
  default {throw 'Unregistered security/service check.'}
 }
 Write-Probe
}catch{Write-Unavailable}
