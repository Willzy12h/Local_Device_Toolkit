using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Settings;
using System.Windows;
using System.Windows.Controls;
namespace LocalDeviceToolkit.App;
public sealed class ActionInputWindow:Window
{
 public ActionInputWindow(TaskDefinition task,DeviceContext context)
 {
  Title="Setting details";Width=620;Height=470;MinWidth=480;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontSize=14;
  var definition=FixedSettingOperation.Definitions.SingleOrDefault(d=>d.OperationId==task.OperationId);
  if(definition is null&&task.OperationId is not ("settings.uk-region-keyboard" or "apps.remove-consumer-copilot"))throw new InvalidOperationException("This operation requires its registered input form.");
  var panel=new StackPanel {Margin=new Thickness(24)};Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  panel.Children.Add(new TextBlock {Text=task.Title,FontSize=24,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(new TextBlock {Text=$"Target: {context.ComputerName}\nOriginal engineer: {context.OriginalUserName}\nScope: {definition?.Scope??OperationScope.CurrentUser}\nOperation: {task.OperationId}",Margin=new Thickness(0,16,0,16),TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(new TextBlock {Text=definition is null?(task.OperationId=="settings.uk-region-keyboard"?"Set UK formats, home region and default keyboard for the original engineer. Preserve existing languages, keyboards, spelling/handwriting preferences and language order. Do not change other profiles or the system locale.":"Remove only the exact Microsoft consumer Copilot app for the original engineer. Microsoft 365/Office and other users are not targets. There is no automatic undo or reinstall."):definition.Setting switch {SettingId.ShowFileExtensions=>"Show file extensions for this Windows user. Open a new Explorer window to verify the experience.",SettingId.LongPaths=>"Enable the Windows long-path setting. Application support and restart requirements still need verification.",SettingId.UkTimeZone=>"Set GMT Standard Time while preserving the existing daylight-saving choice. Use your assigned time-zone right when available. Automatic time-zone configuration or observed management policy blocks this change.",SettingId.DisableFastStartup=>"Disable fast startup. The toolkit will not restart the device.",_=>"Set AC sleep to Never in the active power scheme. Battery sleep and display timeout are preserved."},TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(new TextBlock {Text="Select this setting in the task table. Request a change there, then check and review the current and expected values together.",Margin=new Thickness(0,20,0,16),TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(new Button{Content="Close",IsCancel=true,HorizontalAlignment=HorizontalAlignment.Left});
 }
}
