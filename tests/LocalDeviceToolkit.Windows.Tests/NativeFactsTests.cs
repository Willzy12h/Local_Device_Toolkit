using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Windows.Inspection;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class NativeFactsTests
{
 [Fact] public async Task Context_is_observed_or_explicitly_unsupported_never_invented()
 {
  var provider=new WindowsContextProvider();
  if(!OperatingSystem.IsWindows())await Assert.ThrowsAsync<PlatformNotSupportedException>(()=>provider.GetCurrentAsync(default));
  else
  {
   var context=await provider.GetCurrentAsync(default);Assert.StartsWith("S-1-",context.OriginalUserSid);Assert.True(Guid.TryParse(context.DeviceId,out _));
   var actor=await provider.GetActorAsync(default);Assert.Equal(context.OriginalUserSid,actor.Sid);Assert.True(actor.ProcessId>0);
  }
 }
 [Fact] public void Known_null_is_not_a_measured_boolean()
 {
  var now=DateTimeOffset.UtcNow;
  var bytes=StrictJson.Bytes(new WindowsFactsResponse(1,[new("secureboot.enabled",ObservationState.Known,null,"fixture",now,"")]));
  Assert.Throws<InvalidDataException>(()=>WindowsFactsParser.Parse(bytes,new HashSet<string>{"secureboot.enabled"},now.AddSeconds(-1),now.AddSeconds(1)));
 }
 [Fact] public void Unrequested_facts_are_refused()
 {
  var now=DateTimeOffset.UtcNow;
  var bytes=StrictJson.Bytes(new WindowsFactsResponse(1,[new("secureboot.enabled",ObservationState.Known,StrictJson.Value(true),"fixture",now,"")]));
  Assert.Throws<InvalidDataException>(()=>WindowsFactsParser.Parse(bytes,new HashSet<string>{"tpm.ready"},now.AddSeconds(-1),now.AddSeconds(1)));
 }
 [Fact] public void Facts_outside_the_observed_run_are_refused()
 {
  var now=DateTimeOffset.UtcNow;
  var bytes=StrictJson.Bytes(new WindowsFactsResponse(1,[new("secureboot.enabled",ObservationState.Known,StrictJson.Value(true),"fixture",now.AddHours(-1),"")]));
  Assert.Throws<InvalidDataException>(()=>WindowsFactsParser.Parse(bytes,new HashSet<string>{"secureboot.enabled"},now.AddSeconds(-1),now.AddSeconds(1)));
 }
}
