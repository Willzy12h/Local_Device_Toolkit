using LocalDeviceToolkit.Core;
using System.Text;
using System.Text.Json;
namespace LocalDeviceToolkit.Engine.Evidence;
public sealed record EvidenceRecord(Guid Id,string Kind,int SchemaVersion,DeviceContext Context,DateTimeOffset RecordedAt,JsonElement Data,string Digest)
{
 public static EvidenceRecord Create(Guid id,string kind,DeviceContext context,DateTimeOffset at,JsonElement data)
 {
  var record=new EvidenceRecord(id,kind,1,context,at,data.Clone(),"");
  return record with { Digest=CanonicalJson.Digest(record) };
 }
 public static EvidenceRecord Capture(DeviceCapture capture)=>Create(capture.Id,"capture",capture.Context,capture.CapturedAt,StrictJson.Value(capture));
}
public sealed class EvidenceValidationException:Exception
{
 public string Code { get; }
 public EvidenceValidationException(string message):this("MalformedRecord",message){}
 public EvidenceValidationException(string code,string message):base(message){Code=code;}
}
public sealed record HistoryDiagnostic(string FileName,string Code,string Message);
public sealed record HistoryPage(IReadOnlyList<EvidenceRecord> Entries,IReadOnlyList<HistoryDiagnostic> Diagnostics,string? NextCursor);
public interface IEvidenceStore
{
 Task SaveNewAsync(EvidenceRecord record,CancellationToken ct);
 Task<EvidenceRecord> LoadAsync(Guid id,CancellationToken ct);
 Task<HistoryPage> ReadHistoryAsync(string? cursor,CancellationToken ct);
}
public sealed class EvidenceSchemaRegistry
{
 readonly Dictionary<string,Action<EvidenceRecord>> validators=new(StringComparer.Ordinal);
 public EvidenceSchemaRegistry()
 {
  Register<CaptureSelectionEvidence>("capture-selection.v2",(selection,record)=>
  {
   if(selection.Inputs is not null)foreach(var input in selection.Inputs){if(!selection.CheckIds.Contains(input.Key,StringComparer.Ordinal)||!LocalProbeValues.IsRegistered(input.Key))throw new EvidenceValidationException("Unexpected capture input.");Catalogue.LocalProbeCatalogue.ValidateInput(input.Key,input.Value);}
   if(selection.SchemaVersion!=2||selection.CaptureId==Guid.Empty||selection.TaskIds is null||selection.CheckIds is null||selection.TaskIds.Count is <1 or >100||selection.CheckIds.Count>128||selection.TaskIds.Any(string.IsNullOrWhiteSpace)||selection.CheckIds.Any(string.IsNullOrWhiteSpace)||selection.TaskIds.Distinct(StringComparer.Ordinal).Count()!=selection.TaskIds.Count||selection.CheckIds.Distinct(StringComparer.Ordinal).Count()!=selection.CheckIds.Count)throw new EvidenceValidationException("Invalid exact capture selection.");
  });
  Register<DeviceCapture>("capture",(capture,record)=>
  {
   if(capture.Observations is not null)foreach(var o in capture.Observations.Where(o=>o is not null&&LocalProbeValues.IsRegistered(o.CheckId)))
   {if(o.EvidenceCaptureId!=capture.Id||o.LocalSchemaVersion!=2||o.RuleVersion is null||o.RuleVersion.Length is <1 or >64||o.TargetDigest is null||o.TargetDigest.Length!=64||!o.TargetDigest.All(Uri.IsHexDigit)||o.Coverage is not {} c||c.ReturnedCount is <0 or >1000||c.Complete&&c.HasMore||c.Limitations.Count>20||c.NextCursor?.Length>2048||o.State==ObservationState.Known&&(o.Value is not {} v||!LocalProbeValues.Accepts(o.CheckId,v)))throw new EvidenceValidationException("Invalid bounded local capture.");}

   if(capture.Id!=record.Id||capture.Context!=record.Context||capture.CapturedAt!=record.RecordedAt||capture.CapturedBy is null||capture.Catalogue is null||capture.Origin is null||capture.Observations is null)
    throw new EvidenceValidationException("Capture identity or evidence is inconsistent.");
   if(capture.CapturedAt.Offset!=TimeSpan.Zero||capture.Observations.Any(o=>o is null||string.IsNullOrWhiteSpace(o.ControlId)||string.IsNullOrWhiteSpace(o.CheckId)||o.CapturedAt.Offset!=TimeSpan.Zero||o.CapturedAt>capture.CapturedAt.AddMinutes(2)|| (o.State==ObservationState.Known&&!o.Value.HasValue)))
    throw new EvidenceValidationException("Capture contains invalid observations.");
  });
 }
 public void Register<T>(string kind,Action<T,EvidenceRecord> validate)
 {
  if(!validators.TryAdd(kind,record=>validate(StrictJson.Read<T>(Encoding.UTF8.GetBytes(record.Data.GetRawText())),record)))throw new ArgumentException("Duplicate evidence schema.",nameof(kind));
 }
 public void Validate(EvidenceRecord record)
 {
  if(!validators.TryGetValue(record.Kind,out var validate))throw new EvidenceValidationException("UnsupportedSchema","Unknown evidence kind.");
  validate(record);
 }
}
public sealed class EvidenceStore:IEvidenceStore
{
 readonly string root;
 readonly EvidenceSchemaRegistry schemas;
 readonly DeviceContext? expectedContext;
 public EvidenceStore(string root,EvidenceSchemaRegistry? schemas=null,DeviceContext? context=null)
 {
  this.root=Path.GetFullPath(root);this.schemas=schemas??new();expectedContext=context;
 }
 public static void RefuseReparsePath(string path)
 {
  string? current=Path.GetFullPath(path);
  while(current is not null)
  {
   if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
    throw new EvidenceValidationException("UnsafePath","Evidence paths cannot traverse links or reparse points.");
   current=Path.GetDirectoryName(current);
  }
 }
 void Validate(EvidenceRecord record)
 {
  if(record.SchemaVersion!=1)throw new EvidenceValidationException("UnsupportedSchema","Unsupported evidence schema version.");
  if(record.Id==Guid.Empty||record.Context is null||record.Context.OsVersion is null||string.IsNullOrWhiteSpace(record.Context.DeviceId)||string.IsNullOrWhiteSpace(record.Context.OriginalUserSid)||record.RecordedAt.Offset!=TimeSpan.Zero||record.Data.ValueKind!=JsonValueKind.Object)
   throw new EvidenceValidationException("Evidence identity, UTC time or payload is invalid.");
  if(expectedContext is not null&&(record.Context.DeviceId!=expectedContext.DeviceId||record.Context.OriginalUserSid!=expectedContext.OriginalUserSid))
   throw new EvidenceValidationException("WrongDevice","Record belongs to another device or original user.");
  if(!string.Equals(record.Digest,CanonicalJson.Digest(record with { Digest="" }),StringComparison.Ordinal))throw new EvidenceValidationException("Evidence digest does not match its contents.");
  schemas.Validate(record);
 }
 public async Task SaveNewAsync(EvidenceRecord record,CancellationToken ct)
 {
  ct.ThrowIfCancellationRequested();
  try{Validate(record);}catch(Exception e) when(e is JsonException or InvalidOperationException or ArgumentException){throw new EvidenceValidationException("Invalid evidence payload.");}
  var bytes=StrictJson.Bytes(record);
  if(bytes.Length>StrictJson.RecordLimit)throw new EvidenceValidationException("Evidence exceeds the 8 MiB record limit.");
  RefuseReparsePath(root);Directory.CreateDirectory(root);RefuseReparsePath(root);
  var destination=Path.Combine(root,record.Id.ToString("D")+".json");
  var temporary=Path.Combine(root,"pending-"+Guid.NewGuid().ToString("N")+".tmp");
  try
  {
   await using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.Asynchronous|FileOptions.WriteThrough))
   {
    await stream.WriteAsync(bytes,ct);await stream.FlushAsync(ct);stream.Flush(true);
   }
   ct.ThrowIfCancellationRequested();RefuseReparsePath(root);
   File.Move(temporary,destination,false);
  }
  finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 public Task<EvidenceRecord> LoadAsync(Guid id,CancellationToken ct)=>LoadPathAsync(Path.Combine(root,id.ToString("D")+".json"),id,ct);
 async Task<EvidenceRecord> LoadPathAsync(string path,Guid id,CancellationToken ct)
 {
  RefuseReparsePath(path);
  try
  {
   await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,4096,FileOptions.Asynchronous);
   if(stream.Length>StrictJson.RecordLimit)throw new EvidenceValidationException("Evidence exceeds the 8 MiB record limit.");
   var bytes=new byte[checked((int)stream.Length)];await stream.ReadExactlyAsync(bytes,ct);
   if(stream.Length!=bytes.Length)throw new EvidenceValidationException("Evidence changed during reading.");
   var record=StrictJson.Read<EvidenceRecord>(bytes);
   if(record.Id!=id)throw new EvidenceValidationException("Record ID does not match its filename.");
   Validate(record);return record;
  }
  catch(Exception e) when(e is JsonException or InvalidDataException or InvalidOperationException or ArgumentException or OverflowException)
  {throw new EvidenceValidationException("Record is not valid evidence.");}
 }
 public async Task<HistoryPage> ReadHistoryAsync(string? cursor,CancellationToken ct)
 {
  RefuseReparsePath(root);
  if(!Directory.Exists(root))
  {
   // Directory.Exists is also false when the path cannot be inspected; only a confirmed absence is empty history.
   if(File.Exists(root))return new([], [new("","HistoryUnavailable","The evidence path is not a directory.")], null);
   try{using var probe=Directory.EnumerateFileSystemEntries(root).GetEnumerator();probe.MoveNext();}
   catch(DirectoryNotFoundException) when(cursor is null){return new([], [new("","NoHistory","The evidence directory has not been created.")], null);}
   catch(Exception e) when(e is IOException or UnauthorizedAccessException){return new([], [new("","HistoryUnavailable","The evidence directory could not be inspected.")], null);}
  }
  string? after=null;
  if(cursor is not null)
  {
   try{if(cursor.Length>2048)throw new FormatException();after=new UTF8Encoding(false,true).GetString(Convert.FromBase64String(cursor));}
   catch(Exception e) when(e is FormatException or DecoderFallbackException){throw new EvidenceValidationException("Invalid history cursor.");}
  }
  var selected=new SortedSet<string>(StringComparer.Ordinal);var diagnostics=new List<HistoryDiagnostic>();
  try
  {
   foreach(var path in Directory.EnumerateFiles(root,"*.json",SearchOption.TopDirectoryOnly))
   {
    ct.ThrowIfCancellationRequested();var name=Path.GetFileName(path);
    if(after is not null&&StringComparer.Ordinal.Compare(name,after)<=0)continue;
    selected.Add(name);if(selected.Count>101)selected.Remove(selected.Max!);
   }
  }
  catch(Exception e) when(e is IOException or UnauthorizedAccessException)
  {diagnostics.Add(new("","HistoryUnavailable","The history directory could not be fully read."));}
  bool more=selected.Count>100;var names=selected.Take(100).ToArray();var entries=new List<EvidenceRecord>();
  foreach(var name in names)
  {
   if(!Guid.TryParseExact(Path.GetFileNameWithoutExtension(name),"D",out var id)){diagnostics.Add(new(name,"MalformedRecord","Unrecognised evidence filename."));continue;}
   try{entries.Add(await LoadPathAsync(Path.Combine(root,name),id,ct));}
   catch(EvidenceValidationException e){diagnostics.Add(new(name,e.Code,e.Message));}
   catch(Exception e) when(e is IOException or UnauthorizedAccessException){diagnostics.Add(new(name,"UnavailableRecord","The evidence record could not be read."));}
  }
  return new(entries.AsReadOnly(),diagnostics.AsReadOnly(),more?Convert.ToBase64String(Encoding.UTF8.GetBytes(names[^1])):null);
 }
}
