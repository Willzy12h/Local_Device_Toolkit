using System.Windows;
using System.Windows.Controls;
using System.IO;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Workbench;
using LocalDeviceToolkit.Engine.Evidence;
using Microsoft.Win32;
using LocalDeviceToolkit.Engine.Elevation;
using LocalDeviceToolkit.Windows.Elevation;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Windows.Deployments;
using TaskStatus=LocalDeviceToolkit.Engine.Workbench.TaskStatus;
namespace LocalDeviceToolkit.App;
public partial class MainWindow:Window
{
 readonly LocalDeviceToolkit.Engine.Reporting.ReportService reportService;readonly LocalDeviceToolkit.Engine.Reporting.ManualReviewRecorder manualReviews;
 readonly LocalDeviceToolkit.Engine.Planning.PlanService plans;readonly LocalDeviceToolkit.Engine.Execution.RunCoordinator coordinator;readonly WorkbenchSession session;readonly IEvidenceStore evidence;readonly CancellationTokenSource lifetime=new();bool rendering,isBusy;
 public MainWindow(WorkbenchSession session,IEvidenceStore evidence,LocalDeviceToolkit.Engine.Planning.PlanService plans,LocalDeviceToolkit.Engine.Execution.RunCoordinator coordinator,LocalDeviceToolkit.Engine.Reporting.ReportService reportService,LocalDeviceToolkit.Engine.Reporting.ManualReviewRecorder manualReviews)
 {
  this.reportService=reportService;this.manualReviews=manualReviews;
  InitializeComponent();Height=Math.Min(Height,SystemParameters.WorkArea.Height);Width=Math.Min(Width,SystemParameters.WorkArea.Width);this.session=session;this.evidence=evidence;this.plans=plans;this.coordinator=coordinator;
  var osLabel=!session.Context.IsWorkstation?"Windows Server":session.Context.OsVersion.Major==10&&session.Context.OsVersion.Build>=22000?"Windows 11":"Windows";
  DeviceHeader.Text=$"{session.Context.ComputerName}  •  {session.Context.OriginalUserName}  •  {osLabel} build {session.Context.OsVersion.Build} ({session.Context.Architecture})";
  StandardHeader.Text=$"Build standard {session.Catalogue.Identity.Version}  •  30 controls";
  SessionNotice.Text=session.Context.IsElevated?"This session is elevated. User tasks still target this Windows user.":"Running as the current Windows user. Administrator access is requested only for an approved action.";
  if(!LocalDeviceToolkit.Engine.Planning.PlanService.SupportedTarget(session.Context))SessionNotice.Text+=" Inventory only on this OS; local deployments require Windows 11 x64.";
  Closing+=(_,_)=>lifetime.Cancel();Render();
 }
 internal async Task RenderSmokePagesAsync(string output)
 {
  if(Directory.Exists(output)||File.Exists(output))throw new IOException("Smoke output must be a new directory.");Directory.CreateDirectory(output);
  static void Save(Window window,string path)
  {
   window.UpdateLayout();
   if(window.ActualWidth<1||window.ActualHeight<1)throw new InvalidOperationException("The actual native window has no rendered bounds.");
   var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);
   var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);encoder.Save(file);
  }
  var pages=new[]{("home",(string?)null),("setup","windows"),("diagnostics","configuration"),("deployments",(string?)null),("reports",(string?)null)};
  foreach(var (area,stage) in pages){NavigateWorkspace(area,stage);await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);Save(this,Path.Combine(output,area+".png"));}
  Width=900;Height=520;
  foreach(var (area,stage) in pages.Where(p=>p.Item1!="home"))
  {
   NavigateWorkspace(area,stage);await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);UpdateLayout();
   if(WorkspaceViewport.VerticalOffset>0)throw new InvalidOperationException("Workspace navigation did not restore the page heading.");
   if(area is "setup" or "diagnostics")
   {
    if(TaskList.ActualHeight<180)throw new InvalidOperationException("Compact workflow leaves no usable task-list height.");
    WorkspaceViewport.ScrollToEnd();await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);UpdateLayout();
    if(WorkspaceViewport.ScrollableHeight>0&&WorkspaceViewport.VerticalOffset<=0)throw new InvalidOperationException("Compact workspace content cannot be reached by scrolling.");
   }
   Save(this,Path.Combine(output,area+"-compact.png"));
  }
  Window[] dialogs=[new ReportsWindow(reportService,session,manualReviews),new DeploymentInputWindow(new ApprovedProfileStore(ApprovedProfileStore.UserCacheRoot),(_,_,_)=>throw new InvalidOperationException("Smoke rendering cannot approve media.")),new RestorationInputWindow(evidence),new ManualReviewWindow(session.Catalogue),new ActionInputWindow(session.Workflows.ResolveTask("control.cfg-win-011"),session.Context)];
  string[] names=["saved-reports","deployment-inputs","recorded-restoration","manual-review","setting-inputs"];
  for(var index=0;index<dialogs.Length;index++)
  {
   var window=dialogs[index];window.Owner=this;window.Height=Math.Min(window.Height,SystemParameters.WorkArea.Height);window.Width=Math.Min(window.Width,SystemParameters.WorkArea.Width);window.Show();
   await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);Save(window,Path.Combine(output,names[index]+".png"));window.Height=Math.Max(window.MinHeight,Math.Min(520,window.Height));await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);Save(window,Path.Combine(output,names[index]+"-compact.png"));window.Close();
  }
 }
 void NavigateWorkspace(string area,string? stage=null)
 {
  session.Navigate(area,stage);Search.Text="";Render();WorkspaceViewport.ScrollToTop();
 }
 void Navigate_Click(object sender,RoutedEventArgs e){if(sender is FrameworkElement { Tag:string area })NavigateWorkspace(area);}
 void OpenApplications_Click(object sender,RoutedEventArgs e)=>NavigateWorkspace("setup","applications");
 void OpenWindowsSettings_Click(object sender,RoutedEventArgs e)=>NavigateWorkspace("setup","windows");
 void Queue_Click(object sender,RoutedEventArgs e)=>NavigateWorkspace("deployments");
 void Render()
 {
  rendering=true;
  try
  {
   NavHome.IsChecked=session.Area=="home";NavSetup.IsChecked=session.Area=="setup";NavDiagnostics.IsChecked=session.Area=="diagnostics";NavDeployments.IsChecked=session.Area=="deployments";NavReports.IsChecked=session.Area=="reports";
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
   UpdateQueueButtons();RefreshTasks();QueueShortcut.Content=$"Deployment queue ({session.Queue.Requests.Count})";
   QueueList.ItemsSource=session.Queue.Requests.Select(r=>$"{session.Workflows.ResolveTask(r.TaskId).Title}\n{r.OperationId} • {r.Origin.WorkflowId} / {r.Origin.StageId}").DefaultIfEmpty("No changes queued. Select a supported task and review its fix to add a change.").ToArray();
  }
  finally{rendering=false;}
 }
 void Stage_Changed(object sender,SelectionChangedEventArgs e){if(rendering||session is null)return;if(Stages.SelectedItem is WorkflowStage stage){session.Navigate(session.Area,stage.Id);RefreshTasks();WorkspaceViewport.ScrollToTop();}}
 void Search_Changed(object sender,TextChangedEventArgs e){if(session is not null)RefreshTasks();}
 void RefreshTasks()
 {
  var all=session.Tasks();
  StageProgress.Text=all.Count==0?"":$"{all.Count} tasks • {all.Count(t=>t.Status=="Meets standard")} meet local checks • {all.Count(t=>t.Status=="Needs attention")} need attention • {all.Count(t=>t.Status=="Manual verification")} need manual verification • {all.Count(t=>t.Status=="Unknown")} unknown • {all.Count(t=>t.Status=="Needs administrator read")} need administrator read • {all.Count(t=>t.Status=="Not checked")} not checked";
  var selected=(TaskList.SelectedItem as TaskStatus)?.Definition.Id;
  TaskList.ItemsSource=session.Tasks(Search.Text);TaskList.SelectedItem=TaskList.Items.Cast<TaskStatus>().FirstOrDefault(t=>t.Definition.Id==selected)??TaskList.Items.Cast<TaskStatus>().FirstOrDefault();ShowTask();
 }
 void Task_Changed(object sender,SelectionChangedEventArgs e){if(session is not null)ShowTask();}
 void Reference_Click(object sender,RoutedEventArgs e)
 {
  if(TaskList.SelectedItem is not TaskStatus task)return;
  var files=LocalDeviceToolkit.Engine.Catalogue.CatalogueLoader.ShippedFiles();using var document=StrictJson.Parse(files["reference.json"]);
  var controls=document.RootElement.GetProperty("controls").EnumerateArray().Where(c=>task.Definition.ControlIds.Contains(c.GetProperty("id").GetString()!)).ToArray();
  var content=new TextBox {Text=System.Text.Json.JsonSerializer.Serialize(new { source=document.RootElement.GetProperty("source"),controls },new System.Text.Json.JsonSerializerOptions {WriteIndented=true}),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(20)};
  new Window {Title="Original M365 control reference • read only",Owner=this,Width=800,Height=680,MinWidth=500,MinHeight=400,Content=content,WindowStartupLocation=WindowStartupLocation.CenterOwner}.ShowDialog();
 }
 void ShowTask()
 {
  var selected=TaskList.SelectedItem as TaskStatus;CheckSelected.IsEnabled=selected is not null&&!isBusy;CheckStage.IsEnabled=!isBusy;
  ProtectedRead.IsEnabled=false;
  if(selected is not null&&!isBusy)
  {
   try{WorkerRequestValidator.Validate(new(1,WorkerMode.CaptureProtected,session.Context,new(selected.Definition.ControlIds,session.Origin),null,null),session.Catalogue,session.Workflows);ProtectedRead.IsEnabled=true;}
   catch(LocalDeviceToolkit.Engine.Execution.PlanValidationException){}
  }
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
 async void ProtectedRead_Click(object sender,RoutedEventArgs e)
 {
  if(TaskList.SelectedItem is not TaskStatus task)return;
  var origin=session.Origin;
  await RunUiAsync(async()=>
  {
   var request=new WorkerRequest(1,WorkerMode.CaptureProtected,session.Context,new(task.Definition.ControlIds,origin),null,null);
   WorkerRequestValidator.Validate(request,session.Catalogue,session.Workflows);
   var broker=new UacWorkerBroker(Path.Combine(AppContext.BaseDirectory,"worker","LocalDeviceToolkit.Worker.exe"));
   var response=await broker.RequestAsync(request,lifetime.Token);
   if(!response.Success||response.Capture is null)throw new InvalidOperationException(response.Message);
   await session.AcceptProtectedCaptureAsync(response.Capture,origin,lifetime.Token);RefreshTasks();StatusLine.Text="Protected evidence saved. Original engineer and executing administrator are recorded separately.";
  });
 }
 async void ReviewFix_Click(object sender,RoutedEventArgs e)
 {
  if(TaskList.SelectedItem is not TaskStatus task||task.Definition.OperationId is null||isBusy)return;
  await RunUiAsync(()=>
  {
  System.Text.Json.JsonElement parameters;
  if(task.Definition.OperationId=="settings.restore-recorded")
  {var restore=new RestorationInputWindow(evidence){Owner=this};if(restore.ShowDialog()!=true)return Task.CompletedTask;parameters=StrictJson.Value(restore.Parameters);}
  else if(task.Definition.OperationId=="packages.install")
  {var deployment=new DeploymentInputWindow(new ApprovedProfileStore(ApprovedProfileStore.UserCacheRoot),ApproveProfileAsync,reconcile:ReconcileDeploymentAsync){Owner=this};if(deployment.ShowDialog()!=true)return Task.CompletedTask;parameters=StrictJson.Value(deployment.Parameters);}
  else {var form=new ActionInputWindow(task.Definition,session.Context) {Owner=this};if(form.ShowDialog()!=true)return Task.CompletedTask;parameters=StrictJson.Value(form.Parameters);}
  var request=new ActionRequest(task.Definition.Id,task.Definition.OperationId,parameters,session.Origin);
  var added=session.Queue.Add(request);StatusLine.Text=added switch {LocalDeviceToolkit.Engine.Execution.QueueAddResult.AlreadyQueued=>"This identical change is already in the shared queue.",LocalDeviceToolkit.Engine.Execution.QueueAddResult.Added=>"Added to the shared queue. Review the complete plan before execution.",_=>"A different change already targets this setting; remove it explicitly before adding this request."};Render();return Task.CompletedTask;
  });
 }
 async Task<DeploymentProfile> ApproveProfileAsync(DeploymentProfile profile,string directory,CancellationToken ct)
 {
  var broker=new UacWorkerBroker(Path.Combine(AppContext.BaseDirectory,"worker","LocalDeviceToolkit.Worker.exe"));
  var response=await broker.RequestAsync(new(1,WorkerMode.ApproveDeploymentProfile,session.Context,null,null,null,DeploymentApproval:new(profile,directory)),ct);
  if(!response.Success||response.ApprovedProfile is null)throw new InvalidOperationException(response.Message);
  return response.ApprovedProfile;
 }
 async Task<string> ReconcileDeploymentAsync(DeploymentParameters parameters,bool resolve,CancellationToken ct)
 {
  var cached=await new ApprovedProfileStore(ApprovedProfileStore.UserCacheRoot).LoadAsync(parameters.ProfileId,ct);
  if(cached.Scope==OperationScope.CurrentUser)
  {
   var operation=await UserPackageAsync(parameters,ct);Guid? receipt=null;
   if(resolve)receipt=await operation.ResolveReviewedAsync(session.Context,StrictJson.Value(parameters),new(true,OperationScope.CurrentUser),evidence,ct);
   var observed=await operation.ReconcileAsync(session.Context,StrictJson.Value(parameters),ct);
   return "Original-user read-only reconciliation; no installer was launched.\n"+System.Text.Json.JsonSerializer.Serialize(new{observed,ResolutionReceipt=receipt},new System.Text.Json.JsonSerializerOptions(StrictJson.Options){WriteIndented=true});
  }
  var broker=new UacWorkerBroker(Path.Combine(AppContext.BaseDirectory,"worker","LocalDeviceToolkit.Worker.exe"));
  var response=await broker.RequestAsync(new(1,resolve?WorkerMode.ResolveDeployment:WorkerMode.ReconcileDeployment,session.Context,null,null,null,DeploymentReconcile:parameters,DeploymentResolutionReview:resolve?new(true):null),ct);
  if(!response.Success||response.DeploymentStatus is null)throw new InvalidOperationException(response.Message);
  if(response.DeploymentResolution is not null)await evidence.SaveNewAsync(response.DeploymentResolution,ct);
  return response.Message+"\nObserved by: "+response.Actor.UserName+" ("+response.Actor.Sid+")\n"+System.Text.Json.JsonSerializer.Serialize(new{response.DeploymentStatus,ResolutionReceipt=response.DeploymentResolution},new System.Text.Json.JsonSerializerOptions(StrictJson.Options){WriteIndented=true});
 }
 static async Task<PackageDeploymentOperation> UserPackageAsync(DeploymentParameters input,CancellationToken ct)
 {
  var profile=await ApprovedProfileAuthority.LoadAsync(input.ProfileId,ct);
  if(profile.Scope!=OperationScope.CurrentUser)throw new InvalidOperationException("The independently approved profile does not have original-user scope.");
  return new([profile],new WindowsDeploymentBackend(),new FileDeploymentPendingStore(ApprovedPackageOperation.UserPendingRoot));
 }
 void UpdateQueueButtons(){ReviewQueue.IsEnabled=RemoveQueued.IsEnabled=ClearQueue.IsEnabled=!isBusy&&session.Queue.Requests.Count>0;}
 void RemoveQueued_Click(object sender,RoutedEventArgs e){if(isBusy)return;var index=QueueList.SelectedIndex;var requests=session.Queue.Requests;if(index>=0&&index<requests.Count)session.Queue.Remove(requests[index]);Render();}
 void ClearQueue_Click(object sender,RoutedEventArgs e){if(isBusy)return;session.Queue.Clear();Render();}
 async void ReviewQueue_Click(object sender,RoutedEventArgs e)=>await RunUiAsync(async()=>
 {
  var revision=session.Queue.Revision;var plan=await plans.CreateAsync(session.Context,session.Catalogue,session.Queue.Requests,lifetime.Token);
  var review=new PlanReviewWindow(plan) {Owner=this};if(review.ShowDialog()!=true){StatusLine.Text="Plan review cancelled. No queued action was started.";return;}
  if(session.Queue.Revision!=revision)throw new InvalidOperationException("The queue changed after review. Review a fresh plan.");
  var run=await coordinator.ExecuteAsync(plan,lifetime.Token);
  RunResults.Text=$"Run {run.Id:D} • Evidence complete: {run.EvidenceComplete}\nOriginal engineer: {run.Context.OriginalUserName}\n"+string.Join("\n\n",run.Results.Select(r=>$"{r.Outcome} • {r.Reason}\nExecuting account: {r.Actor?.UserName??"Not confirmed / not started"}\n{string.Join("; ",r.After.Select(o=>o.CheckId+" = "+o.Value?.GetRawText()))}"));
  foreach(var action in plan.Actions.Where(a=>a.Request.OperationId=="packages.install"&&a.Readiness.Privilege==PrivilegeRequirement.CurrentUser&&run.Results.Any(r=>r.ActionId==a.Id)))
  {
   try{var input=StrictJson.Read<DeploymentParameters>(StrictJson.Bytes(action.Request.Parameters));await(await UserPackageAsync(input,CancellationToken.None)).AcknowledgeRecordedRunAsync(session.Context,action.Request.Parameters,run.Id,evidence,CancellationToken.None);}
   catch(Exception error) when(error is IOException or UnauthorizedAccessException or LocalDeviceToolkit.Engine.Execution.PlanValidationException or EvidenceValidationException){RunResults.Text+="\nOriginal-user deployment guard retained; use read-only reconciliation before another attempt.";}
  }
  session.Queue.Clear();Render();StatusLine.Text="Run recorded. Review each outcome; recheck the affected controls before handover. Uncertain operations are never replayed automatically.";
 });
 void History_Click(object sender,RoutedEventArgs e)
 {if(isBusy)return;new ReportsWindow(reportService,session,manualReviews){Owner=this}.ShowDialog();}
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
  if(isBusy)return;isBusy=true;Busy.Visibility=Visibility.Visible;UpdateQueueButtons();ShowTask();
  try{await action();}
  catch(OperationCanceledException){StatusLine.Text="The check was cancelled. No successful result was inferred.";}
  catch(Exception error){StatusLine.Text="The request did not complete: "+error.Message;MessageBox.Show(this,error.Message,"Request did not complete",MessageBoxButton.OK,MessageBoxImage.Warning);}
  finally{isBusy=false;Busy.Visibility=Visibility.Collapsed;UpdateQueueButtons();ShowTask();}
 }
}
