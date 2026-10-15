using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Assessment;
namespace LocalDeviceToolkit.Engine.Workbench;
public sealed record TaskStatus(TaskDefinition Definition,string Status,string Summary,DateTimeOffset? CapturedAt);
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
 public IReadOnlyList<TaskStatus> Tasks(string search="")
 {
  if(StageId is null)return [];
  var stage=Workflows.Workflows.Single(w=>w.Id==Area).Stages.Single(s=>s.Id==StageId);
  return stage.TaskIds.Select(id=>Describe(Workflows.ResolveTask(id))).Where(t=>string.IsNullOrWhiteSpace(search)||t.Definition.Title.Contains(search,StringComparison.OrdinalIgnoreCase)||t.Definition.ControlIds.Any(id=>id.Contains(search,StringComparison.OrdinalIgnoreCase))).ToArray();
 }
 TaskStatus Describe(TaskDefinition task)
 {
  var findings=new List<Finding>();DateTimeOffset? at=null;
  foreach(var control in task.ControlIds)
  {
   var capture=captures.LastOrDefault(c=>c.Observations.Any(o=>o.ControlId==control&&(task.CheckIds.Length==0||task.CheckIds.Contains(o.CheckId,StringComparer.Ordinal))));
   if(capture is null)return new(task,"Not checked","Run a check to collect local evidence.",null);
   findings.Add(AssessmentService.Assess(Catalogue with { Controls=Catalogue.Controls.Where(c=>c.Id==control).Select(c=>c with {CheckIds=c.CheckIds.Where(id=>task.CheckIds.Contains(id,StringComparer.Ordinal)).ToArray()}).ToArray() },capture).Single());
   at=at is null||capture.CapturedAt<at?capture.CapturedAt:at;
  }
  var priority=new[]{FindingStatus.DoesNotMeetStandard,FindingStatus.RequiresElevation,FindingStatus.Unknown,FindingStatus.ManualVerification,FindingStatus.NotApplicable,FindingStatus.MeetsStandard};
  var worst=findings.OrderBy(f=>Array.IndexOf(priority,f.Status)).First();
  var label=worst.Status switch {FindingStatus.DoesNotMeetStandard=>"Needs attention",FindingStatus.RequiresElevation=>"Needs administrator read",FindingStatus.ManualVerification=>"Manual verification",FindingStatus.MeetsStandard=>"Meets standard",FindingStatus.NotApplicable=>"Not applicable",_=>"Unknown"};
  return new(task,label,worst.Reason,at);
 }
 public IReadOnlyList<ProbeObservation> LatestObservations(TaskDefinition task)=>task.ControlIds.SelectMany(control=>captures.LastOrDefault(c=>c.Observations.Any(o=>o.ControlId==control&&(task.CheckIds.Length==0||task.CheckIds.Contains(o.CheckId,StringComparer.Ordinal))))?.Observations.Where(o=>o.ControlId==control&&(task.CheckIds.Length==0||task.CheckIds.Contains(o.CheckId,StringComparer.Ordinal)))??[]).ToArray();
 public async Task AcceptProtectedCaptureAsync(DeviceCapture capture,WorkflowOrigin origin,CancellationToken ct,CaptureRequest? selection=null)
 {
  if(capture.Context!=Context||capture.Catalogue!=Catalogue.Identity||capture.Origin!=origin||!capture.CapturedBy.IsElevated)throw new EvidenceValidationException("Protected evidence does not match the original engineer, device, catalogue, workspace or elevated actor.");
  if(selection is not null&&(selection.CheckIds is null||selection.TaskIds is null||selection.Origin!=origin||!capture.Observations.Select(o=>o.CheckId).Order(StringComparer.Ordinal).SequenceEqual(selection.CheckIds.Order(StringComparer.Ordinal))))throw new EvidenceValidationException("Protected evidence does not match the selected checks.");
  await evidence.SaveNewAsync(EvidenceRecord.Capture(capture),ct);
  if(selection is not null)await evidence.SaveNewAsync(EvidenceRecord.Create(Guid.NewGuid(),"capture-selection.v2",Context,capture.CapturedAt,StrictJson.Value(new CaptureSelectionEvidence(2,capture.Id,selection.TaskIds!,selection.CheckIds!))),ct);
  captures.Add(capture);if(captures.Count>100)captures.RemoveAt(0);
 }
 public Task<DeviceCapture> CaptureAsync(IReadOnlyList<string> taskIds,CancellationToken ct)=>CaptureAtOriginAsync(taskIds,Origin,ct);
 public async Task<DeviceCapture> CaptureAtOriginAsync(IReadOnlyList<string> taskIds,WorkflowOrigin origin,CancellationToken ct)
 {
  if(taskIds.Count is <1 or >100)throw new ArgumentException("Select tasks to check.");
  var selected=taskIds.Select(id=>Workflows.ResolveWorkflowTask(origin.WorkflowId,origin.StageId,id)).ToArray();
  if(!await gate.WaitAsync(0,ct))throw new InvalidOperationException("A check is already running.");
  try
  {
   var request=new CaptureRequest(selected.SelectMany(t=>t.ControlIds).Distinct(StringComparer.Ordinal).ToArray(),origin,selected.SelectMany(t=>t.CheckIds).Distinct(StringComparer.Ordinal).ToArray(),selected.Select(t=>t.Id).ToArray());
   var capture=await inspector.CaptureAsync(request,ct);
   if(capture.Context!=Context||capture.Catalogue!=Catalogue.Identity||capture.Origin!=origin)throw new EvidenceValidationException("The check returned evidence for a different device, user, catalogue or workspace.");
   await evidence.SaveNewAsync(EvidenceRecord.Capture(capture),ct);
   await evidence.SaveNewAsync(EvidenceRecord.Create(Guid.NewGuid(),"capture-selection.v2",Context,capture.CapturedAt,StrictJson.Value(new CaptureSelectionEvidence(2,capture.Id,request.TaskIds!,request.CheckIds!))),ct);
   captures.Add(capture);if(captures.Count>100)captures.RemoveAt(0);return capture;
  }
  finally{gate.Release();}
 }
}
