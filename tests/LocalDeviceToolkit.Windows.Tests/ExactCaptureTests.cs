using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Assessment;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Workbench;
using LocalDeviceToolkit.Tests;
using LocalDeviceToolkit.Windows.Inspection;
using Xunit;

namespace LocalDeviceToolkit.Windows.Tests;

public sealed class ExactCaptureTests
{
 [Fact] public async Task Only_selected_readiness_check_reaches_source()
 {
  var path=Path.Combine(Path.GetTempPath(),"toolkit-exact-"+Guid.NewGuid().ToString("N"));
  try
  {
   var catalogue=CatalogueLoader.LoadShipped();var workflows=WorkflowCatalogue.LoadShipped();var identity=new FixtureIdentity();var source=new RecordingFacts();
   var inspector=new WindowsDeviceInspector(identity,identity,source,catalogue,TimeProvider.System);
   var session=new WorkbenchSession(TestData.Context,catalogue,workflows,inspector,new EvidenceStore(path),new OperationRegistry(workflows,[]));
   session.Navigate("setup","readiness");
   var capture=await session.CaptureAsync(["readiness.secure-boot"],default);
   Assert.Equal(new[]{"secureboot.enabled"},source.Requested);
   Assert.Equal("secureboot.enabled",Assert.Single(capture.Observations).CheckId);
   Assert.Equal(FindingStatus.Unknown,AssessmentService.Assess(catalogue,capture).Single(x=>x.ControlId=="CMP-WIN-001").Status);
   await session.CaptureAsync(["readiness.tpm"],default);
   Assert.Equal("secureboot.enabled",Assert.Single(session.LatestObservations(workflows.ResolveTask("readiness.secure-boot"))).CheckId);
  }
  finally {if(Directory.Exists(path))Directory.Delete(path,true);}
 }
 sealed class RecordingFacts:IWindowsFactsSource
 {
  public IReadOnlyList<string> Requested=[];
  public Task<IReadOnlyList<WindowsFact>> ReadAsync(IReadOnlyList<string> ids,CancellationToken ct)
  {
   Requested=ids.ToArray();
   return Task.FromResult<IReadOnlyList<WindowsFact>>([new("secureboot.enabled",ObservationState.Known,StrictJson.Value(true),"fixture",DateTimeOffset.UtcNow,"Observed.")]);
  }
 }
}
