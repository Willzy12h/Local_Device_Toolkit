using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Settings;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
namespace LocalDeviceToolkit.Windows.Settings;
/// <summary>Queries the original process token; scope and required privilege are separate decisions.</summary>
public static class TimeZonePrivilege
{
 const string Name="SeTimeZonePrivilege";
 public static bool IsAssigned(int processId,string expectedSid)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
  using var process=OpenProcess(0x1000,false,processId);if(process.IsInvalid)throw Error();
  if(!OpenProcessToken(process,8,out var token))throw Error();
  using(token)
  {
   using var identity=new WindowsIdentity(token.DangerousGetHandle());if(identity.User?.Value!=expectedSid)throw new UnauthorizedAccessException("The original time-zone token identity changed.");
   if(!LookupPrivilegeValue(null,Name,out var required))throw Error();
   GetTokenInformation(token,3,IntPtr.Zero,0,out var size);if(Marshal.GetLastWin32Error()!=122||size is <4 or >65536)throw new InvalidDataException("The original token privileges could not be inspected.");
   var buffer=Marshal.AllocHGlobal(size);
   try
   {
    if(!GetTokenInformation(token,3,buffer,size,out var returned)||returned>size)throw Error();
    var count=Marshal.ReadInt32(buffer);if(count<0||count>(returned-4)/12)throw new InvalidDataException("Malformed token privilege list.");
    for(var i=0;i<count;i++){var entry=Marshal.PtrToStructure<LuidAttributes>(buffer+4+12*i);if(entry.Luid.Low==required.Low&&entry.Luid.High==required.High&&(entry.Attributes&4)==0)return true;}
    return false;
   }
   finally{Marshal.FreeHGlobal(buffer);}
  }
 }
 public static IDisposable EnableCurrent()
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
  using var process=OpenProcess(0x1000,false,Environment.ProcessId);if(process.IsInvalid)throw Error();
  if(!OpenProcessToken(process,0x28,out var token))throw Error();
  try
  {
   if(!LookupPrivilegeValue(null,Name,out var luid))throw Error();var desired=new TokenPrivileges {Count=1,Entry=new(){Luid=luid,Attributes=2}};
   if(!AdjustTokenPrivileges(token,false,ref desired,Marshal.SizeOf<TokenPrivileges>(),out var previous,out _)||Marshal.GetLastWin32Error()!=0)throw Error();
   return new PrivilegeLease(token,previous);
  }
  catch{token.Dispose();throw;}
 }
 sealed class PrivilegeLease(SafeAccessTokenHandle token,TokenPrivileges previous):IDisposable
 {
  bool disposed;
  public void Dispose(){if(disposed)return;disposed=true;try{if(previous.Count>0&&!AdjustTokenPrivileges(token,false,ref previous,0,out _,out _))throw Error();}finally{token.Dispose();}}
 }
 static Win32Exception Error()=>new(Marshal.GetLastWin32Error());
 [StructLayout(LayoutKind.Sequential)]struct Luid{public uint Low;public int High;}
 [StructLayout(LayoutKind.Sequential)]struct LuidAttributes{public Luid Luid;public uint Attributes;}
 [StructLayout(LayoutKind.Sequential)]struct TokenPrivileges{public uint Count;public LuidAttributes Entry;}
 [DllImport("kernel32.dll",SetLastError=true)]static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int id);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(SafeProcessHandle process,uint access,out SafeAccessTokenHandle token);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool LookupPrivilegeValue(string? system,string name,out Luid luid);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool GetTokenInformation(SafeAccessTokenHandle token,int info,IntPtr buffer,int size,out int returned);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token,bool disableAll,ref TokenPrivileges state,int length,out TokenPrivileges previous,out int returned);
}
public static class TimeZoneSettings
{
 public static TimeZoneState Read()
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
  if(GetDynamicTimeZoneInformation(out var value)==uint.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error());
  if(string.IsNullOrWhiteSpace(value.KeyName))throw new InvalidDataException("The current Windows time-zone key is unavailable.");
  return new(value.KeyName,value.Disabled);
 }
 public static void Set(TimeZoneState target)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
  if(string.IsNullOrWhiteSpace(target.KeyName)||target.KeyName.Length>128)throw new PlanValidationException("Invalid recorded Windows time zone.");
  for(uint index=0;index<1024;index++)
  {
   var status=EnumDynamicTimeZoneInformation(index,out var zone);if(status==259)break;if(status!=0)throw new Win32Exception(checked((int)status));
   if(!string.Equals(zone.KeyName,target.KeyName,StringComparison.Ordinal))continue;
   zone.Disabled=target.DynamicDaylightTimeDisabled;
   using var privilege=TimeZonePrivilege.EnableCurrent();if(!SetDynamicTimeZoneInformation(ref zone))throw new Win32Exception(Marshal.GetLastWin32Error());
   TimeZoneInfo.ClearCachedData();return;
  }
  throw new PlanValidationException("The recorded time-zone key is not installed on this device.");
 }
 [StructLayout(LayoutKind.Sequential)]struct SystemTime{public ushort Year,Month,DayOfWeek,Day,Hour,Minute,Second,Milliseconds;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct DynamicZone
 {
  public int Bias;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string StandardName;public SystemTime StandardDate;public int StandardBias;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string DaylightName;public SystemTime DaylightDate;public int DaylightBias;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string KeyName;[MarshalAs(UnmanagedType.U1)]public bool Disabled;
 }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint GetDynamicTimeZoneInformation(out DynamicZone zone);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern uint EnumDynamicTimeZoneInformation(uint index,out DynamicZone zone);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetDynamicTimeZoneInformation(ref DynamicZone zone);
}
