using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Settings;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class RestorationTests
{
 [Fact] public async Task Subsequent_drift_blocks_restore()
 {
  var fixture=new Fixture();fixture.Backend.State=new(true,1);
  Assert.False((await fixture.Operation.InspectAsync(TestData.Context,fixture.Parameters,default)).CanExecute);
  Assert.Equal(0,fixture.Backend.Writes);
 }
 [Fact] public async Task Recorded_absence_is_restored_and_independently_verified()
 {
  var fixture=new Fixture();var action=await fixture.Plan();var result=await fixture.Operation.ExecuteAsync(TestData.Context,action,default);
  Assert.Equal(ActionOutcome.AppliedVerified,result.Outcome);Assert.Equal(new RegistryDwordState(false,null),fixture.Backend.State);
 }
 [Fact] public async Task Source_target_changed_after_review_blocks_write()
 {
  var fixture=new Fixture();var action=await fixture.Plan();fixture.Source.Record=fixture.Source.Record with {Before=fixture.Snapshot(new(true,1))};
  Assert.Equal(ActionOutcome.Failed,(await fixture.Operation.ExecuteAsync(TestData.Context,action,default)).Outcome);Assert.Equal(0,fixture.Backend.Writes);
 }
 [Theory][InlineData(false)][InlineData(true)] public async Task Machine_restore_needs_independent_protected_authority(bool protectedFlag)
 {
  var fixture=new Fixture();fixture.Source.Record=fixture.Source.Record with {Setting=SettingId.LongPaths,Scope=OperationScope.Machine,Privilege=PrivilegeRequirement.Administrator,Actor=TestData.Actor with {IsElevated=true},ProtectedJournal=protectedFlag,Before=fixture.Snapshot(new(false,null),SettingId.LongPaths),VerifiedAfter=fixture.Snapshot(new(true,1),SettingId.LongPaths)};
  fixture.Backend.State=new(true,1);fixture.Backend.Actor=TestData.Actor with {IsElevated=true};
  if(!protectedFlag)Assert.False((await fixture.Operation.InspectAsync(TestData.Context,fixture.Parameters,default)).CanExecute);
  else {var action=await fixture.Plan();Assert.Equal(ActionOutcome.Failed,(await fixture.Operation.ExecuteAsync(TestData.Context,action,default)).Outcome);}
  Assert.Equal(0,fixture.Backend.Writes);
 }
 [Fact] public async Task Protected_authority_restores_original_machine_value()
 {
  var fixture=new Fixture();fixture.Source.Authority=true;fixture.Source.Record=fixture.Source.Record with {Setting=SettingId.LongPaths,Scope=OperationScope.Machine,Privilege=PrivilegeRequirement.Administrator,Actor=TestData.Actor with {IsElevated=true},ProtectedJournal=true,Before=fixture.Snapshot(new(false,null),SettingId.LongPaths),VerifiedAfter=fixture.Snapshot(new(true,1),SettingId.LongPaths)};
  fixture.Backend.State=new(true,1);fixture.Backend.Actor=TestData.Actor with {IsElevated=true};
  Assert.Equal(ActionOutcome.AppliedVerified,(await fixture.Operation.ExecuteAsync(TestData.Context,await fixture.Plan(),default)).Outcome);
 }
 [Fact] public async Task Unverified_original_result_is_rejected()
 {
  var fixture=new Fixture();fixture.Source.Record=fixture.Source.Record with {Outcome=ActionOutcome.AppliedUnverified};
  Assert.False((await fixture.Operation.InspectAsync(TestData.Context,fixture.Parameters,default)).CanExecute);
 }
 [Fact] public async Task Readback_failure_never_claims_verified_restoration()
 {
  var fixture=new Fixture();fixture.Backend.Effective=false;
  Assert.Equal(ActionOutcome.AppliedUnverified,(await fixture.Operation.ExecuteAsync(TestData.Context,await fixture.Plan(),default)).Outcome);
 }
 [Fact] public void Arbitrary_targets_and_empty_source_identifiers_are_rejected()
 {
  var fixture=new Fixture();Assert.Throws<PlanValidationException>(()=>fixture.Operation.ValidateParameters(StrictJson.Value(new {sourcePlanId=Guid.NewGuid(),sourceActionId=Guid.NewGuid(),sourceRunId=Guid.NewGuid(),confirmManagedPolicyReview=true,value=0})));
  Assert.Throws<PlanValidationException>(()=>fixture.Operation.ValidateParameters(StrictJson.Value(new RecordedSettingRestorationParameters(Guid.Empty,Guid.NewGuid(),Guid.NewGuid(),true))));
 }
 [Fact] public async Task Ac_restoration_preserves_recorded_dc_and_scheme_identity()
 {
  var fixture=new Fixture();var scheme=Guid.NewGuid();
  fixture.Source.Authority=true;fixture.Source.Record=fixture.Source.Record with {Setting=SettingId.NoAcSleep,Scope=OperationScope.Machine,Privilege=PrivilegeRequirement.Administrator,ProtectedJournal=true,Actor=TestData.Actor with {IsElevated=true},Before=new(SettingId.NoAcSleep,StrictJson.Value(new AcSleepState(scheme,900,600)),"fixture power",DateTimeOffset.UtcNow),VerifiedAfter=new(SettingId.NoAcSleep,StrictJson.Value(new AcSleepState(scheme,0,600)),"fixture power",DateTimeOffset.UtcNow)};
  fixture.Backend.CurrentValue=fixture.Source.Record.VerifiedAfter.Value;fixture.Backend.Actor=TestData.Actor with {IsElevated=true};
  var result=await fixture.Operation.ExecuteAsync(TestData.Context,await fixture.Plan(),default);
  Assert.Equal(ActionOutcome.AppliedVerified,result.Outcome);
  Assert.Equal(new AcSleepState(scheme,900,600),StrictJson.Read<AcSleepState>(StrictJson.Bytes(fixture.Backend.CurrentValue!.Value)));
 }
 [Fact] public async Task User_privilege_time_zone_restores_recorded_zone_and_daylight_flag()
 {
  var fixture=new Fixture();fixture.Source.Record=fixture.Source.Record with {Setting=SettingId.UkTimeZone,Scope=OperationScope.Machine,Before=new(SettingId.UkTimeZone,StrictJson.Value(new TimeZoneState("Pacific Standard Time",true)),"fixture zone",DateTimeOffset.UtcNow),VerifiedAfter=new(SettingId.UkTimeZone,StrictJson.Value(new TimeZoneState("GMT Standard Time",true)),"fixture zone",DateTimeOffset.UtcNow)};
  fixture.Backend.CurrentValue=fixture.Source.Record.VerifiedAfter.Value;fixture.Backend.Privilege=PrivilegeRequirement.CurrentUser;
  Assert.Equal(ActionOutcome.AppliedVerified,(await fixture.Operation.ExecuteAsync(TestData.Context,await fixture.Plan(),default)).Outcome);
  Assert.Equal(new TimeZoneState("Pacific Standard Time",true),StrictJson.Read<TimeZoneState>(StrictJson.Bytes(fixture.Backend.CurrentValue!.Value)));
 }
 [Fact] public async Task User_privilege_original_time_zone_cannot_be_promoted_to_admin_restoration()
 {
  var fixture=new Fixture();fixture.Source.Record=fixture.Source.Record with {Setting=SettingId.UkTimeZone,Scope=OperationScope.Machine,Before=new(SettingId.UkTimeZone,StrictJson.Value(new TimeZoneState("Pacific Standard Time",false)),"fixture zone",DateTimeOffset.UtcNow),VerifiedAfter=new(SettingId.UkTimeZone,StrictJson.Value(new TimeZoneState("GMT Standard Time",false)),"fixture zone",DateTimeOffset.UtcNow)};
  fixture.Backend.CurrentValue=fixture.Source.Record.VerifiedAfter.Value;
  Assert.False((await fixture.Operation.InspectAsync(TestData.Context,fixture.Parameters,default)).CanExecute);Assert.Equal(0,fixture.Backend.Writes);
 }
 [Fact] public async Task Typed_registry_evidence_with_extra_fields_is_rejected()
 {
  var fixture=new Fixture();fixture.Source.Record=fixture.Source.Record with {Before=fixture.Source.Record.Before with {Value=StrictJson.Value(new{exists=false,value=(int?)null,path="arbitrary"})}};
  Assert.False((await fixture.Operation.InspectAsync(TestData.Context,fixture.Parameters,default)).CanExecute);
 }
 [Fact] public void Missing_expected_setting_cannot_claim_a_queue_target()
 {
  var fixture=new Fixture();
  Assert.Throws<PlanValidationException>(()=>fixture.Operation.ValidateParameters(StrictJson.Value(new RecordedSettingRestorationParameters(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),true))));
 }
 [Fact] public async Task Claimed_setting_must_match_the_independently_loaded_source()
 {
  var fixture=new Fixture();
  var parameters=StrictJson.Value(new RecordedSettingRestorationParameters(fixture.Source.Record.SourcePlanId,fixture.Source.Record.SourceActionId,fixture.Source.Record.SourceRunId,true,SettingId.LongPaths));
  var action=await fixture.Plan();
  Assert.False((await fixture.Operation.InspectAsync(TestData.Context,parameters,default)).CanExecute);
  var result=await fixture.Operation.ExecuteAsync(TestData.Context,action with {Request=action.Request with {Parameters=parameters}},default);
  Assert.Equal(ActionOutcome.Failed,result.Outcome);Assert.Equal(0,fixture.Backend.Writes);
 }
 sealed class Fixture
 {
  public readonly Backend Backend=new();public readonly Source Source=new();public readonly RecordedSettingRestorationOperation Operation;
  public System.Text.Json.JsonElement Parameters=>StrictJson.Value(new RecordedSettingRestorationParameters(Source.Record.SourcePlanId,Source.Record.SourceActionId,Source.Record.SourceRunId,true,Source.Record.Setting));
  public SettingSnapshot Snapshot(RegistryDwordState value,SettingId id=SettingId.ShowFileExtensions)=>new(id,StrictJson.Value(value),"fixture registry snapshot",DateTimeOffset.UtcNow);
  public Fixture(){Source.Record=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),TestData.Context,CatalogueLoader.LoadShipped().Identity,SettingId.ShowFileExtensions,OperationScope.CurrentUser,PrivilegeRequirement.CurrentUser,Snapshot(new(false,null)),Snapshot(new(true,0)),TestData.Actor,ActionOutcome.AppliedVerified,false);Operation=new(Source,Backend,CatalogueLoader.LoadShipped().Identity);}
  public async Task<PlannedAction> Plan(){var before=await Operation.CaptureBeforeAsync(TestData.Context,Parameters,default);return new(Guid.NewGuid(),new("settings.restore-recorded",Operation.Id,Parameters,new("setup","windows")),await Operation.InspectAsync(TestData.Context,Parameters,default),before,ObservationFingerprint.Of(before));}
 }
 sealed class Source:IRecordedSettingSource
 {
  public RecordedSettingSource Record=null!;public bool Authority;public bool IsProtectedAuthority=>Authority;
  public Task<RecordedSettingSource> LoadAsync(RecordedSettingRestorationParameters parameters,CancellationToken ct)=>Task.FromResult(Record);
 }
 sealed class Backend:ISettingRestorationBackend
 {
  public System.Text.Json.JsonElement? CurrentValue;public PrivilegeRequirement? Privilege;
  public Task<PrivilegeRequirement> GetPrivilegeAsync(SettingId setting,DeviceContext context,CancellationToken ct)=>Task.FromResult(Privilege??(setting==SettingId.ShowFileExtensions?PrivilegeRequirement.CurrentUser:PrivilegeRequirement.Administrator));
  public RegistryDwordState State=new(true,0);public ExecutionActor Actor=TestData.Actor;public int Writes;public bool Effective=true;
  public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>Task.FromResult(Actor);
  public Task<SettingPolicy> InspectPolicyAsync(SettingId setting,CancellationToken ct)=>Task.FromResult(new SettingPolicy(true,false,"fixture"));
  public Task<SettingSnapshot> ReadAsync(SettingId setting,DeviceContext context,CancellationToken ct)=>Task.FromResult(new SettingSnapshot(setting,CurrentValue??StrictJson.Value(State),"fixture registry snapshot",DateTimeOffset.UtcNow));
  public Task ApplyAsync(SettingId setting,DeviceContext context,SettingSnapshot expectedBefore,CancellationToken ct)=>throw new NotSupportedException();
  public Task RestoreAsync(SettingId setting,DeviceContext context,SettingSnapshot expectedCurrent,SettingSnapshot recordedTarget,CancellationToken ct){Writes++;if(Effective){if(CurrentValue.HasValue)CurrentValue=recordedTarget.Value.Clone();else State=StrictJson.Read<RegistryDwordState>(StrictJson.Bytes(recordedTarget.Value));}return Task.CompletedTask;}
 }
}
