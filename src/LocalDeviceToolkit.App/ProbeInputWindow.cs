using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
namespace LocalDeviceToolkit.App;
public sealed class ProbeInputWindow:Window
{
 public JsonElement Parameters{get;private set;}
 public ProbeInputWindow(string id,JsonElement existing)
 {
  var definition=LocalProbeCatalogue.Resolve(id);Title=definition.Title+" inputs";Width=620;Height=600;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new StackPanel{Margin=new Thickness(22)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  panel.Children.Add(new TextBlock{Text=definition.Disclosure,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
  var fields=new List<(string Key,Func<JsonNode?> Read)>();
  void Text(string key,string label,string fallback="")
  {
   var value=existing.TryGetProperty(key,out var found)?found.ToString():fallback;var box=new TextBox{Text=value,Margin=new Thickness(0,0,0,12)};AutomationProperties.SetName(box,label);
   panel.Children.Add(new Label{Content=label,Target=box});panel.Children.Add(box);fields.Add((key,()=>box.Text.Length==0?null:JsonValue.Create(box.Text.Trim())));
  }
  void Choice(string key,string label,string[] values)
  {
   var box=new ComboBox{ItemsSource=values,SelectedItem=existing.TryGetProperty(key,out var found)?found.GetString():values[0],Margin=new Thickness(0,0,0,12)};AutomationProperties.SetName(box,label);
   panel.Children.Add(new Label{Content=label,Target=box});panel.Children.Add(box);fields.Add((key,()=>JsonValue.Create(box.SelectedItem as string)));
  }
  void Number(string key,string label,string fallback="")
  {
   var box=new TextBox{Text=existing.TryGetProperty(key,out var found)?found.ToString():fallback,Margin=new Thickness(0,0,0,12)};AutomationProperties.SetName(box,label);
   panel.Children.Add(new Label{Content=label,Target=box});panel.Children.Add(box);fields.Add((key,()=>box.Text.Length==0?null:JsonValue.Create(ulong.Parse(box.Text,System.Globalization.CultureInfo.InvariantCulture))));
  }
  switch(definition.InputSchema)
  {
   case "volume":Text("volumeId","Local volume (for example C:)","C:");if(id=="local.storage.capacity")Number("requiredBytes","Required free bytes (optional client target)");break;
   case "history":Number("count","Maximum history records (1–200)","20");break;
   case "adapter":Text("adapterId","Adapter GUID (optional; empty means active adapters)");break;
   case "dns":Text("hostname","Hostname to query");Choice("queryType","Record type",["A","AAAA","CNAME"]);Text("adapterId","Adapter GUID (optional context; does not force routing)");break;
   case "tcp":Text("host","Hostname or IP address");Number("port","TCP port (1–65535)");Text("adapterId","Adapter GUID (optional context; does not force routing)");break;
   case "printer":Text("printerName","Exact printer name");var remote=new CheckBox{Content="Allow this selected shared-printer read (network access)",IsChecked=existing.TryGetProperty("allowRemoteRead",out var allowed)&&allowed.ValueKind==JsonValueKind.True,Margin=new Thickness(0,10,0,16)};panel.Children.Add(remote);fields.Add(("allowRemoteRead",()=>JsonValue.Create(remote.IsChecked==true)));break;
   case "incident":Choice("symptom","Reported symptom",["network","performance","power","printing","updates","applications","crash"]);Text("fromUtc","Incident start (UTC, ISO 8601)",DateTimeOffset.UtcNow.AddHours(-2).ToString("O"));Text("toUtc","Incident end (UTC, ISO 8601)",DateTimeOffset.UtcNow.ToString("O"));break;
   case "profile":Text("profileId","Approved deployment profile ID");break;
  }
  var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};panel.Children.Add(error);
  var save=new Button{Content="Use these inputs",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,0)};panel.Children.Add(save);
  save.Click+=(_,_)=>{try{var value=new JsonObject();foreach(var field in fields){var read=field.Read();if(read is not null)value[field.Key]=read;}Parameters=StrictJson.Value(value);LocalProbeCatalogue.ValidateInput(id,Parameters);DialogResult=true;}catch(Exception e) when(e is FormatException or OverflowException or CatalogueValidationException){error.Text="Check the inputs. "+e.Message;}};
 }
}
