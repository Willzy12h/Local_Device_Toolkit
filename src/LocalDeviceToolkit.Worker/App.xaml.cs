using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.IO.Pipes;
using System.Globalization;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
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
  var operations=WindowsOperations.Create(workflows);
  if(request.Mode==WorkerMode.ExecuteAction)
  {
   var reviewed=request.Plan!.Actions.Single(a=>a.Id==request.ActionId);operations.Validate(reviewed.Request);
   if(reviewed.Readiness.Scope!=OperationScope.Machine)throw new PlanValidationException("Current-user changes cannot run under alternate administrator credentials.");
  }
  var details=request.Mode==WorkerMode.CaptureProtected?
   "Read only: "+string.Join(", ",request.Capture!.ControlIds)+"\nNo protection settings or secrets will be changed or collected.":
   "Action: "+request.Plan!.Actions.Single(a=>a.Id==request.ActionId).Request.OperationId+"\nPlan: "+request.Plan.Id.ToString("D")+"\nExpires: "+request.Plan.ExpiresAt.ToLocalTime().ToString("g")+"\nParameters: "+request.Plan.Actions.Single(a=>a.Id==request.ActionId).Request.Parameters.GetRawText();
  if(!Review(request.OriginalContext,actor,details,request.Mode==WorkerMode.CaptureProtected?"Run protected checks":"Apply reviewed machine action"))
  {
   await WorkerFrames.WriteAsync(pipe,new WorkerResponse(1,false,"Administrator cancelled the protected review. No action started.",actor,null,null),default);return;
  }
  bool ParentStillMatches(){try{return NativePeer.Observe(parentPid)==parent;}catch{return false;}}
  if(!ParentStillMatches())throw new IOException("The initiating process is no longer available.");
  var workspace=new ProtectedWorkspace();using var writer=workspace.AcquireWriter();
  var journal=new EvidenceStore(workspace.Directory("journals"),ToolkitEvidenceSchemas.Create(),request.OriginalContext);
  WorkerResponse response;
  if(request.Mode==WorkerMode.CaptureProtected)
  {
   var source=new NativeFactsSource(workspace.StageReadOnlyScripts(),workspace.Directory("runtime"));
   var inspector=new WindowsDeviceInspector(new OriginalContext(request.OriginalContext),nativeIdentity,source,catalogue,TimeProvider.System);
   var capture=await inspector.CaptureAsync(request.Capture!,default);
   await journal.SaveNewAsync(EvidenceRecord.Capture(capture),default);
   response=new(1,true,"Protected evidence saved. Review unavailable checks separately.",actor,capture,null);
  }
  else
  {
   var service=new ProtectedActionService(operations,new PlanService(operations,TimeProvider.System),catalogue,journal,TimeProvider.System);
   var result=await service.ExecuteAsync(request.Plan!,request.ActionId!.Value,machine,parent.Actor,actor,ParentStillMatches,default);
   response=new(1,true,"Protected action finished; inspect its verified outcome and journal.",actor,null,result);
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
