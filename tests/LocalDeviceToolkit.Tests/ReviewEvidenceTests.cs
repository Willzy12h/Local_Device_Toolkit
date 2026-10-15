using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Reporting;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public class ReviewEvidenceTests
{
 [Fact] public async Task Malformed_required_automatic_probe_must_not_be_overridden_as_manual_success()
 {
  var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
  try
  {
  var schemas=ToolkitEvidenceSchemas.Create(); ReportingEvidenceSchemas.Register(schemas);
  var store=new EvidenceStore(root,schemas,TestData.Context); var catalogue=CatalogueLoader.LoadShipped();
  var control=catalogue.Controls.Single(c=>c.Id=="CFG-WIN-007");
  var now=DateTimeOffset.UtcNow;
  var capture=TestData.Capture() with {CapturedAt=now,Observations=[new(control.Id,"paths.enabled",ObservationState.Known,StrictJson.Value(123),"corrupt required value",now,"")]};
  await store.SaveNewAsync(EvidenceRecord.Capture(capture),default);
  var review=await new ManualReviewRecorder(store,catalogue,TestData.Context,_=>Task.FromResult(TestData.Actor)).RecordAsync(control.Id,ManualReviewOutcome.Verified,"Reviewed application compatibility","Engineer",null,default);
  var report=await new ReportService(store,catalogue,TestData.Context).BuildAsync(ReportPurpose.DiagnosticTicket,[capture.Id,review.Id],default);
  Assert.Equal(FindingStatus.Unknown,Assert.Single(report.Controls).Status);
  }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
