using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Elevation;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
namespace LocalDeviceToolkit.Engine.Settings;
/// <summary>Derives dispatch state from immutable measured state and earlier explicit power actions.</summary>
public static class PowerPlanSequence
{
 public static bool IsPower(string id)=>id=="settings.no-ac-sleep"||Enum.GetValues<PowerSlot>().Any(s=>PowerTimeoutOperation.OperationId(s)==id);
 static (PowerSlot Slot,uint Seconds) Change(PlannedAction action)
 {
  if(action.Request.OperationId=="settings.no-ac-sleep")return(PowerSlot.SleepAc,0);
  var slot=Enum.GetValues<PowerSlot>().Single(s=>PowerTimeoutOperation.OperationId(s)==action.Request.OperationId);return(slot,PowerTimeoutOperation.Seconds(PowerTimeoutOperation.Input(action.Request.Parameters).Minutes));
 }
 public static PlannedAction AtDispatch(ReviewedPlan plan,PlannedAction action)
 {
  if(!IsPower(action.Request.OperationId))return action;
  var prior=plan.Actions.TakeWhile(a=>a.Id!=action.Id).Where(a=>IsPower(a.Request.OperationId)).ToArray();if(prior.Length==0)return action;
  if(action.Before.Count!=1||action.Before[0].Value is not {} original)throw new PlanValidationException("Power before state is missing.");
  var observation=action.Before[0];
  if(action.Request.OperationId=="settings.no-ac-sleep")
  {
   var state=StrictJson.Read<AcSleepState>(StrictJson.Bytes(original));
   foreach(var p in prior){var change=Change(p);if(change.Slot==PowerSlot.SleepAc)state=state with{Seconds=change.Seconds};if(change.Slot==PowerSlot.SleepDc)state=state with{DcSeconds=change.Seconds};}
   observation=observation with{Value=StrictJson.Value(state)};
  }
  else
  {
   var state=StrictJson.Read<PowerTimeoutState>(StrictJson.Bytes(original));
   foreach(var p in prior)
   {
    var previous=p.Request.OperationId=="settings.no-ac-sleep"?StrictJson.Read<AcSleepState>(StrictJson.Bytes(p.Before[0].Value!.Value)).ActiveScheme:StrictJson.Read<PowerTimeoutState>(StrictJson.Bytes(p.Before[0].Value!.Value)).ActiveScheme;
    if(previous!=state.ActiveScheme)throw new PlanValidationException("Selected power actions refer to different schemes.");var change=Change(p);state=state.With(change.Slot,change.Seconds);
   }
   observation=observation with{Value=StrictJson.Value(state)};
  }
  return action with{Before=[observation],BeforeDigest=ObservationFingerprint.Of([observation])};
 }
 public static async Task VerifyProtectedPrefixAsync(ReviewedPlan plan,PlannedAction action,IEvidenceStore journal,CancellationToken ct)
 {
  if(!IsPower(action.Request.OperationId))return;
  foreach(var prior in plan.Actions.TakeWhile(a=>a.Id!=action.Id).Where(a=>IsPower(a.Request.OperationId)))
  {
   var record=await journal.LoadAsync(ProtectedJournalIds.Result(plan.Id,prior.Id),ct);
   if(record.Kind!="result"||record.Context!=plan.Context)throw new PlanValidationException("Protected power prefix is missing.");var value=StrictJson.Read<ResultRecord>(StrictJson.Bytes(record.Data));var result=value.Result;
   if(value.PlanId!=plan.Id||result.ActionId!=prior.Id||result.Outcome!=ActionOutcome.AppliedVerified||result.Actor is not {IsElevated:true}||result.Actor.Sid!=plan.Context.OriginalUserSid||result.After.Count!=1||result.After[0].State!=ObservationState.Known||result.After[0].Value is not {} raw)throw new PlanValidationException("A prior power write lacks verified protected evidence.");
   var projected=AtDispatch(plan,prior);var change=Change(prior);
   var expected=prior.Request.OperationId=="settings.no-ac-sleep"?StrictJson.Value(StrictJson.Read<AcSleepState>(StrictJson.Bytes(projected.Before[0].Value!.Value)) with{Seconds=0}):StrictJson.Value(StrictJson.Read<PowerTimeoutState>(StrictJson.Bytes(projected.Before[0].Value!.Value)).With(change.Slot,change.Seconds));
   if(CanonicalJson.Digest(expected)!=CanonicalJson.Digest(raw))throw new PlanValidationException("The protected power prefix readback does not match the selected writes.");
  }
 }
}
