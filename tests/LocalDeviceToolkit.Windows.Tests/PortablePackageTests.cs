using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class PortablePackageTests : IDisposable
{
 readonly string root = Path.Combine(Path.GetTempPath(), "portable-integrity-" + Guid.NewGuid());
 const string Commit = "0123456789abcdef0123456789abcdef01234567";
 public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
 static string Script([System.Runtime.CompilerServices.CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "build", "Test-Portable.ps1"));
 string Fixture(string defect = "", bool lightweight = false)
 {
  Directory.CreateDirectory(root); var path = Path.Combine(root, Guid.NewGuid() + ".zip"); var files = new Dictionary<string, byte[]>();
  foreach (var prefix in new[] { "", "worker/" })
  {
   var name = prefix.Length == 0 ? "LocalDeviceToolkit.App" : "LocalDeviceToolkit.Worker";
   var pe = new byte[256]; pe[0] = 0x4d; pe[1] = 0x5a; pe[0x3c] = 0x80; pe[0x80] = 0x50; pe[0x81] = 0x45; pe[0x84] = 0x64; pe[0x85] = 0x86;
   files[prefix + name + ".exe"] = pe;
   foreach (var dll in lightweight ? new[]{name+".dll"} : new[] { name + ".dll", "coreclr.dll", "PresentationFramework.dll", "System.Private.CoreLib.dll", "hostfxr.dll", "hostpolicy.dll" }) files[prefix + dll] = [];
   var frameworks=new[] { new { name = "Microsoft.NETCore.App", version = "10.0.0" }, new { name = "Microsoft.WindowsDesktop.App", version = "10.0.0" } };
   files[prefix + name + ".runtimeconfig.json"] = Encoding.UTF8.GetBytes(lightweight ? JsonSerializer.Serialize(new{runtimeOptions=new{tfm="net10.0",frameworks}}) : JsonSerializer.Serialize(new { runtimeOptions = new { tfm = "net10.0", includedFrameworks = frameworks } }));
  }
  var repository = Path.GetDirectoryName(Path.GetDirectoryName(Script())!)!;
  foreach(var leaf in new[]{"Start.cmd","Start.ps1","Runtime.ps1","Run-Silent.cmd","Diagnostics.config.example","ProbeInputs.json.example"})files[leaf]=File.ReadAllBytes(Path.Combine(repository,"packaging",leaf));
  files["Start.exe"]=files["LocalDeviceToolkit.App.exe"].ToArray();
  foreach (var relative in new[] { "NOTICE.md", "HOW-TO-USE.txt", "docs/LOCAL-USAGE.md", "provenance/import-manifest.json", "catalogue/reference.json", "catalogue/local-windows.json", "catalogue/tasks.json", "catalogue/workflows.json", "catalogue/pins.json", "catalogue/local-probes.json", "catalogue/windows-support.json" }) files[relative] = File.ReadAllBytes(Path.Combine(repository, relative));
  foreach(var tool in new[]{"New-DeploymentProfile.ps1","Test-Portable.ps1"})files["tools/"+tool]=File.ReadAllBytes(Path.Combine(repository,"build",tool));
  foreach (var relative in new[]{"registry.json"}.Concat(LocalDeviceToolkit.Windows.Inspection.RegisteredScripts.Manifest.Scripts.Select(s=>s.Path)))
   foreach (var prefix in new[] { "scripts/", "worker/scripts/" }) files[prefix + relative] = File.ReadAllBytes(Path.Combine(repository, "scripts", relative));
  files["LocalDeviceToolkit.App.dll"] = [0];
  if (defect == "case-collision") files["NOTICE.MD"] = [1];
  if (defect == "too-many-entries") for (var i = 0; i < 2000; i++) files["extra/" + i + ".txt"] = [];
  if (defect == "missing-notice") files.Remove("NOTICE.md");
  if (defect == "rehashed-script") files["worker/scripts/settings/Invoke-UserSetting.ps1"] = Encoding.UTF8.GetBytes("# changed bytes without matching the registry pin");
  if (defect == "missing-worker") files.Remove("worker/LocalDeviceToolkit.Worker.exe");
  if (defect == "unsafe-path") files["../outside.txt"] = [1];
  if (defect == "wrong-machine") files["worker/LocalDeviceToolkit.Worker.exe"][0x84] = 0xaa;
  if (defect == "framework-dependent") files["LocalDeviceToolkit.App.runtimeconfig.json"] = Encoding.UTF8.GetBytes("{\"runtimeOptions\":{\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"}}}");
  if(defect=="missing-launcher")files.Remove("Start.ps1");
  if(defect=="missing-silent")files.Remove("Run-Silent.cmd");
  var entries = files.Select(f => new { path = f.Key, bytes = f.Value.Length, sha256 = Convert.ToHexString(SHA256.HashData(f.Value)).ToLowerInvariant() }).ToArray();
  var manifest = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, commit = Commit, builtAt = "2026-10-15T00:00:00.0000000+00:00", runtime = "win-x64", selfContained = !lightweight, workingTreeDirty = defect == "dirty", files = entries });
  if (defect == "duplicate-manifest-key") manifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifest).Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"));
  if (defect == "unknown-manifest-field") manifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifest).Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"unexpected\":true"));
  if (defect == "oversized-manifest") manifest = new byte[8 * 1024 * 1024 + 1];
  if (defect == "extra-file") files["unlisted.txt"] = [1];
  if (defect == "hash-mismatch") files["LocalDeviceToolkit.App.dll"] = [1];
  using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
   foreach (var f in files.Append(new KeyValuePair<string, byte[]>("package-manifest.json", manifest))) { var entry = zip.CreateEntry(f.Key); if (defect == "reparse-entry" && f.Key == "NOTICE.md") entry.ExternalAttributes = 0x400; using var output = entry.Open(); output.Write(f.Value); }
  if (defect == "uncompressed-limit")
  {
   var bytes = File.ReadAllBytes(path);
   for (var i = 0; i < bytes.Length - 28; i++) if (bytes[i] == 0x50 && bytes[i+1] == 0x4b && bytes[i+2] == 1 && bytes[i+3] == 2) { BitConverter.GetBytes(1073741825u).CopyTo(bytes, i + 24); break; }
   File.WriteAllBytes(path, bytes);
  }
  return path;
 }
 async Task<(int Exit, string Output)> Verify(string zip, string result, params string[] extra)
 {
  var shell = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe") : Environment.GetEnvironmentVariable("TOOLKIT_TEST_PWSH") ?? "pwsh";
  var start = new ProcessStartInfo(shell) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
  if (!OperatingSystem.IsWindows()) { start.Environment["XDG_CACHE_HOME"] = Path.Combine(root, "shell-cache"); start.Environment["XDG_CONFIG_HOME"] = Path.Combine(root, "shell-config"); start.Environment["XDG_DATA_HOME"] = Path.Combine(root, "shell-data"); }
  foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", Script(), "-ZipPath", zip, "-ResultPath", result }.Concat(extra)) start.ArgumentList.Add(argument);
  using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
  using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60)); try { await process.WaitForExitAsync(stop.Token); } catch { process.Kill(true); throw; }
  return (process.ExitCode, (await output) + (await errors));
 }
 [Fact] public async Task Synthetic_package_proves_integrity_only_and_never_native_launch()
 {
  var zip = Fixture(); var original = await File.ReadAllBytesAsync(zip); var result = Path.Combine(root, "result.json");
  var verification = await Verify(zip, result, "-ExpectedCommit", Commit, "-ExpectedSha256", Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant()); Assert.True(verification.Exit == 0, verification.Output);
  using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(result)); Assert.False(json.RootElement.GetProperty("NativeLaunchVerified").GetBoolean()); Assert.Equal(Commit, json.RootElement.GetProperty("Commit").GetString()); Assert.Equal(original, await File.ReadAllBytesAsync(zip)); Assert.False(Directory.Exists(Path.Combine(root, "worker")));
 }
 [Fact] public async Task Lightweight_package_declares_runtime_requirement_and_checks_all_files()
 {
  var zip=Fixture(lightweight:true);var result=Path.Combine(root,"result.json");var verification=await Verify(zip,result);
  Assert.True(verification.Exit==0,verification.Output);using var json=JsonDocument.Parse(await File.ReadAllBytesAsync(result));Assert.False(json.RootElement.GetProperty("SelfContained").GetBoolean());Assert.False(json.RootElement.GetProperty("NativeLaunchVerified").GetBoolean());
 }
 [Theory] [InlineData(false)] [InlineData(true)] public async Task Package_without_silent_command_is_refused(bool lightweight)
 {
  var zip=Fixture("missing-silent",lightweight);var verification=await Verify(zip,Path.Combine(root,"result.json"));
  Assert.NotEqual(0,verification.Exit);Assert.Contains("Silent diagnostic asset is missing",verification.Output);
 }
 [Fact] public async Task Lightweight_package_without_launcher_is_refused()
 {
  var zip=Fixture("missing-launcher",lightweight:true);var result=Path.Combine(root,"result.json");var verification=await Verify(zip,result);
  Assert.NotEqual(0,verification.Exit);Assert.Contains("Lightweight startup asset is missing",verification.Output);Assert.False(File.Exists(result));
 }
 [Theory] [InlineData("missing-worker")] [InlineData("hash-mismatch")] [InlineData("unsafe-path")] [InlineData("wrong-machine")] [InlineData("framework-dependent")] [InlineData("duplicate-manifest-key")] [InlineData("missing-notice")] [InlineData("rehashed-script")] [InlineData("case-collision")] [InlineData("too-many-entries")] [InlineData("unknown-manifest-field")] [InlineData("oversized-manifest")] [InlineData("extra-file")] [InlineData("reparse-entry")] [InlineData("uncompressed-limit")]
 public async Task Invalid_package_refused_without_result_or_extract(string defect)
 {
  var zip = Fixture(defect); var original = await File.ReadAllBytesAsync(zip); var result = Path.Combine(root, "result.json"); var verification = await Verify(zip, result);
  Assert.NotEqual(0, verification.Exit);
  var reason = defect switch { "missing-worker" => "Critical bundled", "hash-mismatch" => "Manifest bytes/hash mismatch", "unsafe-path" => "Unsafe ZIP", "wrong-machine" => "AMD64 PE", "framework-dependent" => "Framework-dependent", "duplicate-manifest-key" => "Duplicate JSON member", "missing-notice" => "Critical source asset", "rehashed-script" => "Script registry pin mismatch", "case-collision" => "duplicate/case-colliding", "too-many-entries" => "2000-entry limit", "unknown-manifest-field" => "Unknown JSON field", "oversized-manifest" => "entry exceeds", "extra-file" => "files absent", "reparse-entry" => "link/reparse entry", "uncompressed-limit" => "1 GiB uncompressed limit", _ => throw new InvalidOperationException() };
  Assert.Contains(reason, verification.Output); Assert.False(File.Exists(result)); Assert.Equal(original, await File.ReadAllBytesAsync(zip)); Assert.Single(Directory.GetFiles(root));
 }
 [Fact] public async Task Existing_result_is_preserved()
 {
  var zip = Fixture(); var result = Path.Combine(root, "result.json"); await File.WriteAllTextAsync(result, "preserve"); var verification = await Verify(zip, result);
  Assert.NotEqual(0, verification.Exit); Assert.Contains("never overwritten", verification.Output); Assert.Equal("preserve", await File.ReadAllTextAsync(result));
 }
 [Fact] public async Task Expected_commit_mismatch_is_refused()
 {
  var zip = Fixture(); var result = Path.Combine(root, "result.json"); var verification = await Verify(zip, result, "-ExpectedCommit", new string('a', 40)); Assert.NotEqual(0, verification.Exit); Assert.Contains("does not match ExpectedCommit", verification.Output); Assert.False(File.Exists(result));
 }
 [Fact] public async Task Dirty_package_is_labelled_as_a_review_artifact()
 {
  var zip = Fixture("dirty"); var result = Path.Combine(root, "result.json"); var verification = await Verify(zip, result); Assert.True(verification.Exit == 0, verification.Output);
  using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(result)); Assert.True(json.RootElement.GetProperty("WorkingTreeDirty").GetBoolean()); Assert.Equal("DirtyReviewArtifact", json.RootElement.GetProperty("ArtifactKind").GetString());
 }
 [Fact] public async Task Expected_zip_hash_mismatch_is_refused()
 {
  var zip = Fixture(); var result = Path.Combine(root, "result.json"); var verification = await Verify(zip, result, "-ExpectedSha256", new string('a', 64)); Assert.NotEqual(0, verification.Exit); Assert.Contains("does not match ExpectedSha256", verification.Output); Assert.False(File.Exists(result));
 }

}
