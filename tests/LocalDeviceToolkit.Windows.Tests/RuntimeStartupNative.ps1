param([string]$PackageDirectory,[string]$OutputDirectory,[string]$ExpectedUserSid)
$ErrorActionPreference='Stop'
Start-Transcript -LiteralPath (Join-Path $OutputDirectory 'startup-native.log') | Out-Null
try{
 . (Join-Path $PackageDirectory 'Runtime.ps1')
 if(Test-ToolkitElevation){throw 'The private runtime test must run without administrator rights.'}
 Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class ToolkitTestElevation {
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int type,out int value,int size,out int returned);
 public static bool IsElevated(IntPtr token){int value,size;if(!GetTokenInformation(token,20,out value,4,out size))throw new Win32Exception();return value!=0;}
}
'@
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
 try{if([ToolkitTestElevation]::IsElevated($identity.Token)){throw 'Native test token still reports elevation.'};$userSid=$identity.User.Value;if($userSid -ne $ExpectedUserSid){throw 'Native startup did not run as the standard test account.'}}finally{$identity.Dispose()}
 if(Test-Path -LiteralPath (Join-Path $PackageDirectory '.dotnet')){throw 'Native test requires a missing private runtime.'}
 # Control only global discovery in this UI fixture; the runtime download,
 # checksum verification, extraction, standard account and app launch are real.
 . (Join-Path $PackageDirectory 'Start.ps1')
 function Get-ToolkitGlobalRuntimeRoot {return (Join-Path $PackageDirectory 'absent-global')}
 $env:DOTNET_ROLL_FORWARD='Disable'
 if((Show-ToolkitStartup $PackageDirectory (Join-Path $OutputDirectory 'first-run-views') '') -ne 0){throw 'Missing-runtime installation through the native startup UI failed.'}
 if(-not (Test-ToolkitRuntime (Join-Path $PackageDirectory '.dotnet'))){throw 'The actual runtime download is incomplete.'}
 Install-ToolkitRuntime $PackageDirectory {throw 'A complete installation must not download again.'}
 foreach($mode in @('--startup-preview','--smoke-output')){
  $destination=if($mode -eq '--startup-preview'){Join-Path $OutputDirectory 'startup.png'}else{Join-Path $OutputDirectory 'views'}
  $start=New-Object Diagnostics.ProcessStartInfo((Join-Path $PackageDirectory 'Start.exe'),($mode+' "'+$destination+'"'))
  $start.UseShellExecute=$false
  $start.EnvironmentVariables['DOTNET_ROLL_FORWARD']='Disable'
  $process=[Diagnostics.Process]::Start($start)
  try{if(-not $process.WaitForExit(120000)){throw 'Native startup launcher timed out.'};if($process.ExitCode -ne 0){throw 'Native startup launcher failed.'}}finally{$process.Dispose()}
 }
 if(@(Get-ChildItem -LiteralPath (Join-Path $OutputDirectory 'views') -Filter '*.png').Count -ne 37){throw 'Native application views are incomplete.'}
 if(@(Get-ChildItem -LiteralPath (Join-Path $OutputDirectory 'first-run-views') -Filter '*.png').Count -ne 37){throw 'Missing-runtime first-run views are incomplete.'}
 # A process-scoped policy refusal must produce native feedback, not disappear.
 $blockedStart=New-Object Diagnostics.ProcessStartInfo((Join-Path $PackageDirectory 'Start.exe'))
 $blockedStart.UseShellExecute=$false;$blockedStart.EnvironmentVariables['PSExecutionPolicyPreference']='Restricted'
 $blocked=[Diagnostics.Process]::Start($blockedStart)
 try{
  $deadline=[DateTime]::UtcNow.AddSeconds(20)
  do{Start-Sleep -Milliseconds 100;$blocked.Refresh()}while(-not $blocked.HasExited -and $blocked.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
  if($blocked.HasExited -or $blocked.MainWindowTitle -ne 'Local Device Toolkit startup'){throw 'A pre-UI failure did not produce native error feedback.'}
  if(-not $blocked.CloseMainWindow() -or -not $blocked.WaitForExit(10000) -or $blocked.ExitCode -eq 0){throw 'Blocked startup did not fail closed.'}
 }finally{if(-not $blocked.HasExited){$blocked.Kill()};$blocked.Dispose()}
 & (Join-Path $PackageDirectory 'SilentDiagnosticNative.ps1') -PackageDirectory $PackageDirectory -OutputDirectory (Join-Path $OutputDirectory 'silent') -ExpectedUserSid $userSid
 $result=[ordered]@{Elevated=$false;UserSid=$userSid;PrivateRuntimeVerified=$true;MissingRuntimeUiVerified=$true;NativeLaunchVerified=$true;BlockedStartupFeedbackVerified=$true;SilentDiagnosticsVerified=$true;RuntimeVersion=$script:ToolkitRuntimeVersion;NativeViews=37;StartupPreview=$true;RelaunchRequired=$false;RebootRequired=$false;InteractiveUacAcceptance=$false}
 $result|ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'startup-verification.json') -Encoding UTF8
}catch{Write-Output $_.Exception.ToString();exit 1}finally{
 $logs=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'LocalDeviceToolkit/logs'
 if(Test-Path -LiteralPath $logs){Get-ChildItem -LiteralPath $logs -Filter 'startup-*.log' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 5 | Copy-Item -Destination $OutputDirectory}
 Stop-Transcript | Out-Null
}
