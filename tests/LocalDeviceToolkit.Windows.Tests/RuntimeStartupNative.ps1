param([string]$PackageDirectory,[string]$OutputDirectory)
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
 [DllImport("advapi32.dll")] static extern int RegOverridePredefKey(IntPtr key,IntPtr replacement);
 public static bool IsElevated(IntPtr token){int value,size;if(!GetTokenInformation(token,20,out value,4,out size))throw new Win32Exception();return value!=0;}
 public static void OverrideMachineRegistry(IntPtr replacement){int error=RegOverridePredefKey(new IntPtr(unchecked((int)0x80000002)),replacement);if(error!=0)throw new Win32Exception(error);}
}
'@
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
 try{if([ToolkitTestElevation]::IsElevated($identity.Token)){throw 'Native test token still reports elevation.'}}finally{$identity.Dispose()}
 if(Test-Path -LiteralPath (Join-Path $PackageDirectory '.dotnet')){throw 'Native test requires a missing private runtime.'}
 # Isolate only this PowerShell process's registry view. A fake registered
 # location forces the missing-runtime UI path even if the runner has .NET.
 # No real HKLM setting changes, and the launched application is unaffected.
 $fixtureName='Software\LocalDeviceToolkit\RuntimeTest-'+[Guid]::NewGuid().ToString('N')
 $fixture=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($fixtureName)
 $registered=$fixture.CreateSubKey('SOFTWARE\dotnet\Setup\InstalledVersions\x64')
 try{$registered.SetValue('InstallLocation',(Join-Path $PackageDirectory 'absent-global'))}finally{$registered.Dispose()}
 try{
  [ToolkitTestElevation]::OverrideMachineRegistry($fixture.Handle)
  $global:LASTEXITCODE=0;$env:DOTNET_ROLL_FORWARD='Disable'
  & (Join-Path $PackageDirectory 'Start.ps1') -SmokeOutput (Join-Path $OutputDirectory 'first-run-views')
  if($LASTEXITCODE -ne 0){throw 'Missing-runtime installation through the native startup UI failed.'}
 }finally{[ToolkitTestElevation]::OverrideMachineRegistry([IntPtr]::Zero);$fixture.Dispose();[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($fixtureName)}
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
 $result=[ordered]@{Elevated=$false;PrivateRuntimeVerified=$true;MissingRuntimeUiVerified=$true;NativeLaunchVerified=$true;BlockedStartupFeedbackVerified=$true;RuntimeVersion=$script:ToolkitRuntimeVersion;NativeViews=37;StartupPreview=$true;RelaunchRequired=$false;RebootRequired=$false;InteractiveUacAcceptance=$false}
 $result|ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'startup-verification.json') -Encoding UTF8
}catch{Write-Output $_.Exception.ToString();exit 1}finally{
 $logs=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'LocalDeviceToolkit/logs'
 if(Test-Path -LiteralPath $logs){Get-ChildItem -LiteralPath $logs -Filter 'startup-*.log' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 5 | Copy-Item -Destination $OutputDirectory}
 Stop-Transcript | Out-Null
}
