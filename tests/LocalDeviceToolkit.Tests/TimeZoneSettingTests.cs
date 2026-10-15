using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Settings;
using LocalDeviceToolkit.Engine.Planning;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class TimeZoneSettingTests
{
 [Theory][InlineData(true,PrivilegeRequirement.CurrentUser)][InlineData(false,PrivilegeRequirement.Administrator)]
 public async Task Time_zone_uses_the_original_tokens_assigned_right(bool assigned,PrivilegeRequirement privilege)
 {
  var b=new Backend {Assigned=assigned};var op=Operation(b);var readiness=await op.InspectAsync(TestData.Context,Parameters,default);
  Assert.True(readiness.CanExecute);Assert.Equal(OperationScope.Machine,readiness.Scope);Assert.Equal(privilege,readiness.Privilege);
 }
 [Theory][InlineData(false,ActionOutcome.AppliedVerified)][InlineData(true,ActionOutcome.AppliedUnverified)]
 public async Task Time_zone_preserves_the_existing_daylight_saving_choice(bool drift,ActionOutcome expected)
 {
  var b=new Backend {Assigned=true,ChangeDaylight=drift};var op=Operation(b);var before=await op.CaptureBeforeAsync(TestData.Context,Parameters,default);
  var action=new PlannedAction(Guid.NewGuid(),new("control.cfg-win-011",op.Id,Parameters,new("setup","windows")),await op.InspectAsync(TestData.Context,Parameters,default),before,ObservationFingerprint.Of(before));
  Assert.Equal(expected,(await op.ExecuteAsync(TestData.Context,action,default)).Outcome);Assert.Equal(1,b.Writes);
 }
 [Fact] public async Task Battery_sleep_drift_is_not_a_verified_AC_only_change()
 {
  var b=new PowerBackend();var op=new FixedSettingOperation(FixedSettingOperation.Definitions.Single(d=>d.Setting==SettingId.NoAcSleep),b);var before=await op.CaptureBeforeAsync(TestData.Context,Parameters,default);
  var action=new PlannedAction(Guid.NewGuid(),new("windows.power-ac-sleep",op.Id,Parameters,new("setup","windows")),await op.InspectAsync(TestData.Context,Parameters,default),before,ObservationFingerprint.Of(before));
  Assert.Equal(ActionOutcome.AppliedUnverified,(await op.ExecuteAsync(TestData.Context,action,default)).Outcome);
 }
 static readonly System.Text.Json.JsonElement Parameters=StrictJson.Value(new SettingParameters(true));
 static FixedSettingOperation Operation(Backend b)=>new(FixedSettingOperation.Definitions.Single(d=>d.Setting==SettingId.UkTimeZone),b);
 sealed class Backend:ISettingsBackend
 {
  public bool Assigned,ChangeDaylight;public int Writes;
  public Task<PrivilegeRequirement> GetPrivilegeAsync(SettingId id,DeviceContext context,CancellationToken ct)=>Task.FromResult(Assigned?PrivilegeRequirement.CurrentUser:PrivilegeRequirement.Administrator);
  public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>Task.FromResult(TestData.Actor);
  public Task<SettingPolicy> InspectPolicyAsync(SettingId id,CancellationToken ct)=>Task.FromResult(new SettingPolicy(true,false,"fixture"));
  public Task<SettingSnapshot> ReadAsync(SettingId id,DeviceContext context,CancellationToken ct)=>Task.FromResult(new SettingSnapshot(id,StrictJson.Value(new TimeZoneState(Writes>0?"GMT Standard Time":"Pacific Standard Time",Writes>0&&ChangeDaylight?false:true)),"fixture timezone",DateTimeOffset.UtcNow));
  public Task ApplyAsync(SettingId id,DeviceContext context,SettingSnapshot before,CancellationToken ct){Writes++;return Task.CompletedTask;}
 }
 sealed class PowerBackend:ISettingsBackend
 {
  readonly Guid plan=Guid.NewGuid();bool applied;
  public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>Task.FromResult(TestData.Actor with {IsElevated=true});
  public Task<SettingPolicy> InspectPolicyAsync(SettingId id,CancellationToken ct)=>Task.FromResult(new SettingPolicy(true,false,"fixture"));
  public Task<SettingSnapshot> ReadAsync(SettingId id,DeviceContext context,CancellationToken ct)=>Task.FromResult(new SettingSnapshot(id,StrictJson.Value(new AcSleepState(plan,applied?0u:900u,applied?120u:600u)),"fixture power",DateTimeOffset.UtcNow));
  public Task ApplyAsync(SettingId id,DeviceContext context,SettingSnapshot before,CancellationToken ct){applied=true;return Task.CompletedTask;}
 }
}
