using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Planning;
namespace LocalDeviceToolkit.Engine.Settings;

public sealed record RecordedSettingRestorationParameters(Guid SourcePlanId,Guid SourceActionId,Guid SourceRunId,bool ConfirmManagedPolicyReview);
public sealed record RecordedSettingSource(Guid SourcePlanId,Guid SourceActionId,Guid SourceRunId,DeviceContext Context,CatalogueIdentity Catalogue,SettingId Setting,OperationScope Scope,PrivilegeRequirement Privilege,SettingSnapshot Before,SettingSnapshot VerifiedAfter,ExecutionActor Actor,ActionOutcome Outcome,bool ProtectedJournal);
/// <summary>Adapters must validate original plan/action/run/result linkage before returning a source. Presentation adapters may classify protected sources but cannot authorize a write.</summary>
public interface IRecordedSettingSource
{
 bool IsProtectedAuthority=>false;
 Task<RecordedSettingSource> LoadAsync(RecordedSettingRestorationParameters parameters,CancellationToken ct);
}
public interface ISettingRestorationBackend:ISettingsBackend
{
 Task RestoreAsync(SettingId setting,DeviceContext context,SettingSnapshot expectedCurrent,SettingSnapshot recordedTarget,CancellationToken ct);
}
/// <summary>Restores only exact typed recorded values; no arbitrary value, path or command parameter is accepted.</summary>
public sealed class RecordedSettingRestorationOperation(IRecordedSettingSource source,ISettingRestorationBackend backend,CatalogueIdentity catalogue):ILocalOperation
{
 public string Id=>"settings.restore-recorded";
 public void ValidateParameters(JsonElement parameters)=>ReadParameters(parameters);
 static RecordedSettingRestorationParameters ReadParameters(JsonElement parameters)
 {
  try
  {
   var value=StrictJson.Read<RecordedSettingRestorationParameters>(StrictJson.Bytes(parameters),4096);
   if(value.SourcePlanId==Guid.Empty||value.SourceActionId==Guid.Empty||value.SourceRunId==Guid.Empty)throw new PlanValidationException("Original plan, action and run identities are required.");
   return value;
  }
  catch(Exception e) when(e is JsonException or InvalidDataException or InvalidOperationException){throw new PlanValidationException("Restoration accepts only original evidence identities and policy review confirmation.");}
 }
 public string GetTargetKey(DeviceContext context,JsonElement parameters)
 {var p=ReadParameters(parameters);return context.DeviceId+"/"+p.SourcePlanId.ToString("D")+"/"+p.SourceActionId.ToString("D")+"/"+p.SourceRunId.ToString("D");}
 static T State<T>(SettingSnapshot snapshot)=>StrictJson.Read<T>(StrictJson.Bytes(snapshot.Value),4096);
 static bool SameValue(SettingSnapshot a,SettingSnapshot b)=>a.Setting==b.Setting&&CanonicalJson.Digest(a.Value)==CanonicalJson.Digest(b.Value);
 static void ValidateSnapshot(SettingSnapshot snapshot,SettingId setting)
 {
  if(snapshot.Setting!=setting||string.IsNullOrWhiteSpace(snapshot.Source)||snapshot.Source.Length>512||snapshot.Source.Any(char.IsControl)||snapshot.CapturedAt.Offset!=TimeSpan.Zero)throw new PlanValidationException("Invalid setting snapshot provenance.");
  switch(setting)
  {
   case SettingId.ShowFileExtensions:case SettingId.LongPaths:case SettingId.DisableFastStartup:
    var registry=State<RegistryDwordState>(snapshot);if(registry.Exists!=registry.Value.HasValue)throw new PlanValidationException("Inconsistent registry absence evidence.");break;
   case SettingId.NoAcSleep:
    if(State<AcSleepState>(snapshot).ActiveScheme==Guid.Empty)throw new PlanValidationException("Missing recorded power scheme identity.");break;
   case SettingId.UkTimeZone:
    var zone=State<TimeZoneState>(snapshot);if(string.IsNullOrWhiteSpace(zone.KeyName)||zone.KeyName.Length>128||zone.KeyName.Any(c=>char.IsControl(c)||c is '/' or '\\'))throw new PlanValidationException("Invalid recorded time-zone key.");break;
   default:throw new PlanValidationException("This operation has no recorded restoration implementation.");
  }
 }
 async Task<RecordedSettingSource> LoadAsync(DeviceContext context,RecordedSettingRestorationParameters parameters,CancellationToken ct)
 {
  var record=await source.LoadAsync(parameters,ct);
  if(record.SourcePlanId!=parameters.SourcePlanId||record.SourceActionId!=parameters.SourceActionId||record.SourceRunId!=parameters.SourceRunId||record.Context!=context||record.Catalogue!=catalogue||record.Outcome!=ActionOutcome.AppliedVerified)throw new PlanValidationException("Original verified evidence does not match this device, catalogue and source identities.");
  var definition=FixedSettingOperation.Definitions.SingleOrDefault(d=>d.Setting==record.Setting)??throw new PlanValidationException("Unsupported restoration setting.");
  if(record.Scope!=definition.Scope||!Enum.IsDefined(record.Privilege)||record.Actor is null||record.Actor.ProcessId<=0||record.Actor.ProcessStartedAt.Offset!=TimeSpan.Zero)throw new PlanValidationException("Original execution scope or actor is invalid.");
  if(record.Privilege==PrivilegeRequirement.Administrator)
  {if(record.Scope!=OperationScope.Machine||!record.ProtectedJournal||!record.Actor.IsElevated)throw new PlanValidationException("Original machine writes require independently protected source evidence.");}
  else if(record.Actor.Sid!=context.OriginalUserSid||record.Scope==OperationScope.Machine&&record.Setting!=SettingId.UkTimeZone)throw new PlanValidationException("Original execution identity cannot authorize this restoration.");
  ValidateSnapshot(record.Before,record.Setting);ValidateSnapshot(record.VerifiedAfter,record.Setting);
  var verified=record.Setting switch
  {
   SettingId.NoAcSleep=>State<AcSleepState>(record.VerifiedAfter) is {Seconds:0} power&&power.ActiveScheme==State<AcSleepState>(record.Before).ActiveScheme&&power.DcSeconds==State<AcSleepState>(record.Before).DcSeconds,
   SettingId.UkTimeZone=>State<TimeZoneState>(record.VerifiedAfter) is {KeyName:"GMT Standard Time"} zone&&zone.DynamicDaylightTimeDisabled==State<TimeZoneState>(record.Before).DynamicDaylightTimeDisabled,
   _=>State<RegistryDwordState>(record.VerifiedAfter) is {Exists:true,Value:int value}&&value==(record.Setting==SettingId.LongPaths?1:0)
  };
  if(!verified)throw new PlanValidationException("The recorded after-state does not verify the supported original operation.");
  return record;
 }
 async Task<ActionReadiness> InspectSourceAsync(DeviceContext context,RecordedSettingSource record,bool reviewed,CancellationToken ct)
 {
  ActionReadiness Result(bool ready,string reason)=>new(ready,record.Scope,record.Privilege,reason);
  if(!reviewed)return Result(false,"Review effective GPO/MDM policy before restoring recorded values.");
  var actor=await backend.GetActorAsync(ct);
  if(record.Privilege==PrivilegeRequirement.CurrentUser&&actor.Sid!=context.OriginalUserSid)return Result(false,"Restoration must execute as the original engineer.");
  var privilege=await backend.GetPrivilegeAsync(record.Setting,context,ct);
  if(privilege!=record.Privilege)return Result(false,"Original execution privilege cannot be preserved; review trusted original evidence before proceeding.");
  var policy=await backend.InspectPolicyAsync(record.Setting,ct);
  if(!policy.Available||policy.ConflictObserved)return Result(false,policy.Reason);
  var current=await backend.ReadAsync(record.Setting,context,ct);ValidateSnapshot(current,record.Setting);
  if(!SameValue(current,record.VerifiedAfter))return Result(false,"Subsequent setting drift blocks restoration of recorded values.");
  return Result(true,"Recorded values and original execution scope were checked; policy review was confirmed. Restoration requires separate review.");
 }
 public async Task<ActionReadiness> InspectAsync(DeviceContext context,JsonElement parameters,CancellationToken ct)
 {
  try{var p=ReadParameters(parameters);return await InspectSourceAsync(context,await LoadAsync(context,p,ct),p.ConfirmManagedPolicyReview,ct);}
  catch(Exception e) when(e is PlanValidationException or EvidenceValidationException or JsonException or InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException){return new(false,OperationScope.Machine,PrivilegeRequirement.Administrator,"Recorded restoration is unavailable: "+e.Message);}
 }
 static ProbeObservation Observation(RecordedSettingSource record,SettingSnapshot current)
 {
  var definition=FixedSettingOperation.Definitions.Single(d=>d.Setting==record.Setting);
  // The source digest binds the target and original provenance to the fresh plan. UI and protected worker must derive identical source records.
  return new(definition.ControlId,"setting.restore."+record.Setting,ObservationState.Known,current.Value.Clone(),"Recorded source SHA256 "+CanonicalJson.Digest(record)+"; current: "+current.Source,current.CapturedAt,"Exact current state and independently resolved recorded restoration target.");
 }
 public async Task<IReadOnlyList<ProbeObservation>> CaptureBeforeAsync(DeviceContext context,JsonElement parameters,CancellationToken ct)
 {
  var p=ReadParameters(parameters);var record=await LoadAsync(context,p,ct);var readiness=await InspectSourceAsync(context,record,p.ConfirmManagedPolicyReview,ct);
  if(!readiness.CanExecute)throw new PlanValidationException(readiness.Reason);
  var current=await backend.ReadAsync(record.Setting,context,ct);ValidateSnapshot(current,record.Setting);
  if(!SameValue(current,record.VerifiedAfter))throw new PlanValidationException("The setting changed during restoration review.");
  return [Observation(record,current)];
 }
 public async Task<ActionResult> ExecuteAsync(DeviceContext context,PlannedAction action,CancellationToken ct)
 {
  ActionResult Refuse(string reason)=>new(action.Id,ActionOutcome.Failed,null,"Not started: "+reason,[]);
  RecordedSettingSource record;SettingSnapshot current;
  try
  {
   var p=ReadParameters(action.Request.Parameters);record=await LoadAsync(context,p,ct);
   if(record.Privilege==PrivilegeRequirement.Administrator&&!source.IsProtectedAuthority)return Refuse("An independent protected-journal source is required for the original machine write.");
   var readiness=await InspectSourceAsync(context,record,p.ConfirmManagedPolicyReview,ct);
   if(!readiness.CanExecute||readiness!=action.Readiness)return Refuse("Restoration prerequisites changed. "+readiness.Reason);
   var actor=await backend.GetActorAsync(ct);if(record.Privilege==PrivilegeRequirement.Administrator&&!actor.IsElevated)return Refuse("The authenticated elevated worker is required.");
   current=await backend.ReadAsync(record.Setting,context,ct);ValidateSnapshot(current,record.Setting);
   if(!SameValue(current,record.VerifiedAfter)||action.Before.Count!=1||action.BeforeDigest!=ObservationFingerprint.Of(action.Before)||ObservationFingerprint.Of(new[]{Observation(record,current)})!=action.BeforeDigest)return Refuse("The current setting or recorded restoration source changed after review.");
  }
  catch(Exception e) when(e is PlanValidationException or EvidenceValidationException or JsonException or InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException){return Refuse(e.Message);}
  ct.ThrowIfCancellationRequested();await backend.RestoreAsync(record.Setting,context,current,record.Before,ct);
  var after=await backend.ReadAsync(record.Setting,context,CancellationToken.None);ValidateSnapshot(after,record.Setting);
  var executionActor=await backend.GetActorAsync(CancellationToken.None);var verified=SameValue(after,record.Before);
  return new(action.Id,verified?ActionOutcome.AppliedVerified:ActionOutcome.AppliedUnverified,executionActor,verified?"The exact recorded prior setting was restored and independently read back. Complete the control's manual checks.":"The recorded prior setting was not confirmed by independent readback. Inspect before retrying.",[Observation(record,after)]);
 }
}
