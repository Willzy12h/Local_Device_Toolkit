using System.Windows;
using System.Windows.Controls;
using LocalDeviceToolkit.Engine.Settings;
namespace LocalDeviceToolkit.App;
public sealed class PowerTimeoutInputWindow:Window
{
 public PowerTimeoutInput Parameters{get;private set;}=null!;
 public PowerTimeoutInputWindow(string title)
 {
  Title=title;Width=500;Height=350;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new StackPanel{Margin=new Thickness(22)};Content=panel;panel.Children.Add(new TextBlock{Text="Change this timeout only. Keep the active scheme and every other timeout. Run as the original engineer, including after UAC.",TextWrapping=TextWrapping.Wrap});
  var minutes=new TextBox{Text="",Margin=new Thickness(0,8,0,8)};panel.Children.Add(new Label{Content="Whole minutes (1–1440), or 0 for Never",Target=minutes});panel.Children.Add(minutes);
  var confirm=new CheckBox{Content="I reviewed client and management power policy.",Margin=new Thickness(0,10,0,10)};panel.Children.Add(confirm);var error=new TextBlock{TextWrapping=TextWrapping.Wrap};panel.Children.Add(error);
  var save=new Button{Content="Use this timeout",HorizontalAlignment=HorizontalAlignment.Left};panel.Children.Add(save);save.Click+=(_,_)=>{if(!int.TryParse(minutes.Text,out var n)||n is <0 or >1440||confirm.IsChecked!=true){error.Text="Choose 0–1440 whole minutes and confirm policy review.";return;}Parameters=new(n,true);DialogResult=true;};
 }
}
