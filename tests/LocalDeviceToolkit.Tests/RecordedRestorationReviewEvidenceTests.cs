using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Settings;
using Xunit;

namespace LocalDeviceToolkit.Tests;

public sealed class RecordedRestorationReviewEvidenceTests
{
 [Theory]
 [InlineData(SettingId.ShowFileExtensions)]
 [InlineData(SettingId.NoAcSleep)]
 [InlineData(SettingId.UkTimeZone)]
 public async Task Actual_capture_exposes_typed_current_and_independently_recorded_target(SettingId setting)
 {
  var f = new Fixture(setting);
  var observation = Assert.Single(await f.Operation.CaptureBeforeAsync(TestData.Context, f.Parameters, default));
  var state = observation.Value!.Value;
  Assert.True(state.TryGetProperty("target", out var target), "Review must expose the independently resolved restoration target.");
  Assert.Equal(CanonicalJson.Digest(f.Source.Record.Before.Value), CanonicalJson.Digest(target));
  Assert.Equal(CanonicalJson.Digest(f.Backend.Current.Value), CanonicalJson.Digest(state.GetProperty("current")));
  Assert.Equal(StrictJson.Value(setting).GetRawText(), state.GetProperty("setting").GetRawText());
  Assert.Contains(CanonicalJson.Digest(f.Source.Record), observation.Source);
  Assert.Contains(f.Backend.Current.Source, observation.Source);
  Assert.Equal(0, f.Backend.Writes);
  if (setting == SettingId.ShowFileExtensions)
   Assert.Equal(new RegistryDwordState(false, null), StrictJson.Read<RegistryDwordState>(StrictJson.Bytes(target)));
 }

 [Fact] public async Task Verified_after_evidence_exposes_actual_restored_value_and_same_bound_target()
 {
  var f = new Fixture(SettingId.ShowFileExtensions);
  var before = await f.Operation.CaptureBeforeAsync(TestData.Context, f.Parameters, default);
  var action = new PlannedAction(Guid.NewGuid(), new("restore", f.Operation.Id, f.Parameters, new("setup", "windows")),
   await f.Operation.InspectAsync(TestData.Context, f.Parameters, default), before, ObservationFingerprint.Of(before));
  var result = await f.Operation.ExecuteAsync(TestData.Context, action, default);
  Assert.Equal(ActionOutcome.AppliedVerified, result.Outcome);
  Assert.Equal(1, f.Backend.Writes);
  var after = Assert.Single(result.After).Value!.Value;
  Assert.True(after.TryGetProperty("target", out var target));
  Assert.Equal(CanonicalJson.Digest(target), CanonicalJson.Digest(after.GetProperty("current")));
  Assert.Equal(CanonicalJson.Digest(f.Source.Record.Before.Value), CanonicalJson.Digest(target));
 }

 [Fact] public void Review_wrapper_does_not_open_a_new_target_or_path_parameter_channel()
 {
  var f = new Fixture(SettingId.ShowFileExtensions); var p = StrictJson.Read<RecordedSettingRestorationParameters>(StrictJson.Bytes(f.Parameters));
  Assert.Throws<PlanValidationException>(() => f.Operation.ValidateParameters(StrictJson.Value(new
  {
   p.SourcePlanId, p.SourceActionId, p.SourceRunId, p.ConfirmManagedPolicyReview, p.ExpectedSetting,
   target = new RegistryDwordState(true, 1), path = "arbitrary registry path"
  })));
 }

 sealed class Fixture
 {
  public readonly Source Source;
  public readonly Backend Backend;
  public readonly RecordedSettingRestorationOperation Operation;
  public JsonElement Parameters => StrictJson.Value(new RecordedSettingRestorationParameters(Source.Record.SourcePlanId, Source.Record.SourceActionId, Source.Record.SourceRunId, true, Source.Record.Setting));
  public Fixture(SettingId setting)
  {
   var scheme = Guid.NewGuid();
   var before = setting switch
   {
    SettingId.NoAcSleep => StrictJson.Value(new AcSleepState(scheme, 900, 600)),
    SettingId.UkTimeZone => StrictJson.Value(new TimeZoneState("Pacific Standard Time", true)),
    _ => StrictJson.Value(new RegistryDwordState(false, null))
   };
   var after = setting switch
   {
    SettingId.NoAcSleep => StrictJson.Value(new AcSleepState(scheme, 0, 600)),
    SettingId.UkTimeZone => StrictJson.Value(new TimeZoneState("GMT Standard Time", true)),
    _ => StrictJson.Value(new RegistryDwordState(true, 0))
   };
   var elevated = setting == SettingId.NoAcSleep;
   var snapshot = new SettingSnapshot(setting, before, "registered target source", DateTimeOffset.UtcNow);
   var current = new SettingSnapshot(setting, after, "registered current source", DateTimeOffset.UtcNow);
   var record = new RecordedSettingSource(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TestData.Context,
    CatalogueLoader.LoadShipped().Identity, setting, setting == SettingId.ShowFileExtensions ? OperationScope.CurrentUser : OperationScope.Machine,
    elevated ? PrivilegeRequirement.Administrator : PrivilegeRequirement.CurrentUser, snapshot, current,
    TestData.Actor with { IsElevated = elevated }, ActionOutcome.AppliedVerified, elevated);
   Source = new(record); Backend = new(current, record.Actor, record.Privilege);
   Operation = new(Source, Backend, record.Catalogue);
  }
 }
 sealed class Source(RecordedSettingSource record) : IRecordedSettingSource
 {
  public RecordedSettingSource Record = record;
  public bool IsProtectedAuthority => true;
  public Task<RecordedSettingSource> LoadAsync(RecordedSettingRestorationParameters parameters, CancellationToken ct) => Task.FromResult(Record);
 }
 sealed class Backend(SettingSnapshot current, ExecutionActor actor, PrivilegeRequirement privilege) : ISettingRestorationBackend
 {
  public SettingSnapshot Current = current;
  public int Writes;
  public Task<PrivilegeRequirement> GetPrivilegeAsync(SettingId setting, DeviceContext context, CancellationToken ct) => Task.FromResult(privilege);
  public Task<ExecutionActor> GetActorAsync(CancellationToken ct) => Task.FromResult(actor);
  public Task<SettingPolicy> InspectPolicyAsync(SettingId setting, CancellationToken ct) => Task.FromResult(new SettingPolicy(true, false, "checked"));
  public Task<SettingSnapshot> ReadAsync(SettingId setting, DeviceContext context, CancellationToken ct) => Task.FromResult(Current);
  public Task ApplyAsync(SettingId setting, DeviceContext context, SettingSnapshot expectedBefore, CancellationToken ct) => throw new NotSupportedException();
  public Task RestoreAsync(SettingId setting, DeviceContext context, SettingSnapshot expectedCurrent, SettingSnapshot recordedTarget, CancellationToken ct)
  { Writes++; Current = recordedTarget; return Task.CompletedTask; }
 }
}
