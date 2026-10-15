using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Settings;
using LocalDeviceToolkit.Engine.Execution;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class UserSettingTests
{
 [Fact] public void Setting_UK_defaults_preserves_existing_language_and_input_methods()
 {
  var before=new RegionState("fr-FR",84,null,[new("fr-FR",["040C:0000040C"],true,true)]);
  var after=new RegionState("en-GB",242,"0809:00000809",[..before.Languages,new("en-GB",["0809:00000809"],false,true)]);
  Assert.True(UserSettingOperation.RegionVerified(before,after));Assert.False(UserSettingOperation.RegionVerified(before,after with {Languages=[after.Languages[1]]}));
 }
 [Fact] public void Region_verification_does_not_ignore_changed_handwriting_preferences()
 {
  var before=new RegionState("en-GB",242,"0809:00000809",[new("en-GB",["0809:00000809"],true,true)]);
  Assert.False(UserSettingOperation.RegionVerified(before,before with {Languages=[before.Languages[0] with {Handwriting=false}]}));
 }
 [Fact] public void Microsoft_365_and_unknown_publisher_packages_are_never_consumer_removal_targets()
 {
  Assert.Throws<PlanValidationException>(()=>UserSettingOperation.ValidateConsumerIdentity(new([new("Microsoft.MicrosoftOfficeHub_1_x64__8wekyb3d8bbwe","Microsoft.MicrosoftOfficeHub_8wekyb3d8bbwe","8wekyb3d8bbwe","1.0")])));
  Assert.Throws<PlanValidationException>(()=>UserSettingOperation.ValidateConsumerIdentity(new([new("Microsoft.Copilot_1_x64__evil","Microsoft.Copilot_evil","evil","1.0")])));
  UserSettingOperation.ValidateConsumerIdentity(new([]));
 }
 [Fact] public async Task Alternate_admin_cannot_prepare_current_user_changes()
 {
  var backend=new Backend {Actor=TestData.Actor with {Sid="S-1-5-21-500",IsElevated=true}};var op=new UserSettingOperation(UserSettingId.UkRegionKeyboard,backend);
  Assert.False((await op.InspectAsync(TestData.Context,StrictJson.Value(new SettingParameters(true)),default)).CanExecute);Assert.Equal(0,backend.Reads);
 }
 [Fact] public async Task Policy_failure_blocks_consumer_removal()
 {
  var backend=new Backend {Policy=new(false,false,"Inspection unavailable")};var op=new UserSettingOperation(UserSettingId.RemoveConsumerCopilot,backend);
  Assert.False((await op.InspectAsync(TestData.Context,StrictJson.Value(new SettingParameters(true)),default)).CanExecute);
 }
 [Theory][InlineData(true,ActionOutcome.AppliedVerified)][InlineData(false,ActionOutcome.AppliedUnverified)]
 public async Task Current_user_region_change_requires_independent_readback_and_preservation(bool preserve,ActionOutcome expected)
 {
  var backend=new Backend {DropOriginalLanguage= !preserve};var op=new UserSettingOperation(UserSettingId.UkRegionKeyboard,backend);var parameters=StrictJson.Value(new SettingParameters(true));
  var before=await op.CaptureBeforeAsync(TestData.Context,parameters,default);var action=new PlannedAction(Guid.NewGuid(),new("control.cfg-win-010",op.Id,parameters,new("setup","windows")),await op.InspectAsync(TestData.Context,parameters,default),before,LocalDeviceToolkit.Engine.Planning.ObservationFingerprint.Of(before));
  var result=await op.ExecuteAsync(TestData.Context,action,default);Assert.Equal(expected,result.Outcome);Assert.Equal(1,backend.Writes);Assert.Equal(TestData.Context.OriginalUserSid,result.Actor!.Sid);
 }
 [Fact] public async Task Drift_before_current_user_mutation_prevents_the_write()
 {
  var backend=new Backend();var op=new UserSettingOperation(UserSettingId.UkRegionKeyboard,backend);var parameters=StrictJson.Value(new SettingParameters(true));var before=await op.CaptureBeforeAsync(TestData.Context,parameters,default);
  var action=new PlannedAction(Guid.NewGuid(),new("control.cfg-win-010",op.Id,parameters,new("setup","windows")),await op.InspectAsync(TestData.Context,parameters,default),before,LocalDeviceToolkit.Engine.Planning.ObservationFingerprint.Of(before));backend.Region=backend.Region with {HomeGeoId=99};
  Assert.Equal(ActionOutcome.Failed,(await op.ExecuteAsync(TestData.Context,action,default)).Outcome);Assert.Equal(0,backend.Writes);
 }
 sealed class Backend:IUserSettingsBackend
 {
  public ExecutionActor Actor=TestData.Actor;public SettingPolicy Policy=new(true,false,"fixture");public int Reads;public int Writes;public bool DropOriginalLanguage;
  public RegionState Region=new("fr-FR",84,null,[new("fr-FR",["040C:0000040C"],true,true)]);
  public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>Task.FromResult(Actor);
  public Task<SettingPolicy> InspectPolicyAsync(UserSettingId setting,CancellationToken ct)=>Task.FromResult(Policy);
  public Task<UserSettingSnapshot> ReadAsync(UserSettingId setting,DeviceContext c,CancellationToken ct){Reads++;return Task.FromResult(new UserSettingSnapshot(setting,StrictJson.Value(Region),"fixture user preferences",DateTimeOffset.UtcNow));}
  public Task ApplyAsync(UserSettingId setting,DeviceContext c,UserSettingSnapshot before,CancellationToken ct)
  {Writes++;Region=new("en-GB",242,"0809:00000809",DropOriginalLanguage?[new("en-GB",["0809:00000809"],false,true)]:[..Region.Languages,new("en-GB",["0809:00000809"],false,true)]);return Task.CompletedTask;}
 }
}
