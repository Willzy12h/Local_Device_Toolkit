using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Windows.Processes;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
namespace LocalDeviceToolkit.Windows.Inspection;
public sealed record ScriptPin(string Id,string Path,string Sha256,bool ReadOnly);
public sealed record ScriptManifest(int SchemaVersion,ScriptPin[] Scripts);
public static class RegisteredScripts
{
 public static ScriptManifest Manifest
 {
  get
  {
   using var stream=typeof(RegisteredScripts).Assembly.GetManifestResourceStream("scripts/registry.json")??throw new InvalidDataException("Missing script manifest.");
   using var bytes=new MemoryStream();stream.CopyTo(bytes);return StrictJson.Read<ScriptManifest>(bytes.ToArray());
  }
 }
 public static FileStream OpenVerified(string scriptRoot,string id)
 {
  var manifest=Manifest;
  if(manifest.SchemaVersion!=1||manifest.Scripts is null||manifest.Scripts.Any(p=>p is null)||manifest.Scripts.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count()!=manifest.Scripts.Length)throw new InvalidDataException("Invalid script registry.");
  var pin=manifest.Scripts.SingleOrDefault(p=>p.Id==id)??throw new InvalidDataException("Unregistered script.");
  if(!pin.ReadOnly||Path.IsPathRooted(pin.Path)||pin.Path.Split('/').Any(p=>p is ".." or "."||p.Contains('\\'))||pin.Sha256.Length!=64)throw new InvalidDataException("Invalid read-only script registration.");
  var root=Path.GetFullPath(scriptRoot);var path=Path.GetFullPath(Path.Combine(root,pin.Path));
  if(!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Script escaped its registered root.");
  for(var directory=new DirectoryInfo(Path.GetDirectoryName(path)!);directory is not null;directory=directory.Parent)
   if(directory.Exists&&(directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Script path contains a reparse point.");
  if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Script is a reparse point.");
  var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  try
  {
   if(stream.Length>1024*1024||!Convert.ToHexStringLower(SHA256.HashData(stream)).Equals(pin.Sha256,StringComparison.Ordinal))throw new InvalidDataException("The registered script was changed. Restore the reviewed package.");
   stream.Position=0;return stream;
  }
  catch{stream.Dispose();throw;}
 }
}
public sealed class NativeFactsSource(string scriptRoot,string? protectedRuntime=null):IWindowsFactsSource
{
 public async Task<IReadOnlyList<WindowsFact>> ReadAsync(IReadOnlyList<string> checkIds,CancellationToken ct)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Native Windows inspection requires Windows.");
  if(checkIds.Count is <1 or >128||checkIds.Any(id=>!ProbeValues.IsRegistered(id))||checkIds.Distinct(StringComparer.Ordinal).Count()!=checkIds.Count)throw new ArgumentException("Select registered unique checks.");
  var nativeChecks=checkIds.Where(id=>id is "os.version" or "os.architecture").ToArray();
  var nativeFacts=new List<WindowsFact>();
  if(nativeChecks.Length>0)
  {
   var context=await new WindowsContextProvider().GetCurrentAsync(ct);
   foreach(var id in nativeChecks)nativeFacts.Add(new(id,ObservationState.Known,StrictJson.Value(id=="os.version"?context.OsVersion.ToString():context.Architecture),"Windows native version / OS architecture",DateTimeOffset.UtcNow,"Observed OS, not an emulated process architecture."));
  }
  checkIds=checkIds.Except(nativeChecks,StringComparer.Ordinal).ToArray();
  if(checkIds.Count==0)return nativeFacts.AsReadOnly();
  using var script=RegisteredScripts.OpenVerified(scriptRoot,"device.facts");
  var runtime=protectedRuntime??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDeviceToolkit","runtime");
  Directory.CreateDirectory(runtime);
  for(var directory=new DirectoryInfo(runtime);directory is not null;directory=directory.Parent)
   if((directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Runtime path contains a reparse point.");
  var requestPath=Path.Combine(runtime,Guid.NewGuid().ToString("N")+".json");
  var started=DateTimeOffset.UtcNow;
  try
  {
   await using(var request=new FileStream(requestPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
    await request.WriteAsync(StrictJson.Bytes(new { schemaVersion=1,checkIds }),ct);
   var executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
   var start=new ProcessStartInfo(executable);
   foreach(var argument in new[]{"-NoLogo","-NoProfile","-NonInteractive","-File",script.Name,"-RequestFile",requestPath})start.ArgumentList.Add(argument);
   start.Environment["PSModulePath"]=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","Modules");
   start.Environment.Remove("PSExecutionPolicyPreference");
   var result=await new DiagnosticProcessRunner().RunCommandAsync(start,TimeSpan.FromMinutes(2),ct);
   ct.ThrowIfCancellationRequested();
   if(result.End!=DiagnosticEnd.Exited||result.ExitCode!=0)throw new InvalidDataException("The fixed read-only probe did not complete successfully ("+result.End+"). Check script signing and the normal PowerShell execution policy; no policy was changed.");
   return nativeFacts.Concat(WindowsFactsParser.Parse(Encoding.UTF8.GetBytes(result.StandardOutput),checkIds.ToHashSet(StringComparer.Ordinal),started,DateTimeOffset.UtcNow)).ToArray();
  }
  finally{try{File.Delete(requestPath);}catch(IOException){}catch(UnauthorizedAccessException){}}
 }
}
