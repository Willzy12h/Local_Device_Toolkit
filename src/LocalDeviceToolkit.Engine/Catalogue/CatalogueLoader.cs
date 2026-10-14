using LocalDeviceToolkit.Core;
using System.Reflection;
using System.Text.Json;
namespace LocalDeviceToolkit.Engine.Catalogue;
public static class CatalogueLoader
{
 static byte[] Embedded(string name)
 {
  using var stream = typeof(CatalogueLoader).Assembly.GetManifestResourceStream("catalogue/"+name) ?? throw new CatalogueValidationException("Missing embedded catalogue: "+name);
  using var output = new MemoryStream();stream.CopyTo(output);return output.ToArray();
 }
 public static IReadOnlyDictionary<string,byte[]> ShippedFiles()
 {
  var pins = StrictJson.Read<Dictionary<string,string>>(Embedded("pins.json"));
  return pins.Keys.ToDictionary(name=>name,Embedded,StringComparer.Ordinal);
 }
 internal static IReadOnlyDictionary<string,byte[]> Verify(IReadOnlyDictionary<string,byte[]> files)
 {
  var pins=StrictJson.Read<Dictionary<string,string>>(Embedded("pins.json"));
  if(files.Count!=pins.Count)throw new CatalogueValidationException("Incomplete catalogue inventory.");
  foreach(var pin in pins)
   if(!files.TryGetValue(pin.Key,out var content)||CanonicalJson.Hash(content)!=pin.Value)throw new CatalogueValidationException("Catalogue integrity check failed: "+pin.Key);
  return files;
 }
 public static LocalCatalogue LoadShipped() => Load(ShippedFiles());
 public static LocalCatalogue Load(IReadOnlyDictionary<string,byte[]> files)
 {
  try
  {
   Verify(files);
   using var reference=StrictJson.Parse(files["reference.json"]);
   var rawControls=reference.RootElement.GetProperty("controls").EnumerateArray().ToArray();
   var controls=StrictJson.Read<LocalControl[]>(files["local-windows.json"]);
   var source=reference.RootElement.GetProperty("source");
   var expected=rawControls.Select(c=>c.GetProperty("id").GetString()!).Order(StringComparer.Ordinal).ToArray();
   if(controls.Length!=30 || controls.Select(c=>c.Id).Distinct(StringComparer.Ordinal).Count()!=30 || !expected.SequenceEqual(controls.Select(c=>c.Id).Order(StringComparer.Ordinal))) throw new CatalogueValidationException("The local catalogue must map all 30 source controls exactly once.");
   foreach(var c in controls)
    if(string.IsNullOrWhiteSpace(c.Name)||string.IsNullOrWhiteSpace(c.ManualInstructions)||c.CheckIds is null||c.OperationIds is null|| (c.Coverage==CoverageMode.Manual&&c.OperationIds.Length!=0))throw new CatalogueValidationException("Invalid coverage declaration: "+c.Id);
   return new(new(source.GetProperty("release").GetString()!,source.GetProperty("commit").GetString()!,source.GetProperty("sha256").GetString()!,CanonicalJson.Digest(files.OrderBy(p=>p.Key,StringComparer.Ordinal).ToDictionary(p=>p.Key,p=>CanonicalJson.Hash(p.Value)))),Array.AsReadOnly(controls));
  }
  catch(CatalogueValidationException){throw;}
  catch(Exception e) when(e is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException){throw new CatalogueValidationException("Invalid local catalogue: "+e.Message);}
 }
}
public sealed class WorkflowCatalogue
{
 readonly Dictionary<string,TaskDefinition> tasks;
 readonly WorkflowDefinition[] workflows;
 public IReadOnlyList<WorkflowDefinition> Workflows => Array.AsReadOnly(workflows);
 public WorkflowCatalogue(TaskDefinition[] definitions,WorkflowDefinition[] workflows)
 {
  this.workflows=workflows;
  if(definitions.Length!=definitions.Select(t=>t.Id).Distinct(StringComparer.Ordinal).Count())throw new CatalogueValidationException("Duplicate task definition.");
  tasks=definitions.ToDictionary(t=>t.Id,StringComparer.Ordinal);
  if(workflows.Select(w=>w.Id).Distinct(StringComparer.Ordinal).Count()!=workflows.Length)throw new CatalogueValidationException("Duplicate workflow definition.");
  foreach(var workflow in workflows)
  {
   if(workflow.Stages.Select(s=>s.Id).Distinct(StringComparer.Ordinal).Count()!=workflow.Stages.Length)throw new CatalogueValidationException("Duplicate workflow stage.");
   foreach(var stage in workflow.Stages)
    if(stage.TaskIds.Any(id=>!tasks.ContainsKey(id))||stage.TaskIds.Distinct(StringComparer.Ordinal).Count()!=stage.TaskIds.Length)throw new CatalogueValidationException("Invalid workflow task reference.");
  }
 }
 public static WorkflowCatalogue LoadShipped()
 {
  var files=CatalogueLoader.Verify(CatalogueLoader.ShippedFiles());
  return new(StrictJson.Read<TaskDefinition[]>(files["tasks.json"]),StrictJson.Read<WorkflowDefinition[]>(files["workflows.json"]));
 }
 public TaskDefinition ResolveTask(string id)=>tasks.TryGetValue(id,out var task)?task:throw new CatalogueValidationException("Unknown task: "+id);
 public TaskDefinition ResolveWorkflowTask(string workflow,string stage,string id)
 {
  var section=workflows.SingleOrDefault(w=>w.Id==workflow)?.Stages.SingleOrDefault(s=>s.Id==stage);
  if(section is null||!section.TaskIds.Contains(id,StringComparer.Ordinal))throw new CatalogueValidationException("Task is not declared in this workflow stage.");
  return ResolveTask(id);
 }
}
