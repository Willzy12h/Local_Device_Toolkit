using LocalDeviceToolkit.Core;

namespace LocalDeviceToolkit.Engine.Execution;

/// <summary>Companion progress reporting. Protected evidence remains execution authority.</summary>
public interface IRunProgressSink
{
 Task BeforeActionAsync(ReviewedPlan plan, PlannedAction action, CancellationToken ct);
 Task AfterActionAsync(ReviewedPlan plan, PlannedAction action, ActionResult result, CancellationToken ct);
}
