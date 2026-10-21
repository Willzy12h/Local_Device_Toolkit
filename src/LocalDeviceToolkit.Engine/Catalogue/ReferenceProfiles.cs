using LocalDeviceToolkit.Core;
using System.Text.Json;
namespace LocalDeviceToolkit.Engine.Catalogue;
public enum SupportState { Supported,OutOfSupport,Unknown }
public sealed record SupportAssessment(SupportState State,DateOnly? EndsOn,DateOnly ReferenceDate,string Reason);
public sealed record SupportRelease(string Edition,string Release,DateOnly EndsOn);
public sealed record SupportReference(DateOnly RetrievedOn,string Source,IReadOnlyList<SupportRelease> Releases);
public sealed record ClientChecklist(string Id,int Version,string Label,IReadOnlyDictionary<string,JsonElement> Expected,IReadOnlyList<string> RequiredItems,bool AllowReviewedExceptions);
public static class ReferenceProfiles
{
 public static SupportAssessment EvaluateSupport(string edition,string release,DateOnly asOf)
 {
  using var stream=typeof(ReferenceProfiles).Assembly.GetManifestResourceStream("catalogue/windows-support.json")??throw new CatalogueValidationException("Support reference is missing.");using var bytes=new MemoryStream();stream.CopyTo(bytes);return EvaluateSupport(StrictJson.Read<SupportReference>(bytes.ToArray()),edition,release,asOf);
 }
 public static SupportAssessment EvaluateSupport(SupportReference reference,string edition,string release,DateOnly asOf)
 {
  var match=reference.Releases.Where(r=>r.Edition==edition&&r.Release==release).ToArray();
  if(asOf<reference.RetrievedOn||asOf.DayNumber-reference.RetrievedOn.DayNumber>30||match.Length!=1)return new(SupportState.Unknown,null,reference.RetrievedOn,"Support reference is missing, stale or does not cover this exact edition/release. Client approval is separate.");
  return new(asOf<=match[0].EndsOn?SupportState.Supported:SupportState.OutOfSupport,match[0].EndsOn,reference.RetrievedOn,"Dated Microsoft lifecycle reference; this is not an upgrade recommendation or client approval.");
 }
 public static void Validate(ClientChecklist profile)
 {
  if(string.IsNullOrWhiteSpace(profile.Id)||profile.Id.Length>128||profile.Version<1||string.IsNullOrWhiteSpace(profile.Label)||profile.Label.Length>256||profile.Expected.Count>100||profile.RequiredItems.Count>100||profile.RequiredItems.Distinct(StringComparer.Ordinal).Count()!=profile.RequiredItems.Count)throw new CatalogueValidationException("Invalid client checklist.");
  foreach(var id in profile.RequiredItems)if(!LocalProbeValues.IsRegistered(id)&&!Reporting.HandoverService.ManualItems.ContainsKey(id))throw new CatalogueValidationException("Unregistered handover requirement.");
  foreach(var item in profile.Expected)if(!LocalProbeValues.IsRegistered(item.Key)&&!Enum.GetValues<Settings.PowerSlot>().Any(s=>Settings.PowerTimeoutOperation.OperationId(s)==item.Key))throw new CatalogueValidationException("Unregistered expected-value key. Profiles cannot declare operations or execution permission.");
  foreach(var value in profile.Expected.Values)if(value.ValueKind!=JsonValueKind.String||value.GetString()!.Length>8192)throw new CatalogueValidationException("Expected values must be bounded text.");
 }
}
