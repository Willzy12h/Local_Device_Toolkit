using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Reporting;
using LocalDeviceToolkit.Engine.Workbench;
using Microsoft.Win32;
namespace LocalDeviceToolkit.App;
/// <summary>Native, read-only saved evidence viewer. The only write command records an explicit engineer review.</summary>
public sealed class ReportsWindow : Window
{
 readonly ReportService service;
 readonly WorkbenchSession session;
 readonly ManualReviewRecorder? recorder;
 readonly ListBox history = new() { SelectionMode = SelectionMode.Extended, MinHeight = 120, MaxHeight = 210 };
 readonly ComboBox purpose = new() { ItemsSource = Enum.GetValues<ReportPurpose>(), SelectedIndex = 0, MinWidth = 180 };
 readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
 readonly TextBox details = new() { MinHeight=180,MaxHeight=400, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
 readonly DataGrid controls = new() { IsReadOnly = true, AutoGenerateColumns = false, MinHeight = 160, MaxHeight = 260 };
 readonly Button next = new() { Content = "Next history page", IsEnabled = false };
 readonly List<HistoryDiagnostic> diagnostics = [];
 readonly HashSet<Guid> seen = [];
 string? cursor;
 bool busy;
 LocalReport? report;
 sealed record HistoryItem(Guid Id, string Kind, DateTimeOffset Time, string Device, string OriginalEngineer)
 {
  public override string ToString() => $"{Time:O} | {Kind} | {Device} | {OriginalEngineer} | {Id:D}";
 }
 public ReportsWindow(ReportService service, WorkbenchSession session, ManualReviewRecorder? recorder = null)
 {
  this.service = service; this.session = session; this.recorder = recorder;
  Title = "Saved device reports and recorded reviews"; Width = 1120; Height = 850; MinWidth = 800; MinHeight = 480; FontSize = 14;
  WindowStartupLocation = WindowStartupLocation.CenterOwner;
  var panel = new DockPanel { Margin = new Thickness(20) }; Content = new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
  top.Children.Add(new TextBlock { Text = "Saved evidence and snapshot reports", FontSize = 24 });
  top.Children.Add(new TextBlock { Text = "Select saved records with Ctrl/Shift. History pages use record IDs, not date order. Reports use only selected records; selection never executes or replays an action.", TextWrapping = TextWrapping.Wrap });
  var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) }; top.Children.Add(buttons);
  buttons.Children.Add(purpose);
  AddButton(buttons, "First history page", () => LoadPageAsync(true));
  next.Click += async (_, _) => await GuardAsync(() => LoadPageAsync(false)); buttons.Children.Add(next);
  AddButton(buttons, "Select session captures", SelectSessionAsync);
  AddButton(buttons, "Build selected report", BuildAsync);
  AddButton(buttons, "Record manual review", RecordManualAsync, recorder is not null);
  AddButton(buttons, "Export HTML", () => ExportAsync(ReportFormat.Html));
  AddButton(buttons, "Export CSV", () => ExportAsync(ReportFormat.Csv));
  AddButton(buttons, "Export JSON", () => ExportAsync(ReportFormat.Json));
  top.Children.Add(history); top.Children.Add(status);
  foreach (var (header, path, width) in new[] { ("Control", "ControlId", 100d), ("Name", "Name", 230d), ("Status", "Status", 150d), ("Captured UTC", "CapturedAt", 190d), ("Stale", "Stale", 60d), ("Reason", "Reason", 450d) })
   controls.Columns.Add(new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(path), Width = width });
  DockPanel.SetDock(controls, Dock.Top); panel.Children.Add(controls); panel.Children.Add(details);
  Loaded += async (_, _) => await GuardAsync(() => LoadPageAsync(true));
 }
 void AddButton(Panel panel, string label, Func<Task> action, bool enabled = true)
 {
  var button = new Button { Content = label, Margin = new Thickness(4), Padding = new Thickness(8, 4, 8, 4), IsEnabled = enabled };
  button.Click += async (_, _) => await GuardAsync(action); panel.Children.Add(button);
 }
 async Task GuardAsync(Func<Task> action)
 {
  if (busy) return; busy = true;
  try { await action(); }
  catch (Exception e) when (e is IOException or UnauthorizedAccessException or EvidenceValidationException or InvalidOperationException or ArgumentException or JsonException)
  { status.Text = "Unable to complete: " + e.Message; }
  finally { busy = false; }
 }
 async Task LoadPageAsync(bool reset)
 {
  if (reset) cursor = null;
  history.Items.Clear(); seen.Clear(); diagnostics.Clear();
  var page = await service.ReadHistoryAsync(cursor, CancellationToken.None);
  foreach (var record in page.Entries) AddRecord(record.Id, record.Kind, record.RecordedAt, record.Context);
  diagnostics.AddRange(page.Diagnostics); cursor = page.NextCursor; next.IsEnabled = cursor is not null;
  status.Text = $"Current page: {seen.Count} records. " + (cursor is null ? "End of enumerated history reached." : "More history exists; use Next history page.") +
   (diagnostics.Count == 0 ? "" : "\nHistory diagnostics:\n" + string.Join("\n", diagnostics.Select(d => d.FileName + ": " + d.Code + " — " + d.Message)));
 }
 void AddRecord(Guid id, string kind, DateTimeOffset time, DeviceContext context)
 {
  if (seen.Add(id)) history.Items.Add(new HistoryItem(id, kind, time, context.ComputerName, context.OriginalUserName));
 }
 Task SelectSessionAsync()
 {
  history.SelectedItems.Clear();
  foreach (var capture in session.Captures)
  {
   AddRecord(capture.Id, "capture", capture.CapturedAt, capture.Context);
   history.SelectedItems.Add(history.Items.Cast<HistoryItem>().Single(i => i.Id == capture.Id));
  }
  status.Text = "Selected saved current-session captures. Build the report to validate and load their original evidence IDs.";
  return Task.CompletedTask;
 }
 async Task BuildAsync()
 {
  report = null; controls.ItemsSource = null; details.Clear();
  var ids = history.SelectedItems.Cast<HistoryItem>().Select(i => i.Id).ToArray();
  report = await service.BuildAsync((ReportPurpose)purpose.SelectedItem, ids, CancellationToken.None);
  controls.ItemsSource = report.Controls;
  status.Text = report.Completion + $" Automated verified controls: {report.VerifiedCount}. Generated {report.GeneratedAt:O}.";
  details.Text = string.Join("\n", report.Limitations) + "\n\n" + ReportExporter.Render(report, ReportFormat.Json);
 }
 async Task ExportAsync(ReportFormat format)
 {
  if (report is null) throw new InvalidOperationException("Build a report from selected saved records first.");
  var extension = format.ToString().ToLowerInvariant();
  var dialog = new SaveFileDialog { Title = "Export dated snapshot report to a new file", Filter = $"{format} report|*.{extension}", DefaultExt = extension, AddExtension = true, FileName = $"device-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.{extension}", OverwritePrompt = true };
  if (dialog.ShowDialog(this) != true) return;
  var receipt = await ReportExporter.ExportAsync(report, format, dialog.FileName, CancellationToken.None);
  status.Text = $"Exported {receipt.Bytes} bytes to {receipt.Destination}. Existing files are never overwritten; choose a new name.";
 }
 async Task RecordManualAsync()
 {
  if (recorder is null) return;
  var dialog = new ManualReviewWindow(session.Catalogue) { Owner = this };
  if (dialog.ShowDialog() != true) return;
  var review = await recorder.RecordAsync(dialog.ControlId, dialog.Outcome, dialog.Note, dialog.Source, dialog.ScopeReason, CancellationToken.None);
  AddRecord(review.Id, "manual-review", review.ReviewedAt, review.Context);
  history.SelectedItems.Add(history.Items.Cast<HistoryItem>().Single(i => i.Id == review.Id));
  report = null; status.Text = "Saved original-engineer manual review. Build the report to apply eligible reviews; failing or unavailable automatic evidence remains unresolved.";
 }
}
