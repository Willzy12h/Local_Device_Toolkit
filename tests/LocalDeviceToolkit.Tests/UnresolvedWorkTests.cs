using System.ComponentModel;
using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Workbench;
using Xunit;

namespace LocalDeviceToolkit.Tests;

public sealed class UnresolvedWorkTests
{
 [Theory]
 [InlineData(false)]
 [InlineData(true)]
 public async Task Missing_or_empty_evidence_store_is_complete_history(bool createDirectory)
 {
  var root = Path.Combine(Path.GetTempPath(), "unresolved-empty-" + Guid.NewGuid().ToString("N"));
  try
  {
   if (createDirectory) Directory.CreateDirectory(root);
   var session = Session(new EvidenceStore(root, ToolkitEvidenceSchemas.Create(), TestData.Context));
   await session.LoadActionHistoryAsync(default);
   Assert.False(session.HasUnresolvedWork);
  }
  finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
 }

 [Fact]
 public async Task Evidence_path_that_is_not_a_directory_stays_incomplete()
 {
  var root = Path.Combine(Path.GetTempPath(), "unresolved-file-" + Guid.NewGuid().ToString("N"));
  try
  {
   File.WriteAllText(root, "not a directory");
   var session = Session(new EvidenceStore(root, ToolkitEvidenceSchemas.Create(), TestData.Context));
   await session.LoadActionHistoryAsync(default);
   Assert.True(session.HasUnresolvedWork);
  }
  finally { if (File.Exists(root)) File.Delete(root); }
 }

 public static TheoryData<string> PreflightFailures => new() { "InvalidData", "Win32", "Json", "PlatformNotSupported" };

 [Theory]
 [MemberData(nameof(PreflightFailures))]
 public async Task Preflight_failure_after_a_verified_action_keeps_results_and_records_the_run(string failure)
 {
  using var chain = new ActionChain(3);
  var plan = await chain.CreatePlan();
  chain.Operations[1].PreflightFailure = failure switch
  {
   "InvalidData" => () => new InvalidDataException("The DWORD could not be read."),
   "Win32" => () => new Win32Exception(5),
   "Json" => () => new JsonException("Malformed probe reply."),
   _ => () => new PlatformNotSupportedException()
  };
  var run = await chain.Coordinator().ExecuteAsync(plan, default);
  Assert.Equal(3, run.Results.Count);
  Assert.Equal(ActionOutcome.AppliedVerified, run.Results[0].Outcome);
  Assert.Equal(ActionOutcome.Failed, run.Results[1].Outcome); Assert.Null(run.Results[1].Actor);
  Assert.StartsWith("Not started", run.Results[1].Reason, StringComparison.Ordinal);
  Assert.Equal(ActionOutcome.CancelledBeforeExecution, run.Results[2].Outcome);
  Assert.Equal("true", File.ReadAllText(chain.SettingPaths[0])); Assert.Equal("false", File.ReadAllText(chain.SettingPaths[1])); Assert.Equal("false", File.ReadAllText(chain.SettingPaths[2]));
  Assert.True(run.EvidenceComplete);
  var history = await chain.Store.ReadHistoryAsync(null, default);
  var saved = StrictJson.Read<RunRecord>(StrictJson.Bytes(Assert.Single(history.Entries, e => e.Kind == "run").Data));
  Assert.Equal(run.Id, saved.Id); Assert.Equal(run.Results.Select(r => r.Outcome), saved.Results.Select(r => r.Outcome));
  // Only the dispatched first action left an intent; the failed preflight wrote none.
  Assert.Single(history.Entries, e => e.Kind == "intent");
 }

 [Fact]
 public async Task Cancellation_before_dispatch_and_failure_after_dispatch_stay_distinct()
 {
  using (var chain = new ActionChain(2))
  {
   var plan = await chain.CreatePlan(); using var stop = new CancellationTokenSource();
   chain.Operations[1].PreflightFailure = () => { stop.Cancel(); return new OperationCanceledException(stop.Token); };
   var run = await chain.Coordinator().ExecuteAsync(plan, stop.Token);
   Assert.Equal(ActionOutcome.AppliedVerified, run.Results[0].Outcome);
   Assert.Equal(ActionOutcome.CancelledBeforeExecution, run.Results[1].Outcome);
  }
  using (var chain = new ActionChain(2))
  {
   var plan = await chain.CreatePlan();
   chain.Operations[1].ExecutionFailure = () => new InvalidDataException("Reply lost after start.");
   var run = await chain.Coordinator().ExecuteAsync(plan, default);
   Assert.Equal(ActionOutcome.AppliedVerified, run.Results[0].Outcome);
   Assert.Equal(ActionOutcome.OutcomeUnknown, run.Results[1].Outcome);
   Assert.Contains("reconcile", run.Results[1].Reason, StringComparison.OrdinalIgnoreCase);
  }
 }

 [Theory]
 [InlineData("result")]
 [InlineData("run")]
 public async Task Evidence_write_failure_never_reports_complete_evidence(string failingKind)
 {
  using var chain = new ActionChain(2);
  var plan = await chain.CreatePlan();
  var run = await chain.Coordinator(new FailingStore(chain.Store, failingKind)).ExecuteAsync(plan, default);
  Assert.False(run.EvidenceComplete);
  if (failingKind == "result")
  {
   Assert.Equal(ActionOutcome.OutcomeUnknown, run.Results[0].Outcome);
   Assert.Equal(ActionOutcome.CancelledBeforeExecution, run.Results[1].Outcome);
  }
 }

 static WorkbenchSession Session(IEvidenceStore store)
 {
  var flows = WorkflowCatalogue.LoadShipped();
  return new(TestData.Context, CatalogueLoader.LoadShipped(), flows, new NoInspector(), store, new OperationRegistry(flows, []));
 }

 sealed class NoInspector : IDeviceInspector
 {
  public Task<DeviceCapture> CaptureAsync(CaptureRequest request, CancellationToken ct) => throw new InvalidOperationException("No capture is expected.");
 }

 internal sealed class FailingStore(IEvidenceStore inner, string failingKind) : IEvidenceStore
 {
  public Task SaveNewAsync(EvidenceRecord record, CancellationToken ct) => record.Kind == failingKind ? throw new IOException("Synthetic full disk.") : inner.SaveNewAsync(record, ct);
  public Task<EvidenceRecord> LoadAsync(Guid id, CancellationToken ct) => inner.LoadAsync(id, ct);
  public Task<HistoryPage> ReadHistoryAsync(string? cursor, CancellationToken ct) => inner.ReadHistoryAsync(cursor, ct);
 }

 /// <summary>Independent fixture settings, one per action, so each action has its own target and before-state.</summary>
 internal sealed class ActionChain : IDisposable, IExecutionContextProvider
 {
  readonly string root = Path.Combine(Path.GetTempPath(), "unresolved-chain-" + Guid.NewGuid().ToString("N"));
  readonly OperationRegistry registry;
  readonly PlanService plans;
  public TestClock Clock { get; } = new();
  public LocalCatalogue Catalogue { get; } = CatalogueLoader.LoadShipped();
  public EvidenceStore Store { get; }
  public IReadOnlyList<ChainOperation> Operations { get; }
  public IReadOnlyList<string> SettingPaths { get; }
  public ActionChain(int count)
  {
   Directory.CreateDirectory(root);
   SettingPaths = Enumerable.Range(0, count).Select(i => Path.Combine(root, "setting-" + i + ".txt")).ToArray();
   foreach (var path in SettingPaths) File.WriteAllText(path, "false");
   Operations = SettingPaths.Select((path, i) => new ChainOperation("test.chain-" + i, new FileSettingOperation(path, Clock))).ToArray();
   var tasks = Operations.Select((o, i) => new TaskDefinition("chain-" + i, "Chain " + i, ["CFG-WIN-012"], ["extensions.visible"], o.Id)).ToArray();
   registry = new OperationRegistry(new(tasks, [new("setup", "Setup", [new("windows", "Windows", tasks.Select(t => t.Id).ToArray())])]), Operations);
   plans = new PlanService(registry, Clock);
   Store = new EvidenceStore(Path.Combine(root, "evidence"), ToolkitEvidenceSchemas.Create());
  }
  public Task<ReviewedPlan> CreatePlan() => plans.CreateAsync(TestData.Context, Catalogue, Operations.Select((o, i) => new ActionRequest("chain-" + i, o.Id, StrictJson.Value(new { enabled = true }), new("setup", "windows"))).ToArray(), default);
  public RunCoordinator Coordinator(IEvidenceStore? store = null) => new(plans, registry, this, Catalogue, store ?? Store, new LocalActionDispatcher(registry, this, Clock), Clock);
  public Task<DeviceContext> GetCurrentAsync(CancellationToken ct) => Task.FromResult(TestData.Context);
  public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
 }

 internal sealed class ChainOperation(string id, ILocalOperation inner) : ILocalOperation
 {
  public Func<Exception>? PreflightFailure { get; set; }
  public Func<Exception>? ExecutionFailure { get; set; }
  public string Id => id;
  public void ValidateParameters(JsonElement parameters) => inner.ValidateParameters(parameters);
  public string GetTargetKey(DeviceContext context, JsonElement parameters) => "fixture." + id;
  public Task<ActionReadiness> InspectAsync(DeviceContext context, JsonElement parameters, CancellationToken ct) => inner.InspectAsync(context, parameters, ct);
  public Task<IReadOnlyList<ProbeObservation>> CaptureBeforeAsync(DeviceContext context, JsonElement parameters, CancellationToken ct) => PreflightFailure is { } failure ? throw failure() : inner.CaptureBeforeAsync(context, parameters, ct);
  public Task<ActionResult> ExecuteAsync(DeviceContext context, PlannedAction action, CancellationToken ct) => ExecutionFailure is { } failure ? throw failure() : inner.ExecuteAsync(context, action, ct);
 }
}
