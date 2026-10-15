using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Windows.Inspection;
using LocalDeviceToolkit.Windows.Processes;
namespace LocalDeviceToolkit.Windows.Deployments;
public sealed class WindowsDeploymentBackend(string? scriptRoot=null):IDeploymentBackend
{
 readonly PackageVerifier verifier=new(new WindowsSignatureTrust(),new WindowsInstallerMetadata());
 public Task<Guid?> GetBootSessionAsync(CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(WindowsBootSession.Read());}
 public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>new WindowsContextProvider().GetActorAsync(ct);
 public async Task<DeploymentInputCheck> CheckInputsAsync(DeploymentProfile profile,string sourceDirectory,CancellationToken ct)
 {
  try{if(profile.Format==DeploymentFormat.OfficeOdt){var policy=OfficePolicyGate.Read();if(!policy.Accepted)return policy;}
   if(profile.Format==DeploymentFormat.CurrentUserMsix){using var script=RegisteredScripts.OpenVerified(scriptRoot??Path.Combine(AppContext.BaseDirectory,"scripts"),MsixDeploymentRuntime.ScriptId,false);}
   await using var verified=await verifier.OpenVerifiedAsync(profile,sourceDirectory,ct);return new(true,verified.InputDigest,profile.Scope==OperationScope.CurrentUser?"Original-user MSIX identity, signed dependency set, exact hashes and x64/neutral architecture verified.":"Complete local media hashes, pinned Authenticode signer, product metadata and x64 target architecture verified. Machine deployment requires administrator review.");}
  catch(Exception error) when(error is IOException or UnauthorizedAccessException or InvalidDataException or PlatformNotSupportedException or ArgumentException)
  {return new(false,"","Media refused: "+error.Message);}
 }
 public async Task<IPreparedDeployment> PrepareAsync(DeploymentProfile profile,string sourceDirectory,DeviceContext context,CancellationToken ct)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
  if(profile.Format==DeploymentFormat.CurrentUserMsix)return await MsixDeploymentRuntime.PrepareAsync(profile,sourceDirectory,context,verifier,scriptRoot??Path.Combine(AppContext.BaseDirectory,"scripts"),ct);
  var actor=await GetActorAsync(ct);if(!actor.IsElevated)throw new UnauthorizedAccessException("Machine installer staging requires an elevated actor.");
  if(profile.Format==DeploymentFormat.OfficeOdt&&!OfficePolicyGate.Read().Accepted)throw new UnauthorizedAccessException("Office management policy prerequisites changed before staging.");
  var files=await new PackageStager(verifier).StageAsync(profile,sourceDirectory,ct);try{if(profile.Format==DeploymentFormat.OfficeOdt&&!OfficePolicyGate.Read().Accepted)throw new UnauthorizedAccessException("Office management policy prerequisites changed while staging.");return new Prepared(profile,files);}catch{await files.DisposeAsync();throw;}
 }
 sealed class Prepared(DeploymentProfile profile,VerifiedDeploymentFiles files):IPreparedDeployment
 {
  readonly FileStream? config=profile.Format==DeploymentFormat.OfficeOdt?CreateConfig(profile,files.Root):null;
  static FileStream CreateConfig(DeploymentProfile p,string root){var file=new FileStream(Path.GetFullPath(root)+".config.xml",FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read,4096,FileOptions.WriteThrough);try{file.Write(System.Text.Encoding.UTF8.GetBytes(OfficeDeploymentConfiguration.Create(p,root)));file.Flush(true);return file;}catch{file.Dispose();throw;}}
  public string InputDigest=>files.InputDigest;
  public async ValueTask DisposeAsync(){config?.Dispose();await files.DisposeAsync();}
  int started;
  public Task<DeploymentProcessObservation> StartAsync(CancellationToken ct)=>StartAsync(_=>Task.CompletedTask,ct);
  public async Task<DeploymentProcessObservation> StartAsync(Func<DeploymentProcessObservation,Task> onStarted,CancellationToken ct)
  {
   if(Interlocked.Exchange(ref started,1)!=0)throw new InvalidOperationException("A prepared deployment may be launched only once.");
   var start=DeploymentLaunch.Create(profile,files.Root);var result=await new MutationProcessRunner().StartAndObserveAsync(start,profile.Timeout,ct,p=>onStarted(new(DeploymentProcessEnd.StillRunning,p.ProcessId,p.ProcessStartedAt,start.FileName,null,DeploymentLaunch.MayDetach(profile))));
   var end=result.End switch{MutationEnd.Exited=>DeploymentProcessEnd.Exited,MutationEnd.NotStarted=>DeploymentProcessEnd.NotStarted,MutationEnd.StillRunning=>DeploymentProcessEnd.StillRunning,MutationEnd.CancelledWhileRunning=>DeploymentProcessEnd.CancelledWhileRunning,_=>DeploymentProcessEnd.ObservationLost};
   return new(end,result.ProcessId,result.ProcessStartedAt,start.FileName,result.ExitCode,DeploymentLaunch.MayDetach(profile));
  }
 }
 public Task<DeploymentInventory> ReadInventoryAsync(DeploymentProfile profile,DeviceContext context,CancellationToken ct)
 {
  ct.ThrowIfCancellationRequested();DeploymentProfiles.Validate(profile);
  if(!OperatingSystem.IsWindows())return Task.FromResult(new DeploymentInventory(false,false,null,null,"Windows installed-product APIs are unavailable."));
  if(profile.Format==DeploymentFormat.CurrentUserMsix)return MsixDeploymentRuntime.ReadAsync(profile,context,scriptRoot??Path.Combine(AppContext.BaseDirectory,"scripts"),ct);
  try
  {
   if(profile.Format==DeploymentFormat.OfficeOdt)return Task.FromResult(OfficeInstalledInventory.Read(profile));
   if(profile.Format==DeploymentFormat.MachineMsi)
   {
    var size=4096u;var version=new StringBuilder((int)size);var code=MsiGetProductInfoEx(profile.ProductIdentity,null,4,"VersionString",version,ref size);
    if(code is 1605 or 1608)return Task.FromResult(new DeploymentInventory(true,false,null,null,"Windows Installer reports this ProductCode absent in machine context."));
    if(code!=0)return Task.FromResult(new DeploymentInventory(false,false,null,null,"Windows Installer machine inventory failed: "+code));
    return Task.FromResult(new DeploymentInventory(true,true,profile.ProductIdentity,version.ToString(),"Fresh MsiGetProductInfoEx machine-context ProductCode and VersionString. No Win32_Product query or consistency check was used."));
   }
   var programFiles=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);var path=Path.Combine(programFiles,"Microsoft OneDrive","OneDrive.exe");
   LocalDeviceToolkit.Engine.Evidence.EvidenceStore.RefuseReparsePath(path);
   if(!File.Exists(path))return Task.FromResult(new DeploymentInventory(true,false,null,null,"Per-machine Microsoft OneDrive executable is absent from Program Files."));
   // Deny replacement while signature and version are read. Per-user OneDrive cannot satisfy APP-WIN-005.
   using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);var info=FileVersionInfo.GetVersionInfo(path);
   var trust=new WindowsSignatureTrust().Verify(path,profile.Files.Single(f=>f.RelativePath==profile.EntryPoint).SignerCertificateSha256!);
   if(!trust.Accepted||info.CompanyName!="Microsoft Corporation"||info.ProductName!="Microsoft OneDrive")return Task.FromResult(new DeploymentInventory(false,false,null,null,"Per-machine OneDrive identity/signature could not be independently confirmed. "+trust.Reason));
   return Task.FromResult(new DeploymentInventory(true,true,"Microsoft.OneDrive",WindowsInstallerMetadata.Normalize(info.ProductVersion),"Fresh Program Files OneDrive executable, pinned Windows trust signer and product version. User sign-in and tenant sync remain manual."));
  }
  catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or LocalDeviceToolkit.Engine.Evidence.EvidenceValidationException)
  {return Task.FromResult(new DeploymentInventory(false,false,null,null,"Fresh product inventory unavailable: "+error.GetType().Name));}
 }
 public Task<DeploymentProcessStatus> InspectProcessAsync(DeploymentProcessObservation? observation,CancellationToken ct)
 {
  ct.ThrowIfCancellationRequested();if(observation?.ProcessId is null||observation.StartedAt is null||!Path.IsPathFullyQualified(observation.ExecutablePath))return Task.FromResult(DeploymentProcessStatus.Unknown);
  try
  {
   using var process=Process.GetProcessById(observation.ProcessId.Value);
   if(process.HasExited)return Task.FromResult(DeploymentProcessStatus.NotRunning);
   var created=new DateTimeOffset(process.StartTime.ToUniversalTime());var executable=process.MainModule?.FileName;
   if(executable is null)return Task.FromResult(DeploymentProcessStatus.Unknown);
   if(created!=observation.StartedAt)return Task.FromResult(DeploymentProcessStatus.IdentityMismatch);
   return Task.FromResult(string.Equals(Path.GetFullPath(executable),Path.GetFullPath(observation.ExecutablePath),StringComparison.OrdinalIgnoreCase)?DeploymentProcessStatus.RunningSameIdentity:DeploymentProcessStatus.Unknown);
  }
  catch(ArgumentException){return Task.FromResult(DeploymentProcessStatus.NotRunning);}
  catch(Exception error) when(error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException){return Task.FromResult(DeploymentProcessStatus.Unknown);}
 }
 [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiGetProductInfoExW")]static extern uint MsiGetProductInfoEx(string product,string? userSid,uint context,string property,StringBuilder value,ref uint size);
}
