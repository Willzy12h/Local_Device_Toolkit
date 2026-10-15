using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Planning;
namespace LocalDeviceToolkit.Windows;
public static class WindowsOperations
{
 public static OperationRegistry Create(WorkflowCatalogue workflows)=>new(workflows,[]);
}
