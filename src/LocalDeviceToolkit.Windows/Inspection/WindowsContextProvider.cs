using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Diagnostics;
using System.Text.Json;
namespace LocalDeviceToolkit.Windows.Inspection;
public sealed class WindowsContextProvider:IExecutionContextProvider,IExecutionActorProvider
{
 public Task<DeviceContext> GetCurrentAsync(CancellationToken ct)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Local Windows identity is unavailable on this host.");
  ct.ThrowIfCancellationRequested();
  using var identity=WindowsIdentity.GetCurrent();var sid=identity.User?.Value??throw new InvalidOperationException("Windows did not return the current user SID.");
  using var machine=Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,Microsoft.Win32.RegistryView.Registry64);
  using var cryptography=machine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography",false);
  var raw=cryptography?.GetValue("MachineGuid") as string;
  if(!Guid.TryParse(raw,out var machineId)||machineId==Guid.Empty)throw new InvalidOperationException("The local device identity could not be read. No identity was invented.");
  var version=new NativeVersion { Size=(uint)Marshal.SizeOf<NativeVersion>(),CsdVersion="" };
  if(RtlGetVersion(ref version)!=0)throw new InvalidOperationException("Windows did not return its actual version.");
  return Task.FromResult(new DeviceContext(machineId.ToString("D"),Environment.MachineName,sid,identity.Name,new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),new Version((int)version.Major,(int)version.Minor,(int)version.Build),RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),version.ProductType==1));
 }
 public Task<ExecutionActor> GetActorAsync(CancellationToken ct)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Local Windows identity is unavailable on this host.");
  ct.ThrowIfCancellationRequested();using var identity=WindowsIdentity.GetCurrent();using var process=Process.GetCurrentProcess();
  return Task.FromResult(new ExecutionActor(identity.User?.Value??throw new InvalidOperationException("Missing Windows SID."),identity.Name,new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),process.Id,new DateTimeOffset(process.StartTime.ToUniversalTime())));
 }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
 struct NativeVersion
 {
  public uint Size,Major,Minor,Build,PlatformId;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string CsdVersion;
  public ushort ServicePackMajor,ServicePackMinor,SuiteMask;public byte ProductType,Reserved;
 }
 [DllImport("ntdll.dll",CharSet=CharSet.Unicode)]static extern int RtlGetVersion(ref NativeVersion version);
}
public static class WindowsFactsParser
{
 public static IReadOnlyList<WindowsFact> Parse(byte[] bytes,IReadOnlySet<string> requested,DateTimeOffset started,DateTimeOffset finished)
 {
  try
  {
   var response=StrictJson.Read<WindowsFactsResponse>(bytes,1024*1024);
   if(response.SchemaVersion!=1||response.Facts is null||response.Facts.Count>128||response.Facts.Any(f=>f is null)||response.Facts.Select(f=>f.CheckId).Distinct(StringComparer.Ordinal).Count()!=response.Facts.Count)throw new InvalidDataException("Invalid Windows facts envelope.");
   foreach(var fact in response.Facts)
    if(!requested.Contains(fact.CheckId)||!ProbeValues.IsRegistered(fact.CheckId)||fact.CapturedAt.Offset!=TimeSpan.Zero||fact.CapturedAt<started.AddMinutes(-2)||fact.CapturedAt>finished.AddMinutes(2)||fact.Source is null||fact.Source.Length>512||fact.Reason is null||fact.Reason.Length>1024|| (fact.State==ObservationState.Known&&(!fact.Value.HasValue||!ProbeValues.Accepts(fact.CheckId,fact.Value.Value))))
     throw new InvalidDataException("A Windows fact has invalid identity, value or capture time.");
   return Array.AsReadOnly(response.Facts.Select(f=>f with { Value=f.Value?.Clone() }).ToArray());
  }
  catch(Exception e) when(e is JsonException or InvalidOperationException or ArgumentException){throw new InvalidDataException("Malformed Windows facts response.");}
 }
}
