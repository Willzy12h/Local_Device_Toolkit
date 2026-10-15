using System.Text.Json;
namespace LocalDeviceToolkit.Core;
public sealed record InstalledApplication(string Identity,string Name,string? Version,string? Publisher,OperationScope Scope);
public static class ProbeValues
{
 static readonly HashSet<string> Booleans=new(StringComparer.Ordinal)
 {
  "copilot.consumer.present","secureboot.enabled","tpm.ready","bitlocker.protected","codeintegrity.enforced","antivirus.active","extensions.visible","paths.enabled","faststartup.enabled","keyboard.uk","laps.configured","onedrive.configured","onedrive.filesondemand.configured","edge.configured","store.installed","storageSense.configured",
  "policy.longpaths","policy.extensions","policy.faststartup","policy.acsleep","policy.timezone","policy.region","policy.copilot"
 };
 public static bool IsRegistered(string id)=>Booleans.Contains(id)||id is "os.version" or "os.architecture" or "region.culture" or "timezone.id" or "power.acsleep.seconds" or "apps.inventory" or "antivirus.providers";
 public static bool Accepts(string id,JsonElement value)
 {
  if(Booleans.Contains(id))return value.ValueKind is JsonValueKind.True or JsonValueKind.False;
  if(id=="power.acsleep.seconds")return value.ValueKind==JsonValueKind.Number&&value.TryGetUInt32(out _);
  if(id is "os.version" or "os.architecture" or "region.culture" or "timezone.id")
  {
   if(value.ValueKind!=JsonValueKind.String||value.GetString() is not string text||text.Length is <1 or >512)return false;
   return id!="os.version"||System.Version.TryParse(text,out _);
  }
  if(id=="antivirus.providers")return value.ValueKind==JsonValueKind.Array&&value.GetArrayLength()<=100&&value.EnumerateArray().All(v=>v.ValueKind==JsonValueKind.String&&v.GetString()!.Length<=512);
  if(id=="apps.inventory")
  {
   if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()>1000)return false;
   try
   {
    var rows=StrictJson.Read<InstalledApplication[]>(System.Text.Encoding.UTF8.GetBytes(value.GetRawText()));
    return rows.All(row=>row is not null&&!string.IsNullOrWhiteSpace(row.Identity)&&row.Identity.Length<=512&&!string.IsNullOrWhiteSpace(row.Name)&&row.Name.Length<=512&&(row.Version?.Length??0)<=512&&(row.Publisher?.Length??0)<=512);
   }
   catch(Exception e) when(e is JsonException or InvalidDataException){return false;}
  }
  return false;
 }
}
