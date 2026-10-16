using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Diagnostics;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Reporting;
using LocalDeviceToolkit.Engine.Workbench;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class ReviewFixTests
{
 [Fact] public void Required_local_evidence_must_match_the_current_target()
 {
  var now=DateTimeOffset.UtcNow;var check="local.storage.capacity";var a=new string('a',64);var b=new string('b',64);
  var observation=new ProbeObservation(check,check,ObservationState.Known,StrictJson.Value(new LocalProbeValue("Enough",null,"Observed",new Dictionary<string,string>())),"Fixture",now,"Observed"){Coverage=new(true,false,1,null,[]),TargetDigest=a,LocalSchemaVersion=2,RuleVersion="2026-10-15"};
  var profile=new ClientChecklist("client",1,"Client",new Dictionary<string,System.Text.Json.JsonElement>{{check,StrictJson.Value("Enough")}},[check],false);
  Assert.NotEqual("Ready for handover",HandoverService.Evaluate(profile,TestData.Context,[observation],[],now,localTargets:new Dictionary<string,string>{{check,b}}).State);
  Assert.Equal("Ready for handover",HandoverService.Evaluate(profile,TestData.Context,[observation],[],now,localTargets:new Dictionary<string,string>{{check,a}}).State);
 }
 [Fact] public async Task Uncertain_actions_survive_other_checks_and_app_restart()
 {
  var root=Path.Combine(Path.GetTempPath(),"uncertain-history-"+Guid.NewGuid().ToString("N"));try
  {
   var catalogue=CatalogueLoader.LoadShipped();var flows=WorkflowCatalogue.LoadShipped();var store=new EvidenceStore(root,ToolkitEvidenceSchemas.Create(),TestData.Context);var now=DateTimeOffset.UtcNow;
   var run=new RunRecord(Guid.NewGuid(),Guid.NewGuid(),TestData.Context,now,now,[new(Guid.NewGuid(),ActionOutcome.OutcomeUnknown,TestData.Actor,"Signature process outcome unknown",[])],true);
   await store.SaveNewAsync(EvidenceRecord.Create(run.Id,"run",run.Context,now,StrictJson.Value(run)),default);
   WorkbenchSession Session()=>new(TestData.Context,catalogue,flows,new Inspector(catalogue),store,new OperationRegistry(flows,[]));
   var session=Session();await session.LoadActionHistoryAsync(default);Assert.True(session.HasUnresolvedWork);session.Navigate("setup","readiness");await session.CaptureAsync(["readiness.secure-boot"],default);Assert.True(session.HasUnresolvedWork);
   var restarted=Session();await restarted.LoadActionHistoryAsync(default);Assert.True(restarted.HasUnresolvedWork);
  }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
 [Fact] public void Real_security_and_incomplete_observations_create_linked_signposts()
 {
  var id=Guid.NewGuid();var observation=new ProbeObservation("local.defender.protection","local.defender.protection",ObservationState.Known,StrictJson.Value(new LocalProbeValue("Passive",null,"Observed",new Dictionary<string,string>{{"mode","Passive"}})),"Fixture",DateTimeOffset.UtcNow,"Observed"){Coverage=new(false,false,1,null,["One provider denied"]),EvidenceCaptureId=id};
  var summary=DiagnosticInterpreter.Summarize(DiagnosticInterpreter.Interpret(observation));Assert.Contains(summary.Signposts,f=>f.SecurityRelevant);Assert.Contains(summary.Signposts,f=>f.RequiredUnknown);Assert.All(summary.Signposts,f=>Assert.Contains(id,f.CaptureIds));
 }
 [Fact] public async Task Explicit_protected_resolution_is_linked_to_the_original_action_and_survives_restart()
 {
  var root=Path.Combine(Path.GetTempPath(),"reconciliation-history-"+Guid.NewGuid().ToString("N"));try
  {
   var catalogue=CatalogueLoader.LoadShipped();var flows=WorkflowCatalogue.LoadShipped();var store=new EvidenceStore(root,ToolkitEvidenceSchemas.Create(),TestData.Context);var at=DateTimeOffset.UtcNow;var op="diagnostics.update-defender-signatures";
   var before=new ProbeObservation(op,"repair.review-state",ObservationState.Known,StrictJson.Value(new RepairSnapshot(new string('a',64),"Version",true,"Ready",new Dictionary<string,string>())),"Fixture",at,"Observed");
   var action=new PlannedAction(Guid.NewGuid(),new(op,op,StrictJson.Value(new DefenderSignatureInput(true)),new("diagnostics","local")),new(true,OperationScope.Machine,PrivilegeRequirement.Administrator,"Ready"),[before],ObservationFingerprint.Of([before]));
   var plan=new ReviewedPlan(Guid.NewGuid(),TestData.Context,catalogue.Identity,at,at.AddMinutes(10),[action],""){Lifetime=ReviewLifetime.Start()};plan=plan with{Digest=CanonicalJson.Digest(plan)};
   var run=new RunRecord(Guid.NewGuid(),plan.Id,TestData.Context,at,at,[new(action.Id,ActionOutcome.OutcomeUnknown,TestData.Actor,"Uncertain process",[])],true);
   await store.SaveNewAsync(EvidenceRecord.Create(plan.Id,"plan",plan.Context,at,StrictJson.Value(plan)),default);await store.SaveNewAsync(EvidenceRecord.Create(run.Id,"run",run.Context,at,StrictJson.Value(run)),default);
   WorkbenchSession Session()=>new(TestData.Context,catalogue,flows,new Inspector(catalogue),store,new OperationRegistry(flows,[]));var session=Session();await session.LoadActionHistoryAsync(default);Assert.True(session.HasUnresolvedWork);
   var receiptId=Guid.NewGuid();var receipt=new DiagnosticReconciliation(receiptId,TestData.Context,TestData.Actor with{IsElevated=true},at,true,"Explicit completion reviewed; original process not running."){OriginalActionId=action.Id};
   var record=EvidenceRecord.Create(receiptId,"diagnostic-reconciliation.v1",receipt.Context,at,StrictJson.Value(receipt));await store.SaveNewAsync(record,default);session.RecordDiagnosticResolution(record);Assert.False(session.HasUnresolvedWork);
   var restarted=Session();await restarted.LoadActionHistoryAsync(default);Assert.False(restarted.HasUnresolvedWork);
  }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
 [Fact] public void Unavailable_security_check_signposts_are_visible_in_the_real_preview()
 {
  var id=Guid.NewGuid();var o=new ProbeObservation("local.defender.protection","local.defender.protection",ObservationState.AccessDenied,null,"Fixture",DateTimeOffset.UtcNow,"Access denied"){EvidenceCaptureId=id,Coverage=new(false,false,0,null,["Denied"])};
  var preview=ActionPreviewFormatter.Describe(WorkflowCatalogue.LoadShipped().ResolveTask(o.CheckId),null,[o]);Assert.Contains("1 security",preview.Explanation);Assert.Contains("1 required/incomplete",preview.Explanation);Assert.Contains(id.ToString(),preview.Explanation);
 }
 sealed class Inspector(LocalCatalogue catalogue):IDeviceInspector
 {public Task<DeviceCapture> CaptureAsync(CaptureRequest r,CancellationToken ct)=>Task.FromResult(new DeviceCapture(Guid.NewGuid(),TestData.Context,catalogue.Identity,r.Origin,TestData.Actor,DateTimeOffset.UtcNow,[new("CMP-WIN-001","secureboot.enabled",ObservationState.Known,StrictJson.Value(true),"Fixture",DateTimeOffset.UtcNow,"Observed")]));}
}
