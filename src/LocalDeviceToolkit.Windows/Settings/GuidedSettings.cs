using System.Diagnostics;
namespace LocalDeviceToolkit.Windows.Settings;
public static class GuidedSettings
{
 public static IReadOnlyDictionary<string,string> Destinations{get;}=new Dictionary<string,string>
 {
  ["Display and scaling"]="ms-settings:display",["Audio"]="ms-settings:sound",["Accessibility"]="ms-settings:easeofaccess",["Default apps"]="ms-settings:defaultapps",["Startup apps"]="ms-settings:startupapps",["Notifications"]="ms-settings:notifications",["Storage Sense"]="ms-settings:storagepolicies",["Work account"]="ms-settings:workplace"
 };
 public static void Open(string id){if(!Destinations.TryGetValue(id,out var uri))throw new ArgumentException("Choose a registered Settings destination.");Process.Start(new ProcessStartInfo(uri){UseShellExecute=true});}
}
