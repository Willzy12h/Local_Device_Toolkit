using LocalDeviceToolkit.Core;
namespace LocalDeviceToolkit.Engine.Evidence;

public sealed record CaptureSelectionEvidence(int SchemaVersion,Guid CaptureId,IReadOnlyList<string> TaskIds,IReadOnlyList<string> CheckIds)
{
 [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyDictionary<string,System.Text.Json.JsonElement>? Inputs{get;init;}
}
