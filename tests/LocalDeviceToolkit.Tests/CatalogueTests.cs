using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using System.Text;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class CatalogueTests
{
 [Fact] public void Readiness_checks_have_individually_selectable_live_tasks()
 {
  var workflows=WorkflowCatalogue.LoadShipped();
  var expected=new Dictionary<string,string>{{"os-version","os.version"},{"secure-boot","secureboot.enabled"},{"tpm","tpm.ready"},{"bitlocker","bitlocker.protected"},{"code-integrity","codeintegrity.enforced"},{"antivirus","antivirus.active"}};
  foreach(var pair in expected)
  {
   var task=workflows.ResolveWorkflowTask("setup","readiness","readiness."+pair.Key);
   Assert.Equal(new[]{"CMP-WIN-001"},task.ControlIds);Assert.Equal(new[]{pair.Value},task.CheckIds);Assert.Null(task.OperationId);
   Assert.Same(task,workflows.ResolveWorkflowTask("diagnostics","health",task.Id));
   Assert.Same(task,workflows.ResolveWorkflowTask("diagnostics","security",task.Id));
  }
  Assert.Throws<CatalogueValidationException>(()=>workflows.ResolveWorkflowTask("setup","readiness","security.device-readiness"));
  Assert.Equal(6,workflows.ResolveTask("security.device-readiness").CheckIds.Length);
  foreach(var workflow in workflows.Workflows)
   foreach(var stage in workflow.Stages)
   {
    Assert.DoesNotContain("security.device-readiness",stage.TaskIds);
    foreach(var id in stage.TaskIds.Where(id=>id.StartsWith("readiness.",StringComparison.Ordinal)))Assert.Single(workflows.ResolveWorkflowTask(workflow.Id,stage.Id,id).CheckIds);
   }
 }
 [Fact] public void Each_application_has_one_canonical_task_in_live_workflows()
 {
  var workflows=WorkflowCatalogue.LoadShipped();
  for(var i=1;i<=8;i++)
  {
   var task=workflows.ResolveWorkflowTask("setup","applications","apps.app-win-"+i.ToString("000"));
   Assert.Equal(new[]{"APP-WIN-"+i.ToString("000")},task.ControlIds);Assert.Equal("packages.install",task.OperationId);
   Assert.Same(task,workflows.ResolveWorkflowTask("diagnostics","applications",task.Id));
  }
  Assert.Throws<CatalogueValidationException>(()=>workflows.ResolveWorkflowTask("setup","applications","apps.installation"));
 }
 [Fact] public void UK_changes_have_distinct_selectable_tasks_and_no_live_composite()
 {
  var catalogue=CatalogueLoader.LoadShipped();var workflows=WorkflowCatalogue.LoadShipped();
  var suffixes=new[]{"formats","home-country","input-language","keyboard","default-keyboard"};
  var control=catalogue.Controls.Single(c=>c.Id=="CFG-WIN-010");
  Assert.DoesNotContain("settings.uk-region-keyboard",control.OperationIds);
  foreach(var suffix in suffixes)
  {
   var task=workflows.ResolveWorkflowTask("setup","windows","control.cfg-win-010-"+suffix);
   Assert.Equal("settings.uk-"+suffix,task.OperationId);
   Assert.Same(task,workflows.ResolveWorkflowTask("diagnostics","configuration",task.Id));
  }
 }
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
