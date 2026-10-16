using System.Globalization;
using System.Xml;
using System.Xml.Linq;
namespace LocalDeviceToolkit.Windows.Inspection.Probes;
public sealed record BatteryCapacity(string Reference,ulong? DesignMWh,ulong? FullChargeMWh,uint? CycleCount);
public static class BatteryReportReader
{
 public static IReadOnlyList<BatteryCapacity> Read(Stream xml)
 {
  using var reader=XmlReader.Create(xml,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4*1024*1024});
  var document=XDocument.Load(reader);var batteries=document.Descendants().Where(x=>x.Name.LocalName=="Batteries").SelectMany(x=>x.Elements().Where(e=>e.Name.LocalName=="Battery")).ToArray();
  if(batteries.Length>16)throw new InvalidDataException("Battery report exceeds the supported device count.");
  return batteries.Select((b,i)=>
  {
   ulong? Number(string key)=>ulong.TryParse(b.Elements().SingleOrDefault(x=>x.Name.LocalName==key)?.Value,NumberStyles.None,CultureInfo.InvariantCulture,out var n)?n:null;
   var cycles=Number("CycleCount");return new BatteryCapacity("Battery "+(i+1),Number("DesignCapacity"),Number("FullChargeCapacity"),cycles<=uint.MaxValue?(uint?)cycles:null);
  }).ToArray();
 }
 public static decimal? CapacityPercent(BatteryCapacity battery)=>battery.DesignMWh is >0&&battery.FullChargeMWh is ulong full?(decimal)full/battery.DesignMWh.Value*100:null;
}
