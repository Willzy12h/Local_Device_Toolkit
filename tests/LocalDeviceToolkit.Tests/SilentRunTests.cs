using System.Text;
using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Workbench;
using Xunit;

namespace LocalDeviceToolkit.Tests;

public sealed class SilentRunTests
{
 [Theory]
 [InlineData("all")]
 [InlineData("fast")]
 public void Arguments_are_explicit_and_preserve_input_paths(string preset)
 {
  var options=SilentRunOptions.Parse(["--silent","--preset",preset,"--inputs","a folder/inputs.json"]);
  Assert.Equal(preset,options.Preset);Assert.Equal("a folder/inputs.json",options.InputsFile);
 }
 [Theory]
 [InlineData("--silent")]
 [InlineData("--silent --preset unknown")]
 [InlineData("--silent --preset all --apply")]
 [InlineData("--silent --preset all --preset fast")]
 [InlineData("--silent --preset all --inputs")]
 public void Ambiguous_or_mutating_arguments_are_refused(string command)=>Assert.Throws<ArgumentException>(()=>SilentRunOptions.Parse(command.Split(' ')));

 [Fact] public async Task All_checks_share_the_review_runner_and_cannot_dispatch_changes()
 {
  using var f=new ReviewFixture();var result=await new SilentRunService(f.Session,f.Service,f.Clock).RunAsync(new("all",null),new Dictionary<string,JsonElement>(),default);
  Assert.True(result.Summary.CheckOnly);Assert.Equal(0,f.Backend.Writes);Assert.NotEmpty(f.Session.Captures);Assert.True(f.Backend.Reads>0);
  Assert.Empty(f.Service.Current!.Actions);Assert.False(f.Service.Current.CanApply);
  Assert.Equal(result.Summary.Tasks.Count,result.Summary.Tasks.Select(t=>t.TaskId).Distinct().Count());
  Assert.All(f.Session.Captures,c=>Assert.Equal("diagnostics",c.Origin.WorkflowId));
  Assert.Equal("Different",result.Summary.Tasks.Single(t=>t.TaskId=="windows.file-extensions").Status);
  Assert.Equal("Needs input",result.Summary.Tasks.Single(t=>t.TaskId=="local.network.dns").Status);
  Assert.Equal("Requires administrator",result.Summary.Tasks.Single(t=>t.TaskId=="local.recovery.winre").Status);
  Assert.DoesNotContain(result.Summary.Tasks,t=>t.TaskId is "diagnostics.refresh-dns-cache" or "diagnostics.update-defender-signatures" or "settings.restore-recorded" || t.TaskId.StartsWith("settings.power.",StringComparison.Ordinal));
  Assert.Equal(2,result.Summary.ExitCode);
  var log=await File.ReadAllTextAsync(result.Summary.LogFile);Assert.Contains("Check only",log);Assert.Contains("Needs input",log);Assert.Contains("Exit=2",log);
  Assert.DoesNotContain("Engineer confirmed",log);Assert.DoesNotContain("[cancel]",log);
  using var json=JsonDocument.Parse(await File.ReadAllBytesAsync(result.JsonFile));Assert.True(json.RootElement.GetProperty("checkOnly").GetBoolean());
  Assert.Contains("Different",await File.ReadAllTextAsync(result.HtmlFile));
 }
 [Fact] public async Task Fast_keeps_short_diagnostics_and_all_current_tasks()
 {
  using var f=new ReviewFixture();var runner=new SilentRunService(f.Session,f.Service,f.Clock);
  var all=await runner.RunAsync(new("all",null),new Dictionary<string,JsonElement>(),default);
  var fast=await runner.RunAsync(new("fast",null),new Dictionary<string,JsonElement>(),default);
  Assert.Equal(all.Summary.Tasks.Select(t=>t.TaskId),fast.Summary.Tasks.Select(t=>t.TaskId));
  Assert.DoesNotContain(fast.Summary.Tasks,t=>t.Status=="Excluded timed test");
  Assert.Contains(fast.Summary.Tasks,t=>t.TaskId=="local.battery.capacity"&&t.Status!="Excluded timed test");
  Assert.NotEqual(all.Summary.LogFile,fast.Summary.LogFile);Assert.True(File.Exists(all.JsonFile));Assert.True(File.Exists(fast.JsonFile));
 }
 [Fact] public void Fast_excludes_only_explicit_timed_tests()
 {
  var ordinary=new TaskDefinition("ordinary","Normal check",[],[],null);
  var timed=ordinary with{Id="timed",TimedPerformanceTest=true};
  Assert.False(SilentRunService.Exclude(ordinary,"fast"));Assert.True(SilentRunService.Exclude(timed,"fast"));Assert.False(SilentRunService.Exclude(timed,"all"));
 }
 [Theory]
 [InlineData("{\"not.a.probe\":{}}")]
 [InlineData("{\"local.network.dns\":{\"hostname\":\"a.example\",\"queryType\":\"A\",\"command\":\"anything\"}}")]
 [InlineData("{\"local.network.dns\":{},\"local.network.dns\":{}}")]
 [InlineData("{\"local.network.tcp\":{\"host\":\"a.example\",\"port\":\"443\"}}")]
 [InlineData("{\"local.storage.capacity\":{\"volumeId\":\"C:\",\"requiredBytes\":\"0\"}}")]
 public void Invalid_or_duplicate_inputs_are_refused(string json)
 {
  var error=Record.Exception(()=>SilentRunOptions.ReadInputs(Encoding.UTF8.GetBytes(json)));
  Assert.True(error is CatalogueValidationException or JsonException or InvalidDataException,error?.ToString()??"Invalid inputs were accepted.");
 }
 [Fact] public async Task Itemised_config_selects_reports_and_zero_skips_without_writing()
 {
  using var f=new ReviewFixture();var path=f.LogDirectory+".config";await File.WriteAllTextAsync(path,"# 0 = skip; 2 = report\nlongfilepaths=2\nshowfileextensions=0\n");
  var result=await new SilentRunService(f.Session,f.Service,f.Clock).RunAsync(new("all",null,path),new Dictionary<string,JsonElement>(),default);
  Assert.Equal("Excluded by config",result.Summary.Tasks.Single(t=>t.TaskId=="windows.file-extensions").Status);
  Assert.NotEqual("Excluded by config",result.Summary.Tasks.Single(t=>t.TaskId=="control.cfg-win-007").Status);Assert.Equal(0,f.Backend.Writes);
 }
 [Fact] public async Task Apply_value_in_diagnostic_config_is_refused_before_reading()
 {
  using var f=new ReviewFixture();var path=f.LogDirectory+".config";await File.WriteAllTextAsync(path,"longfilepaths=1\n");
  var error=await Assert.ThrowsAsync<ArgumentException>(()=>new SilentRunService(f.Session,f.Service,f.Clock).RunAsync(new("all",null,path),new Dictionary<string,JsonElement>(),default));
  Assert.Contains("diagnostic",error.Message,StringComparison.OrdinalIgnoreCase);Assert.Equal(0,f.Backend.Reads);Assert.Equal(0,f.Backend.Writes);
 }
 [Fact] public async Task Empty_config_finishes_with_every_task_explicitly_excluded()
 {
  using var f=new ReviewFixture();var path=f.LogDirectory+".config";await File.WriteAllTextAsync(path,"# No selections\n");
  var result=await new SilentRunService(f.Session,f.Service,f.Clock).RunAsync(new("fast",null,path),new Dictionary<string,JsonElement>(),default);
  Assert.All(result.Summary.Tasks,t=>Assert.Equal("Excluded by config",t.Status));Assert.Empty(f.Session.Captures);Assert.Equal(0,result.Summary.ExitCode);
 }
 [Fact] public void Inputs_use_the_existing_closed_probe_schemas()
 {
  var inputs=SilentRunOptions.ReadInputs(Encoding.UTF8.GetBytes("{\"local.network.dns\":{\"hostname\":\"a.example\",\"queryType\":\"A\"}}"));
  Assert.Equal("a.example",inputs["local.network.dns"].GetProperty("hostname").GetString());
 }
 [Fact] public void Shipped_template_selects_every_diagnostic_with_no_write_requests()
 {
  var root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!,"../.."));var workflows=WorkflowCatalogue.LoadShipped();
  var config=SilentRunService.ReadConfig(File.ReadAllBytes(Path.Combine(root,"packaging/Diagnostics.config.example")),workflows);
  Assert.Equal(SilentRunService.DiagnosticTasks(workflows).Count,config.Count);Assert.All(config.Values,v=>Assert.Equal(2,v));
  Assert.NotEmpty(SilentRunOptions.ReadInputs(File.ReadAllBytes(Path.Combine(root,"packaging/ProbeInputs.json.example"))));
 }
 [Theory]
 [InlineData("longfilepaths=2\ncontrol.cfg-win-007=0")]
 [InlineData("longfilepaths=3")]
 [InlineData("longfilepathz=2")]
 public void Unknown_duplicate_or_unsupported_config_items_fail(string config)=>Assert.Throws<ArgumentException>(()=>SilentRunService.ReadConfig(Encoding.UTF8.GetBytes(config),WorkflowCatalogue.LoadShipped()));
 [Fact] public async Task Check_only_completion_cannot_close_a_change_review_without_approval()
 {
  using var f=new ReviewFixture();var review=await f.Service.CheckAsync([f.Selection()],default);
  await Assert.ThrowsAsync<LocalDeviceToolkit.Engine.Execution.PlanValidationException>(()=>f.Service.CompleteChecksAsync(review,default));Assert.Equal(0,f.Backend.Writes);Assert.True(review.CanApply);
 }
 [Fact] public async Task Evidence_write_failure_cannot_be_hidden_by_a_successful_direct_setting_read()
 {
  using var f=new ReviewFixture();var evidence=Path.Combine(Path.GetDirectoryName(f.LogDirectory)!,"evidence");
  if(Directory.Exists(evidence))Directory.Delete(evidence,true);await File.WriteAllTextAsync(evidence,"blocked");
  var config=f.LogDirectory+".config";await File.WriteAllTextAsync(config,"showfileextensions=2");
  var result=await new SilentRunService(f.Session,f.Service,f.Clock).RunAsync(new("all",null,config),new Dictionary<string,JsonElement>(),default);
  Assert.Equal(1,result.Summary.ExitCode);Assert.True(f.Backend.Reads>0);Assert.Equal(0,f.Backend.Writes);
  Assert.Contains("evidence",await File.ReadAllTextAsync(result.Summary.LogFile),StringComparison.OrdinalIgnoreCase);
 }
 static string Source([System.Runtime.CompilerServices.CallerFilePath]string source="")=>source;
}
