# Native CI acceptance: real downloads and WPF startup under a restricted token.
param([Parameter(Mandatory=$true)][string]$ZipPath,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new evidence directory.'}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$root=Join-Path ([IO.Path]::GetTempPath()) ('Toolkit lightweight native '+[Guid]::NewGuid().ToString('N'))
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($ZipPath),$root)
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
foreach($path in @($root,$OutputDirectory)){
 $acl=(Get-Item -LiteralPath $path).GetAccessControl()
 $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))
 (Get-Item -LiteralPath $path).SetAccessControl($acl)
}
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class ToolkitRestrictedStartup {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup {public int cb;public string reserved,desktop,title;public int x,y,xSize,ySize,xChars,yChars,fill,flags;public short show,reservedBytes;public IntPtr reserved2,input,output,error;}
 [StructLayout(LayoutKind.Sequential)] struct ProcessInfo {public IntPtr process,thread;public int pid,tid;}
 [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] static extern IntPtr GetProcessWindowStation();
 [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint thread);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder text,int bytes,out int needed);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int kind,out int value,int bytes,out int returned);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool CreateRestrictedToken(IntPtr token,uint flags,uint disabled,IntPtr sids,uint privileges,IntPtr deleted,uint restricted,IntPtr restricting,out IntPtr result);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcessAsUser(IntPtr token,string application,StringBuilder command,IntPtr processAttributes,IntPtr threadAttributes,bool inherit,uint flags,IntPtr environment,string directory,ref Startup startup,out ProcessInfo process);
 [DllImport("kernel32.dll",SetLastError=true)] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr process,out uint code);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateProcess(IntPtr process,uint code);
 static string ObjectName(IntPtr handle){int needed;var text=new StringBuilder(1024);return GetUserObjectInformation(handle,2,text,2048,out needed)?text.ToString():"unknown";}
 public static int Run(string executable,string arguments,string directory) {
  IntPtr original=IntPtr.Zero,limited=IntPtr.Zero;ProcessInfo process=new ProcessInfo();
  try{
   if(!OpenProcessToken(GetCurrentProcess(),0x02000000,out original))throw new Win32Exception();
   // DISABLE_MAX_PRIVILEGE | LUA_TOKEN: same user, without an administrator token.
   if(!CreateRestrictedToken(original,5,0,IntPtr.Zero,0,IntPtr.Zero,0,IntPtr.Zero,out limited))throw new Win32Exception();
   int elevated,returned;if(!GetTokenInformation(limited,20,out elevated,4,out returned))throw new Win32Exception();
   Console.WriteLine("Restricted token elevation="+elevated+"; desktop="+ObjectName(GetProcessWindowStation())+"\\"+ObjectName(GetThreadDesktop(GetCurrentThreadId())));
   var startup=new Startup();startup.cb=Marshal.SizeOf(typeof(Startup));
   if(!CreateProcessAsUser(limited,executable,new StringBuilder("\""+executable+"\" "+arguments),IntPtr.Zero,IntPtr.Zero,false,0x08000000,IntPtr.Zero,directory,ref startup,out process))throw new Win32Exception();
   if(WaitForSingleObject(process.process,600000)!=0){TerminateProcess(process.process,1);throw new TimeoutException("Restricted native startup timed out.");}
   uint code;if(!GetExitCodeProcess(process.process,out code))throw new Win32Exception();
   if(code!=0)throw new InvalidOperationException("Restricted startup exited with Windows status 0x"+code.ToString("X8")+"; inspect startup-native.log if present.");
   return 0;
  }finally{if(process.thread!=IntPtr.Zero)CloseHandle(process.thread);if(process.process!=IntPtr.Zero)CloseHandle(process.process);if(limited!=IntPtr.Zero)CloseHandle(limited);if(original!=IntPtr.Zero)CloseHandle(original);}
 }
}
'@
$fixture=Join-Path (Split-Path $PSScriptRoot -Parent) 'tests/LocalDeviceToolkit.Windows.Tests/RuntimeStartupNative.ps1'
foreach($path in @($root,$OutputDirectory,$fixture)){if($path.Contains('"')){throw 'Unsupported test path.'}}
$powershell=Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$arguments='-NoLogo -NoProfile -NonInteractive -STA -File "'+$fixture+'" -PackageDirectory "'+$root+'" -OutputDirectory "'+$OutputDirectory+'"'
if([ToolkitRestrictedStartup]::Run($powershell,$arguments,$root) -ne 0){throw 'Restricted native startup failed; inspect startup-native.log.'}
$result=Get-Content -LiteralPath (Join-Path $OutputDirectory 'startup-verification.json') -Raw | ConvertFrom-Json
if($result.Elevated -or -not $result.PrivateRuntimeVerified -or -not $result.NativeLaunchVerified){throw 'Native startup evidence is incomplete.'}
# The unchanged worker requires an administrator token. Its runtime/refusal check
# belongs to the elevated CI parent, never the restricted installer/UI child.
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
Write-Output ('Verified non-admin private runtime installation and startup: '+$OutputDirectory)
