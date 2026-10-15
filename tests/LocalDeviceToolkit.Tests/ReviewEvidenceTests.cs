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
 [Fact] public async Task An_unused_older_manual_review_does_not_hide_an_automatic_pass_count()
 {
  var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
  try
  {
   var schemas=ToolkitEvidenceSchemas.Create();ReportingEvidenceSchemas.Register(schemas);var store=new EvidenceStore(root,schemas,TestData.Context);var catalogue=CatalogueLoader.LoadShipped();var now=DateTimeOffset.UtcNow;
   var capture=TestData.Capture() with{CapturedAt=now,Observations=[new("CFG-WIN-012","extensions.visible",ObservationState.Known,StrictJson.Value(true),"fixture",now,"")]};await store.SaveNewAsync(EvidenceRecord.Capture(capture),default);
   var review=new ManualReview(Guid.NewGuid(),"CFG-WIN-012","An earlier explicit review","engineer",ManualReviewOutcome.Verified,null,TestData.Actor,now.AddMinutes(-1),catalogue.Identity,TestData.Context);await store.SaveNewAsync(EvidenceRecord.Create(review.Id,"manual-review",review.Context,review.ReviewedAt,StrictJson.Value(review)),default);
   var report=await new ReportService(store,catalogue,TestData.Context).BuildAsync(ReportPurpose.DiagnosticTicket,[capture.Id,review.Id],default);
   Assert.Equal(FindingStatus.MeetsStandard,Assert.Single(report.Controls).Status);Assert.Equal(1,report.VerifiedCount);Assert.Single(report.ManualReviews);
  }
  finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }

 [Fact] public async Task Historical_raw_evidence_can_be_reopened_without_claiming_current_catalogue_compliance()
 {
  var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
  try
  {
   var store=new EvidenceStore(root,ToolkitEvidenceSchemas.Create(),TestData.Context);var capture=TestData.Capture();capture=capture with{Catalogue=capture.Catalogue with{LocalSha256=new string('a',64)}};await store.SaveNewAsync(EvidenceRecord.Capture(capture),default);
   var service=new ReportService(store,CatalogueLoader.LoadShipped(),TestData.Context);var record=await service.ReadRecordAsync(capture.Id,default);
   Assert.Equal(capture.Catalogue,StrictJson.Read<DeviceCapture>(StrictJson.Bytes(record.Data)).Catalogue);
   await Assert.ThrowsAsync<EvidenceValidationException>(()=>service.BuildAsync(ReportPurpose.DiagnosticTicket,[capture.Id],default));
  }
  finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }

}
