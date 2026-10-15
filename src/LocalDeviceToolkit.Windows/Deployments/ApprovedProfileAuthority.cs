using LocalDeviceToolkit.Engine.Evidence;
using System.Security.AccessControl;
using System.Security.Principal;
namespace LocalDeviceToolkit.Windows.Deployments;
/// <summary>Read-only access to administrator-approved configuration for original-user package actions.</summary>
public static class ApprovedProfileAuthority
{
 public static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"LocalDeviceToolkit","profiles");
 public static ApprovedProfileStore OpenForRead()
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
  ValidateDirectory(Path.GetDirectoryName(Root)!);ValidateDirectory(Root);return new(Root);
 }
 public static async Task<LocalDeviceToolkit.Engine.Deployments.DeploymentProfile> LoadAsync(string id,CancellationToken ct)
 {
  var store=OpenForRead();if(!LocalDeviceToolkit.Engine.Deployments.DeploymentProfiles.Hash(id))throw new ArgumentException("Invalid approved profile ID.");
  var path=Path.Combine(Root,id+".json");EvidenceStore.RefuseReparsePath(path);
  var acl=new FileInfo(path).GetAccessControl(AccessControlSections.Owner|AccessControlSections.Access);
  ValidateRules(acl.GetOwner(typeof(SecurityIdentifier))?.Value??"",true,acl.GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Select(r=>(r.IdentityReference.Value,r.AccessControlType,r.FileSystemRights)).ToArray());
  return await store.LoadAsync(id,ct);
 }
 internal static void ValidateDirectory(string path)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
  EvidenceStore.RefuseReparsePath(path);var directory=new DirectoryInfo(path);if(!directory.Exists)throw new DirectoryNotFoundException("No administrator-approved deployment profiles exist yet.");
  var acl=directory.GetAccessControl(AccessControlSections.Owner|AccessControlSections.Access);
  ValidateRules(acl.GetOwner(typeof(SecurityIdentifier))?.Value??"",acl.AreAccessRulesProtected,acl.GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Select(r=>(r.IdentityReference.Value,r.AccessControlType,r.FileSystemRights)).ToArray());
 }
 internal static void ValidateRules(string owner,bool isolated,IReadOnlyList<(string Sid,AccessControlType Type,FileSystemRights Rights)> rules)
 {
  static bool Trusted(string sid)=>sid is "S-1-5-32-544" or "S-1-5-18";
  if(!isolated||!Trusted(owner))throw new UnauthorizedAccessException("Approved-profile storage owner or inherited access list is not trusted.");
  const FileSystemRights writes=FileSystemRights.Write|FileSystemRights.Delete|FileSystemRights.DeleteSubdirectoriesAndFiles|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership;
  foreach(var rule in rules)
   if(rule.Type==AccessControlType.Allow&&(rule.Rights&writes)!=0&&!Trusted(rule.Sid))throw new UnauthorizedAccessException("Approved-profile storage permits an untrusted writer.");
 }
}
