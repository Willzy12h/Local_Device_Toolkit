using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Elevation;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Settings;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class RestorationSourceTests:IDisposable
{
 readonly string root=Path.Combine(Path.GetTempPath(),"restoration-source-"+Guid.NewGuid().ToString("N"));
 public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
 [Fact] public async Task Presentation_and_protected_source_digests_match_despite_independent_internal_run_ids()
 {
  var f=await Seed();var preview=await new EvidenceRecordedSettingSource(f.User).LoadAsync(f.Parameters,default);
  var trusted=await new EvidenceRecordedSettingSource(f.Protected,true).LoadAsync(f.Parameters,default);
  Assert.Equal(CanonicalJson.Digest(preview),CanonicalJson.Digest(trusted));Assert.True(trusted.ProtectedJournal);
  Assert.False(new EvidenceRecordedSettingSource(f.User).IsProtectedAuthority);
 }
 [Fact] public async Task Presentation_of_machine_source_cannot_execute_even_under_an_elevated_actor()
 {
  var f=await Seed();var backend=new Backend();var operation=new RecordedSettingRestorationOperation(new EvidenceRecordedSettingSource(f.User),backend,f.Plan.Catalogue);
  var action=await Plan(operation,f.Parameters);
  Assert.Equal(ActionOutcome.Failed,(await operation.ExecuteAsync(TestData.Context,action,default)).Outcome);Assert.Equal(0,backend.Writes);
 }
 [Fact] public async Task Forged_user_target_cannot_steer_independent_protected_execution()
 {
  var f=await Seed(forgeUserTarget:true);var backend=new Backend();var preview=new RecordedSettingRestorationOperation(new EvidenceRecordedSettingSource(f.User),backend,f.Plan.Catalogue);
  var action=await Plan(preview,f.Parameters);var worker=new RecordedSettingRestorationOperation(new EvidenceRecordedSettingSource(f.Protected,true),backend,f.Plan.Catalogue);
  var result=await worker.ExecuteAsync(TestData.Context,action,default);
  Assert.Equal(ActionOutcome.Failed,result.Outcome);Assert.Equal(0,backend.Writes);
 }
 [Fact] public async Task Forged_source_run_id_cannot_change_protected_target()
 {
  var f=await Seed();var adapter=new EvidenceRecordedSettingSource(f.Protected,true);var changed=f.Parameters with {SourceRunId=Guid.NewGuid()};
  var source=await adapter.LoadAsync(changed,default);
  Assert.False(source.Before.Value.GetProperty("exists").GetBoolean());
  Assert.Equal(changed.SourceRunId,source.SourceRunId);
  await Assert.ThrowsAsync<FileNotFoundException>(()=>new EvidenceRecordedSettingSource(f.User).LoadAsync(changed,default));
 }
 [Fact] public async Task Missing_protected_result_blocks_restore_despite_successful_user_history()
 {
  var f=await Seed();File.Delete(Path.Combine(root,"protected",ProtectedJournalIds.Result(f.Plan.Id,f.Plan.Actions[0].Id)+".json"));
  var operation=new RecordedSettingRestorationOperation(new EvidenceRecordedSettingSource(f.Protected,true),new Backend(),f.Plan.Catalogue);
  Assert.False((await operation.InspectAsync(TestData.Context,StrictJson.Value(f.Parameters),default)).CanExecute);
 }
 [Fact] public async Task Wrong_evidence_kind_blocks_review_instead_of_escaping_as_an_exception()
 {
  var f=await Seed();var p=f.Parameters with {SourcePlanId=f.Parameters.SourceRunId};
  var operation=new RecordedSettingRestorationOperation(new EvidenceRecordedSettingSource(f.User),new Backend(),f.Plan.Catalogue);
  Assert.False((await operation.InspectAsync(TestData.Context,StrictJson.Value(p),default)).CanExecute);
 }
 async Task<(EvidenceStore User,EvidenceStore Protected,ReviewedPlan Plan,RecordedSettingRestorationParameters Parameters)> Seed(bool forgeUserTarget=false)
 {
  var user=new EvidenceStore(Path.Combine(root,"user"),ToolkitEvidenceSchemas.Create());var trusted=new EvidenceStore(Path.Combine(root,"protected"),ToolkitEvidenceSchemas.Create());
  var now=DateTimeOffset.UtcNow;var control="CFG-WIN-007";var check="setting.LongPaths";
  ProbeObservation Observation(RegistryDwordState value)=>new(control,check,ObservationState.Known,StrictJson.Value(value),"fixture registry",now,"Exact local setting state");
  var before=new[]{Observation(new(false,null))};var action=new PlannedAction(Guid.NewGuid(),new("windows.long-paths","settings.long-paths",StrictJson.Value(new SettingParameters(true)),new("setup","windows")),new(true,OperationScope.Machine,PrivilegeRequirement.Administrator,"fixture"),before,ObservationFingerprint.Of(before));
  var plan=new ReviewedPlan(Guid.NewGuid(),TestData.Context,CatalogueLoader.LoadShipped().Identity,now,now.AddMinutes(10),new[]{action},"");plan=plan with {Digest=CanonicalJson.Digest(plan)};
  var actor=TestData.Actor with {IsElevated=true};var result=new ActionResult(action.Id,ActionOutcome.AppliedVerified,actor,"fixture verified",new[]{Observation(new(true,1))});
  var userRun=new RunRecord(Guid.NewGuid(),plan.Id,plan.Context,now,now.AddSeconds(1),new[]{result},true);var protectedRun=userRun with {Id=Guid.NewGuid()};
  await trusted.SaveNewAsync(EvidenceRecord.Create(plan.Id,"plan",plan.Context,now,StrictJson.Value(plan)),default);
  await trusted.SaveNewAsync(EvidenceRecord.Create(protectedRun.Id,"run",plan.Context,protectedRun.FinishedAt!.Value,StrictJson.Value(protectedRun)),default);
  await trusted.SaveNewAsync(EvidenceRecord.Create(ProtectedJournalIds.Result(plan.Id,action.Id),"result",plan.Context,now,StrictJson.Value(new ResultRecord(protectedRun.Id,plan.Id,result))),default);
  var userPlan=plan;
  if(forgeUserTarget){var forged=new[]{Observation(new(true,42))};var userAction=action with {Before=forged,BeforeDigest=ObservationFingerprint.Of(forged)};userPlan=plan with {Actions=new[]{userAction},Digest=""};userPlan=userPlan with {Digest=CanonicalJson.Digest(userPlan)};}
  await user.SaveNewAsync(EvidenceRecord.Create(userPlan.Id,"plan",plan.Context,now,StrictJson.Value(userPlan)),default);
  await user.SaveNewAsync(EvidenceRecord.Create(userRun.Id,"run",plan.Context,userRun.FinishedAt!.Value,StrictJson.Value(userRun)),default);
  return(user,trusted,plan,new(plan.Id,action.Id,userRun.Id,true,SettingId.LongPaths));
 }
 static async Task<PlannedAction> Plan(RecordedSettingRestorationOperation operation,RecordedSettingRestorationParameters parameters)
 {
  var p=StrictJson.Value(parameters);var before=await operation.CaptureBeforeAsync(TestData.Context,p,default);
  return new(Guid.NewGuid(),new("settings.restore-recorded",operation.Id,p,new("setup","windows")),await operation.InspectAsync(TestData.Context,p,default),before,ObservationFingerprint.Of(before));
 }
 sealed class Backend:ISettingRestorationBackend
 {
  public int Writes;
  public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>Task.FromResult(TestData.Actor with {IsElevated=true});
  public Task<SettingPolicy> InspectPolicyAsync(SettingId setting,CancellationToken ct)=>Task.FromResult(new SettingPolicy(true,false,"fixture"));
  public Task<SettingSnapshot> ReadAsync(SettingId setting,DeviceContext context,CancellationToken ct)=>Task.FromResult(new SettingSnapshot(setting,StrictJson.Value(new RegistryDwordState(true,1)),"fixture registry",DateTimeOffset.UtcNow));
  public Task ApplyAsync(SettingId setting,DeviceContext context,SettingSnapshot expectedBefore,CancellationToken ct)=>throw new NotSupportedException();
  public Task RestoreAsync(SettingId setting,DeviceContext context,SettingSnapshot expectedCurrent,SettingSnapshot recordedTarget,CancellationToken ct){Writes++;return Task.CompletedTask;}
 }
}
