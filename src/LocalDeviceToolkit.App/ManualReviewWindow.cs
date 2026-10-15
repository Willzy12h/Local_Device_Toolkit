using System.Windows;
using System.Windows.Controls;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Reporting;
namespace LocalDeviceToolkit.App;
public sealed class ManualReviewWindow : Window
{
 readonly ComboBox control;
 readonly ComboBox outcome = new() { ItemsSource = Enum.GetValues<ManualReviewOutcome>(), SelectedIndex = 0 };
 readonly TextBox note = new() { MaxLength = 4000, Height = 140, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
 readonly TextBox source = new() { MaxLength = 1000 };
 readonly TextBox scope = new() { MaxLength = 4000, Height = 70, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
 public ManualReviewWindow(LocalCatalogue catalogue)
 {
  Title = "Record manual verification"; Width = 680; Height = 660; MinWidth = 500; MinHeight = 560; FontSize = 14; WindowStartupLocation = WindowStartupLocation.CenterOwner;
  var panel = new StackPanel { Margin = new Thickness(24) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
  panel.Children.Add(new TextBlock { Text = "Record what you actually reviewed. This creates dated evidence under the original engineer's SID. It does not establish client approval, resolve missing machine checks, or override an automatic failure.", TextWrapping = TextWrapping.Wrap });
  control = new ComboBox { ItemsSource = catalogue.Controls, DisplayMemberPath = "Name", SelectedIndex = 0 };
  Add(panel, "Control", control); Add(panel, "Outcome", outcome); Add(panel, "Review note (up to 4000 characters)", note); Add(panel, "Source / procedure used", source); Add(panel, "Explicit scope and reason (required for excluded / not applicable)", scope);
  var instructions = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) }; panel.Children.Add(instructions);
  void Update() { instructions.Text = ((LocalControl)control.SelectedItem).ManualInstructions; }
  control.SelectionChanged += (_, _) => Update(); Update();
  var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
  var buttons = new WrapPanel(); panel.Children.Add(buttons);
  var save = new Button { Content = "Save recorded review", Margin = new Thickness(4), Padding = new Thickness(8) }; buttons.Children.Add(save);
  save.Click += (_, _) =>
  {
   if (string.IsNullOrWhiteSpace(Note) || string.IsNullOrWhiteSpace(Source) || (Outcome is ManualReviewOutcome.NotApplicable or ManualReviewOutcome.Excluded && string.IsNullOrWhiteSpace(ScopeReason)))
   { error.Text = "Provide a note and source, and an explicit scope/reason for excluded or not applicable items."; return; }
   DialogResult = true;
  };
  buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(4), Padding = new Thickness(8) });
 }
 static void Add(Panel panel, string label, Control value) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 4) }); System.Windows.Automation.AutomationProperties.SetName(value, label); panel.Children.Add(value); }
 public string ControlId => ((LocalControl)control.SelectedItem).Id;
 public ManualReviewOutcome Outcome => (ManualReviewOutcome)outcome.SelectedItem;
 public string Note => note.Text;
 public string Source => source.Text;
 public string? ScopeReason => string.IsNullOrWhiteSpace(scope.Text) ? null : scope.Text;
}
