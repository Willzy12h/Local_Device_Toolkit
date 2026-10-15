using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Execution;
namespace LocalDeviceToolkit.Engine.Elevation;
public static class WorkerRequestValidator
{
 static readonly HashSet<string> MachineChecks=new(StringComparer.Ordinal){"os.version","os.architecture","secureboot.enabled","tpm.ready","bitlocker.protected","codeintegrity.enforced","antivirus.active","antivirus.providers","laps.configured","paths.enabled","faststartup.enabled","power.acsleep.seconds"};
 public static void Validate(WorkerRequest request,LocalCatalogue catalogue,WorkflowCatalogue workflows)
 {
  if(request.SchemaVersion!=2||request.OriginalContext is null)throw new PlanValidationException("Unsupported worker request. Use matching toolkit components.");
  if(request.Mode!=WorkerMode.ApproveDeploymentProfile&&request.DeploymentApproval is not null||request.Mode is not (WorkerMode.ReconcileDeployment or WorkerMode.ResolveDeployment)&&request.DeploymentReconcile is not null||request.Mode!=WorkerMode.ResolveDeployment&&request.DeploymentResolutionReview is not null)throw new PlanValidationException("Deployment configuration and reconciliation cannot be mixed with another request.");
  switch(request.Mode)
  {
   case WorkerMode.CaptureProtected:
    if(request.Plan is not null||request.ActionId is not null||request.Capture is null||request.Capture.Origin is null||request.Capture.ControlIds is null||request.Capture.ControlIds.Count is <1 or >30)throw new PlanValidationException("Invalid protected capture request.");
    var origin=request.Capture.Origin;
    var stage=workflows.Workflows.SingleOrDefault(w=>w.Id==origin.WorkflowId)?.Stages.SingleOrDefault(s=>s.Id==origin.StageId)??throw new PlanValidationException("Unknown capture workspace.");
    if(request.Capture.TaskIds is null||request.Capture.CheckIds is null||request.Capture.TaskIds.Count is <1 or >100||request.Capture.TaskIds.Distinct(StringComparer.Ordinal).Count()!=request.Capture.TaskIds.Count)throw new PlanValidationException("Exact task and check selection is required.");
    var selected=request.Capture.TaskIds.Select(id=>workflows.ResolveWorkflowTask(origin.WorkflowId,origin.StageId,id)).ToArray();
    var allowed=selected.SelectMany(task=>task.ControlIds).ToHashSet(StringComparer.Ordinal);
    var exact=selected.SelectMany(task=>task.CheckIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    if(!request.Capture.CheckIds.Order(StringComparer.Ordinal).SequenceEqual(exact)||!request.Capture.ControlIds.Order(StringComparer.Ordinal).SequenceEqual(allowed.Order(StringComparer.Ordinal))||exact.Length==0||exact.Any(check=>!MachineChecks.Contains(check)))throw new PlanValidationException("Protected reads must match the exact registered machine checks.");
    foreach(var id in request.Capture.ControlIds)
    {
     var control=catalogue.Controls.SingleOrDefault(c=>c.Id==id);
     if(!allowed.Contains(id)||control is null||control.CheckIds.Length==0)throw new PlanValidationException("Protected reads cannot inspect another user's settings, unregistered checks or manual-only controls.");
    }
    break;
   case WorkerMode.ExecuteAction:
    if(request.Capture is not null||request.Plan is null||request.ActionId is null||request.Plan.Context!=request.OriginalContext||request.Plan.Actions is null||request.Plan.Actions.Count(a=>a is not null&&a.Id==request.ActionId)!=1)throw new PlanValidationException("Invalid reviewed action request.");
    break;
   case WorkerMode.ApproveDeploymentProfile:
    if(request.Capture is not null||request.Plan is not null||request.ActionId is not null||request.DeploymentApproval is null)throw new PlanValidationException("Profile approval is separate from installation.");
    try{DeploymentProfiles.Validate(request.DeploymentApproval.Profile);ValidateSource(request.DeploymentApproval.SourceDirectory);}catch(ArgumentException){throw new PlanValidationException("The closed deployment profile or local media directory is invalid.");}
    break;
   case WorkerMode.ReconcileDeployment:
   case WorkerMode.ResolveDeployment:
    if(request.Mode==WorkerMode.ResolveDeployment&&request.DeploymentResolutionReview is not {ConfirmVendorCompletionAndNoPendingChildren:true})throw new PlanValidationException("Explicit vendor-completion review is required to resolve a pending deployment.");
    if(request.Capture is not null||request.Plan is not null||request.ActionId is not null||request.DeploymentReconcile is null||string.IsNullOrWhiteSpace(request.DeploymentReconcile.ProfileId)||request.DeploymentReconcile.ProfileId.Length>128)throw new PlanValidationException("Invalid read-only deployment reconciliation request.");
    ValidateSource(request.DeploymentReconcile.SourceDirectory);break;
   default:throw new PlanValidationException("Unregistered worker operation.");
  }
 }
 static void ValidateSource(string source){if(string.IsNullOrWhiteSpace(source)||source.Length>2048||!Path.IsPathFullyQualified(source))throw new PlanValidationException("An absolute local media directory is required.");}
}
