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
 [Fact] public async Task An_absent_consumer_app_is_no_change_needed_and_cannot_claim_a_removal()
 {
  var op=new UserSettingOperation(UserSettingId.RemoveConsumerCopilot,new AbsentConsumer());var readiness=await op.InspectAsync(TestData.Context,StrictJson.Value(new SettingParameters(true)),default);
  Assert.False(readiness.CanExecute);Assert.Contains("No change needed",readiness.Reason);
 }
 [Theory]
 [InlineData("UkFormats","settings.uk-formats")]
 [InlineData("UkHomeCountry","settings.uk-home-country")]
 [InlineData("UkInputLanguage","settings.uk-input-language")]
 [InlineData("UkKeyboard","settings.uk-keyboard")]
 [InlineData("UkDefaultKeyboard","settings.uk-default-keyboard")]
 public async Task Atomic_UK_changes_only_the_selected_target(string name,string id)
 {
  Assert.True(Enum.TryParse<UserSettingId>(name,out var setting),"Atomic setting must exist: "+name);
  var backend=new Backend();backend.Region=backend.Region with {Languages=[..backend.Region.Languages,new("en-GB",["0809:00000809"],false,true)]};
  if(name=="UkInputLanguage")backend.Region=backend.Region with {Languages=[backend.Region.Languages[0]]};
  if(name=="UkKeyboard")backend.Region=backend.Region with {Languages=[backend.Region.Languages[0],backend.Region.Languages[1] with {InputMethodTips=["0809:00000409"]}]};
  var original=backend.Region;var op=new UserSettingOperation(setting,backend);Assert.Equal(id,op.Id);
  var result=await op.ExecuteAsync(TestData.Context,await Review(op),default);
  Assert.Equal(ActionOutcome.AppliedVerified,result.Outcome);Assert.Equal(1,backend.Writes);
  if(name!="UkFormats")Assert.Equal(original.Culture,backend.Region.Culture);
  if(name!="UkHomeCountry")Assert.Equal(original.HomeGeoId,backend.Region.HomeGeoId);
  if(name!="UkDefaultKeyboard")Assert.Equal(original.DefaultInputMethodOverride,backend.Region.DefaultInputMethodOverride);
  if(name=="UkInputLanguage")Assert.Empty(backend.Region.Languages.Last().InputMethodTips);
 }
 [Theory][InlineData("UkKeyboard")][InlineData("UkDefaultKeyboard")]
 public async Task Missing_UK_prerequisites_are_reviewable_but_never_implicitly_added(string name)
 {
  Assert.True(Enum.TryParse<UserSettingId>(name,out var setting));var backend=new Backend();var op=new UserSettingOperation(setting,backend);
  if(name=="UkDefaultKeyboard")backend.Region=backend.Region with {Languages=[..backend.Region.Languages,new("en-GB",[],false,true)]};
  var action=await Review(op);Assert.True(action.Readiness.CanExecute);
  var result=await op.ExecuteAsync(TestData.Context,action,default);Assert.Equal(ActionOutcome.Failed,result.Outcome);Assert.Equal(0,backend.Writes);Assert.Contains("prerequisite",result.Reason,StringComparison.OrdinalIgnoreCase);
 }
 [Fact] public async Task Separately_reviewed_UK_targets_allow_selected_prerequisites_in_order()
 {
  Assert.True(Enum.TryParse<UserSettingId>("UkInputLanguage",out var language));Assert.True(Enum.TryParse<UserSettingId>("UkKeyboard",out var keyboard));Assert.True(Enum.TryParse<UserSettingId>("UkDefaultKeyboard",out var defaultKeyboard));
  var backend=new Backend();var operations=new[]{language,keyboard,defaultKeyboard}.Select(s=>new UserSettingOperation(s,backend)).ToArray();
  var actions=new List<PlannedAction>();foreach(var op in operations)actions.Add(await Review(op));
  for(var i=0;i<operations.Length;i++)Assert.Equal(ActionOutcome.AppliedVerified,(await operations[i].ExecuteAsync(TestData.Context,actions[i],default)).Outcome);
  Assert.Equal(3,backend.Writes);Assert.Equal("fr-FR",backend.Region.Culture);Assert.Equal(84,backend.Region.HomeGeoId);
 }
 [Theory][InlineData(UserSettingId.UkFormats)][InlineData(UserSettingId.UkHomeCountry)][InlineData(UserSettingId.UkInputLanguage)][InlineData(UserSettingId.UkKeyboard)][InlineData(UserSettingId.UkDefaultKeyboard)]
 public async Task Atomic_actions_require_original_user_policy_and_unchanged_target(UserSettingId setting)
 {
  var backend=new Backend();var op=new UserSettingOperation(setting,backend);var action=await Review(op);
  backend.Actor=backend.Actor with {Sid="S-1-5-21-other"};Assert.Equal(ActionOutcome.Failed,(await op.ExecuteAsync(TestData.Context,action,default)).Outcome);Assert.Equal(0,backend.Writes);
  backend.Actor=TestData.Actor;backend.Policy=new(true,true,"Managed policy");Assert.Equal(ActionOutcome.Failed,(await op.ExecuteAsync(TestData.Context,action,default)).Outcome);Assert.Equal(0,backend.Writes);
  backend.Policy=new(true,false,"fixture");backend.Region=setting switch {
   UserSettingId.UkFormats=>backend.Region with {Culture="de-DE"},UserSettingId.UkHomeCountry=>backend.Region with {HomeGeoId=99},
   UserSettingId.UkInputLanguage=>backend.Region with {Languages=[..backend.Region.Languages,new("en-GB",[],false,true)]},
   UserSettingId.UkKeyboard=>backend.Region with {Languages=[..backend.Region.Languages,new("en-GB",["0809:00000809"],false,true)]},
   _=>backend.Region with {DefaultInputMethodOverride="040C:0000040C"}};
  Assert.Equal(ActionOutcome.Failed,(await op.ExecuteAsync(TestData.Context,action,default)).Outcome);Assert.Equal(0,backend.Writes);
 }
 [Theory][InlineData(UserSettingId.UkFormats)][InlineData(UserSettingId.UkHomeCountry)][InlineData(UserSettingId.UkInputLanguage)][InlineData(UserSettingId.UkKeyboard)][InlineData(UserSettingId.UkDefaultKeyboard)]
 public void Atomic_verification_rejects_changes_to_any_unselected_preference(UserSettingId setting)
 {
  var before=new RegionState("fr-FR",84,null,[new("fr-FR",["040C:0000040C"],true,true),new("en-GB",["0809:00000809"],false,true)]);
  var after=setting switch {UserSettingId.UkFormats=>before with {Culture="en-GB"},UserSettingId.UkHomeCountry=>before with {HomeGeoId=242},UserSettingId.UkDefaultKeyboard=>before with {DefaultInputMethodOverride="0809:00000809"},_=>before};
  Assert.True(UserSettingOperation.AtomicRegionVerified(setting,before,after));
  Assert.False(UserSettingOperation.AtomicRegionVerified(setting,before,after with {Languages=[after.Languages[0] with {Handwriting=false},after.Languages[1]]}));
  Assert.False(UserSettingOperation.AtomicRegionVerified(setting,before,after with {Languages=after.Languages.Reverse().ToArray()}));
 }
 static async Task<PlannedAction> Review(UserSettingOperation op)
 {
  var parameters=StrictJson.Value(new SettingParameters(true));var before=await op.CaptureBeforeAsync(TestData.Context,parameters,default);
  return new(Guid.NewGuid(),new("control.cfg-win-010",op.Id,parameters,new("setup","windows")),await op.InspectAsync(TestData.Context,parameters,default),before,LocalDeviceToolkit.Engine.Planning.ObservationFingerprint.Of(before));
 }
 sealed class AbsentConsumer:IUserSettingsBackend
 {
  public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>Task.FromResult(TestData.Actor);
  public Task<SettingPolicy> InspectPolicyAsync(UserSettingId setting,CancellationToken ct)=>Task.FromResult(new SettingPolicy(true,false,"fixture"));
  public Task<UserSettingSnapshot> ReadAsync(UserSettingId setting,DeviceContext c,CancellationToken ct)=>Task.FromResult(new UserSettingSnapshot(setting,StrictJson.Value(new ConsumerCopilotState([])),"fixture package inventory",DateTimeOffset.UtcNow));
  public Task ApplyAsync(UserSettingId setting,DeviceContext c,UserSettingSnapshot before,CancellationToken ct)=>throw new Exception("Must not write an absent package.");
 }
 sealed class Backend:IUserSettingsBackend
 {
  public ExecutionActor Actor=TestData.Actor;public SettingPolicy Policy=new(true,false,"fixture");public int Reads;public int Writes;public bool DropOriginalLanguage;
  public RegionState Region=new("fr-FR",84,null,[new("fr-FR",["040C:0000040C"],true,true)]);
  public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>Task.FromResult(Actor);
  public Task<SettingPolicy> InspectPolicyAsync(UserSettingId setting,CancellationToken ct)=>Task.FromResult(Policy);
  public Task<UserSettingSnapshot> ReadAsync(UserSettingId setting,DeviceContext c,CancellationToken ct){Reads++;return Task.FromResult(new UserSettingSnapshot(setting,StrictJson.Value(Region),"fixture user preferences",DateTimeOffset.UtcNow));}
  public Task ApplyAsync(UserSettingId setting,DeviceContext c,UserSettingSnapshot before,CancellationToken ct)
  {Writes++;
   if(setting!=UserSettingId.UkRegionKeyboard){Region=setting.ToString() switch {
    "UkFormats"=>Region with {Culture="en-GB"},"UkHomeCountry"=>Region with {HomeGeoId=242},
    "UkInputLanguage"=>Region with {Languages=[..Region.Languages,new("en-GB",[],false,true)]},
    "UkKeyboard"=>Region with {Languages=Region.Languages.Select(l=>l.LanguageTag=="en-GB"?l with {InputMethodTips=l.InputMethodTips.Contains("0809:00000809")?l.InputMethodTips:[..l.InputMethodTips,"0809:00000809"]}:l).ToArray()},
    "UkDefaultKeyboard"=>Region with {DefaultInputMethodOverride="0809:00000809"},_=>Region};return Task.CompletedTask;}
   Region=new("en-GB",242,"0809:00000809",DropOriginalLanguage?[new("en-GB",["0809:00000809"],false,true)]:[..Region.Languages,new("en-GB",["0809:00000809"],false,true)]);return Task.CompletedTask;}
 }
}
