using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
namespace LocalDeviceToolkit.Engine.Evidence;
public static class ToolkitEvidenceSchemas
{
 public static EvidenceSchemaRegistry Create()
 {
  var schemas=new EvidenceSchemaRegistry();
  schemas.Register<ReviewedPlan>("plan",(plan,record)=>
  {
   if(plan.Id!=record.Id||plan.Context!=record.Context||plan.CreatedAt!=record.RecordedAt||plan.Actions is null||plan.Actions.Count is <1 or >100||plan.Actions.Any(a=>a is null||a.Request is null||a.Before is null||a.Before.Count==0)||plan.Digest!=CanonicalJson.Digest(plan with { Digest="" }))throw new EvidenceValidationException("Invalid reviewed-plan evidence.");
  });
  schemas.Register<IntentRecord>("intent",(intent,record)=>
  {
   if(intent.RunId==Guid.Empty||intent.PlanId==Guid.Empty||intent.Action is null||intent.Action.Id==Guid.Empty||intent.BeforeAtDispatch is null||intent.BeforeAtDispatch.Count==0)throw new EvidenceValidationException("Incomplete pre-dispatch intent.");
  });
  schemas.Register<ResultRecord>("result",(result,record)=>
  {
   if(result.RunId==Guid.Empty||result.PlanId==Guid.Empty)throw new EvidenceValidationException("Missing result provenance.");
   ValidateResult(result.Result);
  });
  schemas.Register<RunRecord>("run",(run,record)=>
  {
   if(run.Id!=record.Id||run.PlanId==Guid.Empty||run.Context!=record.Context||run.Results is null||run.Results.Count>100||run.StartedAt.Offset!=TimeSpan.Zero||run.FinishedAt is null||run.FinishedAt.Value.Offset!=TimeSpan.Zero||run.FinishedAt<run.StartedAt)throw new EvidenceValidationException("Invalid run evidence.");
   foreach(var result in run.Results)ValidateResult(result);
  });
  return schemas;
 }
 internal static void ValidateResult(ActionResult result)
 {
  if(result is null||result.ActionId==Guid.Empty||result.After is null||result.After.Any(o=>o is null||o.State==ObservationState.Known&&(!o.Value.HasValue||o.Value.Value.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined))||string.IsNullOrWhiteSpace(result.Reason))throw new EvidenceValidationException("Incomplete action result.");
  if(result.Outcome==ActionOutcome.AppliedVerified&&(result.Actor is null||result.After.Count==0||result.After.Any(o=>o.State!=ObservationState.Known)))throw new EvidenceValidationException("Verified success needs an observed actor and complete readback.");
  if(result.Actor is not null&&(string.IsNullOrWhiteSpace(result.Actor.Sid)||result.Actor.ProcessId<=0||result.Actor.ProcessStartedAt.Offset!=TimeSpan.Zero))throw new EvidenceValidationException("Invalid execution actor.");
 }
}
