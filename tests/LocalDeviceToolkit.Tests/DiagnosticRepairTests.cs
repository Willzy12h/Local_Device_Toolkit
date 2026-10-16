using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Diagnostics;
using LocalDeviceToolkit.Engine.Execution;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class DiagnosticRepairTests
{
 [Fact] public void Dns_requires_explicit_whole_cache_and_supported_hypothesis()
 {
  var operation=new DiagnosticRepairOperation(DiagnosticRepairKind.DnsCache,new Backend());
  Assert.Throws<PlanValidationException>(()=>operation.ValidateParameters(StrictJson.Value(new DnsCacheInput("example.test","192.0.2.1","round-robin",true,true))));
  Assert.Throws<PlanValidationException>(()=>operation.ValidateParameters(StrictJson.Value(new DnsCacheInput("example.test","192.0.2.1","configuration-change",false,true))));
 }
 [Fact] public async Task Uncertain_update_never_becomes_verified_or_retried()
 {
  var backend=new Backend();var operation=new DiagnosticRepairOperation(DiagnosticRepairKind.DefenderSignatures,backend);var input=StrictJson.Value(new DefenderSignatureInput(true));var before=await operation.CaptureBeforeAsync(TestData.Context,input,default);
  var action=new PlannedAction(Guid.NewGuid(),new(operation.Id,operation.Id,input,new("diagnostics","local")),await operation.InspectAsync(TestData.Context,input,default),before,LocalDeviceToolkit.Engine.Planning.ObservationFingerprint.Of(before));
  var result=await operation.ExecuteAsync(TestData.Context,action,default);Assert.Equal(ActionOutcome.OutcomeUnknown,result.Outcome);Assert.Equal(1,backend.Calls);Assert.Null(result.Outcomes!.ExpectedStateVerified);Assert.Null(result.Outcomes.OriginalSymptomResolved);
 }
 [Fact] public async Task Changed_stable_network_identity_blocks_dispatch()
 {
  var backend=new Backend();var operation=new DiagnosticRepairOperation(DiagnosticRepairKind.DnsCache,backend);var input=StrictJson.Value(new DnsCacheInput("example.test","192.0.2.1","configuration-change",true,true));
  var before=await operation.CaptureBeforeAsync(TestData.Context,input,default);var action=new PlannedAction(Guid.NewGuid(),new(operation.Id,operation.Id,input,new("diagnostics","local")),await operation.InspectAsync(TestData.Context,input,default),before,LocalDeviceToolkit.Engine.Planning.ObservationFingerprint.Of(before));backend.AuthDigest=new string('b',64);
  Assert.Equal(ActionOutcome.Failed,(await operation.ExecuteAsync(TestData.Context,action,default)).Outcome);Assert.Equal(0,backend.Calls);
 }
 sealed class Backend:IDiagnosticRepairBackend
 {
  public int Calls;public string AuthDigest=new string('a',64);
  public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>Task.FromResult(TestData.Actor with{IsElevated=true});
  public Task<RepairSnapshot> ReadAsync(DiagnosticRepairKind kind,DeviceContext context,System.Text.Json.JsonElement input,CancellationToken ct)=>Task.FromResult(new RepairSnapshot(AuthDigest,"Version 1",true,"Ready",new Dictionary<string,string>()));
  public Task<RepairDispatch> RunAsync(DiagnosticRepairKind kind,DeviceContext context,System.Text.Json.JsonElement input,Guid action,RepairSnapshot expected,CancellationToken ct){Calls++;return Task.FromResult(new RepairDispatch(null,null,"Process outcome is unknown."));}
 }
}
