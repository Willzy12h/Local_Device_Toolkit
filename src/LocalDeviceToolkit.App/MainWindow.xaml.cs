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
 readonly ReviewRunService reviewRuns;readonly Dictionary<string,TaskSelectionRow> selections=new(StringComparer.Ordinal);
 readonly LocalDeviceToolkit.Engine.Planning.PlanService plans;readonly LocalDeviceToolkit.Engine.Execution.RunCoordinator coordinator;readonly WorkbenchSession session;readonly IEvidenceStore evidence;readonly CancellationTokenSource lifetime=new();bool rendering,isBusy;
 public MainWindow(WorkbenchSession session,IEvidenceStore evidence,LocalDeviceToolkit.Engine.Planning.PlanService plans,LocalDeviceToolkit.Engine.Execution.RunCoordinator coordinator,LocalDeviceToolkit.Engine.Reporting.ReportService reportService,LocalDeviceToolkit.Engine.Reporting.ManualReviewRecorder manualReviews,ReviewRunService reviewRuns)
 {
  this.reportService=reportService;this.manualReviews=manualReviews;this.reviewRuns=reviewRuns;
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
  foreach(var (area,stage) in pages)
  {
   NavigateWorkspace(area,stage);await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);UpdateLayout();
   if(area=="setup"&&(SetupRows.Columns.Any(column=>column.ActualWidth<column.MinWidth-1)||SetupRows.ColumnHeaderHeight>40||SetupRows.RowHeight>50))throw new InvalidOperationException("Setup column labels and row controls must retain readable native bounds.");
   Save(this,Path.Combine(output,area+".png"));
  }
  Width=900;Height=520;
  foreach(var (area,stage) in pages.Where(p=>p.Item1!="home"))
  {
   NavigateWorkspace(area,stage);await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);UpdateLayout();
   if(WorkspaceViewport.VerticalOffset>0)throw new InvalidOperationException("Workspace navigation did not restore the page heading.");
   if(area is "setup" or "diagnostics")
   {
    if((area=="setup"?SetupRows.ActualHeight:TaskList.ActualHeight)<180)throw new InvalidOperationException("Compact workflow leaves no usable task-list height.");
    WorkspaceViewport.ScrollToEnd();await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);UpdateLayout();
    if(WorkspaceViewport.ScrollableHeight>0&&WorkspaceViewport.VerticalOffset<=0)throw new InvalidOperationException("Compact workspace content cannot be reached by scrolling.");
   }
   Save(this,Path.Combine(output,area+"-compact.png"));
  }
  var smokeReview=await reviewRuns.CheckAsync([new ReviewSelection("windows.file-extensions",new("setup","windows"),false)],lifetime.Token);
  if(smokeReview.Actions.Count!=0||smokeReview.ChangesCount!=0||smokeReview.CanApply||smokeReview.Rows.Count!=1)throw new InvalidOperationException("Smoke review must remain read only with one actual checked row and no change plan.");
  if(!System.Text.RegularExpressions.Regex.IsMatch(smokeReview.LogText,@"\d{4}-\d{2}-\d{2}T")||!smokeReview.LogText.Contains("source=",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Read-only smoke log must record dated checks and actual sources.");
  try
  {
  Window[] dialogs=[new ReportsWindow(reportService,session,manualReviews),new DeploymentInputWindow(new ApprovedProfileStore(ApprovedProfileStore.UserCacheRoot),(_,_,_)=>throw new InvalidOperationException("Smoke rendering cannot approve media.")),new RestorationInputWindow(evidence),new ManualReviewWindow(session.Catalogue),new ActionInputWindow(session.Workflows.ResolveTask("control.cfg-win-011"),session.Context),new ReviewRunWindow(smokeReview)];
  string[] names=["saved-reports","deployment-inputs","recorded-restoration","manual-review","setting-inputs","task-review"];
  for(var index=0;index<dialogs.Length;index++)
  {
   var window=dialogs[index];window.Owner=this;window.Height=Math.Min(window.Height,SystemParameters.WorkArea.Height);window.Width=Math.Min(window.Width,SystemParameters.WorkArea.Width);window.Show();
   await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);Save(window,Path.Combine(output,names[index]+".png"));window.Height=Math.Max(window.MinHeight,Math.Min(520,window.Height));await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);Save(window,Path.Combine(output,names[index]+"-compact.png"));window.Close();
  }
  }
  finally{await reviewRuns.CancelAsync(smokeReview,CancellationToken.None);}
  await using(var original=new FileStream(smokeReview.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read))
  await using(var artifact=new FileStream(Path.Combine(output,"read-only-run.log"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
   await original.CopyToAsync(artifact,lifetime.Token);
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
   SetupRows.Visibility=session.Area=="setup"?Visibility.Visible:Visibility.Collapsed;DiagnosticLayout.Visibility=session.Area=="diagnostics"?Visibility.Visible:Visibility.Collapsed;
   DeploymentPanel.Visibility=session.Area=="deployments"?Visibility.Visible:Visibility.Collapsed;
   ReportPanel.Visibility=session.Area=="reports"?Visibility.Visible:Visibility.Collapsed;
   PageTitle.Text=session.Area switch {"setup"=>"New device setup","diagnostics"=>"Diagnostics","deployments"=>"Deployments","reports"=>"Reports & history",_=>"Your device workspace"};
   PageDescription.Text=session.Area switch
   {
    "setup"=>"Select individual options, then check and review each proposed change. Use Check selected only to inspect without applying changes.",
    "diagnostics"=>"Select individual checks and inspect their details. Changes require an explicit request and review.",
    "deployments"=>"Review local changes and approved application deployments before execution.",
    "reports"=>"Use recorded evidence for handover and support. Capture time and limitations remain visible.",
    _=>"Set up a Windows device or investigate an existing one with shared tools and evidence."
   };
   if(session.StageId is not null)
   {
    var flow=session.Workflows.Workflows.Single(w=>w.Id==session.Area);Stages.ItemsSource=flow.Stages;Stages.SelectedItem=flow.Stages.Single(s=>s.Id==session.StageId);
   }
   UpdateQueueButtons();RefreshTasks();QueueShortcut.Content=$"Selected changes ({session.Queue.Requests.Count})";
   QueueList.ItemsSource=session.Queue.Requests.Select(r=>$"{session.Workflows.ResolveTask(r.TaskId).Title}\n{r.OperationId} • {r.Origin.WorkflowId} / {r.Origin.StageId}").DefaultIfEmpty("No changes queued. Select a task and request a change to add it.").ToArray();
  }
  finally{rendering=false;}
 }
 void Stage_Changed(object sender,SelectionChangedEventArgs e){if(rendering||session is null)return;if(Stages.SelectedItem is WorkflowStage stage){session.Navigate(session.Area,stage.Id);RefreshTasks();WorkspaceViewport.ScrollToTop();}}
 void Search_Changed(object sender,TextChangedEventArgs e){if(session is not null)RefreshTasks();}
 void RefreshTasks()
 {
  var wasRendering=rendering;rendering=true;
  try
  {
  var all=session.Tasks();
  StageProgress.Text=all.Count==0?"":$"{all.Count} tasks • {all.Count(t=>t.Status=="Meets standard")} meet local checks • {all.Count(t=>t.Status=="Needs attention")} need attention • {all.Count(t=>t.Status=="Manual verification")} need manual verification • {all.Count(t=>t.Status=="Unknown")} unknown • {all.Count(t=>t.Status=="Needs administrator read")} need administrator read • {all.Count(t=>t.Status=="Not checked")} not checked";
  var selected=(TaskList.SelectedItem as TaskSelectionRow)?.Definition.Id;
  var rows=session.Tasks(Search.Text).Select(t=>GetRow(t.Definition)).ToArray();
  TaskList.ItemsSource=rows;SetupRows.ItemsSource=rows;TaskList.SelectedItem=rows.FirstOrDefault(t=>t.Definition.Id==selected)??rows.FirstOrDefault();SetupRows.SelectedItem=TaskList.SelectedItem;ShowTask();
  }finally{rendering=wasRendering;}
 }
 void Task_Changed(object sender,SelectionChangedEventArgs e){if(session is not null)ShowTask();}
 void TaskDetails_Click(object sender,RoutedEventArgs e)
 {
  if(TaskList.SelectedItem is not TaskSelectionRow row)return;
  var text=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(18),Text=$"{row.Definition.Title}\n\nCurrent: {row.Current}\nExpected: {row.Expected}\nStatus: {row.Status}\nAccount: {row.Account}\n\n{row.Preview.Explanation}\nSource: {row.Preview.Source}\n\n{TaskRequirement.Text}\n\n{TaskLimitations.Text}"};
  new Window{Owner=this,Title="Task details",Width=720,Height=560,MinWidth=500,MinHeight=380,Content=text,WindowStartupLocation=WindowStartupLocation.CenterOwner}.ShowDialog();
 }
 void Reference_Click(object sender,RoutedEventArgs e)
 {
  if(TaskList.SelectedItem is not TaskSelectionRow task)return;
  var files=LocalDeviceToolkit.Engine.Catalogue.CatalogueLoader.ShippedFiles();using var document=StrictJson.Parse(files["reference.json"]);
  var controls=document.RootElement.GetProperty("controls").EnumerateArray().Where(c=>task.Definition.ControlIds.Contains(c.GetProperty("id").GetString()!)).ToArray();
  var content=new TextBox {Text=System.Text.Json.JsonSerializer.Serialize(new { source=document.RootElement.GetProperty("source"),controls },new System.Text.Json.JsonSerializerOptions {WriteIndented=true}),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(20)};
  new Window {Title="Original M365 control reference • read only",Owner=this,Width=800,Height=680,MinWidth=500,MinHeight=400,Content=content,WindowStartupLocation=WindowStartupLocation.CenterOwner}.ShowDialog();
 }
 void ShowTask()
 {
  var selected=TaskList.SelectedItem as TaskSelectionRow;CheckSelected.IsEnabled=selected is not null&&!isBusy;CheckStage.IsEnabled=!isBusy;
  ProtectedRead.IsEnabled=false;
  if(selected is not null&&!isBusy)
  {
   try{WorkerRequestValidator.Validate(new(2,WorkerMode.CaptureProtected,session.Context,new(selected.Definition.ControlIds,session.Origin,selected.Definition.CheckIds,[selected.Definition.Id]),null,null),session.Catalogue,session.Workflows);ProtectedRead.IsEnabled=true;}
   catch(LocalDeviceToolkit.Engine.Execution.PlanValidationException){}
  }
  ReviewFix.IsEnabled=selected?.NeedsInputs==true&&!isBusy;
  DiagnosticChange.IsEnabled=selected?.CanChange==true&&!isBusy;
  if(selected is null){TaskTitle.Text="Select a task";TaskIds.Text=TaskRequirement.Text=TaskLimitations.Text=Observations.Text=ObservedAt.Text="";return;}
  TaskTitle.Text=selected.Definition.Title;TaskIds.Text=string.Join(" • ",selected.Definition.ControlIds);
  var controls=session.Catalogue.Controls.Where(c=>selected.Definition.ControlIds.Contains(c.Id)).ToArray();
  TaskRequirement.Text=string.Join("\n\n",controls.Select(c=>c.ExpectedDescription));
  TaskLimitations.Text=string.Join("\n\n",controls.Select(c=>$"{c.Id} • {c.Coverage}\n{c.ManualInstructions}"));
  var captured=session.Tasks().FirstOrDefault(t=>t.Definition.Id==selected.Definition.Id)?.CapturedAt;
  ObservedAt.Text=captured is DateTimeOffset at?$"Evidence collected {at.ToLocalTime():g}. Recheck before handover or changing settings.":"No evidence collected for this task.";
  var observations=session.LatestObservations(selected.Definition);
  Observations.Text=$"Current: {selected.Current}\nExpected: {selected.Expected}\nStatus: {selected.Status}\nAccount: {selected.Account}\n\n{selected.Preview.Explanation}\nSource: {selected.Preview.Source}";
 }
 TaskSelectionRow GetRow(TaskDefinition task)
 {
  if(!selections.TryGetValue(task.Id,out var row)){row=new(task,session.Origin);selections.Add(task.Id,row);}
  var request=session.Queue.Requests.FirstOrDefault(r=>r.TaskId==task.Id);
  var observations=session.LatestObservations(task);
  var reviewed=reviewRuns.Current?.Rows.FirstOrDefault(r=>r.Task.Id==task.Id);
  var latestCapture=observations.Where(o=>task.CheckIds.Contains(o.CheckId)).Select(o=>o.CapturedAt).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
  var reviewedCapture=reviewed?.AfterCapturedAt??reviewed?.Before.Select(o=>o.CapturedAt).DefaultIfEmpty(DateTimeOffset.MinValue).Max()??DateTimeOffset.MinValue;
  var preview=reviewed is not null&&latestCapture<=reviewedCapture?reviewed.AfterPreview??reviewed.Preview:null;
  row.Update(preview??ActionPreviewFormatter.Describe(task,request,observations));return row;
 }
 void Selection_Changed(object sender,RoutedEventArgs e)
 {
  if(rendering||isBusy||sender is not CheckBox{DataContext:TaskSelectionRow row})return;
  reviewRuns.Invalidate();
  if(!row.Selected){foreach(var request in session.Queue.Requests.Where(r=>r.TaskId==row.Definition.Id).ToArray())session.Queue.Remove(request);row.ChangeRequested=false;}
  else if(session.Area=="setup"&&row.CanChange){row.ChangeRequested=true;SetIntent(row);}
  UpdateSelectionStatus();
 }
 async void DiagnosticReviewChange_Click(object sender,RoutedEventArgs e)
 {
  if(isBusy||TaskList.SelectedItem is not TaskSelectionRow row||!row.CanChange)return;
  row.ChangeRequested=true;SetIntent(row);await CheckReviewAsync();
 }
 void SetIntent(TaskSelectionRow row)
 {
  reviewRuns.Invalidate();
  if(row.ChangeRequested){row.Selected=true;if(!row.NeedsInputs&&row.Definition.OperationId is string operation)session.Queue.Add(new(row.Definition.Id,operation,StrictJson.Value(new LocalDeviceToolkit.Engine.Settings.SettingParameters(true)),row.Origin));}
  else foreach(var request in session.Queue.Requests.Where(r=>r.TaskId==row.Definition.Id).ToArray())session.Queue.Remove(request);
  UpdateSelectionStatus();
 }
 void UpdateSelectionStatus(){QueueShortcut.Content=$"Selected changes ({session.Queue.Requests.Count})";StatusLine.Text=$"{selections.Values.Count(r=>r.Selected)} selected • {selections.Values.Count(r=>r.Selected&&r.ChangeRequested)} changes requested. Configure application or restoration inputs before review.";UpdateQueueButtons();}
 void SetupTask_Changed(object sender,SelectionChangedEventArgs e){if(SetupRows.SelectedItem is TaskSelectionRow row)TaskList.SelectedItem=row;}
 void ConfigureRow_Click(object sender,RoutedEventArgs e){if(sender is FrameworkElement{DataContext:TaskSelectionRow row}){TaskList.SelectedItem=row;ReviewFix_Click(sender,e);}}
 async void CheckSelected_Click(object sender,RoutedEventArgs e){if(TaskList.SelectedItem is TaskSelectionRow row){row.Selected=true;reviewRuns.Invalidate();await CheckReviewAsync(true);}}
 async void CheckStage_Click(object sender,RoutedEventArgs e)=>await CheckReviewAsync();
 async void CheckOnly_Click(object sender,RoutedEventArgs e)=>await CheckReviewAsync(true);
 async Task CheckReviewAsync(bool checkOnly=false)=>await RunUiAsync(async()=>
 {
  var chosen=selections.Values.Where(r=>r.Selected).Select(r=>new ReviewSelection(r.Definition.Id,r.Origin,!checkOnly&&r.ChangeRequested,session.Queue.Requests.FirstOrDefault(q=>q.TaskId==r.Definition.Id))).ToArray();
  if(chosen.Length==0)throw new InvalidOperationException("Select at least one individual task to check.");
  var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};timer.Tick+=(_,_)=>{if(reviewRuns.Current is {} live){LiveLog.Text=live.FilePath+Environment.NewLine+live.LogText;LiveLog.ScrollToEnd();}};timer.Start();LiveLog.Visibility=Visibility.Visible;
  ReviewRun run;try{run=await reviewRuns.CheckAsync(chosen,lifetime.Token);}finally{timer.Stop();if(reviewRuns.Current is {} latest)LiveLog.Text=latest.FilePath+Environment.NewLine+latest.LogText;}
  RefreshTasks();LiveLog.Text=run.FilePath+Environment.NewLine+run.LogText;
  var attempted=false;var applied=false;Func<Task>? apply=checkOnly?null:async()=>{attempted=true;await ApplyReviewAsync(run);applied=true;};var review=new ReviewRunWindow(run,apply){Owner=this};review.ShowDialog();
  if(!attempted)await reviewRuns.CancelAsync(run,lifetime.Token);
  LiveLog.Text=run.FilePath+Environment.NewLine+run.LogText;RefreshTasks();StatusLine.Text=applied?"Run finished. Review each result and readback; failed or unknown results need follow-up.":attempted?"Run stopped. Review the log for failed or uncertain results.":"Review closed. No change was started.";
 });
 static string OutcomeLabel(ActionOutcome outcome)=>outcome switch{ActionOutcome.AppliedVerified=>"Verified",ActionOutcome.AppliedUnverified=>"Changed; verification incomplete",ActionOutcome.RestartRequired=>"Restart required",ActionOutcome.Failed=>"Failed",ActionOutcome.CancelledBeforeExecution=>"Canceled before execution",_=>"Result unknown"};
 async Task ApplyReviewAsync(ReviewRun review)
 {
  var run=await reviewRuns.RunAsync(review,true,lifetime.Token);if(run is null)throw new InvalidOperationException("No change run was recorded. Review the log and check again.");
  string ResultTitle(Guid actionId){var action=review.Actions.FirstOrDefault(a=>a.Id==actionId);return action is null?"Action":session.Workflows.ResolveTask(action.Request.TaskId).Title;}
  RunResults.Text=$"Run {run.Id:D} • {(run.EvidenceComplete?"Evidence recorded":"Log or evidence incomplete — inspect results before continuing")}\n"+string.Join("\n\n",run.Results.Select(result=>$"{OutcomeLabel(result.Outcome)} • {ResultTitle(result.ActionId)}\n{result.Reason}\nExecuting account: {result.Actor?.UserName??"Not confirmed / not started"}"));
  foreach(var action in review.Actions.Where(a=>a.Request.OperationId=="packages.install"&&a.Readiness.Privilege==PrivilegeRequirement.CurrentUser&&run.Results.Any(r=>r.ActionId==a.Id)))
  {
   try{var input=StrictJson.Read<DeploymentParameters>(StrictJson.Bytes(action.Request.Parameters));await(await UserPackageAsync(input,CancellationToken.None)).AcknowledgeRecordedRunAsync(session.Context,action.Request.Parameters,run.Id,evidence,CancellationToken.None);}
   catch(Exception error) when(error is IOException or UnauthorizedAccessException or LocalDeviceToolkit.Engine.Execution.PlanValidationException or EvidenceValidationException){RunResults.Text+="\nDeployment guard retained; reconcile before another attempt.";}
  }
  session.Queue.Clear();foreach(var row in selections.Values)row.ChangeRequested=false;Render();
 }
 async void ProtectedRead_Click(object sender,RoutedEventArgs e)
 {
  if(TaskList.SelectedItem is not TaskSelectionRow task)return;
  var origin=session.Origin;
  await RunUiAsync(async()=>
  {
   var request=new WorkerRequest(2,WorkerMode.CaptureProtected,session.Context,new(task.Definition.ControlIds,origin,task.Definition.CheckIds,[task.Definition.Id]),null,null);
   WorkerRequestValidator.Validate(request,session.Catalogue,session.Workflows);
   var broker=new UacWorkerBroker(Path.Combine(AppContext.BaseDirectory,"worker","LocalDeviceToolkit.Worker.exe"));
   var response=await broker.RequestAsync(request,lifetime.Token);
   if(!response.Success||response.Capture is null)throw new InvalidOperationException(response.Message);
   await session.AcceptProtectedCaptureAsync(response.Capture,origin,lifetime.Token,request.Capture);RefreshTasks();StatusLine.Text="Protected evidence saved. Original engineer and executing administrator are recorded separately.";
  });
 }
 async void ReviewFix_Click(object sender,RoutedEventArgs e)
 {
  if(TaskList.SelectedItem is not TaskSelectionRow task||!task.NeedsInputs||isBusy)return;
  await RunUiAsync(()=>
  {
  System.Text.Json.JsonElement parameters;
  if(task.Definition.OperationId=="settings.restore-recorded")
  {var restore=new RestorationInputWindow(evidence){Owner=this};if(restore.ShowDialog()!=true)return Task.CompletedTask;parameters=StrictJson.Value(restore.Parameters);}
  else if(task.Definition.OperationId=="packages.install")
  {var deployment=new DeploymentInputWindow(new ApprovedProfileStore(ApprovedProfileStore.UserCacheRoot),ApproveProfileAsync,controlId:task.Definition.ControlIds.Single(),reconcile:ReconcileDeploymentAsync){Owner=this};if(deployment.ShowDialog()!=true)return Task.CompletedTask;parameters=StrictJson.Value(deployment.Parameters);}
  else return Task.CompletedTask;
  var request=new ActionRequest(task.Definition.Id,task.Definition.OperationId!,parameters,task.Origin);
  reviewRuns.Invalidate();var added=session.Queue.Add(request);if(added==LocalDeviceToolkit.Engine.Execution.QueueAddResult.Added){task.Selected=true;task.ChangeRequested=true;}StatusLine.Text=added switch {LocalDeviceToolkit.Engine.Execution.QueueAddResult.AlreadyQueued=>"This identical change is already in the shared queue.",LocalDeviceToolkit.Engine.Execution.QueueAddResult.Added=>"Added to the shared queue. Review the complete plan before execution.",_=>"A different change already targets this setting; remove it explicitly before adding this request."};Render();return Task.CompletedTask;
  });
 }
 async Task<DeploymentProfile> ApproveProfileAsync(DeploymentProfile profile,string directory,CancellationToken ct)
 {
  var broker=new UacWorkerBroker(Path.Combine(AppContext.BaseDirectory,"worker","LocalDeviceToolkit.Worker.exe"));
  var response=await broker.RequestAsync(new(2,WorkerMode.ApproveDeploymentProfile,session.Context,null,null,null,DeploymentApproval:new(profile,directory)),ct);
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
  var response=await broker.RequestAsync(new(2,resolve?WorkerMode.ResolveDeployment:WorkerMode.ReconcileDeployment,session.Context,null,null,null,DeploymentReconcile:parameters,DeploymentResolutionReview:resolve?new(true):null),ct);
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
 void RemoveQueued_Click(object sender,RoutedEventArgs e){if(isBusy)return;var index=QueueList.SelectedIndex;var requests=session.Queue.Requests;if(index>=0&&index<requests.Count){reviewRuns.Invalidate();if(selections.TryGetValue(requests[index].TaskId,out var row))row.ChangeRequested=false;session.Queue.Remove(requests[index]);}Render();}
 void ClearQueue_Click(object sender,RoutedEventArgs e){if(isBusy)return;reviewRuns.Invalidate();session.Queue.Clear();foreach(var row in selections.Values)row.ChangeRequested=false;Render();}
 async void ReviewQueue_Click(object sender,RoutedEventArgs e)=>await CheckReviewAsync();
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
  if(isBusy)return;isBusy=true;WorkflowPanel.IsEnabled=false;Busy.Visibility=Visibility.Visible;UpdateQueueButtons();ShowTask();
  try{await action();}
  catch(OperationCanceledException){StatusLine.Text="The check was cancelled. No successful result was inferred.";}
  catch(Exception error){StatusLine.Text="The request did not complete: "+error.Message;MessageBox.Show(this,error.Message,"Request did not complete",MessageBoxButton.OK,MessageBoxImage.Warning);}
  finally{isBusy=false;WorkflowPanel.IsEnabled=true;Busy.Visibility=Visibility.Collapsed;UpdateQueueButtons();ShowTask();}
 }
}
