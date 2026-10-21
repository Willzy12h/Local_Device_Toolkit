using LocalDeviceToolkit.Core;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class IncidentEventProbeTests
{
 [Fact] public async Task Bounded_page_never_reads_message_and_reports_more()
 {
  var result=await ScriptProbeFixture.ReadAsync("probes.events","local.events.incident",new {symptom="crash",fromUtc="2026-10-14T00:00:00Z",toUtc="2026-10-15T00:00:00Z"},"""
function Get-WinEvent {param($LogName,$FilterXPath,$MaxEvents,[switch]$Oldest)
 if(-not $FilterXPath){[pscustomobject]@{RecordId=$(if($Oldest){1}else{1000});TimeCreated=[datetime]'2026-10-13T00:00:00Z'};return}
 1..201 | ForEach-Object { $row=[pscustomobject]@{RecordId=(1000-$_);TimeCreated=[datetime]'2026-10-14T12:00:00Z';ProviderName='Microsoft-Windows-Kernel-Power';Id=41;Properties=@()};$row | Add-Member ScriptProperty Message {throw 'Message must never be read'};$row }
}
""");
  Assert.Equal(ObservationState.Known,result.State);Assert.Equal(200,result.Coverage.ReturnedCount);Assert.True(result.Coverage.HasMore);Assert.False(result.Coverage.Complete);Assert.NotNull(result.Coverage.NextCursor);Assert.DoesNotContain("Message",result.Value!.Details["eventRecords"]);
 }
 [Fact] public async Task Clock_rollback_does_not_skip_unreturned_record_ids()
 {
  const string mocks="""
function Get-WinEvent {param($LogName,$FilterXPath,$MaxEvents,[switch]$Oldest)
 if($LogName -ne 'System'){throw 'Channel denied'}
 if(-not $FilterXPath){[pscustomobject]@{RecordId=$(if($Oldest){1}else{201});TimeCreated=[datetime]'2026-10-14T00:00:00Z'};return}
 $before=[long]([regex]::Match($FilterXPath,'EventRecordID < (\d+)').Groups[1].Value)
 201..1 | Where-Object {$_ -lt $before} | Select-Object -First $MaxEvents | ForEach-Object {[pscustomobject]@{RecordId=$_;TimeCreated=$(if($_ -eq 201){[datetime]'2026-10-14T01:00:00Z'}else{[datetime]'2026-10-14T02:00:00Z'});ProviderName='Microsoft-Windows-Kernel-Power';Id=41;Properties=@()}}
}
""";
  var first=await ScriptProbeFixture.ReadAsync("probes.events","local.events.incident",new{symptom="crash",fromUtc="2026-10-14T00:00:00Z",toUtc="2026-10-15T00:00:00Z"},mocks);
  var one=StrictJson.Read<EventEvidence[]>(System.Text.Encoding.UTF8.GetBytes(first.Value!.Details["eventRecords"]));Assert.Contains(one,e=>e.RecordId==201);
  var second=await ScriptProbeFixture.ReadAsync("probes.events","local.events.incident",new{symptom="crash",fromUtc="2026-10-14T00:00:00Z",toUtc="2026-10-15T00:00:00Z",cursor=first.Coverage.NextCursor},mocks);
  var two=StrictJson.Read<EventEvidence[]>(System.Text.Encoding.UTF8.GetBytes(second.Value!.Details["eventRecords"]));var ids=one.Concat(two).Select(e=>e.RecordId).ToArray();Assert.Equal(201,ids.Length);Assert.Equal(201,ids.Distinct().Count());
 }
 [Fact] public async Task Denied_channels_remain_unknown()
 {
  var result=await ScriptProbeFixture.ReadAsync("probes.events","local.events.incident",new {symptom="crash",fromUtc="2026-10-14T00:00:00Z",toUtc="2026-10-15T00:00:00Z"},"function Get-WinEvent {throw 'Access denied secret'}");
  Assert.Equal(ObservationState.Unavailable,result.State);Assert.False(result.Coverage.Complete);Assert.DoesNotContain("secret",StrictJson.Value(result).GetRawText());
 }
}
