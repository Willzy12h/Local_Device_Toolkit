using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Elevation;
using LocalDeviceToolkit.Engine.Planning;
namespace LocalDeviceToolkit.Windows.Elevation;
public sealed class WindowsActionDispatcher(OperationRegistry operations,IExecutionContextProvider identity,string workerPath):IActionDispatcher
{
 public async Task<ActionResult> DispatchAsync(ReviewedPlan plan,PlannedAction action,CancellationToken ct)
 {
  if(action.Readiness.Scope!=OperationScope.Machine)return await new LocalActionDispatcher(operations,identity).DispatchAsync(plan,action,ct);
  var operation=operations.Find(action.Request.OperationId);var context=await identity.GetCurrentAsync(ct);
  var readiness=await operation.InspectAsync(context,action.Request.Parameters,ct);var before=await operation.CaptureBeforeAsync(context,action.Request.Parameters,ct);
  var valid=new PlanService(operations,TimeProvider.System).ValidateAction(plan,action,context,plan.Catalogue,before,DateTimeOffset.UtcNow);
  if(!valid.IsValid||!readiness.CanExecute||readiness.Scope!=action.Readiness.Scope||readiness.Privilege!=action.Readiness.Privilege)return new(action.Id,ActionOutcome.Failed,null,"Not started: this machine action requires a fresh review.",[]);
  try
  {
   var response=await new UacWorkerBroker(workerPath).RequestAsync(new(1,WorkerMode.ExecuteAction,plan.Context,null,plan,action.Id),ct);
   if(!response.Success)return new(action.Id,ActionOutcome.CancelledBeforeExecution,null,response.Message,[]);
   return response.Result is { } result&&result.ActionId==action.Id?result:new(action.Id,ActionOutcome.OutcomeUnknown,null,"The protected worker did not return this action's outcome. Reconcile before retrying.",[]);
  }
  catch(ElevationCancelledException error){return new(action.Id,ActionOutcome.CancelledBeforeExecution,null,error.Message,[]);}
 }
}
