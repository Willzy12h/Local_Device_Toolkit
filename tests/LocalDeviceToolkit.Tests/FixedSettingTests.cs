using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Settings;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class FixedSettingTests
{
 [Fact] public async Task Current_user_setting_refuses_an_alternate_admin_profile()
 {
  var backend=new Backend{Actor=TestData.Actor with {Sid="S-1-5-21-500",IsElevated=true}};var operation=Create(backend);
  Assert.False((await operation.InspectAsync(TestData.Context,Parameters,default)).CanExecute);Assert.Equal(0,backend.Writes);
 }
 [Fact] public async Task Policy_inspection_failure_and_conflicts_block_changes_even_with_manual_acknowledgement()
 {
  var backend=new Backend{Policy=new(false,false,"Unavailable")};var op=Create(backend);Assert.False((await op.InspectAsync(TestData.Context,Parameters,default)).CanExecute);
  backend.Policy=new(true,true,"Managed");Assert.False((await op.InspectAsync(TestData.Context,Parameters,default)).CanExecute);
 }
 [Fact] public void Script_and_registry_paths_are_not_parameter_channels()=>Assert.Throws<PlanValidationException>(()=>Create(new()).ValidateParameters(StrictJson.Value(new{confirmManagedPolicyReview=true,path="HKLM\\anything",command="arbitrary"})));
 [Fact] public async Task Absent_before_value_is_evidence_of_absence_and_not_an_invented_default()
 {
  var before=await Create(new()).CaptureBeforeAsync(TestData.Context,Parameters,default);var value=Assert.Single(before).Value!.Value;
  Assert.False(value.GetProperty("exists").GetBoolean());Assert.Equal(System.Text.Json.JsonValueKind.Null,value.GetProperty("value").ValueKind);
 }
 [Theory][InlineData(true,ActionOutcome.AppliedVerified)][InlineData(false,ActionOutcome.AppliedUnverified)]
 public async Task Exit_without_the_desired_readback_is_never_verified(bool effective,ActionOutcome expected)
 {
  var backend=new Backend{Effective=effective};var operation=Create(backend);var before=await operation.CaptureBeforeAsync(TestData.Context,Parameters,default);
  var action=new PlannedAction(Guid.NewGuid(),new("windows.file-extensions",operation.Id,Parameters,new("setup","windows")),await operation.InspectAsync(TestData.Context,Parameters,default),before,ObservationFingerprint.Of(before));
  Assert.Equal(expected,(await operation.ExecuteAsync(TestData.Context,action,default)).Outcome);Assert.Equal(1,backend.Writes);
 }
 [Fact] public async Task Drift_after_review_does_not_reach_the_write()
 {
  var backend=new Backend();var op=Create(backend);var before=await op.CaptureBeforeAsync(TestData.Context,Parameters,default);var action=new PlannedAction(Guid.NewGuid(),new("windows.file-extensions",op.Id,Parameters,new("setup","windows")),await op.InspectAsync(TestData.Context,Parameters,default),before,ObservationFingerprint.Of(before));backend.State=new(true,1);
  var result=await op.ExecuteAsync(TestData.Context,action,default);Assert.Equal(ActionOutcome.Failed,result.Outcome);Assert.Equal(0,backend.Writes);
 }
 [Fact] public async Task Machine_write_requires_an_actual_elevated_actor()
 {
  var backend=new Backend();var op=new FixedSettingOperation(FixedSettingOperation.Definitions.Single(d=>d.Setting==SettingId.LongPaths),backend);
  var before=await op.CaptureBeforeAsync(TestData.Context,Parameters,default);var action=new PlannedAction(Guid.NewGuid(),new("control.cfg-win-007",op.Id,Parameters,new("setup","windows")),await op.InspectAsync(TestData.Context,Parameters,default),before,ObservationFingerprint.Of(before));
  Assert.Equal(ActionOutcome.Failed,(await op.ExecuteAsync(TestData.Context,action,default)).Outcome);Assert.Equal(0,backend.Writes);
 }
 [Theory][InlineData(false,true)][InlineData(true,false)]
 public async Task AC_readback_must_still_belong_to_the_reviewed_power_scheme(bool switchPlan,bool verified)
 {
  var backend=new PowerBackend(switchPlan);var op=new FixedSettingOperation(FixedSettingOperation.Definitions.Single(d=>d.Setting==SettingId.NoAcSleep),backend);
  var before=await op.CaptureBeforeAsync(TestData.Context,Parameters,default);var action=new PlannedAction(Guid.NewGuid(),new("windows.power-ac-sleep",op.Id,Parameters,new("setup","windows")),await op.InspectAsync(TestData.Context,Parameters,default),before,ObservationFingerprint.Of(before));
  var result=await op.ExecuteAsync(TestData.Context,action,default);Assert.Equal(verified?ActionOutcome.AppliedVerified:ActionOutcome.AppliedUnverified,result.Outcome);
 }
 sealed class PowerBackend(bool switchPlan):ISettingsBackend
 {
  readonly Guid original=Guid.NewGuid();readonly Guid other=Guid.NewGuid();bool applied;
  public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>Task.FromResult(TestData.Actor with {IsElevated=true});
  public Task<SettingPolicy> InspectPolicyAsync(SettingId id,CancellationToken ct)=>Task.FromResult(new SettingPolicy(true,false,"fixture"));
  public Task<SettingSnapshot> ReadAsync(SettingId id,DeviceContext context,CancellationToken ct)=>Task.FromResult(new SettingSnapshot(id,StrictJson.Value(new AcSleepState(applied&&switchPlan?other:original,applied?0u:900u)),"fixture power API",DateTimeOffset.UtcNow));
  public Task ApplyAsync(SettingId id,DeviceContext context,SettingSnapshot before,CancellationToken ct){applied=true;return Task.CompletedTask;}
 }
 static readonly System.Text.Json.JsonElement Parameters=StrictJson.Value(new SettingParameters(true));
 static FixedSettingOperation Create(Backend b)=>new(FixedSettingOperation.Definitions.Single(d=>d.Setting==SettingId.ShowFileExtensions),b);
 sealed class Backend:ISettingsBackend
 {
  public ExecutionActor Actor=TestData.Actor;public SettingPolicy Policy=new(true,false,"No conflict observed in fixture sources.");public RegistryDwordState State=new(false,null);public int Writes;public bool Effective=true;
  public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>Task.FromResult(Actor);
  public Task<SettingPolicy> InspectPolicyAsync(SettingId id,CancellationToken ct)=>Task.FromResult(Policy);
  public Task<SettingSnapshot> ReadAsync(SettingId id,DeviceContext context,CancellationToken ct)=>Task.FromResult(new SettingSnapshot(id,StrictJson.Value(State),"fixture registry snapshot",DateTimeOffset.UtcNow));
  public Task ApplyAsync(SettingId id,DeviceContext context,SettingSnapshot before,CancellationToken ct){Writes++;if(Effective)State=new(true,0);return Task.CompletedTask;}
 }
}
