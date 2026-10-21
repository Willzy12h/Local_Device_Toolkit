using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Elevation;
using LocalDeviceToolkit.Engine.Planning;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class ProtectedActionTests
{
 [Fact] public async Task User_scope_cannot_be_redirected_into_an_admin_worker()
 {
  using var f=new PlanningFixture();var plan=await f.CreatePlan();var service=new ProtectedActionService(f.Registry,f.Plans,f.Catalogue,f.Store,f.Clock);
  await Assert.ThrowsAsync<PlanValidationException>(()=>service.ExecuteAsync(plan,plan.Actions[0].Id,f.Current,Parent,Admin,()=>true,default));Assert.Equal("false",File.ReadAllText(f.SettingPath));
 }
 [Theory]
 [InlineData(false)]
 [InlineData(true)]
 public async Task Protected_intent_is_durable_before_the_only_write_and_cannot_be_replayed(bool loseResult)
 {
  using var f=new PlanningFixture();var op=new MachineOperation(f.Operation,f.Store) {ThrowAfterWrite=loseResult};var flow=new LocalDeviceToolkit.Engine.Catalogue.WorkflowCatalogue([new("windows.file-extensions","fixture",["CFG-WIN-012"],["extensions.visible"],op.Id)],[new("setup","Setup",[new("windows","Windows",["windows.file-extensions"])])]);
  var registry=new OperationRegistry(flow,[op]);var plans=new PlanService(registry,f.Clock);var plan=await plans.CreateAsync(f.Current,f.Catalogue,[f.Request(true)],default);var service=new ProtectedActionService(registry,plans,f.Catalogue,f.Store,f.Clock);
  var result=await service.ExecuteAsync(plan,plan.Actions[0].Id,f.Current,Parent,Admin,()=>true,default);Assert.Equal(loseResult?ActionOutcome.OutcomeUnknown:ActionOutcome.AppliedVerified,result.Outcome);Assert.Equal(1,op.Writes);
  File.WriteAllText(f.SettingPath,"false");
  await Assert.ThrowsAnyAsync<Exception>(()=>service.ExecuteAsync(plan,plan.Actions[0].Id,f.Current,Parent,Admin,()=>true,default));Assert.Equal(1,op.Writes);
 }
 [Fact] public async Task Lost_parent_prevents_the_protected_write()
 {
  using var f=new PlanningFixture();var plan=await f.CreatePlan();var service=new ProtectedActionService(f.Registry,f.Plans,f.Catalogue,f.Store,f.Clock);
  await Assert.ThrowsAsync<PlanValidationException>(()=>service.ExecuteAsync(plan,plan.Actions[0].Id,f.Current,Parent,Admin,()=>false,default));Assert.Equal("false",File.ReadAllText(f.SettingPath));
 }
 static ExecutionActor Parent=>TestData.Actor with {ProcessId=Environment.ProcessId,ProcessStartedAt=ReviewLifetime.Start().ParentProcessStartedAt};
 static readonly ExecutionActor Admin=TestData.Actor with {Sid="S-1-5-21-100-200-300-500",UserName="TEST\\admin",IsElevated=true,ProcessId=99};
 sealed class MachineOperation(FileSettingOperation inner,IEvidenceStore store):ILocalOperation
 {
  public int Writes;public bool ThrowAfterWrite;public string Id=>inner.Id;
  public void ValidateParameters(System.Text.Json.JsonElement p)=>inner.ValidateParameters(p);
  public string GetTargetKey(DeviceContext c,System.Text.Json.JsonElement p)=>inner.GetTargetKey(c,p);
  public Task<ActionReadiness> InspectAsync(DeviceContext c,System.Text.Json.JsonElement p,CancellationToken ct)=>Task.FromResult(new ActionReadiness(true,OperationScope.Machine,PrivilegeRequirement.Administrator,"Fixture machine scope."));
  public Task<IReadOnlyList<ProbeObservation>> CaptureBeforeAsync(DeviceContext c,System.Text.Json.JsonElement p,CancellationToken ct)=>inner.CaptureBeforeAsync(c,p,ct);
  public async Task<ActionResult> ExecuteAsync(DeviceContext c,PlannedAction action,CancellationToken ct)
  {
   Assert.Contains((await store.ReadHistoryAsync(null,ct)).Entries,e=>e.Kind=="intent");Writes++;var result=await inner.ExecuteAsync(c,action,ct);if(ThrowAfterWrite)throw new PlanValidationException("Fixture lost its completion after the write.");return result with {Actor=Admin};
  }
 }
}
