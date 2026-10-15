using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Assessment;
using LocalDeviceToolkit.Engine.Catalogue;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class AssessmentTests
{
 [Theory]
 [InlineData("secureboot.enabled","false",FindingStatus.DoesNotMeetStandard)]
 [InlineData("faststartup.enabled","false",FindingStatus.MeetsStandard)]
 [InlineData("power.acsleep.seconds","0",FindingStatus.MeetsStandard)]
 [InlineData("power.acsleep.seconds","900",FindingStatus.DoesNotMeetStandard)]
 [InlineData("os.version","\"10.0.26100\"",FindingStatus.DoesNotMeetStandard)]
 [InlineData("os.version","\"10.0.26200\"",FindingStatus.MeetsStandard)]
 [InlineData("timezone.id","\"GMT Standard Time\"",FindingStatus.MeetsStandard)]
 [InlineData("timezone.id","\"UTC\"",FindingStatus.DoesNotMeetStandard)]
 [InlineData("copilot.consumer.present","false",FindingStatus.MeetsStandard)]
 [InlineData("copilot.consumer.present","true",FindingStatus.DoesNotMeetStandard)]
 [InlineData("onedrive.configured","true",FindingStatus.ManualVerification)]
 public void Evidence_is_compared_to_the_actual_local_requirement(string check,string json,FindingStatus expected)
 {
  var catalogue=CatalogueLoader.LoadShipped();var control=catalogue.Controls[0] with { Coverage=CoverageMode.Automatic,CheckIds=[check] };
  var capture=TestData.Capture() with { Observations=[new(control.Id,check,ObservationState.Known,System.Text.Json.JsonDocument.Parse(json).RootElement.Clone(),"fixture",DateTimeOffset.UtcNow,"")] };
  Assert.Equal(expected,Assert.Single(AssessmentService.Assess(catalogue with { Controls=[control] },capture)).Status);
 }
}
