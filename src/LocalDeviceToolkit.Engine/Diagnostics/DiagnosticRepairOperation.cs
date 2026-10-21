using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
namespace LocalDeviceToolkit.Engine.Diagnostics;
public sealed record DiagnosticReconciliation(Guid Id,DeviceContext Context,ExecutionActor Actor,DateTimeOffset At,bool CompletionReviewed,string Summary)
{
 [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public Guid? OriginalActionId{get;init;}
}
public enum DiagnosticRepairKind { DnsCache,DefenderSignatures }
public sealed record DnsCacheInput(string Hostname,string ExpectedAddress,string Hypothesis,bool ConfirmWholeCacheEffect,bool ConfirmStaleCacheHypothesis);
public sealed record DefenderSignatureInput(bool ConfirmNetworkSources);
public sealed record RepairSnapshot(string AuthorizationDigest,string Current,bool CanAct,string Reason,IReadOnlyDictionary<string,string> Details);
public sealed record RepairDispatch(bool? Completed,bool? ExpectedStateVerified,string Reason);
public interface IDiagnosticRepairBackend
{
 Task<ExecutionActor> GetActorAsync(CancellationToken ct);
 Task<RepairSnapshot> ReadAsync(DiagnosticRepairKind kind,DeviceContext context,JsonElement input,CancellationToken ct);
 Task<RepairDispatch> RunAsync(DiagnosticRepairKind kind,DeviceContext context,JsonElement input,Guid action,RepairSnapshot expected,CancellationToken ct);
}
public sealed class DiagnosticRepairOperation(DiagnosticRepairKind kind,IDiagnosticRepairBackend backend):ILocalOperation
{
 public static string OperationId(DiagnosticRepairKind kind)=>kind==DiagnosticRepairKind.DnsCache?"diagnostics.refresh-dns-cache":"diagnostics.update-defender-signatures";
 public string Id=>OperationId(kind);
 public void ValidateParameters(JsonElement input)
 {
  try
  {
   if(kind==DiagnosticRepairKind.DnsCache)
   {
    var p=StrictJson.Read<DnsCacheInput>(StrictJson.Bytes(input),8192);LocalProbeCatalogue.ValidateInput("local.network.dns",StrictJson.Value(new{hostname=p.Hostname,queryType="A"}));
    if(!System.Net.IPAddress.TryParse(p.ExpectedAddress,out _)||p.Hypothesis is not ("configuration-change" or "negative-cache-after-record-created")||!p.ConfirmWholeCacheEffect||!p.ConfirmStaleCacheHypothesis)throw new ArgumentException();
   }
   else if(!StrictJson.Read<DefenderSignatureInput>(StrictJson.Bytes(input),4096).ConfirmNetworkSources)throw new ArgumentException();
  }catch(Exception e) when(e is JsonException or InvalidDataException or ArgumentException or CatalogueValidationException){throw new PlanValidationException(kind==DiagnosticRepairKind.DnsCache?"Choose one hostname, its externally verified expected address and a supported stale-cache hypothesis; confirm the whole-device cache effect. Round-robin differences alone are not evidence.":"Confirm that the configured Windows Defender update sources may be contacted.");}
 }
 public string GetTargetKey(DeviceContext context,JsonElement parameters)=>context.DeviceId+"/"+kind;
 public async Task<ActionReadiness> InspectAsync(DeviceContext context,JsonElement input,CancellationToken ct)
 {ValidateParameters(input);var state=await backend.ReadAsync(kind,context,input,ct);return new(state.CanAct,OperationScope.Machine,PrivilegeRequirement.Administrator,state.Reason);}
 public async Task<IReadOnlyList<ProbeObservation>> CaptureBeforeAsync(DeviceContext context,JsonElement input,CancellationToken ct)
 {ValidateParameters(input);var state=await backend.ReadAsync(kind,context,input,ct);return [Observation(state)];}
 ProbeObservation Observation(RepairSnapshot state)=>new(Id,"repair.review-state",ObservationState.Known,StrictJson.Value(state),"Windows registered diagnostic action / "+kind,DateTimeOffset.UtcNow,"Full permitted observations; stable authorization projection excludes volatile times and versions.");
 public async Task<ActionResult> ExecuteAsync(DeviceContext context,PlannedAction action,CancellationToken ct)
 {
  ValidateParameters(action.Request.Parameters);var actor=await backend.GetActorAsync(ct);var state=await backend.ReadAsync(kind,context,action.Request.Parameters,ct);
  if(!actor.IsElevated||!state.CanAct||ObservationFingerprint.Of([Observation(state)])!=action.BeforeDigest)return new(action.Id,ActionOutcome.Failed,null,"Not started: prerequisite, hypothesis or stable context changed. Check and review again.",[]);
  ct.ThrowIfCancellationRequested();var dispatch=await backend.RunAsync(kind,context,action.Request.Parameters,action.Id,state,ct);
  var after=await backend.ReadAsync(kind,context,action.Request.Parameters,CancellationToken.None);
  var outcome=dispatch.Completed is null?ActionOutcome.OutcomeUnknown:dispatch.Completed==false?ActionOutcome.Failed:dispatch.ExpectedStateVerified==true?ActionOutcome.AppliedVerified:ActionOutcome.AppliedUnverified;
  return new(action.Id,outcome,actor,dispatch.Reason,[Observation(after)]){Outcomes=new(dispatch.Completed,dispatch.ExpectedStateVerified,null)};
 }
}
