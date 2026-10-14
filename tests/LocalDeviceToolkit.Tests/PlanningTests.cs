using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Assessment;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class PlanningTests
{
 [Fact] public async Task Expiry_is_exact_and_never_renewed()
 {
  using var f=new PlanningFixture();var plan=await f.CreatePlan();
  var before=plan.Actions.ToDictionary(a=>a.Id,a=>a.BeforeDigest);
  Assert.True(f.Plans.Validate(plan,f.Current,f.Catalogue.Identity,before,plan.CreatedAt.AddMinutes(9)).IsValid);
  Assert.False(f.Plans.Validate(plan,f.Current,f.Catalogue.Identity,before,plan.CreatedAt.AddMinutes(10)).IsValid);
 }
 [Fact] public async Task Policy_inspection_failure_blocks_change()
 {
  using var f=new PlanningFixture();f.Operation.PolicyUnavailable=true;
  await Assert.ThrowsAsync<PlanValidationException>(()=>f.CreatePlan());
  Assert.Equal("false",File.ReadAllText(f.SettingPath));
 }
 [Fact] public async Task Unsaved_intent_never_dispatches()
 {
  using var f=new PlanningFixture();var plan=await f.CreatePlan();
  var run=f.Coordinator(new FailingIntentStore(f.Store));
  try{await run.ExecuteAsync(plan,default);}catch(IOException){}
  Assert.Equal("false",File.ReadAllText(f.SettingPath));
 }
 [Fact] public async Task Changed_original_user_refuses_execution()
 {
  using var f=new PlanningFixture();var plan=await f.CreatePlan();f.Current=f.Current with { OriginalUserSid="S-1-5-21-100-200-300-1002" };
  try{await f.Coordinator(f.Store).ExecuteAsync(plan,default);}catch(PlanValidationException){}
  Assert.Equal("false",File.ReadAllText(f.SettingPath));
 }
 [Fact] public void Both_workspaces_use_one_queue_entry()
 {
  using var f=new PlanningFixture();var queue=new DeploymentQueue(f.Current,f.Registry);
  Assert.Equal(QueueAddResult.Added,queue.Add(f.Request(true)));
  Assert.Equal(QueueAddResult.AlreadyQueued,queue.Add(f.Request(true,"diagnostics")));
  Assert.Single(queue.Requests);Assert.Equal(1,queue.Revision);
 }
 [Fact] public void Conflicting_parameters_never_silently_replace_the_queue()
 {
  using var f=new PlanningFixture();var queue=new DeploymentQueue(f.Current,f.Registry);
  queue.Add(f.Request(true));Assert.Equal(QueueAddResult.ConflictingParameters,queue.Add(f.Request(false)));
  Assert.True(queue.Requests[0].Parameters.GetProperty("enabled").GetBoolean());Assert.Single(queue.Requests);
 }
 [Fact] public void Rechecking_unchanged_values_does_not_invent_drift_from_timestamps()
 {
  var first=new[]{new ProbeObservation("CFG-WIN-012","extensions.visible",ObservationState.Known,StrictJson.Value(false),"fixture",DateTimeOffset.UtcNow,"")};
  var second=new[]{first[0] with { CapturedAt=first[0].CapturedAt.AddSeconds(5) }};
  Assert.Equal(ObservationFingerprint.Of(first),ObservationFingerprint.Of(second));
 }
 [Fact] public void Unavailable_required_check_is_not_compliance()
 {
  var catalogue=CatalogueLoader.LoadShipped();var control=catalogue.Controls.Single(c=>c.Id=="CFG-WIN-012") with { Coverage=CoverageMode.Partial,CheckIds=["extensions.visible"] };
  var capture=TestData.Capture() with { Observations=[new("CFG-WIN-012","extensions.visible",ObservationState.Unavailable,null,"fixture",DateTimeOffset.UtcNow,"Unable to read.")] };
  var finding=Assert.Single(AssessmentService.Assess(catalogue with { Controls=[control] },capture));Assert.Equal(FindingStatus.Unknown,finding.Status);
 }
 [Fact] public async Task A_dispatcher_cannot_claim_verified_success_without_readback()
 {
  using var f=new PlanningFixture();var plan=await f.CreatePlan();
  var coordinator=new RunCoordinator(f.Plans,f.Registry,f,f.Catalogue,f.Store,new UnprovedDispatcher(),f.Clock);
  var run=await coordinator.ExecuteAsync(plan,default);
  Assert.Equal(ActionOutcome.OutcomeUnknown,Assert.Single(run.Results).Outcome);
 }
 [Theory]
 [InlineData(true,FindingStatus.MeetsStandard)]
 [InlineData(false,FindingStatus.DoesNotMeetStandard)]
 public void Known_values_are_compared_with_the_expected_setting(bool visible,FindingStatus expected)
 {
  var catalogue=CatalogueLoader.LoadShipped();var control=catalogue.Controls.Single(c=>c.Id=="CFG-WIN-012") with { Coverage=CoverageMode.Automatic,CheckIds=["extensions.visible"] };
  var capture=TestData.Capture() with { Observations=[new("CFG-WIN-012","extensions.visible",ObservationState.Known,StrictJson.Value(visible),"fixture",DateTimeOffset.UtcNow,"")] };
  Assert.Equal(expected,Assert.Single(AssessmentService.Assess(catalogue with { Controls=[control] },capture)).Status);
 }
 [Fact] public async Task Completed_run_keeps_before_and_after_evidence()
 {
  using var f=new PlanningFixture();var plan=await f.CreatePlan();var run=await f.Coordinator(f.Store).ExecuteAsync(plan,default);
  Assert.Equal("true",File.ReadAllText(f.SettingPath));Assert.Equal(ActionOutcome.AppliedVerified,Assert.Single(run.Results).Outcome);
  var history=await f.Store.ReadHistoryAsync(null,default);
  Assert.Contains(history.Entries,e=>e.Kind=="intent");Assert.Contains(history.Entries,e=>e.Kind=="run");
  Assert.False(plan.Actions[0].Before[0].Value!.Value.GetBoolean());
 }
}
public sealed class TestClock:TimeProvider { public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now; }
public sealed class PlanningFixture:IDisposable,IExecutionContextProvider
{
 readonly string root=Path.Combine(Path.GetTempPath(),"LocalToolkit-plan-"+Guid.NewGuid().ToString("N"));
 public string SettingPath=>Path.Combine(root,"setting.txt");
 public DeviceContext Current=TestData.Context;
 public TestClock Clock {get;}=new();
 public LocalCatalogue Catalogue {get;}=CatalogueLoader.LoadShipped();
 public FileSettingOperation Operation {get;}
 public OperationRegistry Registry {get;}
 public PlanService Plans {get;}
 public EvidenceStore Store {get;}
 public PlanningFixture()
 {
  Directory.CreateDirectory(root);File.WriteAllText(SettingPath,"false");Operation=new(SettingPath,Clock);
  var task=new TaskDefinition("windows.file-extensions","Fixture setting",["CFG-WIN-012"],["extensions.visible"],Operation.Id);
  var workflows=new WorkflowCatalogue([task],[new("setup","Setup",[new("windows","Windows",[task.Id])]),new("diagnostics","Diagnostics",[new("configuration","Configuration",[task.Id])])]);
  Registry=new(workflows,[Operation]);Plans=new(Registry,Clock);Store=new(Path.Combine(root,"evidence"),ToolkitEvidenceSchemas.Create());
 }
 public ActionRequest Request(bool enabled,string workflow="setup")=>new("windows.file-extensions",Operation.Id,StrictJson.Value(new{enabled}),new(workflow,workflow=="setup"?"windows":"configuration"));
 public Task<ReviewedPlan> CreatePlan()=>Plans.CreateAsync(Current,Catalogue,[Request(true)],default);
 public Task<DeviceContext> GetCurrentAsync(CancellationToken ct)=>Task.FromResult(Current);
 public RunCoordinator Coordinator(IEvidenceStore store)=>new(Plans,Registry,this,Catalogue,store,new LocalActionDispatcher(Registry,this,Clock),Clock);
 public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
public sealed class FileSettingOperation(string path,TestClock clock):ILocalOperation
{
 public string Id=>"test.setting";
 public bool PolicyUnavailable {get;set;}
 public void ValidateParameters(JsonElement parameters)
 {
  if(parameters.ValueKind!=JsonValueKind.Object||parameters.EnumerateObject().Count()!=1||!parameters.TryGetProperty("enabled",out var value)||(value.ValueKind!=JsonValueKind.True&&value.ValueKind!=JsonValueKind.False))throw new PlanValidationException("Invalid fixture parameters.");
 }
 public string GetTargetKey(DeviceContext context,JsonElement parameters)=>"fixture.setting";
 public Task<ActionReadiness> InspectAsync(DeviceContext context,JsonElement parameters,CancellationToken ct)=>Task.FromResult(new ActionReadiness(!PolicyUnavailable,OperationScope.CurrentUser,PrivilegeRequirement.CurrentUser,PolicyUnavailable?"Policy inspection unavailable.":""));
 public Task<IReadOnlyList<ProbeObservation>> CaptureBeforeAsync(DeviceContext context,JsonElement parameters,CancellationToken ct)=>Task.FromResult<IReadOnlyList<ProbeObservation>>([new("CFG-WIN-012","extensions.visible",ObservationState.Known,StrictJson.Value(File.ReadAllText(path)=="true"),"fixture",clock.Now,"")]);
 public async Task<ActionResult> ExecuteAsync(DeviceContext context,PlannedAction action,CancellationToken ct)
 {
  ct.ThrowIfCancellationRequested();File.WriteAllText(path,action.Request.Parameters.GetProperty("enabled").GetBoolean()?"true":"false");
  return new(action.Id,ActionOutcome.AppliedVerified,TestData.Actor,"Fixture verified.",await CaptureBeforeAsync(context,action.Request.Parameters,ct));
 }
}
public sealed class FailingIntentStore(IEvidenceStore inner):IEvidenceStore
{
 public Task SaveNewAsync(EvidenceRecord record,CancellationToken ct)=>record.Kind=="intent"?Task.FromException(new IOException("Synthetic disk-full failure.")):inner.SaveNewAsync(record,ct);
 public Task<EvidenceRecord> LoadAsync(Guid id,CancellationToken ct)=>inner.LoadAsync(id,ct);
 public Task<HistoryPage> ReadHistoryAsync(string? cursor,CancellationToken ct)=>inner.ReadHistoryAsync(cursor,ct);
}

public sealed class UnprovedDispatcher:IActionDispatcher
{
 public Task<ActionResult> DispatchAsync(ReviewedPlan plan,PlannedAction action,CancellationToken ct)=>Task.FromResult(new ActionResult(action.Id,ActionOutcome.AppliedVerified,null,"Claimed success without evidence.",[]));
}
