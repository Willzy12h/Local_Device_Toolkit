using System.Xml.Linq;
using LocalDeviceToolkit.Engine.Deployments;
namespace LocalDeviceToolkit.Windows.Deployments;
public static class OfficeDeploymentConfiguration
{
 public static string Create(DeploymentProfile profile,string stagedMediaRoot)
 {
  DeploymentProfiles.Validate(profile);if(profile.Format!=DeploymentFormat.OfficeOdt)throw new ArgumentException("An approved ODT profile is required.");
  var o=profile.Office!;
  // Fixed configuration only. No arbitrary XML, PIDKEY, Remove, tenant IDs, forced app closure or activation.
  return new XDocument(new XElement("Configuration",new XElement("Add",new XAttribute("SourcePath",stagedMediaRoot),new XAttribute("OfficeClientEdition","64"),new XAttribute("Channel",o.Channel),new XAttribute("Version",profile.Version),new XAttribute("AllowCdnFallback","FALSE"),new XElement("Product",new XAttribute("ID",o.ProductId),new XElement("Language",new XAttribute("ID",o.Language)))),new XElement("Display",new XAttribute("Level","None"),new XAttribute("AcceptEULA","TRUE")),new XElement("Property",new XAttribute("Name","AUTOACTIVATE"),new XAttribute("Value","0")),new XElement("Property",new XAttribute("Name","FORCEAPPSHUTDOWN"),new XAttribute("Value","FALSE")))).ToString(SaveOptions.DisableFormatting);
 }
}
