using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
namespace LocalDeviceToolkit.Engine.Reporting;
public sealed record ManualCheck(Guid Id,string ItemId,string TargetDigest,DeviceContext Context,ManualReviewOutcome Outcome,string Note,ExecutionActor Actor,DateTimeOffset At)
{
 [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? TargetReference{get;init;}
}
public sealed record HandoverResult(string State,IReadOnlyList<string> Outstanding,IReadOnlyList<string> Exceptions);
public static class HandoverService
{
 public static IReadOnlyDictionary<string,string> ManualItems{get;}=new Dictionary<string,string>
 {
  ["manual.display"]="External display",["manual.audio"]="Audio output",["manual.microphone"]="Microphone",["manual.camera"]="Camera",["manual.keyboard"]="Keyboard",["manual.touchpad"]="Touchpad",["manual.printer"]="Selected printer",["manual.vpn"]="VPN connectivity",["manual.signin"]="Application sign-in",["manual.backup"]="Backup verification",["manual.recovery"]="Recovery-key retrieval (reference only; never enter the key)"
 };
 public static HandoverResult Evaluate(ClientChecklist profile,DeviceContext context,IReadOnlyList<ProbeObservation> observations,IReadOnlyList<ManualCheck> manual,DateTimeOffset now,IReadOnlyDictionary<string,string>? manualTargets=null,bool hasUnresolvedActions=false,IReadOnlyDictionary<string,string>? localTargets=null)
 {
  ReferenceProfiles.Validate(profile);var outstanding=new List<string>();var exceptions=new List<string>();
  foreach(var id in profile.RequiredItems)
  {
   if(ManualItems.ContainsKey(id))
   {
    var check=manual.Where(m=>m.ItemId==id&&m.Context==context&&m.Actor.Sid==context.OriginalUserSid&&m.At<=now&&now-m.At<=TimeSpan.FromHours(24)).MaxBy(m=>m.At);
    if(check is null||manualTargets is null||!manualTargets.TryGetValue(id,out var target)||check.TargetDigest!=target||check.Outcome==ManualReviewOutcome.Failed){outstanding.Add(id);continue;}
    if(check.Outcome!=ManualReviewOutcome.Verified){exceptions.Add(id+": "+check.Outcome+" — "+check.Note);if(!profile.AllowReviewedExceptions)outstanding.Add(id);}
   }
   else
   {
    if(localTargets is null||!localTargets.TryGetValue(id,out var requested)){outstanding.Add(id);continue;}
    var o=observations.Where(o=>o.CheckId==id&&o.TargetDigest==requested).MaxBy(o=>o.CapturedAt);
    if(o is null||o.State!=ObservationState.Known||o.LocalSchemaVersion!=2||o.RuleVersion is null||o.Coverage is not {Complete:true,HasMore:false}||o.TargetDigest is null||o.Value is not {} raw||!LocalProbeValues.Accepts(id,raw)||o.CapturedAt>now||now-o.CapturedAt>TimeSpan.FromMinutes(10)||!profile.Expected.TryGetValue(id,out var expected)){outstanding.Add(id);continue;}
    var value=StrictJson.Read<LocalProbeValue>(StrictJson.Bytes(raw));if(expected.ValueKind!=System.Text.Json.JsonValueKind.String||value.Current!=expected.GetString())outstanding.Add(id);
   }
  }
  if(hasUnresolvedActions)outstanding.Add("Unresolved or uncertain actions");
  if(profile.RequiredItems.Count==0)outstanding.Add("Choose required checks for this client; an empty checklist cannot establish readiness.");
  return new(outstanding.Count>0?"Outstanding work":exceptions.Count>0?"Ready with reviewed exceptions":"Ready for handover",outstanding,exceptions);
 }
}
public sealed record ComparedValue(string CheckId,string TargetDigest,string State,string Before,string After);
public static class RunComparisonService
{
 public static IReadOnlyList<ComparedValue> Compare(DeviceCapture before,DeviceCapture after)
 {
  var result=new List<ComparedValue>();
  foreach(var id in before.Observations.Select(o=>o.CheckId).Union(after.Observations.Select(o=>o.CheckId),StringComparer.Ordinal))
  {
   var a=before.Observations.Where(o=>o.CheckId==id).ToArray();var b=after.Observations.Where(o=>o.CheckId==id).ToArray();
   bool comparable=before.Context==after.Context&&before.Catalogue==after.Catalogue&&a.Length==1&&b.Length==1&&a[0].LocalSchemaVersion==2&&b[0].LocalSchemaVersion==2&&a[0].RuleVersion is not null&&a[0].RuleVersion==b[0].RuleVersion&&a[0].TargetDigest is not null&&a[0].TargetDigest==b[0].TargetDigest&&a[0].State==ObservationState.Known&&b[0].State==ObservationState.Known&&a[0].Coverage is {Complete:true,HasMore:false}&&b[0].Coverage is {Complete:true,HasMore:false};
   var old=a.Length==1?a[0].Value?.GetRawText()??"Unknown":"No single observation";var current=b.Length==1?b[0].Value?.GetRawText()??"Unknown":"No single observation";
   result.Add(new(id,b.FirstOrDefault()?.TargetDigest??"Unknown",!comparable?"Not comparable":old==current?"Unchanged":"Changed",old,current));
  }
  return result;
 }
}
