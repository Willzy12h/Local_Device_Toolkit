using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Execution;
namespace LocalDeviceToolkit.Engine.Elevation;
public static class WorkerRequestValidator
{
 static readonly HashSet<string> MachineChecks=new(StringComparer.Ordinal){"os.version","os.architecture","secureboot.enabled","tpm.ready","bitlocker.protected","codeintegrity.enforced","antivirus.active","antivirus.providers","laps.configured","paths.enabled","faststartup.enabled","power.acsleep.seconds"};
 public static void Validate(WorkerRequest request,LocalCatalogue catalogue,WorkflowCatalogue workflows)
 {
  if(request.SchemaVersion!=1||request.OriginalContext is null)throw new PlanValidationException("Unsupported worker request.");
  switch(request.Mode)
  {
   case WorkerMode.CaptureProtected:
    if(request.Plan is not null||request.ActionId is not null||request.Capture is null||request.Capture.Origin is null||request.Capture.ControlIds is null||request.Capture.ControlIds.Count is <1 or >30)throw new PlanValidationException("Invalid protected capture request.");
    var origin=request.Capture.Origin;
    var stage=workflows.Workflows.SingleOrDefault(w=>w.Id==origin.WorkflowId)?.Stages.SingleOrDefault(s=>s.Id==origin.StageId)??throw new PlanValidationException("Unknown capture workspace.");
    var allowed=stage.TaskIds.SelectMany(id=>workflows.ResolveTask(id).ControlIds).ToHashSet(StringComparer.Ordinal);
    foreach(var id in request.Capture.ControlIds)
    {
     var control=catalogue.Controls.SingleOrDefault(c=>c.Id==id);
     if(!allowed.Contains(id)||control is null||control.CheckIds.Length==0||control.CheckIds.Any(check=>!MachineChecks.Contains(check)))throw new PlanValidationException("Protected reads cannot inspect another user's settings, unregistered checks or manual-only controls.");
    }
    break;
   case WorkerMode.ExecuteAction:
    if(request.Capture is not null||request.Plan is null||request.ActionId is null||request.Plan.Context!=request.OriginalContext||request.Plan.Actions is null||request.Plan.Actions.Count(a=>a is not null&&a.Id==request.ActionId)!=1)throw new PlanValidationException("Invalid reviewed action request.");
    break;
   default:throw new PlanValidationException("Unregistered worker operation.");
  }
 }
}
