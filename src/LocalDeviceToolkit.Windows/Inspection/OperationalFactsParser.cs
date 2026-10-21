using LocalDeviceToolkit.Core;
namespace LocalDeviceToolkit.Windows.Inspection;
public static class OperationalFactsParser
{
 public static LocalProbeResult Parse(ReadOnlyMemory<byte> bytes,string checkId,string targetDigest,DateTimeOffset started,DateTimeOffset ended)
 {
  try
  {
   var r=StrictJson.Read<LocalProbeResult>(bytes,256*1024);
   if(r.SchemaVersion!=2||r.CheckId!=checkId||r.TargetDigest!=targetDigest||targetDigest.Length!=64||!targetDigest.All(Uri.IsHexDigit)||r.CapturedAt.Offset!=TimeSpan.Zero||r.CapturedAt<started.AddSeconds(-2)||r.CapturedAt>ended.AddSeconds(2)||string.IsNullOrWhiteSpace(r.Source)||r.Source.Length>512||r.Source.Any(char.IsControl)||r.Reason.Length>2048||r.Coverage.ReturnedCount is <0 or >1000||r.Coverage.Limitations.Count>20||r.Coverage.Limitations.Any(s=>s is null||s.Length>2048)||r.Coverage.NextCursor?.Length>2048||r.Coverage.Complete&&r.Coverage.HasMore)throw new InvalidDataException();
   if(r.State==ObservationState.Known?(r.Value is null||!LocalProbeValues.Accepts(checkId,StrictJson.Value(r.Value))):(r.Value is not null||r.Coverage.Complete))throw new InvalidDataException();
   return r;
  }
  catch(Exception e) when(e is System.Text.Json.JsonException or InvalidDataException or ArgumentException or InvalidOperationException or NullReferenceException)
  {throw new InvalidDataException("The selected check returned invalid, unrequested or out-of-date evidence.");}
 }
}
