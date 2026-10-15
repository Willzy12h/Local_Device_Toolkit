using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Tests;
using LocalDeviceToolkit.Windows.Inspection;
using LocalDeviceToolkit.Windows.Processes;
using System.Diagnostics;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class DeviceInspectorTests
{
 [Fact] public async Task Access_denied_is_not_a_compliant_value()
 {
  var fixture=new InspectorFixture([new("secureboot.enabled",ObservationState.AccessDenied,null,"fixture",DateTimeOffset.UtcNow,"Administrator inspection required.")]);
  var capture=await fixture.Inspector.CaptureAsync(new(["CMP-WIN-001"],new("diagnostics","security")),default);
  var observation=Assert.Single(capture.Observations);Assert.Equal(ObservationState.AccessDenied,observation.State);Assert.Null(observation.Value);
 }
 [Fact] public async Task Missing_check_is_not_a_successful_empty_capture()
 {
  var fixture=new InspectorFixture([]);
  var capture=await fixture.Inspector.CaptureAsync(new(["CMP-WIN-001"],new("setup","readiness")),default);
  Assert.Equal(ObservationState.Unavailable,Assert.Single(capture.Observations).State);
 }
 [Fact] public async Task Duplicate_source_values_are_not_silently_selected()
 {
  var fact=new WindowsFact("secureboot.enabled",ObservationState.Known,StrictJson.Value(true),"fixture",DateTimeOffset.UtcNow,"");var fixture=new InspectorFixture([fact,fact]);
  var capture=await fixture.Inspector.CaptureAsync(new(["CMP-WIN-001"],new("diagnostics","security")),default);
  Assert.Equal(ObservationState.Unavailable,Assert.Single(capture.Observations).State);
 }
 [Fact] public async Task Failed_policy_inspection_remains_unavailable()
 {
  var policy=new PolicyProbe(new FixtureFacts([]));
  Assert.Equal(PolicyInspectionState.Unavailable,(await policy.InspectAsync("settings.long-paths",default)).State);
 }
 [Fact] public async Task Output_limit_preserves_incomplete_status()
 {
  ProcessStartInfo start;
  if(OperatingSystem.IsWindows())
  {
   start=new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"));
   foreach(var value in new[]{"-NoLogo","-NoProfile","-NonInteractive","-Command","[Console]::Write(('x' * 2097152))"})start.ArgumentList.Add(value);
  }
  else{start=new("/usr/bin/head");start.ArgumentList.Add("-c");start.ArgumentList.Add("2097152");start.ArgumentList.Add("/dev/zero");}
  // This verifies the output cap, not PowerShell cold-start speed on a busy runner.
  var result=await new DiagnosticProcessRunner().RunCommandAsync(start,TimeSpan.FromSeconds(45),default);
  Assert.Equal(DiagnosticEnd.OutputLimitReached,result.End);Assert.True(System.Text.Encoding.UTF8.GetByteCount(result.StandardOutput)<=1024*1024);
 }
}
public sealed class FixtureFacts(IReadOnlyList<WindowsFact> facts):IWindowsFactsSource
{
 public Task<IReadOnlyList<WindowsFact>> ReadAsync(IReadOnlyList<string> ids,CancellationToken ct)=>Task.FromResult(facts);
}
public sealed class FixtureIdentity:IExecutionContextProvider,IExecutionActorProvider
{
 public Task<DeviceContext> GetCurrentAsync(CancellationToken ct)=>Task.FromResult(TestData.Context);
 public Task<ExecutionActor> GetActorAsync(CancellationToken ct)=>Task.FromResult(TestData.Actor);
}
public sealed class InspectorFixture
{
 public WindowsDeviceInspector Inspector {get;}
 public InspectorFixture(IReadOnlyList<WindowsFact> facts)
 {
  var catalogue=CatalogueLoader.LoadShipped();var control=catalogue.Controls.Single(c=>c.Id=="CMP-WIN-001") with { Coverage=CoverageMode.Partial,CheckIds=["secureboot.enabled"] };
  var identity=new FixtureIdentity();Inspector=new(identity,identity,new FixtureFacts(facts),catalogue with { Controls=[control] },TimeProvider.System);
 }
}
