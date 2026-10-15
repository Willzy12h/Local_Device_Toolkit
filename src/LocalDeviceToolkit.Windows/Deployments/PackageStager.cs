using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Windows.Elevation;
namespace LocalDeviceToolkit.Windows.Deployments;
public sealed class PackageStager(PackageVerifier verifier)
{
 public Task<VerifiedDeploymentFiles> StageAsync(DeploymentProfile profile,string source,CancellationToken ct)
 {
  // ProtectedWorkspace checks the real elevated token, owner and ACL; submitted paths never select machine staging.
  var root=new ProtectedWorkspace().Directory("staging");return StageIntoAsync(profile,source,root,ct);
 }
 public async Task<VerifiedDeploymentFiles> StageUserAsync(DeploymentProfile profile,string source,LocalDeviceToolkit.Core.DeviceContext original,CancellationToken ct)
 {
  var current=await new LocalDeviceToolkit.Windows.Inspection.WindowsContextProvider().GetCurrentAsync(ct);
  if(profile.Scope!=LocalDeviceToolkit.Core.OperationScope.CurrentUser||current.OriginalUserSid!=original.OriginalUserSid||current.DeviceId!=original.DeviceId)throw new UnauthorizedAccessException("MSIX staging must remain in the original user's profile on the reviewed device.");
  var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDeviceToolkit","staging");return await StageIntoAsync(profile,source,root,ct);
 }
 internal async Task<VerifiedDeploymentFiles> StageIntoAsync(DeploymentProfile profile,string source,string protectedRoot,CancellationToken ct)
 {
  await using var media=await verifier.OpenVerifiedAsync(profile,source,ct);
  EvidenceStore.RefuseReparsePath(protectedRoot);Directory.CreateDirectory(protectedRoot);
  var destination=Path.Combine(protectedRoot,"deployment-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(destination);EvidenceStore.RefuseReparsePath(destination);
  foreach(var item in media.Files)
  {
   var path=Path.Combine(destination,item.Key.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path)!);EvidenceStore.RefuseReparsePath(path);
   await using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous|FileOptions.WriteThrough);item.Value.Position=0;await item.Value.CopyToAsync(file,ct);await file.FlushAsync(ct);file.Flush(true);
  }
  var staged=await verifier.OpenVerifiedAsync(profile,destination,ct);
  if(staged.InputDigest!=media.InputDigest){await staged.DisposeAsync();throw new InvalidDataException("Staged bytes differ from the verified full source manifest.");}
  // Files are intentionally retained after handle disposal: an unobserved installer child may still require its media.
  return staged;
 }
}
