using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Evidence;
namespace LocalDeviceToolkit.Engine.Deployments;
public sealed class PackageDeploymentOperation:ILocalOperation
{
 readonly IReadOnlyDictionary<string,DeploymentProfile> profiles;readonly IDeploymentBackend backend;readonly IDeploymentPendingStore pending;
 public PackageDeploymentOperation(IEnumerable<DeploymentProfile> profiles,IDeploymentBackend backend,IDeploymentPendingStore pending)
 {this.profiles=profiles.Select(p=>{DeploymentProfiles.Validate(p);return p with{Files=Array.AsReadOnly(p.Files.ToArray())};}).ToDictionary(p=>p.Id,StringComparer.Ordinal);this.backend=backend;this.pending=pending;}
 public string Id=>"packages.install";
 DeploymentParameters Parameters(JsonElement value)
 {
  try{var p=StrictJson.Read<DeploymentParameters>(System.Text.Encoding.UTF8.GetBytes(value.GetRawText()),8192);if(!profiles.ContainsKey(p.ProfileId)||string.IsNullOrWhiteSpace(p.SourceDirectory)||p.SourceDirectory.Length>2048||!Path.IsPathFullyQualified(p.SourceDirectory))throw new JsonException();return p;}
  catch(Exception e) when(e is JsonException or InvalidDataException or InvalidOperationException or ArgumentException){throw new PlanValidationException("Choose a registered approved deployment profile and an absolute local media directory. Extra fields, commands and argument overrides are refused.");}
 }
 public void ValidateParameters(JsonElement parameters)=>_=Parameters(parameters);
 public string GetTargetKey(DeviceContext context,JsonElement parameters)=>DeploymentTarget.For(context,profiles[Parameters(parameters).ProfileId]);
 public async Task<ActionReadiness> InspectAsync(DeviceContext context,JsonElement parameters,CancellationToken ct)
 {
  var input=Parameters(parameters);var profile=profiles[input.ProfileId];ActionReadiness Ready(bool ready,string why)=>new(ready,profile.Scope,PrivilegeRequirement.Administrator,why);
  if(context.Architecture!="x64"||!context.IsWorkstation||context.OsVersion.Major!=10||context.OsVersion.Build<22000)return Ready(false,"Deployment is supported on Windows 11 x64 workstations only.");
  if(await pending.ReadAsync(DeploymentTarget.For(context,profile),ct)is not null)return Ready(false,"An earlier deployment may have started. Read-only reconciliation is required; automatic replay is blocked.");
  var check=await backend.CheckInputsAsync(profile,input.SourceDirectory,ct);return Ready(check.Accepted,check.Reason);
 }
 const string Check="deployment.review-state";
 async Task<ProbeObservation> Snapshot(DeploymentProfile p,DeviceContext context,string source,CancellationToken ct)
 {
  var check=await backend.CheckInputsAsync(p,source,ct);var inventory=await backend.ReadInventoryAsync(p,context,ct);
  if(!check.Accepted)throw new PlanValidationException(check.Reason);
  return new(p.ControlId,Check,ObservationState.Known,StrictJson.Value(new DeploymentReviewState(p.Id,check.Digest,inventory)),"Verified package manifest and fresh installed-product inventory",DateTimeOffset.UtcNow,"Package input fingerprint and independent installed-state evidence; no installer is executed during review.");
 }
 public async Task<IReadOnlyList<ProbeObservation>> CaptureBeforeAsync(DeviceContext context,JsonElement parameters,CancellationToken ct)
 {var input=Parameters(parameters);return new[]{await Snapshot(profiles[input.ProfileId],context,input.SourceDirectory,ct)};}
 public async Task<ActionResult> ExecuteAsync(DeviceContext context,PlannedAction action,CancellationToken ct)
 {
  var input=Parameters(action.Request.Parameters);var profile=profiles[input.ProfileId];var readiness=await InspectAsync(context,action.Request.Parameters,ct);
  ActionResult Before(string reason)=>new(action.Id,ActionOutcome.Failed,null,"Not started: "+reason,[]);
  if(!readiness.CanExecute||action.Readiness.Scope!=readiness.Scope||action.Readiness.Privilege!=readiness.Privilege)return Before(readiness.Reason);
  var actor=await backend.GetActorAsync(ct);if(!actor.IsElevated)return Before("An authenticated elevated actor is required for this machine profile.");
  var fresh=await Snapshot(profile,context,input.SourceDirectory,ct);
  if(action.Before.Count!=1||ObservationFingerprint.Of(new[]{fresh})!=action.BeforeDigest)return Before("Package inputs or installed state changed after review.");
  var review=StrictJson.Read<DeploymentReviewState>(System.Text.Encoding.UTF8.GetBytes(fresh.Value!.Value.GetRawText()));
  await using var prepared=await backend.PrepareAsync(profile,input.SourceDirectory,context,ct);
  if(prepared.InputDigest!=review.InputDigest)return Before("The staged file set differs from the reviewed input fingerprint.");
  ct.ThrowIfCancellationRequested();
  var claim=new DeploymentPending(action.Id,DeploymentTarget.For(context,profile),context.OriginalUserSid,DateTimeOffset.UtcNow,null);
  await pending.ClaimAsync(claim,ct); // Failure here propagates to the coordinator; mutation has not begun.
  try
  {
   if(ct.IsCancellationRequested){await pending.CompleteAsync(claim,CancellationToken.None);return new(action.Id,ActionOutcome.CancelledBeforeExecution,null,"Queue stopped before installer launch.",[]);}
   var process=await prepared.StartAsync(ct);claim=claim with{Process=process};await pending.ObserveAsync(claim,CancellationToken.None);
   if(process.End==DeploymentProcessEnd.NotStarted){await pending.CompleteAsync(claim,CancellationToken.None);return new(action.Id,ActionOutcome.Failed,actor,"Windows refused to start the approved installer.",[]);}
   var inventory=await backend.ReadInventoryAsync(profile,context,CancellationToken.None);
   var verified=inventory.Available&&inventory.Present&&inventory.ProductIdentity==profile.ProductIdentity&&inventory.Version==profile.Version;
   var after=new ProbeObservation(profile.ControlId,"deployment.installed-product",inventory.Available?ObservationState.Known:ObservationState.Unavailable,inventory.Available?StrictJson.Value(inventory):null,"Fresh installed-product query",DateTimeOffset.UtcNow,inventory.Reason);
   bool uncertain=process.End!=DeploymentProcessEnd.Exited||process.MayDetach;
   ActionOutcome outcome;
   if(uncertain)outcome=ActionOutcome.OutcomeUnknown;
   else if(process.ExitCode is 3010 or 1641)outcome=ActionOutcome.RestartRequired;
   else if(process.ExitCode==0)outcome=verified?ActionOutcome.AppliedVerified:ActionOutcome.AppliedUnverified;
   else outcome=ActionOutcome.Failed;
   // Keep the guard until the coordinator has durably persisted a matching run receipt.
   var reason=uncertain?"Installer completion is uncertain; its process was never killed and no automatic retry is permitted. The persistent pending guard remains. Reconcile fresh product state and process identity; a detached bootstrapper may still have children.":outcome==ActionOutcome.RestartRequired?"The MSI reported restart required or restart initiated (3010/1641). No toolkit restart was requested.":verified&&outcome==ActionOutcome.AppliedVerified?"The installer exited successfully and fresh installed-product identity and exact version match the approved profile.":"Installer exit: "+process.ExitCode+". "+(verified?"Expected product observed.":"Expected installed-product identity and version were not independently confirmed. Inspect before a new reviewed attempt.");
   return new(action.Id,outcome,actor,reason,new[]{after});
  }
  catch(Exception error) when(error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or OperationCanceledException)
  {return new(action.Id,ActionOutcome.OutcomeUnknown,actor,"Deployment may have started; supervision or durable result recording failed. The pending guard blocks replay. "+error.GetType().Name,[]);}
 }
 public async Task<DeploymentReconciliation> ReconcileAsync(DeviceContext context,JsonElement parameters,CancellationToken ct)
 {
  var input=Parameters(parameters);var profile=profiles[input.ProfileId];var guard=await pending.ReadAsync(DeploymentTarget.For(context,profile),ct);var inventory=await backend.ReadInventoryAsync(profile,context,ct);var status=await backend.InspectProcessAsync(guard?.Process,ct);
  return new(guard,inventory,status,"Read-only reconciliation never launches or retries an installer, deletes a pending guard, or infers detached child completion from a parent PID. Installed-state evidence and process identity must be reviewed separately.");
 }
 public async Task<Guid> ResolveReviewedAsync(DeviceContext context,JsonElement parameters,DeploymentResolutionReview review,IEvidenceStore trustedEvidence,CancellationToken ct)
 {
  var input=Parameters(parameters);var profile=profiles[input.ProfileId];var guard=await pending.ReadAsync(DeploymentTarget.For(context,profile),ct)??throw new PlanValidationException("There is no pending deployment to resolve.");
  var actor=await backend.GetActorAsync(ct);
  if(!actor.IsElevated||profile.Scope!=OperationScope.Machine||!review.ConfirmVendorCompletionAndNoPendingChildren)throw new PlanValidationException("Resolution requires the current authenticated device context, an elevated engineer and explicit review of vendor completion and pending children.");
  var process=await backend.InspectProcessAsync(guard.Process,ct);
  if(process is not (DeploymentProcessStatus.NotRunning or DeploymentProcessStatus.IdentityMismatch))throw new PlanValidationException("The original installer is running or cannot be identified. Its pending guard cannot be cleared by a confirmation checkbox.");
  var inventory=await backend.ReadInventoryAsync(profile,context,ct);
  if(!inventory.Available||!inventory.Present||inventory.ProductIdentity!=profile.ProductIdentity||inventory.Version!=profile.Version)throw new PlanValidationException("Resolution requires fresh exact installed-product identity and version. Absent or incomplete inventory does not prove a safe retry.");
  var resolution=new DeploymentResolution(Guid.NewGuid(),context,guard,inventory,process,actor,DateTimeOffset.UtcNow,true);
  await trustedEvidence.SaveNewAsync(EvidenceRecord.Create(resolution.Id,"deployment-resolution",context,resolution.ReviewedAt,StrictJson.Value(resolution)),ct);
  // Clearing the gate happens only after the separate explicit-review receipt is durable. This performs no device mutation.
  await pending.CompleteAsync(guard,ct);return resolution.Id;
 }
 // The supplied evidence store is composed by the application/worker, never selected by request parameters.
 public async Task AcknowledgeRecordedRunAsync(DeviceContext context,JsonElement parameters,Guid runRecordId,IEvidenceStore trustedEvidence,CancellationToken ct)
 {
  var input=Parameters(parameters);var profile=profiles[input.ProfileId];var guard=await pending.ReadAsync(DeploymentTarget.For(context,profile),ct);
  if(guard is null)return;
  if(guard.OriginalUserSid!=context.OriginalUserSid||guard.Process is not {End:DeploymentProcessEnd.Exited,MayDetach:false,ExitCode:not null})throw new PlanValidationException("An ambiguous or detached deployment cannot be acknowledged for replay.");
  var record=await trustedEvidence.LoadAsync(runRecordId,ct);
  if(record.Kind!="run"||record.Context.DeviceId!=context.DeviceId||record.Context.OriginalUserSid!=context.OriginalUserSid)throw new PlanValidationException("The durable run receipt belongs to another device or original user.");
  var run=StrictJson.Read<RunRecord>(System.Text.Encoding.UTF8.GetBytes(record.Data.GetRawText()));var result=run.Results.SingleOrDefault(r=>r.ActionId==guard.ActionId);
  if(run.Id!=runRecordId||!run.EvidenceComplete||run.FinishedAt is null||run.FinishedAt<guard.ClaimedAt||result?.Actor is null||result.Outcome is ActionOutcome.OutcomeUnknown or ActionOutcome.CancelledBeforeExecution)throw new PlanValidationException("No complete matching saved deployment result authorises releasing this guard.");
  var code=guard.Process.ExitCode.Value;
  bool consistent=code==0?result.Outcome is ActionOutcome.AppliedVerified or ActionOutcome.AppliedUnverified:code is 3010 or 1641?result.Outcome==ActionOutcome.RestartRequired:result.Outcome==ActionOutcome.Failed;
  if(!consistent)throw new PlanValidationException("The durable result conflicts with the installer observation.");
  await pending.CompleteAsync(guard,ct);
 }
 public sealed record DeploymentReviewState(string ProfileId,string InputDigest,DeploymentInventory Inventory);
}
