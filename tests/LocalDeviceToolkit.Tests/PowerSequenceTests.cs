using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Settings;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Elevation;
using LocalDeviceToolkit.Engine.Evidence;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class PowerSequenceTests
{
 [Fact] public async Task Ordered_projection_preserves_measured_before_and_requires_protected_prefix()
 {
  var state=new PowerTimeoutState(Guid.NewGuid(),60,120,180,240);var first=Action(PowerSlot.SleepAc,0,state);var second=Action(PowerSlot.DisplayDc,10,state);
  var plan=new ReviewedPlan(Guid.NewGuid(),TestData.Context,LocalDeviceToolkit.Engine.Catalogue.CatalogueLoader.LoadShipped().Identity,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddMinutes(10),[first,second],"");
  var projected=PowerPlanSequence.AtDispatch(plan,second);Assert.Equal(state.With(PowerSlot.SleepAc,0),StrictJson.Read<PowerTimeoutState>(StrictJson.Bytes(projected.Before[0].Value!.Value)));Assert.Equal(state,StrictJson.Read<PowerTimeoutState>(StrictJson.Bytes(second.Before[0].Value!.Value)));
  var path=Path.Combine(Path.GetTempPath(),"prefix-"+Guid.NewGuid().ToString("N"));try{var store=new EvidenceStore(path,ToolkitEvidenceSchemas.Create(),TestData.Context);await Assert.ThrowsAnyAsync<Exception>(()=>PowerPlanSequence.VerifyProtectedPrefixAsync(plan,second,store,default));}finally{if(Directory.Exists(path))Directory.Delete(path,true);}
 }
 [Fact] public async Task Protected_sequence_records_the_actual_dispatch_state_without_rewriting_reviewed_before()
 {
  var path=Path.Combine(Path.GetTempPath(),"protected-power-"+Guid.NewGuid().ToString("N"));try
  {
   var backend=new PowerBackend();var flows=LocalDeviceToolkit.Engine.Catalogue.WorkflowCatalogue.LoadShipped();var catalogue=LocalDeviceToolkit.Engine.Catalogue.CatalogueLoader.LoadShipped();var registry=new OperationRegistry(flows,Enum.GetValues<PowerSlot>().Select(s=>new PowerTimeoutOperation(s,backend)));var plans=new PlanService(registry,TimeProvider.System);var store=new EvidenceStore(path,ToolkitEvidenceSchemas.Create(),TestData.Context);
   ActionRequest Request(PowerSlot slot,int minutes){var id=PowerTimeoutOperation.OperationId(slot);return new(id,id,StrictJson.Value(new PowerTimeoutInput(minutes,true)),new("setup","local"));}
   var plan=await plans.CreateAsync(TestData.Context,catalogue,[Request(PowerSlot.SleepAc,0),Request(PowerSlot.DisplayDc,10)],default);var parent=TestData.Actor with{ProcessId=Environment.ProcessId,ProcessStartedAt=plan.Lifetime!.ParentProcessStartedAt};var service=new ProtectedActionService(registry,plans,catalogue,store,TimeProvider.System);
   foreach(var action in plan.Actions)Assert.Equal(ActionOutcome.AppliedVerified,(await service.ExecuteAsync(plan,action.Id,TestData.Context,parent,backend.Actor,()=>true,default)).Outcome);
   var record=Assert.Single((await store.ReadHistoryAsync(null,default)).Entries,e=>e.Kind=="intent"&&StrictJson.Read<LocalDeviceToolkit.Engine.Execution.IntentRecord>(StrictJson.Bytes(e.Data)).Action.Id==plan.Actions[1].Id);var intent=StrictJson.Read<LocalDeviceToolkit.Engine.Execution.IntentRecord>(StrictJson.Bytes(record.Data));
   Assert.Equal(0u,StrictJson.Read<PowerTimeoutState>(StrictJson.Bytes(intent.BeforeAtDispatch[0].Value!.Value)).SleepAc);Assert.Equal(60u,StrictJson.Read<PowerTimeoutState>(StrictJson.Bytes(plan.Actions[1].Before[0].Value!.Value)).SleepAc);
  }finally{if(Directory.Exists(path))Directory.Delete(path,true);}
 }
 sealed class PowerBackend:IPowerTimeoutBackend
 {
  public readonly ExecutionActor Actor=TestData.Actor with{IsElevated=true};PowerTimeoutState state=new(Guid.NewGuid(),60,120,180,240);
  public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>Task.FromResult(Actor);
  public Task<SettingPolicy> InspectPolicyAsync(CancellationToken ct)=>Task.FromResult(new SettingPolicy(true,false,"Ready"));
  public Task<PowerTimeoutState> ReadAsync(DeviceContext context,CancellationToken ct)=>Task.FromResult(state);
  public Task WriteAsync(DeviceContext context,PowerSlot slot,uint seconds,PowerTimeoutState expected,CancellationToken ct){Assert.Equal(expected,state);state=state.With(slot,seconds);return Task.CompletedTask;}
 }
 static PlannedAction Action(PowerSlot slot,int minutes,PowerTimeoutState state)
 {
  var observation=new ProbeObservation("CFG-WIN-020","power.timeout-state",ObservationState.Known,StrictJson.Value(state),"Fixture",DateTimeOffset.UtcNow,"Exact");var id=PowerTimeoutOperation.OperationId(slot);
  return new(Guid.NewGuid(),new(id,id,StrictJson.Value(new PowerTimeoutInput(minutes,true)),new("setup","local")),new(true,OperationScope.Machine,PrivilegeRequirement.Administrator,"Ready"),[observation],ObservationFingerprint.Of([observation]));
 }
}
