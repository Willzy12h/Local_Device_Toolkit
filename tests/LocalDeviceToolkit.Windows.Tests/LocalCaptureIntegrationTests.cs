using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Workbench;
using LocalDeviceToolkit.Tests;
using LocalDeviceToolkit.Windows.Inspection;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class LocalCaptureIntegrationTests
{
 [Fact] public async Task Exact_target_is_captured_and_changing_it_removes_old_status()
 {
  var path=Path.Combine(Path.GetTempPath(),"local-integration-"+Guid.NewGuid().ToString("N"));
  try{
   var catalogue=CatalogueLoader.LoadShipped();var flows=WorkflowCatalogue.LoadShipped();var identity=new FixtureIdentity();var source=new LocalSource();
   var inspector=new WindowsDeviceInspector(identity,identity,new NoLegacy(),catalogue,TimeProvider.System,source);
   var session=new WorkbenchSession(TestData.Context,catalogue,flows,inspector,new EvidenceStore(path),new OperationRegistry(flows,[]));
   session.Navigate("diagnostics","local");var first=StrictJson.Value(new{hostname="one.example",queryType="A"});session.SetInputs("local.network.dns",first);
   var capture=await session.CaptureAsync(["local.network.dns"],default);var observation=Assert.Single(capture.Observations);
   Assert.Equal("one.example",source.Last.GetProperty("hostname").GetString());Assert.NotNull(observation.Coverage);Assert.Single(session.LatestObservations(flows.ResolveTask("local.network.dns")));
   session.SetInputs("local.network.dns",StrictJson.Value(new{hostname="two.example",queryType="A"}));Assert.Empty(session.LatestObservations(flows.ResolveTask("local.network.dns")));
   Assert.Equal(30,catalogue.Controls.Count);
  }finally{if(Directory.Exists(path))Directory.Delete(path,true);}
 }
 sealed class NoLegacy:IWindowsFactsSource {public Task<IReadOnlyList<WindowsFact>> ReadAsync(IReadOnlyList<string> ids,CancellationToken ct)=>throw new InvalidOperationException("Unselected legacy source");}
 sealed class LocalSource:ILocalProbeSource
 {
  public System.Text.Json.JsonElement Last;
  public Task<LocalProbeResult> ReadAsync(string id,System.Text.Json.JsonElement input,DeviceContext context,CancellationToken ct){Last=input;return Task.FromResult(new LocalProbeResult(2,id,LocalProbeCatalogue.TargetDigest(id,input,context),ObservationState.Known,new("Resolved",null,"Observed",new Dictionary<string,string>{{"hostname",input.GetProperty("hostname").GetString()!}}),"Fixture",DateTimeOffset.UtcNow,new(true,false,1,null,[]),"Read only"));}
 }
}
