using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Reporting;
using LocalDeviceToolkit.Engine.Workbench;
using LocalDeviceToolkit.Windows.Inspection;
using LocalDeviceToolkit.Windows.Settings;
namespace LocalDeviceToolkit.App;
public sealed class HandoverWindow:Window
{
 public HandoverWindow(WorkbenchSession session,IEvidenceStore evidence,bool hasUnresolvedActions=false)
 {
  Title="Engineer handover";Width=900;Height=760;MinWidth=650;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new StackPanel{Margin=new Thickness(22)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  panel.Children.Add(new TextBlock{Text="Manual checks apply to the original engineer and the specific target tested. Never enter recovery keys, passwords or tokens.",TextWrapping=TextWrapping.Wrap});
  var client=new TextBox();panel.Children.Add(new Label{Content="Client label (optional)",Target=client});panel.Children.Add(client);var ticket=new TextBox();panel.Children.Add(new Label{Content="Ticket reference (optional)",Target=ticket});panel.Children.Add(ticket);
  var summary=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MinHeight=100,MaxHeight=240,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  var saved=new List<ManualCheck>();var items=new List<CheckBox>();var targets=new Dictionary<string,TextBox>();
  ClientChecklist? loadedProfile=null;
  foreach(var pair in HandoverService.ManualItems)
  {
   var row=new StackPanel{Margin=new Thickness(0,12,0,0)};var required=new CheckBox{Content=pair.Value,Tag=pair.Key};items.Add(required);row.Children.Add(required);
   var target=new TextBox{ToolTip="Exact nonsecret target tested (device, printer, account reference or protector reference)"};row.Children.Add(new Label{Content="Target tested (nonsecret reference)",Target=target});row.Children.Add(target);targets[pair.Key]=target;
   var outcome=new ComboBox{ItemsSource=Enum.GetValues<ManualReviewOutcome>(),SelectedIndex=-1};row.Children.Add(new Label{Content="Test result (starts unverified)",Target=outcome});row.Children.Add(outcome);
   var note=new TextBox();row.Children.Add(new Label{Content="Result and scope note",Target=note});row.Children.Add(note);
   var record=new Button{Content="Record this test",HorizontalAlignment=HorizontalAlignment.Left};row.Children.Add(record);panel.Children.Add(row);
   record.Click+=async(_,_)=>{try
   {
    if(string.IsNullOrWhiteSpace(target.Text)||target.Text.Length>256||string.IsNullOrWhiteSpace(note.Text)||note.Text.Length>2000||outcome.SelectedItem is not ManualReviewOutcome result)throw new ArgumentException("Enter the tested target, result and scope note.");
    var actor=await new WindowsContextProvider().GetActorAsync(default);var check=new ManualCheck(Guid.NewGuid(),pair.Key,CanonicalJson.Digest(new{Key=pair.Key,Target=target.Text,session.Context.DeviceId,session.Context.OriginalUserSid}),session.Context,result,note.Text,actor,DateTimeOffset.UtcNow){TargetReference=target.Text};
    await evidence.SaveNewAsync(EvidenceRecord.Create(check.Id,"manual-check.v2",session.Context,check.At,StrictJson.Value(check)),default);saved.Add(check);record.Content="Recorded "+check.At.ToLocalTime().ToString("g");
   }catch(Exception error){summary.Text="Not recorded: "+error.Message;}};
  }
  var load=new Button{Content="Load client checklist (expectations only)",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,8)};panel.Children.Add(load);
  load.Click+=async(_,_)=>{try{var picker=new Microsoft.Win32.OpenFileDialog{Filter="Client checklist JSON|*.json",Title="Load requirements without selecting tasks or granting execution permission"};if(picker.ShowDialog(this)!=true)return;if(new System.IO.FileInfo(picker.FileName).Length>65536)throw new ArgumentException("Checklist exceeds 64 KiB.");var profile=StrictJson.Read<ClientChecklist>(await System.IO.File.ReadAllBytesAsync(picker.FileName),65536);ReferenceProfiles.Validate(profile);loadedProfile=profile;summary.Text="Loaded "+profile.Label+" version "+profile.Version+". Required: "+string.Join(", ",profile.RequiredItems)+". No tasks were selected or run. Manual tests still need target-specific evidence.";}catch(Exception e){summary.Text="Checklist not loaded: "+e.Message;}};
  var evaluate=new Button{Content="Review selected handover requirements",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,16,0,8)};panel.Children.Add(evaluate);panel.Children.Add(summary);
  evaluate.Click+=(_,_)=>{var profile=loadedProfile??new ClientChecklist("session",1,string.IsNullOrWhiteSpace(client.Text)?"Client":client.Text.Length<=256?client.Text:"Client",new Dictionary<string,JsonElement>(),items.Where(c=>c.IsChecked==true).Select(c=>(string)c.Tag).ToArray(),false);var result=HandoverService.Evaluate(profile,session.Context,session.Captures.SelectMany(c=>c.Observations).ToArray(),saved,DateTimeOffset.UtcNow,targets.ToDictionary(p=>p.Key,p=>CanonicalJson.Digest(new{Key=p.Key,Target=p.Value.Text,session.Context.DeviceId,session.Context.OriginalUserSid})),hasUnresolvedActions||session.HasUnresolvedWork,LocalProbeCatalogue.All.ToDictionary(d=>d.Id,d=>LocalProbeCatalogue.TargetDigest(d.Id,session.Inputs(d.Id),session.Context)),session.UnresolvedWork);summary.Text="Manual checklist only; complete local checks and unresolved changes separately.\n"+result.State+"\nOutstanding:"+(result.Outstanding.Count==0?" none":string.Concat(result.Outstanding.Select(o=>"\n• "+o)))+"\nExceptions: "+string.Join("; ",result.Exceptions)+"\nClient: "+client.Text+"\nTicket: "+ticket.Text+"\n\nRecorded manual evidence:\n"+string.Join("\n",saved.Select(m=>m.ItemId+"; target="+m.TargetReference+"; "+m.Outcome+"; "+m.Actor.UserName+"; "+m.At.ToString("O")+"; "+m.Note));};
  var copy=new Button{Content="Copy displayed handover summary",HorizontalAlignment=HorizontalAlignment.Left};copy.Click+=(_,_)=>Clipboard.SetText(summary.Text);panel.Children.Add(copy);
  panel.Children.Add(new TextBlock{Text="Open Windows Settings — opening a page does not verify a change.",Margin=new Thickness(0,20,0,8)});
  var settings=new WrapPanel();panel.Children.Add(settings);
  foreach(var destination in GuidedSettings.Destinations.Keys)
  {
   var button=new Button{Content="Open "+destination,Margin=new Thickness(4)};settings.Children.Add(button);button.Click+=async(_,_)=>
   {try{await using var log=await LocalDeviceToolkit.Engine.Logging.RunTextLog.CreateAsync(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDeviceToolkit","logs"),session.Context,TimeProvider.System,default);await log.AppendAsync("guidance","Selected Settings page: "+destination+". This is a launch, not verified configuration.",default);GuidedSettings.Open(destination);await log.AppendAsync("guidance","Launch requested; no verification claimed.",default);summary.Text="Settings launch logged: "+log.FilePath;}catch(Exception error){summary.Text="Settings launch not confirmed: "+error.Message;}};
  }
 }
}
