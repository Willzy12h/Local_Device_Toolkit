using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Reporting;
using Xunit;

namespace LocalDeviceToolkit.Tests;

public sealed class HistoryPresentationTests
{
 [Fact] public async Task History_displays_newest_first_without_changing_page_members_or_cursor()
 {
  var older=EvidenceRecord.Capture(TestData.Capture() with {Id=Guid.Parse("00000000-0000-0000-0000-000000000001"),CapturedAt=DateTimeOffset.UtcNow.AddHours(-1)});
  var newer=EvidenceRecord.Capture(TestData.Capture() with {Id=Guid.Parse("00000000-0000-0000-0000-000000000002")});
  var page=new HistoryPage([older,newer],[new("broken.json","MalformedRecord","Fixture diagnostic")],"fixture-cursor");
  var store=new HistoryStore(page);var service=new ReportService(store,CatalogueLoader.LoadShipped(),TestData.Context);
  var result=await service.ReadHistoryAsync("previous-cursor",default);
  Assert.Equal(new[]{newer.Id,older.Id},result.Entries.Select(r=>r.Id));
  Assert.Equal(page.NextCursor,result.NextCursor);Assert.Equal(page.Diagnostics,result.Diagnostics);
  Assert.Equal("previous-cursor",store.Cursor);Assert.Equal(older.Id,page.Entries[0].Id);
 }

 sealed class HistoryStore(HistoryPage page):IEvidenceStore
 {
  public string? Cursor{get;private set;}
  public Task<HistoryPage> ReadHistoryAsync(string? cursor,CancellationToken ct){Cursor=cursor;return Task.FromResult(page);}
  public Task SaveNewAsync(EvidenceRecord record,CancellationToken ct)=>throw new InvalidOperationException("History must not save evidence.");
  public Task<EvidenceRecord> LoadAsync(Guid id,CancellationToken ct)=>throw new InvalidOperationException("History must not load extra records.");
 }
}
