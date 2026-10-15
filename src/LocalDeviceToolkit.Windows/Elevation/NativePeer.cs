using LocalDeviceToolkit.Core;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace LocalDeviceToolkit.Windows.Elevation;
public sealed record NativePeer(ExecutionActor Actor,string ImagePath)
{
 public static NativePeer Observe(int pid)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
  using var process=OpenProcess(0x1000,false,pid);if(process.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
  if(!GetProcessTimes(process,out var created,out _,out _,out _))throw new Win32Exception(Marshal.GetLastWin32Error());
  var path=new StringBuilder(32768);var size=path.Capacity;if(!QueryFullProcessImageName(process,0,path,ref size))throw new Win32Exception(Marshal.GetLastWin32Error());
  if(!OpenProcessToken(process,8,out var token))throw new Win32Exception(Marshal.GetLastWin32Error());
  using(token)
  {
   using var identity=new WindowsIdentity(token.DangerousGetHandle());
   if(!GetTokenInformation(token,20,out var elevation,4,out _))throw new Win32Exception(Marshal.GetLastWin32Error());
   return new(new(identity.User?.Value??throw new InvalidDataException("Peer has no user SID."),identity.Name,elevation!=0,pid,new DateTimeOffset(DateTime.FromFileTimeUtc(created))),path.ToString());
  }
 }
 public static int PipeClient(SafePipeHandle handle){if(!GetNamedPipeClientProcessId(handle,out var id))throw new Win32Exception(Marshal.GetLastWin32Error());return checked((int)id);}
 public static int PipeServer(SafePipeHandle handle){if(!GetNamedPipeServerProcessId(handle,out var id))throw new Win32Exception(Marshal.GetLastWin32Error());return checked((int)id);}
 [DllImport("kernel32.dll",SetLastError=true)]static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(SafeProcessHandle process,out long creation,out long exit,out long kernel,out long user);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(SafeProcessHandle process,int flags,StringBuilder path,ref int size);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(SafeProcessHandle process,uint access,out SafeAccessTokenHandle token);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool GetTokenInformation(SafeAccessTokenHandle token,int info,out int value,int length,out int returned);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint pid);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint pid);
}
