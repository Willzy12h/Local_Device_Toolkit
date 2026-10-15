using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Windows.Inspection;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class OperationalFactsParserTests
{
 static LocalProbeResult Result=>new(2,"local.bitlocker.state",new string('a',64),ObservationState.Known,new("Protected",null,"Observed",new Dictionary<string,string>{{"protection","On"},{"recoveryVerification","Not externally verified"}}),"Fixed local probe",DateTimeOffset.UtcNow,new(true,false,1,null,[]),"Observed locally.");
 [Fact] public void Nonsecret_result_preserves_target_and_coverage()
 {
  var r=Result;var parsed=OperationalFactsParser.Parse(StrictJson.Bytes(r),r.CheckId,r.TargetDigest,r.CapturedAt.AddSeconds(-1),r.CapturedAt.AddSeconds(1));Assert.True(parsed.Coverage.Complete);Assert.Equal(r.TargetDigest,parsed.TargetDigest);
 }
 [Fact] public void Recovery_password_or_unrequested_detail_is_refused_before_persistence()
 {
  var r=Result with {Value=new("Protected",null,"Observed",new Dictionary<string,string>{{"recoveryPassword","secret"}})};
  Assert.Throws<InvalidDataException>(()=>OperationalFactsParser.Parse(StrictJson.Bytes(r),r.CheckId,r.TargetDigest,r.CapturedAt.AddSeconds(-1),r.CapturedAt.AddSeconds(1)));
 }
 [Fact] public void Wrong_target_unknown_version_and_stale_time_are_refused()
 {
  var r=Result;
  foreach(var invalid in new[]{r with {SchemaVersion=1},r with {TargetDigest=new string('b',64)},r with {CapturedAt=r.CapturedAt.AddHours(-1)},r with {CheckId="local.print.queue"}})
   Assert.Throws<InvalidDataException>(()=>OperationalFactsParser.Parse(StrictJson.Bytes(invalid),r.CheckId,r.TargetDigest,r.CapturedAt.AddSeconds(-1),r.CapturedAt.AddSeconds(1)));
 }
 [Fact] public void Unknown_provider_cannot_carry_an_invented_value()
 {
  var r=Result with {State=ObservationState.Unavailable};
  Assert.Throws<InvalidDataException>(()=>OperationalFactsParser.Parse(StrictJson.Bytes(r),r.CheckId,r.TargetDigest,r.CapturedAt.AddSeconds(-1),r.CapturedAt.AddSeconds(1)));
 }
}
