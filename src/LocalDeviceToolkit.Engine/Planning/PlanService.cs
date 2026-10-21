using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Catalogue;
using System.Text.Json;
namespace LocalDeviceToolkit.Engine.Planning;
public sealed class OperationRegistry
{
 readonly WorkflowCatalogue workflows;
 readonly Dictionary<string,ILocalOperation> items;
 public OperationRegistry(WorkflowCatalogue workflows,IEnumerable<ILocalOperation> operations)
 {this.workflows=workflows;items=operations.ToDictionary(o=>o.Id,StringComparer.Ordinal);}
 public ILocalOperation Find(string id)=>items.TryGetValue(id,out var operation)?operation:throw new PlanValidationException("Unregistered operation: "+id);
 public void Validate(ActionRequest request)
 {
  if(request is null||request.Origin is null||request.Parameters.ValueKind!=JsonValueKind.Object)throw new PlanValidationException("A registered task with typed parameters is required.");
  try
  {
   var task=workflows.ResolveWorkflowTask(request.Origin.WorkflowId,request.Origin.StageId,request.TaskId);
   if(task.OperationId!=request.OperationId)throw new PlanValidationException("The operation does not belong to this task.");
   using var parsed=StrictJson.Parse(System.Text.Encoding.UTF8.GetBytes(request.Parameters.GetRawText()),1024*1024);
   Find(request.OperationId).ValidateParameters(parsed.RootElement);
  }
  catch(CatalogueValidationException e){throw new PlanValidationException(e.Message);}
  catch(Exception e) when(e is JsonException or InvalidDataException or InvalidOperationException or ArgumentException){throw new PlanValidationException("Task parameters are invalid.");}
 }
 public string Key(DeviceContext context,ActionRequest request)
 {
  Validate(request);var target=Find(request.OperationId).GetTargetKey(context,request.Parameters);
  if(string.IsNullOrWhiteSpace(target)||target.Length>256||target.Any(char.IsControl))throw new PlanValidationException("Invalid logical change target.");
  return target;
 }
}
public static class ObservationFingerprint
{
 static object? Value(ProbeObservation o)
 {
  if(o.CheckId=="repair.review-state"&&o.ControlId is "diagnostics.refresh-dns-cache" or "diagnostics.update-defender-signatures"&&o.Value is {} raw)
  {var s=StrictJson.Read<Diagnostics.RepairSnapshot>(StrictJson.Bytes(raw),65536);if(s.AuthorizationDigest.Length!=64||!s.AuthorizationDigest.All(Uri.IsHexDigit)||s.Details.Count>32)throw new PlanValidationException("Invalid diagnostic authorization state.");return new{s.AuthorizationDigest,s.CanAct,s.Reason};}
  return o.Value;
 }
 public static string Of(IReadOnlyList<ProbeObservation> observations)=>CanonicalJson.Digest(observations.OrderBy(o=>o.ControlId,StringComparer.Ordinal).ThenBy(o=>o.CheckId,StringComparer.Ordinal).Select(o=>new{o.ControlId,o.CheckId,o.State,Value=Value(o),o.Source,o.Reason}).ToArray());
}
public sealed class DeploymentQueue(DeviceContext context,OperationRegistry registry)
{
 readonly Dictionary<string,ActionRequest> requests=new(StringComparer.Ordinal);
 public IReadOnlyList<ActionRequest> Requests=>Array.AsReadOnly(requests.Values.Select(Clone).ToArray());
 public long Revision {get;private set;}
 static ActionRequest Clone(ActionRequest request)=>request with { Parameters=request.Parameters.Clone() };
 public QueueAddResult Add(ActionRequest request)
 {
  var key=registry.Key(context,request);
  if(requests.TryGetValue(key,out var existing))return CanonicalJson.Serialize(existing.Parameters)==CanonicalJson.Serialize(request.Parameters)?QueueAddResult.AlreadyQueued:QueueAddResult.ConflictingParameters;
  requests.Add(key,Clone(request));Revision++;return QueueAddResult.Added;
 }
 public void Replace(ActionRequest request)
 {
  var key=registry.Key(context,request);if(!requests.ContainsKey(key))throw new PlanValidationException("The conflicting queue target no longer exists.");
  requests[key]=Clone(request);Revision++;
 }
 public bool Remove(ActionRequest request)
 {
  // Removal must remain available when referenced media/profile files have disappeared. It grants no execution authority.
  var digest=CanonicalJson.Digest(request);var key=requests.Where(pair=>CanonicalJson.Digest(pair.Value)==digest).Select(pair=>pair.Key).FirstOrDefault();
  if(key is null||!requests.Remove(key))return false;Revision++;return true;
 }
 public void Clear(){if(requests.Count==0)return;requests.Clear();Revision++;}
}
public sealed class PlanService(OperationRegistry registry,TimeProvider clock)
{
 public static bool SupportedTarget(DeviceContext context)=>context.IsWorkstation&&context.OsVersion.Major==10&&context.OsVersion.Build>=22000&&context.Architecture=="x64";
 public async Task<ReviewedPlan> CreateAsync(DeviceContext context,LocalCatalogue catalogue,IReadOnlyList<ActionRequest> requests,CancellationToken ct)
 {
  if(!SupportedTarget(context))throw new PlanValidationException("Local deployments require a supported Windows 11 x64 device.");
  if(requests.Count is <1 or >100)throw new PlanValidationException("Select between 1 and 100 actions.");
  var actions=new List<PlannedAction>();var targets=new HashSet<string>(StringComparer.Ordinal);
  foreach(var submitted in requests)
  {
   ct.ThrowIfCancellationRequested();registry.Validate(submitted);
   var request=submitted with { Parameters=submitted.Parameters.Clone() };
   if(!targets.Add(registry.Key(context,request)))throw new PlanValidationException("The same logical target occurs more than once.");
   var operation=registry.Find(request.OperationId);var readiness=await operation.InspectAsync(context,request.Parameters,ct);
   if(!readiness.CanExecute)throw new PlanValidationException(readiness.Reason);
   var captured=await operation.CaptureBeforeAsync(context,request.Parameters,ct);
   ValidateBefore(captured,clock.GetUtcNow());
   var before=Array.AsReadOnly(captured.Select(o=>o with { Value=o.Value?.Clone() }).ToArray());
   actions.Add(new(Guid.NewGuid(),request,readiness,before,ObservationFingerprint.Of(before)));
  }
  var now=clock.GetUtcNow();var plan=new ReviewedPlan(Guid.NewGuid(),context,catalogue.Identity,now,now.AddMinutes(10),actions.AsReadOnly(),""){Lifetime=ReviewLifetime.Start()};
  plan=plan with { Digest=CanonicalJson.Digest(plan) };
  if(StrictJson.Bytes(plan).Length>900*1024)throw new PlanValidationException("This plan is too large for the worker protocol; review a smaller batch.");
  return plan;
 }
 public static void ValidateBefore(IReadOnlyList<ProbeObservation> before,DateTimeOffset now)
 {
  if(before is null||before.Count==0||before.Any(o=>o is null||o.State!=ObservationState.Known||(!o.Value.HasValue||o.Value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)||o.CapturedAt.Offset!=TimeSpan.Zero||o.CapturedAt>now.AddMinutes(2)||o.CapturedAt<now.AddMinutes(-10))||before.Select(o=>o.ControlId+"|"+o.CheckId).Distinct(StringComparer.Ordinal).Count()!=before.Count)
   throw new PlanValidationException("Complete, current before-state evidence is required.");
 }
 List<string> BaseReasons(ReviewedPlan plan,DeviceContext context,CatalogueIdentity catalogue,DateTimeOffset now)
 {
  var reasons=new List<string>();
  try
  {
   if(plan.Id==Guid.Empty||plan.Actions is null||plan.Actions.Count is <1 or >100||plan.Actions.Any(a=>a is null)||plan.Actions.Select(a=>a.Id).Distinct().Count()!=plan.Actions.Count)throw new PlanValidationException("Invalid action inventory.");
   if(plan.Lifetime is null||plan.Lifetime.IsExpired(Environment.TickCount64))reasons.Add("Review elapsed limit exceeded or historical plan lacks live review provenance.");
   if(plan.Digest!=CanonicalJson.Digest(plan with { Digest="" }))reasons.Add("The reviewed plan has changed.");
   if(plan.Context!=context)reasons.Add("The device or original-user context has changed.");
   if(!SupportedTarget(context))reasons.Add("This device is not a supported deployment target.");
   if(plan.Catalogue!=catalogue)reasons.Add("The standard or task catalogue has changed.");
   if(plan.CreatedAt.Offset!=TimeSpan.Zero||plan.ExpiresAt.Offset!=TimeSpan.Zero||plan.ExpiresAt!=plan.CreatedAt.AddMinutes(10)||now<plan.CreatedAt||now>=plan.ExpiresAt)reasons.Add("The reviewed plan has expired or the clock changed; review a new plan.");
   var keys=new HashSet<string>(StringComparer.Ordinal);
   foreach(var action in plan.Actions)
   {
    registry.Validate(action.Request);
    if(action.Id==Guid.Empty||!action.Readiness.CanExecute||action.Before is null||action.Before.Count==0||action.BeforeDigest!=ObservationFingerprint.Of(action.Before))reasons.Add("An action's reviewed before evidence is invalid.");
    if(!keys.Add(registry.Key(context,action.Request)))reasons.Add("The plan repeats a logical change target.");
   }
  }
  catch(Exception e) when(e is PlanValidationException or JsonException or InvalidOperationException or ArgumentException or NullReferenceException){reasons.Add("The reviewed plan is malformed or refers to an unavailable task.");}
  return reasons;
 }
 public PlanValidation Validate(ReviewedPlan plan,DeviceContext context,CatalogueIdentity catalogue,IReadOnlyDictionary<Guid,string> before,DateTimeOffset now)
 {
  var reasons=BaseReasons(plan,context,catalogue,now);
  if(reasons.Count!=0)return new(false,reasons.AsReadOnly());
  foreach(var action in plan.Actions)
   if(!before.TryGetValue(action.Id,out var digest)||digest!=action.BeforeDigest)reasons.Add("The observed before state has changed.");
  return new(reasons.Count==0,reasons.AsReadOnly());
 }
 public PlanValidation ValidateAction(ReviewedPlan plan,PlannedAction action,DeviceContext context,CatalogueIdentity catalogue,IReadOnlyList<ProbeObservation> before,DateTimeOffset now)
 {
  var reasons=BaseReasons(plan,context,catalogue,now);
  if(reasons.Count!=0)return new(false,reasons.AsReadOnly());
  if(!plan.Actions.Any(a=>a.Id==action.Id&&CanonicalJson.Digest(a)==CanonicalJson.Digest(action)))reasons.Add("The requested action is not the reviewed action.");
  try{ValidateBefore(before,now);if(ObservationFingerprint.Of(before)!=Settings.PowerPlanSequence.AtDispatch(plan,action).BeforeDigest)reasons.Add("The target changed after review.");}
  catch(PlanValidationException e){reasons.Add(e.Message);}
  return new(reasons.Count==0,reasons.AsReadOnly());
 }
}
