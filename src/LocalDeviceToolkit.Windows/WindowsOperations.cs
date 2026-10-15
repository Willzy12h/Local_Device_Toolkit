using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Planning;
namespace LocalDeviceToolkit.Windows;
public static class WindowsOperations
{
 public static OperationRegistry Create(WorkflowCatalogue workflows)
 {
  var settings=LocalDeviceToolkit.Engine.Settings.FixedSettingOperation.Definitions.Select(d=>(LocalDeviceToolkit.Engine.Execution.ILocalOperation)new LocalDeviceToolkit.Engine.Settings.FixedSettingOperation(d,new Settings.WindowsSettingsBackend()));
  var users=Enum.GetValues<LocalDeviceToolkit.Engine.Settings.UserSettingId>().Select(id=>(LocalDeviceToolkit.Engine.Execution.ILocalOperation)new LocalDeviceToolkit.Engine.Settings.UserSettingOperation(id,new Settings.WindowsUserSettingsBackend(Path.Combine(AppContext.BaseDirectory,"scripts"))));
  return new(workflows,settings.Concat(users));
 }
}
