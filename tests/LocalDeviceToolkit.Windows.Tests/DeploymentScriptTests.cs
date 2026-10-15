using System.Diagnostics;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Windows.Deployments;
using LocalDeviceToolkit.Windows.Inspection;
using LocalDeviceToolkit.Windows.Processes;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class DeploymentScriptTests
{
 [Fact] public async Task Fixed_MSIX_script_refuses_another_user_before_any_package_cmdlet()
 {
  using var script=RegisteredScripts.OpenVerified(Path.Combine(AppContext.BaseDirectory,"scripts"),MsixDeploymentRuntime.ScriptId,false);
  if(!OperatingSystem.IsWindows()){await Assert.ThrowsAsync<PlatformNotSupportedException>(()=>new WindowsContextProvider().GetActorAsync(default));return;}
  var result=await ReadScript(script.Name,"S-1-0-0");Assert.Equal(DiagnosticEnd.Exited,result.End);Assert.NotEqual(0,result.ExitCode);Assert.Contains("Invalid original user",result.StandardError);
 }
 [Fact] public async Task Fresh_MSIX_read_reports_absent_synthetic_package_without_mutation()
 {
  using var script=RegisteredScripts.OpenVerified(Path.Combine(AppContext.BaseDirectory,"scripts"),MsixDeploymentRuntime.ScriptId,false);
  if(!OperatingSystem.IsWindows()){await Assert.ThrowsAsync<PlatformNotSupportedException>(()=>new WindowsContextProvider().GetActorAsync(default));return;}
  var actor=await new WindowsContextProvider().GetActorAsync(default);var result=await ReadScript(script.Name,actor.Sid);Assert.Equal(DiagnosticEnd.Exited,result.End);Assert.Equal(0,result.ExitCode);var reply=StrictJson.Read<MsixDeploymentRuntime.InventoryReply>(System.Text.Encoding.UTF8.GetBytes(result.StandardOutput),65536);Assert.Equal(actor.Sid,reply.OriginalUserSid);Assert.True(reply.Available);Assert.False(reply.Present);Assert.Null(reply.ProductIdentity);Assert.Null(reply.Version);
 }
 static async Task<DiagnosticResult> ReadScript(string script,string sid)
 {
  var name="Toolkit.Synthetic"+Guid.NewGuid().ToString("N");var publisher="CN=Toolkit Synthetic Fixture";var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
  try
  {await File.WriteAllBytesAsync(path,StrictJson.Bytes(new{schemaVersion=1,action="Read",originalUserSid=sid,name,publisher,familyName=MsixIdentity.FamilyName(name,publisher),version="1.0.0.0",entryPoint=(string?)null,dependencies=Array.Empty<string>()}));var start=MsixDeploymentRuntime.Start(script,path);return await new DiagnosticProcessRunner().RunCommandAsync(start,TimeSpan.FromSeconds(60),default);}
  finally{File.Delete(path);}
 }
}
