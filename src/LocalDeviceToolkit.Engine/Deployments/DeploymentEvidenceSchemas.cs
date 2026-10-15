using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Evidence;
namespace LocalDeviceToolkit.Engine.Deployments;
public sealed record DeploymentResolutionReview(bool ConfirmVendorCompletionAndNoPendingChildren,OperationScope Scope=OperationScope.Machine,Guid? CurrentBootSession=null);
public sealed record DeploymentResolution(Guid Id,DeviceContext Context,DeploymentPending Pending,DeploymentInventory Inventory,DeploymentProcessStatus ProcessStatus,ExecutionActor ReviewedBy,DateTimeOffset ReviewedAt,bool ConfirmVendorCompletionAndNoPendingChildren,OperationScope Scope=OperationScope.Machine,Guid? CurrentBootSession=null);
public static class DeploymentEvidenceSchemas
{
 public static void Register(EvidenceSchemaRegistry schemas)
 {
  schemas.Register<DeploymentResolution>("deployment-resolution",(resolution,record)=>
  {
   if(resolution.Id!=record.Id||resolution.Context!=record.Context||resolution.ReviewedAt!=record.RecordedAt||resolution.Pending is null||resolution.Inventory is null||resolution.ReviewedBy is null||resolution.ReviewedAt.Offset!=TimeSpan.Zero||resolution.ReviewedAt<resolution.Pending.ClaimedAt||!Enum.IsDefined(resolution.Scope)||(resolution.Scope==OperationScope.Machine?!resolution.ReviewedBy.IsElevated:resolution.ReviewedBy.Sid!=resolution.Context.OriginalUserSid)||!resolution.Inventory.Available||!resolution.Inventory.Present||string.IsNullOrWhiteSpace(resolution.Inventory.ProductIdentity)||string.IsNullOrWhiteSpace(resolution.Inventory.Version)||!resolution.ConfirmVendorCompletionAndNoPendingChildren||resolution.ProcessStatus is not (DeploymentProcessStatus.NotRunning or DeploymentProcessStatus.IdentityMismatch))throw new EvidenceValidationException("Invalid explicit deployment resolution receipt.");
   if(!DeploymentRecoveryProof.HasCompleteProcessIdentity(resolution.Pending.Process)&&(!DeploymentRecoveryProof.HasDifferentBoot(resolution.Pending,resolution.CurrentBootSession)||resolution.ProcessStatus!=DeploymentProcessStatus.NotRunning))throw new EvidenceValidationException("Resolution without process identity requires positive evidence of a different boot session.");
  });
 }
}

internal static class DeploymentRecoveryProof
{
 public static bool HasDifferentBoot(DeploymentPending pending,Guid? observedBoot)=>pending.BootSession is Guid previous&&previous!=Guid.Empty&&observedBoot is Guid current&&current!=Guid.Empty&&previous!=current;
 public static bool HasCompleteProcessIdentity(DeploymentProcessObservation? process)=>process is {ProcessId:>0,StartedAt:DateTimeOffset started}&&started.Offset==TimeSpan.Zero&&started>DateTimeOffset.UnixEpoch&&!string.IsNullOrWhiteSpace(process.ExecutablePath)&&!process.ExecutablePath.Any(char.IsControl)&&Path.IsPathFullyQualified(process.ExecutablePath);
}
