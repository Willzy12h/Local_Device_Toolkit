using LocalDeviceToolkit.Windows.Inspection;
using LocalDeviceToolkit.Windows.Settings;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class TimeZoneReadTests
{
 [Fact] public async Task Native_time_zone_key_and_original_token_right_can_be_read_without_writes()
 {
  if(!OperatingSystem.IsWindows()){Assert.Throws<PlatformNotSupportedException>(()=>TimeZoneSettings.Read());Assert.Throws<PlatformNotSupportedException>(()=>TimeZonePrivilege.IsAssigned(Environment.ProcessId,"unsupported"));return;}
  var context=await new WindowsContextProvider().GetCurrentAsync(default);var actual=TimeZoneSettings.Read();
  Assert.Contains(actual.KeyName,TimeZoneInfo.GetSystemTimeZones().Select(t=>t.Id));
  _=TimeZonePrivilege.IsAssigned(Environment.ProcessId,context.OriginalUserSid);
  Assert.Throws<UnauthorizedAccessException>(()=>TimeZonePrivilege.IsAssigned(Environment.ProcessId,"S-1-5-18"));
 }
}
