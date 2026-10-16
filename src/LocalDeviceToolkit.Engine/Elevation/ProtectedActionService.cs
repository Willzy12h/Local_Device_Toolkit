using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Planning;
namespace LocalDeviceToolkit.Engine.Elevation;
public sealed class ProtectedActionService(OperationRegistry operations,PlanService plans,LocalCatalogue catalogue,IEvidenceStore journal,TimeProvider clock)
{
 public async Task<ActionResult> ExecuteAsync(ReviewedPlan plan,Guid actionId,DeviceContext physicalMachine,ExecutionActor parent,ExecutionActor worker,Func<bool> parentStillMatches,CancellationToken ct)
 {
  WorkerGuard.ValidateOriginal(plan.Context,physicalMachine,parent);
  if(plan.Lifetime is not {} lifetime||!lifetime.Matches(parent)||lifetime.IsExpired(Environment.TickCount64))throw new PlanValidationException("A fresh review from the authenticated parent is required.");
  if(!worker.IsElevated||worker.ProcessId<=0||!parentStillMatches())throw new PlanValidationException("An authenticated live parent and elevated worker are required.");
  if(plan.Actions is null)throw new PlanValidationException("Invalid action inventory.");
  var action=plan.Actions.SingleOrDefault(a=>a is not null&&a.Id==actionId)??throw new PlanValidationException("The action is not in this plan.");
  operations.Validate(action.Request);var operation=operations.Find(action.Request.OperationId);
  IReadOnlyList<ProbeObservation> checkedBefore=action.Before;
  async Task CheckAsync()
  {
   ct.ThrowIfCancellationRequested();
   await Settings.PowerPlanSequence.VerifyProtectedPrefixAsync(plan,action,journal,ct);
   var readiness=await operation.InspectAsync(plan.Context,action.Request.Parameters,ct);
   if(!readiness.CanExecute||readiness.Scope!=OperationScope.Machine||readiness.Privilege!=PrivilegeRequirement.Administrator||readiness!=action.Readiness)throw new PlanValidationException("The worker accepts only the exact reviewed machine action; original-user operations must stay in the original process.");
   var before=await operation.CaptureBeforeAsync(plan.Context,action.Request.Parameters,ct);checkedBefore=before;
   var valid=plans.ValidateAction(plan,action,plan.Context,catalogue.Identity,before,clock.GetUtcNow());
   if(!valid.IsValid)throw new PlanValidationException(string.Join(" ",valid.Reasons));
   if(!parentStillMatches())throw new PlanValidationException("The authenticated parent is no longer available.");
  }
  await CheckAsync();
  try
  {
   var existing=await journal.LoadAsync(plan.Id,ct);
   if(existing.Kind!="plan"||CanonicalJson.Digest(existing.Data)!=CanonicalJson.Digest(StrictJson.Value(plan)))throw new PlanValidationException("A different plan already uses this identity.");
  }
  catch(IOException error) when(error is FileNotFoundException or DirectoryNotFoundException){await journal.SaveNewAsync(EvidenceRecord.Create(plan.Id,"plan",plan.Context,plan.CreatedAt,StrictJson.Value(plan)),ct);}
  // Deterministic, non-overwriting intent identity prevents replay even across worker restarts.
  var intentId=new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plan.Id.ToString("D")+"/"+action.Id.ToString("D"))).AsSpan(0,16));
  var runId=Guid.NewGuid();var started=clock.GetUtcNow();
  await journal.SaveNewAsync(EvidenceRecord.Create(intentId,"intent",plan.Context,started,StrictJson.Value(new IntentRecord(runId,plan.Id,action,checkedBefore))),ct);
  ActionResult result;bool dispatched=false;
  try
  {
   await CheckAsync();
   dispatched=true;result=await operation.ExecuteAsync(plan.Context,Settings.PowerPlanSequence.AtDispatch(plan,action),ct);ToolkitEvidenceSchemas.ValidateResult(result);
   if(result.ActionId!=action.Id||result.Actor!=worker)result=new(action.Id,ActionOutcome.OutcomeUnknown,worker,"The execution result did not match the authenticated worker/action; reconcile before retrying.",[]);
  }
  catch(PlanValidationException error) when(!dispatched){result=new(action.Id,ActionOutcome.Failed,worker,"Not started: "+error.Message,[]);}
  catch(OperationCanceledException){result=new(action.Id,dispatched?ActionOutcome.OutcomeUnknown:ActionOutcome.CancelledBeforeExecution,worker,dispatched?"Cancellation occurred after dispatch. Reconcile before retrying.":"Cancelled before execution.",[]);}
  catch(Exception){result=new(action.Id,ActionOutcome.OutcomeUnknown,worker,"The protected execution outcome could not be confirmed. Reconcile before retrying.",[]);}
  try
  {
   await journal.SaveNewAsync(EvidenceRecord.Create(ProtectedJournalIds.Result(plan.Id,action.Id),"result",plan.Context,clock.GetUtcNow(),StrictJson.Value(new ResultRecord(runId,plan.Id,result))),CancellationToken.None);
   var run=new RunRecord(runId,plan.Id,plan.Context,started,clock.GetUtcNow(),[result],true);
   await journal.SaveNewAsync(EvidenceRecord.Create(runId,"run",plan.Context,run.FinishedAt!.Value,StrictJson.Value(run)),CancellationToken.None);
  }
  catch(Exception error) when(error is IOException or UnauthorizedAccessException or EvidenceValidationException){return result with {Outcome=ActionOutcome.OutcomeUnknown,Reason="The protected result could not be durably saved. Reconcile before retrying."};}
  return result;
 }
}
