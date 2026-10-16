using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Elevation;
using LocalDeviceToolkit.Engine.Execution;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class ProtectedInputTests
{
 [Fact] public void Unconfigured_volume_disables_protected_read_with_validation_error()
 {
  var request=new WorkerRequest(2,WorkerMode.CaptureProtected,TestData.Context,new(["local.bitlocker.state"],new("setup","local"),["local.bitlocker.state"],["local.bitlocker.state"]),null,null);
  Assert.Throws<PlanValidationException>(()=>WorkerRequestValidator.Validate(request,CatalogueLoader.LoadShipped(),WorkflowCatalogue.LoadShipped()));
 }
}
