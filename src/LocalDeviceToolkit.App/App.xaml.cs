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
  try
  {
   var identity=new WindowsContextProvider();var context=await identity.GetCurrentAsync(default);
   var catalogue=CatalogueLoader.LoadShipped();var workflows=WorkflowCatalogue.LoadShipped();
   var store=new EvidenceStore(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDeviceToolkit","evidence"),ToolkitEvidenceSchemas.Create(),context);
   var source=new NativeFactsSource(System.IO.Path.Combine(AppContext.BaseDirectory,"scripts"));
   var inspector=new WindowsDeviceInspector(identity,identity,source,catalogue,TimeProvider.System);
   var operations=LocalDeviceToolkit.Windows.WindowsOperations.Create(workflows);
   var session=new WorkbenchSession(context,catalogue,workflows,inspector,store,operations);
   var plans=new PlanService(operations,TimeProvider.System);
   var coordinator=new LocalDeviceToolkit.Engine.Execution.RunCoordinator(plans,operations,identity,catalogue,store,new LocalDeviceToolkit.Windows.Elevation.WindowsActionDispatcher(operations,identity,System.IO.Path.Combine(AppContext.BaseDirectory,"worker","LocalDeviceToolkit.Worker.exe")),TimeProvider.System);
   MainWindow=new MainWindow(session,store,plans,coordinator);MainWindow.Show();
  }
  catch(Exception error){MessageBox.Show("The toolkit could not start safely.\n\n"+error.Message,"Local Device Toolkit",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);}
 }
}
