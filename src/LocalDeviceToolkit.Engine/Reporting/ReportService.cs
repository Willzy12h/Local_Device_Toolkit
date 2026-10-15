using System.Text;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Assessment;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Execution;
namespace LocalDeviceToolkit.Engine.Reporting;
/// <summary>Projects only selected immutable records, never a fabricated capture or deployment.</summary>
public sealed class ReportService(IEvidenceStore store, LocalCatalogue catalogue, DeviceContext context)
{
 readonly EvidenceSchemaRegistry schemas = CreateSchemas();
 static EvidenceSchemaRegistry CreateSchemas() { var schemas = ToolkitEvidenceSchemas.Create(); ReportingEvidenceSchemas.Register(schemas); LocalDeviceToolkit.Engine.Deployments.DeploymentEvidenceSchemas.Register(schemas); return schemas; }
 public Task<HistoryPage> ReadHistoryAsync(string? cursor, CancellationToken ct) => store.ReadHistoryAsync(cursor, ct);
 async Task<EvidenceRecord> LoadAsync(Guid id, CancellationToken ct)
 {
  var record = await store.LoadAsync(id, ct);
  if (record.Id != id || record.SchemaVersion != 1 || record.Context.DeviceId != context.DeviceId || record.Context.OriginalUserSid != context.OriginalUserSid ||
      record.Digest != CanonicalJson.Digest(record with { Digest = "" })) throw new EvidenceValidationException("Report record identity, schema, context or digest mismatch.");
  schemas.Validate(record); return record;
 }
 static T Read<T>(EvidenceRecord record) => StrictJson.Read<T>(Encoding.UTF8.GetBytes(record.Data.GetRawText()));
 public async Task<LocalReport> BuildAsync(ReportPurpose purpose, IReadOnlyList<Guid> recordIds, CancellationToken ct)
 {
  if (!Enum.IsDefined(purpose) || recordIds.Count is < 1 or > 500) throw new EvidenceValidationException("Select between 1 and 500 saved evidence records.");
  var records = new List<EvidenceRecord>();
  foreach (var id in recordIds.Distinct()) records.Add(await LoadAsync(id, ct));
  var captures = records.Where(r => r.Kind == "capture").Select(Read<DeviceCapture>).ToArray();
  var reviews = records.Where(r => r.Kind == "manual-review").Select(Read<ManualReview>).ToArray();
  var runs = records.Where(r => r.Kind == "run").Select(Read<RunRecord>).ToArray();
  if (runs.Length > 1) throw new EvidenceValidationException("Select one deployment run per report.");
  var run = runs.SingleOrDefault();
  var plans = records.Where(r => r.Kind == "plan").Select(Read<ReviewedPlan>).ToList();
  if (run is not null && plans.All(p => p.Id != run.PlanId)) { var record = await LoadAsync(run.PlanId, ct); if (record.Kind != "plan") throw new EvidenceValidationException("Run references a non-plan record."); records.Add(record); plans.Add(Read<ReviewedPlan>(record)); }
  if (plans.Count > 1) throw new EvidenceValidationException("Select one reviewed plan per report.");
  var plan = plans.SingleOrDefault();
  if (captures.Any(c => c.Catalogue != catalogue.Identity) || reviews.Any(r => r.Catalogue != catalogue.Identity || !catalogue.Controls.Any(c => c.Id == r.ControlId)) || plans.Any(p => p.Catalogue != catalogue.Identity))
   throw new EvidenceValidationException("Evidence belongs to a different catalogue or unknown control; it cannot establish current compliance.");
  if (plan is not null && (plan.Actions.Any(a => a.Id == Guid.Empty) || plan.Actions.Select(a => a.Id).Distinct().Count() != plan.Actions.Count)) throw new EvidenceValidationException("Reviewed plan action identities are invalid or duplicated.");
  if (run is not null && (plan is null || run.PlanId != plan.Id || run.Context != plan.Context || run.Results.Select(r => r.ActionId).Distinct().Count() != run.Results.Count || run.Results.Any(r => !plan.Actions.Any(a => a.Id == r.ActionId))))
   throw new EvidenceValidationException("Deployment run and reviewed plan do not match.");
  var dispatchIntents = records.Where(r => r.Kind == "intent").Select(Read<IntentRecord>).ToArray();
  var standaloneResults = records.Where(r => r.Kind == "result").Select(Read<ResultRecord>).ToArray();
  var intents = dispatchIntents.Where(i => run is null || i.RunId != run.Id || !run.Results.Any(r => r.ActionId == i.Action.Id)).ToArray();
  var now = DateTimeOffset.UtcNow;
  if (captures.Any(c => c.CapturedAt > now.AddMinutes(2)) || reviews.Any(r => r.ReviewedAt > now.AddMinutes(2))) throw new EvidenceValidationException("Evidence is future dated.");
  var controls = new List<ReportControl>();
  foreach (var control in catalogue.Controls.Where(c => purpose == ReportPurpose.SetupHandover || captures.Any(cap => cap.Observations.Any(o => o.ControlId == c.Id)) || reviews.Any(r => r.ControlId == c.Id)))
  {
   // Select a complete historical capture, not individual latest checks across captures.
   var capture = captures.Where(c => c.Observations.Any(o => o.ControlId == control.Id)).OrderByDescending(c => c.CapturedAt).ThenBy(c => c.Id).FirstOrDefault();
   var review = reviews.Where(r => r.ControlId == control.Id).OrderByDescending(r => r.ReviewedAt).ThenBy(r => r.Id).FirstOrDefault();
   var stale = capture is not null && (now - capture.CapturedAt > TimeSpan.FromMinutes(10) || capture.Observations.Where(o => o.ControlId == control.Id).Any(o => now - o.CapturedAt > TimeSpan.FromMinutes(10)));
   var finding = capture is null ? new Finding(control.Id, FindingStatus.Unknown, "No selected capture contains this control.", []) : AssessmentService.Assess(catalogue with { Controls = [control] }, capture).Single();
   var status = finding.Status; var reason = finding.Reason;
   if (stale) { status = FindingStatus.Unknown; reason = "Dated snapshot exceeds ten minutes; historical finding: " + finding.Status + ". " + reason; }
   var eligible = control.Coverage == CoverageMode.Manual || finding.Status == FindingStatus.ManualVerification;
   if (review is not null && eligible && !stale && (capture is null || review.ReviewedAt >= capture.CapturedAt) && now - review.ReviewedAt <= TimeSpan.FromMinutes(10))
   {
    status = review.Outcome switch { ManualReviewOutcome.Verified => FindingStatus.MeetsStandard, ManualReviewOutcome.Failed => FindingStatus.DoesNotMeetStandard, _ => FindingStatus.NotApplicable };
    reason = "Recorded manual review: " + review.Outcome + ". " + review.Note + (review.ScopeReason is null ? "" : " Scope/reason: " + review.ScopeReason);
   }
   controls.Add(new(control.Id, control.Name, control.ExpectedDescription, control.Coverage, status, reason, capture?.Id, capture?.CapturedAt, capture?.CapturedBy, stale, finding.Observations, review));
  }
  var resolutions=records.Where(r=>r.Kind=="deployment-resolution").Select(Read<LocalDeviceToolkit.Engine.Deployments.DeploymentResolution>).ToArray();
  var limitations = new List<string> { "Selected, dated local evidence only; this report is not a claim of current compliance. Only the displayed selected records were considered, not all history.", "Inventory does not establish application licensing, sign-in, escrow, tenant policy or client approval. Manual reviews and excluded scope are not automated success.", "Capture IDs, actors and timestamps remain distinct. No checks are combined across captures to invent completeness." };
  if(resolutions.Length!=0)limitations.Add("Deployment completion reviews are explicit administrator observations. They preserve the original pending requester and actual reviewer; they do not rewrite the original uncertain run or prove current catalogue compliance.");
  if (intents.Length != 0) limitations.Add("Intent alone supplies no independently validated catalogue identity or current compliance. Unfinished dispatch intent: no selected complete run/result establishes its outcome. Do not replay; use operation-specific read-only reconciliation.");
  if (run is { EvidenceComplete: false }) limitations.Add("Run result persistence was incomplete; recorded outcomes may be missing.");
  if (run is not null && plan is not null && run.Results.Count != plan.Actions.Count) limitations.Add("Some planned actions have no recorded result: stopped, cancelled or uncertain; never infer success.");
  if (records.Any(r => r.Kind == "result")) limitations.Add("Standalone result records are retained as sources; they do not establish a complete deployment run or current catalogue compliance.");
  return new(purpose, now, captures.FirstOrDefault()?.Context ?? plan?.Context ?? context, catalogue.Identity, records.Select(r => r.Id).ToArray(), captures, reviews, plan, run, controls,
   plan?.Actions.Select(a => new ReportAction(a, run?.Results.SingleOrDefault(r => r.ActionId == a.Id))).ToArray() ?? [], dispatchIntents, intents, standaloneResults, limitations,resolutions);
 }
}
