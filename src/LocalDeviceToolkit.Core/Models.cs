using System.Text.Json;
namespace LocalDeviceToolkit.Core;
public enum ObservationState { Known, AccessDenied, Unavailable, NotSupported, Manual }
public enum FindingStatus { MeetsStandard, DoesNotMeetStandard, Unknown, RequiresElevation, NotApplicable, ManualVerification }
public enum CoverageMode { Automatic, Partial, Manual }
public enum OperationScope { CurrentUser, Machine }
public enum PrivilegeRequirement { CurrentUser, Administrator }
public enum ActionOutcome { AppliedVerified, AppliedUnverified, RestartRequired, Failed, CancelledBeforeExecution, OutcomeUnknown }
public sealed record DeviceContext(string DeviceId, string ComputerName, string OriginalUserSid, string OriginalUserName, bool IsElevated, Version OsVersion, string Architecture);
public sealed record ExecutionActor(string Sid, string UserName, bool IsElevated, int ProcessId, DateTimeOffset ProcessStartedAt);
public sealed record CatalogueIdentity(string Version, string SourceCommit, string SourceSha256, string LocalSha256);
public sealed record WorkflowOrigin(string WorkflowId, string StageId);
public sealed record ProbeObservation(string ControlId, string CheckId, ObservationState State, JsonElement? Value, string Source, DateTimeOffset CapturedAt, string Reason);
public sealed record CaptureRequest(IReadOnlyList<string> ControlIds, WorkflowOrigin Origin);
public sealed record DeviceCapture(Guid Id, DeviceContext Context, CatalogueIdentity Catalogue, WorkflowOrigin Origin, ExecutionActor CapturedBy, DateTimeOffset CapturedAt, IReadOnlyList<ProbeObservation> Observations);
public sealed record Finding(string ControlId, FindingStatus Status, string Reason, IReadOnlyList<ProbeObservation> Observations);
public sealed record ActionRequest(string TaskId, string OperationId, JsonElement Parameters, WorkflowOrigin Origin);
public sealed record ActionReadiness(bool CanExecute, OperationScope Scope, PrivilegeRequirement Privilege, string Reason);
public sealed record PlannedAction(Guid Id, ActionRequest Request, ActionReadiness Readiness, string BeforeDigest);
public sealed record ReviewedPlan(Guid Id, DeviceContext Context, CatalogueIdentity Catalogue, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, IReadOnlyList<PlannedAction> Actions, string Digest);
public sealed record ActionResult(Guid ActionId, ActionOutcome Outcome, ExecutionActor? Actor, string Reason, IReadOnlyList<ProbeObservation> After);
public sealed record RunRecord(Guid Id, Guid PlanId, DeviceContext Context, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, IReadOnlyList<ActionResult> Results);
public sealed record LocalControl(string Id, string Name, string ExpectedDescription, CoverageMode Coverage, string[] CheckIds, string[] OperationIds, string ManualInstructions);
public sealed record TaskDefinition(string Id, string Title, string[] ControlIds, string[] CheckIds, string? OperationId);
public sealed record WorkflowStage(string Id, string Title, string[] TaskIds);
public sealed record WorkflowDefinition(string Id, string Title, WorkflowStage[] Stages);
public sealed record LocalCatalogue(CatalogueIdentity Identity, IReadOnlyList<LocalControl> Controls);
public sealed class CatalogueValidationException(string message) : Exception(message);
