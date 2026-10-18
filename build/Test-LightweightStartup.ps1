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
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
public static class ToolkitRestrictedStartup {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup {public int cb;public string reserved,desktop,title;public int x,y,xSize,ySize,xChars,yChars,fill,flags;public short show,reservedBytes;public IntPtr reserved2,input,output,error;}
 [StructLayout(LayoutKind.Sequential)] struct ProcessInfo {public IntPtr process,thread;public int pid,tid;}
 [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] static extern IntPtr GetProcessWindowStation();
 [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint thread);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenWindowStation(string name,bool inherit,uint access);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenDesktop(string name,uint flags,bool inherit,uint access);
 [DllImport("user32.dll")] static extern bool CloseWindowStation(IntPtr handle);
 [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr handle);
 [DllImport("user32.dll",SetLastError=true)] static extern bool GetUserObjectSecurity(IntPtr handle,ref uint information,byte[] descriptor,uint bytes,out uint needed);
 [DllImport("user32.dll",SetLastError=true)] static extern bool SetUserObjectSecurity(IntPtr handle,ref uint information,byte[] descriptor);
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
 static byte[] AllowCurrentUser(IntPtr handle,int rights){
  uint information=4,needed;GetUserObjectSecurity(handle,ref information,null,0,out needed);
  if(needed==0||needed>65536)throw new Win32Exception();var original=new byte[needed];
  if(!GetUserObjectSecurity(handle,ref information,original,needed,out needed))throw new Win32Exception();
  var descriptor=new RawSecurityDescriptor(original,0);
  if(descriptor.DiscretionaryAcl!=null){
   using(var identity=WindowsIdentity.GetCurrent())descriptor.DiscretionaryAcl.InsertAce(descriptor.DiscretionaryAcl.Count,new CommonAce(AceFlags.None,AceQualifier.AccessAllowed,rights,identity.User,false,null));
   var updated=new byte[descriptor.BinaryLength];descriptor.GetBinaryForm(updated,0);
   if(!SetUserObjectSecurity(handle,ref information,updated))throw new Win32Exception();
  }
  return original;
 }
 static void Restore(IntPtr handle,byte[] original){if(handle!=IntPtr.Zero&&original!=null){uint information=4;if(!SetUserObjectSecurity(handle,ref information,original))throw new Win32Exception();}}
 public static int Run(string executable,string arguments,string directory) {
  IntPtr original=IntPtr.Zero,limited=IntPtr.Zero,station=IntPtr.Zero,desktop=IntPtr.Zero;byte[] stationAcl=null,desktopAcl=null;ProcessInfo process=new ProcessInfo();
  try{
   if(!OpenProcessToken(GetCurrentProcess(),0x02000000,out original))throw new Win32Exception();
   // DISABLE_MAX_PRIVILEGE | LUA_TOKEN: same user, without an administrator token.
   if(!CreateRestrictedToken(original,5,0,IntPtr.Zero,0,IntPtr.Zero,0,IntPtr.Zero,out limited))throw new Win32Exception();
   int elevated,returned;if(!GetTokenInformation(limited,20,out elevated,4,out returned))throw new Win32Exception();
   Console.WriteLine("Restricted token elevation="+elevated+"; desktop="+ObjectName(GetProcessWindowStation())+"\\"+ObjectName(GetThreadDesktop(GetCurrentThreadId())));
   // CI's desktop grants its elevated runner access through Administrators.
   // Give the SAME user explicit access while its filtered child runs, then
   // restore both original DACLs. This is test infrastructure, not app setup.
   var stationName=ObjectName(GetProcessWindowStation());var desktopName=ObjectName(GetThreadDesktop(GetCurrentThreadId()));
   station=OpenWindowStation(stationName,false,0x00060000);if(station==IntPtr.Zero)throw new Win32Exception();
   desktop=OpenDesktop(desktopName,0,false,0x00060000);if(desktop==IntPtr.Zero)throw new Win32Exception();
   stationAcl=AllowCurrentUser(station,0x000F037F);desktopAcl=AllowCurrentUser(desktop,0x000F01FF);
   var startup=new Startup();startup.cb=Marshal.SizeOf(typeof(Startup));startup.desktop=stationName+"\\"+desktopName;
   if(!CreateProcessAsUser(limited,executable,new StringBuilder("\""+executable+"\" "+arguments),IntPtr.Zero,IntPtr.Zero,false,0x08000000,IntPtr.Zero,directory,ref startup,out process))throw new Win32Exception();
   if(WaitForSingleObject(process.process,600000)!=0){TerminateProcess(process.process,1);throw new TimeoutException("Restricted native startup timed out.");}
   uint code;if(!GetExitCodeProcess(process.process,out code))throw new Win32Exception();
   if(code!=0)throw new InvalidOperationException("Restricted startup exited with Windows status 0x"+code.ToString("X8")+"; inspect startup-native.log if present.");
   return 0;
  }finally{
   try{Restore(desktop,desktopAcl);}finally{
    try{Restore(station,stationAcl);}finally{
     if(desktop!=IntPtr.Zero)CloseDesktop(desktop);if(station!=IntPtr.Zero)CloseWindowStation(station);
     if(process.thread!=IntPtr.Zero)CloseHandle(process.thread);if(process.process!=IntPtr.Zero)CloseHandle(process.process);if(limited!=IntPtr.Zero)CloseHandle(limited);if(original!=IntPtr.Zero)CloseHandle(original);
    }
   }
  }
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
