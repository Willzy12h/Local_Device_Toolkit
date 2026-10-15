using LocalDeviceToolkit.Engine.Deployments;
using Microsoft.Win32;
namespace LocalDeviceToolkit.Windows.Deployments;
public sealed record OfficePolicySource(string Source,bool Readable,bool Present);
public static class OfficePolicyGate
{
 public static DeploymentInputCheck Read()
 {
  var sources=new List<OfficePolicySource>();
  try
  {
   using var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);
   foreach(var path in new[]{@"SOFTWARE\Policies\Microsoft\Office\16.0\Common\OfficeUpdate",@"SOFTWARE\Policies\Microsoft\cloud\office\16.0\Common\officeupdate",@"SOFTWARE\Policies\Microsoft\office\16.0\common\officeservicemanager"})
   {using var key=machine.OpenSubKey(path,false);sources.Add(new("HKLM/"+path,true,key is not null&&(key.ValueCount>0||key.SubKeyCount>0)));}
   using var config=machine.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration",false);var managed=config?.GetValue("OfficeMgmtCOM");sources.Add(new("HKLM Click-to-Run OfficeMgmtCOM",true,managed is not null&&managed.ToString() is not ("False" or "false" or "0")));
   return Evaluate(sources);
  }
  catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException){sources.Add(new("Machine Office policy sources",false,false));return Evaluate(sources);}
 }
 public static DeploymentInputCheck Evaluate(IEnumerable<OfficePolicySource> sources)
 {
  var observed=sources.ToArray();if(observed.Length==0||observed.Any(s=>!s.Readable))return new(false,"","Office update/management policy inspection is unavailable; local channel deployment is blocked.");
  if(observed.Any(s=>s.Present))return new(false,"","An Office update or management policy source is present. Use the approved managed deployment channel; local ODT configuration cannot override it.");
  return new(true,"","No conflict observed in the checked machine Office update/management sources. External GPO/MDM/client and licensing review remains required.");
 }
}
