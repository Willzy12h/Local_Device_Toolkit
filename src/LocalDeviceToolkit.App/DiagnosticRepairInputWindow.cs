using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Diagnostics;
namespace LocalDeviceToolkit.App;
public sealed class DiagnosticRepairInputWindow:Window
{
 public JsonElement Parameters{get;private set;}
 public DiagnosticRepairInputWindow(DiagnosticRepairKind kind,Func<bool,Task<string>> reconcile)
 {
  Title=kind==DiagnosticRepairKind.DnsCache?"Review DNS cache refresh":"Review Defender signature request";Width=650;Height=580;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new StackPanel{Margin=new Thickness(22)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};var error=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MinHeight=80,MaxHeight=200,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  var host=new TextBox();var address=new TextBox();var hypothesis=new ComboBox{ItemsSource=new[]{"configuration-change","negative-cache-after-record-created"},SelectedIndex=-1};
  var confirm=new CheckBox{Margin=new Thickness(0,12,0,12)};var evidence=new CheckBox{Content="I independently verified the expected address and stale-cache hypothesis."};
  if(kind==DiagnosticRepairKind.DnsCache)
  {
   panel.Children.Add(new TextBlock{Text="Clears the whole device DNS client cache once. No exact restoration. Round-robin differences alone do not establish stale cache. The selected hostname is queried through the system resolver after clearing; network access may occur.",TextWrapping=TextWrapping.Wrap});
   foreach(var field in new[]{("Hostname",(Control)host),("Externally verified expected IP address",(Control)address),("Supported hypothesis",(Control)hypothesis)}){panel.Children.Add(new Label{Content=field.Item1,Target=field.Item2});panel.Children.Add(field.Item2);}
   confirm.Content="I approve the whole-device DNS client cache effect.";panel.Children.Add(evidence);
  }
  else
  {
   panel.Children.Add(new TextBlock{Text="Requests signature updates using configured/default Windows Defender sources. May contact your configured network sources. Does not enable Defender or change source policy. A completed request does not prove that newer signatures existed or that the original symptom is resolved.",TextWrapping=TextWrapping.Wrap});confirm.Content="I approve network access to the configured/default signature sources.";
   var read=new Button{Content="Read unresolved request state (administrator)",HorizontalAlignment=HorizontalAlignment.Left};var resolve=new Button{Content="Review completion and resolve guard (administrator)",HorizontalAlignment=HorizontalAlignment.Left};
   var completion=new CheckBox{Content="I reviewed vendor completion and confirmed no update remains in progress.",Margin=new Thickness(0,10,0,10)};
   panel.Children.Add(read);panel.Children.Add(completion);panel.Children.Add(resolve);
   read.Click+=async(_,_)=>{try{error.Text=await reconcile(false);}catch(Exception e){error.Text=e.Message;}};
   resolve.Click+=async(_,_)=>{if(completion.IsChecked!=true){error.Text="Review completion before resolving the guard.";return;}try{error.Text=await reconcile(true);}catch(Exception e){error.Text=e.Message;}};
  }
  panel.Children.Add(confirm);var save=new Button{Content="Use these reviewed inputs",HorizontalAlignment=HorizontalAlignment.Left};panel.Children.Add(save);panel.Children.Add(error);
  save.Click+=(_,_)=>{try{Parameters=kind==DiagnosticRepairKind.DnsCache?StrictJson.Value(new DnsCacheInput(host.Text.Trim(),address.Text.Trim(),hypothesis.SelectedItem as string??"",confirm.IsChecked==true,evidence.IsChecked==true)):StrictJson.Value(new DefenderSignatureInput(confirm.IsChecked==true));if(confirm.IsChecked!=true||kind==DiagnosticRepairKind.DnsCache&&(evidence.IsChecked!=true||hypothesis.SelectedIndex<0||!System.Net.IPAddress.TryParse(address.Text,out _)))throw new ArgumentException("Choose inputs and confirm each disclosed effect.");DialogResult=true;}catch(Exception e) when(e is ArgumentException){error.Text=e.Message;}};
 }
}
