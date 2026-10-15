using LocalDeviceToolkit.Windows.Inspection;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class RegisteredScriptTests
{
 [Fact] public void Altered_read_only_script_cannot_be_run()
 {
  var directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(directory,"diagnostics"));
  try{File.WriteAllText(Path.Combine(directory,"diagnostics","Get-DeviceFacts.ps1"),"Write-Output fabricated");Assert.Throws<InvalidDataException>(()=>RegisteredScripts.OpenVerified(directory,"device.facts"));}
  finally{Directory.Delete(directory,true);}
 }
 [Fact] public void Shipped_script_matches_the_reviewed_pin()
 {
  using var script=RegisteredScripts.OpenVerified(Path.Combine(AppContext.BaseDirectory,"scripts"),"device.facts");Assert.True(script.Length>0);
 }
 [Fact] public void Protected_read_script_updates_have_separate_immutable_stage_locations()
 {
  var first=new ScriptManifest(1,[new("device.facts","diagnostics/Get-DeviceFacts.ps1",new string('a',64),true)]);
  var changed=first with {Scripts=[first.Scripts[0] with {Sha256=new string('b',64)}]};Assert.NotEqual(ScriptStageIdentity.For(first),ScriptStageIdentity.For(changed));
  Assert.Equal(ScriptStageIdentity.For(first),ScriptStageIdentity.For(first with {Scripts=[..first.Scripts,new("settings.user","settings/Invoke-UserSetting.ps1",new string('c',64),false)]}));
 }
 [Fact] public void Unregistered_script_id_is_not_a_command_channel()=>Assert.Throws<InvalidDataException>(()=>RegisteredScripts.OpenVerified(Path.GetTempPath(),"cmd.exe"));
 [Fact] public async Task Native_source_does_not_invent_data_on_non_windows()
 {
  if(!OperatingSystem.IsWindows())await Assert.ThrowsAsync<PlatformNotSupportedException>(()=>new NativeFactsSource(Path.GetTempPath()).ReadAsync(["tpm.ready"],default));
 }
}
