using LocalDeviceToolkit.Core;
namespace LocalDeviceToolkit.Engine.Execution;
public interface IDeviceInspector { Task<DeviceCapture> CaptureAsync(CaptureRequest request,CancellationToken ct); }
public interface IExecutionContextProvider { Task<DeviceContext> GetCurrentAsync(CancellationToken ct); }
public interface ILocalOperation
{
 string Id {get;}
 void ValidateParameters(System.Text.Json.JsonElement parameters);
 string GetTargetKey(DeviceContext context,System.Text.Json.JsonElement parameters);
 Task<ActionReadiness> InspectAsync(DeviceContext context,System.Text.Json.JsonElement parameters,CancellationToken ct);
 Task<IReadOnlyList<ProbeObservation>> CaptureBeforeAsync(DeviceContext context,System.Text.Json.JsonElement parameters,CancellationToken ct);
 Task<ActionResult> ExecuteAsync(DeviceContext context,PlannedAction action,CancellationToken ct);
}
public interface IActionDispatcher { Task<ActionResult> DispatchAsync(ReviewedPlan plan,PlannedAction action,CancellationToken ct); }
public sealed class PlanValidationException(string message):Exception(message);
public sealed record PlanValidation(bool IsValid,IReadOnlyList<string> Reasons);
public enum QueueAddResult { Added, AlreadyQueued, ConflictingParameters }
public sealed record IntentRecord(Guid RunId,Guid PlanId,PlannedAction Action,IReadOnlyList<ProbeObservation> BeforeAtDispatch);
public sealed record ResultRecord(Guid RunId,Guid PlanId,ActionResult Result);
