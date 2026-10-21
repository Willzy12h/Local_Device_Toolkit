using System.Windows;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Workbench;
using LocalDeviceToolkit.Windows.Inspection;
namespace LocalDeviceToolkit.App;
public partial class App:Application
{
 protected override async void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);
  var smoke=e.Args.Length==2&&e.Args[0]=="--smoke-output";
  var silent=e.Args.Length>0&&e.Args[0]=="--silent";
  if(silent)ShutdownMode=ShutdownMode.OnExplicitShutdown;
  try
  {
   var silentOptions=silent?SilentRunOptions.Parse(e.Args):null;
   if(e.Args.Length!=0&&!smoke&&!silent)throw new ArgumentException("Use no arguments for normal startup, or --silent --preset all|fast [--config <file>] [--inputs <file>] for diagnostic reports.");
   var identity=new WindowsContextProvider();var context=await identity.GetCurrentAsync(default);
   var catalogue=CatalogueLoader.LoadShipped();var workflows=WorkflowCatalogue.LoadShipped();
   var schemas=ToolkitEvidenceSchemas.Create();LocalDeviceToolkit.Engine.Reporting.ReportingEvidenceSchemas.Register(schemas);LocalDeviceToolkit.Engine.Deployments.DeploymentEvidenceSchemas.Register(schemas);
   var store=new EvidenceStore(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDeviceToolkit","evidence"),schemas,context);
   var source=new NativeFactsSource(System.IO.Path.Combine(AppContext.BaseDirectory,"scripts"));
   var inspector=new WindowsDeviceInspector(identity,identity,source,catalogue,TimeProvider.System,new OperationalProbeSource(System.IO.Path.Combine(AppContext.BaseDirectory,"scripts")));
   var operations=LocalDeviceToolkit.Windows.WindowsOperations.Create(workflows,settingSource:store,deploymentProfiles:new LocalDeviceToolkit.Windows.Deployments.ApprovedProfileStore(LocalDeviceToolkit.Windows.Deployments.ApprovedProfileStore.UserCacheRoot),deploymentPending:new LocalDeviceToolkit.Engine.Deployments.FileDeploymentPendingStore(System.IO.Path.Combine(LocalDeviceToolkit.Windows.Deployments.ApprovedProfileStore.UserCacheRoot,"preview-pending")));
   var session=new WorkbenchSession(context,catalogue,workflows,inspector,store,operations);
   if(!silent)await session.LoadActionHistoryAsync(default);
   var plans=new PlanService(operations,TimeProvider.System);
   var coordinator=new LocalDeviceToolkit.Engine.Execution.RunCoordinator(plans,operations,identity,catalogue,store,new LocalDeviceToolkit.Windows.Elevation.WindowsActionDispatcher(operations,identity,System.IO.Path.Combine(AppContext.BaseDirectory,"worker","LocalDeviceToolkit.Worker.exe")),TimeProvider.System);
   var reviewRuns=new ReviewRunService(operations,plans,coordinator,session,System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDeviceToolkit","logs"),TimeProvider.System);
   if(silentOptions is not null)
   {
    var inputs=silentOptions.InputsFile is null?new Dictionary<string,System.Text.Json.JsonElement>():SilentRunOptions.ReadInputs(SilentRunOptions.ReadFile(silentOptions.InputsFile));
    var result=await new SilentRunService(session,reviewRuns,TimeProvider.System).RunAsync(silentOptions,inputs,default);
    Shutdown(result.Summary.ExitCode);return;
   }
   MainWindow=new MainWindow(session,store,plans,coordinator,new LocalDeviceToolkit.Engine.Reporting.ReportService(store,catalogue,context),new LocalDeviceToolkit.Engine.Reporting.ManualReviewRecorder(store,catalogue,context,identity.GetActorAsync),reviewRuns);MainWindow.Show();
   if(smoke)
   {
    await ((MainWindow)MainWindow).RenderSmokePagesAsync(System.IO.Path.GetFullPath(e.Args[1]));Shutdown(0);
   }
  }
  catch(Exception error)
  {
   if(e.Args.Length>0)
   {
    try
    {
     var logs=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDeviceToolkit","logs");
     EvidenceStore.RefuseReparsePath(logs);System.IO.Directory.CreateDirectory(logs);
     var path=System.IO.Path.Combine(logs,"app-error-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".log");
     using var output=new System.IO.StreamWriter(new System.IO.FileStream(path,System.IO.FileMode.CreateNew,System.IO.FileAccess.Write,System.IO.FileShare.Read));
     output.WriteLine(DateTimeOffset.UtcNow.ToString("O")+" "+error);Console.Error.WriteLine("Toolkit error. Details: "+path);
    }
    catch(Exception){Console.Error.WriteLine(error.Message);}
    Shutdown(error is ArgumentException or System.Text.Json.JsonException or CatalogueValidationException?64:1);return;
   }
   MessageBox.Show("The toolkit could not start safely.\n\n"+error.Message,"Local Device Toolkit",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);
  }
 }
}
