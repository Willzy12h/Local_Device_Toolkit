using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Windows.Inspection;
using System.Security.AccessControl;
using System.Security.Principal;
namespace LocalDeviceToolkit.Windows.Elevation;
public sealed class ProtectedWorkspace
{
 static readonly SecurityIdentifier Administrators=new(WellKnownSidType.BuiltinAdministratorsSid,null);
 static readonly SecurityIdentifier SystemSid=new(WellKnownSidType.LocalSystemSid,null);
 public string Root{get;}
 public ProtectedWorkspace()
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
  using var identity=WindowsIdentity.GetCurrent();if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new UnauthorizedAccessException("Protected storage requires an elevated administrator.");
  var parent=Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
  Root=Path.Combine(parent,"LocalDeviceToolkit");EvidenceStore.RefuseReparsePath(Root);
  CreateOrValidate(Root);
  GrantRead(Root,FileSystemRights.Traverse|FileSystemRights.ReadPermissions|FileSystemRights.ReadAttributes,false);
 }
 static void CreateOrValidate(string path)
 {
  EvidenceStore.RefuseReparsePath(path);
  var directory=new DirectoryInfo(path);
  if(!directory.Exists)
  {
   var security=new DirectorySecurity();security.SetOwner(Administrators);security.SetAccessRuleProtection(true,false);
   foreach(var sid in new[]{Administrators,SystemSid})security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
   directory.Create(security);
  }
  var actual=directory.GetAccessControl(AccessControlSections.Owner|AccessControlSections.Access);
  var owner=actual.GetOwner(typeof(SecurityIdentifier));
  if(!Administrators.Equals(owner)&&!SystemSid.Equals(owner))throw new UnauthorizedAccessException("Protected storage has an unexpected owner; an administrator must inspect it before use.");
  if(!actual.AreAccessRulesProtected)throw new UnauthorizedAccessException("Protected storage inherits an unreviewed access list.");
  const FileSystemRights write=FileSystemRights.Write|FileSystemRights.Delete|FileSystemRights.DeleteSubdirectoriesAndFiles|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership;
  foreach(FileSystemAccessRule rule in actual.GetAccessRules(true,true,typeof(SecurityIdentifier)))
   if(rule.AccessControlType==AccessControlType.Allow&&(rule.FileSystemRights&write)!=0&&!rule.IdentityReference.Equals(Administrators)&&!rule.IdentityReference.Equals(SystemSid))throw new UnauthorizedAccessException("Protected storage permits an unexpected writer.");
 }
 static void GrantRead(string path,FileSystemRights rights,bool inherit)
 {
  var directory=new DirectoryInfo(path);var security=directory.GetAccessControl(AccessControlSections.Access);
  security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),rights,inherit?InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit:InheritanceFlags.None,PropagationFlags.None,AccessControlType.Allow));directory.SetAccessControl(security);
 }
 public string Directory(string name)
 {
  if(name is not ("journals" or "scripts" or "staging" or "runtime" or "profiles"))throw new ArgumentException("Unregistered protected directory.");
  var path=Path.Combine(Root,name);CreateOrValidate(path);if(name=="profiles")GrantRead(path,FileSystemRights.ReadAndExecute,true);return path;
 }
 public FileStream AcquireWriter()=>new(Path.Combine(Root,"device-writer.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
 public string StageReadOnlyScripts()
 {
  var parent=Directory("scripts");var manifest=RegisteredScripts.Manifest;
  var root=Path.Combine(parent,ScriptStageIdentity.For(manifest));CreateOrValidate(root);
  foreach(var pin in manifest.Scripts)
  {
   if(!pin.ReadOnly)continue;
   var resources=typeof(RegisteredScripts).Assembly.GetManifestResourceNames();
   var name=resources.Single(n=>n.Replace('\\','/')=="scripts/"+pin.Path);
   using var stream=typeof(RegisteredScripts).Assembly.GetManifestResourceStream(name)??throw new InvalidDataException("Missing registered script.");
   using var memory=new MemoryStream();stream.CopyTo(memory);var bytes=memory.ToArray();
   if(LocalDeviceToolkit.Core.CanonicalJson.Hash(bytes)!=pin.Sha256)throw new InvalidDataException("Embedded script integrity failed.");
   var destination=Path.GetFullPath(Path.Combine(root,pin.Path));
   if(!destination.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid script path.");
   // Every subdirectory gets the same protected ACL; no user-controlled folder is traversed.
   var relative=Path.GetDirectoryName(pin.Path)??"";var current=root;
   foreach(var part in relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Where(p=>p.Length>0)){if(part is "." or "..")throw new InvalidDataException();current=Path.Combine(current,part);CreateOrValidate(current);}
   EvidenceStore.RefuseReparsePath(destination);
   if(!File.Exists(destination))
   {
    using var file=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough);file.Write(bytes);file.Flush(true);
   }
   using var verified=RegisteredScripts.OpenVerified(root,pin.Id);
  }
  return root;
 }
}
