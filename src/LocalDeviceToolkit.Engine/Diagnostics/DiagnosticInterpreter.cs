using LocalDeviceToolkit.Core;
namespace LocalDeviceToolkit.Engine.Diagnostics;
public sealed record DiagnosticFinding(string Id,string RuleVersion,string Observed,string Why,string NextStep,string Confidence,bool SecurityRelevant,bool RequiredUnknown,IReadOnlyList<Guid> CaptureIds,string? SuggestedOperationId);
public sealed record DiagnosticSummary(IReadOnlyList<DiagnosticFinding> Expanded,IReadOnlyList<DiagnosticFinding> Remaining,IReadOnlyList<DiagnosticFinding> Signposts);
public sealed record IncidentGroup(string Channel,string Provider,int EventId,string Resource,int Count,DateTimeOffset FirstUtc,DateTimeOffset LastUtc,IReadOnlyList<long> RecordIds);
public static class IncidentGrouper
{
 public static IReadOnlyList<IncidentGroup> Group(IEnumerable<EventEvidence> events)=>events
  .GroupBy(e=>new {e.Channel,e.Provider,e.EventId,e.Resource,Signature=CanonicalJson.Digest(e.Fields),Window=e.TimeUtc.UtcTicks/TimeSpan.FromMinutes(15).Ticks})
  .OrderByDescending(g=>g.Max(e=>e.TimeUtc))
  .ThenBy(g=>g.Key.Channel,StringComparer.Ordinal).ThenBy(g=>g.Key.Provider,StringComparer.Ordinal).ThenBy(g=>g.Key.EventId)
  .ThenBy(g=>g.Key.Resource,StringComparer.Ordinal).ThenBy(g=>g.Key.Signature,StringComparer.Ordinal)
  .Select(g=>new IncidentGroup(g.Key.Channel,g.Key.Provider,g.Key.EventId,g.Key.Resource,g.Count(),g.Min(e=>e.TimeUtc),g.Max(e=>e.TimeUtc),g.OrderBy(e=>e.TimeUtc).ThenBy(e=>e.RecordId).Select(e=>e.RecordId).ToArray())).ToArray();
}
public static class DiagnosticInterpreter
{
 public static DiagnosticSummary Summarize(IEnumerable<DiagnosticFinding> findings)
 {
  var all=findings.OrderByDescending(f=>f.SecurityRelevant).ThenByDescending(f=>f.RequiredUnknown).ToArray();return new(all.Take(3).ToArray(),all.Skip(3).ToArray(),all.Where(f=>f.SecurityRelevant||f.RequiredUnknown).ToArray());
 }
 public static IReadOnlyList<DiagnosticFinding> Interpret(ProbeObservation observation)
 {
  var findings=new List<DiagnosticFinding>();var captureIds=observation.EvidenceCaptureId is Guid id?new[]{id}:Array.Empty<Guid>();
  var value=observation.State==ObservationState.Known&&observation.Value is {} raw&&LocalProbeValues.Accepts(observation.CheckId,raw)?StrictJson.Read<LocalProbeValue>(StrictJson.Bytes(raw)):null;
  bool security=observation.CheckId is "local.defender.protection" or "local.defender.signatures" or "local.bitlocker.state";
  if(value is null||observation.State!=ObservationState.Known||observation.Coverage is not {Complete:true,HasMore:false}||value?.Status=="Unknown")
   findings.Add(new(observation.CheckId+"/coverage","2026-10-15","Collection is incomplete or unknown.",string.Join("; ",observation.Coverage?.Limitations??[observation.Reason]),"Resolve missing evidence before declaring the required check complete.","Not established",security,true,captureIds,null));
  if(security&&value is not null)findings.Add(new(observation.CheckId+"/security","2026-10-15",value.Current,"Local security configuration is an observation; client policy, other providers and required manual checks remain separate.","Review this security evidence and the approved client expectation.","Confirmed observation",true,false,captureIds,null));
  if(observation.CheckId=="local.events.incident"&&value?.Details.TryGetValue("eventRecords",out var records)==true)
  {
   var events=StrictJson.Read<EventEvidence[]>(System.Text.Encoding.UTF8.GetBytes(records),120000);
   foreach(var group in IncidentGrouper.Group(events))
   {
    var finding=ExplainEvent(events.First(e=>e.Channel==group.Channel&&e.RecordId==group.RecordIds[0]));
    findings.Add(finding with{Observed=finding.Observed+$" Group: {group.Count} events; first UTC {group.FirstUtc:O}; last UTC {group.LastUtc:O}.",CaptureIds=captureIds});
   }
  }
  return findings;
 }
 public static DiagnosticFinding ExplainEvent(EventEvidence e)
 {
  bool shutdown=e.Provider=="Microsoft-Windows-Kernel-Power"&&e.EventId==41;
  bool documented=e.Provider=="Microsoft-Windows-DistributedCOM"&&e.EventId==10016&&KnownDcom(e.Fields);
  return new(e.Channel+":"+e.RecordId,"2026-10-15",shutdown?"Windows recorded an unclean shutdown.":documented?"Documented DCOM permission pattern observed.":e.Provider+" · event "+e.EventId+" · "+e.Resource,
   shutdown?"The cause is not established by event 41.":documented?"Microsoft documents this exact pattern as expected; it does not establish the incident cause.":"A matching event is an observation. Its relationship to the reported symptom is not established.",
   documented?"Keep for reference. Do not change DCOM permissions.":"Compare the time and resource with the reported symptom; review related evidence.","Confirmed observation",false,false,[],null);
 }
 static bool KnownDcom(IReadOnlyDictionary<string,string> f)
 {
  string Get(string key)=>f.GetValueOrDefault(key,"").Trim('{','}').ToLowerInvariant();
  var pair=Get("clsid")+"/"+Get("appid");var sid=Get("sid");
  return pair switch
  {
   "d63b10c5-bb46-4990-a94f-e40b9d520160/9ca88ee3-acb7-47c8-afc4-ab702511c276"=>sid=="s-1-5-18",
   "260eb9de-5cbe-4bff-a99a-3710af55bf1e/260eb9de-5cbe-4bff-a99a-3710af55bf1e"=>true,
   "c2f03a33-21f5-47fa-b4bb-156362a2f239/316cded5-e4ae-4b15-9113-7055d84dcc97"=>sid=="s-1-5-19",
   "6b3b8d23-fa8d-40b9-8dbd-b950333e2c52/4839ddb7-58c2-48f5-8283-e1d1807d0d7d"=>sid=="s-1-5-19",_=>false
  };
 }
}
