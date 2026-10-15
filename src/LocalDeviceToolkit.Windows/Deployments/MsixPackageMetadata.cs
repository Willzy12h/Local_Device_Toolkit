using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using LocalDeviceToolkit.Engine.Deployments;
namespace LocalDeviceToolkit.Windows.Deployments;
public static class MsixPackageMetadata
{
 sealed record Dependency(string Name,string Publisher,Version Minimum);
 sealed record Package(string Name,string Publisher,string Version,string Architecture,IReadOnlyList<Dependency> Dependencies);
 public static string FamilyName(string name,string publisher)=>MsixIdentity.FamilyName(name,publisher);
 public static void Validate(DeploymentProfile profile,string entryPath)
 {
  DeploymentProfiles.Validate(profile);if(profile.Format!=DeploymentFormat.CurrentUserMsix)throw new ArgumentException("An approved current-user MSIX profile is required.");
  var expected=profile.Msix!;var primary=ReadPath(entryPath,expected.Name,expected.Publisher,profile.Version);
  var dependencies=new Dictionary<string,Package>(StringComparer.Ordinal);
  var mediaRoot=Path.GetDirectoryName(entryPath)!;for(var level=1;level<profile.EntryPoint.Split('/').Length;level++)mediaRoot=Path.GetDirectoryName(mediaRoot)??throw new InvalidDataException("Invalid MSIX media root.");
  foreach(var pin in profile.Files.Where(f=>!f.RelativePath.Equals(profile.EntryPoint,StringComparison.OrdinalIgnoreCase)))
  {
   var path=Path.Combine(mediaRoot,pin.RelativePath.Replace('/',Path.DirectorySeparatorChar));
   var package=ReadPath(path,null,null,null);
   if(!dependencies.TryAdd(package.Name,package))throw new InvalidDataException("Duplicate dependency identity in MSIX media.");
  }
  var reachable=new HashSet<string>(StringComparer.Ordinal);var queue=new Queue<Dependency>(primary.Dependencies);
  while(queue.TryDequeue(out var required))
  {
   if(!dependencies.TryGetValue(required.Name,out var dependency)||dependency.Publisher!=required.Publisher||!Version.TryParse(dependency.Version,out var version)||version<required.Minimum)throw new InvalidDataException("The complete approved MSIX dependency set does not satisfy "+required.Name+".");
   if(reachable.Add(required.Name))foreach(var child in dependency.Dependencies)queue.Enqueue(child);
  }
  if(reachable.Count!=dependencies.Count)throw new InvalidDataException("Unrelated MSIX packages cannot be injected as dependencies.");
 }
 static Package ReadPath(string path,string? name,string? publisher,string? version)
 {using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);using var zip=new ZipArchive(stream,ZipArchiveMode.Read);return ReadArchive(zip,name,publisher,version,true);}
 static Package ReadArchive(ZipArchive zip,string? name,string? publisher,string? version,bool allowBundle)
 {
  CheckArchive(zip);var manifest=zip.GetEntry("AppxManifest.xml");var bundle=zip.GetEntry("AppxMetadata/AppxBundleManifest.xml");
  if(manifest is not null&&bundle is null)
  {
   var document=ReadXml(manifest);if(document.Root?.Name.LocalName!="Package")throw new InvalidDataException("Expected an MSIX package manifest.");
   var identity=document.Root.Elements().SingleOrDefault(e=>e.Name.LocalName=="Identity")??throw new InvalidDataException("Missing MSIX Identity.");
   var actualName=Required(identity,"Name");var actualPublisher=Required(identity,"Publisher");var actualVersion=Required(identity,"Version");var architecture=Required(identity,"ProcessorArchitecture");
   Match(actualName,actualPublisher,actualVersion,architecture,name,publisher,version);
   var dependencies=document.Root.Elements().Where(e=>e.Name.LocalName=="Dependencies").Elements().Where(e=>e.Name.LocalName=="PackageDependency").Select(e=>new Dependency(Required(e,"Name"),Required(e,"Publisher"),Version.Parse(Required(e,"MinVersion")))).ToArray();
   if(dependencies.Length>128)throw new InvalidDataException("Too many MSIX dependencies.");return new(actualName,actualPublisher,actualVersion,architecture,dependencies);
  }
  if(!allowBundle||bundle is null||manifest is not null)throw new InvalidDataException("A supported signed MSIX package or bundle manifest is required.");
  var bundleDocument=ReadXml(bundle);if(bundleDocument.Root?.Name.LocalName!="Bundle")throw new InvalidDataException("Expected an MSIX bundle manifest.");
  var bundleIdentity=bundleDocument.Root.Elements().SingleOrDefault(e=>e.Name.LocalName=="Identity")??throw new InvalidDataException("Missing bundle identity.");
  var bundleName=Required(bundleIdentity,"Name");var bundlePublisher=Required(bundleIdentity,"Publisher");var bundleVersion=Required(bundleIdentity,"Version");Match(bundleName,bundlePublisher,bundleVersion,"neutral",name,publisher,version);
  var selected=bundleDocument.Root.Elements().Where(e=>e.Name.LocalName=="Packages").Elements().Where(e=>e.Name.LocalName=="Package"&&((string?)e.Attribute("Architecture") is "x64" or "neutral")).ToArray();
  if(selected.Length is <1 or >64||!selected.Any(e=>(string?)e.Attribute("Type")=="application"))throw new InvalidDataException("The bundle contains no supported x64/neutral application.");
  var combined=new List<Dependency>();long expanded=0;
  foreach(var package in selected)
  {
   var path=Required(package,"FileName");if(!DeploymentProfiles.SafeRelativePath(path))throw new InvalidDataException("Unsafe bundled package path.");
   var nested=zip.GetEntry(path)??throw new InvalidDataException("Missing bundled package.");expanded=checked(expanded+nested.Length);if(expanded>8L*1024*1024*1024||nested.Length>4L*1024*1024*1024)throw new InvalidDataException("Bundled packages exceed the inspection limit.");
   // A seekable bounded temporary stream avoids ZipArchive buffering an entire large nested package in memory.
   var temporary=Path.Combine(Path.GetTempPath(),"ldt-msix-"+Guid.NewGuid().ToString("N")+".tmp");
   using var spool=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,65536,FileOptions.DeleteOnClose);
   using(var source=nested.Open()){var buffer=new byte[65536];long copied=0;int count;while((count=source.Read(buffer,0,buffer.Length))>0){copied=checked(copied+count);if(copied>nested.Length)throw new InvalidDataException("Nested package expands beyond its declared bounded size.");spool.Write(buffer,0,count);}}if(spool.Length!=nested.Length)throw new InvalidDataException("Invalid nested package length.");spool.Position=0;
   using var inner=new ZipArchive(spool,ZipArchiveMode.Read,true);var actual=ReadArchive(inner,bundleName,bundlePublisher,bundleVersion,false);combined.AddRange(actual.Dependencies);
  }
  return new(bundleName,bundlePublisher,bundleVersion,"neutral",combined);
 }
 static void Match(string name,string publisher,string version,string architecture,string? expectedName,string? expectedPublisher,string? expectedVersion)
 {if((expectedName is not null&&name!=expectedName)||(expectedPublisher is not null&&publisher!=expectedPublisher)||(expectedVersion is not null&&version!=expectedVersion)||architecture is not ("x64" or "neutral")||!Version.TryParse(version,out var parsed)||parsed.Revision<0)throw new InvalidDataException("MSIX name, publisher, exact version or x64/neutral architecture differs from the approved profile.");}
 static string Required(XElement element,string name)=>(string?)element.Attribute(name) is string value&&value.Length is >0 and <=2048?value:throw new InvalidDataException("Missing/invalid MSIX "+name+".");
 static XDocument ReadXml(ZipArchiveEntry entry)
 {if(entry.Length>1024*1024)throw new InvalidDataException("MSIX manifest exceeds 1 MiB.");using var stream=entry.Open();using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024,MaxCharactersFromEntities=0});try{return XDocument.Load(reader);}catch(XmlException error){throw new InvalidDataException("Invalid MSIX manifest XML.",error);}}
 static void CheckArchive(ZipArchive zip)
 {if(zip.Entries.Count>4096)throw new InvalidDataException("MSIX ZIP entry limit exceeded.");var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long expanded=0;foreach(var entry in zip.Entries){if(!DeploymentProfiles.SafeRelativePath(entry.FullName.TrimEnd('/'))||!names.Add(entry.FullName))throw new InvalidDataException("Unsafe or duplicate MSIX ZIP entry.");expanded=checked(expanded+entry.Length);if(entry.Length>4L*1024*1024*1024||expanded>16L*1024*1024*1024)throw new InvalidDataException("MSIX expanded-size limit exceeded.");}}
}
