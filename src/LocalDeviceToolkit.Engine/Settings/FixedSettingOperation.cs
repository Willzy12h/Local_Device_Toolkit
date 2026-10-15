using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using System.Text.Json;
namespace LocalDeviceToolkit.Engine.Settings;
public enum SettingId { ShowFileExtensions,LongPaths,DisableFastStartup,NoAcSleep }
public sealed record SettingParameters(bool ConfirmManagedPolicyReview);
public sealed record RegistryDwordState(bool Exists,int? Value);
public sealed record AcSleepState(Guid ActiveScheme,uint Seconds);
public sealed record SettingSnapshot(SettingId Setting,JsonElement Value,string Source,DateTimeOffset CapturedAt);
public sealed record SettingPolicy(bool Available,bool ConflictObserved,string Reason);
public sealed record SettingDefinition(SettingId Setting,string OperationId,string ControlId,OperationScope Scope);
public interface ISettingsBackend
{
 Task<ExecutionActor> GetActorAsync(CancellationToken ct);
 Task<SettingPolicy> InspectPolicyAsync(SettingId setting,CancellationToken ct);
 Task<SettingSnapshot> ReadAsync(SettingId setting,DeviceContext context,CancellationToken ct);
 Task ApplyAsync(SettingId setting,DeviceContext context,SettingSnapshot expectedBefore,CancellationToken ct);
}
public sealed class FixedSettingOperation(SettingDefinition definition,ISettingsBackend backend):ILocalOperation
{
 public static IReadOnlyList<SettingDefinition> Definitions {get;}=Array.AsReadOnly(new SettingDefinition[]{new(SettingId.ShowFileExtensions,"settings.show-file-extensions","CFG-WIN-012",OperationScope.CurrentUser),new(SettingId.LongPaths,"settings.long-paths","CFG-WIN-007",OperationScope.Machine),new(SettingId.DisableFastStartup,"settings.disable-fast-startup","CFG-WIN-019",OperationScope.Machine),new(SettingId.NoAcSleep,"settings.no-ac-sleep","CFG-WIN-020",OperationScope.Machine)});
 public string Id=>definition.OperationId;
 public void ValidateParameters(JsonElement parameters)
 {
  try{_=StrictJson.Read<SettingParameters>(System.Text.Encoding.UTF8.GetBytes(parameters.GetRawText()),4096);}
  catch(Exception error) when(error is JsonException or InvalidOperationException or InvalidDataException){throw new PlanValidationException("This setting accepts only a managed-policy review confirmation; paths, commands and unknown fields are refused.");}
 }
 SettingParameters Parameters(JsonElement value){ValidateParameters(value);return StrictJson.Read<SettingParameters>(System.Text.Encoding.UTF8.GetBytes(value.GetRawText()),4096);}
 public string GetTargetKey(DeviceContext context,JsonElement parameters)=>(definition.Scope==OperationScope.CurrentUser?context.OriginalUserSid:context.DeviceId)+"/"+definition.Setting;
 public async Task<ActionReadiness> InspectAsync(DeviceContext context,JsonElement parameters,CancellationToken ct)
 {
  ActionReadiness Result(bool ready,string reason)=>new(ready,definition.Scope,definition.Scope==OperationScope.CurrentUser?PrivilegeRequirement.CurrentUser:PrivilegeRequirement.Administrator,reason);
  if(!Parameters(parameters).ConfirmManagedPolicyReview)return Result(false,"Review effective GPO/MDM policy and confirm this local change is appropriate before proceeding.");
  var actor=await backend.GetActorAsync(ct);
  if(definition.Scope==OperationScope.CurrentUser&&actor.Sid!=context.OriginalUserSid)return Result(false,"Current-user changes must execute as the original engineer, never an alternate administrator.");
  var policy=await backend.InspectPolicyAsync(definition.Setting,ct);
  if(!policy.Available||policy.ConflictObserved)return Result(false,policy.Reason);
  return Result(true,"No conflict observed in the checked policy sources. Engineer confirmed the required external policy review; this does not prove the device is unmanaged.");
 }
 string CheckId=>"setting."+definition.Setting;
 ProbeObservation Observation(SettingSnapshot snapshot)
 {
  if(snapshot.Setting!=definition.Setting||snapshot.Source is null||snapshot.Source.Length is <1 or >512||snapshot.CapturedAt.Offset!=TimeSpan.Zero)throw new PlanValidationException("Invalid local setting evidence.");
  ValidateSnapshot(snapshot);
  return new(definition.ControlId,CheckId,ObservationState.Known,snapshot.Value.Clone(),snapshot.Source,snapshot.CapturedAt,"Exact local setting state; missing values are recorded as absent, not defaulted.");
 }
 static void ValidateSnapshot(SettingSnapshot snapshot)
 {
  if(snapshot.Setting==SettingId.NoAcSleep)
  {var state=StrictJson.Read<AcSleepState>(System.Text.Encoding.UTF8.GetBytes(snapshot.Value.GetRawText()),4096);if(state.ActiveScheme==Guid.Empty)throw new PlanValidationException("Power plan identity is missing.");}
  else
  {var state=StrictJson.Read<RegistryDwordState>(System.Text.Encoding.UTF8.GetBytes(snapshot.Value.GetRawText()),4096);if(state.Exists!=state.Value.HasValue)throw new PlanValidationException("Registry setting evidence is inconsistent.");}
 }
 public async Task<IReadOnlyList<ProbeObservation>> CaptureBeforeAsync(DeviceContext context,JsonElement parameters,CancellationToken ct)
 {ValidateParameters(parameters);return new[]{Observation(await backend.ReadAsync(definition.Setting,context,ct))};}
 public async Task<ActionResult> ExecuteAsync(DeviceContext context,PlannedAction action,CancellationToken ct)
 {
  var readiness=await InspectAsync(context,action.Request.Parameters,ct);
  if(!readiness.CanExecute||readiness.Scope!=action.Readiness.Scope||readiness.Privilege!=action.Readiness.Privilege)return new(action.Id,ActionOutcome.Failed,null,"Not started: prerequisites or execution identity changed. "+readiness.Reason,[]);
  if(readiness.Privilege==PrivilegeRequirement.Administrator&&!(await backend.GetActorAsync(ct)).IsElevated)return new(action.Id,ActionOutcome.Failed,null,"Not started: the action requires an authenticated elevated execution token.",[]);
  var before=await backend.ReadAsync(definition.Setting,context,ct);var observation=Observation(before);
  if(action.Before.Count!=1||action.Before[0].CheckId!=CheckId||ObservationFingerprint.Of(new[]{observation})!=action.BeforeDigest)return new(action.Id,ActionOutcome.Failed,null,"Not started: the setting changed after review.",[]);
  ct.ThrowIfCancellationRequested();await backend.ApplyAsync(definition.Setting,context,before,ct);
  var after=await backend.ReadAsync(definition.Setting,context,CancellationToken.None);var afterObservation=Observation(after);var actor=await backend.GetActorAsync(CancellationToken.None);
  var desired=definition.Setting==SettingId.NoAcSleep?StrictJson.Read<AcSleepState>(System.Text.Encoding.UTF8.GetBytes(after.Value.GetRawText())) is {Seconds:0} power&&power.ActiveScheme==StrictJson.Read<AcSleepState>(System.Text.Encoding.UTF8.GetBytes(before.Value.GetRawText())).ActiveScheme:
   StrictJson.Read<RegistryDwordState>(System.Text.Encoding.UTF8.GetBytes(after.Value.GetRawText())) is {Exists:true,Value:int value}&&value==(definition.Setting==SettingId.LongPaths?1:0);
  return new(action.Id,desired?ActionOutcome.AppliedVerified:ActionOutcome.AppliedUnverified,actor,desired?"The exact local setting was read back with the reviewed value. Complete the control's manual and application checks.":"The desired setting was not confirmed by independent readback. Inspect before retrying.",[afterObservation]);
 }
}
