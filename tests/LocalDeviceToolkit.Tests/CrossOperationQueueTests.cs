using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Settings;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class CrossOperationQueueTests
{
 [Theory]
 [InlineData(SettingId.ShowFileExtensions)] [InlineData(SettingId.LongPaths)]
 [InlineData(SettingId.DisableFastStartup)] [InlineData(SettingId.NoAcSleep)] [InlineData(SettingId.UkTimeZone)]
 public void Fixed_change_and_recorded_restoration_conflict_for_the_same_setting(SettingId setting)
 {
  var f=new Fixture();var queue=new DeploymentQueue(TestData.Context,f.Registry);
  Assert.Equal(QueueAddResult.Added,queue.Add(f.Fixed(setting)));
  Assert.Equal(QueueAddResult.ConflictingParameters,queue.Add(f.Restore(setting)));
  Assert.Single(queue.Requests);Assert.Equal(1,queue.Revision);
 }
 [Fact] public void Different_restoration_histories_of_one_setting_require_explicit_replacement()
 {
  var f=new Fixture();var queue=new DeploymentQueue(TestData.Context,f.Registry);
  var first=f.Restore(SettingId.LongPaths);var second=f.Restore(SettingId.LongPaths);
  queue.Add(first);
  Assert.Equal(QueueAddResult.ConflictingParameters,queue.Add(second));
  Assert.Single(queue.Requests);Assert.Equal(first.Parameters.GetRawText(),queue.Requests[0].Parameters.GetRawText());
  queue.Replace(second);Assert.Single(queue.Requests);Assert.Equal(second.Parameters.GetRawText(),queue.Requests[0].Parameters.GetRawText());
 }
 [Fact] public void Different_settings_can_coexist_in_the_queue()
 {
  var f=new Fixture();var queue=new DeploymentQueue(TestData.Context,f.Registry);
  Assert.Equal(QueueAddResult.Added,queue.Add(f.Fixed(SettingId.LongPaths)));
  Assert.Equal(QueueAddResult.Added,queue.Add(f.Restore(SettingId.ShowFileExtensions)));
  Assert.Equal(2,queue.Requests.Count);
 }
 [Fact] public async Task Direct_plan_cannot_bypass_cross_operation_target_conflicts()
 {
  var f=new Fixture();
  var error=await Assert.ThrowsAsync<PlanValidationException>(()=>new PlanService(f.Registry,TimeProvider.System).CreateAsync(TestData.Context,CatalogueLoader.LoadShipped(),[f.Fixed(SettingId.ShowFileExtensions),f.Restore(SettingId.ShowFileExtensions)],default));
  Assert.Contains("same logical target",error.Message);
 }
 sealed class Fixture
 {
  public OperationRegistry Registry{get;}
  public Fixture()
  {
   var tasks=FixedSettingOperation.Definitions.Select(d=>new TaskDefinition(d.OperationId,d.OperationId,[d.ControlId],[],d.OperationId)).Append(new("restore","Restore",["CFG-WIN-012"],[],"settings.restore-recorded")).ToArray();
   var workflows=new WorkflowCatalogue(tasks,[new("setup","Setup",[new("windows","Windows",tasks.Select(t=>t.Id).ToArray())])]);
   var backend=new Backend();
   Registry=new(workflows,FixedSettingOperation.Definitions.Select(d=>(ILocalOperation)new FixedSettingOperation(d,backend)).Append(new RecordedSettingRestorationOperation(new UnusedSource(),backend,CatalogueLoader.LoadShipped().Identity)));
  }
  public ActionRequest Fixed(SettingId setting){var d=FixedSettingOperation.Definitions.Single(d=>d.Setting==setting);return new(d.OperationId,d.OperationId,StrictJson.Value(new SettingParameters(true)),new("setup","windows"));}
  public ActionRequest Restore(SettingId setting)=>new("restore","settings.restore-recorded",StrictJson.Value(new RecordedSettingRestorationParameters(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),true,setting)),new("setup","windows"));
 }
 sealed class UnusedSource:IRecordedSettingSource
 {
  public Task<RecordedSettingSource> LoadAsync(RecordedSettingRestorationParameters p,CancellationToken ct)=>throw new InvalidOperationException("Queue target resolution must not read source history.");
 }
 sealed class Backend:ISettingRestorationBackend
 {
  public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>Task.FromResult(TestData.Actor);
  public Task<SettingPolicy> InspectPolicyAsync(SettingId setting,CancellationToken ct)=>Task.FromResult(new SettingPolicy(true,false,"fixture"));
  public Task<SettingSnapshot> ReadAsync(SettingId setting,DeviceContext context,CancellationToken ct)=>Task.FromResult(new SettingSnapshot(setting,StrictJson.Value(new RegistryDwordState(true,0)),"fixture",DateTimeOffset.UtcNow));
  public Task ApplyAsync(SettingId setting,DeviceContext context,SettingSnapshot before,CancellationToken ct)=>throw new InvalidOperationException("Queue tests never mutate.");
  public Task RestoreAsync(SettingId setting,DeviceContext context,SettingSnapshot before,SettingSnapshot target,CancellationToken ct)=>throw new InvalidOperationException("Queue tests never mutate.");
 }
}
