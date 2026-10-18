using System.Net;
using System.Text;
using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Settings;
namespace LocalDeviceToolkit.Engine.Workbench;

public sealed record SilentRunOptions(string Preset,string? InputsFile,string? ConfigFile=null)
{
 public static SilentRunOptions Parse(string[] args)
 {
  const string usage="Use --silent --preset all|fast [--config <Diagnostics.config>] [--inputs <probe-inputs.json>]. Silent diagnostics cannot apply changes.";
  if(args.Length is not (3 or 5 or 7)||args[0]!="--silent"||args[1]!="--preset"||args[2] is not ("all" or "fast"))throw new ArgumentException(usage);
  var paths=new Dictionary<string,string>(StringComparer.Ordinal);
  for(var i=3;i<args.Length;i+=2)
   if(args[i] is not ("--inputs" or "--config")||string.IsNullOrWhiteSpace(args[i+1])||args[i+1].Any(char.IsControl)||!paths.TryAdd(args[i],args[i+1]))throw new ArgumentException(usage);
  return new(args[2],paths.GetValueOrDefault("--inputs"),paths.GetValueOrDefault("--config"));
 }
 public static IReadOnlyDictionary<string,JsonElement> ReadInputs(byte[] bytes)
 {
  var inputs=StrictJson.Read<Dictionary<string,JsonElement>>(bytes,64*1024);
  if(inputs.Count>LocalProbeCatalogue.All.Count)throw new CatalogueValidationException("Too many check inputs.");
  foreach(var pair in inputs)
   try{LocalProbeCatalogue.ValidateInput(pair.Key,pair.Value);}
   catch(InvalidOperationException){throw new CatalogueValidationException("Invalid JSON field type in inputs for "+pair.Key+". Use the documented probe schema.");}
  return inputs;
 }
 public static byte[] ReadFile(string path)
 {
  EvidenceStore.RefuseReparsePath(Path.GetFullPath(path));
  using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(file.Length>64*1024)throw new ArgumentException("Diagnostic input/config files must not exceed 64 KiB.");
  var bytes=new byte[checked((int)file.Length)];file.ReadExactly(bytes);return bytes;
 }
}
public sealed record SilentTaskResult(string TaskId,string Title,string Status,string Current,string Expected,string Reason,string Source);
public sealed record SilentRunSummary(int SchemaVersion,Guid RunId,string Preset,DateTimeOffset StartedAt,DateTimeOffset FinishedAt,bool CheckOnly,bool EvidenceComplete,int ExitCode,string LogFile,IReadOnlyList<SilentTaskResult> Tasks);
public sealed record SilentRunResult(SilentRunSummary Summary,string JsonFile,string HtmlFile);
public sealed class SilentRunService(WorkbenchSession session,ReviewRunService reviews,TimeProvider clock)
{
 public static bool Exclude(TaskDefinition task,string preset)=>preset=="fast"&&task.TimedPerformanceTest;
 public static IReadOnlyList<TaskDefinition> DiagnosticTasks(WorkflowCatalogue workflows)=>workflows.Workflows.Single(w=>w.Id=="diagnostics").Stages.Single(s=>s.Id=="all").TaskIds.Select(workflows.ResolveTask).Where(t=>t.OperationId is null||t.CheckIds.Length>0||CanReadSetting(t.OperationId)).ToArray();
 public static IReadOnlyDictionary<string,int> ReadConfig(byte[] bytes,WorkflowCatalogue workflows)
 {
  if(bytes.Length>64*1024)throw new ArgumentException("Diagnostic config must not exceed 64 KiB.");
  var known=DiagnosticTasks(workflows).ToDictionary(t=>t.Id,t=>t.Id,StringComparer.OrdinalIgnoreCase);
  known.Add("longfilepaths","control.cfg-win-007");known.Add("showfileextensions","windows.file-extensions");
  var result=new Dictionary<string,int>(StringComparer.Ordinal);
  foreach(var raw in new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF').Split('\n'))
  {
   var line=raw.Trim();if(line.Length==0||line.StartsWith('#')||line.StartsWith(';'))continue;
   var pair=line.Split('=');
   if(pair.Length!=2||!known.TryGetValue(pair[0].Trim(),out var id))throw new ArgumentException("Unknown diagnostic config item: "+line);
   var value=pair[1].Trim();
   if(value=="1")throw new ArgumentException("Value 1 requests a change and is refused in diagnostic mode. Use 0 to skip or 2 to report.");
   if(value is not ("0" or "2")||!result.TryAdd(id,value=="2"?2:0))throw new ArgumentException("Duplicate item or invalid diagnostic value: "+line);
  }
  return result;
 }
 public async Task<SilentRunResult> RunAsync(SilentRunOptions options,IReadOnlyDictionary<string,JsonElement> inputs,CancellationToken ct)
 {
  _=SilentRunOptions.Parse(["--silent","--preset",options.Preset]);
  foreach(var pair in inputs){LocalProbeCatalogue.ValidateInput(pair.Key,pair.Value);session.SetInputs(pair.Key,pair.Value);}
  var configuration=options.ConfigFile is null?null:ReadConfig(SilentRunOptions.ReadFile(options.ConfigFile),session.Workflows);
  var started=clock.GetUtcNow();var selected=new List<ReviewSelection>();var skipped=new Dictionary<string,SilentTaskResult>(StringComparer.Ordinal);
  var tasks=DiagnosticTasks(session.Workflows).Select(t=>(Task:t,Origin:new WorkflowOrigin("diagnostics","all"))).ToArray();
  foreach(var item in tasks)
  {
   var task=item.Task;string? status=null,reason=null;
   if(configuration is not null&&configuration.GetValueOrDefault(task.Id)==0){status="Excluded by config";reason="Value 0 or an omitted item: no check was requested.";}
   else if(Exclude(task,options.Preset)){status="Excluded timed test";reason="Fast preset excludes this deliberate timed performance test.";}
   else if(LocalProbeValues.IsRegistered(task.Id))
   {
    var probe=LocalProbeCatalogue.Resolve(task.Id);
    try{LocalProbeCatalogue.ValidateInput(task.Id,session.Inputs(task.Id));}
    catch(CatalogueValidationException e){status="Needs input";reason=e.Message+" Supply this check's inputs with --inputs.";}
    if(status is null&&probe.Privilege==PrivilegeRequirement.Administrator&&!session.Context.IsElevated){status="Requires administrator";reason="No elevation was requested. This check needs an already elevated process.";}
   }
   else if(task.CheckIds.Length==0&&!CanReadSetting(task.OperationId))
   {status=task.OperationId is null?"Manual verification":"Requires approval";reason=task.OperationId is null?"This task needs the engineer's manual verification.":"Configure and approve this change in the interactive toolkit. Silent mode cannot apply changes or deployments.";}
   if(status is not null)skipped.Add(task.Id,new(task.Id,task.Title,status,"Not checked","Not supplied",reason!,"Task catalogue"));
   else selected.Add(new(task.Id,item.Origin,false));
  }
  var earlierCaptures=session.Captures.Select(c=>c.Id).ToHashSet();
  var review=await reviews.CheckSilentAsync(selected,ct);
  try
  {
   var rows=review.Rows.ToDictionary(r=>r.Task.Id,r=>new SilentTaskResult(r.Task.Id,r.Task.Title,
    r.Before.Any(o=>o.Coverage is {Complete:false} or {HasMore:true})?"Unknown":ActionPreviewFormatter.StatusLabel(r.Preview.Status),r.Preview.Current,r.Preview.Expected,r.Preview.Explanation,r.Preview.Source),StringComparer.Ordinal);
   // A capture enters the session only after both capture and selection evidence save.
   // Direct setting reads must not hide an earlier capture/persistence failure.
   var evidenceComplete=session.Captures.Count(c=>!earlierCaptures.Contains(c.Id))==selected.Count;
   if(!evidenceComplete)
   {
    foreach(var item in selected)rows[item.TaskId]=rows[item.TaskId] with{Status="Unknown",Reason="Capture/evidence incomplete for this run. Observed values do not establish complete saved evidence. "+rows[item.TaskId].Reason};
    await reviews.AppendAsync(review,"evidence-error","One or more selected captures could not be collected or persisted. Exit 1; the run is incomplete.",ct);
   }
   foreach(var pair in skipped){rows.Add(pair.Key,pair.Value);await reviews.AppendAsync(review,"skip",pair.Value.TaskId+"; "+pair.Value.Status+"; "+pair.Value.Reason,ct);}
   var ordered=tasks.Select(t=>rows[t.Task.Id]).ToArray();
   var incomplete=ordered.Any(r=>r.Status is not ("Already correct" or "Different" or "Missing" or "Observed" or "Not applicable" or "Excluded timed test" or "Excluded by config"));
   var exit=!evidenceComplete?1:incomplete?2:ordered.Any(r=>r.Status is "Different" or "Missing")?3:0;
   var summary=new SilentRunSummary(1,review.RunId,options.Preset,started,clock.GetUtcNow(),true,evidenceComplete,exit,review.FilePath,ordered);
   var json=Path.ChangeExtension(review.FilePath,"json");var html=Path.ChangeExtension(review.FilePath,"html");
   await reviews.AppendAsync(review,"silent",$"Preset={options.Preset}; Check only; no changes, deployments, UAC or manual review confirmations. {ordered.Length} diagnostic task results. Config={options.ConfigFile??"All diagnostics"}; Excluded timed tests={ordered.Count(t=>t.Status=="Excluded timed test")}. Short checks and ordinary provider timeouts remain enabled.",ct);
   await WriteNewAsync(html,Html(summary),ct);
   await WriteNewAsync(json,JsonSerializer.Serialize(summary,new JsonSerializerOptions(StrictJson.Options){WriteIndented=true}),ct);
   await reviews.AppendAsync(review,"summary",$"Exit={exit}; JSON={json}; HTML={html}; completed checks do not certify manual tasks or deployments.",ct);
   await reviews.CompleteChecksAsync(review,ct);
   return new(summary,json,html);
  }
  catch
  {
   try{await reviews.AppendAsync(review,"error","Silent reporting failed. Inspect this log; do not treat this run as complete.",CancellationToken.None);}finally{await reviews.CancelAsync(review,CancellationToken.None);}
   throw;
  }
 }
 static bool CanReadSetting(string? operation)=>operation is not null&&(FixedSettingOperation.Definitions.Any(d=>d.OperationId==operation)||operation.StartsWith("settings.uk-",StringComparison.Ordinal)||operation=="apps.remove-consumer-copilot");
 static async Task WriteNewAsync(string path,string text,CancellationToken ct)
 {
  EvidenceStore.RefuseReparsePath(path);
  await using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read,4096,FileOptions.Asynchronous);
  await stream.WriteAsync(new UTF8Encoding(false,true).GetBytes(text),ct);await stream.FlushAsync(ct);stream.Flush(true);
 }
 static string Html(SilentRunSummary summary)
 {
  static string E(string value)=>WebUtility.HtmlEncode(value);
  var text=new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\"><title>Toolkit check report</title><style>body{font:15px Segoe UI,system-ui;margin:2rem}table{border-collapse:collapse;width:100%}td,th{border:1px solid #ccc;padding:.6rem;text-align:left;vertical-align:top;white-space:pre-wrap;overflow-wrap:anywhere}</style></head><body><h1>Device checks</h1>");
  text.Append("<p>Check only · ").Append(E(summary.Preset)).Append(" · ").Append(E(summary.StartedAt.ToString("O"))).Append(" · Exit ").Append(summary.ExitCode).Append(" · Evidence complete: ").Append(summary.EvidenceComplete).Append("</p><p>No changes or deployments were applied. Missing, blocked and manual tasks remain incomplete. This local report may contain device and network details.</p><table><tr><th>Task</th><th>Status</th><th>Current</th><th>Expected</th><th>Details / source</th></tr>");
  foreach(var r in summary.Tasks)text.Append("<tr><td>").Append(E(r.Title+"\n"+r.TaskId)).Append("</td><td>").Append(E(r.Status)).Append("</td><td>").Append(E(r.Current)).Append("</td><td>").Append(E(r.Expected)).Append("</td><td>").Append(E(r.Reason+"\n"+r.Source)).Append("</td></tr>");
  return text.Append("</table></body></html>").ToString();
 }
}
