using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
using System.Text.Json;
namespace LocalDeviceToolkit.Windows.Inspection;
public sealed record WindowsFact(string CheckId,ObservationState State,JsonElement? Value,string Source,DateTimeOffset CapturedAt,string Reason);
public sealed record WindowsFactsResponse(int SchemaVersion,IReadOnlyList<WindowsFact> Facts);
public interface IWindowsFactsSource { Task<IReadOnlyList<WindowsFact>> ReadAsync(IReadOnlyList<string> checkIds,CancellationToken ct); }
public interface IExecutionActorProvider { Task<ExecutionActor> GetActorAsync(CancellationToken ct); }
public sealed class WindowsDeviceInspector(IExecutionContextProvider contexts,IExecutionActorProvider actors,IWindowsFactsSource source,LocalCatalogue catalogue,TimeProvider clock):IDeviceInspector
{
 public async Task<DeviceCapture> CaptureAsync(CaptureRequest request,CancellationToken ct)
 {
  var context=await contexts.GetCurrentAsync(ct);var actor=await actors.GetActorAsync(ct);
  if(request.ControlIds.Count==0||request.ControlIds.Count>100)throw new ArgumentException("Select a bounded set of controls.");
  var selected=request.ControlIds.Distinct(StringComparer.Ordinal).Select(id=>catalogue.Controls.SingleOrDefault(c=>c.Id==id)??throw new CatalogueValidationException("Unknown control: "+id)).ToArray();
  var checks=selected.SelectMany(c=>c.CheckIds).Distinct(StringComparer.Ordinal).ToArray();
  IReadOnlyList<WindowsFact> facts=[];
  if(checks.Length>0)
  {
   try{facts=await source.ReadAsync(checks,ct);}
   catch(OperationCanceledException){throw;}
   catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
   {facts=[];}
  }
  var now=clock.GetUtcNow();var observations=new List<ProbeObservation>();
  foreach(var control in selected)
  {
   if(control.CheckIds.Length==0){observations.Add(new(control.Id,"manual",ObservationState.Manual,null,"Build standard",now,control.ManualInstructions));continue;}
   foreach(var check in control.CheckIds)
   {
    var matches=facts.Where(f=>f.CheckId==check).ToArray();
    if(matches.Length!=1||matches[0].CapturedAt.Offset!=TimeSpan.Zero||matches[0].CapturedAt>now.AddMinutes(2)||matches[0].CapturedAt<now.AddMinutes(-10)||(matches[0].State==ObservationState.Known&&(!matches[0].Value.HasValue||!ProbeValues.Accepts(check,matches[0].Value!.Value))))
     observations.Add(new(control.Id,check,ObservationState.Unavailable,null,"Windows inspection",now,"The check returned missing, invalid or ambiguous evidence."));
    else
    {var fact=matches[0];observations.Add(new(control.Id,check,fact.State,fact.Value?.Clone(),fact.Source,fact.CapturedAt,fact.Reason));}
   }
  }
  return new(Guid.NewGuid(),context,catalogue.Identity,request.Origin,actor,now,observations.AsReadOnly());
 }
}
public enum PolicyInspectionState { NoObservedConflict,ConflictObserved,Unavailable }
public sealed record PolicyInspection(PolicyInspectionState State,string Reason);
public sealed class PolicyProbe(IWindowsFactsSource source)
{
 public async Task<PolicyInspection> InspectAsync(string operationId,CancellationToken ct)
 {
  var check=operationId switch
  {
   "settings.long-paths"=>"policy.longpaths","settings.show-file-extensions"=>"policy.extensions","settings.disable-fast-startup"=>"policy.faststartup","settings.no-ac-sleep"=>"policy.acsleep","settings.uk-time-zone"=>"policy.timezone","settings.uk-region-keyboard"=>"policy.region","apps.remove-consumer-copilot"=>"policy.copilot",_=>null
  };
  if(check is null)return new(PolicyInspectionState.Unavailable,"No policy inspection is registered for this operation.");
  var facts=await source.ReadAsync([check],ct);var values=facts.Where(f=>f.CheckId==check).ToArray();
  if(values.Length!=1||values[0].State!=ObservationState.Known||values[0].Value is not JsonElement value||value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))return new(PolicyInspectionState.Unavailable,"Required policy inspection is unavailable.");
  return value.GetBoolean()?new(PolicyInspectionState.ConflictObserved,"A policy source controls this setting; use the managed workflow."):new(PolicyInspectionState.NoObservedConflict,"No conflict was observed in the checked policy sources; this does not prove the device is unmanaged.");
 }
}
