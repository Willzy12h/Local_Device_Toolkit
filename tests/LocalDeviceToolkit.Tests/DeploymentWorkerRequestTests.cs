using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Engine.Elevation;
using LocalDeviceToolkit.Engine.Execution;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class DeploymentWorkerRequestTests
{
 static DeploymentProfile Profile=>new("approved-id","APP-WIN-003",DeploymentFormat.MachineMsi,"{11111111-1111-1111-1111-111111111111}","1.0","Approved app","Vendor",OperationScope.Machine,"x64","setup.msi",[new("setup.msi",new string('a',64),new string('b',64))],TimeSpan.FromMinutes(30));
 [Fact] public void Approval_is_a_closed_request_separate_from_installation()
 {
  var request=new WorkerRequest(1,WorkerMode.ApproveDeploymentProfile,TestData.Context,null,null,null,DeploymentApproval:new(Profile,Path.GetFullPath("media")));
  WorkerRequestValidator.Validate(request,CatalogueLoader.LoadShipped(),WorkflowCatalogue.LoadShipped());
  Assert.Throws<PlanValidationException>(()=>WorkerRequestValidator.Validate(request with{Mode=WorkerMode.CaptureProtected},CatalogueLoader.LoadShipped(),WorkflowCatalogue.LoadShipped()));
  Assert.Throws<PlanValidationException>(()=>WorkerRequestValidator.Validate(request with{DeploymentApproval=request.DeploymentApproval! with{Profile=Profile with{EntryPoint="arbitrary.ps1"}}},CatalogueLoader.LoadShipped(),WorkflowCatalogue.LoadShipped()));
 }
 [Fact] public void Reconciliation_never_accepts_a_plan_or_installer_profile()
 {
  var request=new WorkerRequest(1,WorkerMode.ReconcileDeployment,TestData.Context,null,null,null,DeploymentReconcile:new("approved-id",Path.GetFullPath("media")));
  WorkerRequestValidator.Validate(request,CatalogueLoader.LoadShipped(),WorkflowCatalogue.LoadShipped());
  Assert.Throws<PlanValidationException>(()=>WorkerRequestValidator.Validate(request with{DeploymentApproval=new(Profile,Path.GetFullPath("media"))},CatalogueLoader.LoadShipped(),WorkflowCatalogue.LoadShipped()));
 }
}
