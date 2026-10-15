using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Reporting;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class ReportingTests : IDisposable
{
 readonly string root = Path.Combine(Path.GetTempPath(), "report-tests-" + Guid.NewGuid());
 readonly LocalCatalogue catalogue = CatalogueLoader.LoadShipped();
 public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
 EvidenceStore Store() { var schemas = ToolkitEvidenceSchemas.Create(); ReportingEvidenceSchemas.Register(schemas); return new(root, schemas, TestData.Context); }
 [Fact] public async Task Read_only_report_has_no_run_and_setup_lists_every_control_once()
 {
  var store = Store(); var capture = TestData.Capture(); await store.SaveNewAsync(EvidenceRecord.Capture(capture), default);
  var report = await new ReportService(store, catalogue, TestData.Context).BuildAsync(ReportPurpose.SetupHandover, [capture.Id], default);
  Assert.Null(report.Run); Assert.Equal(30, report.Controls.Count); Assert.Equal(30, report.Controls.Select(c => c.ControlId).Distinct().Count()); Assert.Equal(0, report.VerifiedCount);
 }
 [Fact] public async Task Latest_whole_capture_does_not_merge_partial_checks_and_stale_is_unknown()
 {
  var control = new LocalControl("test", "Test", "Both", CoverageMode.Automatic, ["secureboot.enabled", "tpm.ready"], [], "");
  var local = catalogue with { Controls = [control] }; var store = Store(); var time = DateTimeOffset.UtcNow.AddHours(-1);
  var first = TestData.Capture() with { CapturedAt = time, Observations = [new("test", "secureboot.enabled", ObservationState.Known, StrictJson.Value(true), "source", time, "")] };
  var second = first with { Id = Guid.NewGuid(), CapturedAt = time.AddMinutes(1), Observations = [new("test", "tpm.ready", ObservationState.Known, StrictJson.Value(true), "source", time, "")] };
  await store.SaveNewAsync(EvidenceRecord.Capture(first), default); await store.SaveNewAsync(EvidenceRecord.Capture(second), default);
  var report = await new ReportService(store, local, TestData.Context).BuildAsync(ReportPurpose.DiagnosticTicket, [first.Id, second.Id, first.Id], default);
  var row = Assert.Single(report.Controls); Assert.Equal(second.Id, row.CaptureId); Assert.Single(row.Observations); Assert.Equal(FindingStatus.Unknown, row.Status); Assert.True(row.Stale); Assert.Equal(2, report.Captures.Count);
 }
 [Fact] public async Task Wrong_catalogue_and_wrong_kind_are_rejected()
 {
  var store = Store(); var capture = TestData.Capture() with { Catalogue = catalogue.Identity with { Version = "old" } }; await store.SaveNewAsync(EvidenceRecord.Capture(capture), default);
  await Assert.ThrowsAsync<EvidenceValidationException>(() => new ReportService(store, catalogue, TestData.Context).BuildAsync(ReportPurpose.DiagnosticTicket, [capture.Id], default));
 }
 [Theory] [InlineData("=1+1")] [InlineData(" \t@SUM(A1)")] [InlineData("\r\n-2+3")] [InlineData("\u0001+cmd")]
 public void Csv_neutralizes_formulas_after_whitespace_and_controls(string input) { Assert.StartsWith("\"'", ReportExporter.CsvCell(input)); }
 [Fact] public async Task Export_encodes_markup_and_refuses_overwrite()
 {
  var store = Store(); var capture = TestData.Capture() with { Context = TestData.Context with { ComputerName = "<script>alert(1)</script>é" } }; await store.SaveNewAsync(EvidenceRecord.Capture(capture), default);
  var report = await new ReportService(store, catalogue, TestData.Context).BuildAsync(ReportPurpose.SetupHandover, [capture.Id], default);
  var html = ReportExporter.Render(report, ReportFormat.Html); Assert.DoesNotContain("<script>", html); Assert.Contains("&lt;script&gt;", html);
  var path = Path.Combine(root, "report.html"); await ReportExporter.ExportAsync(report, ReportFormat.Html, path, default);
  await Assert.ThrowsAsync<IOException>(() => ReportExporter.ExportAsync(report, ReportFormat.Html, path, default));
 }
 [Fact] public async Task Manual_review_is_bound_to_engineer_and_persists_after_restart()
 {
  var store = Store(); var recorder = new ManualReviewRecorder(store, catalogue, TestData.Context, _ => Task.FromResult(TestData.Actor));
  var control = catalogue.Controls.First(c => c.Coverage == CoverageMode.Manual);
  var review = await recorder.RecordAsync(control.Id, ManualReviewOutcome.Verified, "Inspected against procedure", "Engineer observation", null, default);
  var loaded = await Store().LoadAsync(review.Id, default); Assert.Equal("manual-review", loaded.Kind);
  var wrong = new ManualReviewRecorder(store, catalogue, TestData.Context, _ => Task.FromResult(TestData.Actor with { Sid = "other" }));
  await Assert.ThrowsAsync<EvidenceValidationException>(() => wrong.RecordAsync(control.Id, ManualReviewOutcome.Verified, "note", "source", null, default));
 }
 [Theory] [InlineData(ObservationState.Known, false, FindingStatus.DoesNotMeetStandard)] [InlineData(ObservationState.AccessDenied, true, FindingStatus.RequiresElevation)] [InlineData(ObservationState.Unavailable, true, FindingStatus.Unknown)]
 public async Task Manual_review_cannot_override_failed_denied_or_unavailable_automatic_checks(ObservationState state, bool value, FindingStatus expected)
 {
  var store = Store(); var control = catalogue.Controls.First(c => c.CheckIds.Contains("bitlocker.protected") && c.CheckIds.Length == 1);
  var time = DateTimeOffset.UtcNow;
  var capture = TestData.Capture() with { CapturedAt = time, Observations = [new(control.Id, "bitlocker.protected", state, StrictJson.Value(value), "measured", time, "reason")] };
  await store.SaveNewAsync(EvidenceRecord.Capture(capture), default);
  var review = await new ManualReviewRecorder(store, catalogue, TestData.Context, _ => Task.FromResult(TestData.Actor)).RecordAsync(control.Id, ManualReviewOutcome.Verified, "Checked escrow", "Tenant console", null, default);
  var report = await new ReportService(store, catalogue, TestData.Context).BuildAsync(ReportPurpose.DiagnosticTicket, [capture.Id, review.Id], default);
  Assert.Equal(expected, Assert.Single(report.Controls).Status); Assert.Equal(0, report.VerifiedCount);
 }
 [Fact] public async Task Explicit_exclusion_stays_distinct_from_automated_success()
 {
  var store = Store(); var control = catalogue.Controls.First(c => c.Coverage == CoverageMode.Manual);
  var review = await new ManualReviewRecorder(store, catalogue, TestData.Context, _ => Task.FromResult(TestData.Actor)).RecordAsync(control.Id, ManualReviewOutcome.Excluded, "Not in scope", "Engineer scope assessment", "No approved tenant access", default);
  var report = await new ReportService(store, catalogue, TestData.Context).BuildAsync(ReportPurpose.DiagnosticTicket, [review.Id], default);
  Assert.Equal(FindingStatus.NotApplicable, Assert.Single(report.Controls).Status); Assert.Equal(ManualReviewOutcome.Excluded, report.Controls[0].ManualReview!.Outcome); Assert.Equal(0, report.VerifiedCount);
 }
 [Fact] public async Task Cancelled_export_does_not_leave_a_false_report_file()
 {
  var store = Store(); var capture = TestData.Capture(); await store.SaveNewAsync(EvidenceRecord.Capture(capture), default);
  var report = await new ReportService(store, catalogue, TestData.Context).BuildAsync(ReportPurpose.SetupHandover, [capture.Id], default);
  var path = Path.Combine(root, "cancelled.json"); using var stop = new CancellationTokenSource(); stop.Cancel();
  await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReportExporter.ExportAsync(report, ReportFormat.Json, path, stop.Token));
  Assert.False(File.Exists(path));
 }
 [Fact] public async Task Deployment_unknown_and_missing_results_do_not_count_as_verified()
 {
  var store = Store(); var now = DateTimeOffset.UtcNow;
  var action = new PlannedAction(Guid.NewGuid(), new("task", "operation", StrictJson.Value(new { value = true }), new("setup", "windows")), new(true, OperationScope.Machine, PrivilegeRequirement.Administrator, "ready"), [new("test", "secureboot.enabled", ObservationState.Known, StrictJson.Value(false), "before", now, "before")], "before-digest");
  var plan = new ReviewedPlan(Guid.NewGuid(), TestData.Context, catalogue.Identity, now, now.AddMinutes(5), [action], ""); plan = plan with { Digest = CanonicalJson.Digest(plan) };
  var run = new RunRecord(Guid.NewGuid(), plan.Id, TestData.Context, now, now, [new(action.Id, ActionOutcome.OutcomeUnknown, TestData.Actor with { Sid = "admin", IsElevated = true }, "Lost installer contact", [])], false);
  await store.SaveNewAsync(EvidenceRecord.Create(plan.Id, "plan", plan.Context, plan.CreatedAt, StrictJson.Value(plan)), default);
  await store.SaveNewAsync(EvidenceRecord.Create(run.Id, "run", run.Context, run.FinishedAt!.Value, StrictJson.Value(run)), default);
  var report = await new ReportService(store, catalogue, TestData.Context).BuildAsync(ReportPurpose.DeploymentAudit, [run.Id, run.Id], default);
  Assert.Equal(0, report.VerifiedCount); Assert.Contains("uncertain", report.Completion); Assert.Equal(plan.Id, report.Plan!.Id); Assert.Equal("admin", Assert.Single(report.DeploymentActions).Result!.Actor!.Sid); Assert.Equal(TestData.Context.OriginalUserSid, report.Context.OriginalUserSid); Assert.Equal(2, report.SourceRecordIds.Count);
 }

 [Fact] public async Task Unfinished_intent_remains_uncertain_without_fabricating_run_or_replaying()
 {
  var store = Store(); var time = DateTimeOffset.UtcNow;
  var observation = new ProbeObservation("test", "secureboot.enabled", ObservationState.Known, StrictJson.Value(false), "dispatch read", time, "before dispatch");
  var action = new PlannedAction(Guid.NewGuid(), new("task", "operation", StrictJson.Value(new { }), new("diagnostics", "health")), new(true, OperationScope.Machine, PrivilegeRequirement.Administrator, "ready"), [observation], "digest");
  var intent = new LocalDeviceToolkit.Engine.Execution.IntentRecord(Guid.NewGuid(), Guid.NewGuid(), action, [observation]);
  var record = EvidenceRecord.Create(Guid.NewGuid(), "intent", TestData.Context, time, StrictJson.Value(intent)); await store.SaveNewAsync(record, default);
  var report = await new ReportService(store, catalogue, TestData.Context).BuildAsync(ReportPurpose.DeploymentAudit, [record.Id], default);
  Assert.Null(report.Run); Assert.Single(report.UnfinishedIntents); Assert.Single(report.DispatchIntents); Assert.Contains("uncertain", report.Completion); Assert.Equal(0, report.VerifiedCount);
  Assert.Equal(record.Digest, (await store.LoadAsync(record.Id, default)).Digest);
 }
 [Fact] public async Task History_pages_preserve_corruption_diagnostics_and_next_cursor()
 {
  var store = Store(); for (var i = 0; i < 101; i++) await store.SaveNewAsync(EvidenceRecord.Capture(TestData.Capture()), default);
  await File.WriteAllTextAsync(Path.Combine(root, "00000000-0000-0000-0000-000000000001.json"), "{broken");
  var service = new ReportService(store, catalogue, TestData.Context); var first = await service.ReadHistoryAsync(null, default);
  Assert.NotNull(first.NextCursor); Assert.Contains(first.Diagnostics, d => d.Code == "MalformedRecord");
  var second = await service.ReadHistoryAsync(first.NextCursor, default); Assert.Null(second.NextCursor); Assert.Equal(101, first.Entries.Count + second.Entries.Count);
 }

 [Fact] public async Task Complete_but_stale_automatic_snapshot_is_unknown_and_retains_actor_and_time()
 {
  var store = Store(); var control = new LocalControl("test", "Test", "Secure Boot", CoverageMode.Automatic, ["secureboot.enabled"], [], "");
  var local = catalogue with { Controls = [control] }; var time = DateTimeOffset.UtcNow.AddMinutes(-11); var actor = TestData.Actor with { Sid = "actual-admin", IsElevated = true };
  var capture = TestData.Capture() with { CapturedAt = time, CapturedBy = actor, Observations = [new(control.Id, "secureboot.enabled", ObservationState.Known, StrictJson.Value(true), "measured", time, "observed")] };
  await store.SaveNewAsync(EvidenceRecord.Capture(capture), default);
  var report = await new ReportService(store, local, TestData.Context).BuildAsync(ReportPurpose.DiagnosticTicket, [capture.Id], default);
  var row = Assert.Single(report.Controls); Assert.True(row.Stale); Assert.Equal(FindingStatus.Unknown, row.Status); Assert.Equal(time, row.CapturedAt); Assert.Equal(actor, row.CapturedBy); Assert.Equal(0, report.VerifiedCount);
 }
 [Fact] public async Task Current_manual_review_completes_partial_verification_without_automated_success_count()
 {
  var store = Store(); var time = DateTimeOffset.UtcNow; var control = catalogue.Controls.First(c => c.CheckIds.SequenceEqual(new[] { "bitlocker.protected" }));
  var capture = TestData.Capture() with { CapturedAt = time, Observations = [new(control.Id, "bitlocker.protected", ObservationState.Known, StrictJson.Value(true), "measured", time, "observed")] };
  await store.SaveNewAsync(EvidenceRecord.Capture(capture), default);
  var review = await new ManualReviewRecorder(store, catalogue, TestData.Context, _ => Task.FromResult(TestData.Actor)).RecordAsync(control.Id, ManualReviewOutcome.Verified, "Escrow verified in tenant console", "Approved recovery procedure", null, default);
  var report = await new ReportService(store, catalogue, TestData.Context).BuildAsync(ReportPurpose.DiagnosticTicket, [capture.Id, review.Id], default);
  Assert.Equal(FindingStatus.MeetsStandard, Assert.Single(report.Controls).Status); Assert.NotNull(report.Controls[0].ManualReview); Assert.Equal(0, report.VerifiedCount);
 }
 [Fact] public async Task Manual_review_notes_are_bounded_and_exclusions_require_explicit_scope()
 {
  var store = Store(); var control = catalogue.Controls.First(); var recorder = new ManualReviewRecorder(store, catalogue, TestData.Context, _ => Task.FromResult(TestData.Actor));
  await Assert.ThrowsAsync<EvidenceValidationException>(() => recorder.RecordAsync(control.Id, ManualReviewOutcome.Verified, new string('x', 4001), "source", null, default));
  await Assert.ThrowsAsync<EvidenceValidationException>(() => recorder.RecordAsync(control.Id, ManualReviewOutcome.Excluded, "note", "source", null, default));
 }
 [Fact] public void Csv_quotes_newlines_quotes_and_preserves_non_ascii()
 {
  Assert.Equal("\"a,b\r\n\"\"é\"\"\"", ReportExporter.CsvCell("a,b\r\n\"é\""));
 }

 [Fact] public void Csv_missing_optional_text_is_an_empty_cell() { Assert.Equal("\"\"", ReportExporter.CsvCell(null!)); }

}
