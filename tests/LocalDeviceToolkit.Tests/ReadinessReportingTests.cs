using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Reporting;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class ReadinessReportingTests
{
 [Fact] public void Unknown_required_item_blocks_handover_and_exceptions_are_visible()
 {
  var profile=new ClientChecklist("client",1,"Client",new Dictionary<string,System.Text.Json.JsonElement>(),["manual.vpn"],true);
  var result=HandoverService.Evaluate(profile,TestData.Context,[],[],DateTimeOffset.UtcNow);
  Assert.NotEqual("Ready for handover",result.State);Assert.Contains("manual.vpn",result.Outstanding);
 }
 [Fact] public void Different_target_is_not_comparable()
 {
  var a=new ProbeObservation("local.network.dns","local.network.dns",ObservationState.Known,StrictJson.Value(new LocalProbeValue("Resolved",null,"Observed",new Dictionary<string,string>())),"Fixture",DateTimeOffset.UtcNow,"Observed"){TargetDigest=new string('a',64),Coverage=new(true,false,1,null,[])};
  var b=a with {TargetDigest=new string('b',64)};var capture=TestData.Capture() with {Observations=[a]};
  Assert.All(RunComparisonService.Compare(capture,capture with {Observations=[b]}),x=>Assert.Equal("Not comparable",x.State));
 }
 [Fact] public void Legacy_observation_has_no_added_null_fields()
 {
  var observation=new ProbeObservation("control","check",ObservationState.Known,StrictJson.Value(true),"Fixture",DateTimeOffset.UtcNow,"Observed");
  var json=StrictJson.Value(observation).GetRawText();Assert.DoesNotContain("coverage",json);Assert.DoesNotContain("targetDigest",json);
 }
}
