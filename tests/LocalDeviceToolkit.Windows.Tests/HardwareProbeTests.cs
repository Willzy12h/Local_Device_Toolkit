using LocalDeviceToolkit.Core;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class HardwareProbeTests
{
 [Fact] public async Task Intentionally_disabled_device_is_explained_and_not_enabled()
 {
  var result=await ScriptProbeFixture.ReadAsync("probes.hardware","local.hardware.problems",new {},"""
function Get-PnpDevice {param([switch]$PresentOnly) if(-not $PresentOnly){throw 'Historical devices must not be queried'};[pscustomobject]@{Status='Error';FriendlyName='Fixture keyboard';Class='Keyboard';InstanceId='fixture'}}
function Get-PnpDeviceProperty {param($InstanceId,$KeyName)[pscustomobject]@{Data=22}}
function Enable-PnpDevice {throw 'Mutation forbidden'}
""");
  Assert.Equal(ObservationState.Known,result.State);Assert.Contains("Disabled (22)",result.Value!.Details["devices"]);
 }
}
