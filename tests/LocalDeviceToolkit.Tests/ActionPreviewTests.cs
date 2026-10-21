using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Settings;
using LocalDeviceToolkit.Engine.Workbench;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class ActionPreviewTests
{
 [Fact] public void Ordinary_explanations_do_not_expose_object_json()
 {
  var task=new TaskDefinition("windows.file-extensions","Show file extensions",["CFG-WIN-012"],["extensions.visible"],"settings.show-file-extensions");
  var request=new ActionRequest(task.Id,task.OperationId!,StrictJson.Value(new SettingParameters(true)),new("setup","windows"));
  var observed=new ProbeObservation("CFG-WIN-012","setting.ShowFileExtensions",ObservationState.Known,StrictJson.Value(new RegistryDwordState(true,1)),"fixture",DateTimeOffset.UtcNow,"");
  Assert.DoesNotContain("{",ActionPreviewFormatter.Describe(task,request,[observed]).Explanation);
 }

 [Theory][InlineData(true,0,false,null,PreviewStatus.Different,"Remove stored value")][InlineData(false,null,false,null,PreviewStatus.AlreadyCorrect,"Remove stored value")][InlineData(false,null,true,1,PreviewStatus.Missing,"Hidden")]
 public void Recorded_registry_restoration_uses_recorded_presence_and_value(bool exists,int? value,bool targetExists,int? targetValue,PreviewStatus status,string expected)
 {
  var snapshot=new {setting=SettingId.ShowFileExtensions,current=new RegistryDwordState(exists,value),target=new RegistryDwordState(targetExists,targetValue)};
  var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request("settings.restore-recorded"),[Observation(snapshot,"setting.restore.ShowFileExtensions")]);
  Assert.Equal(status,preview.Status);Assert.Contains(expected,preview.Expected);Assert.Contains("HideFileExt",preview.Source);Assert.Equal(status!=PreviewStatus.AlreadyCorrect,preview.NeedsChange);
 }
 [Fact] public void Recorded_power_restoration_shows_both_recorded_timeouts()
 {
  var plan=Guid.NewGuid();var snapshot=new {setting=SettingId.NoAcSleep,current=new AcSleepState(plan,0,600),target=new AcSleepState(plan,900,1200)};
  var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request("settings.restore-recorded"),[Observation(snapshot,"setting.restore.NoAcSleep")]);
  Assert.Equal(PreviewStatus.Different,preview.Status);Assert.Contains("15 minutes",preview.Expected);Assert.Contains("20 minutes",preview.Expected);Assert.Contains(plan.ToString(),preview.Expected);
 }
 [Fact] public void Recorded_time_zone_restoration_does_not_substitute_the_uk_standard()
 {
  var snapshot=new {setting=SettingId.UkTimeZone,current=new TimeZoneState("GMT Standard Time",false),target=new TimeZoneState("Pacific Standard Time",true)};
  var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request("settings.restore-recorded"),[Observation(snapshot,"setting.restore.UkTimeZone")]);
  Assert.Equal(PreviewStatus.Different,preview.Status);Assert.Contains("Pacific Standard Time",preview.Expected);Assert.Contains("Disabled",preview.Expected);Assert.DoesNotContain("United Kingdom",preview.Expected);
 }
 [Fact] public void Recorded_restoration_with_mismatched_setting_check_is_unreadable()
 {
  var snapshot=new {setting=SettingId.LongPaths,current=new RegistryDwordState(true,1),target=new RegistryDwordState(true,0)};
  Assert.Equal(PreviewStatus.Unreadable,ActionPreviewFormatter.Describe(TaskDefinition,Request("settings.restore-recorded"),[Observation(snapshot,"setting.restore.ShowFileExtensions")]).Status);
 }
 static readonly TaskDefinition TaskDefinition=new("windows.file-extensions","Show file extensions",["CFG-WIN-012"],["extensions.visible"],"settings.show-file-extensions");
 static ActionRequest Request(string operation="settings.show-file-extensions")=>new(TaskDefinition.Id,operation,StrictJson.Value(new SettingParameters(true)),new("setup","windows"));
 static ProbeObservation Observation(object value,string check="setting.ShowFileExtensions",ObservationState state=ObservationState.Known)=>new("CFG-WIN-012",check,state,StrictJson.Value(value),@"HKEY_USERS\S-1-5-21-123\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\HideFileExt",DateTimeOffset.UtcNow,"fixture reason");
 [Fact] public void Missing_is_not_an_effective_default()
 {
  var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request(),[Observation(new{exists=false,value=(int?)null})]);
  Assert.Equal(PreviewStatus.Missing,preview.Status);Assert.Contains("no stored",preview.Explanation,StringComparison.OrdinalIgnoreCase);Assert.True(preview.NeedsChange);
 }
 [Theory][InlineData(ObservationState.AccessDenied)][InlineData(ObservationState.Unavailable)][InlineData(ObservationState.NotSupported)]
 public void Failed_reads_are_unreadable(ObservationState state)=>Assert.Equal(PreviewStatus.Unreadable,ActionPreviewFormatter.Describe(TaskDefinition,Request(),[Observation(new{exists=false,value=(int?)null},state:state)]).Status);
 [Fact] public void Malformed_known_snapshot_is_unreadable()=>Assert.Equal(PreviewStatus.Unreadable,ActionPreviewFormatter.Describe(TaskDefinition,Request(),[Observation(new{exists=true})]).Status);
 [Fact] public void Unknown_numeric_value_keeps_raw_value_and_full_source()
 {
  var observation=Observation(new{exists=true,value=2});var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request(),[observation]);
  Assert.Equal(PreviewStatus.Different,preview.Status);Assert.Contains("2 (unrecognised value)",preview.Current);Assert.Contains(observation.Source,preview.Source);Assert.Contains("2",preview.Explanation);
 }
 [Fact] public void Already_correct_requires_no_write()
 {
  var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request(),[Observation(new{exists=true,value=0})]);
  Assert.Equal(PreviewStatus.AlreadyCorrect,preview.Status);Assert.False(preview.NeedsChange);Assert.Contains("Shown",preview.Current);
 }
 [Fact] public void Ac_power_values_show_battery_and_preserve_scheme()
 {
  var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request("settings.no-ac-sleep"),[Observation(new AcSleepState(Guid.NewGuid(),900,600),"setting.NoAcSleep")]);
  Assert.Equal(PreviewStatus.Different,preview.Status);Assert.Contains("15 minutes",preview.Current);Assert.Contains("10 minutes",preview.Current);Assert.Contains("Never",preview.Expected);Assert.Contains("Battery",preview.Expected);
 }
 [Fact] public void Uk_time_zone_has_friendly_name_and_preserved_dst()
 {
  var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request("settings.uk-timezone"),[Observation(new TimeZoneState("GMT Standard Time",false),"setting.UkTimeZone")]);
  Assert.Equal(PreviewStatus.AlreadyCorrect,preview.Status);Assert.Contains("United Kingdom",preview.Current);Assert.Contains("GMT Standard Time",preview.Explanation);Assert.Contains("daylight saving",preview.Current);
 }
 [Fact] public void Readiness_blocks_a_change_without_hiding_values()
 {
  var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request(),[Observation(new{exists=true,value=1})],new(false,OperationScope.CurrentUser,PrivilegeRequirement.CurrentUser,"Policy conflict"));
  Assert.Equal(PreviewStatus.Blocked,preview.Status);Assert.False(preview.NeedsChange);Assert.Contains("Policy conflict",preview.Explanation);Assert.Contains("Hidden",preview.Current);
 }
 [Fact] public void Readonly_task_without_probes_is_manual()=>Assert.Equal(PreviewStatus.Manual,ActionPreviewFormatter.Describe(TaskDefinition with {OperationId=null,CheckIds=[]},null,[]).Status);
 [Fact] public void Readonly_value_does_not_invent_target()=>Assert.Equal("See control guidance",ActionPreviewFormatter.Describe(TaskDefinition with {OperationId=null},null,[Observation(true,"extensions.visible")]).Expected);
 [Theory]
 [InlineData("settings.uk-formats","setting.UkFormats","{\"culture\":\"en-GB\"}")]
 [InlineData("settings.uk-home-country","setting.UkHomeCountry","{\"homeGeoId\":242}")]
 [InlineData("settings.uk-input-language","setting.UkInputLanguage","{\"languageTag\":\"en-GB\",\"present\":true}")]
 [InlineData("settings.uk-keyboard","setting.UkKeyboard","{\"languageTag\":\"en-GB\",\"inputMethodTips\":[\"0809:00000809\"]}")]
 [InlineData("settings.uk-default-keyboard","setting.UkDefaultKeyboard","{\"defaultInputMethodOverride\":\"0809:00000809\"}")]
 public void Each_atomic_uk_setting_compares_its_projected_value(string operation,string check,string json)
 {
  var observation=Observation(true,check) with {Value=System.Text.Json.JsonDocument.Parse(json).RootElement.Clone()};
  Assert.Equal(PreviewStatus.AlreadyCorrect,ActionPreviewFormatter.Describe(TaskDefinition,Request(operation),[observation]).Status);
 }
 [Fact] public void Absent_copilot_stays_already_correct_when_readiness_reports_no_change_needed()
 {
  var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request("apps.remove-consumer-copilot"),[Observation(new ConsumerCopilotState([]),"setting.RemoveConsumerCopilot")],new(false,OperationScope.CurrentUser,PrivilegeRequirement.CurrentUser,"No change needed"));
  Assert.Equal(PreviewStatus.AlreadyCorrect,preview.Status);Assert.False(preview.NeedsChange);
 }
 [Fact] public void Copilot_identity_must_be_exact()
 {
  var wrong=new ConsumerCopilotState([new("Other_1.0.0.0_x64__8wekyb3d8bbwe","Other_8wekyb3d8bbwe","8wekyb3d8bbwe","1.0.0.0")]);
  Assert.Equal(PreviewStatus.Unreadable,ActionPreviewFormatter.Describe(TaskDefinition,Request("apps.remove-consumer-copilot"),[Observation(wrong,"setting.RemoveConsumerCopilot")]).Status);
 }
 [Fact] public void Stored_registry_paths_are_visible_even_for_generic_probe_sources()
 {
  var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request("settings.long-paths"),[Observation(new{exists=true,value=0},"setting.LongPaths") with {Source="Registry read"}]);
  Assert.Contains(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled",preview.Source);Assert.Contains("Registry read",preview.Source);
 }

 [Fact] public void Present_exact_copilot_package_needs_removal()
 {
  var state=new ConsumerCopilotState([new("Microsoft.Copilot_1.0.0.0_x64__8wekyb3d8bbwe","Microsoft.Copilot_8wekyb3d8bbwe","8wekyb3d8bbwe","1.0.0.0")]);
  Assert.Equal(PreviewStatus.Different,ActionPreviewFormatter.Describe(TaskDefinition,Request("apps.remove-consumer-copilot"),[Observation(state,"setting.RemoveConsumerCopilot")]).Status);
 }
 [Theory][InlineData(false,false,PreviewStatus.Unreadable)][InlineData(true,false,PreviewStatus.Manual)][InlineData(true,true,PreviewStatus.Manual)]
 public void Package_inventory_never_invents_approved_version(bool available,bool present,PreviewStatus status)
 {
  var state=new LocalDeviceToolkit.Engine.Deployments.DeploymentInventory(available,present,"Actual.Product","9.1","Exact inventory reason");
  var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request("packages.install"),[Observation(state,"deployment.installed-product")]);
  Assert.Equal(status,preview.Status);Assert.Equal("Approved version",preview.Expected);if(present)Assert.Contains("9.1",preview.Current);
 }

 [Theory]
 [InlineData("2.0",true,true,PreviewStatus.AlreadyCorrect)]
 [InlineData("1.0",true,true,PreviewStatus.Different)]
 [InlineData("2.0",true,false,PreviewStatus.Different)]
 [InlineData(null,false,true,PreviewStatus.Missing)]
 public void Package_comparison_uses_only_captured_approved_identity_and_version(string? installedVersion,bool present,bool identityMatches,PreviewStatus status)
 {
  var inventory=new LocalDeviceToolkit.Engine.Deployments.DeploymentInventory(true,present,identityMatches?"Approved.Product":"Other.Product",installedVersion,"inventory");
  var snapshot=new {profileId="approved-profile",inputDigest="digest",inventory,expectedVersion="2.0",productName="Approved application",productIdentity="Approved.Product"};
  var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request("packages.install"),[Observation(snapshot,"deployment.review-state")]);
  Assert.Equal(status,preview.Status);Assert.Contains("Approved application",preview.Expected);Assert.Contains("2.0",preview.Expected);Assert.Equal(status is PreviewStatus.Different or PreviewStatus.Missing,preview.NeedsChange);
 }

 [Fact] public void Machine_time_zone_does_not_claim_administrator_when_user_holds_the_privilege()
 {
  var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request("settings.uk-timezone"),[Observation(new TimeZoneState("GMT Standard Time",false),"setting.UkTimeZone")],new(true,OperationScope.Machine,PrivilegeRequirement.CurrentUser,"Privilege assigned"));
  Assert.Equal("Current user (device setting)",preview.Account);
 }

 [Fact] public void Undefined_known_json_is_unreadable()
 {
  var observation=Observation(true) with {Value=default(System.Text.Json.JsonElement)};
  Assert.Equal(PreviewStatus.Unreadable,ActionPreviewFormatter.Describe(TaskDefinition,Request(),[observation]).Status);
 }

 [Fact] public void Other_known_country_shows_its_name_and_raw_geography_id()
 {
  var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request("settings.uk-home-country"),[Observation(new{homeGeoId=244},"setting.UkHomeCountry")]);
  Assert.Contains("United States",preview.Current);Assert.Contains("244",preview.Current);Assert.Equal(PreviewStatus.Different,preview.Status);
 }
 [Fact] public void Other_keyboard_id_shows_input_language_without_guessing_keyboard_layout()
 {
  var preview=ActionPreviewFormatter.Describe(TaskDefinition,Request("settings.uk-keyboard"),[Observation(new{languageTag="en-GB",inputMethodTips=new[]{"0409:00000409"}},"setting.UkKeyboard")]);
  Assert.Contains("English (United States)",preview.Current);Assert.Contains("0409:00000409",preview.Current);Assert.Contains("input method ID",preview.Current);Assert.Equal(PreviewStatus.Different,preview.Status);
 }

 [Theory]
 [InlineData("secureboot.enabled","Enabled")][InlineData("tpm.ready","Ready")][InlineData("bitlocker.protected","Protected")][InlineData("codeintegrity.enforced","Enforced")][InlineData("antivirus.active","Active")]
 public void Atomic_readonly_health_values_have_closed_expectations(string check,string expected)
 {
  var task=TaskDefinition with {OperationId=null,CheckIds=[check]};
  var correct=ActionPreviewFormatter.Describe(task,null,[Observation(true,check)]);var different=ActionPreviewFormatter.Describe(task,null,[Observation(false,check)]);
  Assert.Equal(expected,correct.Expected);Assert.Equal(PreviewStatus.AlreadyCorrect,correct.Status);Assert.Equal(PreviewStatus.Different,different.Status);Assert.False(different.NeedsChange);
 }
 [Theory][InlineData("10.0.26200.0",PreviewStatus.AlreadyCorrect)][InlineData("10.0.22631.0",PreviewStatus.Different)]
 public void Readonly_os_uses_the_existing_catalogue_version_floor(string version,PreviewStatus status)
 {
  var task=TaskDefinition with {OperationId=null,CheckIds=["os.version"]};var preview=ActionPreviewFormatter.Describe(task,null,[Observation(version,"os.version")]);
  Assert.Equal(status,preview.Status);Assert.Contains("26200",preview.Expected);Assert.Contains(version,preview.Current);Assert.False(preview.NeedsChange);
 }
 [Fact] public void Readonly_inventory_summarises_rows_without_raw_json_in_ordinary_details()
 {
  var inventory=new[]{new InstalledApplication("App.Identity","Friendly app","1.0","Publisher",OperationScope.Machine)};
  var task=TaskDefinition with {OperationId=null,CheckIds=["apps.inventory"]};var preview=ActionPreviewFormatter.Describe(task,null,[Observation(inventory,"apps.inventory")]);
  Assert.Equal("1 applications found",preview.Current);Assert.Equal(PreviewStatus.Manual,preview.Status);Assert.DoesNotContain("{",preview.Explanation);Assert.DoesNotContain("{",preview.Current);
 }
 [Fact] public void Readonly_generic_objects_offer_details_without_raw_json_in_current()
 {
  var task=TaskDefinition with {OperationId=null,CheckIds=["unknown.inventory"]};var preview=ActionPreviewFormatter.Describe(task,null,[Observation(new{detail="exact fact"},"unknown.inventory")]);
  Assert.Equal("Details available",preview.Current);Assert.DoesNotContain("{",preview.Explanation);Assert.Equal(PreviewStatus.Manual,preview.Status);
 }
 [Fact] public void Readonly_check_arrays_do_not_report_a_false_pass()
 {
  var task=TaskDefinition with {OperationId=null,CheckIds=["secureboot.enabled","tpm.ready"]};var preview=ActionPreviewFormatter.Describe(task,null,[Observation(true,"secureboot.enabled"),Observation(true,"tpm.ready")]);
  Assert.Equal(PreviewStatus.Manual,preview.Status);Assert.False(preview.NeedsChange);
 }

}
