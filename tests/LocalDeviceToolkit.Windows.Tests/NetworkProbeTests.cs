using LocalDeviceToolkit.Core;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class NetworkProbeTests
{
 [Fact] public async Task Dns_uses_system_resolver_and_reports_selected_context()
 {
  var result=await ScriptProbeFixture.ReadAsync("probes.network","local.network.dns",new {hostname="internal.example",queryType="A"},"""
function Get-NetAdapter {[pscustomobject]@{Status='Up';InterfaceGuid=[Guid]'00000000-0000-0000-0000-000000000001';Name='Fixture';ifIndex=1}}
function Get-DnsClientServerAddress {param($InterfaceIndex)[pscustomobject]@{AddressFamily='IPv4';ServerAddresses=@('10.0.0.1')}}
function Get-NetIPAddress {param($InterfaceIndex)[pscustomobject]@{IPAddress='10.0.0.2';PrefixLength=24}}
function Resolve-DnsName {param($Name,$Type,[switch]$DnsOnly,[switch]$QuickTimeout,$Server) if($Server -or $Name -ne 'internal.example' -or -not $DnsOnly){throw 'Unapproved query'};[pscustomobject]@{Name=$Name;Type=$Type;IPAddress='10.0.0.3';TTL=10}}
""");
  Assert.Equal(ObservationState.Known,result.State);Assert.Equal("internal.example",result.Value!.Details["hostname"]);Assert.Contains("10.0.0.3",result.Value.Details["answers"]);Assert.Contains("does not force",result.Value.Details["vpn"]);
 }
}
