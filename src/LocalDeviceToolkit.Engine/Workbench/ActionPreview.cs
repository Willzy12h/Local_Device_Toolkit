using System.Globalization;
using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Engine.Settings;
namespace LocalDeviceToolkit.Engine.Workbench;

public enum PreviewStatus { NotChecked,AlreadyCorrect,Different,Missing,Unreadable,Blocked,Manual }
public sealed record ActionPreview(string Title,string Current,string Expected,PreviewStatus Status,string Explanation,string Source,string Account,bool NeedsChange);

/// <summary>Formats captured evidence without reading the device or authorising a write.</summary>
public static class ActionPreviewFormatter
{
 public static string StatusLabel(PreviewStatus status)=>status switch
 {
  PreviewStatus.NotChecked=>"Not checked",PreviewStatus.AlreadyCorrect=>"Already correct",PreviewStatus.Different=>"Different",PreviewStatus.Missing=>"Missing",PreviewStatus.Unreadable=>"Unreadable",PreviewStatus.Blocked=>"Blocked",PreviewStatus.Manual=>"Manual verification",_=>throw new ArgumentOutOfRangeException(nameof(status))
 };
 public static ActionPreview Describe(TaskDefinition task,ActionRequest? request,IReadOnlyList<ProbeObservation> observations,ActionReadiness? readiness=null)
 {
  var operation=request?.OperationId??task.OperationId;
  var definition=FixedSettingOperation.Definitions.SingleOrDefault(d=>d.OperationId==operation);
  string account=readiness is not null
   ?readiness.Privilege==PrivilegeRequirement.Administrator?"Administrator":readiness.Scope==OperationScope.Machine?"Current user (device setting)":"Original signed-in user"
   :definition?.Scope==OperationScope.Machine?"Device setting · privilege checked at review":operation is "packages.install"?"Approved profile account":operation is null?"Read only":"Original signed-in user";
  string expected=Expected(operation);
  ActionPreview Result(string current,PreviewStatus status,string explanation,string source="")
  {
   if(readiness is {CanExecute:false}&&status is PreviewStatus.Different or PreviewStatus.Missing or PreviewStatus.NotChecked or PreviewStatus.Manual)
   {status=PreviewStatus.Blocked;explanation+=" "+readiness.Reason;}
   if(status==PreviewStatus.AlreadyCorrect)explanation+=" Already correct; no write is needed.";
   return new(task.Title,current,expected,status,explanation,source,account,request is not null&&(status is PreviewStatus.Different or PreviewStatus.Missing));
  }
  if(request is null)
  {
   if(operation is null)expected="See control guidance";
   var relevant=observations.Where(o=>task.ControlIds.Contains(o.ControlId)&&task.CheckIds.Contains(o.CheckId)).ToArray();
   if(relevant.Length==0)return Result("Not checked",task.CheckIds.Length==0?PreviewStatus.Manual:PreviewStatus.NotChecked,"Run the registered checks and follow the control's manual guidance.");
   string source=string.Join(Environment.NewLine,relevant.Select(o=>o.Source).Distinct());
   var failed=relevant.FirstOrDefault(o=>o.State is not (ObservationState.Known or ObservationState.Manual)||o.State==ObservationState.Known&&(o.Value is null||o.Value.Value.ValueKind==JsonValueKind.Undefined||ProbeValues.IsRegistered(o.CheckId)&&!ProbeValues.Accepts(o.CheckId,o.Value.Value)));
   if(failed is not null)return Result("Unavailable",PreviewStatus.Unreadable,"The captured check is unavailable or malformed. "+failed.Reason,source);
   if(relevant.GroupBy(o=>o.CheckId).Any(group=>group.Count()>1))return Result("Ambiguous captured values",PreviewStatus.Unreadable,"More than one observation was supplied for the same check.",source);
   string details=string.Join(Environment.NewLine,relevant.Select(o=>o.CheckId+": "+o.Reason));
   if(task.CheckIds.Length==1&&relevant.Length==1&&relevant[0] is {State:ObservationState.Known,Value:JsonElement single})
   {
    string id=relevant[0].CheckId;
    if(ReadOnlyExpected(id) is string knownExpected)
    {
     expected=knownExpected;
     bool match=id=="os.version"?Version.Parse(single.GetString()!)>=new Version(10,0,26200):single.GetBoolean();
     return Result(ReadOnlyCurrent(id,single),match?PreviewStatus.AlreadyCorrect:PreviewStatus.Different,details,source);
    }
   }
   return Result(string.Join(Environment.NewLine,relevant.Select(o=>(relevant.Length>1?o.CheckId+": ":"")+(o.Value is JsonElement v?ReadOnlyCurrent(o.CheckId,v):o.Reason))),PreviewStatus.Manual,details,source);
  }
  var check=definition is not null?"setting."+definition.Setting:operation switch
  {
   "settings.uk-formats"=>"setting.UkFormats","settings.uk-home-country"=>"setting.UkHomeCountry","settings.uk-input-language"=>"setting.UkInputLanguage","settings.uk-keyboard"=>"setting.UkKeyboard","settings.uk-default-keyboard"=>"setting.UkDefaultKeyboard","apps.remove-consumer-copilot"=>"setting.RemoveConsumerCopilot","packages.install"=>"deployment.review-state",_=>null
  };
  var observation=operation=="settings.restore-recorded"?observations.LastOrDefault(o=>o.CheckId.StartsWith("setting.restore.",StringComparison.Ordinal)):observations.LastOrDefault(o=>o.CheckId==check);
  if(operation=="packages.install")observation??=observations.LastOrDefault(o=>o.CheckId=="deployment.installed-product");
  if(observation is null)return Result("Not checked",PreviewStatus.NotChecked,"No captured operation snapshot is available.");
  observation=observation with {Source=RegistrySource(operation,observation.Source)};
  if(observation.State!=ObservationState.Known)return Result("Unavailable",observation.State==ObservationState.Manual?PreviewStatus.Manual:PreviewStatus.Unreadable,observation.Reason,observation.Source);
  if(observation.Value is not JsonElement value)return Result("Unavailable",PreviewStatus.Unreadable,"The captured value is missing; its effective state is unknown. "+observation.Reason,observation.Source);
  if(value.ValueKind==JsonValueKind.Undefined)return Result("Unreadable captured value",PreviewStatus.Unreadable,"The captured JSON value is undefined; its effective state is unknown.",observation.Source);
  try
  {
   (string current,PreviewStatus status,string explanation) formatted;
   if(operation=="settings.restore-recorded")
   {
    var restore=Read<RestorationSnapshot>(value);
    var registered=FixedSettingOperation.Definitions.Single(d=>d.Setting==restore.Setting);
    if(observation.CheckId!="setting.restore."+restore.Setting)throw new JsonException("Restoration setting and check do not match.");
    var restored=Restoration(restore);
    expected=restored.expected;
    observation=observation with {Source=RegistrySource(registered.OperationId,observation.Source)};
    if(readiness is null)account=registered.Scope==OperationScope.Machine?"Device setting · privilege checked at review":"Original signed-in user";
    formatted=(restored.current,restored.status,restored.explanation);
   }
   else if(definition is not null&&definition.Setting is not (SettingId.NoAcSleep or SettingId.UkTimeZone))
   {
    var state=Read<RegistryDwordState>(value);
    if(state.Exists!=state.Value.HasValue)throw new JsonException("Inconsistent registry evidence.");
    if(!state.Exists)formatted=("No stored value",PreviewStatus.Missing,"There is no stored setting. Windows may use an OS default; no effective value is assumed.");
    else
    {
     int desired=definition.Setting==SettingId.LongPaths?1:0;
     string current=state.Value is 0 or 1?(state.Value==desired?expected:Opposite(expected))+" ("+state.Value+")":state.Value+" (unrecognised value)";
     formatted=(current,state.Value==desired?PreviewStatus.AlreadyCorrect:PreviewStatus.Different,"Stored DWORD: "+state.Value+"; requested DWORD: "+desired+".");
    }
   }
   else if(definition?.Setting==SettingId.NoAcSleep)
   {
    var state=Read<AcSleepState>(value);if(state.ActiveScheme==Guid.Empty)throw new JsonException("Missing active power plan.");
    expected="AC: Never; Battery: "+Duration(state.DcSeconds)+" (preserved)";
    formatted=("AC: "+Duration(state.Seconds)+"; Battery: "+Duration(state.DcSeconds),state.Seconds==0?PreviewStatus.AlreadyCorrect:PreviewStatus.Different,"Active power plan: "+state.ActiveScheme+". AC seconds: "+state.Seconds+"; battery seconds: "+state.DcSeconds+". Preserve the active plan and battery timeout.");
   }
   else if(definition?.Setting==SettingId.UkTimeZone)
   {
    var state=Read<TimeZoneState>(value);if(string.IsNullOrWhiteSpace(state.KeyName))throw new JsonException("Missing time zone.");
    string dst=state.DynamicDaylightTimeDisabled?"Disabled":"Enabled";expected+="; automatic daylight saving: "+dst+" (preserved)";
    formatted=((state.KeyName=="GMT Standard Time"?"United Kingdom":state.KeyName)+"; automatic daylight saving: "+dst,state.KeyName=="GMT Standard Time"?PreviewStatus.AlreadyCorrect:PreviewStatus.Different,"Windows time zone: "+state.KeyName+"; dynamicDaylightTimeDisabled: "+state.DynamicDaylightTimeDisabled+". Preserve the daylight-saving preference.");
   }
   else if(operation=="apps.remove-consumer-copilot")
   {
    var state=Read<ConsumerCopilotState>(value);UserSettingOperation.ValidateConsumerIdentity(state);
    formatted=(state.Packages.Length==0?"Not installed":string.Join(Environment.NewLine,state.Packages.Select(p=>p.PackageFullName)),state.Packages.Length==0?PreviewStatus.AlreadyCorrect:PreviewStatus.Different,"Exact consumer Copilot package inventory for the original user; other packages are preserved.");
   }
   else if(operation=="packages.install")
   {
    var review=observation.CheckId=="deployment.review-state"?Read<PackageSnapshot>(value):null;
    var inventory=review is null?Read<DeploymentInventory>(value):review.Inventory;
    if(inventory is null||!inventory.Available)return Result("Unavailable",PreviewStatus.Unreadable,inventory?.Reason??"Installed inventory is unavailable.",observation.Source);
    if(inventory.Present&&(string.IsNullOrWhiteSpace(inventory.ProductIdentity)||string.IsNullOrWhiteSpace(inventory.Version)))throw new JsonException("Present product inventory lacks identity or version.");
    string current=inventory.Present?(inventory.ProductIdentity??"Installed product")+"; version: "+(inventory.Version??"unknown"):"Not installed";
    if(review is null||string.IsNullOrWhiteSpace(review.ExpectedVersion)||string.IsNullOrWhiteSpace(review.ProductIdentity))
     formatted=(current,PreviewStatus.Manual,inventory.Reason+" The captured inventory does not include the approved target identity and version; profile identity alone cannot establish a match or authorise installation.");
    else
    {
     if(!Version.TryParse(review.ExpectedVersion,out _))throw new JsonException("Malformed approved version.");
     expected=(string.IsNullOrWhiteSpace(review.ProductName)?review.ProductIdentity:review.ProductName)+"; version: "+review.ExpectedVersion;
     bool correct=inventory.Present&&inventory.ProductIdentity==review.ProductIdentity&&inventory.Version==review.ExpectedVersion;
     formatted=(current,!inventory.Present?PreviewStatus.Missing:correct?PreviewStatus.AlreadyCorrect:PreviewStatus.Different,inventory.Reason+" Approved product identity: "+review.ProductIdentity+"; approved version: "+review.ExpectedVersion+". Exact installed identity and version are compared.");
    }
   }
   else formatted=Atomic(operation,value);
   return Result(formatted.current,formatted.status,formatted.explanation+" "+observation.Reason,observation.Source);
  }
  catch(Exception error) when(error is JsonException or InvalidDataException or InvalidOperationException or ArgumentException or LocalDeviceToolkit.Engine.Execution.PlanValidationException or NullReferenceException)
  {return Result("Unreadable captured value",PreviewStatus.Unreadable,"This value could not be interpreted. Its effective state is unknown. See the run log for raw evidence. "+observation.Reason,observation.Source);}
 }
 static string Expected(string? operation)=>operation switch
 {
  "settings.show-file-extensions"=>"Shown","settings.long-paths"=>"Enabled","settings.disable-fast-startup"=>"Disabled","settings.no-ac-sleep"=>"AC: Never (battery timeout preserved)","settings.uk-timezone"=>"United Kingdom (GMT Standard Time)","settings.uk-formats"=>"English (United Kingdom) (en-GB)","settings.uk-home-country"=>"United Kingdom (242)","settings.uk-input-language"=>"English (United Kingdom) present","settings.uk-keyboard"=>"United Kingdom keyboard (0809:00000809)","settings.uk-default-keyboard"=>"United Kingdom keyboard (0809:00000809)","apps.remove-consumer-copilot"=>"Not installed","packages.install"=>"Approved version",_=>"See control guidance"
 };
 static string RegistrySource(string? operation,string actual)
 {
  string? path=operation switch
  {
   "settings.show-file-extensions"=>@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced\HideFileExt",
   "settings.long-paths"=>@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled",
   "settings.disable-fast-startup"=>@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Power\HiberbootEnabled",_=>null
  };
  return path is null||actual.Contains(path,StringComparison.OrdinalIgnoreCase)?actual:actual+Environment.NewLine+"Registered value (Registry64): "+path;
 }
 static string Opposite(string value)=>value switch {"Shown"=>"Hidden","Enabled"=>"Disabled",_=>"Enabled"};
 static T Read<T>(JsonElement value)=>StrictJson.Read<T>(System.Text.Encoding.UTF8.GetBytes(value.GetRawText()),256*1024);
 static string Duration(uint seconds)=>seconds==0?"Never":seconds%60==0?seconds/60+" minutes":seconds+" seconds";
 static string Friendly(JsonElement value)=>value.ValueKind switch {JsonValueKind.True=>"Enabled (true)",JsonValueKind.False=>"Disabled (false)",JsonValueKind.String=>value.GetString()??"No value",JsonValueKind.Null=>"No stored value",JsonValueKind.Object=>"Details available",JsonValueKind.Array=>value.GetArrayLength()+" items reported",JsonValueKind.Undefined=>"Unavailable",_=>value.GetRawText()};
 static string? ReadOnlyExpected(string id)=>id switch
 {
  "secureboot.enabled"=>"Enabled","tpm.ready"=>"Ready","bitlocker.protected"=>"Protected","codeintegrity.enforced"=>"Enforced","antivirus.active"=>"Active","os.version"=>"Windows version 10.0.26200 or later (catalogue minimum)",_=>null
 };
 static string ReadOnlyCurrent(string id,JsonElement value)
 {
  if(id=="apps.inventory"&&value.ValueKind==JsonValueKind.Array)return value.GetArrayLength()+" applications found";
  if(id=="antivirus.providers"&&value.ValueKind==JsonValueKind.Array)return value.GetArrayLength()+" antivirus providers reported";
  if(id=="os.version"&&value.ValueKind==JsonValueKind.String)return "Windows version "+value.GetString();
  if(value.ValueKind is JsonValueKind.True or JsonValueKind.False)
  {
   bool enabled=value.GetBoolean();
   return id switch {"secureboot.enabled"=>enabled?"Enabled":"Disabled","tpm.ready"=>enabled?"Ready":"Not ready","bitlocker.protected"=>enabled?"Protected":"Not protected","codeintegrity.enforced"=>enabled?"Enforced":"Not enforced","antivirus.active"=>enabled?"Active":"Inactive",_=>Friendly(value)};
  }
  return Friendly(value);
 }
 static (string,PreviewStatus,string) Atomic(string? operation,JsonElement value)
 {
  string current;bool correct;
  switch(operation)
  {
   case "settings.uk-formats":var culture=Read<FormatsSnapshot>(value).Culture;if(string.IsNullOrWhiteSpace(culture))throw new JsonException();current=CultureName(culture);correct=culture.Equals("en-GB",StringComparison.OrdinalIgnoreCase);break;
   case "settings.uk-home-country":var country=Read<CountrySnapshot>(value).HomeGeoId;if(country<0)throw new JsonException();current=CountryName(country);correct=country==242;break;
   case "settings.uk-input-language":var language=Read<LanguageSnapshot>(value);if(language.LanguageTag!="en-GB")throw new JsonException();current="English (United Kingdom): "+(language.Present?"Present":"Not present");correct=language.Present;break;
   case "settings.uk-keyboard":var keyboard=Read<KeyboardSnapshot>(value);if(keyboard.LanguageTag!="en-GB"||keyboard.InputMethodTips is null||keyboard.InputMethodTips.Any(string.IsNullOrWhiteSpace))throw new JsonException();current=keyboard.InputMethodTips.Length==0?"No keyboard stored for English (United Kingdom)":string.Join(", ",keyboard.InputMethodTips.Select(KeyboardName));correct=keyboard.InputMethodTips.Contains("0809:00000809");break;
   case "settings.uk-default-keyboard":var tip=Read<DefaultKeyboardSnapshot>(value).DefaultInputMethodOverride;current=tip is null?"No stored default override":KeyboardName(tip);correct=tip=="0809:00000809";if(tip is null)return(current,PreviewStatus.Missing,"There is no stored default keyboard override; no effective default is assumed.");break;
   default:return(Friendly(value),PreviewStatus.Manual,"No registered value comparison is available; follow the control guidance.");
  }
  return(current,correct?PreviewStatus.AlreadyCorrect:PreviewStatus.Different,"Compare the selected UK setting only; preserve all other original-user preferences.");
 }
 static string CultureName(string value){try{return CultureInfo.GetCultureInfo(value).EnglishName+" ("+value+")";}catch(CultureNotFoundException){return value+" (unrecognised culture)";}}
 static string CountryName(int geoId)
 {
  if(geoId==242)return "United Kingdom (242)";
  if(geoId==244)return "United States (244)";
  var names=new HashSet<string>(StringComparer.Ordinal);
  foreach(var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
  {
   try{var region=new RegionInfo(culture.Name);if(region.GeoId==geoId)names.Add(region.EnglishName);}
   catch(ArgumentException){ }
  }
  return names.Count==1?names.Single()+" ("+geoId+")":geoId+" (Windows home country ID)";
 }
 static string KeyboardName(string value)
 {
  if(value=="0809:00000809")return "United Kingdom keyboard (0809:00000809)";
  if(value.Length>5&&value[4]==':'&&int.TryParse(value.AsSpan(0,4),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out int language))
  {
   try{return CultureInfo.GetCultureInfo(language).EnglishName+"; "+value+" (input method ID)";}
   catch(CultureNotFoundException){ }
  }
  return value+" (input method ID)";
 }
 static (string current,string expected,PreviewStatus status,string explanation) Restoration(RestorationSnapshot snapshot)
 {
  if(snapshot.Setting==SettingId.NoAcSleep)
  {
   var current=Read<AcSleepState>(snapshot.Current);var target=Read<AcSleepState>(snapshot.Target);
   if(current.ActiveScheme==Guid.Empty||target.ActiveScheme==Guid.Empty)throw new JsonException("A recorded power plan is missing.");
   string Text(AcSleepState state)=>"AC: "+Duration(state.Seconds)+"; Battery: "+Duration(state.DcSeconds)+"; active power plan: "+state.ActiveScheme;
   return(Text(current),Text(target),current==target?PreviewStatus.AlreadyCorrect:PreviewStatus.Different,"Restore the exact recorded AC/battery timeout and plan state; no standard timeout is substituted.");
  }
  if(snapshot.Setting==SettingId.UkTimeZone)
  {
   var current=Read<TimeZoneState>(snapshot.Current);var target=Read<TimeZoneState>(snapshot.Target);
   if(string.IsNullOrWhiteSpace(current.KeyName)||string.IsNullOrWhiteSpace(target.KeyName))throw new JsonException("A recorded time zone is missing.");
   string Text(TimeZoneState state)=>(state.KeyName=="GMT Standard Time"?"United Kingdom (GMT Standard Time)":state.KeyName)+"; automatic daylight saving: "+(state.DynamicDaylightTimeDisabled?"Disabled":"Enabled");
   return(Text(current),Text(target),current==target?PreviewStatus.AlreadyCorrect:PreviewStatus.Different,"Restore the exact recorded Windows time-zone identity and daylight-saving preference.");
  }
  var actual=Read<RegistryDwordState>(snapshot.Current);var desired=Read<RegistryDwordState>(snapshot.Target);
  if(actual.Exists!=actual.Value.HasValue||desired.Exists!=desired.Value.HasValue)throw new JsonException("Inconsistent recorded registry state.");
  string RegistryText(RegistryDwordState state)
  {
   if(!state.Exists)return "No stored value";
   if(state.Value is not (0 or 1))return state.Value+" (unrecognised value)";
   string label=snapshot.Setting switch {SettingId.ShowFileExtensions=>state.Value==0?"Shown":"Hidden",SettingId.LongPaths=>state.Value==1?"Enabled":"Disabled",_=>state.Value==0?"Disabled":"Enabled"};
   return label+" ("+state.Value+")";
  }
  return(RegistryText(actual),desired.Exists?RegistryText(desired):"Remove stored value",actual==desired?PreviewStatus.AlreadyCorrect:actual.Exists?PreviewStatus.Different:PreviewStatus.Missing,"Restore the exact recorded registry presence and DWORD. An absent recorded value means remove the stored value; no effective OS default is assumed.");
 }
 sealed record RestorationSnapshot(SettingId Setting,JsonElement Current,JsonElement Target);
 sealed record PackageSnapshot(string ProfileId,string InputDigest,DeploymentInventory Inventory,string? ExpectedVersion=null,string? ProductName=null,string? ProductIdentity=null);
 sealed record FormatsSnapshot(string Culture);
 sealed record CountrySnapshot(int HomeGeoId);
 sealed record LanguageSnapshot(string LanguageTag,bool Present);
 sealed record KeyboardSnapshot(string LanguageTag,string[] InputMethodTips);
 sealed record DefaultKeyboardSnapshot(string? DefaultInputMethodOverride);
}
