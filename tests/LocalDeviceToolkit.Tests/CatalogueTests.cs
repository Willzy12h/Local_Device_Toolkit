using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using System.Text;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class CatalogueTests
{
 [Fact] public void All_source_controls_have_truthful_coverage()
 {
  var catalogue = CatalogueLoader.LoadShipped();
  Assert.Equal(30, catalogue.Controls.Count);
  Assert.Equal(30, catalogue.Controls.Select(c => c.Id).Distinct().Count());
  Assert.All(catalogue.Controls.Where(c => c.Coverage == CoverageMode.Manual), c => Assert.Empty(c.OperationIds));
  Assert.All(catalogue.Controls, c => Assert.False(string.IsNullOrWhiteSpace(c.ManualInstructions)));
 }
 [Fact] public void Changed_reference_is_refused()
 {
  var files = CatalogueLoader.ShippedFiles().ToDictionary(p => p.Key, p => p.Value.ToArray());
  files["reference.json"] = Encoding.UTF8.GetBytes("{}");
  Assert.Throws<CatalogueValidationException>(() => CatalogueLoader.Load(files));
 }
 [Fact] public void Duplicate_members_are_refused()
 {
  Assert.ThrowsAny<Exception>(() => StrictJson.Parse(Encoding.UTF8.GetBytes("{\"id\":1,\"id\":2}")));
 }
 [Fact] public void Setup_and_diagnostics_resolve_the_same_task()
 {
  var registry = WorkflowCatalogue.LoadShipped();
  Assert.Same(registry.ResolveWorkflowTask("setup","windows","windows.file-extensions"), registry.ResolveWorkflowTask("diagnostics","configuration","windows.file-extensions"));
 }
 [Fact] public void Canonical_json_normalises_property_order_numbers_and_escaped_text()
 {
  using var first = System.Text.Json.JsonDocument.Parse("""{"name":"\u0041","count":1.0}""");
  using var second = System.Text.Json.JsonDocument.Parse("""{"count":1,"name":"A"}""");
  Assert.Equal(CanonicalJson.Serialize(first.RootElement), CanonicalJson.Serialize(second.RootElement));
 }
 [Fact] public void Unknown_workflow_membership_is_refused()
 {
  var registry = WorkflowCatalogue.LoadShipped();
  Assert.Throws<CatalogueValidationException>(() => registry.ResolveWorkflowTask("diagnostics","security","windows.file-extensions"));
 }
}
