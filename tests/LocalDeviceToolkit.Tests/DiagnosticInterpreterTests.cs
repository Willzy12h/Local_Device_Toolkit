using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Diagnostics;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class DiagnosticInterpreterTests
{
 [Fact] public void Security_and_unknown_are_signposted_beyond_three_expanded_findings()
 {
  var values=Enumerable.Range(0,6).Select(i=>new DiagnosticFinding("f"+i,"1","Observed "+i,"Review evidence","Next step","Confirmed observation",i==5,i==4,[],null)).ToArray();
  var summary=DiagnosticInterpreter.Summarize(values);Assert.Equal(3,summary.Expanded.Count);Assert.Contains(summary.Signposts,f=>f.SecurityRelevant);Assert.Contains(summary.Signposts,f=>f.RequiredUnknown);
 }
 [Fact] public void Event_41_is_unclean_shutdown_and_not_a_cause()
 {
  var finding=DiagnosticInterpreter.ExplainEvent(new("System","Microsoft-Windows-Kernel-Power",41,1,DateTimeOffset.UtcNow,"record:1",new Dictionary<string,string>()));
  Assert.Contains("unclean",finding.Observed,StringComparison.OrdinalIgnoreCase);Assert.Contains("not established",finding.Why,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("faulty power supply",finding.Why,StringComparison.OrdinalIgnoreCase);
 }
 [Fact] public void Same_event_id_different_resource_remains_separate()
 {
  var at=DateTimeOffset.UtcNow;var events=new[]{new EventEvidence("Application","Application Error",1000,1,at,"a.exe",new Dictionary<string,string>{{"app","a.exe"}}),new EventEvidence("Application","Application Error",1000,2,at,"b.exe",new Dictionary<string,string>{{"app","b.exe"}})};
  Assert.Equal(2,IncidentGrouper.Group(events).Count);
 }
 [Fact] public void Equal_time_incident_groups_and_records_have_deterministic_order()
 {
  var at=DateTimeOffset.Parse("2026-10-21T10:01:00Z");
  var events=new[]{
   new EventEvidence("Application","Application Error",1000,3,at,"b.exe",new Dictionary<string,string>{{"app","b.exe"}}),
   new EventEvidence("Application","Application Error",1000,2,at,"a.exe",new Dictionary<string,string>{{"app","a.exe"}}),
   new EventEvidence("Application","Application Error",1000,1,at,"a.exe",new Dictionary<string,string>{{"app","a.exe"}})};
  var forward=IncidentGrouper.Group(events);var reverse=IncidentGrouper.Group(events.Reverse());
  Assert.Equal(StrictJson.Value(forward).GetRawText(),StrictJson.Value(reverse).GetRawText());
  Assert.Equal("a.exe",forward[0].Resource);Assert.Equal(new long[]{1,2},forward[0].RecordIds);
 }
 [Fact] public void Incident_explanation_includes_group_count_and_exact_utc_range()
 {
  var first=DateTimeOffset.Parse("2026-10-21T10:01:00Z");var last=first.AddMinutes(2);
  var events=new[]{new EventEvidence("System","Microsoft-Windows-Kernel-Power",41,1,first,"device",new Dictionary<string,string>()),new EventEvidence("System","Microsoft-Windows-Kernel-Power",41,2,last,"device",new Dictionary<string,string>())};
  var value=new LocalProbeValue("2 events",null,"Observed",new Dictionary<string,string>{{"eventRecords",StrictJson.Value(events).GetRawText()}});
  var observation=new ProbeObservation("local.events.incident","local.events.incident",ObservationState.Known,StrictJson.Value(value),"Fixture",last,"Observed"){Coverage=new(true,false,2,null,[])};
  var finding=Assert.Single(DiagnosticInterpreter.Interpret(observation));
  Assert.Contains("2 events",finding.Observed);Assert.Contains(first.ToString("O"),finding.Observed);Assert.Contains(last.ToString("O"),finding.Observed);
  Assert.Contains("not established",finding.Why);
 }
}
