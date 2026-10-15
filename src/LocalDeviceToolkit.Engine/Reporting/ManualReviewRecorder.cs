using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Evidence;
namespace LocalDeviceToolkit.Engine.Reporting;
public static class ReportingEvidenceSchemas
{
 public static void Register(EvidenceSchemaRegistry schemas) => schemas.Register<ManualReview>("manual-review", Validate);
 internal static void Validate(ManualReview review, EvidenceRecord record)
 {
  if (review is null || review.Context is null || review.Id == Guid.Empty || review.Id != record.Id || review.Context != record.Context || review.ReviewedAt != record.RecordedAt ||
      review.ReviewedAt.Offset != TimeSpan.Zero || review.Catalogue is null || review.Actor is null ||
      review.Actor.Sid != review.Context.OriginalUserSid || string.IsNullOrWhiteSpace(review.Actor.UserName) || review.Actor.ProcessId <= 0 || review.Actor.ProcessStartedAt.Offset != TimeSpan.Zero ||
      string.IsNullOrWhiteSpace(review.ControlId) || review.ControlId.Length > 200 || string.IsNullOrWhiteSpace(review.Note) || review.Note.Length > 4000 ||
      string.IsNullOrWhiteSpace(review.Source) || review.Source.Length > 1000 || !Enum.IsDefined(review.Outcome) || review.ScopeReason?.Length > 4000 ||
      (review.Outcome is ManualReviewOutcome.Excluded or ManualReviewOutcome.NotApplicable && string.IsNullOrWhiteSpace(review.ScopeReason)))
   throw new EvidenceValidationException("Invalid bounded manual review or original-engineer identity.");
 }
}
public sealed class ManualReviewRecorder(IEvidenceStore store, LocalCatalogue catalogue, DeviceContext context,
 Func<CancellationToken, Task<ExecutionActor>> actorProvider)
{
 public async Task<ManualReview> RecordAsync(string controlId, ManualReviewOutcome outcome, string note, string source, string? scopeReason, CancellationToken ct)
 {
  if (!catalogue.Controls.Any(c => c.Id == controlId)) throw new EvidenceValidationException("Unknown control for manual review.");
  var review = new ManualReview(Guid.NewGuid(), controlId, note, source, outcome, scopeReason, await actorProvider(ct), DateTimeOffset.UtcNow, catalogue.Identity, context);
  var record = EvidenceRecord.Create(review.Id, "manual-review", context, review.ReviewedAt, StrictJson.Value(review));
  ReportingEvidenceSchemas.Validate(review, record);
  await store.SaveNewAsync(record, ct); return review;
 }
}
