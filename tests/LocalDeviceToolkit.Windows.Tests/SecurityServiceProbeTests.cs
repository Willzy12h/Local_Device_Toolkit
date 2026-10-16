using LocalDeviceToolkit.Core;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class SecurityServiceProbeTests
{
 [Fact] public async Task Passive_Defender_remains_passive_without_enabling_it()
 {
  var result=await ScriptProbeFixture.ReadAsync("probes.services","local.defender.protection",new {},"""
function Get-MpComputerStatus {[pscustomobject]@{AMRunningMode='Passive';AntivirusEnabled=$false;RealTimeProtectionEnabled=$false}}
function Set-MpPreference {throw 'Mutation forbidden'}
""");
  Assert.Equal(ObservationState.Known,result.State);Assert.Equal("Passive",result.Value!.Details["mode"]);Assert.Equal("False",result.Value.Details["protectionEnabled"]);
 }
 [Fact] public async Task Printer_documents_and_owners_do_not_leave_projection()
 {
  var result=await ScriptProbeFixture.ReadAsync("probes.services","local.print.queue",new {printerName="Fixture",allowRemoteRead=false},"""
function Get-Printer {[pscustomobject]@{Name='Fixture';Type='Local'}}
function Get-PrintJob {param($PrinterName)[pscustomobject]@{ID=1;JobStatus='Paused';DocumentName='PRIVATE-DOCUMENT';UserName='PRIVATE-OWNER'}}
function Restart-Service {throw 'Mutation forbidden'}
""");
  Assert.Equal(ObservationState.Known,result.State);Assert.Contains("Paused",result.Value!.Details["jobs"]);var json=StrictJson.Value(result).GetRawText();Assert.DoesNotContain("PRIVATE-DOCUMENT",json);Assert.DoesNotContain("PRIVATE-OWNER",json);
 }
}
