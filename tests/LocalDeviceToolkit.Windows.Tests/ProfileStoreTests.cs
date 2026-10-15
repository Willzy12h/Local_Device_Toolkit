using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Windows.Deployments;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class ProfileStoreTests : IDisposable
{
 readonly string root = Path.Combine(Path.GetTempPath(), "profile-store-" + Guid.NewGuid());
 public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
 static DeploymentProfile Profile() => ApprovedProfileStore.WithContentId(new("", "APP-WIN-001", DeploymentFormat.MachineMsi, "{11111111-2222-3333-4444-555555555555}", "1.2.3", "Example App", "Example Publisher", OperationScope.Machine, "x64", "setup.msi", [new("setup.msi", new string('a', 64), new string('b', 64))], TimeSpan.FromMinutes(20)));
 [Fact] public async Task Immutable_content_addressed_profile_roundtrips_after_restart()
 {
  var profile = Profile(); var store = new ApprovedProfileStore(root); await store.SaveAsync(profile, default);
  var reopened = await new ApprovedProfileStore(root).LoadAsync(profile.Id, default);
  Assert.Equal(profile.Id, reopened.Id); Assert.Equal(profile.Files[0], Assert.Single(reopened.Files)); Assert.Equal(CanonicalJson.Digest(profile with { Id = "" }), reopened.Id);
  await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(profile, default));
 }
 [Fact] public async Task Reimport_can_restore_a_lost_preview_cache_without_overwriting_protected_approval()
 {
  var profile=Profile();var store=new ApprovedProfileStore(root);var first=await store.SaveOrLoadIdenticalAsync(profile,default);var path=Path.Combine(root,profile.Id+".json");var bytes=await File.ReadAllBytesAsync(path);
  var reopened=await new ApprovedProfileStore(root).SaveOrLoadIdenticalAsync(profile,default);Assert.Equal(first.Id,reopened.Id);Assert.Equal(bytes,await File.ReadAllBytesAsync(path));
  await File.WriteAllBytesAsync(path,StrictJson.Bytes(profile with{ProductName="tampered"}));await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveOrLoadIdenticalAsync(profile,default));
 }
 [Fact] public async Task A_queued_profile_can_be_removed_after_its_preview_file_disappears()
 {
  var profile=Profile();var store=new ApprovedProfileStore(root);await store.SaveAsync(profile,default);
  var registry=new LocalDeviceToolkit.Engine.Planning.OperationRegistry(LocalDeviceToolkit.Engine.Catalogue.WorkflowCatalogue.LoadShipped(),[new ApprovedPackageOperation(store,new FileDeploymentPendingStore(Path.Combine(root,"pending")),true)]);
  var context=new DeviceContext("device","PC","S-1-5-21-123","engineer",false,new Version(10,0,26200),"x64",true);var queue=new LocalDeviceToolkit.Engine.Planning.DeploymentQueue(context,registry);
  var request=new ActionRequest("apps.installation","packages.install",StrictJson.Value(new DeploymentParameters(profile.Id,Path.GetFullPath("media"))),new("setup","applications"));queue.Add(request);File.Delete(Path.Combine(root,profile.Id+".json"));
  Assert.True(queue.Remove(queue.Requests[0]));Assert.Empty(queue.Requests);
 }
 [Fact] public async Task Mutated_profile_and_unsafe_id_are_refused()
 {
  var store = new ApprovedProfileStore(root); var profile = Profile();
  await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(profile with { Publisher = "changed" }, default));
  await Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync("../outside", default));
 }
 [Fact] public async Task Corrupt_and_misnamed_files_do_not_hide_a_healthy_profile()
 {
  var store = new ApprovedProfileStore(root); var profile = Profile(); await store.SaveAsync(profile, default);
  await File.WriteAllTextAsync(Path.Combine(root, new string('c', 64) + ".json"), "{broken");
  await File.WriteAllBytesAsync(Path.Combine(root, "unexpected.json"), StrictJson.Bytes(profile));
  var list = await store.ReadAllAsync(default); Assert.Single(list.Profiles); Assert.Equal(2, list.Notices.Count); Assert.False(list.LimitReached);
 }
 [Fact] public async Task Payload_must_match_the_hash_filename()
 {
  var store = new ApprovedProfileStore(root); var profile = Profile(); await store.SaveAsync(profile, default);
  await File.WriteAllBytesAsync(Path.Combine(root, profile.Id + ".json"), StrictJson.Bytes(profile with { Version = "9.9" }));
  await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(profile.Id, default));
 }
 [Fact] public async Task Import_is_strict_bounded_and_can_assign_a_blank_content_id()
 {
  Directory.CreateDirectory(root); var path = Path.Combine(root, "import.json"); var profile = Profile();
  await File.WriteAllBytesAsync(path, StrictJson.Bytes(profile with { Id = "" }));
  Assert.Equal(profile.Id, (await ApprovedProfileStore.ReadImportAsync(path, default)).Id);
  await File.WriteAllTextAsync(path, "{\"id\":\"\",\"id\":\"\"}");
  await Assert.ThrowsAsync<InvalidDataException>(() => ApprovedProfileStore.ReadImportAsync(path, default));
  await using (var oversized = new FileStream(path, FileMode.Create)) oversized.SetLength(256 * 1024 + 1);
  await Assert.ThrowsAsync<InvalidDataException>(() => ApprovedProfileStore.ReadImportAsync(path, default));
 }
 [Fact] public async Task Listing_has_a_bounded_visible_limit()
 {
  Directory.CreateDirectory(root);
  for (var i = 0; i < 1001; i++) await File.WriteAllTextAsync(Path.Combine(root, i.ToString("x64") + ".json"), "{}");
  var list = await new ApprovedProfileStore(root).ReadAllAsync(default);
  Assert.True(list.LimitReached); Assert.Empty(list.Profiles); Assert.InRange(list.Notices.Count, 1, 1000);
 }
 [Fact] public async Task Reparse_root_is_refused()
 {
  if (OperatingSystem.IsWindows()) return; // Windows junction coverage belongs to native acceptance.
  var target = root + "-target"; Directory.CreateDirectory(target); Directory.CreateSymbolicLink(root, target);
  try { await Assert.ThrowsAsync<EvidenceValidationException>(() => new ApprovedProfileStore(root).SaveAsync(Profile(), default)); }
  finally { Directory.Delete(root); Directory.Delete(target); }
 }
 [Fact] public async Task Cancelled_save_does_not_create_a_profile_or_temporary_file()
 {
  using var stop = new CancellationTokenSource(); stop.Cancel();
  await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ApprovedProfileStore(root).SaveAsync(Profile(), stop.Token));
  Assert.False(Directory.Exists(root));
 }
 [Fact] public async Task Import_with_a_supplied_wrong_id_is_not_silently_repaired()
 {
  Directory.CreateDirectory(root); var path = Path.Combine(root, "proposed.json");
  await File.WriteAllBytesAsync(path, StrictJson.Bytes(Profile() with { Id = new string('e', 64) }));
  await Assert.ThrowsAsync<InvalidDataException>(() => ApprovedProfileStore.ReadImportAsync(path, default));
 }
 [Fact] public async Task A_full_profile_directory_refuses_additional_profiles()
 {
  Directory.CreateDirectory(root);
  for (var i = 0; i < 1000; i++) await File.WriteAllTextAsync(Path.Combine(root, i.ToString("x64") + ".json"), "{}");
  await Assert.ThrowsAsync<InvalidDataException>(() => new ApprovedProfileStore(root).SaveAsync(Profile(), default));
  Assert.Equal(1000, Directory.EnumerateFiles(root).Count());
 }

}
