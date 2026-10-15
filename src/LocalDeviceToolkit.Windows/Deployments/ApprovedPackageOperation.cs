using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Engine.Execution;
namespace LocalDeviceToolkit.Windows.Deployments;
/// <summary>Reloads immutable approved profile IDs. User copies supply previews; the worker independently uses protected copies.</summary>
public sealed class ApprovedPackageOperation(ApprovedProfileStore profiles,IDeploymentPendingStore pending):ILocalOperation
{
 public string Id=>"packages.install";
 public PackageDeploymentOperation Resolve(JsonElement parameters)
 {
  DeploymentParameters input;
  try{input=StrictJson.Read<DeploymentParameters>(StrictJson.Bytes(parameters),8192);}catch(Exception e) when(e is JsonException or InvalidDataException or InvalidOperationException){throw new PlanValidationException("Choose an approved profile and its local media directory.");}
  try {var profile=profiles.LoadAsync(input.ProfileId,default).GetAwaiter().GetResult();return new([profile],new WindowsDeploymentBackend(),pending);}
  catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException){throw new PlanValidationException("Approved deployment profile unavailable: "+e.Message);}
 }
 public void ValidateParameters(JsonElement parameters)=>Resolve(parameters).ValidateParameters(parameters);
 public string GetTargetKey(DeviceContext context,JsonElement parameters)=>Resolve(parameters).GetTargetKey(context,parameters);
 public Task<ActionReadiness> InspectAsync(DeviceContext context,JsonElement parameters,CancellationToken ct)=>Resolve(parameters).InspectAsync(context,parameters,ct);
 public Task<IReadOnlyList<ProbeObservation>> CaptureBeforeAsync(DeviceContext context,JsonElement parameters,CancellationToken ct)=>Resolve(parameters).CaptureBeforeAsync(context,parameters,ct);
 public Task<ActionResult> ExecuteAsync(DeviceContext context,PlannedAction action,CancellationToken ct)=>Resolve(action.Request.Parameters).ExecuteAsync(context,action,ct);
}
