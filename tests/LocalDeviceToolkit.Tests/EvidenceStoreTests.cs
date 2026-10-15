using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Evidence;
using System.Text;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class EvidenceStoreTests:IDisposable
{
 readonly string root=Path.Combine(Path.GetTempPath(),"LocalToolkit-"+Guid.NewGuid().ToString("N"),"evidence with spaces é");
 public void Dispose(){var parent=Directory.GetParent(root)!.FullName;if(Directory.Exists(parent))Directory.Delete(parent,true);}
 [Fact] public async Task Saved_capture_reopens_with_original_identity()
 {
  var store=new EvidenceStore(root);var record=EvidenceRecord.Capture(TestData.Capture());
  await store.SaveNewAsync(record,default);var loaded=await store.LoadAsync(record.Id,default);
  Assert.NotNull(loaded);Assert.Equal(record.Digest,loaded.Digest);Assert.Equal(TestData.Context.OriginalUserSid,loaded.Context.OriginalUserSid);
  await Assert.ThrowsAsync<IOException>(()=>store.SaveNewAsync(record,default));
 }
 [Fact] public async Task Damaged_entries_do_not_hide_healthy_history()
 {
  Directory.CreateDirectory(root);var record=EvidenceRecord.Capture(TestData.Capture());var store=new EvidenceStore(root);
  await store.SaveNewAsync(record,default);
  await File.WriteAllTextAsync(Path.Combine(root,Guid.NewGuid()+".json"),"{bad json");
  var page=await store.ReadHistoryAsync(null,default);
  Assert.Single(page.Entries);Assert.Contains(page.Diagnostics,d=>d.Code=="MalformedRecord");
 }
 [Fact] public async Task Duplicate_members_and_changed_digests_are_refused()
 {
  Directory.CreateDirectory(root);var id=Guid.NewGuid();var path=Path.Combine(root,id+".json");var store=new EvidenceStore(root);
  await File.WriteAllTextAsync(path,"{\"id\":\""+id+"\",\"id\":\""+id+"\"}");
  await Assert.ThrowsAsync<EvidenceValidationException>(()=>store.LoadAsync(id,default));
  var record=EvidenceRecord.Capture(TestData.Capture());
  await Assert.ThrowsAsync<EvidenceValidationException>(()=>store.SaveNewAsync(record with { Digest="changed" },default));
 }
 [Fact] public async Task Record_limit_is_enforced_before_parsing()
 {
  Directory.CreateDirectory(root);var id=Guid.NewGuid();var path=Path.Combine(root,id+".json");
  using(var file=File.Create(path))file.SetLength(8*1024*1024+1);
  await Assert.ThrowsAsync<EvidenceValidationException>(()=>new EvidenceStore(root).LoadAsync(id,default));
 }
 [Fact] public async Task History_continues_after_the_first_page()
 {
  var store=new EvidenceStore(root);
  for(int i=0;i<101;i++)await store.SaveNewAsync(EvidenceRecord.Capture(TestData.Capture()),default);
  var first=await store.ReadHistoryAsync(null,default);
  Assert.Equal(100,first.Entries.Count);Assert.NotNull(first.NextCursor);
  var next=await store.ReadHistoryAsync(first.NextCursor,default);Assert.Single(next.Entries);Assert.Null(next.NextCursor);
 }
 [Fact] public async Task Reparse_root_is_refused()
 {
  var parent=Directory.GetParent(root)!.FullName;Directory.CreateDirectory(parent);var target=Path.Combine(parent,"target");Directory.CreateDirectory(target);
  if(OperatingSystem.IsWindows())
  {
   var start=new System.Diagnostics.ProcessStartInfo("cmd.exe") { Arguments="/d /c mklink /J \""+root+"\" \""+target+"\"",UseShellExecute=false,CreateNoWindow=true };
   using var process=System.Diagnostics.Process.Start(start)!;process.WaitForExit();Assert.Equal(0,process.ExitCode);
  }
  else Directory.CreateSymbolicLink(root,target);
  try{await Assert.ThrowsAsync<EvidenceValidationException>(()=>new EvidenceStore(root).SaveNewAsync(EvidenceRecord.Capture(TestData.Capture()),default));}
  finally{Directory.Delete(root,false);} // Remove the link itself before recursive fixture cleanup; never traverse its target.
 }
}
