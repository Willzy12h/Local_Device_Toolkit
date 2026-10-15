using System.Text.Json;
namespace LocalDeviceToolkit.Core;

public sealed record LocalProbeValue(string Current,string? Expected,string Status,IReadOnlyDictionary<string,string> Details);
public sealed record ProbeCoverage(bool Complete,bool HasMore,int ReturnedCount,string? NextCursor,IReadOnlyList<string> Limitations);
public sealed record LocalProbeResult(int SchemaVersion,string CheckId,string TargetDigest,ObservationState State,LocalProbeValue? Value,string Source,DateTimeOffset CapturedAt,ProbeCoverage Coverage,string Reason);
public static class LocalProbeValues
{
 static readonly IReadOnlyDictionary<string,string[]> fields=new Dictionary<string,string[]>(StringComparer.Ordinal)
 {
  ["local.os.support"]=["edition","release","build","architecture","supportEnd","referenceDate","clientTarget"],
  ["local.identity.context"]=["joinState","management","originalUser"],
  ["local.restart.reasons"]=["reasons","coverage"], ["local.update.history"]=["updates","coverage"],
  ["local.recovery.winre"]=["winreStatus","winreLocation"],
  ["local.bitlocker.state"]=["volume","conversion","protection","suspended","protectors","recoveryVerification"],
  ["local.storage.capacity"]=["volume","freeBytes","totalBytes","requiredBytes"],
  ["local.storage.health"]=["volume","health","healthProvider"],
  ["local.hardware.identity"]=["manufacturer","model","biosVersion","biosDate"],
  ["local.hardware.problems"]=["devices","coverage"], ["local.power.capabilities"]=["sleepStates"], ["local.battery.capacity"]=["batteries"],
  ["local.defender.protection"]=["mode","protectionEnabled"], ["local.defender.signatures"]=["signatureVersion","signatureTime","sourceOrder"],
  ["local.network.adapter"]=["adapter","address","route","resolvers","vpn"],
  ["local.network.dns"]=["hostname","queryType","answers","adapter","resolvers","vpn"],
  ["local.network.tcp"]=["endpoint","port","connected","adapter","route"],
  ["local.proxy.user"]=["proxy","autoDetect","pacUrl","bypass"], ["local.proxy.winhttp"]=["proxy","bypass"],
  ["local.time.status"]=["serviceState","source","lastSync"], ["local.print.spooler"]=["serviceState","startupMode"],
  ["local.print.queue"]=["printer","jobs","coverage"],
  ["local.events.incident"]=["eventSummary","eventRecords","channelGeneration","fromUtc","toUtc"],
  ["local.app.verification"]=["profileId","packageChecked","installed","configured","signedIn","licensed","restart","verified"]
 };
 public static IReadOnlyCollection<string> Ids=>fields.Keys.ToArray();
 public static bool IsRegistered(string id)=>fields.ContainsKey(id);
 public static bool Accepts(string id,JsonElement value)
 {
  if(!fields.TryGetValue(id,out var allowed))return false;
  try
  {
   var parsed=StrictJson.Read<LocalProbeValue>(System.Text.Encoding.UTF8.GetBytes(value.GetRawText()),256*1024);
   return parsed.Current.Length is >0 and <=8192&&parsed.Expected?.Length is not >8192&&parsed.Status is "Observed" or "Current" or "Different" or "Missing" or "Unknown" or "Not applicable"&&parsed.Details.Count<=allowed.Length&&parsed.Details.All(p=>allowed.Contains(p.Key,StringComparer.Ordinal)&&p.Value is not null&&p.Value.Length<=128*1024)&&(!(parsed.Status is "Current" or "Different")||parsed.Expected is not null);
  }
  catch(Exception e) when(e is JsonException or InvalidDataException or InvalidOperationException or ArgumentException){return false;}
 }
 public static string StatusLabel(ObservationState state,LocalProbeValue? value)=>state==ObservationState.Known?value?.Status??"Observed":state==ObservationState.NotSupported?"Not applicable":"Unknown";
}
