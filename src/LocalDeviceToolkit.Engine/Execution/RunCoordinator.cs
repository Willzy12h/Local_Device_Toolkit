using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Evidence;
namespace LocalDeviceToolkit.Engine.Execution;
public sealed class RunCoordinator(PlanService plans,OperationRegistry operations,IExecutionContextProvider contexts,LocalCatalogue catalogue,IEvidenceStore evidence,IActionDispatcher dispatcher,TimeProvider clock)
{
 readonly SemaphoreSlim gate=new(1,1);
 readonly HashSet<Guid> consumed=[];
 public async Task<RunRecord> ExecuteAsync(ReviewedPlan plan,CancellationToken stop,IRunProgressSink? progress=null)
 {
  if(!await gate.WaitAsync(0,CancellationToken.None))throw new PlanValidationException("An operation is already running.");
  try
  {
   if(plan.Lifetime is not {} lifetime||lifetime.IsExpired(Environment.TickCount64)||lifetime.ParentProcessId!=Environment.ProcessId||lifetime.ParentProcessStartedAt!=ReviewLifetime.Start().ParentProcessStartedAt)throw new PlanValidationException("A new review in this app session is required.");
   if(consumed.Contains(plan.Id))throw new PlanValidationException("This plan has already been used; review a fresh plan.");
   stop.ThrowIfCancellationRequested();var context=await contexts.GetCurrentAsync(stop);
   var initial=plans.Validate(plan,context,catalogue.Identity,plan.Actions.ToDictionary(a=>a.Id,a=>a.BeforeDigest),clock.GetUtcNow());
   if(!initial.IsValid)throw new PlanValidationException(string.Join(" ",initial.Reasons));
   await evidence.SaveNewAsync(EvidenceRecord.Create(plan.Id,"plan",plan.Context,plan.CreatedAt,StrictJson.Value(plan)),stop);
   consumed.Add(plan.Id);
   var runId=Guid.NewGuid();var started=clock.GetUtcNow();var results=new List<ActionResult>();bool halted=false,complete=true;
   foreach(var action in plan.Actions)
   {
    ActionResult result;bool notifyResult=false;
    if(halted||stop.IsCancellationRequested)
     result=new(action.Id,ActionOutcome.CancelledBeforeExecution,null,"Not started: the queue was stopped.",[]);
    else
    {
     bool dispatched=false;
     try
     {
      context=await contexts.GetCurrentAsync(stop);var operation=operations.Find(action.Request.OperationId);
      var readiness=await operation.InspectAsync(context,action.Request.Parameters,stop);
      if(!readiness.CanExecute||readiness.Scope!=action.Readiness.Scope||(action.Readiness.Privilege==PrivilegeRequirement.CurrentUser&&readiness.Privilege==PrivilegeRequirement.Administrator))throw new PlanValidationException(readiness.CanExecute?"Scope or privilege requirements changed; review a new plan.":readiness.Reason);
      var before=await operation.CaptureBeforeAsync(context,action.Request.Parameters,stop);
      var validation=plans.ValidateAction(plan,action,context,catalogue.Identity,before,clock.GetUtcNow());
      if(!validation.IsValid)throw new PlanValidationException(string.Join(" ",validation.Reasons));
      var intent=new IntentRecord(runId,plan.Id,action,before);
      await evidence.SaveNewAsync(EvidenceRecord.Create(Guid.NewGuid(),"intent",plan.Context,clock.GetUtcNow(),StrictJson.Value(intent)),stop);
      stop.ThrowIfCancellationRequested();
      validation=plans.ValidateAction(plan,action,context,catalogue.Identity,before,clock.GetUtcNow());
      if(!validation.IsValid)throw new PlanValidationException(string.Join(" ",validation.Reasons));
      if(progress is not null)
      {
       try{await progress.BeforeActionAsync(plan,action,stop);}
       catch(Exception)
       {
        complete=false;halted=true;
        throw new PlanValidationException("The companion run log could not be saved; execution was blocked.");
       }
      }
      stop.ThrowIfCancellationRequested();
      notifyResult=true;
      dispatched=true;
      result=await dispatcher.DispatchAsync(plan,action,stop);
      ToolkitEvidenceSchemas.ValidateResult(result);
      if(result.ActionId!=action.Id)result=new(action.Id,ActionOutcome.OutcomeUnknown,null,"Execution returned another action identity; reconcile before retrying.",[]);
     }
     catch(OperationCanceledException) when(!dispatched){result=new(action.Id,ActionOutcome.CancelledBeforeExecution,null,"Cancelled before execution.",[]);}
     catch(PlanValidationException e) when(!dispatched){result=new(action.Id,ActionOutcome.Failed,null,"Not started: "+e.Message,[]);}
     catch(Exception e) when(e is IOException or UnauthorizedAccessException or EvidenceValidationException)
     {
      complete=false;result=new(action.Id,dispatched?ActionOutcome.OutcomeUnknown:ActionOutcome.Failed,null,dispatched?"Execution or evidence became unavailable; reconcile before retrying.":"Not started: required evidence could not be saved or inspected.",[]);
     }
     catch(Exception) when(dispatched){result=new(action.Id,ActionOutcome.OutcomeUnknown,null,"The execution outcome could not be confirmed; reconcile before retrying.",[]);}
    }
    bool resultSaved=true;
    try
    {
     await evidence.SaveNewAsync(EvidenceRecord.Create(Guid.NewGuid(),"result",plan.Context,clock.GetUtcNow(),StrictJson.Value(new ResultRecord(runId,plan.Id,result))),CancellationToken.None);
    }
    catch(Exception e) when(e is IOException or UnauthorizedAccessException or EvidenceValidationException)
    {
     complete=false;resultSaved=false;halted=true;
     if(result.Actor is not null)result=result with { Outcome=ActionOutcome.OutcomeUnknown,Reason="A result was observed but could not be durably saved; reconcile before retrying." };
    }
    if(progress is not null&&notifyResult&&resultSaved)
    {
     try{await progress.AfterActionAsync(plan,action,result,CancellationToken.None);}
     catch(Exception)
     {
      complete=false;halted=true;
      result=result with { Reason=result.Reason+" Companion run log unavailable after execution; remaining actions were stopped." };
     }
    }
    results.Add(result);
    if(result.Outcome!=ActionOutcome.AppliedVerified)halted=true;
   }
   var run=new RunRecord(runId,plan.Id,plan.Context,started,clock.GetUtcNow(),results.AsReadOnly(),complete);
   try{await evidence.SaveNewAsync(EvidenceRecord.Create(run.Id,"run",run.Context,run.FinishedAt!.Value,StrictJson.Value(run)),CancellationToken.None);}
   catch(Exception e) when(e is IOException or UnauthorizedAccessException or EvidenceValidationException){run=run with { EvidenceComplete=false };}
   return run;
  }
  finally{gate.Release();}
 }
}
public sealed class LocalActionDispatcher(OperationRegistry registry,IExecutionContextProvider contexts,TimeProvider? clock=null):IActionDispatcher
{
 public async Task<ActionResult> DispatchAsync(ReviewedPlan plan,PlannedAction action,CancellationToken ct)
 {
  var operation=registry.Find(action.Request.OperationId);var context=await contexts.GetCurrentAsync(ct);
  var readiness=await operation.InspectAsync(context,action.Request.Parameters,ct);
  if(!readiness.CanExecute||readiness.Scope!=action.Readiness.Scope)return new(action.Id,ActionOutcome.Failed,null,"Not started: prerequisites or scope changed.",[]);
  if(readiness.Privilege==PrivilegeRequirement.Administrator&&!context.IsElevated)return new(action.Id,ActionOutcome.Failed,null,"Not started: this action requires the verified elevation worker.",[]);
  var before=await operation.CaptureBeforeAsync(context,action.Request.Parameters,ct);
  var validation=new PlanService(registry,clock??TimeProvider.System).ValidateAction(plan,action,context,plan.Catalogue,before,(clock??TimeProvider.System).GetUtcNow());
  if(!validation.IsValid)return new(action.Id,ActionOutcome.Failed,null,"Not started: "+string.Join(" ",validation.Reasons),[]);
  ct.ThrowIfCancellationRequested();
  try{return await operation.ExecuteAsync(context,Settings.PowerPlanSequence.AtDispatch(plan,action),ct);}
  catch(Exception){return new(action.Id,ActionOutcome.OutcomeUnknown,null,"The action's completion could not be confirmed; reconcile before retrying.",[]);}
 }
}
