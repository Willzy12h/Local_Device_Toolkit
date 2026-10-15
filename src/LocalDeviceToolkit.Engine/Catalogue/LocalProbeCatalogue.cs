using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LocalDeviceToolkit.Core;
namespace LocalDeviceToolkit.Engine.Catalogue;

public sealed record LocalProbeDefinition(string Id,string Title,OperationScope Scope,PrivilegeRequirement Privilege,bool MayUseNetwork,string Disclosure,string InputSchema,string Scenario);
public static class LocalProbeCatalogue
{
 static readonly Lazy<IReadOnlyList<LocalProbeDefinition>> all=new(()=>
 {
  using var stream=typeof(LocalProbeCatalogue).Assembly.GetManifestResourceStream("catalogue/local-probes.json")??throw new CatalogueValidationException("Local probe catalogue is missing.");
  using var bytes=new MemoryStream();stream.CopyTo(bytes);var values=StrictJson.Read<LocalProbeDefinition[]>(bytes.ToArray());
  if(values.Length!=24||values.Select(x=>x.Id).Distinct(StringComparer.Ordinal).Count()!=24||values.Any(x=>!LocalProbeValues.IsRegistered(x.Id)||string.IsNullOrWhiteSpace(x.Title)||string.IsNullOrWhiteSpace(x.Disclosure)))throw new CatalogueValidationException("Invalid local check definitions.");
  return Array.AsReadOnly(values);
 });
 public static IReadOnlyList<LocalProbeDefinition> All=>all.Value;
 public static LocalProbeDefinition Resolve(string id)=>All.SingleOrDefault(p=>p.Id==id)??throw new CatalogueValidationException("Unknown local check.");
 public static void ValidateInput(string id,JsonElement value)
 {
  var definition=Resolve(id);
  if(value.ValueKind!=JsonValueKind.Object||System.Text.Encoding.UTF8.GetByteCount(value.GetRawText())>8192)throw new CatalogueValidationException("Check inputs must be a bounded object.");
  var keys=definition.InputSchema switch {"volume"=>new[]{"volumeId","requiredBytes"},"history"=>["count"],"dns"=>["hostname","queryType","adapterId"],"tcp"=>["host","port","adapterId"],"adapter"=>["adapterId"],"printer"=>["printerName","allowRemoteRead"],"incident"=>["symptom","fromUtc","toUtc","cursor"],"profile"=>["profileId"],_=>Array.Empty<string>()};
  if(value.EnumerateObject().Any(p=>!keys.Contains(p.Name,StringComparer.Ordinal)))throw new CatalogueValidationException("Unknown input field. Commands and unregistered options are refused.");
  string Text(string key)=>value.TryGetProperty(key,out var member)&&member.ValueKind==JsonValueKind.String?member.GetString()!:throw new CatalogueValidationException("Choose "+key+".");
  void Target(string text){if(text.Length is <1 or >253||text.Any(char.IsWhiteSpace)||text.Any(char.IsControl)||text.Contains('/')||text.Contains('\\')||text.Contains('@')||(!IPAddress.TryParse(text,out _)&&Uri.CheckHostName(text)==UriHostNameType.Unknown))throw new CatalogueValidationException("Choose one hostname or IP address.");}
  switch(definition.InputSchema)
  {
   case "volume":if(!Regex.IsMatch(Text("volumeId"),"^[A-Za-z]:$",RegexOptions.CultureInvariant))throw new CatalogueValidationException("Choose one local drive letter.");if(value.TryGetProperty("requiredBytes",out var required)&&!required.TryGetUInt64(out _))throw new CatalogueValidationException("Required space must be a non-negative byte count.");break;
   case "history":if(value.TryGetProperty("count",out var count)&&(!count.TryGetInt32(out var n)||n is <1 or >200))throw new CatalogueValidationException("Select 1 to 200 update records.");break;
   case "dns":Target(Text("hostname"));if(Text("queryType") is not ("A" or "AAAA" or "CNAME"))throw new CatalogueValidationException("Choose A, AAAA or CNAME.");break;
   case "tcp":Target(Text("host"));if(!value.TryGetProperty("port",out var port)||!port.TryGetInt32(out var number)||number is <1 or >65535)throw new CatalogueValidationException("Choose one port from 1 to 65535.");break;
   case "printer":var printer=Text("printerName");if(printer.Length is <1 or >256||printer.Any(char.IsControl))throw new CatalogueValidationException("Choose a literal printer name.");if(value.TryGetProperty("allowRemoteRead",out var remote)&&remote.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new CatalogueValidationException("Confirm remote access explicitly.");if(printer.StartsWith("\\\\",StringComparison.Ordinal)&&(!value.TryGetProperty("allowRemoteRead",out var allowed)||allowed.ValueKind!=JsonValueKind.True))throw new CatalogueValidationException("Confirm shared-printer access before collection.");break;
   case "incident":if(Text("symptom") is not ("network" or "performance" or "power" or "printing" or "updates" or "applications" or "crash"))throw new CatalogueValidationException("Choose a registered symptom.");if(!DateTimeOffset.TryParse(Text("fromUtc"),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out var from)||!DateTimeOffset.TryParse(Text("toUtc"),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out var to)||from.Offset!=TimeSpan.Zero||to.Offset!=TimeSpan.Zero||to<=from||to-from>TimeSpan.FromDays(7))throw new CatalogueValidationException("Choose a UTC incident window up to seven days.");if(value.TryGetProperty("cursor",out var cursor)&&cursor.ValueKind!=JsonValueKind.Null&&(cursor.ValueKind!=JsonValueKind.String||cursor.GetString()!.Length>2048))throw new CatalogueValidationException("Invalid event cursor.");break;
   case "profile":var profile=Text("profileId");if(profile.Length is <1 or >128||profile.Any(char.IsControl))throw new CatalogueValidationException("Choose an approved profile.");break;
  }
  if(value.TryGetProperty("adapterId",out var adapter)&&(adapter.ValueKind!=JsonValueKind.String||!Guid.TryParse(adapter.GetString(),out _)))throw new CatalogueValidationException("Choose a valid adapter identity.");
 }
 public static string TargetDigest(string id,JsonElement parameters,DeviceContext context)=>CanonicalJson.Digest(new {id,parameters,context.DeviceId,context.OriginalUserSid});
}
