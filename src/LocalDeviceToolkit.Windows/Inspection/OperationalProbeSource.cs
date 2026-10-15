using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Windows.Inspection.Probes;
using LocalDeviceToolkit.Windows.Processes;
namespace LocalDeviceToolkit.Windows.Inspection;

public sealed class OperationalProbeSource(string scriptRoot,string? protectedRuntime=null,TimeProvider? clock=null):ILocalProbeSource
{
 readonly TimeProvider time=clock??TimeProvider.System;
 public async Task<LocalProbeResult> ReadAsync(string id,JsonElement parameters,DeviceContext context,CancellationToken ct)
 {
  LocalProbeCatalogue.ValidateInput(id,parameters);var definition=LocalProbeCatalogue.Resolve(id);var digest=LocalProbeCatalogue.TargetDigest(id,parameters,context);
  if(!OperatingSystem.IsWindows())return Unknown(id,digest,"Native Windows inspection is unavailable on this operating system.",ObservationState.NotSupported);
  var actor=await new WindowsContextProvider().GetActorAsync(ct);
  if(definition.Scope==OperationScope.CurrentUser&&actor.Sid!=context.OriginalUserSid)return Unknown(id,digest,"This check must run as the original engineer.",ObservationState.AccessDenied);
  if(definition.Privilege==PrivilegeRequirement.Administrator&&!actor.IsElevated)return Unknown(id,digest,"Select the explicit administrator read for this check.",ObservationState.AccessDenied);
  var runtime=protectedRuntime??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDeviceToolkit","runtime");
  EvidenceStore.RefuseReparsePath(runtime);Directory.CreateDirectory(runtime);EvidenceStore.RefuseReparsePath(runtime);
  var work=Path.Combine(runtime,"probe-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
  LocalProbeResult result;bool cleanupFailed=false;
  try
  {
   ct.ThrowIfCancellationRequested();
   if(id is "local.proxy.user" or "local.proxy.winhttp")
   {
    var proxy=UserProxyReader.Read(id=="local.proxy.user");var details=new Dictionary<string,string>{{"proxy",proxy.Proxy},{"bypass",proxy.Bypass}};
    if(id=="local.proxy.user"){details["autoDetect"]=proxy.AutoDetect;details["pacUrl"]=proxy.PacUrl;}
    result=Known(id,digest,"Proxy configuration collected",details,["Configuration only; no PAC file was fetched or evaluated. Apps can use different proxy stacks."]);
   }
   else if(id=="local.identity.context")result=Known(id,digest,NativeJoinContext.Read(),new Dictionary<string,string>{{"joinState",NativeJoinContext.Read()},{"management","Local join indicators do not prove MDM enrollment, tenant compliance or assignment."},{"originalUser",context.OriginalUserName}},["No online device authentication or tenant query was made."]);
   else if(id=="local.battery.capacity")result=await BatteryAsync(id,digest,work,ct);
   else if(id is "local.events.incident" or "local.app.verification")result=Unknown(id,digest,"This provider requires its registered evidence integration.");
   else
   {
    var family=id.StartsWith("local.network.",StringComparison.Ordinal)?"network":id.StartsWith("local.defender.",StringComparison.Ordinal)||id.StartsWith("local.print.",StringComparison.Ordinal)||id=="local.time.status"?"services":id.StartsWith("local.hardware.",StringComparison.Ordinal)||id.StartsWith("local.storage.",StringComparison.Ordinal)||id=="local.power.capabilities"?"hardware":"readiness";
    using var script=RegisteredScripts.OpenVerified(scriptRoot,"probes."+family);using var helpers=RegisteredScripts.OpenVerified(scriptRoot,"probes.common");
    var requestFile=Path.Combine(work,"request.json");await File.WriteAllBytesAsync(requestFile,StrictJson.Bytes(new {schemaVersion=2,checkId=id,targetDigest=digest,parameters}),ct);
    var start=PowerShell(script.Name,requestFile,helpers.Name);var began=time.GetUtcNow();var execution=await new DiagnosticProcessRunner().RunCommandAsync(start,TimeSpan.FromSeconds(30),ct);ct.ThrowIfCancellationRequested();
    result=execution.End==DiagnosticEnd.Exited&&execution.ExitCode==0?OperationalFactsParser.Parse(Encoding.UTF8.GetBytes(execution.StandardOutput),id,digest,began,time.GetUtcNow()):Unknown(id,digest,"The selected read did not complete within its bounds; no value was inferred.");
    if(id=="local.os.support"&&result.Value is { } os)
    {
     var support=ReferenceProfiles.EvaluateSupport(os.Details.GetValueOrDefault("edition", ""),os.Details.GetValueOrDefault("release", ""),DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime));
     var details=os.Details.ToDictionary(p=>p.Key,p=>p.Value);details["architecture"]=context.Architecture;details["referenceDate"]=support.ReferenceDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);details["supportEnd"]=support.EndsOn?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"Unknown";details["clientTarget"]="Not supplied; client approval is separate from support.";
     result=result with {Value=os with {Current=os.Current+"; support "+support.State,Status=support.State==SupportState.Unknown?"Unknown":"Observed",Details=details},Coverage=result.Coverage with {Complete=result.Coverage.Complete&&support.State!=SupportState.Unknown,Limitations=result.Coverage.Limitations.Append(support.Reason).ToArray()}};
    }
   }
  }
  catch(OperationCanceledException){throw;}
  catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or EntryPointNotFoundException or System.Xml.XmlException)
  {result=Unknown(id,digest,"The selected local provider was unavailable or returned unrecognized data. No default value was inferred.");}
  finally
  {
   try{EvidenceStore.RefuseReparsePath(work);Directory.Delete(work,true);}catch(Exception e) when(e is IOException or UnauthorizedAccessException or EvidenceValidationException){cleanupFailed=true;}
  }
  return cleanupFailed?result with {Coverage=result.Coverage with {Complete=false,Limitations=result.Coverage.Limitations.Append("Temporary diagnostic output could not be fully removed; inspect the private runtime folder.").ToArray()}}:result;
 }
 async Task<LocalProbeResult> BatteryAsync(string id,string digest,string work,CancellationToken ct)
 {
  var output=Path.Combine(work,"battery.xml");var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"powercfg.exe"));
  foreach(var arg in new[]{"/batteryreport","/output",output,"/xml"})start.ArgumentList.Add(arg);
  var execution=await new DiagnosticProcessRunner().RunCommandAsync(start,TimeSpan.FromSeconds(30),ct);ct.ThrowIfCancellationRequested();
  if(execution.End!=DiagnosticEnd.Exited||execution.ExitCode!=0||!File.Exists(output)||new FileInfo(output).Length>4*1024*1024)return Unknown(id,digest,"Battery report did not complete within its bounds.");
  await using var stream=new FileStream(output,FileMode.Open,FileAccess.Read,FileShare.Read);var batteries=BatteryReportReader.Read(stream);
  if(batteries.Count==0)return Unknown(id,digest,"No battery capacity evidence is available. This may be expected for a desktop or VM.",ObservationState.NotSupported);
  var lines=batteries.Select(b=>$"{b.Reference}: design {b.DesignMWh?.ToString(CultureInfo.InvariantCulture)??"Unknown"} mWh; full charge {b.FullChargeMWh?.ToString(CultureInfo.InvariantCulture)??"Unknown"} mWh; ratio {BatteryReportReader.CapacityPercent(b)?.ToString("0.0",CultureInfo.InvariantCulture)??"Unknown"}%; cycles {b.CycleCount?.ToString(CultureInfo.InvariantCulture)??"Unknown"}");
  return Known(id,digest,batteries.Count+" battery capacity record(s)",new Dictionary<string,string>{{"batteries",string.Join(Environment.NewLine,lines)}},["Reported capacity ratio is descriptive, not a universal replacement threshold. Raw history/serial data is not retained."]);
 }
 internal LocalProbeResult Known(string id,string digest,string current,IReadOnlyDictionary<string,string> details,IReadOnlyList<string> limitations)=>new(2,id,digest,ObservationState.Known,new(current,null,"Observed",details),"Windows local inspection",time.GetUtcNow(),new(true,false,1,null,limitations),"Observed locally.");
 internal LocalProbeResult Unknown(string id,string digest,string reason,ObservationState state=ObservationState.Unavailable)=>new(2,id,digest,state,null,"Windows local inspection",time.GetUtcNow(),new(false,false,0,null,[reason]),reason);
 static ProcessStartInfo PowerShell(string script,string request,string helpers)
 {
  var system=Environment.GetFolderPath(Environment.SpecialFolder.System);var start=new ProcessStartInfo(Path.Combine(system,"WindowsPowerShell","v1.0","powershell.exe"));
  foreach(var arg in new[]{"-NoLogo","-NoProfile","-NonInteractive","-File",script,"-RequestFile",request,"-HelpersPath",helpers})start.ArgumentList.Add(arg);
  start.Environment["PSModulePath"]=Path.Combine(system,"WindowsPowerShell","v1.0","Modules");start.Environment.Remove("PSExecutionPolicyPreference");return start;
 }
}
static class NativeJoinContext
{
 public static string Read()
 {
  IntPtr domain=IntPtr.Zero,aad=IntPtr.Zero;
  try
  {
   var domainStatus=NetGetJoinInformation(null,out domain,out var join)==0?join:-1;
   var aadStatus=NetGetAadJoinInformation(null,out aad)==0?(aad==IntPtr.Zero?0:Marshal.ReadInt32(aad)):-1;
   return "Domain: "+(domainStatus==3?"Joined":domainStatus is 1 or 2?"Not joined":"Unknown")+"; Entra device join: "+(aadStatus==1?"Observed":aadStatus==2?"Workplace registration observed":aadStatus==0?"Not observed":"Unknown");
  }
  finally{if(domain!=IntPtr.Zero)NetApiBufferFree(domain);if(aad!=IntPtr.Zero)NetFreeAadJoinInformation(aad);}
 }
 [DllImport("netapi32.dll",CharSet=CharSet.Unicode)]static extern int NetGetJoinInformation(string? server,out IntPtr name,out int status);
 [DllImport("netapi32.dll",CharSet=CharSet.Unicode)]static extern int NetGetAadJoinInformation(string? tenant,out IntPtr information);
 [DllImport("netapi32.dll")]static extern int NetApiBufferFree(IntPtr memory);
 [DllImport("netapi32.dll")]static extern void NetFreeAadJoinInformation(IntPtr information);
}
