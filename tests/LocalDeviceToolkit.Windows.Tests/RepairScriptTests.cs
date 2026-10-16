using System.Diagnostics;
using System.Text.Json;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Windows.Inspection;
using LocalDeviceToolkit.Windows.Processes;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class RepairScriptTests
{
 [Theory][InlineData("Passive",false)][InlineData("Normal",true)]
 public async Task Defender_mode_and_sources_are_projected_without_requesting_updates(string mode,bool expected)
 {
  var root=Path.Combine(Path.GetTempPath(),"repair-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try
  {
   using var script=RegisteredScripts.OpenVerified(Path.Combine(AppContext.BaseDirectory,"scripts"),"repair.read");var request=Path.Combine(root,"request.json");var fixture=Path.Combine(root,"fixture.ps1");
   await File.WriteAllBytesAsync(request,StrictJson.Bytes(new{kind="DefenderSignatures",input=new{confirmNetworkSources=true}}));
   await File.WriteAllTextAsync(fixture,"""
param([string]$Probe,[string]$RequestFile,[string]$Mode)
function Get-MpComputerStatus {[pscustomobject]@{AMRunningMode=$Mode;AMServiceEnabled=$true;AntivirusEnabled=$true;AntivirusSignatureVersion='1.2.3.4';AntivirusSignatureLastUpdated=[datetime]'2026-10-15T00:00:00Z'}}
function Get-MpPreference {[pscustomobject]@{SignatureFallbackOrder='InternalDefinitionUpdateServer|MicrosoftUpdateServer'}}
function Update-MpSignature {throw 'A read must never update'}
& $Probe -RequestFile $RequestFile
""");
   var shell=OperatingSystem.IsWindows()?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"):Environment.GetEnvironmentVariable("TOOLKIT_TEST_PWSH")!;
   var start=new ProcessStartInfo(shell);foreach(var arg in new[]{"-NoProfile","-NonInteractive","-File",fixture,"-Probe",script.Name,"-RequestFile",request,"-Mode",mode})start.ArgumentList.Add(arg);
   var result=await new DiagnosticProcessRunner().RunCommandAsync(start,TimeSpan.FromSeconds(30),default);Assert.Equal(0,result.ExitCode);using var value=JsonDocument.Parse(result.StandardOutput);Assert.Equal(expected,value.RootElement.GetProperty("ready").GetBoolean());Assert.Equal(mode,value.RootElement.GetProperty("identity").GetProperty("mode").GetString());Assert.DoesNotContain("A read must never update",result.StandardOutput);
  }finally{Directory.Delete(root,true);}
 }
 [Fact] public async Task Ipv4_only_adapter_is_readable_and_effective_routing_is_bound()
 {
  var root=Path.Combine(Path.GetTempPath(),"dns-state-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try
  {
   using var script=RegisteredScripts.OpenVerified(Path.Combine(AppContext.BaseDirectory,"scripts"),"repair.read");var request=Path.Combine(root,"request.json");var fixture=Path.Combine(root,"fixture.ps1");
   await File.WriteAllBytesAsync(request,StrictJson.Bytes(new{kind="DnsCache",input=new{hostname="example.test",expectedAddress="192.0.2.2",hypothesis="configuration-change",confirmWholeCacheEffect=true,confirmStaleCacheHypothesis=true}}));
   await File.WriteAllTextAsync(fixture,"""
param([string]$Probe,[string]$RequestFile)
$env:SystemRoot='/tmp'
function Get-NetIPConfiguration {param([switch]$All) if(-not $All){throw 'Virtual interfaces must be included'};[pscustomobject]@{NetAdapter=[pscustomobject]@{Status='Up';InterfaceGuid='11111111-1111-1111-1111-111111111111';InterfaceDescription='Fixture VPN'};IPv4Address=@([pscustomobject]@{IPAddress='192.0.2.10'});IPv6Address=@();DNSServer=@([pscustomobject]@{ServerAddresses=@('192.0.2.53')});IPv4DefaultGateway=@([pscustomobject]@{NextHop='192.0.2.1'});IPv6DefaultGateway=$null}}
function Get-DnsClientNrptPolicy {param([switch]$Effective) @()}
function Get-FileHash {param($LiteralPath,$Algorithm) [pscustomobject]@{Hash=('a'*64)}}
function Get-DnsClientCache {[pscustomobject]@{Entry='example.test';Data='192.0.2.1';Status='Success'}}
function Get-NetRoute {param($PolicyStore) [pscustomobject]@{DestinationPrefix='0.0.0.0/0';NextHop='192.0.2.1';InterfaceIndex=1;RouteMetric=25}}
function Get-NetIPInterface {[pscustomobject]@{InterfaceIndex=1;AddressFamily='IPv4';InterfaceMetric=10;ConnectionState='Connected'}}
& $Probe -RequestFile $RequestFile
""");
   var shell=OperatingSystem.IsWindows()?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"):Environment.GetEnvironmentVariable("TOOLKIT_TEST_PWSH")!;var start=new ProcessStartInfo(shell);foreach(var arg in new[]{"-NoProfile","-NonInteractive","-File",fixture,"-Probe",script.Name,"-RequestFile",request})start.ArgumentList.Add(arg);
   var result=await new DiagnosticProcessRunner().RunCommandAsync(start,TimeSpan.FromSeconds(30),default);Assert.Equal(0,result.ExitCode);using var value=JsonDocument.Parse(result.StandardOutput);Assert.True(value.RootElement.GetProperty("ready").GetBoolean());Assert.Contains("RouteMetric",value.RootElement.GetProperty("identity").GetProperty("routes").GetString());Assert.Contains("InterfaceMetric",value.RootElement.GetProperty("identity").GetProperty("interfaces").GetString());
  }finally{Directory.Delete(root,true);}
 }
 [Fact] public void Update_body_has_no_source_override_or_security_changes()
 {
  using var file=RegisteredScripts.OpenVerified(Path.Combine(AppContext.BaseDirectory,"scripts"),"repair.defender",false);using var read=new StreamReader(file);var text=read.ReadToEnd();Assert.Contains("Update-MpSignature;",text);Assert.DoesNotContain("-UpdateSource",text);Assert.DoesNotContain("Set-MpPreference",text);
 }
}
