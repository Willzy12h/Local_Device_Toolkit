using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Settings;
using LocalDeviceToolkit.Engine.Workbench;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class ReviewRunTests
{
 [Fact] public async Task Review_reads_current_and_expected_values_before_any_write()
 {
  using var f=new ReviewFixture();var review=await f.Service.CheckAsync([f.Selection()],default);
  Assert.Equal(0,f.Backend.Writes);var row=Assert.Single(review.Rows);Assert.Equal(PreviewStatus.Different,row.Preview.Status);Assert.Contains("Shown",row.Preview.Expected,StringComparison.OrdinalIgnoreCase);Assert.Contains("HKCU",row.Preview.Source);
  Assert.True(review.CanApply);Assert.Contains("review",review.LogText,StringComparison.OrdinalIgnoreCase);Assert.Contains(row.Preview.Source.Replace("\\","\\\\").Replace("\r","\\r").Replace("\n","\\n"),review.LogText);await f.Service.CancelAsync(review,default);
 }
 [Fact] public async Task Checks_and_verified_changes_append_to_the_same_unique_dated_file()
 {
  using var f=new ReviewFixture();var review=await f.Service.CheckAsync([f.Selection()],default);var path=review.FilePath;string before;
  using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
  using(var reader=new StreamReader(stream))before=await reader.ReadToEndAsync();
  var run=await f.Service.RunAsync(review,true,default);Assert.Equal(1,f.Backend.Writes);Assert.Equal(ActionOutcome.AppliedVerified,Assert.Single(run!.Results).Outcome);
  Assert.Equal(path,review.FilePath);Assert.StartsWith(before,File.ReadAllText(path));Assert.Contains("confirm",review.LogText,StringComparison.OrdinalIgnoreCase);Assert.Contains("AppliedVerified",review.LogText);Assert.Contains(TestData.Actor.UserName.Replace("\\","\\\\"),review.LogText);Assert.Contains(f.Clock.Now.ToString("O"),review.LogText);Assert.Single(Directory.GetFiles(f.LogDirectory,"*.log"));
 }
 [Fact] public async Task Verified_after_values_replace_the_main_preview_but_preserve_original_before_evidence()
 {
  using var f=new ReviewFixture();var review=await f.Service.CheckAsync([f.Selection()],default);await f.Service.RunAsync(review,true,default);
  var row=Assert.Single(review.Rows);Assert.NotNull(row.AfterPreview);Assert.Equal(PreviewStatus.AlreadyCorrect,row.AfterPreview!.Status);Assert.Contains("Shown",row.AfterPreview.Current);Assert.Equal(1,row.Before[0].Value!.Value.GetProperty("value").GetInt32());Assert.Equal("Hidden (1)",row.Preview.Current);
 }
 [Fact] public async Task New_review_gets_a_new_log_even_with_the_same_clock()
 {
  using var f=new ReviewFixture();var first=await f.Service.CheckAsync([f.Selection()],default);var second=await f.Service.CheckAsync([f.Selection()],default);
  Assert.NotEqual(first.FilePath,second.FilePath);Assert.Contains("superseded",first.LogText,StringComparison.OrdinalIgnoreCase);Assert.Equal(2,Directory.GetFiles(f.LogDirectory,"*.log").Length);await f.Service.CancelAsync(second,default);
 }
 [Fact] public async Task Already_correct_values_are_logged_and_never_rewritten()
 {
  using var f=new ReviewFixture();f.Backend.State=new(true,0);var review=await f.Service.CheckAsync([f.Selection()],default);
  Assert.Equal(PreviewStatus.AlreadyCorrect,Assert.Single(review.Rows).Preview.Status);Assert.False(review.CanApply);Assert.Null(await f.Service.RunAsync(review,true,default));Assert.Equal(0,f.Backend.Writes);Assert.Contains("skip",review.LogText,StringComparison.OrdinalIgnoreCase);
 }
 [Fact] public async Task Missing_value_is_reported_as_absence_before_it_is_created()
 {
  using var f=new ReviewFixture();f.Backend.State=new(false,null);var review=await f.Service.CheckAsync([f.Selection()],default);
  Assert.Equal(PreviewStatus.Missing,Assert.Single(review.Rows).Preview.Status);Assert.True(review.CanApply);Assert.Equal(0,f.Backend.Writes);await f.Service.RunAsync(review,true,default);Assert.Equal(new RegistryDwordState(true,0),f.Backend.State);
 }
 [Fact] public async Task Failed_read_is_not_missing_and_cannot_run()
 {
  using var f=new ReviewFixture();f.Backend.ReadDenied=true;var review=await f.Service.CheckAsync([f.Selection()],default);
  Assert.Equal(PreviewStatus.Unreadable,Assert.Single(review.Rows).Preview.Status);Assert.False(review.CanApply);await Assert.ThrowsAsync<PlanValidationException>(()=>f.Service.RunAsync(review,true,default));Assert.Equal(0,f.Backend.Writes);await f.Service.CancelAsync(review,default);
 }
 [Fact] public async Task Explicit_policy_confirmation_is_required_after_review()
 {
  using var f=new ReviewFixture();var review=await f.Service.CheckAsync([f.Selection()],default);
  await Assert.ThrowsAsync<PlanValidationException>(()=>f.Service.RunAsync(review,false,default));Assert.Equal(0,f.Backend.Writes);await f.Service.CancelAsync(review,default);
 }
 [Fact] public async Task A_changed_selection_invalidates_confirmation()
 {
  using var f=new ReviewFixture();var review=await f.Service.CheckAsync([f.Selection()],default);f.Service.Invalidate();
  await Assert.ThrowsAsync<PlanValidationException>(()=>f.Service.RunAsync(review,true,default));Assert.Equal(0,f.Backend.Writes);await f.Service.CancelAsync(review,default);
 }
 [Fact] public async Task Expired_review_is_not_renewed_at_confirmation()
 {
  using var f=new ReviewFixture();var review=await f.Service.CheckAsync([f.Selection()],default);f.Clock.Now=f.Clock.Now.AddMinutes(10);
  await Assert.ThrowsAsync<PlanValidationException>(()=>f.Service.RunAsync(review,true,default));Assert.Equal(0,f.Backend.Writes);await f.Service.CancelAsync(review,default);
 }
 [Fact] public async Task Drift_after_review_is_not_overwritten()
 {
  using var f=new ReviewFixture();var review=await f.Service.CheckAsync([f.Selection()],default);f.Backend.State=new(true,7);
  var run=await f.Service.RunAsync(review,true,default);Assert.Equal(ActionOutcome.Failed,Assert.Single(run!.Results).Outcome);Assert.Equal(0,f.Backend.Writes);Assert.Equal(7,f.Backend.State.Value);
 }
 [Fact] public async Task Check_only_never_builds_or_runs_a_change_plan()
 {
  using var f=new ReviewFixture();var review=await f.Service.CheckAsync([f.Selection(false)],default);
  Assert.False(review.CanApply);Assert.Equal(0,review.ChangesCount);Assert.Null(await f.Service.RunAsync(review,true,default));Assert.Equal(0,f.Backend.Writes);Assert.Single(f.Session.Captures);
 }
 [Fact] public async Task Cancellation_preserves_review_log_and_never_dispatches()
 {
  using var f=new ReviewFixture();var review=await f.Service.CheckAsync([f.Selection()],default);await f.Service.CancelAsync(review,default);
  Assert.True(File.Exists(review.FilePath));Assert.Contains("cancel",review.LogText,StringComparison.OrdinalIgnoreCase);Assert.Equal(0,f.Backend.Writes);await Assert.ThrowsAsync<PlanValidationException>(()=>f.Service.RunAsync(review,true,default));
 }
 [Fact] public async Task Failure_to_create_log_prevents_even_the_precheck()
 {
  using var f=new ReviewFixture();File.WriteAllText(f.LogDirectory,"not a directory");
  await Assert.ThrowsAnyAsync<IOException>(()=>f.Service.CheckAsync([f.Selection()],default));Assert.Equal(0,f.Backend.Reads);Assert.Equal(0,f.Backend.Writes);
 }
 [Fact] public async Task Different_live_stage_can_be_checked_without_navigating_the_ui()
 {
  using var f=new ReviewFixture();f.Session.Navigate("diagnostics","configuration");var review=await f.Service.CheckAsync([f.Selection(false)],default);
  Assert.Equal("diagnostics",f.Session.Area);Assert.Equal("configuration",f.Session.StageId);Assert.Equal("setup",Assert.Single(f.Session.Captures).Origin.WorkflowId);await f.Service.CancelAsync(review,default);
 }
}
public sealed class ReviewFixture:IDisposable,IExecutionContextProvider,IDeviceInspector
{
 readonly string root=Path.Combine(Path.GetTempPath(),"review-run-"+Guid.NewGuid().ToString("N"));
 public string LogDirectory=>Path.Combine(root,"logs");public TestClock Clock{get;}=new();public ReviewBackend Backend{get;}=new();public WorkbenchSession Session{get;}public ReviewRunService Service{get;}
 public ReviewFixture()
 {
  Directory.CreateDirectory(root);var catalogue=CatalogueLoader.LoadShipped();var workflows=WorkflowCatalogue.LoadShipped();var operation=new FixedSettingOperation(FixedSettingOperation.Definitions.Single(d=>d.Setting==SettingId.ShowFileExtensions),Backend);var registry=new OperationRegistry(workflows,[operation]);
  var store=new EvidenceStore(Path.Combine(root,"evidence"),ToolkitEvidenceSchemas.Create());Session=new(TestData.Context,catalogue,workflows,this,store,registry);Session.Navigate("setup","windows");var plans=new PlanService(registry,Clock);var coordinator=new RunCoordinator(plans,registry,this,catalogue,store,new LocalActionDispatcher(registry,this,Clock),Clock);Service=new(registry,plans,coordinator,Session,LogDirectory,Clock);
 }
 public ReviewSelection Selection(bool change=true)=>new("windows.file-extensions",new("setup","windows"),change,change?new("windows.file-extensions","settings.show-file-extensions",StrictJson.Value(new SettingParameters(true)),new("setup","windows")):null);
 public Task<DeviceContext> GetCurrentAsync(CancellationToken ct)=>Task.FromResult(TestData.Context);
 public Task<DeviceCapture> CaptureAsync(CaptureRequest request,CancellationToken ct)=>Task.FromResult(new DeviceCapture(Guid.NewGuid(),TestData.Context,Session.Catalogue.Identity,request.Origin,TestData.Actor,Clock.Now,[new("CFG-WIN-012","extensions.visible",ObservationState.Known,StrictJson.Value(Backend.State is {Exists:true,Value:0}),"fixture read-only facts",Clock.Now,"fixture")]));
 public void Dispose(){if(Service.Current is {} review)Service.CancelAsync(review,default).GetAwaiter().GetResult();if(Directory.Exists(root))Directory.Delete(root,true);}
}
public sealed class ReviewBackend:ISettingsBackend
{
 public RegistryDwordState State=new(true,1);public bool ReadDenied;public int Writes,Reads;
 public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>Task.FromResult(TestData.Actor);
 public Task<SettingPolicy> InspectPolicyAsync(SettingId setting,CancellationToken ct)=>Task.FromResult(new SettingPolicy(true,false,"fixture policy"));
 public Task<SettingSnapshot> ReadAsync(SettingId setting,DeviceContext context,CancellationToken ct){Reads++;if(ReadDenied)throw new UnauthorizedAccessException("fixture access denied");return Task.FromResult(new SettingSnapshot(setting,StrictJson.Value(State),@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\HideFileExt",DateTimeOffset.UtcNow));}
 public Task ApplyAsync(SettingId setting,DeviceContext context,SettingSnapshot expectedBefore,CancellationToken ct){Writes++;State=new(true,0);return Task.CompletedTask;}
}
