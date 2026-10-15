using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Settings;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Windows.Inspection;
using LocalDeviceToolkit.Windows.Processes;
using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;
namespace LocalDeviceToolkit.Windows.Settings;
public enum UserScriptAction { ReadRegion,ApplyRegion,ReadConsumerCopilot,RemoveConsumerCopilot }
public sealed record UserScriptRequest(int SchemaVersion,UserScriptAction Action,string OriginalUserSid,JsonElement? ExpectedBefore);
public sealed record UserScriptReply(int SchemaVersion,UserScriptAction Action,string OriginalUserSid,bool Completed,JsonElement? Value,DateTimeOffset CapturedAt,string Reason);
public sealed class WindowsUserSettingsBackend(string scriptRoot):IUserSettingsBackend
{
 readonly WindowsContextProvider identity=new();
 public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>identity.GetActorAsync(ct);
 async Task ValidateContext(DeviceContext original,CancellationToken ct)
 {
  var current=await identity.GetCurrentAsync(ct);
  if(current.OriginalUserSid!=original.OriginalUserSid||current.DeviceId!=original.DeviceId||!PlanService.SupportedTarget(current))throw new PlanValidationException("Current-user preferences require the original engineer on the reviewed Windows workstation.");
 }
 public Task<SettingPolicy> InspectPolicyAsync(UserSettingId setting,CancellationToken ct)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();ct.ThrowIfCancellationRequested();
  try
  {
   var paths=setting==UserSettingId.UkRegionKeyboard?new[]{@"SOFTWARE\Policies\Microsoft\Control Panel\International",@"SOFTWARE\Policies\Microsoft\MUI\Settings",@"SOFTWARE\Microsoft\PolicyManager\current\device\TimeLanguageSettings"}:new[]{@"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot",@"SOFTWARE\Microsoft\PolicyManager\current\device\WindowsAI"};
   foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})
    foreach(var path in paths)
    {
     using var root=RegistryKey.OpenBaseKey(hive,RegistryView.Registry64);using var key=root.OpenSubKey(path,false);
     if(key is not null&&(key.ValueCount>0||key.SubKeyCount>0))return Task.FromResult(new SettingPolicy(true,true,"A potentially related management policy source is present; use the managed configuration workflow."));
    }
   return Task.FromResult(new SettingPolicy(true,false,"No conflict observed in checked registry sources. Review tenant assignments and other policy sources separately."));
  }
  catch(Exception error) when(error is UnauthorizedAccessException or System.Security.SecurityException or IOException){return Task.FromResult(new SettingPolicy(false,false,"Current-user policy sources could not be inspected. No unmanaged-device assumption was made."));}
 }
 public async Task<UserSettingSnapshot> ReadAsync(UserSettingId setting,DeviceContext original,CancellationToken ct)
 {
  await ValidateContext(original,ct);var action=setting==UserSettingId.UkRegionKeyboard?UserScriptAction.ReadRegion:UserScriptAction.ReadConsumerCopilot;
  var reply=await RunAsync(action,original,null,ct);
  return new(setting,reply.Value?.Clone()??throw new InvalidDataException("The user probe returned no measured state."),"Windows PowerShell 5.1 / fixed original-user preferences probe",reply.CapturedAt);
 }
 public async Task ApplyAsync(UserSettingId setting,DeviceContext original,UserSettingSnapshot before,CancellationToken ct)
 {
  await ValidateContext(original,ct);var policy=await InspectPolicyAsync(setting,ct);if(!policy.Available||policy.ConflictObserved)throw new PlanValidationException(policy.Reason);
  if(before.Setting!=setting)throw new PlanValidationException("Current-user before evidence targets another operation.");
  var action=setting==UserSettingId.UkRegionKeyboard?UserScriptAction.ApplyRegion:UserScriptAction.RemoveConsumerCopilot;
  _=await RunAsync(action,original,before.Value,ct);
 }
 async Task<UserScriptReply> RunAsync(UserScriptAction action,DeviceContext original,JsonElement? before,CancellationToken ct)
 {
  ct.ThrowIfCancellationRequested();using var script=RegisteredScripts.OpenVerified(scriptRoot,"settings.user",false);
  var runtime=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDeviceToolkit","runtime");EvidenceStore.RefuseReparsePath(runtime);Directory.CreateDirectory(runtime);EvidenceStore.RefuseReparsePath(runtime);
  var requestPath=Path.Combine(runtime,Guid.NewGuid().ToString("N")+".json");var resultPath=requestPath+".result.json";var cleanup=true;var started=DateTimeOffset.UtcNow;
  try
  {
   var request=StrictJson.Bytes(new UserScriptRequest(1,action,original.OriginalUserSid,before?.Clone()));if(request.Length>65536)throw new InvalidDataException("Current-user state exceeds the supported request limit.");
   await using(var file=new FileStream(requestPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read))await file.WriteAsync(request,ct);
   var executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
   var start=new ProcessStartInfo(executable);
   foreach(var argument in new[]{"-NoLogo","-NoProfile","-NonInteractive","-File",script.Name,"-RequestFile",requestPath})start.ArgumentList.Add(argument);
   start.Environment["PSModulePath"]=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","Modules");start.Environment.Remove("PSExecutionPolicyPreference");
   cleanup=false;var observation=await new MutationProcessRunner().StartAndObserveAsync(start,TimeSpan.FromMinutes(2),ct);
   cleanup=observation.End is MutationEnd.Exited or MutationEnd.NotStarted;
   UserChangeUncertainException Uncertain()=>new("The original-user operation did not return confirmed evidence ("+observation.End+"). Inspect the current state before retrying; retained runtime files, if any: "+Path.GetFileName(requestPath),StrictJson.Value(new {observation.End,observation.ProcessId,observation.ProcessStartedAt,observation.ExitCode,RequestFile=Path.GetFileName(requestPath)}));
   if(observation.End!=MutationEnd.Exited||observation.ExitCode!=0)throw Uncertain();
   try
   {
    EvidenceStore.RefuseReparsePath(resultPath);await using var file=new FileStream(resultPath,FileMode.Open,FileAccess.Read,FileShare.Read);
    if(file.Length>1024*1024)throw new InvalidDataException("Oversized user-setting result.");var bytes=new byte[checked((int)file.Length)];await file.ReadExactlyAsync(bytes,CancellationToken.None);
    var reply=StrictJson.Read<UserScriptReply>(bytes,1024*1024);
    if(reply.SchemaVersion!=1||reply.Action!=action||reply.OriginalUserSid!=original.OriginalUserSid||!reply.Completed||reply.CapturedAt.Offset!=TimeSpan.Zero||reply.CapturedAt<started.AddMinutes(-2)||reply.CapturedAt>DateTimeOffset.UtcNow.AddMinutes(2)||reply.Reason.Length>1024)throw new InvalidDataException("Invalid original-user result identity or time.");
    if(action is UserScriptAction.ReadRegion or UserScriptAction.ReadConsumerCopilot){if(reply.Value is not JsonElement {ValueKind:JsonValueKind.Object})throw new InvalidDataException("Missing user-state object.");}
    else if(reply.Value is not null)throw new InvalidDataException("Mutation replies must not masquerade as independent readback.");
    return reply;
   }
   catch(Exception error) when(error is IOException or JsonException or UnauthorizedAccessException){throw Uncertain();}
  }
  finally
  {
   if(cleanup)foreach(var path in new[]{requestPath,resultPath})try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}
  }
 }
}
