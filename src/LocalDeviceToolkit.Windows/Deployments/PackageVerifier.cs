using System.Security.Cryptography;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Engine.Evidence;
namespace LocalDeviceToolkit.Windows.Deployments;
public sealed record SignatureTrust(bool Accepted,string Reason);
public interface IPackageSignatureTrust {SignatureTrust Verify(string path,string expectedCertificateSha256);}
public interface IInstallerMetadata {void Validate(DeploymentProfile profile,string installerPath);}
public sealed class VerifiedDeploymentFiles:IAsyncDisposable
{
 public string Root{get;} public string InputDigest{get;} public IReadOnlyDictionary<string,FileStream> Files{get;}
 internal VerifiedDeploymentFiles(string root,string digest,Dictionary<string,FileStream> files){Root=root;InputDigest=digest;Files=new System.Collections.ObjectModel.ReadOnlyDictionary<string,FileStream>(files);}
 public ValueTask DisposeAsync(){foreach(var file in Files.Values)file.Dispose();return ValueTask.CompletedTask;}
}
public sealed class PackageVerifier(IPackageSignatureTrust signatures,IInstallerMetadata metadata)
{
 public async Task<VerifiedDeploymentFiles> OpenVerifiedAsync(DeploymentProfile profile,string directory,CancellationToken ct)
 {
  DeploymentProfiles.Validate(profile);directory=Path.GetFullPath(directory);
  if(directory.StartsWith(@"\\",StringComparison.Ordinal)||directory.StartsWith("//",StringComparison.Ordinal))throw new InvalidDataException("Use local media copied to a local disk, not a UNC share.");
  var files=new Dictionary<string,FileStream>(StringComparer.OrdinalIgnoreCase);
  try
  {
   Safe(directory);var expected=profile.Files.Select(f=>f.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);var observed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   var visitedEntries=0;
   void InspectTree(string path,int depth)
   {
    if(depth>32)throw new InvalidDataException("Media directory depth exceeds the inspection limit.");
    Safe(path);foreach(var entry in Directory.EnumerateFileSystemEntries(path))
    {ct.ThrowIfCancellationRequested();if(++visitedEntries>4096)throw new InvalidDataException("Media directory entries exceed the inspection limit.");Safe(entry);if(Directory.Exists(entry))InspectTree(entry,depth+1);else{if(observed.Count>=256)throw new InvalidDataException("Media exceeds the approved file count.");observed.Add(Path.GetRelativePath(directory,entry).Replace(Path.DirectorySeparatorChar,'/'));}}
   }
   InspectTree(directory,0);if(!expected.SetEquals(observed))throw new InvalidDataException("The media file set differs from the complete approved manifest. Unlisted side-load files and missing dependencies are refused.");
   var hashes=new List<object>();
   foreach(var pin in profile.Files.OrderBy(f=>f.RelativePath,StringComparer.Ordinal))
   {
    ct.ThrowIfCancellationRequested();var path=Path.Combine(directory,pin.RelativePath.Replace('/',Path.DirectorySeparatorChar));Safe(path);
    var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);files.Add(pin.RelativePath,file);Safe(path);
    var digest=Convert.ToHexString(await SHA256.HashDataAsync(file,ct));file.Position=0;
    if(!digest.Equals(pin.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Hash mismatch for approved dependency: "+pin.RelativePath);
    if(pin.SignerCertificateSha256 is not null)
    {var trust=signatures.Verify(path,pin.SignerCertificateSha256);if(!trust.Accepted)throw new InvalidDataException("Windows signature verification failed for "+pin.RelativePath+": "+trust.Reason);}
    hashes.Add(new{pin.RelativePath,Sha256=digest,Signer=pin.SignerCertificateSha256?.ToUpperInvariant()});
   }
   metadata.Validate(profile,Path.Combine(directory,profile.EntryPoint.Replace('/',Path.DirectorySeparatorChar)));
   return new(directory,CanonicalJson.Digest(new{Profile=profile,Files=hashes}),files);
  }
  catch{foreach(var file in files.Values)file.Dispose();throw;}
 }
 static void Safe(string path)
 {try{EvidenceStore.RefuseReparsePath(path);}catch(EvidenceValidationException error){throw new InvalidDataException("Installer media must not traverse links or reparse points.",error);}}
}
