using LocalDeviceToolkit.Core;
using System.Text.Json;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Assessment;
namespace LocalDeviceToolkit.Engine.Workbench;
public sealed record TaskStatus(TaskDefinition Definition,string Status,string Summary,DateTimeOffset? CapturedAt);
public sealed record UnresolvedWorkReason(string Code,string Summary)
{
 public string? FileName{get;init;}
 public Guid? RecordId{get;init;}
 public Guid? RunId{get;init;}
 public Guid? ActionId{get;init;}
}
/// <summary>Stable codes for <see cref="UnresolvedWorkReason"/>.</summary>
public static class UnresolvedWorkCodes
{
 public const string HistoryNotLoaded="HistoryNotLoaded",HistoryUnavailable="HistoryUnavailable",HistoryRecordUnreadable="HistoryRecordUnreadable",HistoryLimitReached="HistoryLimitReached";
 public const string QueuedChanges="QueuedChanges",RunEvidenceIncomplete="RunEvidenceIncomplete",ActionNeedsFollowUp="ActionNeedsFollowUp",ActionRecordWithoutRun="ActionRecordWithoutRun";
}
public sealed class WorkbenchSession
{
 readonly IDeviceInspector inspector;readonly IEvidenceStore evidence;readonly List<DeviceCapture> captures=[];readonly SemaphoreSlim gate=new(1,1);
 public WorkbenchSession(DeviceContext context,LocalCatalogue catalogue,WorkflowCatalogue workflows,IDeviceInspector inspector,IEvidenceStore evidence,OperationRegistry operations){Context=context;Catalogue=catalogue;Workflows=workflows;Queue=new(context,operations);this.inspector=inspector;this.evidence=evidence;}
 public DeviceContext Context{get;}
 public LocalCatalogue Catalogue{get;}
 public WorkflowCatalogue Workflows{get;}
 public DeploymentQueue Queue{get;}
 public string Area{get;private set;}="home";
 public string? StageId{get;private set;}
 readonly Dictionary<string,JsonElement> inputs=new(StringComparer.Ordinal);
 public long InputRevision{get;private set;}
 public JsonElement Inputs(string id)=>inputs.GetValueOrDefault(id,StrictJson.Value(new{})).Clone();
 public void SetInputs(string id,JsonElement value){LocalProbeCatalogue.ValidateInput(id,value);if(CanonicalJson.Digest(Inputs(id))!=CanonicalJson.Digest(value)){inputs[id]=value.Clone();InputRevision++;}}
 bool Matches(ProbeObservation o)=>!LocalProbeValues.IsRegistered(o.CheckId)||o.TargetDigest==LocalProbeCatalogue.TargetDigest(o.CheckId,Inputs(o.CheckId),Context);
 readonly Dictionary<Guid,RunRecord> runs=[];readonly Dictionary<Guid,string> actionOperations=[];readonly HashSet<Guid> reconciled=[];readonly HashSet<Guid> intentActions=[];readonly HashSet<Guid> resultActions=[];bool historyComplete;
 readonly List<UnresolvedWorkReason> historyReasons=[];
 public const int MaxHistoryPages=100;
 bool Resolved(Guid actionId)=>reconciled.Contains(actionId)&&actionOperations.GetValueOrDefault(actionId) is "diagnostics.update-defender-signatures" or "packages.install";
 bool NeedsFollowUp(ActionResult a)=>!Resolved(a.ActionId)&&(a.Outcome is ActionOutcome.OutcomeUnknown or ActionOutcome.Failed or ActionOutcome.RestartRequired||a.Outcome==ActionOutcome.AppliedUnverified&&(a.Outcomes is null||a.Outcomes.ActionCompleted!=true||a.Outcomes.ExpectedStateVerified==false));
 string Operation(Guid actionId)=>actionOperations.GetValueOrDefault(actionId)??"Unknown operation";
 /// <summary>Every reason handover cannot treat local action history as settled. Reasons explain; they never resolve anything.</summary>
 public IReadOnlyList<UnresolvedWorkReason> UnresolvedWork
 {
  get
  {
   var reasons=new List<UnresolvedWorkReason>();
   if(!historyComplete)reasons.AddRange(historyReasons.Count>0?historyReasons:[new(UnresolvedWorkCodes.HistoryNotLoaded,"Saved action history has not been loaded in this session.")]);
   if(Queue.Requests.Count>0)reasons.Add(new(UnresolvedWorkCodes.QueuedChanges,$"{Queue.Requests.Count} queued change(s) have not been run or removed."));
   foreach(var run in runs.Values.OrderBy(r=>r.StartedAt).ThenBy(r=>r.Id))
   {
    if(!run.EvidenceComplete)reasons.Add(new(UnresolvedWorkCodes.RunEvidenceIncomplete,$"Run {run.Id:D} did not save complete evidence; recorded outcomes may be missing."){RunId=run.Id});
    foreach(var a in run.Results.Where(NeedsFollowUp))reasons.Add(new(UnresolvedWorkCodes.ActionNeedsFollowUp,$"{Operation(a.ActionId)}: {a.Outcome} in run {run.Id:D}. {a.Reason}"){RunId=run.Id,ActionId=a.ActionId});
   }
   foreach(var id in intentActions.Concat(resultActions).Distinct().Where(id=>!Resolved(id)&&!runs.Values.Any(r=>r.Results.Any(a=>a.ActionId==id))))
    reasons.Add(new(UnresolvedWorkCodes.ActionRecordWithoutRun,$"{Operation(id)}: a dispatch intent or result was saved without a run record, so its outcome is not established."){ActionId=id});
   return reasons.AsReadOnly();
  }
 }
 public bool HasUnresolvedWork=>UnresolvedWork.Count>0;
 public void RecordRun(RunRecord run,ReviewedPlan? plan=null)
 {
  if(run.Context!=Context)throw new EvidenceValidationException("Run history belongs to another context.");runs[run.Id]=run;
  if(plan is not null){if(plan.Context!=Context||plan.Id!=run.PlanId)throw new EvidenceValidationException("Run and plan context differ.");foreach(var action in plan.Actions)actionOperations[action.Id]=action.Request.OperationId;}
 }
 public void RecordDiagnosticResolution(EvidenceRecord record)
 {
  if(record.Kind!="diagnostic-reconciliation.v1"||record.Context!=Context)throw new EvidenceValidationException("Unexpected reconciliation evidence.");
  var receipt=StrictJson.Read<Diagnostics.DiagnosticReconciliation>(StrictJson.Bytes(record.Data));
  if(receipt.CompletionReviewed&&receipt.Actor.IsElevated&&receipt.OriginalActionId is Guid id&&id!=Guid.Empty)reconciled.Add(id);
 }
 public void RecordDeploymentResolution(EvidenceRecord record)
 {
  if(record.Kind!="deployment-resolution"||record.Context!=Context)throw new EvidenceValidationException("Unexpected deployment reconciliation.");
  var value=StrictJson.Read<Deployments.DeploymentResolution>(StrictJson.Bytes(record.Data));
  if(value.ConfirmVendorCompletionAndNoPendingChildren&&value.Pending.OriginalUserSid==Context.OriginalUserSid)reconciled.Add(value.Pending.ActionId);
 }
 public async Task LoadActionHistoryAsync(CancellationToken ct)
 {
  historyComplete=false;historyReasons.Clear();string? cursor=null;
  try
  {
   for(var pageNumber=0;pageNumber<MaxHistoryPages;pageNumber++)
   {
    var page=await evidence.ReadHistoryAsync(cursor,ct);
    // A store that was never created is empty, complete history; every other diagnostic keeps history incomplete.
    var neverCreated=cursor is null&&page.NextCursor is null&&page.Entries.Count==0&&page.Diagnostics is [{Code:"NoHistory"}];
    if(!neverCreated)foreach(var d in page.Diagnostics)
     historyReasons.Add(d.FileName.Length==0?new(UnresolvedWorkCodes.HistoryUnavailable,$"Saved history could not be read completely: {d.Code}. {d.Message}"):new(UnresolvedWorkCodes.HistoryRecordUnreadable,$"Saved record {d.FileName} could not be used: {d.Code}. {d.Message}"){FileName=d.FileName});
    foreach(var record in page.Entries)
    {
     try
     {
      if(record.Kind=="run")RecordRun(StrictJson.Read<RunRecord>(StrictJson.Bytes(record.Data)));
      else if(record.Kind=="plan"){var p=StrictJson.Read<ReviewedPlan>(StrictJson.Bytes(record.Data));foreach(var a in p.Actions)actionOperations[a.Id]=a.Request.OperationId;}
      else if(record.Kind=="intent"){var intent=StrictJson.Read<IntentRecord>(StrictJson.Bytes(record.Data));intentActions.Add(intent.Action.Id);actionOperations[intent.Action.Id]=intent.Action.Request.OperationId;}
      else if(record.Kind=="result")resultActions.Add(StrictJson.Read<ResultRecord>(StrictJson.Bytes(record.Data)).Result.ActionId);
      else if(record.Kind=="diagnostic-reconciliation.v1")RecordDiagnosticResolution(record);
      else if(record.Kind=="deployment-resolution")RecordDeploymentResolution(record);
     }
     catch(Exception e) when(e is EvidenceValidationException or System.Text.Json.JsonException)
     {historyReasons.Add(new(UnresolvedWorkCodes.HistoryRecordUnreadable,$"Saved {record.Kind} record {record.Id:D} could not be applied: {(e is EvidenceValidationException v?v.Message:e.GetType().Name)}"){RecordId=record.Id});}
    }
    cursor=page.NextCursor;if(cursor is null){historyComplete=historyReasons.Count==0;return;}
   }
   historyReasons.Add(new(UnresolvedWorkCodes.HistoryLimitReached,$"Saved history continues beyond {MaxHistoryPages} pages; later records were not checked, so unresolved actions may be missing."));
  }
  catch(OperationCanceledException){throw;}
  catch(Exception e) when(e is IOException or UnauthorizedAccessException or EvidenceValidationException or System.Text.Json.JsonException)
  {historyReasons.Add(new(UnresolvedWorkCodes.HistoryUnavailable,$"Saved history could not be read: {(e is EvidenceValidationException v?v.Message:e.GetType().Name)}"));}
 }
 public IReadOnlyList<DeviceCapture> Captures=>captures.AsReadOnly();
 public void Navigate(string area,string? stage=null)
 {
  if(area is "setup" or "diagnostics")
  {
   var flow=Workflows.Workflows.Single(w=>w.Id==area);
   var selected=flow.Stages.SingleOrDefault(s=>s.Id==(stage??flow.Stages[0].Id))??throw new CatalogueValidationException("Unknown stage.");
   Area=area;StageId=selected.Id;
  }
  else if(area is "home" or "deployments" or "reports"){Area=area;StageId=null;}
  else throw new CatalogueValidationException("Unknown workspace.");
 }
 public WorkflowOrigin Origin=>StageId is not null?new(Area,StageId):throw new InvalidOperationException("Select a workflow stage.");
 public IReadOnlyList<TaskStatus> Tasks(string search="",string? scenario=null)
 {
  if(StageId is null)return [];
  var stage=Workflows.Workflows.Single(w=>w.Id==Area).Stages.Single(s=>s.Id==StageId);
  return stage.TaskIds.Where(id=>scenario is null||LocalProbeValues.IsRegistered(id)&&LocalProbeCatalogue.Resolve(id).Scenario.Contains(scenario,StringComparison.OrdinalIgnoreCase)).Select(id=>Describe(Workflows.ResolveTask(id))).Where(t=>string.IsNullOrWhiteSpace(search)||t.Definition.Title.Contains(search,StringComparison.OrdinalIgnoreCase)||t.Definition.ControlIds.Any(id=>id.Contains(search,StringComparison.OrdinalIgnoreCase))).ToArray();
 }
 TaskStatus Describe(TaskDefinition task)
 {
  if(task.ControlIds.Length==0)return new(task,"Not checked","Configure and review this action individually.",null);
  if(LocalProbeValues.IsRegistered(task.Id))
  {
   var latest=LatestObservations(task).SingleOrDefault();if(latest is null)return new(task,"Not checked","Choose inputs, then check.",null);
   var value=latest.Value is JsonElement raw?StrictJson.Read<LocalProbeValue>(StrictJson.Bytes(raw)):null;
   return new(task,LocalProbeValues.StatusLabel(latest.State,value),value?.Current??latest.Reason,latest.CapturedAt);
  }
  var findings=new List<Finding>();DateTimeOffset? at=null;
  foreach(var control in task.ControlIds)
  {
   var capture=captures.LastOrDefault(c=>c.Observations.Any(o=>Matches(o)&&o.ControlId==control&&(task.CheckIds.Length==0||task.CheckIds.Contains(o.CheckId,StringComparer.Ordinal))));
   if(capture is null)return new(task,"Not checked","Run a check to collect local evidence.",null);
   findings.Add(AssessmentService.Assess(Catalogue with { Controls=Catalogue.Controls.Where(c=>c.Id==control).Select(c=>c with {CheckIds=c.CheckIds.Where(id=>task.CheckIds.Contains(id,StringComparer.Ordinal)).ToArray()}).ToArray() },capture).Single());
   at=at is null||capture.CapturedAt<at?capture.CapturedAt:at;
  }
  var priority=new[]{FindingStatus.DoesNotMeetStandard,FindingStatus.RequiresElevation,FindingStatus.Unknown,FindingStatus.ManualVerification,FindingStatus.NotApplicable,FindingStatus.MeetsStandard};
  var worst=findings.OrderBy(f=>Array.IndexOf(priority,f.Status)).First();
  var label=worst.Status switch {FindingStatus.DoesNotMeetStandard=>"Needs attention",FindingStatus.RequiresElevation=>"Needs administrator read",FindingStatus.ManualVerification=>"Manual verification",FindingStatus.MeetsStandard=>"Meets standard",FindingStatus.NotApplicable=>"Not applicable",_=>"Unknown"};
  return new(task,label,worst.Reason,at);
 }
 public IReadOnlyList<ProbeObservation> LatestObservations(TaskDefinition task)=>task.ControlIds.SelectMany(control=>captures.LastOrDefault(c=>c.Observations.Any(o=>Matches(o)&&o.ControlId==control&&(task.CheckIds.Length==0||task.CheckIds.Contains(o.CheckId,StringComparer.Ordinal))))?.Observations.Where(o=>Matches(o)&&o.ControlId==control&&(task.CheckIds.Length==0||task.CheckIds.Contains(o.CheckId,StringComparer.Ordinal)))??[]).ToArray();
 public async Task AcceptProtectedCaptureAsync(DeviceCapture capture,WorkflowOrigin origin,CancellationToken ct,CaptureRequest? selection=null)
 {
  if(capture.Context!=Context||capture.Catalogue!=Catalogue.Identity||capture.Origin!=origin||!capture.CapturedBy.IsElevated)throw new EvidenceValidationException("Protected evidence does not match the original engineer, device, catalogue, workspace or elevated actor.");
  if(selection is not null&&(selection.CheckIds is null||selection.TaskIds is null||selection.Origin!=origin||!capture.Observations.Select(o=>o.CheckId).Order(StringComparer.Ordinal).SequenceEqual(selection.CheckIds.Order(StringComparer.Ordinal))))throw new EvidenceValidationException("Protected evidence does not match the selected checks.");
  if(selection is not null)foreach(var o in capture.Observations.Where(o=>LocalProbeValues.IsRegistered(o.CheckId)))
  {var input=selection.Inputs?.GetValueOrDefault(o.CheckId)??StrictJson.Value(new{});LocalProbeCatalogue.ValidateInput(o.CheckId,input);if(o.TargetDigest!=LocalProbeCatalogue.TargetDigest(o.CheckId,input,Context)||o.Coverage is null||o.State==ObservationState.Known&&(o.Value is not {} v||!LocalProbeValues.Accepts(o.CheckId,v)))throw new EvidenceValidationException("Protected local evidence does not match its target or schema.");}
  await evidence.SaveNewAsync(EvidenceRecord.Capture(capture),ct);
  if(selection is not null)await evidence.SaveNewAsync(EvidenceRecord.Create(Guid.NewGuid(),"capture-selection.v2",Context,capture.CapturedAt,StrictJson.Value(new CaptureSelectionEvidence(2,capture.Id,selection.TaskIds!,selection.CheckIds!){Inputs=selection.Inputs})),ct);
  captures.Add(capture);if(captures.Count>100)captures.RemoveAt(0);
 }
 public Task<DeviceCapture> CaptureAsync(IReadOnlyList<string> taskIds,CancellationToken ct)=>CaptureAtOriginAsync(taskIds,Origin,ct);
 public async Task<DeviceCapture> CaptureAtOriginAsync(IReadOnlyList<string> taskIds,WorkflowOrigin origin,CancellationToken ct)
 {
  if(taskIds.Count is <1 or >100||taskIds.Distinct(StringComparer.Ordinal).Count()!=taskIds.Count)throw new ArgumentException("Select tasks to check.");
  var selected=taskIds.Select(id=>Workflows.ResolveWorkflowTask(origin.WorkflowId,origin.StageId,id)).ToArray();
  if(!await gate.WaitAsync(0,ct))throw new InvalidOperationException("A check is already running.");
  try
  {
   var request=new CaptureRequest(selected.SelectMany(t=>t.ControlIds).Distinct(StringComparer.Ordinal).ToArray(),origin,selected.SelectMany(t=>t.CheckIds).Distinct(StringComparer.Ordinal).ToArray(),selected.Select(t=>t.Id).ToArray()){Inputs=selected.Where(t=>LocalProbeValues.IsRegistered(t.Id)).ToDictionary(t=>t.Id,t=>Inputs(t.Id),StringComparer.Ordinal)};
   var capture=await inspector.CaptureAsync(request,ct);
   if(capture.Context!=Context||capture.Catalogue!=Catalogue.Identity||capture.Origin!=origin)throw new EvidenceValidationException("The check returned evidence for a different device, user, catalogue or workspace.");
   await evidence.SaveNewAsync(EvidenceRecord.Capture(capture),ct);
   await evidence.SaveNewAsync(EvidenceRecord.Create(Guid.NewGuid(),"capture-selection.v2",Context,capture.CapturedAt,StrictJson.Value(new CaptureSelectionEvidence(2,capture.Id,request.TaskIds!,request.CheckIds!){Inputs=request.Inputs})),ct);
   captures.Add(capture);if(captures.Count>100)captures.RemoveAt(0);return capture;
  }
  finally{gate.Release();}
 }
}
