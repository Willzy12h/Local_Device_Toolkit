using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Settings;
using LocalDeviceToolkit.Engine.Planning;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class PowerTimeoutTests
{
 [Fact] public void Minutes_are_bounded_and_preserve_never()
 {Assert.Equal(0u,PowerTimeoutOperation.Seconds(0));Assert.Equal(86400u,PowerTimeoutOperation.Seconds(1440));Assert.Throws<ArgumentOutOfRangeException>(()=>PowerTimeoutOperation.Seconds(1441));}
 [Fact] public async Task Alternate_user_is_blocked_and_no_unrelated_index_changes()
 {
  var backend=new Backend();var operation=new PowerTimeoutOperation(PowerSlot.DisplayDc,backend);var input=StrictJson.Value(new PowerTimeoutInput(10,true));
  var before=await operation.CaptureBeforeAsync(TestData.Context,input,default);var readiness=await operation.InspectAsync(TestData.Context,input,default);
  var action=new PlannedAction(Guid.NewGuid(),new("power.display-dc",operation.Id,input,new("setup","local")),readiness,before,ObservationFingerprint.Of(before));
  backend.Actor=TestData.Actor with {Sid="alternate",IsElevated=true};Assert.False((await operation.InspectAsync(TestData.Context,input,default)).CanExecute);Assert.Equal(0,backend.Writes);
  backend.Actor=TestData.Actor with {IsElevated=true};var result=await operation.ExecuteAsync(TestData.Context,action,default);Assert.Equal(ActionOutcome.AppliedVerified,result.Outcome);Assert.Equal(new PowerTimeoutState(backend.State.ActiveScheme,60,120,180,600),backend.State);
 }
 [Fact] public void Power_restoration_and_new_changes_share_the_same_target()
 {
  var operation=new PowerTimeoutOperation(PowerSlot.SleepAc,new Backend());var restore=new RecordedSettingRestorationOperation(null!,null!,LocalDeviceToolkit.Engine.Catalogue.CatalogueLoader.LoadShipped().Identity);
  var input=StrictJson.Value(new RecordedSettingRestorationParameters(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),true,SettingId.PowerSleepAc));
  Assert.Equal(operation.GetTargetKey(TestData.Context,StrictJson.Value(new PowerTimeoutInput(0,true))),restore.GetTargetKey(TestData.Context,input));
 }
 sealed class Backend:IPowerTimeoutBackend
 {
  public PowerTimeoutState State=new(Guid.NewGuid(),60,120,180,240);public ExecutionActor Actor=TestData.Actor;public int Writes;
  public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>Task.FromResult(Actor);
  public Task<SettingPolicy> InspectPolicyAsync(CancellationToken ct)=>Task.FromResult(new SettingPolicy(true,false,"No conflict"));
  public Task<PowerTimeoutState> ReadAsync(DeviceContext context,CancellationToken ct)=>Task.FromResult(State);
  public Task WriteAsync(DeviceContext context,PowerSlot slot,uint seconds,PowerTimeoutState expected,CancellationToken ct){Assert.Equal(expected,State);State=State.With(slot,seconds);Writes++;return Task.CompletedTask;}
 }
}
