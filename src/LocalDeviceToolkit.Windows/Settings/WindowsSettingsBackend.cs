using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Settings;
using LocalDeviceToolkit.Windows.Inspection;
using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace LocalDeviceToolkit.Windows.Settings;
public sealed class WindowsSettingsBackend:ISettingsBackend
{
 readonly WindowsContextProvider identity=new();
 public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>identity.GetActorAsync(ct);
 static (RegistryHive Hive,string Path,string Name,int Desired) RegistrySetting(SettingId setting)=>setting switch
 {
  SettingId.ShowFileExtensions=>(RegistryHive.CurrentUser,@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced","HideFileExt",0),
  SettingId.LongPaths=>(RegistryHive.LocalMachine,@"SYSTEM\CurrentControlSet\Control\FileSystem","LongPathsEnabled",1),
  SettingId.DisableFastStartup=>(RegistryHive.LocalMachine,@"SYSTEM\CurrentControlSet\Control\Session Manager\Power","HiberbootEnabled",0),
  _=>throw new PlanValidationException("No registry action is registered for this setting.")
 };
 async Task ValidateContext(SettingId setting,DeviceContext context,CancellationToken ct)
 {
  var current=await identity.GetCurrentAsync(ct);
  if(current.DeviceId!=context.DeviceId||!PlanService.SupportedTarget(current))throw new PlanValidationException("The device is not the reviewed supported Windows workstation.");
  if(setting==SettingId.ShowFileExtensions&&current.OriginalUserSid!=context.OriginalUserSid)throw new UnauthorizedAccessException("This action cannot read or change another engineer's profile.");
 }
 public async Task<SettingSnapshot> ReadAsync(SettingId setting,DeviceContext context,CancellationToken ct)
 {
  await ValidateContext(setting,context,ct);
  if(setting==SettingId.NoAcSleep)return new(setting,StrictJson.Value(PowerSettings.Read()),"Windows active power scheme / AC standby timeout",DateTimeOffset.UtcNow);
  var spec=RegistrySetting(setting);using var root=RegistryKey.OpenBaseKey(spec.Hive,RegistryView.Registry64);using var key=root.OpenSubKey(spec.Path,false);
  RegistryDwordState state;
  if(key is null||!key.GetValueNames().Contains(spec.Name,StringComparer.OrdinalIgnoreCase))state=new(false,null);
  else
  {
   if(key.GetValueKind(spec.Name)!=RegistryValueKind.DWord)throw new InvalidDataException("The expected setting is not a DWORD. No conversion or default was inferred.");
   state=new(true,key.GetValue(spec.Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) is int value?value:throw new InvalidDataException("The DWORD could not be read."));
  }
  return new(setting,StrictJson.Value(state),$"Windows Registry64 {spec.Hive} / {spec.Path} / {spec.Name}",DateTimeOffset.UtcNow);
 }
 public Task<SettingPolicy> InspectPolicyAsync(SettingId setting,CancellationToken ct)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();ct.ThrowIfCancellationRequested();
  try
  {
   var sources=new List<(RegistryHive Hive,string Path,string? Value)>();
   switch(setting)
   {
    case SettingId.ShowFileExtensions:
     foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})
     {
      sources.Add((hive,@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer","HideFileExt"));
      sources.Add((hive,@"SOFTWARE\Policies\Microsoft\Windows\Explorer","HideFileExt"));
     }
     sources.Add((RegistryHive.LocalMachine,@"SOFTWARE\Microsoft\PolicyManager\current\device\ADMX_WindowsExplorer",null));break;
    case SettingId.LongPaths:
     sources.Add((RegistryHive.LocalMachine,@"SOFTWARE\Policies\Microsoft\Windows\System","LongPathsEnabled"));
     sources.Add((RegistryHive.LocalMachine,@"SOFTWARE\Microsoft\PolicyManager\current\device\System",null));break;
    case SettingId.DisableFastStartup:
     sources.Add((RegistryHive.LocalMachine,@"SOFTWARE\Policies\Microsoft\Windows\System","HiberbootEnabled"));
     sources.Add((RegistryHive.LocalMachine,@"SOFTWARE\Microsoft\PolicyManager\current\device\ADMX_System",null));break;
    case SettingId.NoAcSleep:
     sources.Add((RegistryHive.LocalMachine,@"SOFTWARE\Policies\Microsoft\Power\PowerSettings\238c9fa8-0aad-41ed-83f4-97be242c8f20\29f6c1db-86da-48c5-9fdb-f2b67b1f44da","ACSettingIndex"));
     sources.Add((RegistryHive.LocalMachine,@"SOFTWARE\Microsoft\PolicyManager\current\device\Power",null));break;
    default:return Task.FromResult(new SettingPolicy(false,false,"Policy inspection is not registered."));
   }
   foreach(var source in sources)
   {
    using var root=RegistryKey.OpenBaseKey(source.Hive,RegistryView.Registry64);using var key=root.OpenSubKey(source.Path,false);
    if(key is not null&&(source.Value is null?key.ValueCount>0||key.SubKeyCount>0:key.GetValueNames().Contains(source.Value,StringComparer.OrdinalIgnoreCase)))return Task.FromResult(new SettingPolicy(true,true,"A checked GPO or device-management policy source controls this setting. Use the managed configuration workflow."));
   }
   return Task.FromResult(new SettingPolicy(true,false,"No conflict observed in the checked registry policy sources. Other management sources require the engineer's separate review."));
  }
  catch(Exception error) when(error is UnauthorizedAccessException or System.Security.SecurityException or IOException){return Task.FromResult(new SettingPolicy(false,false,"Required policy sources could not be read. No unmanaged-device assumption was made."));}
 }
 public async Task ApplyAsync(SettingId setting,DeviceContext context,SettingSnapshot expectedBefore,CancellationToken ct)
 {
  await ValidateContext(setting,context,ct);var current=await ReadAsync(setting,context,ct);
  if(current.Setting!=expectedBefore.Setting||CanonicalJson.Serialize(current.Value)!=CanonicalJson.Serialize(expectedBefore.Value))throw new PlanValidationException("The exact setting changed before the write.");
  var policy=await InspectPolicyAsync(setting,ct);if(!policy.Available||policy.ConflictObserved)throw new PlanValidationException(policy.Reason);
  if(setting!=SettingId.ShowFileExtensions&&!(await GetActorAsync(ct)).IsElevated)throw new UnauthorizedAccessException("The reviewed machine setting requires an elevated token.");
  ct.ThrowIfCancellationRequested();
  if(setting==SettingId.NoAcSleep)
  {
   var state=StrictJson.Read<AcSleepState>(System.Text.Encoding.UTF8.GetBytes(expectedBefore.Value.GetRawText()));PowerSettings.SetAcNever(state.ActiveScheme);return;
  }
  var spec=RegistrySetting(setting);using var root=RegistryKey.OpenBaseKey(spec.Hive,RegistryView.Registry64);using var key=root.CreateSubKey(spec.Path,true)??throw new IOException("The fixed setting key could not be opened.");
  key.SetValue(spec.Name,spec.Desired,RegistryValueKind.DWord);key.Flush();
 }
}
static class PowerSettings
{
 static readonly Guid SleepSubgroup=new("238c9fa8-0aad-41ed-83f4-97be242c8f20"),StandbyIdle=new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
 public static AcSleepState Read()
 {
  Check(PowerGetActiveScheme(IntPtr.Zero,out var pointer));
  try{var plan=Marshal.PtrToStructure<Guid>(pointer);var sub=SleepSubgroup;var setting=StandbyIdle;Check(PowerReadACValueIndex(IntPtr.Zero,ref plan,ref sub,ref setting,out var value));return new(plan,value);}
  finally{LocalFree(pointer);}
 }
 public static void SetAcNever(Guid expectedPlan)
 {
  if(Read().ActiveScheme!=expectedPlan)throw new PlanValidationException("The active power scheme changed.");
  var plan=expectedPlan;var sub=SleepSubgroup;var setting=StandbyIdle;Check(PowerWriteACValueIndex(IntPtr.Zero,ref plan,ref sub,ref setting,0));Check(PowerSetActiveScheme(IntPtr.Zero,ref plan));
 }
 static void Check(uint result){if(result!=0)throw new Win32Exception(checked((int)result));}
 [DllImport("powrprof.dll")]static extern uint PowerGetActiveScheme(IntPtr root,out IntPtr scheme);
 [DllImport("powrprof.dll")]static extern uint PowerReadACValueIndex(IntPtr root,ref Guid plan,ref Guid sub,ref Guid setting,out uint value);
 [DllImport("powrprof.dll")]static extern uint PowerWriteACValueIndex(IntPtr root,ref Guid plan,ref Guid sub,ref Guid setting,uint value);
 [DllImport("powrprof.dll")]static extern uint PowerSetActiveScheme(IntPtr root,ref Guid plan);
 [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr memory);
}
