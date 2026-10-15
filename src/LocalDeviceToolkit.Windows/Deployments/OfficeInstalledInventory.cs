using LocalDeviceToolkit.Engine.Deployments;
using Microsoft.Win32;
namespace LocalDeviceToolkit.Windows.Deployments;
public sealed record OfficeInventoryState(string? ProductReleaseIds,string? Version,string? Platform,string? Culture,string? UpdateChannel);
public static class OfficeInstalledInventory
{
 public const string MonthlyEnterpriseChannelId="55336b82-a18d-4dd6-b5f6-9e5095c314a6";
 public static DeploymentInventory Read(DeploymentProfile profile)
 {
  using var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);using var key=machine.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration",false);
  if(key is null)return new(true,false,null,null,"Machine Click-to-Run configuration is absent.");
  return Evaluate(profile,new(key.GetValue("ProductReleaseIds") as string,key.GetValue("VersionToReport") as string,key.GetValue("Platform") as string,key.GetValue("ClientCulture") as string,key.GetValue("UpdateChannel") as string));
 }
 public static DeploymentInventory Evaluate(DeploymentProfile profile,OfficeInventoryState state)
 {
  if(profile.Format!=DeploymentFormat.OfficeOdt||profile.Office is null)throw new ArgumentException("An approved Office profile is required.");
  if(string.IsNullOrWhiteSpace(state.ProductReleaseIds)||string.IsNullOrWhiteSpace(state.Version)||state.Platform is null||state.Culture is null||state.UpdateChannel is null)return new(false,false,null,null,"Machine Click-to-Run identity/version/platform/culture/update-channel evidence is incomplete.");
  bool channel=Uri.TryCreate(state.UpdateChannel,UriKind.Absolute,out var uri)&&uri.Host.Equals("officecdn.microsoft.com",StringComparison.OrdinalIgnoreCase)&&uri.AbsolutePath.TrimEnd('/').EndsWith("/"+MonthlyEnterpriseChannelId,StringComparison.OrdinalIgnoreCase);
  if(state.Platform!="x64"||!state.Culture.Equals("en-gb",StringComparison.OrdinalIgnoreCase)||!channel)return new(false,false,null,null,"Fresh machine Click-to-Run state does not confirm the approved x64/en-gb/MonthlyEnterprise configuration.");
  var products=state.ProductReleaseIds.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
  return new(true,products.Contains(profile.Office.ProductId,StringComparer.Ordinal),products.Contains(profile.Office.ProductId,StringComparer.Ordinal)?profile.Office.ProductId:state.ProductReleaseIds,state.Version,"Fresh HKLM 64-bit Click-to-Run ProductReleaseIds, VersionToReport, Platform, ClientCulture and UpdateChannel. Installation does not prove subscription activation or licensing rights.");
 }
}
