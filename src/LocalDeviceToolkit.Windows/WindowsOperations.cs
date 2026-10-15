using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Planning;
namespace LocalDeviceToolkit.Windows;
public static class WindowsOperations
{
 public static OperationRegistry Create(WorkflowCatalogue workflows)=>new(workflows,LocalDeviceToolkit.Engine.Settings.FixedSettingOperation.Definitions.Select(d=>new LocalDeviceToolkit.Engine.Settings.FixedSettingOperation(d,new Settings.WindowsSettingsBackend())));
}
