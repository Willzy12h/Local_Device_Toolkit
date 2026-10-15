using System.Diagnostics;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class PortablePublisherTests : IDisposable
{
 readonly string root = Path.Combine(Path.GetTempPath(), "portable-producer-" + Guid.NewGuid());
 public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
 static string Script([System.Runtime.CompilerServices.CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "build", "Publish-Portable.ps1"));
 [Fact] public async Task Actual_producer_normalizes_single_backslashes_and_emits_canonical_nested_zip_entries()
 {
  Directory.CreateDirectory(root); var harness = Path.Combine(root, "producer-test.ps1");
  await File.WriteAllTextAsync(harness, """
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($env:TOOLKIT_PUBLISH_SCRIPT,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Producer syntax failed.'}
foreach($name in @('ConvertTo-PortableEntryName','Get-PortablePackageFiles','New-PortableArchive')){
 $definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
 if($null -eq $definition){throw ('Required real producer function is missing: '+$name)}
 . ([scriptblock]::Create($definition.Extent.Text))
}
if((ConvertTo-PortableEntryName 'worker\nested\tool.dll') -cne 'worker/nested/tool.dll'){throw 'Single backslashes were not normalized.'}
$source=Join-Path $PSScriptRoot 'source'
[void][IO.Directory]::CreateDirectory((Join-Path $source 'worker/nested'))
[IO.File]::WriteAllText((Join-Path $source 'root.txt'),'root-content')
[IO.File]::WriteAllText((Join-Path $source 'worker/nested/tool.dll'),'nested-content')
$destination=Join-Path $PSScriptRoot 'actual.zip'
New-PortableArchive -SourceDirectory $source -ArchivePath $destination
$archive=[IO.Compression.ZipFile]::OpenRead($destination)
try{
 $names=@($archive.Entries|ForEach-Object {$_.FullName})
 if($names.Count -ne 2 -or -not ($names -ccontains 'root.txt') -or -not ($names -ccontains 'worker/nested/tool.dll')){throw ('Noncanonical produced names: '+($names -join ','))}
 foreach($entry in $archive.Entries){if($entry.FullName.Contains('\')){throw 'Produced ZIP entry contains a backslash.'}}
}finally{$archive.Dispose()}
$before=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
$refused=$false;try{New-PortableArchive -SourceDirectory $source -ArchivePath $destination}catch{$refused=$true}
if(-not $refused -or (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -cne $before){throw 'Existing archive was replaced.'}
'PASS: actual producer normalizes Windows separators and emits canonical nested ZIP entries without replacement.'
""");
  var shell = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe") : Environment.GetEnvironmentVariable("TOOLKIT_TEST_PWSH") ?? "pwsh";
  var start = new ProcessStartInfo(shell) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
  start.Environment["TOOLKIT_PUBLISH_SCRIPT"] = Script();
  if (OperatingSystem.IsWindows()) { start.Environment["PSModulePath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "Modules"); start.Environment.Remove("PSExecutionPolicyPreference"); }
  if (!OperatingSystem.IsWindows()) { start.Environment["XDG_CACHE_HOME"] = Path.Combine(root, "shell-cache"); start.Environment["XDG_CONFIG_HOME"] = Path.Combine(root, "shell-config"); start.Environment["XDG_DATA_HOME"] = Path.Combine(root, "shell-data"); }
  foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", harness }) start.ArgumentList.Add(argument);
  using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
  using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60)); try { await process.WaitForExitAsync(stop.Token); } catch { process.Kill(true); throw; }
  var text = (await output) + (await errors); Assert.True(process.ExitCode == 0, text); Assert.Contains("PASS: actual producer", text);
 }
}
