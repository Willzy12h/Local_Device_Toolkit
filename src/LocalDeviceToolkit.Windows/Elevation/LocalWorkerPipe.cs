using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
namespace LocalDeviceToolkit.Windows.Elevation;
public static class LocalWorkerPipe
{
 public static NamedPipeServerStream Create(string name,string originalSid)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
  if(!name.StartsWith("LocalDeviceToolkit-",StringComparison.Ordinal)||!Guid.TryParseExact(name["LocalDeviceToolkit-".Length..],"N",out _))throw new ArgumentException("Invalid worker pipe name.");
  var sid=new SecurityIdentifier(originalSid).Value;
  if(!ConvertStringSecurityDescriptorToSecurityDescriptor($"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{sid})",1,out var descriptor,out _))throw new Win32Exception(Marshal.GetLastWin32Error());
  try
  {
   var attributes=new SecurityAttributes {Length=Marshal.SizeOf<SecurityAttributes>(),Descriptor=descriptor,Inherit=false};
   // PIPE_REJECT_REMOTE_CLIENTS is a pipe-mode flag (8), not a .NET PipeOptions value.
   var handle=CreateNamedPipe(@"\\.\pipe\"+name,0x40000000|0x00080000|3,8,1,1024*1024,1024*1024,30000,ref attributes);
   if(handle.IsInvalid){var error=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(error);}
   try{return new NamedPipeServerStream(PipeDirection.InOut,true,false,handle);}catch{handle.Dispose();throw;}
  }
  finally{LocalFree(descriptor);}
 }
 [StructLayout(LayoutKind.Sequential)]struct SecurityAttributes {public int Length;public IntPtr Descriptor;[MarshalAs(UnmanagedType.Bool)]public bool Inherit;}
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl,uint revision,out IntPtr descriptor,out uint size);
 [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr memory);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafePipeHandle CreateNamedPipe(string name,uint openMode,uint pipeMode,uint instances,uint outSize,uint inSize,uint timeout,ref SecurityAttributes attributes);
}
