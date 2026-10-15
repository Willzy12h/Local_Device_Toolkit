using System.Globalization;
using System.Text;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Evidence;

namespace LocalDeviceToolkit.Engine.Logging;

/// <summary>A human-readable companion log. Never use this file as evidence or authorization.</summary>
public sealed class RunTextLog : IAsyncDisposable
{
 const int RecordLimit = 64 * 1024;
 const int FileLimit = 16 * 1024 * 1024;
 static readonly UTF8Encoding Utf8 = new(false, true);
 readonly FileStream stream;
 readonly TimeProvider clock;
 readonly SemaphoreSlim gate = new(1, 1);
 bool disposed;
 Exception? writeFailure;

 RunTextLog(string path, Guid id, FileStream stream, TimeProvider clock)
 { FilePath = path; RunId = id; this.stream = stream; this.clock = clock; }

 public string FilePath { get; }
 public Guid RunId { get; }

 public static async Task<RunTextLog> CreateAsync(string directory, DeviceContext context, TimeProvider clock, CancellationToken ct)
 {
  ct.ThrowIfCancellationRequested();
  ArgumentException.ThrowIfNullOrWhiteSpace(directory);
  ArgumentNullException.ThrowIfNull(context);
  ArgumentNullException.ThrowIfNull(clock);
  var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
  if (string.Equals(root, Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase))
   throw new ArgumentException("A run log must have a dedicated directory.", nameof(directory));
  EvidenceStore.RefuseReparsePath(root);
  Directory.CreateDirectory(root);
  EvidenceStore.RefuseReparsePath(root);
  var id = Guid.NewGuid();
  var name = clock.GetUtcNow().ToUniversalTime().ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture) + "-" + id.ToString("N") + ".log";
  var path = Path.GetFullPath(Path.Combine(root, name));
  if (!string.Equals(Path.GetDirectoryName(path), root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
   throw new ArgumentException("The log path must be inside its directory.", nameof(directory));
  EvidenceStore.RefuseReparsePath(path);
  var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 4096,
   FileOptions.Asynchronous | FileOptions.WriteThrough);
  var log = new RunTextLog(path, id, stream, clock);
  try
  {
   await log.AppendAsync("run", $"Current-user companion log; not protected evidence or authorization. Run={id:D}; OriginalUser={context.OriginalUserName}; OriginalUserSid={context.OriginalUserSid}; Device={context.ComputerName}; DeviceId={context.DeviceId}; Source=selected-work review; Elevated={context.IsElevated}", ct);
   return log;
  }
  catch { await log.DisposeAsync(); throw; }
 }

 public async Task AppendAsync(string stage, string message, CancellationToken ct)
 {
  ct.ThrowIfCancellationRequested();
  ArgumentNullException.ThrowIfNull(stage);
  ArgumentNullException.ThrowIfNull(message);
  // Bound before escaping too, so hostile input cannot demand an unbounded allocation.
  if (stage.Length > RecordLimit || message.Length > RecordLimit)
   throw new ArgumentException("A log record must not exceed 64 KiB.");
  var line = clock.GetUtcNow().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + " [" + Escape(stage) + "] " + Escape(message) + "\n";
  if (Utf8.GetByteCount(line) > RecordLimit) throw new ArgumentException("A log record must not exceed 64 KiB.");
  var bytes = Utf8.GetBytes(line);
  await gate.WaitAsync(ct);
  try
  {
   ObjectDisposedException.ThrowIf(disposed, this);
   if (writeFailure is not null) throw new IOException("The run log failed previously; further actions must stop.", writeFailure);
   ct.ThrowIfCancellationRequested();
   try
   {
    EvidenceStore.RefuseReparsePath(FilePath);
    if (stream.Length > FileLimit - bytes.Length) throw new IOException("The run log has reached its 16 MiB limit.");
    stream.Position = stream.Length;
    await stream.WriteAsync(bytes, ct);
    await stream.FlushAsync(ct);
    stream.Flush(flushToDisk: true);
   }
   catch (Exception error) { writeFailure = error; throw; }
  }
  finally { gate.Release(); }
 }

 public async Task<string> ReadAsync(CancellationToken ct)
 {
  await gate.WaitAsync(ct);
  try
  {
   ObjectDisposedException.ThrowIf(disposed, this);
   ct.ThrowIfCancellationRequested();
   EvidenceStore.RefuseReparsePath(FilePath);
   if (stream.Length > FileLimit) throw new IOException("The run log exceeds its 16 MiB limit.");
   var bytes = new byte[checked((int)stream.Length)];
   stream.Position = 0;
   await stream.ReadExactlyAsync(bytes, ct);
   return Utf8.GetString(bytes);
  }
  finally { gate.Release(); }
 }

 public async ValueTask DisposeAsync()
 {
  await gate.WaitAsync();
  try
  {
   if (disposed) return;
   disposed = true;
   await stream.DisposeAsync();
  }
  finally { gate.Release(); }
 }

 static string Escape(string value)
 {
  var escaped = new StringBuilder(value.Length);
  foreach (var character in value)
   switch (character)
   {
    case '\\': escaped.Append("\\\\"); break;
    case '\r': escaped.Append("\\r"); break;
    case '\n': escaped.Append("\\n"); break;
    case '\t': escaped.Append("\\t"); break;
    default:
     if (char.IsControl(character) || character is '\u2028' or '\u2029' || char.GetUnicodeCategory(character) == UnicodeCategory.Format)
      escaped.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
     else escaped.Append(character);
     break;
   }
  return escaped.ToString();
 }
}
