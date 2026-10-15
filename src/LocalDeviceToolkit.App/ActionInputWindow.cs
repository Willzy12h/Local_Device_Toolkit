using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Settings;
using System.Windows;
using System.Windows.Controls;
namespace LocalDeviceToolkit.App;
public sealed class ActionInputWindow:Window
{
 public ActionInputWindow(TaskDefinition task,DeviceContext context)
 {
  Title="Review local change";Width=620;Height=470;MinWidth=480;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontSize=14;
  var definition=FixedSettingOperation.Definitions.SingleOrDefault(d=>d.OperationId==task.OperationId);
  if(definition is null&&task.OperationId is not ("settings.uk-region-keyboard" or "apps.remove-consumer-copilot"))throw new InvalidOperationException("This operation requires its registered input form.");
  var panel=new StackPanel {Margin=new Thickness(24)};Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  panel.Children.Add(new TextBlock {Text=task.Title,FontSize=24,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(new TextBlock {Text=$"Target: {context.ComputerName}\nOriginal engineer: {context.OriginalUserName}\nScope: {definition?.Scope??OperationScope.CurrentUser}\nOperation: {task.OperationId}",Margin=new Thickness(0,16,0,16),TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(new TextBlock {Text=definition is null?(task.OperationId=="settings.uk-region-keyboard"?"Set UK formats, home region and default keyboard for the original engineer. Preserve existing languages, keyboards, spelling/handwriting preferences and language order. Do not change other profiles or the system locale.":"Remove only the exact Microsoft consumer Copilot app for the original engineer. Microsoft 365/Office and other users are not targets. There is no automatic undo or reinstall."):definition.Setting switch {SettingId.ShowFileExtensions=>"Show file extensions for this Windows user. Open a new Explorer window to verify the experience.",SettingId.LongPaths=>"Enable the Windows long-path setting. Application support and restart requirements still need verification.",SettingId.UkTimeZone=>"Set GMT Standard Time while preserving the existing daylight-saving choice. Use your assigned time-zone right when available. Automatic time-zone configuration or observed management policy blocks this change.",SettingId.DisableFastStartup=>"Disable fast startup. The toolkit will not restart the device.",_=>"Set AC sleep to Never in the active power scheme. Battery sleep and display timeout are preserved."},TextWrapping=TextWrapping.Wrap});
  var confirmed=new CheckBox {Content=new TextBlock {Text="I reviewed effective GPO/MDM policy and confirmed this local change is appropriate for this device.",TextWrapping=TextWrapping.Wrap},Margin=new Thickness(0,20,0,16)};
  panel.Children.Add(confirmed);
  var buttons=new WrapPanel();var add=new Button {Content="Add to shared queue",IsEnabled=false};var cancel=new Button {Content="Cancel",IsCancel=true};confirmed.Checked+=(_,_)=>add.IsEnabled=true;confirmed.Unchecked+=(_,_)=>add.IsEnabled=false;add.Click+=(_,_)=>DialogResult=true;buttons.Children.Add(add);buttons.Children.Add(cancel);panel.Children.Add(buttons);
 }
 public SettingParameters Parameters=>new(true);
}
public sealed class PlanReviewWindow:Window
{
 readonly System.Windows.Threading.DispatcherTimer timer;
 public PlanReviewWindow(ReviewedPlan plan)
 {
  Title="Approve local deployment plan";Width=760;Height=680;MinWidth=560;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontSize=14;
  var panel=new DockPanel {Margin=new Thickness(24)};Content=panel;
  var controls=new StackPanel();DockPanel.SetDock(controls,Dock.Bottom);panel.Children.Add(controls);
  var expiry=new TextBlock {Margin=new Thickness(0,12,0,12)};controls.Children.Add(expiry);
  var buttons=new WrapPanel();var apply=new Button {Content="Apply reviewed plan"};var cancel=new Button {Content="Cancel",IsCancel=true};apply.Click+=(_,_)=>DialogResult=true;buttons.Children.Add(apply);buttons.Children.Add(cancel);controls.Children.Add(buttons);
  var text=new TextBox {IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Text=$"Device: {plan.Context.ComputerName}\nOriginal engineer: {plan.Context.OriginalUserName}\nSID: {plan.Context.OriginalUserSid}\nStandard: {plan.Catalogue.Version}\nPlan: {plan.Id:D}\n\n"+string.Join("\n\n",plan.Actions.Select(a=>$"{a.Request.OperationId}\nScope: {a.Readiness.Scope} • Required privilege: {a.Readiness.Privilege}\nParameters: {a.Request.Parameters.GetRawText()}\nBefore: {string.Join("; ",a.Before.Select(o=>o.CheckId+" = "+o.Value?.GetRawText()))}\n{a.Readiness.Reason}"))+"\n\nMachine actions use a separate administrator review. The executing administrator is recorded separately. No automatic restart. Any uncertain result stops the remaining queue."};
  System.Windows.Automation.AutomationProperties.SetName(text,"Plan targets, scope, privilege, parameters and before values");panel.Children.Add(text);
  void Update(){var remaining=plan.ExpiresAt-DateTimeOffset.UtcNow;apply.IsEnabled=remaining>TimeSpan.Zero;expiry.Text=remaining>TimeSpan.Zero?$"Plan expires in {remaining.Minutes:00}:{remaining.Seconds:00}. Before values and policy are checked again before each action.":"This plan expired. Cancel and review a fresh plan.";}
  timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,_)=>Update();Closed+=(_,_)=>timer.Stop();Update();timer.Start();
 }
}
