using System.ComponentModel;
using System.Runtime.InteropServices;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Settings;
using LocalDeviceToolkit.Windows.Inspection;
using Microsoft.Win32;
namespace LocalDeviceToolkit.Windows.Settings;
public sealed class WindowsPowerTimeoutBackend:IPowerTimeoutBackend
{
 readonly WindowsContextProvider identity=new();
 static readonly Guid Sleep=new("238c9fa8-0aad-41ed-83f4-97be242c8f20"),Standby=new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da"),Video=new("7516b95f-f776-4464-8c53-06167f40cc99"),Display=new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
 public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>identity.GetActorAsync(ct);
 public Task<SettingPolicy> InspectPolicyAsync(CancellationToken ct)
 {
  ct.ThrowIfCancellationRequested();try
  {
   using var root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);
   foreach(var path in new[]{@"SOFTWARE\Policies\Microsoft\Power\PowerSettings",@"SOFTWARE\Microsoft\PolicyManager\current\device\Power"})
   {using var key=root.OpenSubKey(path);if(key is not null&&(key.ValueCount>0||key.SubKeyCount>0))return Task.FromResult(new SettingPolicy(true,true,"A related power policy source is present; use managed configuration."));}
   return Task.FromResult(new SettingPolicy(true,false,"No conflict observed in checked power policy sources; external client policy review is required."));
  }catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException){return Task.FromResult(new SettingPolicy(false,false,"Power policy could not be read."));}
 }
 public async Task<PowerTimeoutState> ReadAsync(DeviceContext context,CancellationToken ct)
 {
  var current=await identity.GetCurrentAsync(ct);if(current.DeviceId!=context.DeviceId||current.OriginalUserSid!=context.OriginalUserSid||!PlanService.SupportedTarget(current))throw new PlanValidationException("Power scheme reads require the original engineer on the reviewed device.");
  Check(PowerGetActiveScheme(IntPtr.Zero,out var pointer));Guid plan;try{plan=Marshal.PtrToStructure<Guid>(pointer);}finally{LocalFree(pointer);}
  uint Read(Guid sub,Guid setting,bool ac){uint value;Check(ac?PowerReadACValueIndex(IntPtr.Zero,ref plan,ref sub,ref setting,out value):PowerReadDCValueIndex(IntPtr.Zero,ref plan,ref sub,ref setting,out value));return value;}
  var state=new PowerTimeoutState(plan,Read(Sleep,Standby,true),Read(Sleep,Standby,false),Read(Video,Display,true),Read(Video,Display,false));
  Check(PowerGetActiveScheme(IntPtr.Zero,out pointer));try{if(Marshal.PtrToStructure<Guid>(pointer)!=plan)throw new PlanValidationException("The active scheme changed during inspection.");}finally{LocalFree(pointer);}return state;
 }
 public async Task WriteAsync(DeviceContext context,PowerSlot slot,uint seconds,PowerTimeoutState expected,CancellationToken ct)
 {
  if(!Enum.IsDefined(slot)||await ReadAsync(context,ct)!=expected)throw new PlanValidationException("Power scheme or indices changed before the write.");
  var actor=await GetActorAsync(ct);var policy=await InspectPolicyAsync(ct);if(!actor.IsElevated||actor.Sid!=context.OriginalUserSid||!policy.Available||policy.ConflictObserved)throw new PlanValidationException("Original-engineer elevation and policy checks are required.");
  ct.ThrowIfCancellationRequested();var plan=expected.ActiveScheme;var sub=slot is PowerSlot.SleepAc or PowerSlot.SleepDc?Sleep:Video;var setting=slot is PowerSlot.SleepAc or PowerSlot.SleepDc?Standby:Display;
  Check(slot is PowerSlot.SleepAc or PowerSlot.DisplayAc?PowerWriteACValueIndex(IntPtr.Zero,ref plan,ref sub,ref setting,seconds):PowerWriteDCValueIndex(IntPtr.Zero,ref plan,ref sub,ref setting,seconds));
  var after=await ReadAsync(context,CancellationToken.None);if(after!=expected.With(slot,seconds))throw new PlanValidationException("Power state changed or only part of the write completed; activation was refused.");
  if((await GetActorAsync(CancellationToken.None)).Sid!=context.OriginalUserSid)throw new PlanValidationException("Engineer identity changed before activation.");
  Check(PowerSetActiveScheme(IntPtr.Zero,ref plan));
 }
 static void Check(uint code){if(code!=0)throw new Win32Exception(checked((int)code));}
 [DllImport("powrprof.dll")]static extern uint PowerGetActiveScheme(IntPtr root,out IntPtr scheme);
 [DllImport("powrprof.dll")]static extern uint PowerReadACValueIndex(IntPtr root,ref Guid plan,ref Guid sub,ref Guid setting,out uint value);
 [DllImport("powrprof.dll")]static extern uint PowerReadDCValueIndex(IntPtr root,ref Guid plan,ref Guid sub,ref Guid setting,out uint value);
 [DllImport("powrprof.dll")]static extern uint PowerWriteACValueIndex(IntPtr root,ref Guid plan,ref Guid sub,ref Guid setting,uint value);
 [DllImport("powrprof.dll")]static extern uint PowerWriteDCValueIndex(IntPtr root,ref Guid plan,ref Guid sub,ref Guid setting,uint value);
 [DllImport("powrprof.dll")]static extern uint PowerSetActiveScheme(IntPtr root,ref Guid plan);
 [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr memory);
}
