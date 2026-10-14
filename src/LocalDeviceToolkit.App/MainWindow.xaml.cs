using System.Windows;
using System.Windows.Controls;
using System.IO;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Workbench;
using LocalDeviceToolkit.Engine.Evidence;
using Microsoft.Win32;
using TaskStatus=LocalDeviceToolkit.Engine.Workbench.TaskStatus;
namespace LocalDeviceToolkit.App;
public partial class MainWindow:Window
{
 readonly WorkbenchSession session;readonly IEvidenceStore evidence;readonly CancellationTokenSource lifetime=new();bool rendering,isBusy;
 public MainWindow(WorkbenchSession session,IEvidenceStore evidence)
 {
  InitializeComponent();this.session=session;this.evidence=evidence;
  DeviceHeader.Text=$"{session.Context.ComputerName}  •  {session.Context.OriginalUserName}  •  Windows {session.Context.OsVersion} ({session.Context.Architecture})";
  StandardHeader.Text=$"Build standard {session.Catalogue.Identity.Version}  •  30 controls";
  SessionNotice.Text=session.Context.IsElevated?"This session is elevated. User tasks still target this Windows user.":"Running as the current Windows user. Administrator access is requested only for an approved action.";
  Closing+=(_,_)=>lifetime.Cancel();Render();
 }
 void Navigate_Click(object sender,RoutedEventArgs e){if(sender is Button { Tag:string area }){session.Navigate(area);Search.Text="";Render();}}
 void Queue_Click(object sender,RoutedEventArgs e){session.Navigate("deployments");Render();}
 void Render()
 {
  rendering=true;
  try
  {
   HomePanel.Visibility=session.Area=="home"?Visibility.Visible:Visibility.Collapsed;
   WorkflowPanel.Visibility=session.StageId is not null?Visibility.Visible:Visibility.Collapsed;
   DeploymentPanel.Visibility=session.Area=="deployments"?Visibility.Visible:Visibility.Collapsed;
   ReportPanel.Visibility=session.Area=="reports"?Visibility.Visible:Visibility.Collapsed;
   PageTitle.Text=session.Area switch {"setup"=>"New device setup","diagnostics"=>"Diagnostics","deployments"=>"Deployments","reports"=>"Reports & history",_=>"Your device workspace"};
   PageDescription.Text=session.Area switch
   {
    "setup"=>"Work through readiness, the build standard, Windows configuration and applications. Finish with fresh handover evidence.",
    "diagnostics"=>"Collect read-only evidence, investigate differences and review a fix when a supported action is available.",
    "deployments"=>"Review local changes and approved application deployments before execution.",
    "reports"=>"Use recorded evidence for handover and support. Capture time and limitations remain visible.",
    _=>"Set up a Windows device or investigate an existing one with shared tools and evidence."
   };
   if(session.StageId is not null)
   {
    var flow=session.Workflows.Workflows.Single(w=>w.Id==session.Area);Stages.ItemsSource=flow.Stages;Stages.SelectedItem=flow.Stages.Single(s=>s.Id==session.StageId);
   }
   RefreshTasks();QueueShortcut.Content=$"Deployment queue ({session.Queue.Requests.Count})";
   QueueList.ItemsSource=session.Queue.Requests.Select(r=>$"{session.Workflows.ResolveTask(r.TaskId).Title}\n{r.OperationId} • {r.Origin.WorkflowId} / {r.Origin.StageId}").DefaultIfEmpty("No changes queued. Select a supported task and review its fix to add a change.").ToArray();
  }
  finally{rendering=false;}
 }
 void Stage_Changed(object sender,SelectionChangedEventArgs e){if(rendering||session is null)return;if(Stages.SelectedItem is WorkflowStage stage){session.Navigate(session.Area,stage.Id);RefreshTasks();}}
 void Search_Changed(object sender,TextChangedEventArgs e){if(session is not null)RefreshTasks();}
 void RefreshTasks()
 {
  var selected=(TaskList.SelectedItem as TaskStatus)?.Definition.Id;
  TaskList.ItemsSource=session.Tasks(Search.Text);TaskList.SelectedItem=TaskList.Items.Cast<TaskStatus>().FirstOrDefault(t=>t.Definition.Id==selected)??TaskList.Items.Cast<TaskStatus>().FirstOrDefault();ShowTask();
 }
 void Task_Changed(object sender,SelectionChangedEventArgs e){if(session is not null)ShowTask();}
 void ShowTask()
 {
  var selected=TaskList.SelectedItem as TaskStatus;CheckSelected.IsEnabled=selected is not null&&!isBusy;CheckStage.IsEnabled=!isBusy;
  ReviewFix.IsEnabled=selected?.Definition.OperationId is not null&&!isBusy;
  if(selected is null){TaskTitle.Text="Select a task";TaskIds.Text=TaskRequirement.Text=TaskLimitations.Text=Observations.Text=ObservedAt.Text="";return;}
  TaskTitle.Text=selected.Definition.Title;TaskIds.Text=string.Join(" • ",selected.Definition.ControlIds);
  var controls=session.Catalogue.Controls.Where(c=>selected.Definition.ControlIds.Contains(c.Id)).ToArray();
  TaskRequirement.Text=string.Join("\n\n",controls.Select(c=>c.ExpectedDescription));
  TaskLimitations.Text=string.Join("\n\n",controls.Select(c=>$"{c.Id} • {c.Coverage}\n{c.ManualInstructions}"));
  ObservedAt.Text=selected.CapturedAt is DateTimeOffset at?$"Evidence collected {at.ToLocalTime():g}. Recheck before handover or changing settings.":"No evidence collected for this task.";
  var observations=session.LatestObservations(selected.Definition);
  Observations.Text=string.Join("\n\n",observations.Select(o=>$"{o.ControlId} / {o.CheckId}\n{o.State}: {o.Value?.GetRawText()??"No measured value"}\n{o.Reason}\nSource: {o.Source}"));
 }
 async void CheckSelected_Click(object sender,RoutedEventArgs e){if(TaskList.SelectedItem is TaskStatus task)await RunUiAsync(async()=>{await session.CaptureAsync([task.Definition.Id],lifetime.Token);StatusLine.Text="Check complete. Evidence saved; review the observed values and limitations.";RefreshTasks();});}
 async void CheckStage_Click(object sender,RoutedEventArgs e)=>await RunUiAsync(async()=>{var ids=session.Tasks().Select(t=>t.Definition.Id).ToArray();await session.CaptureAsync(ids,lifetime.Token);StatusLine.Text="Stage checks complete. Evidence saved; manual verification remains visible.";RefreshTasks();});
 void ReviewFix_Click(object sender,RoutedEventArgs e){StatusLine.Text="Select a supported action and review its exact parameters before adding it to the queue.";}
 async void History_Click(object sender,RoutedEventArgs e)=>await RunUiAsync(async()=>
 {
  var page=await evidence.ReadHistoryAsync(null,lifetime.Token);
  HistoryText.Text=string.Join("\n\n",page.Entries.OrderByDescending(r=>r.RecordedAt).Select(r=>$"{r.RecordedAt.ToLocalTime():g} • {r.Kind}\n{r.Context.ComputerName} • {r.Context.OriginalUserName}\nRecord {r.Id:D}\nSHA-256 {r.Digest}"));
  if(page.Diagnostics.Count>0)HistoryText.Text+="\n\nHistory notices\n"+string.Join("\n",page.Diagnostics.Select(d=>$"{d.FileName}: {d.Code} — {d.Message}"));
  if(page.NextCursor is not null)HistoryText.Text+="\n\nMore records are available in the evidence directory.";
  StatusLine.Text=$"Loaded {page.Entries.Count} verified history records.";
 });
 async void Export_Click(object sender,RoutedEventArgs e)=>await RunUiAsync(async()=>
 {
  var capture=session.Captures.LastOrDefault()??throw new InvalidOperationException("Run a check before exporting capture evidence.");
  var record=await evidence.LoadAsync(capture.Id,lifetime.Token);
  var dialog=new SaveFileDialog { Title="Export recorded capture evidence",Filter="JSON evidence (*.json)|*.json",FileName=$"{capture.Context.ComputerName}-{capture.CapturedAt:yyyyMMdd-HHmmss}-{capture.Id:N}.json",OverwritePrompt=true };
  if(dialog.ShowDialog(this)!=true)return;
  var temporary=dialog.FileName+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{await File.WriteAllBytesAsync(temporary,StrictJson.Bytes(record),lifetime.Token);File.Move(temporary,dialog.FileName,true);}
  finally{if(File.Exists(temporary))File.Delete(temporary);}
  StatusLine.Text="Capture evidence exported with its identity, timestamp, limitations and digest.";
 });
 async Task RunUiAsync(Func<Task> action)
 {
  if(isBusy)return;isBusy=true;Busy.Visibility=Visibility.Visible;ShowTask();
  try{await action();}
  catch(OperationCanceledException){StatusLine.Text="The check was cancelled. No successful result was inferred.";}
  catch(Exception error){StatusLine.Text="The request did not complete: "+error.Message;MessageBox.Show(this,error.Message,"Request did not complete",MessageBoxButton.OK,MessageBoxImage.Warning);}
  finally{isBusy=false;Busy.Visibility=Visibility.Collapsed;ShowTask();}
 }
}
