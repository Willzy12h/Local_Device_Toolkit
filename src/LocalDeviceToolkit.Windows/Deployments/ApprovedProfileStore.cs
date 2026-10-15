using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Engine.Evidence;
namespace LocalDeviceToolkit.Windows.Deployments;
public sealed record ProfileStoreNotice(string FileName, string Message);
public sealed record ProfileStoreListing(IReadOnlyList<DeploymentProfile> Profiles, IReadOnlyList<ProfileStoreNotice> Notices, bool LimitReached);
/// <summary>
/// Immutable, content-addressed profile files. A user cache is selection/display data, never worker authorization.
/// Workers must independently use a root whose administrator-only ACLs were established by ProtectedWorkspace.
/// This class checks paths and content; it does not establish ACLs or confer administrator approval.
/// </summary>
public sealed class ApprovedProfileStore
{
 public const int MaximumProfileBytes = 256 * 1024;
 public const int MaximumProfiles = 1000;
 public string Root { get; }
 public static string UserCacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalDeviceToolkit", "deployment-profiles");
 public ApprovedProfileStore(string root) { ArgumentException.ThrowIfNullOrWhiteSpace(root); Root = Path.GetFullPath(root); }
 public static string ContentId(DeploymentProfile profile) => CanonicalJson.Digest(profile with { Id = "" });
 public static DeploymentProfile WithContentId(DeploymentProfile profile)
 {
  ArgumentNullException.ThrowIfNull(profile);
  var result = profile with { Id = ContentId(profile), Files = Array.AsReadOnly(profile.Files?.ToArray() ?? []) };
  DeploymentProfiles.Validate(result); return result;
 }
 static void Validate(DeploymentProfile profile)
 {
  DeploymentProfiles.Validate(profile);
  if (profile.Id != ContentId(profile)) throw new InvalidDataException("Profile ID does not match its complete content SHA-256.");
 }
 string PathFor(string id)
 {
  if (!DeploymentProfiles.Hash(id) || id != id.ToLowerInvariant()) throw new ArgumentException("A lowercase SHA-256 profile ID is required.", nameof(id));
  EvidenceStore.RefuseReparsePath(Root); return Path.Combine(Root, id + ".json");
 }
 public async Task<DeploymentProfile> SaveOrLoadIdenticalAsync(DeploymentProfile profile,CancellationToken ct)
 {
  Validate(profile);
  try{await SaveAsync(profile,ct);return profile;}
  catch(IOException) when(File.Exists(PathFor(profile.Id)))
  {var existing=await LoadAsync(profile.Id,ct);if(CanonicalJson.Digest(existing)!=CanonicalJson.Digest(profile))throw new InvalidDataException("Existing approval does not match this complete profile.");return existing;}
 }
 public async Task SaveAsync(DeploymentProfile profile, CancellationToken ct)
 {
  ct.ThrowIfCancellationRequested(); Validate(profile);
  var bytes = StrictJson.Bytes(profile);
  if (bytes.Length > MaximumProfileBytes) throw new InvalidDataException("Profile exceeds the 256 KiB limit.");
  var destination = PathFor(profile.Id); Directory.CreateDirectory(Root); EvidenceStore.RefuseReparsePath(destination);
  if (Directory.EnumerateFiles(Root, "*.json", SearchOption.TopDirectoryOnly).Take(MaximumProfiles).Count() >= MaximumProfiles)
   throw new InvalidDataException("Profile directory reached the 1000-file limit; an engineer must review it before adding profiles.");
  var temporary = Path.Combine(Root, ".profile-pending-" + Guid.NewGuid().ToString("N") + ".tmp");
  try
  {
   await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
   { await file.WriteAsync(bytes, ct); await file.FlushAsync(ct); file.Flush(true); }
   ct.ThrowIfCancellationRequested(); EvidenceStore.RefuseReparsePath(destination); File.Move(temporary, destination, false);
  }
  finally { if (File.Exists(temporary)) File.Delete(temporary); }
 }
 public async Task<DeploymentProfile> LoadAsync(string id, CancellationToken ct)
 {
  var profile = await ReadFileAsync(PathFor(id), ct); Validate(profile);
  if (profile.Id != id) throw new InvalidDataException("Profile content does not match its filename.");
  return profile with { Files = Array.AsReadOnly(profile.Files.ToArray()) };
 }
 /// <summary>Reads untrusted proposed configuration. Blank IDs are assigned; supplied IDs must match the full content.</summary>
 public static async Task<DeploymentProfile> ReadImportAsync(string path, CancellationToken ct)
 {
  var profile = await ReadFileAsync(Path.GetFullPath(path), ct);
  if (profile.Id.Length == 0) return WithContentId(profile);
  Validate(profile); return profile;
 }
 static async Task<DeploymentProfile> ReadFileAsync(string path, CancellationToken ct)
 {
  EvidenceStore.RefuseReparsePath(path);
  try
  {
   await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
   if (file.Length > MaximumProfileBytes) throw new InvalidDataException("Profile exceeds the 256 KiB limit.");
   var bytes = new byte[checked((int)file.Length)]; await file.ReadExactlyAsync(bytes, ct);
   if (file.Length != bytes.Length) throw new InvalidDataException("Profile changed during reading.");
   return StrictJson.Read<DeploymentProfile>(bytes, MaximumProfileBytes);
  }
  catch (Exception e) when (e is JsonException or ArgumentException or OverflowException)
  { throw new InvalidDataException("Profile must match the strict deployment JSON schema.", e); }
 }
 public async Task<ProfileStoreListing> ReadAllAsync(CancellationToken ct)
 {
  EvidenceStore.RefuseReparsePath(Root);
  if (!Directory.Exists(Root)) return new([], [new("", "No cached profiles yet. Import a deployment profile JSON for administrator review.")], false);
  var profiles = new List<DeploymentProfile>(); var notices = new List<ProfileStoreNotice>(); var files = new List<string>(); bool limit = false;
  try
  {
   foreach (var path in Directory.EnumerateFiles(Root, "*.json", SearchOption.TopDirectoryOnly))
   { ct.ThrowIfCancellationRequested(); if (files.Count == MaximumProfiles) { limit = true; break; } files.Add(path); }
  }
  catch (Exception e) when (e is IOException or UnauthorizedAccessException) { notices.Add(new("", "Profile directory could not be fully read: " + e.Message)); }
  foreach (var path in files)
  {
   ct.ThrowIfCancellationRequested(); var name = Path.GetFileName(path); var id = Path.GetFileNameWithoutExtension(name);
   try { profiles.Add(await LoadAsync(id, ct)); }
   catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or EvidenceValidationException)
   { if (notices.Count < MaximumProfiles) notices.Add(new(name, e.Message)); }
  }
  return new(profiles.OrderBy(p => p.ProductName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id, StringComparer.Ordinal).ToArray(), notices.AsReadOnly(), limit);
 }
}
