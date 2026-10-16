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
}
