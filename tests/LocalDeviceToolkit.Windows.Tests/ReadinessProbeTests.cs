using System.Diagnostics;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Windows.Inspection;
using LocalDeviceToolkit.Windows.Processes;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class ReadinessProbeTests
{
 [Fact] public async Task Bitlocker_uses_nonsecret_methods_and_friendly_conversion()
 {
  var result=await ScriptProbeFixture.ReadAsync("probes.readiness","local.bitlocker.state",new {volumeId="C:"},"""
function Get-CimInstance {param($Namespace,$ClassName,$Filter) if($ClassName -ne 'Win32_EncryptableVolume'){throw 'Unexpected class'};[pscustomobject]@{Fixture=1}}
function Invoke-CimMethod {param($InputObject,$MethodName,$Arguments)
 switch($MethodName){
 'GetConversionStatus' {[pscustomobject]@{ReturnValue=0;ConversionStatus=1}}
 'GetProtectionStatus' {[pscustomobject]@{ReturnValue=0;ProtectionStatus=1}}
 'GetKeyProtectors' {[pscustomobject]@{ReturnValue=0;VolumeKeyProtectorID=@('{00000000-0000-0000-0000-000000000001}')}}
 'GetKeyProtectorType' {[pscustomobject]@{ReturnValue=0;KeyProtectorType=3}}
 default {throw 'Secret or unsupported method was requested'}
 }}
""");
  Assert.Equal(ObservationState.Known,result.State);Assert.Contains("Fully encrypted",result.Value!.Current);Assert.Equal("On",result.Value.Details["protection"]);Assert.DoesNotContain("recoveryPassword",StrictJson.Value(result).GetRawText());
 }
}
public static class ScriptProbeFixture
{
 public static async Task<LocalProbeResult> ReadAsync(string scriptId,string checkId,object input,string mocks)
 {
  var root=Path.Combine(Path.GetTempPath(),"toolkit-probe-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try
  {
   using var script=RegisteredScripts.OpenVerified(Path.Combine(AppContext.BaseDirectory,"scripts"),scriptId);using var helpers=RegisteredScripts.OpenVerified(Path.Combine(AppContext.BaseDirectory,"scripts"),"probes.common");
   var request=Path.Combine(root,"request.json");var wrapper=Path.Combine(root,"fixture.ps1");var digest=new string('a',64);
   await File.WriteAllBytesAsync(request,StrictJson.Bytes(new {schemaVersion=2,checkId,targetDigest=digest,parameters=input}));
   await File.WriteAllTextAsync(wrapper,"param([string]$Probe,[string]$RequestFile,[string]$HelpersPath)\n"+mocks+"\n& $Probe -RequestFile $RequestFile -HelpersPath $HelpersPath\n");
   var executable=OperatingSystem.IsWindows()?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"):Environment.GetEnvironmentVariable("TOOLKIT_TEST_PWSH")??throw new InvalidOperationException("Activate the pinned PowerShell test tool.");
   var start=new ProcessStartInfo(executable);foreach(var arg in new[]{"-NoLogo","-NoProfile","-NonInteractive","-File",wrapper,"-Probe",script.Name,"-RequestFile",request,"-HelpersPath",helpers.Name})start.ArgumentList.Add(arg);
   var began=DateTimeOffset.UtcNow;var result=await new DiagnosticProcessRunner().RunCommandAsync(start,TimeSpan.FromSeconds(45),default);
   Assert.Equal(DiagnosticEnd.Exited,result.End);Assert.Equal(0,result.ExitCode);
   return OperationalFactsParser.Parse(System.Text.Encoding.UTF8.GetBytes(result.StandardOutput),checkId,digest,began,DateTimeOffset.UtcNow);
  }
  finally {Directory.Delete(root,true);}
 }
}
