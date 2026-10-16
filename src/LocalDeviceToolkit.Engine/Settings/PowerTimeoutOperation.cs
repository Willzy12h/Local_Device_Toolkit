using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
namespace LocalDeviceToolkit.Engine.Settings;
public enum PowerSlot { SleepAc,SleepDc,DisplayAc,DisplayDc }
public sealed record PowerTimeoutState(Guid ActiveScheme,uint SleepAc,uint SleepDc,uint DisplayAc,uint DisplayDc)
{
 public PowerTimeoutState With(PowerSlot slot,uint seconds)=>slot switch {PowerSlot.SleepAc=>this with{SleepAc=seconds},PowerSlot.SleepDc=>this with{SleepDc=seconds},PowerSlot.DisplayAc=>this with{DisplayAc=seconds},PowerSlot.DisplayDc=>this with{DisplayDc=seconds},_=>throw new ArgumentOutOfRangeException(nameof(slot))};
 public uint Get(PowerSlot slot)=>slot switch{PowerSlot.SleepAc=>SleepAc,PowerSlot.SleepDc=>SleepDc,PowerSlot.DisplayAc=>DisplayAc,PowerSlot.DisplayDc=>DisplayDc,_=>throw new ArgumentOutOfRangeException(nameof(slot))};
}
public sealed record PowerTimeoutInput(int Minutes,bool ConfirmManagedPolicyReview);
public interface IPowerTimeoutBackend
{
 Task<ExecutionActor> GetActorAsync(CancellationToken ct);
 Task<SettingPolicy> InspectPolicyAsync(CancellationToken ct);
 Task<PowerTimeoutState> ReadAsync(DeviceContext context,CancellationToken ct);
 Task WriteAsync(DeviceContext context,PowerSlot slot,uint seconds,PowerTimeoutState expected,CancellationToken ct);
}
public sealed class PowerTimeoutOperation(PowerSlot slot,IPowerTimeoutBackend backend):ILocalOperation
{
 public static SettingId Setting(PowerSlot slot)=>(SettingId)((int)SettingId.PowerSleepAc+(int)slot);
 public static bool IsSetting(SettingId setting)=>setting is >=SettingId.PowerSleepAc and <=SettingId.PowerDisplayDc;
 public static PowerSlot Slot(SettingId setting)=>IsSetting(setting)?(PowerSlot)((int)setting-(int)SettingId.PowerSleepAc):throw new ArgumentException("Not a power timeout setting.");
 public static string OperationId(PowerSlot slot)=>"settings.power."+slot.ToString().ToLowerInvariant();
 public string Id=>OperationId(slot);
 public static uint Seconds(int minutes)=>minutes is >=0 and <=1440?checked((uint)minutes*60):throw new ArgumentOutOfRangeException(nameof(minutes),"Choose Never (0) or 1 to 1440 minutes.");
 public static PowerTimeoutInput Input(JsonElement value){var p=StrictJson.Read<PowerTimeoutInput>(StrictJson.Bytes(value),4096);_=Seconds(p.Minutes);return p;}
 public void ValidateParameters(JsonElement value){try{_=Input(value);}catch(Exception e) when(e is ArgumentException or JsonException or InvalidDataException){throw new PlanValidationException("Choose Never or 1–1440 whole minutes and confirm policy review.");}}
 public static string TargetKey(DeviceContext context,PowerSlot slot)=>context.DeviceId+"/"+(slot==PowerSlot.SleepAc?"NoAcSleep":slot.ToString());
 public string GetTargetKey(DeviceContext context,JsonElement parameters)=>TargetKey(context,slot);
 public async Task<ActionReadiness> InspectAsync(DeviceContext context,JsonElement parameters,CancellationToken ct)
 {
  ValidateParameters(parameters);var actor=await backend.GetActorAsync(ct);var policy=await backend.InspectPolicyAsync(ct);
  var ready=actor.Sid==context.OriginalUserSid&&Input(parameters).ConfirmManagedPolicyReview&&policy.Available&&!policy.ConflictObserved;
  return new(ready,OperationScope.Machine,PrivilegeRequirement.Administrator,actor.Sid!=context.OriginalUserSid?"Power changes must run as the original engineer, including after elevation.":!Input(parameters).ConfirmManagedPolicyReview?"Confirm client and management policy review.":policy.Reason);
 }
 public async Task<IReadOnlyList<ProbeObservation>> CaptureBeforeAsync(DeviceContext context,JsonElement parameters,CancellationToken ct)
 {
  ValidateParameters(parameters);var state=await backend.ReadAsync(context,ct);if(state.ActiveScheme==Guid.Empty)throw new PlanValidationException("Active power scheme is unknown.");
  return [Observation(state)];
 }
 ProbeObservation Observation(PowerTimeoutState state)=>new("CFG-WIN-020","power.timeout-state",ObservationState.Known,StrictJson.Value(state),"Windows power APIs / active scheme / four timeout indices",DateTimeOffset.UtcNow,"Exact scheme and AC/battery sleep/display seconds; all unselected indices are preserved.");
 public async Task<ActionResult> ExecuteAsync(DeviceContext context,PlannedAction action,CancellationToken ct)
 {
  var readiness=await InspectAsync(context,action.Request.Parameters,ct);var actor=await backend.GetActorAsync(ct);
  if(!readiness.CanExecute||!actor.IsElevated)return new(action.Id,ActionOutcome.Failed,null,"Not started: original-engineer elevation and readable nonconflicting policy are required.",[]);
  var before=await backend.ReadAsync(context,ct);if(ObservationFingerprint.Of([Observation(before)])!=action.BeforeDigest)return new(action.Id,ActionOutcome.Failed,null,"Not started: scheme or timeout changed after review.",[]);
  var expected=before.With(slot,Seconds(Input(action.Request.Parameters).Minutes));ct.ThrowIfCancellationRequested();await backend.WriteAsync(context,slot,expected.Get(slot),before,ct);
  var after=await backend.ReadAsync(context,CancellationToken.None);var verified=after==expected;
  return new(action.Id,verified?ActionOutcome.AppliedVerified:ActionOutcome.AppliedUnverified,actor,verified?"Action completed; expected indices verified. Sleep/display behavior requires a manual test; original symptom resolution is not established.":"Write attempted; exact scheme and all four timeout indices were not confirmed. Stop and inspect.",[Observation(after)]){Outcomes=new(true,verified,null)};
 }
}
