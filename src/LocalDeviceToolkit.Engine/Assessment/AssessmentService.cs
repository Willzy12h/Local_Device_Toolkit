using LocalDeviceToolkit.Core;
using System.Text.Json;
namespace LocalDeviceToolkit.Engine.Assessment;
public static class AssessmentService
{
 public static IReadOnlyList<Finding> Assess(LocalCatalogue catalogue,DeviceCapture capture)
 {
  return catalogue.Controls.Select(control=>
  {
   var observations=capture.Observations.Where(o=>o.ControlId==control.Id).ToArray();
   Finding Result(FindingStatus status,string reason)=>new(control.Id,status,reason,Array.AsReadOnly(observations));
   if(capture.Catalogue!=catalogue.Identity)return Result(FindingStatus.Unknown,"The capture belongs to another catalogue version.");
   if(control.Coverage==CoverageMode.Manual||control.CheckIds.Length==0)return Result(FindingStatus.ManualVerification,control.ManualInstructions);
   bool missing=false,denied=false,failed=false,manual=false;
   foreach(var id in control.CheckIds)
   {
    var matches=observations.Where(o=>o.CheckId==id).ToArray();
    if(matches.Length!=1){missing=true;continue;}
    var observation=matches[0];
    if(observation.State==ObservationState.AccessDenied){denied=true;continue;}
    if(observation.State!=ObservationState.Known||!observation.Value.HasValue){missing=true;continue;}
    bool? match=Match(id,observation.Value.Value);
    if(match is false)failed=true;else if(match is null)manual=true;
   }
   if(failed)return Result(FindingStatus.DoesNotMeetStandard,"At least one observed required setting differs from the standard; inspect all checks and limitations.");
   if(denied)return Result(FindingStatus.RequiresElevation,"A required check needs explicit administrator access.");
   if(missing)return Result(FindingStatus.Unknown,"Required evidence is missing, unavailable or ambiguous.");
   if(manual||control.Coverage==CoverageMode.Partial)return Result(FindingStatus.ManualVerification,"Available local checks passed; complete the documented manual verification.");
   return Result(FindingStatus.MeetsStandard,"All declared local checks meet the standard.");
  }).ToArray();
 }
 static bool? Match(string id,JsonElement value)
 {
  if(!ProbeValues.Accepts(id,value))return null;
  if(id is "secureboot.enabled" or "tpm.ready" or "bitlocker.protected" or "codeintegrity.enforced" or "antivirus.active" or "extensions.visible" or "paths.enabled" or "keyboard.uk" or "store.installed")return value.GetBoolean();
  if(id is "faststartup.enabled" or "copilot.consumer.present")return !value.GetBoolean();
  if(id=="power.acsleep.seconds")return value.GetUInt32()==0;
  if(id=="os.version")return Version.Parse(value.GetString()!)>=new Version(10,0,26200);
  if(id=="timezone.id")return value.GetString()=="GMT Standard Time";
  if(id=="region.culture")return value.GetString()=="en-GB";
  // Presence of policy/profile keys or an inventory is evidence for manual review, never proof of its contents or licensing.
  return null;
 }
}
