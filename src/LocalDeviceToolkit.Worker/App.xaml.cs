using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.IO.Pipes;
using System.Globalization;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Windows.Deployments;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Elevation;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Windows.Inspection;
using LocalDeviceToolkit.Windows;
using LocalDeviceToolkit.Windows.Elevation;
namespace LocalDeviceToolkit.Worker;
public partial class App:Application
{
 protected override async void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);
  try{await RunAsync(e.Args);Shutdown(0);}
  catch(Exception error){MessageBox.Show("The protected request was refused or could not complete. No successful outcome was inferred.\n\n"+error.Message,"Local Device Toolkit • protected worker",MessageBoxButton.OK,MessageBoxImage.Warning);Shutdown(2);}
 }
 async Task RunAsync(string[] args)
 {
  if(args.Length!=6||args[0]!="--pipe"||args[2]!="--parent-pid"||args[4]!="--parent-started"||!args[1].StartsWith("LocalDeviceToolkit-",StringComparison.Ordinal)||!Guid.TryParseExact(args[1]["LocalDeviceToolkit-".Length..],"N",out _)||!int.TryParse(args[3],NumberStyles.None,CultureInfo.InvariantCulture,out var parentPid)||!long.TryParse(args[5],NumberStyles.None,CultureInfo.InvariantCulture,out var parentStarted))throw new InvalidDataException("Start the worker from the toolkit's reviewed action; direct command execution is unavailable.");
  var nativeIdentity=new WindowsContextProvider();var actor=await nativeIdentity.GetActorAsync(default);
  if(!actor.IsElevated)throw new UnauthorizedAccessException("The worker did not receive an elevated token.");
  var packageRoot=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,".."));
  var expectedParent=Path.Combine(packageRoot,"LocalDeviceToolkit.App.exe");
  var parent=NativePeer.Observe(parentPid);
  if(parent.Actor.ProcessStartedAt.UtcTicks!=parentStarted||!string.Equals(parent.ImagePath,expectedParent,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The initiating process is not this package's toolkit application.");
  using var pipe=new NamedPipeClientStream(".",args[1],PipeDirection.InOut,PipeOptions.Asynchronous,System.Security.Principal.TokenImpersonationLevel.Identification);
  using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30)))await pipe.ConnectAsync(deadline.Token);
  if(NativePeer.PipeServer(pipe.SafePipeHandle)!=parentPid||NativePeer.Observe(parentPid)!=parent)throw new InvalidDataException("The pipe server is not the initiating process.");
  WorkerRequest request;
  using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30)))request=await WorkerFrames.ReadAsync<WorkerRequest>(pipe,deadline.Token);
  WorkerGuard.ValidateBuild(request.ToolkitBuild,ToolkitBuildIdentity.Current);
  var catalogue=CatalogueLoader.LoadShipped();var workflows=WorkflowCatalogue.LoadShipped();
  WorkerRequestValidator.Validate(request,catalogue,workflows);
  var machine=await nativeIdentity.GetCurrentAsync(default);WorkerGuard.ValidateOriginal(request.OriginalContext,machine,parent.Actor);
  var workspace=new ProtectedWorkspace();
  var schemas=ToolkitEvidenceSchemas.Create();DeploymentEvidenceSchemas.Register(schemas);
  var journal=new EvidenceStore(workspace.Directory("journals"),schemas,request.OriginalContext);
  var profileStore=new ApprovedProfileStore(workspace.Directory("profiles"));
  var deploymentPending=new FileDeploymentPendingStore(workspace.Directory("journals"));
  var packages=new ApprovedPackageOperation(profileStore,deploymentPending);
  var operations=WindowsOperations.Create(workflows,parentPid,journal,true,profileStore,deploymentPending,workspace.StageScripts(true),workspace.Directory("runtime"));
  if(request.Mode==WorkerMode.ExecuteAction)
  {
   var reviewed=request.Plan!.Actions.Single(a=>a.Id==request.ActionId);operations.Validate(reviewed.Request);
   if(reviewed.Readiness.Scope!=OperationScope.Machine)throw new PlanValidationException("Current-user changes cannot run under alternate administrator credentials.");
  }
  string details,button;
  if(request.Mode==WorkerMode.CaptureProtected){details="Read only: "+string.Join(", ",request.Capture!.ControlIds)+"\nNo protection settings or secrets will be changed or collected.";button="Run protected checks";}
  else if(request.Mode==WorkerMode.ApproveDeploymentProfile)
  {
   if(!PlanService.SupportedTarget(request.OriginalContext))throw new PlanValidationException("Approved local deployments require Windows 11 x64.");
   var input=request.DeploymentApproval!;var check=await new WindowsDeploymentBackend().CheckInputsAsync(input.Profile,input.SourceDirectory,default);
   if(!check.Accepted)throw new PlanValidationException(check.Reason);
   details="Approve this exact local software profile for future reviewed deployments. This approval does not install software. Verify client authorization, licensing and vendor guidance.\nMedia: "+input.SourceDirectory+"\n"+System.Text.Json.JsonSerializer.Serialize(input.Profile,new System.Text.Json.JsonSerializerOptions(StrictJson.Options){WriteIndented=true});button="Approve profile only";
  }
  else if(request.Mode is WorkerMode.ReconcileDiagnostic or WorkerMode.ResolveDiagnostic)
  {details="Read the protected unresolved Defender signature request and fresh local Defender state. No update is launched. "+(request.Mode==WorkerMode.ResolveDiagnostic?"Engineer explicitly reviewed vendor completion and confirmed no update remains in progress. The original process must be confirmed no longer running; this does not change the original uncertain outcome.":"The pending guard is retained.");button=request.Mode==WorkerMode.ResolveDiagnostic?"Record completion review and resolve guard":"Read signature request state";}
  else if(request.Mode is WorkerMode.ReconcileDeployment or WorkerMode.ResolveDeployment)
  {
   if((await profileStore.LoadAsync(request.DeploymentReconcile!.ProfileId,default)).Scope!=OperationScope.Machine)throw new PlanValidationException("Original-user package reconciliation stays in the original engineer process.");
   var input=StrictJson.Value(request.DeploymentReconcile!);var status=await packages.Resolve(input).ReconcileAsync(request.OriginalContext,input,default);
   details=(request.Mode==WorkerMode.ResolveDeployment?"Record explicit administrator review that the vendor operation has finished and no installer children remain. Fresh state and original process identity are checked again; no installer is launched.\n":"Read-only deployment inventory and original process status. No installer is launched or pending guard removed.\n")+System.Text.Json.JsonSerializer.Serialize(status,new System.Text.Json.JsonSerializerOptions(StrictJson.Options){WriteIndented=true});
   button=request.Mode==WorkerMode.ResolveDeployment?"Record reviewed completion":"Read deployment state";
  }
  else
  {
   var action=request.Plan!.Actions.Single(a=>a.Id==request.ActionId);
   details="Action: "+action.Request.OperationId+"\nPlan: "+request.Plan.Id.ToString("D")+"\nExpires: "+request.Plan.ExpiresAt.ToLocalTime().ToString("g")+"\nParameters: "+action.Request.Parameters.GetRawText()+"\nBefore / source binding: "+string.Join("; ",action.Before.Select(o=>o.Source+" / "+o.CheckId+" = "+o.Value?.GetRawText()));button="Apply reviewed machine action";
   if(action.Request.OperationId=="packages.install"){var input=StrictJson.Read<DeploymentParameters>(StrictJson.Bytes(action.Request.Parameters));details+="\nApproved package identity and full file manifest:\n"+System.Text.Json.JsonSerializer.Serialize(await profileStore.LoadAsync(input.ProfileId,default),new System.Text.Json.JsonSerializerOptions(StrictJson.Options){WriteIndented=true});}
  }
  if(!Review(request.OriginalContext,actor,details,button))
  {
   await WorkerFrames.WriteAsync(pipe,new WorkerResponse(2,false,"Administrator cancelled the protected review. No action started.",actor,null,null),default);return;
  }
  bool ParentStillMatches(){try{return NativePeer.Observe(parentPid)==parent;}catch{return false;}}
  if(!ParentStillMatches())throw new IOException("The initiating process is no longer available.");
  using var writer=workspace.AcquireWriter();
  WorkerResponse response;
  if(request.Mode==WorkerMode.CaptureProtected)
  {
   var source=new NativeFactsSource(workspace.StageScripts(),workspace.Directory("runtime"));
   var inspector=new WindowsDeviceInspector(new OriginalContext(request.OriginalContext),nativeIdentity,source,catalogue,TimeProvider.System,new OperationalProbeSource(workspace.StageScripts(),workspace.Directory("runtime")));
   var capture=await inspector.CaptureAsync(request.Capture!,default);
   await journal.SaveNewAsync(EvidenceRecord.Capture(capture),default);
   await journal.SaveNewAsync(EvidenceRecord.Create(Guid.NewGuid(),"capture-selection.v2",capture.Context,capture.CapturedAt,StrictJson.Value(new CaptureSelectionEvidence(2,capture.Id,request.Capture!.TaskIds!,request.Capture.CheckIds!){Inputs=request.Capture.Inputs})),default);
   response=new(2,true,"Protected evidence saved. Review unavailable checks separately.",actor,capture,null);
  }
  else if(request.Mode==WorkerMode.ApproveDeploymentProfile)
  {
   var input=request.DeploymentApproval!;var check=await new WindowsDeploymentBackend().CheckInputsAsync(input.Profile,input.SourceDirectory,default);if(!check.Accepted)throw new PlanValidationException(check.Reason);
   var approved=ApprovedProfileStore.WithContentId(input.Profile);approved=await profileStore.SaveOrLoadIdenticalAsync(approved,default);
   response=new(2,true,"Approved exact profile saved; no software was installed.",actor,null,null,approved);
  }
  else if(request.Mode is WorkerMode.ReconcileDiagnostic or WorkerMode.ResolveDiagnostic)
  {
   var backend=new LocalDeviceToolkit.Windows.Diagnostics.WindowsDiagnosticRepairBackend(workspace.StageScripts(true),workspace.Directory("runtime"),deploymentPending);
   EvidenceRecord? completedReceipt=null;
   var message=await backend.ReconcileAsync(request.OriginalContext,request.Mode==WorkerMode.ResolveDiagnostic,default,async (originalActionId,summary)=>
   {var receiptId=Guid.NewGuid();var reviewedAt=DateTimeOffset.UtcNow;completedReceipt=EvidenceRecord.Create(receiptId,"diagnostic-reconciliation.v1",request.OriginalContext,reviewedAt,StrictJson.Value(new LocalDeviceToolkit.Engine.Diagnostics.DiagnosticReconciliation(receiptId,request.OriginalContext,actor,reviewedAt,true,summary){OriginalActionId=originalActionId}));await journal.SaveNewAsync(completedReceipt,default);});
   var id=Guid.NewGuid();var at=DateTimeOffset.UtcNow;
   await journal.SaveNewAsync(EvidenceRecord.Create(id,"diagnostic-reconciliation.v1",request.OriginalContext,at,StrictJson.Value(new LocalDeviceToolkit.Engine.Diagnostics.DiagnosticReconciliation(id,request.OriginalContext,actor,at,request.Mode==WorkerMode.ResolveDiagnostic,message))),default);
   response=new(2,true,message,actor,null,null,DiagnosticResolution:completedReceipt);
  }
  else if(request.Mode is WorkerMode.ReconcileDeployment or WorkerMode.ResolveDeployment)
  {
   var input=StrictJson.Value(request.DeploymentReconcile!);var operation=packages.Resolve(input);EvidenceRecord? resolution=null;
   if(request.Mode==WorkerMode.ResolveDeployment){var id=await operation.ResolveReviewedAsync(request.OriginalContext,input,request.DeploymentResolutionReview!,journal,default);resolution=await journal.LoadAsync(id,default);}
   var status=await operation.ReconcileAsync(request.OriginalContext,input,default);
   response=new(2,true,request.Mode==WorkerMode.ResolveDeployment?"Reviewed completion recorded; inspect the receipt. No installer was launched.":"Read-only deployment state collected. No installer was launched.",actor,null,null,DeploymentStatus:status,DeploymentResolution:resolution);
  }
  else
  {
   var service=new ProtectedActionService(operations,new PlanService(operations,TimeProvider.System),catalogue,journal,TimeProvider.System);
   var result=await service.ExecuteAsync(request.Plan!,request.ActionId!.Value,machine,parent.Actor,actor,ParentStillMatches,default);
   if(request.Plan!.Actions.Single(a=>a.Id==request.ActionId).Request.OperationId=="packages.install")
   {
    try{var saved=await journal.LoadAsync(ProtectedJournalIds.Result(request.Plan.Id,request.ActionId!.Value),default);var receipt=StrictJson.Read<ResultRecord>(StrictJson.Bytes(saved.Data));var input=request.Plan.Actions.Single(a=>a.Id==request.ActionId).Request.Parameters;await packages.Resolve(input).AcknowledgeRecordedRunAsync(request.OriginalContext,input,receipt.RunId,journal,default);}
    catch(Exception error) when(error is IOException or PlanValidationException or EvidenceValidationException or UnauthorizedAccessException){result=result with{Reason=result.Reason+" Pending guard retained; use read-only reconciliation before any further deployment."};}
   }
   response=new(2,true,"Protected action finished; inspect its verified outcome and journal.",actor,null,result);
  }
  await WorkerFrames.WriteAsync(pipe,response,default);
 }
 static bool Review(DeviceContext original,ExecutionActor actor,string details,string buttonText)
 {
  var panel=new StackPanel {Margin=new Thickness(24)};
  panel.Children.Add(new TextBlock {Text="Protected device request",FontSize=24,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(new TextBlock {Text=$"Device: {original.ComputerName}\nOriginal engineer: {original.OriginalUserName} ({original.OriginalUserSid})\nExecuting administrator: {actor.UserName} ({actor.Sid})",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,16)});
  panel.Children.Add(new TextBox {Text=details,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=250,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
  var window=new Window {Title="Local Device Toolkit • administrator review",Width=680,Height=520,MinWidth=500,MinHeight=400,FontFamily=new System.Windows.Media.FontFamily("Segoe UI"),FontSize=14,WindowStartupLocation=WindowStartupLocation.CenterScreen,Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
  var confirm=new Button {Content=buttonText,Padding=new Thickness(16,10,16,10),Margin=new Thickness(0,20,0,10)};confirm.Click+=(_,_)=>window.DialogResult=true;
  var cancel=new Button {Content="Cancel",IsCancel=true,Padding=new Thickness(16,10,16,10)};panel.Children.Add(confirm);panel.Children.Add(cancel);return window.ShowDialog()==true;
 }
 sealed class OriginalContext(DeviceContext original):IExecutionContextProvider{public Task<DeviceContext> GetCurrentAsync(CancellationToken ct)=>Task.FromResult(original);}
}
