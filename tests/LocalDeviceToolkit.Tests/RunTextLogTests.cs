using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Logging;
using System.Text;
using Xunit;

namespace LocalDeviceToolkit.Tests;

public sealed class RunTextLogTests : IDisposable
{
 readonly string root = Path.Combine(Path.GetTempPath(), "LocalToolkit-log-" + Guid.NewGuid().ToString("N"));
 readonly FrozenClock clock = new();
 public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

 [Fact] public async Task Same_clock_creates_unique_logs_and_preserves_prior_content()
 {
  await using var first = await RunTextLog.CreateAsync(root, TestData.Context, clock, default);
  await first.AppendAsync("review", "first review", default);
  var original = await first.ReadAsync(default);
  await using var second = await RunTextLog.CreateAsync(root, TestData.Context, clock, default);
  Assert.NotEqual(first.RunId, second.RunId);
  Assert.NotEqual(first.FilePath, second.FilePath);
  Assert.StartsWith("20261015-123456789-", Path.GetFileName(first.FilePath));
  Assert.EndsWith(".log", first.FilePath);
  Assert.Equal(original, await first.ReadAsync(default));
  Assert.Equal(2, Directory.GetFiles(root).Length);
 }

 [Fact] public async Task Review_and_action_append_to_same_live_readable_file_with_original_identity()
 {
  await using var log = await RunTextLog.CreateAsync(root, TestData.Context, clock, default);
  await log.AppendAsync("review", "HKCU\\Example expected=é", default);
  var before = await log.ReadAsync(default);
  await log.AppendAsync("dispatch", "actual actor=administrator", default);
  await log.AppendAsync("verified", "after=é", default);
  var after = await log.ReadAsync(default);
  Assert.StartsWith(before, after);
  Assert.Contains(TestData.Context.OriginalUserSid, after);
  Assert.Contains("TEST\\\\engineer", after);
  Assert.Contains(TestData.Context.DeviceId, after);
  Assert.Contains(TestData.Context.ComputerName, after);
  Assert.Contains("2026-10-15T12:34:56.7890000+00:00", after);
  Assert.Contains("companion", after, StringComparison.OrdinalIgnoreCase);
  Assert.Contains("after=é", after);
  await using var readerStream = new FileStream(log.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
  using var reader = new StreamReader(readerStream, Encoding.UTF8);
  Assert.Equal(after, await reader.ReadToEndAsync());
 }

 [Fact] public async Task Control_characters_cannot_fabricate_log_lines()
 {
  await using var log = await RunTextLog.CreateAsync(root, TestData.Context with { OriginalUserName = "name\r\nFORGED" }, clock, default);
  var count = (await log.ReadAsync(default)).Split('\n').Length;
  await log.AppendAsync("stage\nforged", "Unicode 世界 😀\r\nFORGED\t\0\u001b\u2028\u2029\u0085", default);
  var text = await log.ReadAsync(default);
  Assert.Equal(count + 1, text.Split('\n').Length);
  Assert.Contains("世界 😀", text);
  Assert.Contains("\\r\\nFORGED", text);
  Assert.DoesNotContain('\r', text);
  Assert.DoesNotContain('\0', text);
  Assert.DoesNotContain('\u001b', text);
  Assert.DoesNotContain('\u2028', text);
  Assert.DoesNotContain('\u2029', text);
  Assert.DoesNotContain('\u0085', text);
 }

 [Fact] public async Task Cancellation_and_message_limits_do_not_mutate_file()
 {
  await using var log = await RunTextLog.CreateAsync(root, TestData.Context, clock, default);
  var original = await log.ReadAsync(default);
  using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
  await Assert.ThrowsAnyAsync<OperationCanceledException>(() => log.AppendAsync("review", "cancelled", cancelled.Token));
  await Assert.ThrowsAnyAsync<OperationCanceledException>(() => log.ReadAsync(cancelled.Token));
  await Assert.ThrowsAsync<ArgumentException>(() => log.AppendAsync("review", new string('x', 65537), default));
  await Assert.ThrowsAsync<ArgumentException>(() => log.AppendAsync(new string('x', 65537), "review", default));
  Assert.Equal(original, await log.ReadAsync(default));
  await log.AppendAsync("review", "still usable", default);
 }

 [Fact] public async Task Total_file_size_is_bounded_without_partial_record()
 {
  await using var log = await RunTextLog.CreateAsync(root, TestData.Context, clock, default);
  var message = new string('x', 60000);
  for (var i = 0; i < 279; i++) await log.AppendAsync("check", message, default);
  var original = await log.ReadAsync(default);
  await Assert.ThrowsAsync<IOException>(() => log.AppendAsync("check", message, default));
  Assert.Equal(original, await log.ReadAsync(default));
  Assert.True(new FileInfo(log.FilePath).Length <= 16 * 1024 * 1024);
 }

 [Fact] public async Task Existing_file_as_directory_fails_without_overwriting_it()
 {
  Directory.CreateDirectory(root);
  var path = Path.Combine(root, "existing"); await File.WriteAllTextAsync(path, "preserve me");
  await Assert.ThrowsAnyAsync<IOException>(() => RunTextLog.CreateAsync(path, TestData.Context, clock, default));
  Assert.Equal("preserve me", await File.ReadAllTextAsync(path));
 }

 [Fact] public async Task An_unwritable_log_directory_reports_access_failure()
 {
  if (OperatingSystem.IsWindows()) return;
  Directory.CreateDirectory(root);
  File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
  try { await Assert.ThrowsAsync<UnauthorizedAccessException>(() => RunTextLog.CreateAsync(root, TestData.Context, clock, default)); }
  finally { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
  Assert.Empty(Directory.GetFiles(root));
 }

 [Fact] public async Task Cancelled_creation_does_not_create_directory()
 {
  using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
  await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunTextLog.CreateAsync(root, TestData.Context, clock, cancelled.Token));
  Assert.False(Directory.Exists(root));
 }

 [Fact] public async Task Disposal_is_idempotent_preserves_file_and_rejects_operations()
 {
  var log = await RunTextLog.CreateAsync(root, TestData.Context, clock, default);
  await log.AppendAsync("review", "saved", default);
  var original = await log.ReadAsync(default);
  await log.DisposeAsync(); await log.DisposeAsync();
  Assert.Equal(original, await File.ReadAllTextAsync(log.FilePath));
  await Assert.ThrowsAsync<ObjectDisposedException>(() => log.AppendAsync("review", "late", default));
  await Assert.ThrowsAsync<ObjectDisposedException>(() => log.ReadAsync(default));
 }

 [Fact] public async Task Concurrent_appends_are_complete_unique_lines()
 {
  await using var log = await RunTextLog.CreateAsync(root, TestData.Context, clock, default);
  await Task.WhenAll(Enumerable.Range(0, 32).Select(i => log.AppendAsync("check", "message=" + i.ToString("D2"), default)));
  var lines = (await log.ReadAsync(default)).Split('\n');
  for (var i = 0; i < 32; i++) Assert.Single(lines, line => line.EndsWith("message=" + i.ToString("D2"), StringComparison.Ordinal));
 }

 [Fact] public async Task Reparse_directory_is_refused_at_creation()
 {
  if (OperatingSystem.IsWindows()) return;
  Directory.CreateDirectory(root); var target = Path.Combine(root, "target"); Directory.CreateDirectory(target);
  var link = Path.Combine(root, "link"); Directory.CreateSymbolicLink(link, target);
  try { await Assert.ThrowsAsync<EvidenceValidationException>(() => RunTextLog.CreateAsync(link, TestData.Context, clock, default)); Assert.Empty(Directory.GetFiles(target)); }
  finally { Directory.Delete(link); }
 }

 [Fact] public async Task Replaced_path_is_refused_and_failed_append_permanently_blocks_writes()
 {
  if (OperatingSystem.IsWindows()) return;
  await using var log = await RunTextLog.CreateAsync(root, TestData.Context, clock, default);
  var path = log.FilePath; var moved = path + ".moved"; var target = Path.Combine(root, "target.log");
  await File.WriteAllTextAsync(target, "untouched"); File.Move(path, moved); File.CreateSymbolicLink(path, target);
  try
  {
   await Assert.ThrowsAsync<EvidenceValidationException>(() => log.AppendAsync("action", "must fail", default));
   await Assert.ThrowsAsync<EvidenceValidationException>(() => log.ReadAsync(default));
   Assert.Equal("untouched", await File.ReadAllTextAsync(target));
  }
  finally { File.Delete(path); File.Move(moved, path); }
  await Assert.ThrowsAsync<IOException>(() => log.AppendAsync("action", "still blocked", default));
  Assert.DoesNotContain("must fail", await log.ReadAsync(default));
 }

 [Fact] public async Task Ordinary_path_replacement_cannot_redirect_owned_append_read_or_disposal()
 {
  if (OperatingSystem.IsWindows()) return; // The held handle refuses renaming on Windows.
  var log = await RunTextLog.CreateAsync(root, TestData.Context, clock, default);
  var moved = log.FilePath + ".owned";
  File.Move(log.FilePath, moved); await File.WriteAllTextAsync(log.FilePath, "replacement untouched");
  try
  {
   await log.AppendAsync("check", "only owned file", default);
   Assert.Contains("only owned file", await log.ReadAsync(default));
   Assert.Equal("replacement untouched", await File.ReadAllTextAsync(log.FilePath));
  }
  finally { await log.DisposeAsync(); }
  Assert.Contains("only owned file", await File.ReadAllTextAsync(moved));
  Assert.Equal("replacement untouched", await File.ReadAllTextAsync(log.FilePath));
 }

 sealed class FrozenClock : TimeProvider
 {
  public override DateTimeOffset GetUtcNow() => new(2026, 10, 15, 12, 34, 56, 789, TimeSpan.Zero);
 }
}
