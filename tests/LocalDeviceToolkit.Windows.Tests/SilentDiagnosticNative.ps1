# Read-only acceptance on a real standard Windows account, using packaged EXE/CMD.
param([string]$PackageDirectory,[string]$OutputDirectory,[string]$ExpectedUserSid)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
[void][IO.Directory]::CreateDirectory($OutputDirectory)
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class ToolkitSilentWindows {
 delegate bool Visitor(IntPtr handle,IntPtr state);
 [DllImport("user32.dll")] static extern bool EnumWindows(Visitor visitor,IntPtr state);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr handle,StringBuilder text,int count);
 public static bool HasToolkitWindow(){
  bool found=false;
  EnumWindows(delegate(IntPtr handle,IntPtr state){
   if(!IsWindowVisible(handle))return true;
   var text=new StringBuilder(512);GetWindowText(handle,text,512);
   if(text.ToString().Replace(" ","").IndexOf("LocalDeviceToolkit",StringComparison.OrdinalIgnoreCase)>=0)found=true;
   return true;
  },IntPtr.Zero);
  return found;
 }
}
'@
$logs=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'LocalDeviceToolkit/logs'
function Invoke-SilentCase([string]$Name,[string]$File,[string]$Arguments,[int[]]$Expected,[switch]$BlockedPolicy,[string]$CmdConfig){
 $start=New-Object Diagnostics.ProcessStartInfo($File,$Arguments)
 $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WorkingDirectory=$PackageDirectory
 if($CmdConfig){$start.EnvironmentVariables['TOOLKIT_SILENT_CONFIG']=$CmdConfig}
 if($BlockedPolicy){$start.EnvironmentVariables['PSExecutionPolicyPreference']='Restricted'}else{$start.EnvironmentVariables.Remove('PSExecutionPolicyPreference')}
 $process=[Diagnostics.Process]::Start($start);$deadline=[DateTime]::UtcNow.AddMinutes(6)
 try{
  do{
   if([ToolkitSilentWindows]::HasToolkitWindow()){throw ('Unexpected toolkit pop-up: '+$Name)}
   if([DateTime]::UtcNow -ge $deadline){throw ('Silent acceptance timed out: '+$Name)}
   Start-Sleep -Milliseconds 50;$process.Refresh()
  }while(-not $process.HasExited)
  if($Expected -notcontains $process.ExitCode){throw ($Name+' returned '+$process.ExitCode)}
  return $process.ExitCode
 }finally{if(-not $process.HasExited){$process.Kill()};$process.Dispose()}
}
function Read-NewReport([int]$Before){
 $reports=@(Get-ChildItem -LiteralPath $logs -Filter '*.json')
 if($reports.Count -ne $Before+1){throw 'The silent run did not create exactly one fresh report.'}
 $file=$reports|Sort-Object LastWriteTimeUtc -Descending|Select-Object -First 1
 $data=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json
 if(-not $data.checkOnly -or $data.tasks.Count -ne 63){throw 'Silent report coverage/check-only contract failed.'}
 $log=Get-Content -LiteralPath $data.logFile -Raw
 if(-not $log.Contains($ExpectedUserSid) -or $log.Contains('Engineer confirmed') -or $log.Contains('[start]')){throw 'Silent run did not preserve the original user/read-only contract.'}
 foreach($extension in @('.log','.json','.html')){Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($file.FullName,$extension)) -Destination $OutputDirectory}
 return $data
}
$starter=Join-Path $PackageDirectory 'Start.exe'
$longPath='HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem'
$extensionPath='HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
$longBefore=[Microsoft.Win32.Registry]::GetValue($longPath,'LongPathsEnabled','<missing>')
$extensionsBefore=[Microsoft.Win32.Registry]::GetValue($extensionPath,'HideFileExt','<missing>')
$count=@(Get-ChildItem -LiteralPath $logs -Filter '*.json').Count
$allExit=Invoke-SilentCase 'all EXE' $starter '--silent --preset all' @(0,2,3)
$all=Read-NewReport $count
if($all.preset -cne 'all' -or $all.exitCode -ne $allExit){throw 'The EXE did not propagate its diagnostic result.'}
if(@($all.tasks|Where-Object {$_.taskId -eq 'local.recovery.winre' -and $_.status -eq 'Requires administrator'}).Count -ne 1){throw 'An administrator-only check was not reported without UAC.'}
if(@($all.tasks|Where-Object {$_.taskId -eq 'local.network.dns' -and $_.status -eq 'Needs input'}).Count -ne 1){throw 'A targetless DNS check was not reported as incomplete.'}
if(@($all.tasks|Where-Object {$_.taskId -eq 'local.battery.capacity' -and $_.status -ne 'Excluded timed test'}).Count -ne 1){throw 'Short battery evidence was excluded.'}
# Spaces and CMD metacharacters must remain literal file-path characters.
$config=Join-Path $OutputDirectory 'engineer checks & !notes!.config'
[IO.File]::WriteAllText($config,"# read only`r`nlongfilepaths=2`r`nshowfileextensions=2`r`n")
$count++
$fastExit=Invoke-SilentCase 'fast CMD' (Join-Path $env:SystemRoot 'System32/cmd.exe') ('/d /v:on /s /c ""'+(Join-Path $PackageDirectory 'Run-Silent.cmd')+'" fast "!TOOLKIT_SILENT_CONFIG!""') @(0,2,3) -CmdConfig $config
$fast=Read-NewReport $count
if($fast.preset -cne 'fast' -or $fast.exitCode -ne $fastExit){throw 'CMD did not preserve the preset and exit code.'}
if(@($fast.tasks|Where-Object {$_.status -eq 'Excluded by config'}).Count -ne 61){throw 'Itemised config selection failed.'}
if(@($fast.tasks|Where-Object {$_.taskId -eq 'windows.file-extensions' -and $_.status -ne 'Excluded by config'}).Count -ne 1){throw 'Selected check was skipped.'}
if((Invoke-SilentCase 'invalid preset' $starter '--silent --preset invalid' @(64)) -ne 64){throw 'Invalid arguments did not fail closed.'}
$bad=Join-Path $OutputDirectory 'apply is refused.config';[IO.File]::WriteAllText($bad,'longfilepaths=1')
if((Invoke-SilentCase 'change refused' $starter ('--silent --preset all --config "'+$bad+'"') @(64)) -ne 64){throw 'Config requested a change in diagnostic mode.'}
[void](Invoke-SilentCase 'blocked script policy' $starter '--silent --preset fast' @(1) -BlockedPolicy)
if($longBefore -cne [Microsoft.Win32.Registry]::GetValue($longPath,'LongPathsEnabled','<missing>') -or $extensionsBefore -cne [Microsoft.Win32.Registry]::GetValue($extensionPath,'HideFileExt','<missing>')){throw 'A diagnostic run changed a monitored registry setting.'}
[ordered]@{StandardAccount=$true;OriginalUserSid=$ExpectedUserSid;ExeVerified=$true;CmdVerified=$true;NoToolkitWindows=$true;NoUacRequested=$true;AllExit=$allExit;FastExit=$fastExit;DiagnosticTasks=63;ConfigZeroAndTwoVerified=$true;ConfigOneRefused=$true;BlockedPolicyNoPopup=$true;DeviceChangesApplied=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $OutputDirectory 'silent-verification.json') -Encoding UTF8
