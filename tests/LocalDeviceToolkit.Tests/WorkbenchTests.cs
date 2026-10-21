using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Workbench;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Planning;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class WorkbenchTests
{
 [Fact] public void Navigation_does_not_inspect_or_change_queue()
 {
  var f=new Fixture();f.Session.Navigate("setup","windows");f.Session.Navigate("diagnostics","configuration");
  Assert.Equal(0,f.Inspector.Calls);Assert.Empty(f.Session.Queue.Requests);Assert.Empty(f.Session.Captures);
  Assert.All(f.Session.Tasks(),t=>Assert.Equal("Not checked",t.Status));
 }
 [Fact] public async Task Both_workspaces_show_the_same_canonical_task_and_saved_capture()
 {
  var f=new Fixture();f.Session.Navigate("setup","windows");var setup=f.Session.Tasks().Single(t=>t.Definition.Id=="windows.file-extensions");
  var capture=await f.Session.CaptureAsync([setup.Definition.Id],default);
  f.Session.Navigate("diagnostics","configuration");var diagnostic=f.Session.Tasks().Single(t=>t.Definition.Id==setup.Definition.Id);
  Assert.Same(setup.Definition,diagnostic.Definition);Assert.Equal("Meets standard",diagnostic.Status);Assert.Equal(capture.CapturedAt,diagnostic.CapturedAt);Assert.Single(f.Store.Records,x=>x.Kind=="capture");Assert.Single(f.Store.Records,x=>x.Kind=="capture-selection.v2");Assert.Single(f.Session.Captures);
 }
 [Fact] public async Task Failed_save_does_not_present_capture_as_durable_evidence()
 {
  var f=new Fixture();f.Store.Fail=true;f.Session.Navigate("diagnostics","configuration");
  await Assert.ThrowsAsync<IOException>(()=>f.Session.CaptureAsync(["windows.file-extensions"],default));Assert.Empty(f.Session.Captures);
 }
 [Fact] public async Task Protected_capture_preserves_original_engineer_and_actual_administrator()
 {
  var f=new Fixture();f.Session.Navigate("diagnostics","health");var origin=f.Session.Origin;
  var capture=TestData.Capture() with {Origin=origin,CapturedBy=TestData.Actor with {Sid="S-1-5-21-500",UserName="TEST\\admin",IsElevated=true},Observations=[new("CMP-WIN-001","secureboot.enabled",ObservationState.Known,StrictJson.Value(true),"fixture",DateTimeOffset.UtcNow,"")]};
  await f.Session.AcceptProtectedCaptureAsync(capture,origin,default);Assert.Equal(TestData.Context.OriginalUserSid,Assert.Single(f.Session.Captures).Context.OriginalUserSid);Assert.NotEqual(TestData.Context.OriginalUserSid,f.Session.Captures[0].CapturedBy.Sid);
  await Assert.ThrowsAsync<EvidenceValidationException>(()=>f.Session.AcceptProtectedCaptureAsync(capture with {Context=capture.Context with {OriginalUserSid=capture.CapturedBy.Sid}},origin,default));Assert.Single(f.Store.Records);
 }
 [Fact] public async Task Task_outside_selected_stage_cannot_start_a_probe()
 {
  var f=new Fixture();f.Session.Navigate("diagnostics","health");
  await Assert.ThrowsAsync<CatalogueValidationException>(()=>f.Session.CaptureAsync(["windows.file-extensions"],default));Assert.Equal(0,f.Inspector.Calls);
 }
 sealed class Fixture
 {
  public readonly Probe Inspector=new();public readonly Store Store=new();public readonly WorkbenchSession Session;
  public Fixture(){var workflows=WorkflowCatalogue.LoadShipped();Session=new(TestData.Context,CatalogueLoader.LoadShipped(),workflows,Inspector,Store,new OperationRegistry(workflows,[]));}
 }
 sealed class Probe:IDeviceInspector
 {
  public int Calls;
  public Task<DeviceCapture> CaptureAsync(CaptureRequest request,CancellationToken ct){Calls++;return Task.FromResult(TestData.Capture() with { Origin=request.Origin,Observations=[new("CFG-WIN-012","extensions.visible",ObservationState.Known,StrictJson.Value(true),"fixture",DateTimeOffset.UtcNow,"")] });}
 }
 sealed class Store:IEvidenceStore
 {
  public bool Fail;public List<EvidenceRecord> Records=[];
  public Task SaveNewAsync(EvidenceRecord record,CancellationToken ct){if(Fail)throw new IOException("Disk full fixture.");Records.Add(record);return Task.CompletedTask;}
  public Task<EvidenceRecord> LoadAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
  public Task<HistoryPage> ReadHistoryAsync(string? cursor,CancellationToken ct)=>Task.FromResult(new HistoryPage(Records,[],null));
 }
}
