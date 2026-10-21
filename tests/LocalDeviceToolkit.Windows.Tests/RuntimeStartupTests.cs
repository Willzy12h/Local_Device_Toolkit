using System.Diagnostics;
using Xunit;

namespace LocalDeviceToolkit.Windows.Tests;

public sealed class RuntimeStartupTests
{
 [Theory]
 [InlineData("discovery")]
 [InlineData("selection")]
 [InlineData("rollforward")]
 [InlineData("oldercore")]
 [InlineData("compatiblepatch")]
 [InlineData("install")]
 [InlineData("corrupt")]
 [InlineData("traversal")]
 [InlineData("partial")]
 [InlineData("elevated")]
 public async Task Startup_checks_and_private_install_preserve_safety(string scenario)
 {
  var repository=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!,"../.."));
  Assert.True(File.Exists(Path.Combine(repository,"packaging/Runtime.ps1")),"The runtime startup implementation is missing.");
  var shell=OperatingSystem.IsWindows()?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell/v1.0/powershell.exe"):Environment.GetEnvironmentVariable("TOOLKIT_TEST_PWSH")??"pwsh";
  var start=new ProcessStartInfo(shell){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
  foreach(var arg in new[]{"-NoProfile","-NonInteractive","-File",Path.Combine(repository,"tests/LocalDeviceToolkit.Windows.Tests/RuntimeStartupFixture.ps1"),"-Repository",repository,"-Scenario",scenario})start.ArgumentList.Add(arg);
  using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
  try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(true);throw;}
  Assert.True(process.ExitCode==0,(await output)+(await errors));Assert.Contains("PASS "+scenario,await output);
 }
 static string Source([System.Runtime.CompilerServices.CallerFilePath]string path="")=>path;
}
