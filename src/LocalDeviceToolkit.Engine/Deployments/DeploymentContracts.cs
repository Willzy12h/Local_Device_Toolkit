using LocalDeviceToolkit.Core;
namespace LocalDeviceToolkit.Engine.Deployments;
// Profiles are trusted application configuration, never deserialised from worker requests or local media.
public enum DeploymentFormat { MachineMsi, OneDriveMachineExe, CurrentUserMsix, OfficeOdt }
public sealed record DeploymentFile(string RelativePath,string Sha256,string? SignerCertificateSha256);
public sealed record DeploymentProfile(string Id,string ControlId,DeploymentFormat Format,string ProductIdentity,string Version,string ProductName,string Publisher,OperationScope Scope,string Architecture,string EntryPoint,IReadOnlyList<DeploymentFile> Files,TimeSpan Timeout,MsixPackageOptions? Msix=null,OfficeDeploymentOptions? Office=null);
public sealed record MsixPackageOptions(string Name,string Publisher,string FamilyName);
public sealed record OfficeDeploymentOptions(string ProductId,string Channel,string Language,bool LicenseAuthorized);
public sealed record DeploymentParameters(string ProfileId,string SourceDirectory);
public sealed record DeploymentInputCheck(bool Accepted,string Digest,string Reason);
public sealed record DeploymentInventory(bool Available,bool Present,string? ProductIdentity,string? Version,string Reason);
public enum DeploymentProcessEnd { Exited, NotStarted, StillRunning, CancelledWhileRunning, ObservationLost }
public enum DeploymentProcessStatus { RunningSameIdentity, NotRunning, IdentityMismatch, Unknown }
public sealed record DeploymentProcessObservation(DeploymentProcessEnd End,int? ProcessId,DateTimeOffset? StartedAt,string ExecutablePath,int? ExitCode,bool MayDetach);
public sealed record DeploymentPending(Guid ActionId,string Target,string OriginalUserSid,DateTimeOffset ClaimedAt,DeploymentProcessObservation? Process,Guid? BootSession=null);
public sealed record DeploymentReconciliation(DeploymentPending? Pending,DeploymentInventory Inventory,DeploymentProcessStatus ProcessStatus,string Reason,Guid? CurrentBootSession=null);
public interface IPreparedDeployment:IAsyncDisposable
{
 string InputDigest{get;}
 Task<DeploymentProcessObservation> StartAsync(CancellationToken ct);
 Task<DeploymentProcessObservation> StartAsync(Func<DeploymentProcessObservation,Task> onStarted,CancellationToken ct)=>StartAsync(ct);
}
public interface IDeploymentBackend
{
 Task<Guid?> GetBootSessionAsync(CancellationToken ct)=>Task.FromResult<Guid?>(null);
 Task<ExecutionActor> GetActorAsync(CancellationToken ct);
 Task<DeploymentInputCheck> CheckInputsAsync(DeploymentProfile profile,string sourceDirectory,CancellationToken ct);
 Task<DeploymentInventory> ReadInventoryAsync(DeploymentProfile profile,DeviceContext context,CancellationToken ct);
 Task<IPreparedDeployment> PrepareAsync(DeploymentProfile profile,string sourceDirectory,DeviceContext context,CancellationToken ct);
 Task<DeploymentProcessStatus> InspectProcessAsync(DeploymentProcessObservation? process,CancellationToken ct);
}
public interface IDeploymentPendingStore
{
 Task<DeploymentPending?> ReadAsync(string target,CancellationToken ct);
 Task ClaimAsync(DeploymentPending pending,CancellationToken ct);
 Task ObserveAsync(DeploymentPending pending,CancellationToken ct);
 Task CompleteAsync(DeploymentPending pending,CancellationToken ct);
}
public static class DeploymentTarget
{
 public static string For(DeviceContext context,DeploymentProfile profile)=>CanonicalJson.Digest(new{context.DeviceId,profile.Scope,User=profile.Scope==OperationScope.CurrentUser?context.OriginalUserSid:"machine",profile.ProductIdentity});
}
public static class DeploymentProfiles
{
 public static void Validate(DeploymentProfile p)
 {
  if(p is null||string.IsNullOrWhiteSpace(p.Id)||p.Id.Length>128||p.ControlId is null||!System.Text.RegularExpressions.Regex.IsMatch(p.ControlId,@"^APP-WIN-00[1-8]$")||!Enum.IsDefined(p.Format)||!Enum.IsDefined(p.Scope)||p.Architecture!="x64"||p.Timeout<=TimeSpan.Zero||p.Timeout>TimeSpan.FromHours(1)||!System.Version.TryParse(p.Version,out _)||string.IsNullOrWhiteSpace(p.ProductName)||string.IsNullOrWhiteSpace(p.Publisher)||p.Files is null||p.Files.Count is <1 or >256)throw new ArgumentException("A complete approved x64 deployment profile is required.");
  if(p.Format!=DeploymentFormat.CurrentUserMsix&&p.Scope!=OperationScope.Machine)throw new ArgumentException("This installer format requires machine scope.");
  if(p.Format==DeploymentFormat.MachineMsi&&!Guid.TryParseExact(p.ProductIdentity,"B",out _))throw new ArgumentException("MSI requires its exact ProductCode.");
  if(p.Format==DeploymentFormat.OneDriveMachineExe&&(p.ProductIdentity!="Microsoft.OneDrive"||p.ControlId!="APP-WIN-005"||p.Publisher!="Microsoft Corporation"))throw new ArgumentException("Only the reviewed per-machine Microsoft OneDrive EXE profile is supported.");
  if(!SafeRelativePath(p.EntryPoint)||!ValidExtension(p.Format,p.EntryPoint))throw new ArgumentException("Invalid installer entry point.");
  if(p.Format==DeploymentFormat.CurrentUserMsix)
  {
   if(p.Scope!=OperationScope.CurrentUser||p.Msix is null||p.Office is not null||p.Msix.Name is null||!System.Text.RegularExpressions.Regex.IsMatch(p.Msix.Name,@"^[A-Za-z0-9.-]{3,50}$")||string.IsNullOrWhiteSpace(p.Msix.Publisher)||p.Msix.Publisher.Length>1024||p.ProductIdentity!=p.Msix.FamilyName||p.Msix.FamilyName!=MsixIdentity.FamilyName(p.Msix.Name,p.Msix.Publisher))throw new ArgumentException("MSIX requires the approved original-user package name, publisher and derived family identity.");
   if(p.Files.Any(f=>f is null||!ValidExtension(DeploymentFormat.CurrentUserMsix,f.RelativePath)||f.SignerCertificateSha256 is null))throw new ArgumentException("Every MSIX dependency must be an approved signed package with a certificate pin.");
  }
  else if(p.Msix is not null)throw new ArgumentException("Unexpected MSIX configuration.");
  if(p.Format==DeploymentFormat.OfficeOdt)
  {
   if(p.Office is null||p.Office.ProductId is not ("O365BusinessRetail" or "O365ProPlusRetail")||p.ProductIdentity!=p.Office.ProductId||p.Office.Channel!="MonthlyEnterprise"||p.Office.Language!="en-gb"||!p.Office.LicenseAuthorized||p.Publisher!="Microsoft Corporation"||p.ProductName!="Microsoft Office Deployment Tool"||!p.Version.StartsWith("16.0.",StringComparison.Ordinal))throw new ArgumentException("Office requires a reviewed supported product, local x64 media, MonthlyEnterprise/en-gb and explicit licensing/EULA authorisation.");
   foreach(var required in new[]{"Office/Data/"+p.Version+"/stream.x64.x-none.dat","Office/Data/"+p.Version+"/stream.x64.en-gb.dat","Office/Data/v64.cab"})if(!p.Files.Any(f=>f.RelativePath.Equals(required,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("The required offline Office media is missing from the approved full manifest.");
  }
  else if(p.Office is not null)throw new ArgumentException("Unexpected Office configuration.");
  var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var file in p.Files)
   if(file is null||!SafeRelativePath(file.RelativePath)||!paths.Add(file.RelativePath)||!Hash(file.Sha256)||(file.SignerCertificateSha256 is not null&&!Hash(file.SignerCertificateSha256)))throw new ArgumentException("Every dependency needs a unique safe relative path and an exact SHA-256 hash.");
  if(p.Files.SingleOrDefault(f=>f.RelativePath.Equals(p.EntryPoint,StringComparison.OrdinalIgnoreCase))?.SignerCertificateSha256 is null)throw new ArgumentException("The installer requires a pinned signing certificate and Windows trust verification.");
  if(p.Format==DeploymentFormat.OneDriveMachineExe&&p.Files.Count!=1)throw new ArgumentException("OneDrive accepts only the single approved self-contained setup executable.");
 }
 static bool ValidExtension(DeploymentFormat format,string path)=>format==DeploymentFormat.CurrentUserMsix?new[]{".msix",".appx",".msixbundle",".appxbundle"}.Any(e=>path.EndsWith(e,StringComparison.OrdinalIgnoreCase)):path.EndsWith(format==DeploymentFormat.MachineMsi?".msi":".exe",StringComparison.OrdinalIgnoreCase);
 public static bool Hash(string? value)=>value is not null&&value.Length==64&&value.All(Uri.IsHexDigit);
 public static bool SafeRelativePath(string? value)=>value is not null&&value.Length is >0 and <=240&&!value.Contains('\\')&&!value.Contains(':')&&!value.StartsWith('/')&&!value.Any(char.IsControl)&&value.Split('/').All(part=>part.Length>0&&part is not "." and not ".."&&!part.EndsWith('.')&&!part.EndsWith(' ')&&!part.Contains('*')&&!part.Contains('?')&&!part.Contains('"')&&!part.Contains('<')&&!part.Contains('>')&&!part.Contains('|')&&!System.Text.RegularExpressions.Regex.IsMatch(part.Split('.')[0].TrimEnd(' '),@"^(CON|PRN|AUX|NUL|CLOCK\$|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])$",System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant));
}

public static class MsixIdentity
{
 public static string FamilyName(string name,string publisher)
 {
  const string alphabet="0123456789abcdefghjkmnpqrstvwxyz";var bytes=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.Unicode.GetBytes(publisher));var result=new System.Text.StringBuilder(name+"_");
  for(var group=0;group<13;group++){int value=0;for(var bit=0;bit<5;bit++){int index=group*5+bit;value=(value<<1)|(index<64?(bytes[index/8]>>(7-index%8))&1:0);}result.Append(alphabet[value]);}return result.ToString();
 }
}
