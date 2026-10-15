using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
namespace LocalDeviceToolkit.Engine.Reporting;
public enum ReportPurpose { SetupHandover, DiagnosticTicket, DeploymentAudit }
public enum ReportFormat { Html, Csv, Json }
public enum ManualReviewOutcome { Verified, Failed, NotApplicable, Excluded }
public sealed record ManualReview(Guid Id, string ControlId, string Note, string Source, ManualReviewOutcome Outcome,
 string? ScopeReason, ExecutionActor Actor, DateTimeOffset ReviewedAt, CatalogueIdentity Catalogue, DeviceContext Context);
public sealed record ReportControl(string ControlId, string Name, string Expected, CoverageMode Coverage, FindingStatus Status,
 string Reason, Guid? CaptureId, DateTimeOffset? CapturedAt, ExecutionActor? CapturedBy, bool Stale,
 IReadOnlyList<ProbeObservation> Observations, ManualReview? ManualReview);
public sealed record ReportAction(PlannedAction Planned, ActionResult? Result);
public sealed record LocalReport(ReportPurpose Purpose, DateTimeOffset GeneratedAt, DeviceContext Context, CatalogueIdentity Catalogue,
 IReadOnlyList<Guid> SourceRecordIds, IReadOnlyList<DeviceCapture> Captures, IReadOnlyList<ManualReview> ManualReviews,
 ReviewedPlan? Plan, RunRecord? Run, IReadOnlyList<ReportControl> Controls, IReadOnlyList<ReportAction> DeploymentActions,
 IReadOnlyList<IntentRecord> DispatchIntents, IReadOnlyList<IntentRecord> UnfinishedIntents, IReadOnlyList<ResultRecord> StandaloneResults, IReadOnlyList<string> Limitations,IReadOnlyList<LocalDeviceToolkit.Engine.Deployments.DeploymentResolution>? DeploymentResolutions=null)
{
 public int VerifiedCount => Controls.Count(c => c.Status == FindingStatus.MeetsStandard && c.ManualReview is null && !c.Stale);
 public string Completion => UnfinishedIntents.Count != 0 || StandaloneResults.Any(r => Run is null || r.RunId != Run.Id || r.PlanId != Run.PlanId) || Run is { EvidenceComplete: false } || DeploymentActions.Any(a => a.Result is null || a.Result.Outcome != ActionOutcome.AppliedVerified)
  ? "Incomplete or uncertain; inspect recorded outcomes and limitations."
  : Controls.Count == 0 || Controls.Any(c => c.Status != FindingStatus.MeetsStandard) ? "Snapshot has outstanding verification or scope limitations." : "Declared checks and recorded reviews complete for the dated snapshots only.";
}
public sealed record ExportReceipt(string Destination, ReportFormat Format, long Bytes);
