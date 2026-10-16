using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Engine.Diagnostics;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Windows.Inspection;
using LocalDeviceToolkit.Windows.Processes;
namespace LocalDeviceToolkit.Windows.Diagnostics;
public sealed class WindowsDiagnosticRepairBackend(string scriptRoot,string? protectedRuntime=null,IDeploymentPendingStore? pending=null):IDiagnosticRepairBackend
{
 readonly WindowsContextProvider identity=new();
 sealed record Reply(int SchemaVersion,DiagnosticRepairKind Kind,IReadOnlyDictionary<string,string> Identity,IReadOnlyDictionary<string,string> Observed,bool Ready,string Reason);
 public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>identity.GetActorAsync(ct);
 static string Target(DeviceContext context)=>CanonicalJson.Digest(new{context.DeviceId,Operation="Defender signature request"});
 static ProcessStartInfo PowerShell(string path)
 {
  var system=Environment.GetFolderPath(Environment.SpecialFolder.System);var start=new ProcessStartInfo(Path.Combine(system,"WindowsPowerShell","v1.0","powershell.exe"));
  foreach(var a in new[]{"-NoLogo","-NoProfile","-NonInteractive","-File",path})start.ArgumentList.Add(a);
  start.Environment["PSModulePath"]=Path.Combine(system,"WindowsPowerShell","v1.0","Modules");start.Environment.Remove("PSExecutionPolicyPreference");return start;
 }
 public async Task<RepairSnapshot> ReadAsync(DiagnosticRepairKind kind,DeviceContext context,JsonElement input,CancellationToken ct)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();var current=await identity.GetCurrentAsync(ct);if(current.DeviceId!=context.DeviceId||!PlanService.SupportedTarget(current))throw new PlanValidationException("Diagnostic actions require the reviewed Windows device.");
  using var script=RegisteredScripts.OpenVerified(scriptRoot,"repair.read");
  var runtime=protectedRuntime??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDeviceToolkit","runtime");EvidenceStore.RefuseReparsePath(runtime);Directory.CreateDirectory(runtime);EvidenceStore.RefuseReparsePath(runtime);var request=Path.Combine(runtime,"repair-"+Guid.NewGuid().ToString("N")+".json");
  try
  {
   await File.WriteAllBytesAsync(request,StrictJson.Bytes(new{kind,input}),ct);var start=PowerShell(script.Name);start.ArgumentList.Add("-RequestFile");start.ArgumentList.Add(request);
   var result=await new DiagnosticProcessRunner().RunCommandAsync(start,TimeSpan.FromSeconds(30),ct);ct.ThrowIfCancellationRequested();if(result.End!=DiagnosticEnd.Exited||result.ExitCode!=0)throw new PlanValidationException("Required bounded diagnostic read did not complete.");
   var reply=StrictJson.Read<Reply>(Encoding.UTF8.GetBytes(result.StandardOutput),65536);
   var allowedIdentity=kind==DiagnosticRepairKind.DnsCache?new[]{"adapters","routes","interfaces","nrpt","hosts","cachedAddresses","negative"}:new[]{"mode","sourceOrder","enabled"};var allowedObserved=kind==DiagnosticRepairKind.DnsCache?new[]{"cachedAddresses","negative"}:new[]{"signatureVersion","signatureTime"};
   bool Fields(IReadOnlyDictionary<string,string> values,string[] keys)=>values.Count<=keys.Length&&values.All(p=>keys.Contains(p.Key,StringComparer.Ordinal)&&p.Value is not null&&p.Value.Length<=16384);
   if(reply.SchemaVersion!=1||reply.Kind!=kind||reply.Reason.Length>1024||!Fields(reply.Identity,allowedIdentity)||!Fields(reply.Observed,allowedObserved)||reply.Ready&&reply.Identity.Count!=allowedIdentity.Length)throw new PlanValidationException("Diagnostic read returned unregistered or incomplete values.");
   var details=reply.Identity.Concat(reply.Observed).GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Last().Value,StringComparer.Ordinal);
   if(kind==DiagnosticRepairKind.DnsCache)details["networkContextDigest"]=CanonicalJson.Digest(reply.Identity.Where(p=>p.Key is not ("cachedAddresses" or "negative")).ToDictionary(p=>p.Key,p=>p.Value));
   var unresolved=kind==DiagnosticRepairKind.DefenderSignatures&&pending is not null&&await pending.ReadAsync(Target(context),ct) is not null;
   var auth=CanonicalJson.Digest(new{kind,context.DeviceId,context.OriginalUserSid,input,reply.Identity});
   var description=kind==DiagnosticRepairKind.DnsCache?"Cached addresses: "+reply.Observed.GetValueOrDefault("cachedAddresses","Unknown")+"; negative entry: "+reply.Observed.GetValueOrDefault("negative","Unknown"):"Signatures: "+reply.Observed.GetValueOrDefault("signatureVersion","Unknown")+"; observed update time: "+reply.Observed.GetValueOrDefault("signatureTime","Unknown")+"; sources: "+reply.Identity.GetValueOrDefault("sourceOrder","Unknown");
   return new(auth,description,reply.Ready&&!unresolved,unresolved?"An unresolved signature request blocks another attempt. Use protected read-only reconciliation before retrying.":reply.Reason,details);
  }finally{File.Delete(request);}
 }
 public async Task<RepairDispatch> RunAsync(DiagnosticRepairKind kind,DeviceContext context,JsonElement input,Guid action,RepairSnapshot expected,CancellationToken ct)
 {
  var actor=await identity.GetActorAsync(ct);if(!actor.IsElevated)throw new PlanValidationException("A protected elevated worker is required.");
  var current=await ReadAsync(kind,context,input,ct);if(!current.CanAct||current.AuthorizationDigest!=expected.AuthorizationDigest)throw new PlanValidationException("Diagnostic context changed immediately before dispatch.");
  ct.ThrowIfCancellationRequested();
  if(kind==DiagnosticRepairKind.DnsCache)
  {
   if(!DnsFlushResolverCache())return new(false,false,"DNS client cache refresh failed. No resolver or server configuration was changed.");
   var p=StrictJson.Read<DnsCacheInput>(StrictJson.Bytes(input));var family=System.Net.IPAddress.Parse(p.ExpectedAddress).AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork?"A":"AAAA";
   var verification=await new OperationalProbeSource(scriptRoot,protectedRuntime).ReadAsync("local.network.dns",StrictJson.Value(new{hostname=p.Hostname,queryType=family}),context,CancellationToken.None);
   var network=await ReadAsync(kind,context,input,CancellationToken.None);var answers=verification.Value?.Details.GetValueOrDefault("answers","")??"";
   var addresses=System.Text.RegularExpressions.Regex.Matches(answers,@"IPAddress=([^;\s]+)").Select(m=>System.Net.IPAddress.TryParse(m.Groups[1].Value,out var ip)?ip.ToString():"");
   var verified=verification.State==ObservationState.Known&&verification.Coverage.Complete&&addresses.Contains(System.Net.IPAddress.Parse(p.ExpectedAddress).ToString(),StringComparer.Ordinal)&&network.Details.GetValueOrDefault("networkContextDigest")==expected.Details.GetValueOrDefault("networkContextDigest");
   return new(true,verified,"Whole-device DNS client cache refresh completed. Selected hostname rechecked through the system resolver; expected address "+(verified?"observed":"not confirmed")+". Original symptom resolution is not established. No exact cache restoration is available.");
  }
  if(pending is null)throw new PlanValidationException("A protected persistent request guard is required.");
  using var script=RegisteredScripts.OpenVerified(scriptRoot,"repair.defender",false);var start=PowerShell(script.Name);
  var claim=new DeploymentPending(action,Target(context),context.OriginalUserSid,DateTimeOffset.UtcNow,null);await pending.ClaimAsync(claim,ct);
  var observation=await new MutationProcessRunner().StartAndObserveAsync(start,TimeSpan.FromMinutes(2),ct,async p=>await pending.ObserveAsync(claim with{Process=new(DeploymentProcessEnd.StillRunning,p.ProcessId,p.ProcessStartedAt,start.FileName,null,false)},CancellationToken.None));
  var ended=observation.End is MutationEnd.Exited or MutationEnd.NotStarted;
  await pending.ObserveAsync(claim with{Process=new(ended?observation.End==MutationEnd.Exited?DeploymentProcessEnd.Exited:DeploymentProcessEnd.NotStarted:DeploymentProcessEnd.ObservationLost,observation.ProcessId,observation.ProcessStartedAt,start.FileName,observation.ExitCode,false)},CancellationToken.None);
  if(ended)await pending.CompleteAsync(claim,CancellationToken.None);
  return !ended?new(null,null,"Signature request completion is unknown. The process was not killed; the persistent guard blocks another request until explicit protected reconciliation."):observation.End==MutationEnd.NotStarted||observation.ExitCode!=0?new(false,null,"Signature request failed or did not start. Inspect current state before any new review."):new(true,null,"Signature request completed using Windows configured/default sources. Fresh version/time are recorded separately. An unchanged version does not establish that a newer version existed; latest state and original symptom resolution remain unknown.");
 }
 public async Task<string> ReconcileAsync(DeviceContext context,bool confirmCompleted,CancellationToken ct,Func<Guid,string,Task>? beforeResolve=null)
 {
  if(pending is null||!(await identity.GetActorAsync(ct)).IsElevated)throw new PlanValidationException("Protected reconciliation is required.");
  var claim=await pending.ReadAsync(Target(context),ct);if(claim is null)return "No unresolved signature request guard. No update was launched.";
  var state=claim.Process is null?DeploymentProcessStatus.Unknown:await new Deployments.WindowsDeploymentBackend().InspectProcessAsync(claim.Process,ct);
  var fresh=await ReadAsync(DiagnosticRepairKind.DefenderSignatures,context,StrictJson.Value(new DefenderSignatureInput(true)),ct);
  if(confirmCompleted)
  {
   if(state!=DeploymentProcessStatus.NotRunning||!fresh.Details.TryGetValue("mode",out var mode)||mode!="Normal"||!fresh.Details.ContainsKey("signatureVersion"))throw new PlanValidationException("Request completion or fresh active Defender state is not established. Guard retained.");
   if(beforeResolve is null)throw new PlanValidationException("A durable protected completion review must be recorded before resolving the guard.");
   await beforeResolve(claim.ActionId,"Original action="+claim.ActionId+"; process="+state+"; "+fresh.Current+". Engineer reviewed vendor completion; original uncertain result is unchanged.");
   await pending.CompleteAsync(claim,ct);return "Engineer explicitly reviewed completion, original process is no longer running, and fresh Defender state was read. Guard resolved; original uncertain result remains unchanged. A new update still requires a fresh review.";
  }
  return "Read only; no update launched. Original process: "+state+". "+fresh.Current+". Guard retained until explicit completion review.";
 }
 [DllImport("dnsapi.dll")] [return:MarshalAs(UnmanagedType.Bool)]static extern bool DnsFlushResolverCache();
}
