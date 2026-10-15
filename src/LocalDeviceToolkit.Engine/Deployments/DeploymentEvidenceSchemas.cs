using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Evidence;
namespace LocalDeviceToolkit.Engine.Deployments;
public sealed record DeploymentResolutionReview(bool ConfirmVendorCompletionAndNoPendingChildren);
public sealed record DeploymentResolution(Guid Id,DeviceContext Context,DeploymentPending Pending,DeploymentInventory Inventory,DeploymentProcessStatus ProcessStatus,ExecutionActor ReviewedBy,DateTimeOffset ReviewedAt,bool ConfirmVendorCompletionAndNoPendingChildren);
public static class DeploymentEvidenceSchemas
{
 public static void Register(EvidenceSchemaRegistry schemas)
 {
  schemas.Register<DeploymentResolution>("deployment-resolution",(resolution,record)=>
  {
   if(resolution.Id!=record.Id||resolution.Context!=record.Context||resolution.ReviewedAt!=record.RecordedAt||resolution.Pending is null||resolution.Inventory is null||resolution.ReviewedBy is null||resolution.ReviewedAt.Offset!=TimeSpan.Zero||resolution.ReviewedAt<resolution.Pending.ClaimedAt||!resolution.ReviewedBy.IsElevated||!resolution.Inventory.Available||!resolution.Inventory.Present||string.IsNullOrWhiteSpace(resolution.Inventory.ProductIdentity)||string.IsNullOrWhiteSpace(resolution.Inventory.Version)||!resolution.ConfirmVendorCompletionAndNoPendingChildren||resolution.ProcessStatus is not (DeploymentProcessStatus.NotRunning or DeploymentProcessStatus.IdentityMismatch))throw new EvidenceValidationException("Invalid explicit deployment resolution receipt.");
  });
 }
}
