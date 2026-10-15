using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Logging;
using LocalDeviceToolkit.Engine.Planning;
namespace LocalDeviceToolkit.Engine.Workbench;

public sealed record ReviewSelection(string TaskId,WorkflowOrigin Origin,bool ChangeRequested,ActionRequest? Request=null);
public sealed record ReviewRow(TaskDefinition Task,ActionRequest? Request,WorkflowOrigin Origin,bool ChangeRequested,ActionPreview Preview,IReadOnlyList<ProbeObservation> Before,ActionReadiness? Readiness,ActionPreview? AfterPreview=null,DateTimeOffset? AfterCapturedAt=null);
public sealed class ReviewRun
{
 internal ReviewRun(RunTextLog log,long generation,DateTimeOffset createdAt){Log=log;Generation=generation;CreatedAt=createdAt;}
 internal RunTextLog Log{get;}internal long Generation{get;}internal DateTimeOffset CreatedAt{get;}internal ReviewedPlan? Plan{get;set;}internal bool Closed{get;set;}
 internal readonly List<ReviewRow> Items=[];
 public IReadOnlyList<ReviewRow> Rows=>Items.AsReadOnly();
 public string FilePath=>Log.FilePath;public Guid RunId=>Log.RunId;public string LogText{get;internal set;}="";public string State{get;internal set;}="Checking";
 public IReadOnlyList<PlannedAction> Actions=>Plan?.Actions??[];
 public int ChangesCount=>Items.Count(r=>r.ChangeRequested&&r.Preview.NeedsChange);
 public bool CanApply=>!Closed&&State=="Ready"&&Plan is not null&&ChangesCount>0;
}

/// <summary>Human review and companion logging. Immutable plans and protected execution remain the authority.</summary>
public sealed class ReviewRunService(OperationRegistry operations,PlanService plans,RunCoordinator coordinator,WorkbenchSession session,string logDirectory,TimeProvider clock)
{
 readonly SemaphoreSlim gate=new(1,1);long generation;
 public ReviewRun? Current{get;private set;}
 public void Invalidate()=>Interlocked.Increment(ref generation);
 static string Raw(IReadOnlyList<ProbeObservation> values)=>string.Join("; ",values.Select(o=>$"{o.CheckId}={o.Value?.GetRawText()??"Unavailable"}; source={o.Source}; captured={o.CapturedAt:O}; {o.Reason}"));
 internal async Task AppendAsync(ReviewRun run,string stage,string message,CancellationToken ct)
 {
  // Keep complete long source/value details across bounded records; never silently truncate them.
  if(message.Length==0)await run.Log.AppendAsync(stage,"",ct);
  for(var offset=0;offset<message.Length;)
  {
   var end=Math.Min(message.Length,offset+8000);if(end<message.Length&&char.IsHighSurrogate(message[end-1])&&char.IsLowSurrogate(message[end]))end--;
   await run.Log.AppendAsync(stage,message[offset..end],ct);offset=end;
  }
  run.LogText=await run.Log.ReadAsync(CancellationToken.None);
 }
 async Task CloseAsync(ReviewRun run)
 {if(run.Closed)return;run.Closed=true;await run.Log.DisposeAsync();}
 public async Task<ReviewRun> CheckAsync(IReadOnlyList<ReviewSelection> selection,CancellationToken ct)
 {
  if(!await gate.WaitAsync(0,ct))throw new PlanValidationException("A check or change is already running.");
  ReviewRun? run=null;
  try
  {
   if(selection is null||selection.Count is <1 or >100)throw new PlanValidationException("Select between 1 and 100 tasks.");
   var selected=selection.Select(s=>s with{Origin=s.Origin with{},Request=s.Request is null?null:s.Request with{Parameters=s.Request.Parameters.Clone()}}).ToArray();
   foreach(var item in selected)
   {
    var task=session.Workflows.ResolveWorkflowTask(item.Origin.WorkflowId,item.Origin.StageId,item.TaskId);
    if(item.Request is not null&&(item.Request.TaskId!=task.Id||item.Request.OperationId!=task.OperationId||item.Request.Origin!=item.Origin))throw new PlanValidationException("The selected task and proposed change do not match.");
    if(item.Request is not null)operations.Validate(item.Request);
   }
   if(selected.Select(s=>s.TaskId).Distinct(StringComparer.Ordinal).Count()!=selected.Length)throw new PlanValidationException("Each task can be selected only once.");
   if(Current is {Closed:false} previous)
   {
    try{await AppendAsync(previous,"review","Superseded by a new review. No further actions can run from this review.",CancellationToken.None);}finally{previous.State="Superseded";await CloseAsync(previous);}
   }
   var version=Interlocked.Increment(ref generation);
   run=new(await RunTextLog.CreateAsync(logDirectory,session.Context,clock,ct),version,clock.GetUtcNow());Current=run;
   run.LogText=await run.Log.ReadAsync(ct);
   await AppendAsync(run,"selection",string.Join("; ",selected.Select(s=>$"{s.TaskId}: {(s.ChangeRequested?"Review change":"Check only")}")),ct);
   var facts=new Dictionary<WorkflowOrigin,IReadOnlyList<ProbeObservation>>();
   foreach(var group in selected.Where(s=>!s.ChangeRequested).GroupBy(s=>s.Origin))
   {
    try
    {
     await AppendAsync(run,"check","Reading selected checks: "+string.Join(", ",group.Select(s=>s.TaskId)),ct);
     var capture=await session.CaptureAtOriginAsync(group.Select(s=>s.TaskId).ToArray(),group.Key,ct);facts[group.Key]=capture.Observations;
    }
    catch(Exception error) when(error is not OperationCanceledException)
    {
     facts[group.Key]=group.SelectMany(s=>session.Workflows.ResolveTask(s.TaskId).ControlIds.Select(id=>new ProbeObservation(id,"read.failure",ObservationState.Unavailable,null,"Registered device checks",clock.GetUtcNow(),"Could not read: "+error.Message))).ToArray();
     await AppendAsync(run,"check","Could not read selected checks: "+error.Message,ct);
    }
   }
   foreach(var item in selected)
   {
    ct.ThrowIfCancellationRequested();var task=session.Workflows.ResolveTask(item.TaskId);IReadOnlyList<ProbeObservation> before=[];ActionReadiness? readiness=null;ActionPreview preview;
    if(!item.ChangeRequested)
    {
     before=facts.GetValueOrDefault(item.Origin,[]).Where(o=>task.ControlIds.Contains(o.ControlId)&&(task.CheckIds.Contains(o.CheckId)||o.CheckId=="read.failure")).ToArray();
     preview=ActionPreviewFormatter.Describe(task,null,before);
     if(task.OperationId is string checkOperation && (Settings.FixedSettingOperation.Definitions.Any(d=>d.OperationId==checkOperation)||checkOperation.StartsWith("settings.uk-",StringComparison.Ordinal)||checkOperation=="apps.remove-consumer-copilot"))
     {
      try
      {
       var inspectRequest=new ActionRequest(task.Id,checkOperation,StrictJson.Value(new Settings.SettingParameters(false)),item.Origin);
       before=await operations.Find(checkOperation).CaptureBeforeAsync(session.Context,inspectRequest.Parameters,ct);
       preview=ActionPreviewFormatter.Describe(task,inspectRequest,before);
      }
      catch(Exception error) when(error is not OperationCanceledException){preview=new(task.Title,"Could not read","Not confirmed",PreviewStatus.Unreadable,error.Message,string.Join("; ",before.Select(o=>o.Source)),"Read only",false);}
     }
     if(before.Any(o=>o.CheckId=="read.failure"))preview=preview with{Current="Could not read",Status=PreviewStatus.Unreadable,Explanation=before[0].Reason,NeedsChange=false};
    }
    else if(task.OperationId is null||item.Request is null)
     preview=new(task.Title,"Not checked","Choose required inputs",PreviewStatus.Blocked,task.OperationId is null?"This task has no automatic change. Run its checks or record a manual check.":"Choose the required inputs before reviewing this change.","","Not running",false);
    else
    {
     try
     {
      var operation=operations.Find(item.Request.OperationId);
      await AppendAsync(run,"check","Reading current value: "+task.Title,ct);
      before=Array.AsReadOnly((await operation.CaptureBeforeAsync(session.Context,item.Request.Parameters,ct)).Select(o=>o with{Value=o.Value?.Clone()}).ToArray());
      readiness=await operation.InspectAsync(session.Context,item.Request.Parameters,ct);
      preview=ActionPreviewFormatter.Describe(task,item.Request,before,readiness);
      if(preview.Status==PreviewStatus.Manual)preview=preview with{Status=PreviewStatus.Blocked,Explanation=preview.Explanation+" This change needs an exact expected value before it can run.",NeedsChange=false};
     }
     catch(Exception error) when(error is not OperationCanceledException)
     {preview=new(task.Title,"Could not read","Not confirmed",error is PlanValidationException?PreviewStatus.Blocked:PreviewStatus.Unreadable,"No change will run: "+error.Message,string.Join("; ",before.Select(o=>o.Source)),readiness?.Scope==OperationScope.Machine?"Administrator":"Current user",false);}
    }
    var row=new ReviewRow(task,item.Request,item.Origin,item.ChangeRequested,preview,before,readiness);run.Items.Add(row);
    await LogRowAsync(run,row,ct);
    if(row.ChangeRequested&&row.Preview.Status==PreviewStatus.AlreadyCorrect)await AppendAsync(run,"skip",task.Title+": already correct; no write is scheduled.",ct);
   }
   await CheckPrerequisitesAsync(run,ct);
   foreach(var blocked in run.Items.Where(r=>r.ChangeRequested&&r.Preview.Status==PreviewStatus.Blocked))await LogRowAsync(run,blocked,ct);
   if(run.Items.Any(r=>r.ChangeRequested&&r.Preview.Status is PreviewStatus.Blocked or PreviewStatus.Unreadable or PreviewStatus.NotChecked or PreviewStatus.Manual))
   {run.State="Blocked";await AppendAsync(run,"review","Blocked items must be resolved before any selected change can run. No changes have been applied.",ct);return run;}
   var changes=run.Items.Where(r=>r.ChangeRequested&&r.Preview.NeedsChange&&r.Request is not null).OrderBy(r=>Order(r.Request!.OperationId)).ToArray();
   if(changes.Length>0)
   {
    try
    {
     run.Plan=await plans.CreateAsync(session.Context,session.Catalogue,changes.Select(r=>r.Request!).ToArray(),ct);
     foreach(var action in run.Plan.Actions)
     {
      var row=changes.Single(r=>r.Request==action.Request||CanonicalJson.Digest(r.Request)==CanonicalJson.Digest(action.Request));
      if(ObservationFingerprint.Of(row.Before)!=action.BeforeDigest)throw new PlanValidationException("A value changed during review. Check again before confirming.");
     }
     // Expiry starts with the first review, never after time spent reading the page.
     var plan=run.Plan with{CreatedAt=run.CreatedAt,ExpiresAt=run.CreatedAt.AddMinutes(10),Digest=""};run.Plan=plan with{Digest=CanonicalJson.Digest(plan)};
     run.State="Ready";
    }
    catch(PlanValidationException error)
    {
     run.State="Blocked";run.Plan=null;
     for(var i=0;i<run.Items.Count;i++)if(run.Items[i].ChangeRequested&&run.Items[i].Preview.NeedsChange)run.Items[i]=run.Items[i] with{Preview=run.Items[i].Preview with{Status=PreviewStatus.Blocked,Explanation=error.Message,NeedsChange=false}};
     await AppendAsync(run,"review","Plan blocked: "+error.Message,ct);return run;
    }
   }
   else run.State="Checked";
   await AppendAsync(run,"review",$"Review ready. {changes.Length} changes need confirmation. Already-correct values are skipped. No changes have been applied.",ct);
   return run;
  }
  catch
  {
   if(run is not null)
   {
    run.State="Failed";
    try{await AppendAsync(run,"check","Check stopped. No changes were dispatched.",CancellationToken.None);}catch(Exception){}finally{await CloseAsync(run);}
   }
   throw;
  }
  finally{gate.Release();}
 }
 static int Order(string operation)=>operation switch{"settings.uk-input-language"=>0,"settings.uk-keyboard"=>1,"settings.uk-default-keyboard"=>2,_=>3};
 async Task CheckPrerequisitesAsync(ReviewRun run,CancellationToken ct)
 {
  bool Selected(string op)=>run.Items.Any(r=>r.ChangeRequested&&r.Request?.OperationId==op&&r.Preview.Status is PreviewStatus.AlreadyCorrect or PreviewStatus.Different or PreviewStatus.Missing);
  foreach(var row in run.Items.ToArray())
  {
   if(!row.ChangeRequested||row.Request is null||!row.Preview.NeedsChange)continue;
   string? requirement=null;
   if(row.Request.OperationId=="settings.uk-keyboard"&&!Selected("settings.uk-input-language"))
   {
    // The keyboard snapshot intentionally does not invent a language that has not been read.
    var op=operations.Find("settings.uk-input-language");
    var probe=await op.CaptureBeforeAsync(session.Context,row.Request.Parameters,ct);
    if(!probe.Any(o=>o.Value is JsonElement v&&v.TryGetProperty("present",out var p)&&p.ValueKind==JsonValueKind.True))requirement="Select Add UK input language first. It will not be added automatically.";
   }
   if(row.Request.OperationId=="settings.uk-default-keyboard"&&!Selected("settings.uk-keyboard"))
   {
    var op=operations.Find("settings.uk-keyboard");var probe=await op.CaptureBeforeAsync(session.Context,row.Request.Parameters,ct);
    if(!probe.Any(o=>o.Value is JsonElement v&&v.TryGetProperty("inputMethodTips",out var p)&&p.ValueKind==JsonValueKind.Array&&p.EnumerateArray().Any(t=>t.GetString()=="0809:00000809")))requirement="Select Add UK keyboard first. It will not be added automatically.";
   }
   if(requirement is not null){var i=run.Items.IndexOf(row);run.Items[i]=row with{Preview=row.Preview with{Status=PreviewStatus.Blocked,Explanation=requirement,NeedsChange=false}};}
  }
 }
 async Task LogRowAsync(ReviewRun run,ReviewRow row,CancellationToken ct)=>await AppendAsync(run,"review",$"{row.Task.Title}; Status={ActionPreviewFormatter.StatusLabel(row.Preview.Status)}; Current={row.Preview.Current}; Expected={row.Preview.Expected}; Account={row.Preview.Account}; Source={row.Preview.Source}; {row.Preview.Explanation}; Raw before: {Raw(row.Before)}",ct);
 public async Task<RunRecord?> RunAsync(ReviewRun review,bool policyConfirmed,CancellationToken ct)
 {
  if(!await gate.WaitAsync(0,ct))throw new PlanValidationException("A check or change is already running.");
  try
  {
   if(!ReferenceEquals(review,Current)||review.Closed||review.Generation!=Volatile.Read(ref generation))throw new PlanValidationException("The selection changed. Check and review it again.");
   if(!policyConfirmed)throw new PlanValidationException("Confirm the company policy review before running changes.");
   if(clock.GetUtcNow()<review.CreatedAt||clock.GetUtcNow()>=review.CreatedAt.AddMinutes(10))throw new PlanValidationException("This review expired. Check and review again.");
   if(review.State=="Blocked")throw new PlanValidationException("Resolve blocked items before running changes.");
   if(review.Plan is null)
   {await AppendAsync(review,"skip","Check-only or already correct: no changes were needed or dispatched.",ct);review.State="Finished";await CloseAsync(review);return null;}
   await AppendAsync(review,"confirm",$"Engineer confirmed {review.ChangesCount} explicit changes and company policy review. Device={session.Context.ComputerName}; User={session.Context.OriginalUserName}",ct);
   review.State="Running";
   var result=await coordinator.ExecuteAsync(review.Plan,ct,new LogProgress(this,review));
   foreach(var action in review.Plan.Actions)
   {
    var observed=result.Results.Single(r=>r.ActionId==action.Id);UpdateAfter(review,action,observed);
   }
   try
   {
    await AppendAsync(review,"finish",$"Run={result.Id:D}; Evidence complete={result.EvidenceComplete}; "+string.Join("; ",result.Results.Select(r=>$"{r.ActionId:D}: {r.Outcome}; {r.Reason}")),CancellationToken.None);
    review.State="Finished";
   }
   catch(Exception error) when(error is IOException or UnauthorizedAccessException or ObjectDisposedException)
   {result=result with{EvidenceComplete=false};review.State="Finished — log incomplete";}
   await CloseAsync(review);return result;
  }
  catch
  {
   if(ReferenceEquals(review,Current)&&!review.Closed)
   {
    review.State="Stopped";
    try{await AppendAsync(review,"stop","The review/run stopped. Inspect the recorded results before retrying; no automatic retry will occur.",CancellationToken.None);}catch(Exception){}finally{await CloseAsync(review);}
   }
   throw;
  }
  finally{gate.Release();}
 }
 public async Task CancelAsync(ReviewRun review,CancellationToken ct)
 {
  if(!await gate.WaitAsync(0,ct))throw new PlanValidationException("A check or change is already running.");
  try{if(review.Closed)return;try{await AppendAsync(review,"cancel","Review cancelled. No further changes will be started from this review.",ct);}finally{review.State="Cancelled";await CloseAsync(review);}}
  finally{gate.Release();}
 }
 static ActionPreview FormatAfter(ReviewRow row,ActionResult result)
 {
  var values=result.After;
  if(row.Request?.OperationId=="packages.install"&&row.Before.FirstOrDefault(o=>o.CheckId=="deployment.review-state")?.Value is JsonElement captured)
  {
   var original=StrictJson.Read<PackageDeploymentOperation.DeploymentReviewState>(StrictJson.Bytes(captured));
   values=values.Select(o=>o.CheckId=="deployment.installed-product"&&o.Value is JsonElement actual&&o.State==ObservationState.Known?o with{CheckId="deployment.review-state",Value=StrictJson.Value(original with{Inventory=StrictJson.Read<DeploymentInventory>(StrictJson.Bytes(actual))})}:o).ToArray();
  }
  var preview=values.Count>0?ActionPreviewFormatter.Describe(row.Task,row.Request,values,row.Readiness):row.Preview with{Current="Not rechecked",NeedsChange=false};
  if(result.Outcome!=ActionOutcome.AppliedVerified)preview=preview with{Status=result.Outcome==ActionOutcome.CancelledBeforeExecution?PreviewStatus.NotChecked:PreviewStatus.Blocked,NeedsChange=false};
  return preview with{Explanation=result.Reason+" "+preview.Explanation,NeedsChange=false};
 }
 static void UpdateAfter(ReviewRun review,PlannedAction action,ActionResult result)
 {
  var i=review.Items.FindIndex(r=>r.Request is not null&&CanonicalJson.Digest(r.Request)==CanonicalJson.Digest(action.Request));
  if(i<0)return;
  var row=review.Items[i];review.Items[i]=row with{AfterPreview=FormatAfter(row,result),AfterCapturedAt=result.After.Count>0?result.After.Max(o=>o.CapturedAt):DateTimeOffset.UtcNow};
 }
 sealed class LogProgress(ReviewRunService owner,ReviewRun review):IRunProgressSink
 {
  public Task BeforeActionAsync(ReviewedPlan plan,PlannedAction action,CancellationToken ct)
  {
   var row=review.Items.Single(r=>r.Request is not null&&CanonicalJson.Digest(r.Request)==CanonicalJson.Digest(action.Request));
   return owner.AppendAsync(review,"start",$"{row.Task.Title}; Current={row.Preview.Current}; Expected={row.Preview.Expected}; Source={row.Preview.Source}; Reviewed before (freshly rechecked)={Raw(action.Before)}",ct);
  }
  public async Task AfterActionAsync(ReviewedPlan plan,PlannedAction action,ActionResult result,CancellationToken ct)
  {
   UpdateAfter(review,action,result);var row=review.Items.Single(r=>r.Request is not null&&CanonicalJson.Digest(r.Request)==CanonicalJson.Digest(action.Request));
   await owner.AppendAsync(review,"result",$"{row.Task.Title}; Outcome={result.Outcome}; Current={row.AfterPreview!.Current}; Expected={row.AfterPreview.Expected}; Status={ActionPreviewFormatter.StatusLabel(row.AfterPreview.Status)}; Actor={result.Actor?.UserName??"Not started / not confirmed"}; SID={result.Actor?.Sid??"Not confirmed"}; Source={row.AfterPreview.Source}; {result.Reason}; Readback={Raw(result.After)}",ct);
  }
 }
}
