using LocalDeviceToolkit.Core;
namespace LocalDeviceToolkit.Engine.Evidence;

public sealed record CaptureSelectionEvidence(int SchemaVersion,Guid CaptureId,IReadOnlyList<string> TaskIds,IReadOnlyList<string> CheckIds);
