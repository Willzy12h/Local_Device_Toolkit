using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using LocalDeviceToolkit.App;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Engine.Reporting;
using LocalDeviceToolkit.Engine.Workbench;
using LocalDeviceToolkit.Tests;

internal static class Program
{
 [STAThread]
 public static int Main()
 {
  if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native WPF regression requires Windows.");
  // Base Application only: never invoke the production App.OnStartup or native providers.
  var application = new Application();
  var repository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!, "../.."));
  var xaml = XDocument.Load(Path.Combine(repository, "src/LocalDeviceToolkit.App/App.xaml"));
  var resources = xaml.Root!.Elements().Single(e => e.Name.LocalName == "Application.Resources");
  application.Resources = (ResourceDictionary)XamlReader.Parse("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" + string.Concat(resources.Elements().Select(e => e.ToString())) + "</ResourceDictionary>");
  var workflows = WorkflowCatalogue.LoadShipped();
  var catalogue = CatalogueLoader.LoadShipped();
  var unused = new NoDeviceAccess();
  var operations = new OperationRegistry(workflows, []);
  var session = new WorkbenchSession(TestData.Context, catalogue, workflows, unused, unused, operations);
  var plans = new PlanService(operations, TimeProvider.System);
  var coordinator = new RunCoordinator(plans, operations, unused, catalogue, unused, unused, TimeProvider.System);
  var reviews = new ReviewRunService(operations, plans, coordinator, session, Path.GetTempPath(), TimeProvider.System);
  var window = new MainWindow(session, unused, plans, coordinator, new ReportService(unused, catalogue, TestData.Context), new ManualReviewRecorder(unused, catalogue, TestData.Context, _ => throw new InvalidOperationException("No actor collection allowed.")), reviews);
  try
  {
   window.Show(); Pump();
   typeof(MainWindow).GetMethod("NavigateWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, ["setup", "readiness"]);
   Pump();
   var grid = (DataGrid)window.FindName("SetupRows");
   var row = grid.Items.Cast<TaskSelectionRow>().Single(r => r.Definition.Id == "readiness.secure-boot");
   grid.SelectedItem = row;
   grid.ScrollIntoView(row, grid.Columns[0]);
   grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[0]);
   grid.BeginEdit(); Pump();
   var checkbox = Find<CheckBox>(grid, b => ReferenceEquals(b.DataContext, row)) ?? throw new InvalidOperationException("Setup checkbox was not rendered.");
   checkbox.Focus();
   typeof(ToggleButton).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(checkbox, null);
   Pump();
   Console.WriteLine($"Setup after click: visibleChecked={checkbox.IsChecked}; modelSelected={row.Selected}; task={row.Definition.Id}");
   Require(checkbox.IsChecked == true, "The real Setup checkbox did not toggle.");
   Require(row.Selected, "The visibly ticked Setup checkbox was not committed to the selected task model.");
   Require(grid.Items.Cast<TaskSelectionRow>().Count(r => r.Selected) == 1, "Setup must select exactly the ticked task.");
   Require(session.Queue.Requests.Count == 0, "A read-only checkbox test must never queue changes.");
   Console.WriteLine("PASS: native Setup checkbox updates selection without device access.");
   return 0;
  }
  catch (Exception error) { Console.Error.WriteLine(error); return 1; }
  finally { window.Close(); application.Shutdown(); }
 }

 static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
 static void Pump()
 {
  var frame = new DispatcherFrame();
  Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
  Dispatcher.PushFrame(frame);
 }
 static T? Find<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
 {
  if (root is T match && predicate(match)) return match;
  for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
   if (Find(VisualTreeHelper.GetChild(root, i), predicate) is { } found) return found;
  return null;
 }
 static string Source([System.Runtime.CompilerServices.CallerFilePath] string source = "") => source;

 sealed class NoDeviceAccess : IDeviceInspector, IEvidenceStore, IExecutionContextProvider, IActionDispatcher
 {
  static Exception Refused() => new InvalidOperationException("Selection tests cannot read or modify device state or evidence.");
  public Task<DeviceCapture> CaptureAsync(CaptureRequest request, CancellationToken ct) => throw Refused();
  public Task SaveNewAsync(EvidenceRecord record, CancellationToken ct) => throw Refused();
  public Task<EvidenceRecord> LoadAsync(Guid id, CancellationToken ct) => throw Refused();
  public Task<HistoryPage> ReadHistoryAsync(string? cursor, CancellationToken ct) => throw Refused();
  public Task<DeviceContext> GetCurrentAsync(CancellationToken ct) => throw Refused();
  public Task<ActionResult> DispatchAsync(ReviewedPlan plan, PlannedAction action, CancellationToken ct) => throw Refused();
 }
}
