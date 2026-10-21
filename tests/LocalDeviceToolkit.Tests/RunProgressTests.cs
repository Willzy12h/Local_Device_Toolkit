using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using Xunit;

namespace LocalDeviceToolkit.Tests;

public sealed class RunProgressTests
{
 [Theory]
 [InlineData(false)]
 [InlineData(true)]
 public async Task Before_callback_failure_blocks_mutation_even_for_unexpected_errors(bool unexpected)
 {
  using var f = new PlanningFixture(); var plan = await f.CreatePlan();
  var sink = new Sink { Before = () => { if (unexpected) throw new InvalidOperationException("broken observer"); throw new IOException("full companion log"); } };
  var run = await f.Coordinator(f.Store).ExecuteAsync(plan, default, sink);
  Assert.Equal("false", File.ReadAllText(f.SettingPath));
  Assert.False(run.EvidenceComplete);
  Assert.Equal(ActionOutcome.Failed, Assert.Single(run.Results).Outcome);
  Assert.Contains("log", run.Results[0].Reason, StringComparison.OrdinalIgnoreCase);
 }

 [Fact] public async Task Callbacks_bracket_real_mutation_and_follow_durable_evidence()
 {
  using var f = new PlanningFixture(); var plan = await f.CreatePlan(); var observed = new List<string>();
  var sink = new Sink
  {
   Before = () => { Assert.Equal("false", File.ReadAllText(f.SettingPath)); observed.Add("before"); },
   After = () => { Assert.Equal("true", File.ReadAllText(f.SettingPath)); observed.Add("after"); }
  };
  sink.BeforeAsync = async () => Assert.Contains((await f.Store.ReadHistoryAsync(null, default)).Entries, e => e.Kind == "intent");
  sink.AfterAsync = async () => Assert.Contains((await f.Store.ReadHistoryAsync(null, default)).Entries, e => e.Kind == "result");
  var run = await f.Coordinator(f.Store).ExecuteAsync(plan, default, sink);
  Assert.Equal(new[] { "before", "after" }, observed);
  Assert.True(run.EvidenceComplete); Assert.Equal(ActionOutcome.AppliedVerified, Assert.Single(run.Results).Outcome);
 }

 [Theory]
 [InlineData(false)]
 [InlineData(true)]
 public async Task After_callback_failure_preserves_verified_result_and_halts_second_action(bool unexpected)
 {
  using var first = new PlanningFixture(); using var second = new PlanningFixture();
  var other = new OtherOperation(second.Operation);
  var tasks = new[]
  {
   new TaskDefinition("first", "First", ["CFG-WIN-012"], ["extensions.visible"], first.Operation.Id),
   new TaskDefinition("second", "Second", ["CFG-WIN-012"], ["extensions.visible"], other.Id)
  };
  var registry = new OperationRegistry(new(tasks, [new("setup", "Setup", [new("windows", "Windows", ["first", "second"])])]), [first.Operation, other]);
  var plans = new PlanService(registry, first.Clock);
  var requests = new[] { new ActionRequest("first", first.Operation.Id, StrictJson.Value(new { enabled = true }), new("setup", "windows")), new ActionRequest("second", other.Id, StrictJson.Value(new { enabled = true }), new("setup", "windows")) };
  var plan = await plans.CreateAsync(first.Current, first.Catalogue, requests, default);
  var sink = new Sink { After = () => { if (unexpected) throw new InvalidOperationException("broken observer"); throw new IOException("full companion log"); } };
  var coordinator = new RunCoordinator(plans, registry, first, first.Catalogue, first.Store, new LocalActionDispatcher(registry, first, first.Clock), first.Clock);
  var run = await coordinator.ExecuteAsync(plan, default, sink);
  Assert.Equal("true", File.ReadAllText(first.SettingPath)); Assert.Equal("false", File.ReadAllText(second.SettingPath));
  Assert.False(run.EvidenceComplete);
  Assert.Equal(ActionOutcome.AppliedVerified, run.Results[0].Outcome); Assert.NotNull(run.Results[0].Actor); Assert.NotEmpty(run.Results[0].After);
  Assert.Contains("log", run.Results[0].Reason, StringComparison.OrdinalIgnoreCase);
  Assert.Equal(ActionOutcome.CancelledBeforeExecution, run.Results[1].Outcome);
 }

 sealed class Sink : IRunProgressSink
 {
  public Action? Before, After;
  public Func<Task>? BeforeAsync, AfterAsync;
  public async Task BeforeActionAsync(ReviewedPlan plan, PlannedAction action, CancellationToken ct) { Before?.Invoke(); if (BeforeAsync is not null) await BeforeAsync(); }
  public async Task AfterActionAsync(ReviewedPlan plan, PlannedAction action, ActionResult result, CancellationToken ct) { After?.Invoke(); if (AfterAsync is not null) await AfterAsync(); }
 }
 sealed class OtherOperation(ILocalOperation inner) : ILocalOperation
 {
  public string Id => "test.other-setting";
  public void ValidateParameters(JsonElement parameters) => inner.ValidateParameters(parameters);
  public string GetTargetKey(DeviceContext context, JsonElement parameters) => "fixture.other-setting";
  public Task<ActionReadiness> InspectAsync(DeviceContext context, JsonElement parameters, CancellationToken ct) => inner.InspectAsync(context, parameters, ct);
  public Task<IReadOnlyList<ProbeObservation>> CaptureBeforeAsync(DeviceContext context, JsonElement parameters, CancellationToken ct) => inner.CaptureBeforeAsync(context, parameters, ct);
  public Task<ActionResult> ExecuteAsync(DeviceContext context, PlannedAction action, CancellationToken ct) => inner.ExecuteAsync(context, action, ct);
 }
}
