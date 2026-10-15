using System.Diagnostics;
using LocalDeviceToolkit.Engine.Deployments;
namespace LocalDeviceToolkit.Windows.Deployments;
public static class DeploymentLaunch
{
 public static ProcessStartInfo Create(DeploymentProfile profile,string stagedRoot)
 {
  DeploymentProfiles.Validate(profile);var entry=Path.Combine(Path.GetFullPath(stagedRoot),profile.EntryPoint.Replace('/',Path.DirectorySeparatorChar));
  var start=new ProcessStartInfo{UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=stagedRoot};
  if(profile.Format==DeploymentFormat.MachineMsi)
  {
   start.FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe");
   foreach(var arg in new[]{"/i",entry,"/qn","/norestart","REBOOT=ReallySuppress","ALLUSERS=1","MSIINSTALLPERUSER="})start.ArgumentList.Add(arg);
  }
  else if(profile.Format==DeploymentFormat.OfficeOdt)
  {start.FileName=entry;start.ArgumentList.Add("/configure");start.ArgumentList.Add(Path.GetFullPath(stagedRoot)+".config.xml");}
  else if(profile.Format==DeploymentFormat.OneDriveMachineExe)
  {
   start.FileName=entry;start.ArgumentList.Add("/allusers");start.ArgumentList.Add("/silent");
   // Microsoft's per-machine OneDrive setup switches. It can delegate work; exit zero never proves child completion.
  }
  else throw new ArgumentException("MSIX requires the fixed original-user package adapter.");
  // Prevent user process environment from injecting MSI transforms, CLR profilers, DLL search paths or code into the elevated child.
  // Machine PATH is not needed: approved processes are addressed by exact trusted absolute paths.
  var systemRoot=Environment.GetFolderPath(Environment.SpecialFolder.Windows);var temp=Path.Combine(systemRoot,"Temp");
  start.Environment.Clear();start.Environment["SystemRoot"]=systemRoot;start.Environment["WINDIR"]=systemRoot;start.Environment["SystemDrive"]=Path.GetPathRoot(systemRoot)??"";start.Environment["TEMP"]=temp;start.Environment["TMP"]=temp;
  start.Environment["PATH"]=Path.Combine(systemRoot,"System32")+";"+systemRoot;
  return start;
 }
 public static bool MayDetach(DeploymentProfile p)=>p.Format is DeploymentFormat.OneDriveMachineExe or DeploymentFormat.OfficeOdt;
}
