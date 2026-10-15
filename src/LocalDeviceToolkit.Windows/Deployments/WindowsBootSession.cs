using System.Runtime.InteropServices;
namespace LocalDeviceToolkit.Windows.Deployments;
public static class WindowsBootSession
{
 public static Guid? Read()
 {
  if(!OperatingSystem.IsWindows())return null;
  var code=NtQuerySystemInformation(90,out var info,(uint)Marshal.SizeOf<BootEnvironment>(),out var length);
  return code==0&&length>=20&&info.BootIdentifier!=Guid.Empty?info.BootIdentifier:null;
 }
 [StructLayout(LayoutKind.Sequential)]struct BootEnvironment{public Guid BootIdentifier;public uint FirmwareType;public ulong BootFlags;}
 [DllImport("ntdll.dll")]static extern int NtQuerySystemInformation(uint informationClass,out BootEnvironment buffer,uint length,out uint returnedLength);
}
