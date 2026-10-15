using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Evidence;
namespace LocalDeviceToolkit.Engine.Deployments;
// Machine callers MUST use an administrator-controlled root (ProtectedWorkspace journals), never a user directory.
// A guard is not history or authority: a damaged/missing completion record blocks replay, and reconciliation is read-only.
public sealed class FileDeploymentPendingStore(string root):IDeploymentPendingStore
{
 string PathFor(string target){if(!DeploymentProfiles.Hash(target))throw new ArgumentException("Invalid target key.");EvidenceStore.RefuseReparsePath(root);return Path.Combine(Path.GetFullPath(root),"deployment-"+target.ToLowerInvariant()+".pending.json");}
 public async Task<DeploymentPending?> ReadAsync(string target,CancellationToken ct)
 {
  var path=PathFor(target);EvidenceStore.RefuseReparsePath(path);
  try
  {
   await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,4096,FileOptions.Asynchronous);
   if(file.Length>65536)throw new InvalidDataException("Pending deployment guard is oversized; inspect manually.");
   var bytes=new byte[checked((int)file.Length)];await file.ReadExactlyAsync(bytes,ct);var pending=StrictJson.Read<DeploymentPending>(bytes,65536);
   if(pending.Target!=target||pending.ActionId==Guid.Empty||string.IsNullOrWhiteSpace(pending.OriginalUserSid)||pending.ClaimedAt.Offset!=TimeSpan.Zero||pending.BootSession==Guid.Empty)throw new InvalidDataException("Invalid pending deployment guard; inspect manually.");
   return pending;
  }
  catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}
 }
 public async Task ClaimAsync(DeploymentPending pending,CancellationToken ct)
 {
  var path=PathFor(pending.Target);Directory.CreateDirectory(Path.GetDirectoryName(path)!);EvidenceStore.RefuseReparsePath(path);
  // CreateNew is the durable per-target interprocess gate. No installer is started until Flush(true) completes.
  await WriteAsync(path,pending,ct);
 }
 static async Task WriteAsync(string path,DeploymentPending pending,CancellationToken ct)
 {
  await using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.Asynchronous|FileOptions.WriteThrough);
  await file.WriteAsync(StrictJson.Bytes(pending),ct);await file.FlushAsync(ct);file.Flush(true);
 }
 public async Task ObserveAsync(DeploymentPending pending,CancellationToken ct)
 {
  await RequireOwner(pending,ct);var path=PathFor(pending.Target);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{await WriteAsync(temporary,pending,ct);EvidenceStore.RefuseReparsePath(path);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 public async Task CompleteAsync(DeploymentPending pending,CancellationToken ct){await RequireOwner(pending,ct);var path=PathFor(pending.Target);EvidenceStore.RefuseReparsePath(path);File.Delete(path);}
 async Task RequireOwner(DeploymentPending pending,CancellationToken ct){var current=await ReadAsync(pending.Target,ct);if(current?.ActionId!=pending.ActionId)throw new InvalidDataException("Pending deployment ownership changed; replay is blocked.");}
}
