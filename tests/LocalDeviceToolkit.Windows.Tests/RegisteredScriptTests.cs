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
 [Fact] public void Unregistered_script_id_is_not_a_command_channel()=>Assert.Throws<InvalidDataException>(()=>RegisteredScripts.OpenVerified(Path.GetTempPath(),"cmd.exe"));
 [Fact] public async Task Native_source_does_not_invent_data_on_non_windows()
 {
  if(!OperatingSystem.IsWindows())await Assert.ThrowsAsync<PlatformNotSupportedException>(()=>new NativeFactsSource(Path.GetTempPath()).ReadAsync(["tpm.ready"],default));
 }
}
