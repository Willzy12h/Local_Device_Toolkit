using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Workbench;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class EngineerWorkflowTests
{
 [Fact] public void Every_stage_renders_without_executing_a_probe_or_action()
 {
  var flows=WorkflowCatalogue.LoadShipped();var catalogue=CatalogueLoader.LoadShipped();var path=Path.Combine(Path.GetTempPath(),"stage-fixture-"+Guid.NewGuid().ToString("N"));
  var session=new WorkbenchSession(TestData.Context,catalogue,flows,new NoRead(),new EvidenceStore(path),new OperationRegistry(flows,[]));
  foreach(var workflow in flows.Workflows)foreach(var stage in workflow.Stages){session.Navigate(workflow.Id,stage.Id);Assert.Equal(stage.TaskIds.Length,session.Tasks().Count);}
  Assert.Empty(session.Captures);Assert.Empty(session.Queue.Requests);
 }
 [Fact] public void Strict_local_input_refuses_duplicate_fields()
 {
  using var document=System.Text.Json.JsonDocument.Parse("""{"hostname":"one.test","hostname":"two.test","queryType":"A"}""");
  Assert.ThrowsAny<Exception>(()=>LocalProbeCatalogue.ValidateInput("local.network.dns",document.RootElement));
 }
 sealed class NoRead:IDeviceInspector {public Task<DeviceCapture> CaptureAsync(CaptureRequest r,CancellationToken ct)=>throw new InvalidOperationException("Rendering must not collect.");}
}
