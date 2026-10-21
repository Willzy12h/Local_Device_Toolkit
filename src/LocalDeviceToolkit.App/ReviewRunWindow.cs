using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LocalDeviceToolkit.Engine.Workbench;
namespace LocalDeviceToolkit.App;
public sealed class ReviewRunWindow:Window
{
 readonly System.Windows.Threading.DispatcherTimer timer;
 sealed record ReviewDisplayRow(ReviewRow Row){public ActionPreview Preview=>Row.AfterPreview??Row.Preview;public string Status=>ActionPreviewFormatter.StatusLabel(Preview.Status);}
 public ReviewRunWindow(ReviewRun? run,Func<Task>? apply=null)
 {
  Title="Review selected tasks";Width=1080;Height=760;MinWidth=760;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new DockPanel{Margin=new Thickness(18)};Content=panel;
  var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);panel.Children.Add(bottom);
  var policy=new CheckBox{Content="I checked company policy for these changes",Margin=new Thickness(0,10,0,10),Visibility=run?.ChangesCount>0?Visibility.Visible:Visibility.Collapsed};bottom.Children.Add(policy);
  var buttons=new WrapPanel();var execute=new Button{Content=$"Run {run?.ChangesCount??0} changes",IsEnabled=false,Visibility=run?.ChangesCount>0?Visibility.Visible:Visibility.Collapsed};var close=new Button{Content="Close",IsCancel=true};buttons.Children.Add(execute);buttons.Children.Add(close);bottom.Children.Add(buttons);
  policy.Checked+=(_,_)=>execute.IsEnabled=run?.CanApply==true&&apply is not null;policy.Unchecked+=(_,_)=>execute.IsEnabled=false;
  var log=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=100,MaxHeight=260};
  bool started=false;
  Closing+=(_,e)=>{if(started&&!close.IsEnabled)e.Cancel=true;};
  var heading=new TextBlock{Text=run is null?"No check yet. Select tasks and run checks to see measured values.":"Review current and expected values. Already correct items are skipped; blocked items cannot run. Reviews expire after ten minutes.",Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(heading,Dock.Top);panel.Children.Add(heading);
  var grid=new Grid{MinHeight=360};grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});panel.Children.Add(new ScrollViewer{Content=grid,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
  var displayedRows=run?.Rows.ToArray()??[];
  var table=new DataGrid{IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,MinHeight=130,MaxHeight=300,ItemsSource=displayedRows.Select(r=>new ReviewDisplayRow(r)).ToArray()};
  foreach(var (label,path,width) in new[]{("Task","Row.Task.Title",2d),("Current","Preview.Current",1d),("Expected","Preview.Expected",1d),("Status","Status",1d),("Account","Preview.Account",1d)})table.Columns.Add(new DataGridTextColumn{Header=label,Binding=new Binding(path),Width=new DataGridLength(width,DataGridLengthUnitType.Star)});
  System.Windows.Automation.AutomationProperties.SetName(table,"Selected task review: current, expected, status and account");grid.Children.Add(table);
  var detail=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=130,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Text="Select a row for exact source and check details."};Grid.SetRow(detail,1);grid.Children.Add(detail);
  table.SelectionChanged+=(_,_)=>{if(table.SelectedItem is ReviewDisplayRow display){var row=display.Row;var preview=display.Preview;detail.Text=$"{row.Task.Title}\n{ActionPreviewFormatter.StatusLabel(preview.Status)} • {(row.ChangeRequested?"Change requested":"Check only")}\n{preview.Explanation}\nSource: {preview.Source}\nAccount: {preview.Account}";}};
  if(table.Items.Count>0)table.SelectedIndex=0;
  System.Windows.Automation.AutomationProperties.SetName(log,"Live dated check and change log");Grid.SetRow(log,2);grid.Children.Add(log);
  void Refresh()
  {
   log.Text=run is null?"No log created. No checks have run.":run.FilePath+Environment.NewLine+run.LogText;
   var rows=run?.Rows.ToArray()??[];
   if(rows.Length==displayedRows.Length&&rows.Select((row,index)=>ReferenceEquals(row,displayedRows[index])).All(same=>same))return;
   var selectedTask=(table.SelectedItem as ReviewDisplayRow)?.Row.Task.Id;
   var scroll=FindScroll(table);var vertical=scroll?.VerticalOffset??0;var horizontal=scroll?.HorizontalOffset??0;
   displayedRows=rows;table.ItemsSource=rows.Select(row=>new ReviewDisplayRow(row)).ToArray();
   table.SelectedItem=table.Items.Cast<ReviewDisplayRow>().FirstOrDefault(row=>row.Row.Task.Id==selectedTask)??table.Items.Cast<ReviewDisplayRow>().FirstOrDefault();
   table.UpdateLayout();scroll?.ScrollToVerticalOffset(vertical);scroll?.ScrollToHorizontalOffset(horizontal);
  }
  execute.Click+=async(_,_)=>{if(started||apply is null)return;started=true;execute.IsEnabled=false;policy.IsEnabled=false;close.IsEnabled=false;try{await apply();execute.Content="Run finished — see results";}catch(Exception error){execute.Content="Run stopped";MessageBox.Show(this,error.Message,"Run did not complete",MessageBoxButton.OK,MessageBoxImage.Warning);}finally{close.IsEnabled=true;Refresh();}};
  timer=new(){Interval=TimeSpan.FromMilliseconds(500)};timer.Tick+=(_,_)=>Refresh();Closed+=(_,_)=>timer.Stop();Refresh();timer.Start();
 }
 static ScrollViewer? FindScroll(DependencyObject parent)
 {
  if(parent is ScrollViewer scroll)return scroll;
  for(var index=0;index<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);index++)
   if(FindScroll(System.Windows.Media.VisualTreeHelper.GetChild(parent,index)) is {} found)return found;
  return null;
 }
}
