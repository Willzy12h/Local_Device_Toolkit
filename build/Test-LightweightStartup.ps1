# Native CI acceptance using a temporary real standard Windows account.
# Account creation/cleanup belong to the elevated test runner, never app setup.
param([Parameter(Mandatory=$true)][string]$ZipPath,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$env:PSModulePath=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::System)) 'WindowsPowerShell/v1.0/Modules'
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new evidence directory.'}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$temporary=if($env:RUNNER_TEMP){$env:RUNNER_TEMP}else{[IO.Path]::GetTempPath()}
$container=Join-Path $temporary ('Toolkit lightweight native '+[Guid]::NewGuid().ToString('N'))
$root=Join-Path $container 'kit'
[void][IO.Directory]::CreateDirectory($container)
$accountName='ldt_rt_'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$password=ConvertTo-SecureString ([Guid]::NewGuid().ToString('N')+'aA!4') -AsPlainText -Force
$account=$null
try{
 $account=New-LocalUser -Name $accountName -Password $password -Description 'Disposable toolkit startup CI test'
 $users=Get-LocalGroup -SID 'S-1-5-32-545'
 if(@(Get-LocalGroupMember -Group $users.Name | Where-Object {$_.SID -eq $account.SID}).Count -eq 0){Add-LocalGroupMember -Group $users.Name -Member $account.Name}
 foreach($path in @($container,$OutputDirectory)){
  $acl=(Get-Item -LiteralPath $path).GetAccessControl()
  $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($account.SID,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))
  (Get-Item -LiteralPath $path).SetAccessControl($acl)
 }
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 [IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($ZipPath),$root)
 $fixture=Join-Path $root 'startup-native-fixture.ps1'
 Copy-Item -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'tests/LocalDeviceToolkit.Windows.Tests/RuntimeStartupNative.ps1') -Destination $fixture
 foreach($path in @($root,$OutputDirectory,$fixture)){if($path.Contains('"')){throw 'Unsupported test path.'}}
 $start=New-Object Diagnostics.ProcessStartInfo
 $start.FileName=Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
 $start.Arguments='-NoLogo -NoProfile -NonInteractive -STA -File "'+$fixture+'" -PackageDirectory "'+$root+'" -OutputDirectory "'+$OutputDirectory+'" -ExpectedUserSid "'+$account.SID.Value+'"'
 $start.UseShellExecute=$false;$start.UserName=$accountName;$start.Domain=$env:COMPUTERNAME;$start.Password=$password;$start.LoadUserProfile=$true
 $start.CreateNoWindow=$true;$start.WorkingDirectory=$root;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 $process=[Diagnostics.Process]::Start($start)
 try{
  $output=$process.StandardOutput.ReadToEndAsync();$errors=$process.StandardError.ReadToEndAsync()
  if(-not $process.WaitForExit(600000)){$process.Kill();throw 'Standard-account native startup timed out.'}
  [IO.File]::WriteAllText((Join-Path $OutputDirectory 'process-output.log'),($output.GetAwaiter().GetResult()+$errors.GetAwaiter().GetResult()))
  if($process.ExitCode -ne 0){throw 'Standard-account native startup failed; inspect process-output.log and startup-native.log.'}
 }finally{$process.Dispose()}
 $result=Get-Content -LiteralPath (Join-Path $OutputDirectory 'startup-verification.json') -Raw | ConvertFrom-Json
 if($result.Elevated -or $result.UserSid -ne $account.SID.Value -or -not $result.PrivateRuntimeVerified -or -not $result.NativeLaunchVerified){throw 'Native startup evidence is incomplete.'}
# The unchanged worker requires an administrator token. Its runtime/refusal check
# belongs to the elevated CI parent, never the standard-account installer/UI child.
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
try{$administrator=(New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)}finally{$identity.Dispose()}
if($administrator){
 # Launch with no request: the existing worker must load its private runtime and
 # display its managed refusal. No protected action or UAC approval is exercised.
 $workerStart=New-Object Diagnostics.ProcessStartInfo((Join-Path $root 'worker/LocalDeviceToolkit.Worker.exe'))
 $workerStart.UseShellExecute=$false
 $trace=Join-Path $OutputDirectory 'worker-host.log'
 $workerStart.EnvironmentVariables['DOTNET_HOST_TRACE']='1';$workerStart.EnvironmentVariables['DOTNET_HOST_TRACEFILE']=$trace
 $workerStart.EnvironmentVariables['COREHOST_TRACE']='1';$workerStart.EnvironmentVariables['COREHOST_TRACEFILE']=$trace
 $worker=[Diagnostics.Process]::Start($workerStart)
 try{
  $deadline=[DateTime]::UtcNow.AddSeconds(20)
  do{Start-Sleep -Milliseconds 100;$worker.Refresh()}while(-not $worker.HasExited -and $worker.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
  if($worker.HasExited -or $worker.MainWindowTitle -notlike '*protected worker*'){throw 'Worker did not reach its managed refusal.'}
  if(-not $worker.CloseMainWindow() -or -not $worker.WaitForExit(10000) -or $worker.ExitCode -ne 2){throw 'Worker did not refuse a missing request.'}
 }finally{if(-not $worker.HasExited){$worker.Kill()};$worker.Dispose()}
 $privatePath=Join-Path $root '.dotnet'
 if(@(Get-Content -LiteralPath $trace | Where-Object {$_ -match 'CoreCLR path' -and $_.Contains($privatePath)}).Count -eq 0){throw 'Worker did not resolve the private Core runtime.'}
 $result | Add-Member -NotePropertyName WorkerPrivateRuntimeVerified -NotePropertyValue $true
 $result | Add-Member -NotePropertyName WorkerTestElevated -NotePropertyValue $true
 $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'startup-verification.json') -Encoding UTF8
}
 Write-Output ('Verified standard-account private runtime installation and startup: '+$OutputDirectory)
}finally{
 if($null -ne $account){Remove-LocalUser -Name $accountName}
 $password.Dispose()
}
