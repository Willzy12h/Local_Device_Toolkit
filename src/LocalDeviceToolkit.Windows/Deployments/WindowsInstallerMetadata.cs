using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LocalDeviceToolkit.Engine.Deployments;
namespace LocalDeviceToolkit.Windows.Deployments;
public sealed class WindowsInstallerMetadata:IInstallerMetadata
{
 public void Validate(DeploymentProfile p,string path)
 {
  if(p.Format==DeploymentFormat.CurrentUserMsix){MsixPackageMetadata.Validate(p,path);return;}
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows installer metadata is unavailable.");
  if(p.Format==DeploymentFormat.MachineMsi)
  {
   Check(MsiOpenDatabase(path,IntPtr.Zero,out var database));
   try
   {
    if(!string.Equals(Property(database,"ProductCode"),p.ProductIdentity,StringComparison.OrdinalIgnoreCase)||Property(database,"ProductVersion")!=p.Version||Property(database,"ProductName")!=p.ProductName||Property(database,"Manufacturer")!=p.Publisher)throw new InvalidDataException("MSI product code, version, name or publisher differs from the approved profile.");
    Check(MsiGetSummaryInformation(database,null,0,out var summary));
    try{var size=1024u;var value=new StringBuilder((int)size);Check(MsiSummaryInfoGetProperty(summary,7,out _,out _,out _,value,ref size));if(value.ToString().Split(';')[0]!="x64")throw new InvalidDataException("The MSI summary template is not the approved x64 architecture.");}
    finally{MsiCloseHandle(summary);}
    // Reject all external cab paths which are not pinned in the complete manifest.
    Check(MsiDatabaseOpenView(database,"SELECT `Cabinet` FROM `Media`",out var view));
    try{Check(MsiViewExecute(view,0));uint status;while((status=MsiViewFetch(view,out var record))==0){try{var cab=ReadString(record,1);if(cab.Length>0&&!cab.StartsWith('#')&&!p.Files.Any(f=>f.RelativePath.Equals(cab,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("MSI references an unpinned external cabinet.");}finally{MsiCloseHandle(record);}}if(status!=259)Check(status);}
    finally{MsiCloseHandle(view);}
   }
   finally{MsiCloseHandle(database);}
  }
  else if(p.Format==DeploymentFormat.OfficeOdt)
  {
   var info=FileVersionInfo.GetVersionInfo(path);
   if(info.CompanyName!="Microsoft Corporation"||info.ProductName!=p.ProductName||!string.Equals(info.OriginalFilename,"setup.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The signed binary is not the approved Microsoft Office Deployment Tool setup.exe.");
  }
  else
  {
   var info=FileVersionInfo.GetVersionInfo(path);
   if(info.ProductName!=p.ProductName||info.CompanyName!=p.Publisher||Normalize(info.ProductVersion)!=p.Version)throw new InvalidDataException("OneDrive setup metadata differs from the approved name, publisher or product version.");
   using var file=File.OpenRead(path);using var reader=new BinaryReader(file);if(reader.ReadUInt16()!=0x5A4D)throw new InvalidDataException("Invalid OneDrive PE file.");file.Position=0x3c;var offset=reader.ReadInt32();if(offset<0||offset>file.Length-6)throw new InvalidDataException("Invalid PE header.");file.Position=offset;if(reader.ReadUInt32()!=0x00004550||reader.ReadUInt16()!=0x8664)throw new InvalidDataException("The approved OneDrive setup must be the x64 executable.");
  }
 }
 internal static string? Normalize(string? value)=>value?.Split(' ')[0].Replace(',','.');
 static string Property(uint database,string name)
 {
  Check(MsiDatabaseOpenView(database,"SELECT `Value` FROM `Property` WHERE `Property`='"+name+"'",out var view));
  try{Check(MsiViewExecute(view,0));Check(MsiViewFetch(view,out var record));try{return ReadString(record,1);}finally{MsiCloseHandle(record);}}
  finally{MsiCloseHandle(view);}
 }
 static string ReadString(uint record,uint field){var size=4096u;var value=new StringBuilder((int)size);Check(MsiRecordGetString(record,field,value,ref size));return value.ToString();}
 static void Check(uint status){if(status!=0)throw new InvalidDataException("Windows Installer metadata query failed: "+status);}
 [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiOpenDatabaseW")]static extern uint MsiOpenDatabase(string path,IntPtr persist,out uint database);
 [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiDatabaseOpenViewW")]static extern uint MsiDatabaseOpenView(uint database,string sql,out uint view);
 [DllImport("msi.dll")]static extern uint MsiViewExecute(uint view,uint record);
 [DllImport("msi.dll")]static extern uint MsiViewFetch(uint view,out uint record);
 [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiRecordGetStringW")]static extern uint MsiRecordGetString(uint record,uint field,StringBuilder value,ref uint size);
 [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiGetSummaryInformationW")]static extern uint MsiGetSummaryInformation(uint database,string? path,uint count,out uint summary);
 [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiSummaryInfoGetPropertyW")]static extern uint MsiSummaryInfoGetProperty(uint summary,uint property,out uint type,out int integer,out System.Runtime.InteropServices.ComTypes.FILETIME time,StringBuilder value,ref uint size);
 [DllImport("msi.dll")]static extern uint MsiCloseHandle(uint handle);
}
