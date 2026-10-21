using System.Diagnostics;
using Xunit;

namespace LocalDeviceToolkit.Windows.Tests;

public sealed class CmdStartupTests
{
 [WindowsCmdFact]
 public async Task Cmd_startup_overrides_only_the_child_process_policy_and_handles_spaces()
 {
  var root = Path.Combine(Path.GetTempPath(), "toolkit cmd fixture " + Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  try
  {
   var repository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!, "../.."));
   var launcher = Path.Combine(root, "Start.cmd");
   File.Copy(Path.Combine(repository, "packaging/Start.cmd"), launcher);
   // Inert startup substitute: never loads the actual runtime installer or application.
   await File.WriteAllTextAsync(Path.Combine(root, "Start.ps1"), "if ($env:PSExecutionPolicyPreference -ne 'Bypass') { throw 'Expected process-only policy' }; Write-Output 'START-CMD-PROCESS-BYPASS'");
   var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
   { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
   foreach (var arg in new[] { "/d", "/c", "call", launcher }) start.ArgumentList.Add(arg);
   start.Environment["PSExecutionPolicyPreference"] = "Restricted";
   var parentPreference = Environment.GetEnvironmentVariable("PSExecutionPolicyPreference");
   using var process = Process.Start(start)!;
   process.StandardInput.Close();
   var output = process.StandardOutput.ReadToEndAsync();
   var errors = process.StandardError.ReadToEndAsync();
   using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
   try { await process.WaitForExitAsync(timeout.Token); }
   catch { if (!process.HasExited) process.Kill(true); throw; }
   var stdout = await output; var stderr = await errors;
   Assert.Contains("START-CMD-PROCESS-BYPASS", stdout);
   Assert.Equal(0, process.ExitCode);
   Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
   Assert.Equal("Restricted", start.Environment["PSExecutionPolicyPreference"]);
   Assert.Equal(parentPreference, Environment.GetEnvironmentVariable("PSExecutionPolicyPreference"));
  }
  finally { Directory.Delete(root, true); }
 }

 static string Source([System.Runtime.CompilerServices.CallerFilePath] string source = "") => source;
}

public sealed class WindowsCmdFactAttribute : FactAttribute
{
 public WindowsCmdFactAttribute()
 {
  if (!OperatingSystem.IsWindows()) Skip = "Requires native Windows CMD and Windows PowerShell; covered by Windows CI.";
 }
}
