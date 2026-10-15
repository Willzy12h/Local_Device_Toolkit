using System.Diagnostics;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class DeploymentProfileDraftTests:IDisposable
{
 readonly string root=Path.Combine(Path.GetTempPath(),"deployment-draft-"+Guid.NewGuid().ToString("N"));
 public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
 static string Script=>ScriptFromSource();
 static string ScriptFromSource([System.Runtime.CompilerServices.CallerFilePath]string source="")
 {
  var script=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"..","..","build","New-DeploymentProfile.ps1"));
  if(!File.Exists(script))throw new FileNotFoundException("Deployment draft helper is missing from the source checkout.",script);
  return script;
 }
 static async Task<(int Exit,string Output)> PowerShell(params string[] arguments)
 {
  var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
  foreach(var argument in new[]{"-NoProfile","-NonInteractive"}.Concat(arguments))start.ArgumentList.Add(argument);
  using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);throw;}
  return(process.ExitCode,(await output)+(await error));
 }
 [Fact] public async Task Helper_parses_with_installed_Windows_PowerShell_51()
 {
  if(!OperatingSystem.IsWindows())return;
  // Pass the path as process environment data, never interpolate it as PowerShell source.
  var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
  start.Environment["TOOLKIT_DRAFT_SCRIPT"]=Script;
  foreach(var argument in new[]{"-NoProfile","-NonInteractive","-Command","$t=$null;$e=$null;[void][Management.Automation.Language.Parser]::ParseFile($env:TOOLKIT_DRAFT_SCRIPT,[ref]$t,[ref]$e);if($e.Count){$e|Out-String|Write-Error;exit 1};if($PSVersionTable.PSVersion.Major -ne 5){throw 'Windows PowerShell 5.1 required'}"})start.ArgumentList.Add(argument);
  using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();Assert.True(process.ExitCode==0,(await output)+(await error));
 }
 [Theory][InlineData("../setup.msi")][InlineData("folder\\setup.msi")][InlineData("/setup.msi")]
 public async Task Unsafe_relative_entry_points_are_refused_before_output(string entry)
 {
  if(!OperatingSystem.IsWindows())return;
  var source=Path.Combine(root,"source");Directory.CreateDirectory(source);var output=Path.Combine(root,"profile.json");
  var result=await PowerShell("-File",Script,"-SourceDirectory",source,"-EntryPoint",entry,"-ControlId","APP-WIN-001","-Format","MachineMsi","-OutputPath",output);
  Assert.NotEqual(0,result.Exit);Assert.False(File.Exists(output));Assert.Contains("relative",result.Output,StringComparison.OrdinalIgnoreCase);
 }
 [Fact] public async Task Unsigned_installer_is_refused_without_executing_or_creating_a_draft()
 {
  if(!OperatingSystem.IsWindows())return;
  var source=Path.Combine(root,"source");Directory.CreateDirectory(source);var installer=Path.Combine(source,"setup.msi");var bytes=new byte[]{1,2,3,4};await File.WriteAllBytesAsync(installer,bytes);var output=Path.Combine(root,"profile.json");
  var result=await PowerShell("-File",Script,"-SourceDirectory",source,"-EntryPoint","setup.msi","-ControlId","APP-WIN-001","-Format","MachineMsi","-OutputPath",output);
  Assert.NotEqual(0,result.Exit);Assert.Contains("Authenticode",result.Output,StringComparison.OrdinalIgnoreCase);Assert.False(File.Exists(output));Assert.Equal(bytes,await File.ReadAllBytesAsync(installer));
 }
 [Fact] public async Task Existing_output_is_never_overwritten()
 {
  if(!OperatingSystem.IsWindows())return;
  var source=Path.Combine(root,"source");Directory.CreateDirectory(source);var output=Path.Combine(root,"profile.json");await File.WriteAllTextAsync(output,"keep this exact content");
  var result=await PowerShell("-File",Script,"-SourceDirectory",source,"-EntryPoint","setup.msi","-ControlId","APP-WIN-001","-Format","MachineMsi","-OutputPath",output);
  Assert.NotEqual(0,result.Exit);Assert.Contains("overwriting",result.Output,StringComparison.OrdinalIgnoreCase);Assert.Equal("keep this exact content",await File.ReadAllTextAsync(output));
 }
}
