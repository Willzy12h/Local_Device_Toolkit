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
  // Additional check comparators are admitted with their Windows probe tests in T4.
  if(id=="extensions.visible")return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
  return null;
 }
}
