using LocalDeviceToolkit.Core;
using System.Buffers.Binary;
using LocalDeviceToolkit.Engine.Execution;
namespace LocalDeviceToolkit.Engine.Elevation;
public enum WorkerMode { CaptureProtected,ExecuteAction,ApproveDeploymentProfile,ReconcileDeployment,ResolveDeployment,ReconcileDiagnostic,ResolveDiagnostic }
public sealed record DeploymentProfileApproval(LocalDeviceToolkit.Engine.Deployments.DeploymentProfile Profile,string SourceDirectory);
public sealed record WorkerRequest(int SchemaVersion,WorkerMode Mode,DeviceContext OriginalContext,CaptureRequest? Capture,ReviewedPlan? Plan,Guid? ActionId,string? ToolkitBuild=null,DeploymentProfileApproval? DeploymentApproval=null,LocalDeviceToolkit.Engine.Deployments.DeploymentParameters? DeploymentReconcile=null,LocalDeviceToolkit.Engine.Deployments.DeploymentResolutionReview? DeploymentResolutionReview=null,bool? DiagnosticCompletionReview=null);
public sealed record WorkerResponse(int SchemaVersion,bool Success,string Message,ExecutionActor Actor,DeviceCapture? Capture,ActionResult? Result,LocalDeviceToolkit.Engine.Deployments.DeploymentProfile? ApprovedProfile=null,LocalDeviceToolkit.Engine.Deployments.DeploymentReconciliation? DeploymentStatus=null,LocalDeviceToolkit.Engine.Evidence.EvidenceRecord? DeploymentResolution=null,LocalDeviceToolkit.Engine.Evidence.EvidenceRecord? DiagnosticResolution=null);
public static class WorkerFrames
{
 public const int MaximumBytes=1024*1024;
 public static async Task WriteAsync<T>(Stream stream,T value,CancellationToken ct)
 {
  var bytes=StrictJson.Bytes(value);if(bytes.Length is <1 or >MaximumBytes)throw new InvalidDataException("Worker message exceeds the frame limit.");
  var length=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(length,bytes.Length);await stream.WriteAsync(length,ct);await stream.WriteAsync(bytes,ct);await stream.FlushAsync(ct);
 }
 public static async Task<T> ReadAsync<T>(Stream stream,CancellationToken ct)
 {
  var length=new byte[4];await stream.ReadExactlyAsync(length,ct);var size=BinaryPrimitives.ReadInt32LittleEndian(length);
  if(size is <1 or >MaximumBytes)throw new InvalidDataException("Invalid worker frame length.");
  var bytes=new byte[size];await stream.ReadExactlyAsync(bytes,ct);return StrictJson.Read<T>(bytes,MaximumBytes);
 }
}
public static class WorkerGuard
{
 public static void ValidateBuild(string? requested,string actual){if(string.IsNullOrWhiteSpace(requested)||requested.Length>256||!string.Equals(requested,actual,StringComparison.Ordinal))throw new PlanValidationException("The toolkit components belong to different builds. Extract a complete matching package before continuing.");}
 public static void ValidateOriginal(DeviceContext claimed,DeviceContext machine,ExecutionActor observedParent)
 {
  if(claimed is null||machine is null||observedParent is null||observedParent.ProcessId<=0||claimed.DeviceId!=machine.DeviceId||claimed.ComputerName!=machine.ComputerName||claimed.OsVersion!=machine.OsVersion||claimed.Architecture!=machine.Architecture||claimed.IsWorkstation!=machine.IsWorkstation||claimed.OriginalUserSid!=observedParent.Sid||!string.Equals(claimed.OriginalUserName,observedParent.UserName,StringComparison.OrdinalIgnoreCase)||claimed.IsElevated!=observedParent.IsElevated)
   throw new PlanValidationException("The request's device or original user does not match the Windows-observed parent process.");
 }
}
