using System.Diagnostics;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Windows.Inspection;
using LocalDeviceToolkit.Windows.Processes;
namespace LocalDeviceToolkit.Windows.Deployments;
public static class MsixDeploymentRuntime
{
 public const string ScriptId="deployments.msix";
 sealed record Request(int SchemaVersion,string Action,string OriginalUserSid,string Name,string Publisher,string FamilyName,string Version,string? EntryPoint,IReadOnlyList<string> Dependencies);
 public sealed record InventoryReply(int SchemaVersion,string OriginalUserSid,bool Available,bool Present,string? ProductIdentity,string? Version,DateTimeOffset CapturedAt);
 static async Task ValidateOriginal(DeviceContext original,CancellationToken ct)
 {var current=await new WindowsContextProvider().GetCurrentAsync(ct);if(current.DeviceId!=original.DeviceId||current.OriginalUserSid!=original.OriginalUserSid)throw new UnauthorizedAccessException("MSIX must run as the original engineer on the reviewed device.");}
 public static async Task<IPreparedDeployment> PrepareAsync(DeploymentProfile profile,string source,DeviceContext original,PackageVerifier verifier,string scriptRoot,CancellationToken ct)
 {
  await ValidateOriginal(original,ct);var files=await new PackageStager(verifier).StageUserAsync(profile,source,original,ct);
  try{return new Prepared(profile,files,original,scriptRoot);}catch{await files.DisposeAsync();throw;}
 }
 public static async Task<DeploymentInventory> ReadAsync(DeploymentProfile profile,DeviceContext original,string scriptRoot,CancellationToken ct)
 {
  await ValidateOriginal(original,ct);using var script=RegisteredScripts.OpenVerified(scriptRoot,ScriptId,false);
  using var request=WriteRequest(profile,original,"Read",null);var started=DateTimeOffset.UtcNow;var result=await new DiagnosticProcessRunner().RunCommandAsync(Start(script.Name,request.Name),TimeSpan.FromMinutes(1),ct);
  if(result.End!=DiagnosticEnd.Exited||result.ExitCode!=0)return new(false,false,null,null,"Fresh original-user Get-AppxPackage did not complete: "+result.End);
  try
  {
   var reply=StrictJson.Read<InventoryReply>(System.Text.Encoding.UTF8.GetBytes(result.StandardOutput),65536);
   if(reply.SchemaVersion!=1||reply.OriginalUserSid!=original.OriginalUserSid||!reply.Available||reply.CapturedAt.Offset!=TimeSpan.Zero||reply.CapturedAt<started.AddSeconds(-5)||reply.CapturedAt>DateTimeOffset.UtcNow.AddSeconds(5)||reply.Present&&(reply.ProductIdentity!=profile.Msix!.FamilyName||reply.Version is null)||!reply.Present&&(reply.ProductIdentity is not null||reply.Version is not null))throw new InvalidDataException("Invalid fresh original-user package evidence.");
   return new(true,reply.Present,reply.ProductIdentity,reply.Version,"Fresh Get-AppxPackage for the original SID; exact family/publisher, x64/neutral architecture and package Status=Ok.");
  }
  catch(Exception error) when(error is System.Text.Json.JsonException or InvalidDataException){return new(false,false,null,null,"Fresh MSIX inventory evidence was malformed or did not match the original user.");}
 }
 sealed class Prepared:IPreparedDeployment
 {
  readonly DeploymentProfile profile;readonly VerifiedDeploymentFiles files;readonly DeviceContext original;readonly FileStream script,request;int started;
  public Prepared(DeploymentProfile profile,VerifiedDeploymentFiles files,DeviceContext original,string scriptRoot)
  {this.profile=profile;this.files=files;this.original=original;script=RegisteredScripts.OpenVerified(scriptRoot,ScriptId,false);try{request=WriteRequest(profile,original,"Install",files.Root);}catch{script.Dispose();throw;}}
  public string InputDigest=>files.InputDigest;
  public Task<DeploymentProcessObservation> StartAsync(CancellationToken ct)=>StartAsync(_=>Task.CompletedTask,ct);
  public async Task<DeploymentProcessObservation> StartAsync(Func<DeploymentProcessObservation,Task> onStarted,CancellationToken ct)
  {
   if(Interlocked.Exchange(ref started,1)!=0)throw new InvalidOperationException("An approved MSIX deployment may be started only once.");await ValidateOriginal(original,ct);
   var start=Start(script.Name,request.Name);var result=await new MutationProcessRunner().StartAndObserveAsync(start,profile.Timeout,ct,p=>onStarted(Convert(p,start.FileName)));
   return Convert(result,start.FileName);
  }
  public async ValueTask DisposeAsync(){request.Dispose();script.Dispose();await files.DisposeAsync();}
 }
 static DeploymentProcessObservation Convert(MutationObservation o,string executable)=>new(o.End switch{MutationEnd.Exited=>DeploymentProcessEnd.Exited,MutationEnd.NotStarted=>DeploymentProcessEnd.NotStarted,MutationEnd.StillRunning=>DeploymentProcessEnd.StillRunning,MutationEnd.CancelledWhileRunning=>DeploymentProcessEnd.CancelledWhileRunning,_=>DeploymentProcessEnd.ObservationLost},o.ProcessId,o.ProcessStartedAt,executable,o.ExitCode,false);
 static FileStream WriteRequest(DeploymentProfile p,DeviceContext original,string action,string? media)
 {
  var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDeviceToolkit","runtime");EvidenceStore.RefuseReparsePath(root);Directory.CreateDirectory(root);EvidenceStore.RefuseReparsePath(root);
  var path=Path.Combine(root,"msix-"+Guid.NewGuid().ToString("N")+".json");var options=p.Msix!;
  var file=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read,4096,FileOptions.WriteThrough);
  try{var entry=media is null?null:Path.Combine(media,p.EntryPoint.Replace('/',Path.DirectorySeparatorChar));var dependencies=media is null?Array.Empty<string>():p.Files.Where(f=>!f.RelativePath.Equals(p.EntryPoint,StringComparison.OrdinalIgnoreCase)).Select(f=>Path.Combine(media,f.RelativePath.Replace('/',Path.DirectorySeparatorChar))).ToArray();var bytes=StrictJson.Bytes(new Request(1,action,original.OriginalUserSid,options.Name,options.Publisher,options.FamilyName,p.Version,entry,dependencies));if(bytes.Length>65536)throw new InvalidDataException("MSIX request exceeds the supported size.");file.Write(bytes);file.Flush(true);return file;}catch{file.Dispose();throw;}
 }
 internal static ProcessStartInfo Start(string script,string request)
 {
  var system=Environment.GetFolderPath(Environment.SpecialFolder.System);var start=new ProcessStartInfo(Path.Combine(system,"WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=false,CreateNoWindow=true};
  foreach(var argument in new[]{"-NoLogo","-NoProfile","-NonInteractive","-File",script,"-RequestFile",request})start.ArgumentList.Add(argument);
  start.Environment["PSModulePath"]=Path.Combine(system,"WindowsPowerShell","v1.0","Modules");start.Environment.Remove("PSExecutionPolicyPreference");
  return start;
 }
}
