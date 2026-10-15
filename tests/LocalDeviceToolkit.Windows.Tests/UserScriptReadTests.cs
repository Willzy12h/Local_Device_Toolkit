using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Settings;
using LocalDeviceToolkit.Windows.Inspection;
using LocalDeviceToolkit.Windows.Settings;
using LocalDeviceToolkit.Windows.Processes;
using System.Diagnostics;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class UserScriptReadTests
{
 [Theory][InlineData(UserScriptAction.ReadRegion)][InlineData(UserScriptAction.ReadConsumerCopilot)]
 public async Task Fixed_script_reads_actual_user_state_without_mutation(UserScriptAction action)
 {
  if(!OperatingSystem.IsWindows())
  {await Assert.ThrowsAsync<PlatformNotSupportedException>(()=>new WindowsUserSettingsBackend(Path.GetTempPath()).GetActorAsync(default));return;}
  var actor=await new WindowsContextProvider().GetActorAsync(default);
  var root=Path.Combine(Path.GetTempPath(),"Toolkit read fixture é "+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var request=Path.Combine(root,"request.json");
  try
  {
   using var script=RegisteredScripts.OpenVerified(Path.Combine(AppContext.BaseDirectory,"scripts"),"settings.user",false);
   await File.WriteAllBytesAsync(request,StrictJson.Bytes(new UserScriptRequest(1,action,actor.Sid,null)));
   var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"));
   foreach(var arg in new[]{"-NoLogo","-NoProfile","-NonInteractive","-File",script.Name,"-RequestFile",request})start.ArgumentList.Add(arg);
   start.Environment["PSModulePath"]=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","Modules");start.Environment.Remove("PSExecutionPolicyPreference");
   var result=await new MutationProcessRunner().StartAndObserveAsync(start,TimeSpan.FromSeconds(60),default);
   Assert.Equal(MutationEnd.Exited,result.End);Assert.Equal(0,result.ExitCode);
   var reply=StrictJson.Read<UserScriptReply>(await File.ReadAllBytesAsync(request+".result.json"));Assert.True(reply.Completed);Assert.Equal(actor.Sid,reply.OriginalUserSid);Assert.Equal(action,reply.Action);Assert.NotNull(reply.Value);
   if(action==UserScriptAction.ReadRegion)
   {
    var region=StrictJson.Read<RegionState>(System.Text.Encoding.UTF8.GetBytes(reply.Value!.Value.GetRawText()));Assert.NotEmpty(region.Culture);Assert.NotEmpty(region.Languages);
    _=UserSettingOperation.RegionVerified(region,region); // validates the shape, without assuming the CI account uses UK preferences.
   }
   else UserSettingOperation.ValidateConsumerIdentity(StrictJson.Read<ConsumerCopilotState>(System.Text.Encoding.UTF8.GetBytes(reply.Value!.Value.GetRawText())));
  }
  finally{Directory.Delete(root,true);}
 }
}
