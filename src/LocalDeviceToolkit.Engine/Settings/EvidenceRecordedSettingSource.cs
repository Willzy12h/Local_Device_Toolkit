using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Elevation;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Execution;
namespace LocalDeviceToolkit.Engine.Settings;
/// <summary>The protected composition reads only administrator-owned journals; the presentation composition reads the original engineer's records for review.</summary>
public sealed class EvidenceRecordedSettingSource(IEvidenceStore store,bool protectedAuthority=false):IRecordedSettingSource
{
 public bool IsProtectedAuthority=>protectedAuthority;
 static T Read<T>(EvidenceRecord record,string kind)
 {if(record.Kind!=kind)throw new EvidenceValidationException("The recorded restoration source has the wrong evidence kind.");return StrictJson.Read<T>(StrictJson.Bytes(record.Data));}
 public async Task<RecordedSettingSource> LoadAsync(RecordedSettingRestorationParameters p,CancellationToken ct)
 {
  var planRecord=await store.LoadAsync(p.SourcePlanId,ct);var plan=Read<ReviewedPlan>(planRecord,"plan");
  if(plan.Id!=p.SourcePlanId||plan.Context!=planRecord.Context||plan.Digest!=CanonicalJson.Digest(plan with {Digest=""}))throw new EvidenceValidationException("The original reviewed plan does not match its identity.");
  var action=plan.Actions.SingleOrDefault(a=>a.Id==p.SourceActionId)??throw new EvidenceValidationException("The original action is not in that plan.");
  ActionResult result;
  if(protectedAuthority)
  {
   var resultRecord=await store.LoadAsync(ProtectedJournalIds.Result(plan.Id,action.Id),ct);var recorded=Read<ResultRecord>(resultRecord,"result");
   if(recorded.PlanId!=plan.Id||resultRecord.Context!=plan.Context)throw new EvidenceValidationException("The protected result does not belong to that reviewed plan.");
   var run=Read<RunRecord>(await store.LoadAsync(recorded.RunId,ct),"run");
   if(run.PlanId!=plan.Id||!run.EvidenceComplete||run.Context!=plan.Context||run.Results.Count!=1||CanonicalJson.Digest(run.Results[0])!=CanonicalJson.Digest(recorded.Result))throw new EvidenceValidationException("The protected run/result linkage is incomplete.");
   result=recorded.Result;
  }
  else
  {
   var run=Read<RunRecord>(await store.LoadAsync(p.SourceRunId,ct),"run");
   if(run.Id!=p.SourceRunId||run.PlanId!=plan.Id||run.Context!=plan.Context||!run.EvidenceComplete)throw new EvidenceValidationException("The original run does not match the reviewed plan.");
   result=run.Results.SingleOrDefault(r=>r.ActionId==action.Id)??throw new EvidenceValidationException("There is no recorded result for that action.");
  }
  var definition=FixedSettingOperation.Definitions.SingleOrDefault(d=>d.OperationId==action.Request.OperationId)??throw new EvidenceValidationException("The original operation does not support recorded restoration.");
  if(result.ActionId!=action.Id||result.Outcome!=ActionOutcome.AppliedVerified||result.Actor is null||action.Readiness.Scope!=definition.Scope||action.Before.Count!=1||result.After.Count!=1||action.BeforeDigest!=Planning.ObservationFingerprint.Of(action.Before))throw new EvidenceValidationException("Only a complete verified fixed-setting action can be restored.");
  var before=action.Before[0];var after=result.After[0];var expectedCheck="setting."+definition.Setting;
  if(before.ControlId!=definition.ControlId||after.ControlId!=definition.ControlId||before.CheckId!=expectedCheck||after.CheckId!=expectedCheck||before.State!=ObservationState.Known||after.State!=ObservationState.Known||!before.Value.HasValue||!after.Value.HasValue)throw new EvidenceValidationException("The original setting before/after evidence is incomplete.");
  // For preview this flag classifies the source. The worker independently loads its own protected records before any elevated write.
  var requiresProtected=action.Readiness.Scope==OperationScope.Machine&&action.Readiness.Privilege==PrivilegeRequirement.Administrator;
  if(protectedAuthority&&!requiresProtected)throw new EvidenceValidationException("User-privilege evidence cannot authorize elevated restoration.");
  return new(plan.Id,action.Id,p.SourceRunId,plan.Context,plan.Catalogue,definition.Setting,action.Readiness.Scope,action.Readiness.Privilege,new(definition.Setting,before.Value.Value.Clone(),before.Source,before.CapturedAt),new(definition.Setting,after.Value.Value.Clone(),after.Source,after.CapturedAt),result.Actor,result.Outcome,requiresProtected);
 }
}
