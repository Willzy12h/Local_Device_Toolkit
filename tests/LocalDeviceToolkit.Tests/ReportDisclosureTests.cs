using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Reporting;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class ReportDisclosureTests
{
 [Fact] public void Default_export_omits_identifiers_and_notes_and_opt_in_html_encodes_markup()
 {
  var catalogue=CatalogueLoader.LoadShipped();var at=DateTimeOffset.UtcNow;var review=new ManualReview(Guid.NewGuid(),"CFG-WIN-001","<script>private-note</script>","Manual",ManualReviewOutcome.Verified,null,TestData.Actor,at,catalogue.Identity,TestData.Context);
  var report=new LocalReport(ReportPurpose.DiagnosticTicket,at,TestData.Context,catalogue.Identity,[],[],[review],null,null,[],[],[],[],[],[]);
  var minimized=ReportDisclosure.Render(ReportDisclosure.Project(report,false),ReportFormat.Json);Assert.DoesNotContain(TestData.Context.OriginalUserSid,minimized);Assert.DoesNotContain("private-note",minimized);Assert.DoesNotContain(TestData.Context.ComputerName,minimized);
  var html=ReportDisclosure.Render(ReportDisclosure.Project(report,true),ReportFormat.Html);Assert.DoesNotContain("<script>",html);Assert.Contains("&lt;script&gt;",html);
 }
}
