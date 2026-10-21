using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Execution;
using System.Text.Json;
using LocalDeviceToolkit.Engine.Planning;
namespace LocalDeviceToolkit.Engine.Settings;
public enum UserSettingId { UkRegionKeyboard,RemoveConsumerCopilot,UkFormats,UkHomeCountry,UkInputLanguage,UkKeyboard,UkDefaultKeyboard }
public sealed record UserLanguageState(string LanguageTag,string[] InputMethodTips,bool Handwriting,bool Spellchecking);
public sealed record RegionState(string Culture,int HomeGeoId,string? DefaultInputMethodOverride,UserLanguageState[] Languages);
public sealed record ConsumerPackage(string PackageFullName,string PackageFamilyName,string PublisherId,string Version);
public sealed record ConsumerCopilotState(ConsumerPackage[] Packages);
public sealed record UserSettingSnapshot(UserSettingId Setting,JsonElement Value,string Source,DateTimeOffset CapturedAt);
public interface IUserSettingsBackend
{
 Task<ExecutionActor> GetActorAsync(CancellationToken ct);
 Task<SettingPolicy> InspectPolicyAsync(UserSettingId setting,CancellationToken ct);
 Task<UserSettingSnapshot> ReadAsync(UserSettingId setting,DeviceContext original,CancellationToken ct);
 Task ApplyAsync(UserSettingId setting,DeviceContext original,UserSettingSnapshot before,CancellationToken ct);
}
public sealed class UserSettingOperation(UserSettingId setting,IUserSettingsBackend backend):ILocalOperation
{
 public string Id=>setting switch {UserSettingId.UkRegionKeyboard=>"settings.uk-region-keyboard",UserSettingId.RemoveConsumerCopilot=>"apps.remove-consumer-copilot",UserSettingId.UkFormats=>"settings.uk-formats",UserSettingId.UkHomeCountry=>"settings.uk-home-country",UserSettingId.UkInputLanguage=>"settings.uk-input-language",UserSettingId.UkKeyboard=>"settings.uk-keyboard",UserSettingId.UkDefaultKeyboard=>"settings.uk-default-keyboard",_=>throw new PlanValidationException("Unknown current-user setting.")};
 string ControlId=>setting==UserSettingId.RemoveConsumerCopilot?"CFG-WIN-021":"CFG-WIN-010";
 string CheckId=>"setting."+setting;
 public void ValidateParameters(JsonElement p)
 {
  try{_=StrictJson.Read<SettingParameters>(System.Text.Encoding.UTF8.GetBytes(p.GetRawText()),4096);}
  catch(Exception error) when(error is JsonException or InvalidOperationException or InvalidDataException){throw new PlanValidationException("Only the typed policy-review confirmation is accepted for this current-user action.");}
 }
 static T Read<T>(JsonElement value)=>StrictJson.Read<T>(System.Text.Encoding.UTF8.GetBytes(value.GetRawText()),64*1024);
 static bool Text(string? value,int maximum)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=maximum&&!value.Any(char.IsControl);
 public string GetTargetKey(DeviceContext c,JsonElement p)=>c.OriginalUserSid+"/"+setting;
 public async Task<ActionReadiness> InspectAsync(DeviceContext c,JsonElement p,CancellationToken ct)
 {
  ValidateParameters(p);ActionReadiness Result(bool ready,string reason)=>new(ready,OperationScope.CurrentUser,PrivilegeRequirement.CurrentUser,reason);
  if(!Read<SettingParameters>(p).ConfirmManagedPolicyReview)return Result(false,"Confirm the required GPO/MDM review before making this change.");
  var actor=await backend.GetActorAsync(ct);if(actor.Sid!=c.OriginalUserSid)return Result(false,"This current-user action must run as the original engineer; alternate administrator profiles are refused.");
  var policy=await backend.InspectPolicyAsync(setting,ct);if(!policy.Available||policy.ConflictObserved)return Result(false,policy.Reason);
  if(setting==UserSettingId.RemoveConsumerCopilot)
  {var snapshot=await backend.ReadAsync(setting,c,ct);_=Observe(snapshot);if(Read<ConsumerCopilotState>(snapshot.Value).Packages.Length==0)return Result(false,"No change needed: the exact consumer Copilot app is already absent for the original engineer.");}
  return Result(true,"Original engineer only. No conflict observed in the checked sources; external policy review was confirmed.");
 }
 ProbeObservation Observe(UserSettingSnapshot snapshot)
 {
  if(snapshot.Setting!=setting||!Text(snapshot.Source,512)||snapshot.CapturedAt.Offset!=TimeSpan.Zero)throw new PlanValidationException("Invalid original-user setting evidence.");
  if(setting!=UserSettingId.RemoveConsumerCopilot)ValidateRegion(Read<RegionState>(snapshot.Value));else ValidateConsumerIdentity(Read<ConsumerCopilotState>(snapshot.Value));
  return new(ControlId,CheckId,ObservationState.Known,Project(snapshot.Value),snapshot.Source,snapshot.CapturedAt,"Exact original-user settings; other users and Microsoft 365 Copilot are not targets.");
 }
 public async Task<IReadOnlyList<ProbeObservation>> CaptureBeforeAsync(DeviceContext c,JsonElement p,CancellationToken ct)
 {ValidateParameters(p);if((await backend.GetActorAsync(ct)).Sid!=c.OriginalUserSid)throw new PlanValidationException("Original-user identity changed.");return new[]{Observe(await backend.ReadAsync(setting,c,ct))};}
 public async Task<ActionResult> ExecuteAsync(DeviceContext c,PlannedAction action,CancellationToken ct)
 {
  var readiness=await InspectAsync(c,action.Request.Parameters,ct);var actor=await backend.GetActorAsync(ct);
  if(!readiness.CanExecute||action.Request.OperationId!=Id||action.Readiness.Scope!=OperationScope.CurrentUser||action.Readiness.Privilege!=PrivilegeRequirement.CurrentUser)return new(action.Id,ActionOutcome.Failed,null,"Not started: current-user prerequisites or scope changed. "+readiness.Reason,[]);
  var before=await backend.ReadAsync(setting,c,ct);var observed=Observe(before);
  if(action.Before.Count!=1||action.Before[0].CheckId!=CheckId||ObservationFingerprint.Of(new[]{observed})!=action.BeforeDigest)return new(action.Id,ActionOutcome.Failed,null,"Not started: current-user settings changed after review.",[]);
  if(setting is UserSettingId.UkKeyboard or UserSettingId.UkDefaultKeyboard)
  {
   var uk=Read<RegionState>(before.Value).Languages.SingleOrDefault(l=>l.LanguageTag.Equals("en-GB",StringComparison.OrdinalIgnoreCase));
   if(uk is null||setting==UserSettingId.UkDefaultKeyboard&&!uk.InputMethodTips.Contains("0809:00000809",StringComparer.OrdinalIgnoreCase))return new(action.Id,ActionOutcome.Failed,null,"Not started: prerequisite missing. "+(setting==UserSettingId.UkKeyboard?"Add the UK input language first.":"Add the UK keyboard first."),[]);
  }
  ct.ThrowIfCancellationRequested();
  try
  {
   await backend.ApplyAsync(setting,c,before,ct);var after=await backend.ReadAsync(setting,c,CancellationToken.None);var observation=Observe(after);
   var verified=setting==UserSettingId.RemoveConsumerCopilot?Read<ConsumerCopilotState>(after.Value).Packages.Length==0:setting==UserSettingId.UkRegionKeyboard?RegionVerified(Read<RegionState>(before.Value),Read<RegionState>(after.Value)):AtomicRegionVerified(setting,Read<RegionState>(before.Value),Read<RegionState>(after.Value));
   return new(action.Id,verified?ActionOutcome.AppliedVerified:ActionOutcome.AppliedUnverified,actor,verified?"Current-user readback confirmed the requested change and required preserved values. Complete manual verification; a new session may be needed for regional UI changes.":"Current-user readback did not confirm every requested/preserved value. Inspect before retrying.",[observation]);
  }
  catch(UserChangeUncertainException error){return new(action.Id,ActionOutcome.OutcomeUnknown,actor,error.Message,[new(ControlId,"process.user-setting",ObservationState.Known,error.ProcessEvidence.Clone(),"Owned PowerShell process observation",DateTimeOffset.UtcNow,"Process metadata is not proof of the setting outcome.")]);}
  catch(Exception){return new(action.Id,ActionOutcome.OutcomeUnknown,actor,"Current-user execution/readback was interrupted or unavailable. State may be partial; reconcile before another attempt.",[]);}
 }
 // Reviewed evidence covers only the selected target. Fresh full snapshots still guard the
 // script's write and verify every preserved preference, allowing ordered selected actions.
 JsonElement Project(JsonElement value)
 {
  if(setting is UserSettingId.RemoveConsumerCopilot or UserSettingId.UkRegionKeyboard)return value.Clone();
  var region=Read<RegionState>(value);var uk=region.Languages.SingleOrDefault(l=>l.LanguageTag.Equals("en-GB",StringComparison.OrdinalIgnoreCase));
  return setting switch {
   UserSettingId.UkFormats=>StrictJson.Value(new {region.Culture}),
   UserSettingId.UkHomeCountry=>StrictJson.Value(new {region.HomeGeoId}),
   UserSettingId.UkInputLanguage=>StrictJson.Value(new {LanguageTag="en-GB",Present=uk is not null}),
   UserSettingId.UkKeyboard=>StrictJson.Value(new {LanguageTag="en-GB",InputMethodTips=uk?.InputMethodTips??[]}),
   UserSettingId.UkDefaultKeyboard=>StrictJson.Value(new {region.DefaultInputMethodOverride}),
   _=>throw new PlanValidationException("Unknown region target.")};
 }
 public static bool AtomicRegionVerified(UserSettingId target,RegionState before,RegionState after)
 {
  ValidateRegion(before);ValidateRegion(after);
  var uk=before.Languages.SingleOrDefault(l=>l.LanguageTag.Equals("en-GB",StringComparison.OrdinalIgnoreCase));
  var expected=target switch {
   UserSettingId.UkFormats=>before with {Culture="en-GB"},
   UserSettingId.UkHomeCountry=>before with {HomeGeoId=242},
   UserSettingId.UkInputLanguage=>uk is null?before with {Languages=[..before.Languages,new("en-GB",[],false,true)]}:before,
   UserSettingId.UkKeyboard when uk is not null=>before with {Languages=before.Languages.Select(l=>l==uk?l with {InputMethodTips=l.InputMethodTips.Contains("0809:00000809",StringComparer.OrdinalIgnoreCase)?l.InputMethodTips:[..l.InputMethodTips,"0809:00000809"]}:l).ToArray()},
   UserSettingId.UkDefaultKeyboard when uk?.InputMethodTips.Contains("0809:00000809",StringComparer.OrdinalIgnoreCase)==true=>before with {DefaultInputMethodOverride="0809:00000809"},
   _=>null};
  // Windows chooses preferences for a new language; only its empty keyboard list is fixed.
  if(target==UserSettingId.UkInputLanguage&&uk is null&&after.Languages.Length==before.Languages.Length+1)
   expected=expected! with {Languages=[..before.Languages,after.Languages[^1] with {LanguageTag="en-GB",InputMethodTips=[]}]};
  return expected is not null&&StrictJson.Value(expected).GetRawText()==StrictJson.Value(after).GetRawText();
 }
 static void ValidateRegion(RegionState state)
 {
  if(state is null||!Text(state.Culture,64)||state.HomeGeoId<0||state.DefaultInputMethodOverride is not null&&!Text(state.DefaultInputMethodOverride,256)||state.Languages is null||state.Languages.Length is <1 or >128||state.Languages.Any(l=>l is null||!Text(l.LanguageTag,64)||l.InputMethodTips is null||l.InputMethodTips.Length>128||l.InputMethodTips.Any(t=>!Text(t,256)))||state.Languages.Select(l=>l.LanguageTag).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=state.Languages.Length)throw new PlanValidationException("Invalid language/preferences snapshot.");
 }
 public static bool RegionVerified(RegionState before,RegionState after)
 {
  ValidateRegion(before);ValidateRegion(after);
  if(!after.Culture.Equals("en-GB",StringComparison.OrdinalIgnoreCase)||after.HomeGeoId!=242||after.DefaultInputMethodOverride!="0809:00000809")return false;
  var uk=after.Languages.SingleOrDefault(l=>l.LanguageTag.Equals("en-GB",StringComparison.OrdinalIgnoreCase));if(uk is null||!uk.InputMethodTips.Contains("0809:00000809"))return false;
  if(after.Languages.Length!=before.Languages.Length+(before.Languages.Any(l=>l.LanguageTag.Equals("en-GB",StringComparison.OrdinalIgnoreCase))?0:1))return false;
  for(int index=0;index<before.Languages.Length;index++)
  {
   var original=before.Languages[index];var actual=after.Languages[index];
   if(!original.LanguageTag.Equals(actual.LanguageTag,StringComparison.OrdinalIgnoreCase)||original.Handwriting!=actual.Handwriting||original.Spellchecking!=actual.Spellchecking||original.InputMethodTips.Except(actual.InputMethodTips,StringComparer.OrdinalIgnoreCase).Any()||actual.InputMethodTips.Except(original.InputMethodTips,StringComparer.OrdinalIgnoreCase).Any(t=>!original.LanguageTag.Equals("en-GB",StringComparison.OrdinalIgnoreCase)||t!="0809:00000809"))return false;
  }
  return true;
 }
 public static void ValidateConsumerIdentity(ConsumerCopilotState state)
 {
  if(state is null||state.Packages is null||state.Packages.Length>16||state.Packages.Any(p=>p is null||p.PackageFamilyName!="Microsoft.Copilot_8wekyb3d8bbwe"||p.PublisherId!="8wekyb3d8bbwe"||!Text(p.PackageFullName,512)||!p.PackageFullName.StartsWith("Microsoft.Copilot_",StringComparison.Ordinal)||!p.PackageFullName.EndsWith("_8wekyb3d8bbwe",StringComparison.Ordinal)||p.PackageFullName.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ('_' or '.' or '-' or '~'))||!Version.TryParse(p.Version,out _))||state.Packages.Select(p=>p.PackageFullName).Distinct(StringComparer.Ordinal).Count()!=state.Packages.Length)throw new PlanValidationException("Only the exact Microsoft consumer Copilot package identity is allowed. Microsoft 365/Office and other publishers are refused.");
 }
}

public sealed class UserChangeUncertainException(string reason,JsonElement processEvidence):IOException(reason){public JsonElement ProcessEvidence{get;}=processEvidence.Clone();}
