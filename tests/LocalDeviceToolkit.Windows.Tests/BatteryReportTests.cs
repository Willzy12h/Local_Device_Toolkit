using System.Text;
using LocalDeviceToolkit.Windows.Inspection.Probes;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class BatteryReportTests
{
 [Fact] public void Multiple_batteries_and_zero_design_do_not_invent_health()
 {
  using var xml=new MemoryStream(Encoding.UTF8.GetBytes("""<BatteryReport xmlns="urn:test"><Batteries><Battery><DesignCapacity>50000</DesignCapacity><FullChargeCapacity>40000</FullChargeCapacity><CycleCount>20</CycleCount></Battery><Battery><DesignCapacity>0</DesignCapacity><FullChargeCapacity>30000</FullChargeCapacity></Battery></Batteries></BatteryReport>"""));
  var batteries=BatteryReportReader.Read(xml);Assert.Equal(2,batteries.Count);Assert.Equal(80m,BatteryReportReader.CapacityPercent(batteries[0]));Assert.Null(BatteryReportReader.CapacityPercent(batteries[1]));
 }
 [Fact] public void External_entities_are_never_evaluated()
 {
  using var xml=new MemoryStream(Encoding.UTF8.GetBytes("""<!DOCTYPE BatteryReport [<!ENTITY secret SYSTEM "file:///etc/passwd">]><BatteryReport>&secret;</BatteryReport>"""));
  Assert.ThrowsAny<Exception>(()=>BatteryReportReader.Read(xml));
 }
}
