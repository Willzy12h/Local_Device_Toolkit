using System.Windows;
using System.Windows.Controls;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Evidence;
using LocalDeviceToolkit.Engine.Settings;
namespace LocalDeviceToolkit.App;
public sealed class RestorationInputWindow:Window
{
 readonly IEvidenceStore store;readonly ListBox runs=new(){MinHeight=140,MaxHeight=230};readonly ComboBox actions=new(){MinWidth=400};readonly TextBlock notice=new(){TextWrapping=TextWrapping.Wrap};readonly CheckBox confirm=new(){Content="I reviewed effective GPO/MDM policy and approve restoring the recorded prior value."};readonly Button add=new(){Content="Add restoration to shared queue",IsEnabled=false};string? cursor;
 sealed record SourceAction(RunRecord Run,PlannedAction Action){public override string ToString()=>Action.Request.OperationId+" / "+Action.Id.ToString("D");}
 sealed record SourceRun(RunRecord Run){public override string ToString()=>$"{Run.FinishedAt:g} / {Run.Id:D}";}
 public RecordedSettingRestorationParameters Parameters{get;private set;}=null!;
 public RestorationInputWindow(IEvidenceStore store)
 {
  this.store=store;Title="Restore a recorded local setting";Width=780;Height=650;MinWidth=600;MinHeight=450;FontSize=14;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var panel=new StackPanel{Margin=new Thickness(24)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  panel.Children.Add(new TextBlock{Text="Select an original verified setting change",FontSize=24,TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(new TextBlock{Text="Restoration uses the exact saved prior value or absence. Machine changes require independent protected-journal evidence. Subsequent drift, policy changes or a different standard block restoration. Installers, language changes and app removal have no automatic undo.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)});
  panel.Children.Add(runs);var next=new Button{Content="Load next history page"};panel.Children.Add(next);panel.Children.Add(new TextBlock{Text="Original action",Margin=new Thickness(0,12,0,4)});panel.Children.Add(actions);panel.Children.Add(notice);panel.Children.Add(confirm);panel.Children.Add(add);panel.Children.Add(new Button{Content="Cancel",IsCancel=true});
  next.Click+=async(_,_)=>await LoadAsync();Loaded+=async(_,_)=>await LoadAsync();runs.SelectionChanged+=async(_,_)=>await SelectRunAsync();actions.SelectionChanged+=(_,_)=>Update();confirm.Checked+=(_,_)=>Update();confirm.Unchecked+=(_,_)=>Update();
  add.Click+=(_,_)=>{if(actions.SelectedItem is SourceAction selected){Parameters=new(selected.Run.PlanId,selected.Action.Id,selected.Run.Id,true,FixedSettingOperation.Definitions.Single(d=>d.OperationId==selected.Action.Request.OperationId).Setting);DialogResult=true;}};
 }
 async Task LoadAsync()
 {
  try{var page=await store.ReadHistoryAsync(cursor,default);runs.Items.Clear();actions.Items.Clear();foreach(var record in page.Entries.Where(r=>r.Kind=="run")){var run=StrictJson.Read<RunRecord>(StrictJson.Bytes(record.Data));if(run.EvidenceComplete&&run.Results.Any(r=>r.Outcome==ActionOutcome.AppliedVerified))runs.Items.Add(new SourceRun(run));}cursor=page.NextCursor;notice.Text=$"History page: {runs.Items.Count} candidate runs. "+(cursor is null?"End of history; next load restarts.":"More history pages available.")+string.Join("; ",page.Diagnostics.Select(d=>d.Message));Update();}
  catch(Exception error){notice.Text="Could not load recorded runs: "+error.Message;}
 }
 async Task SelectRunAsync()
 {
  actions.Items.Clear();if(runs.SelectedItem is not SourceRun selected)return;
  try{var record=await store.LoadAsync(selected.Run.PlanId,default);if(record.Kind!="plan")throw new EvidenceValidationException("The source is not a plan.");var plan=StrictJson.Read<ReviewedPlan>(StrictJson.Bytes(record.Data));foreach(var action in plan.Actions.Where(a=>FixedSettingOperation.Definitions.Any(d=>d.OperationId==a.Request.OperationId)&&selected.Run.Results.Any(r=>r.ActionId==a.Id&&r.Outcome==ActionOutcome.AppliedVerified)))actions.Items.Add(new SourceAction(selected.Run,action));actions.SelectedIndex=actions.Items.Count>0?0:-1;Update();}
  catch(Exception error){notice.Text="Could not load original plan: "+error.Message;}
 }
 void Update()
 {
  add.IsEnabled=confirm.IsChecked==true&&actions.SelectedItem is SourceAction;
  if(actions.SelectedItem is SourceAction selected)notice.Text=$"Plan {selected.Run.PlanId:D}\nOriginal scope: {selected.Action.Readiness.Scope} / {selected.Action.Readiness.Privilege}\nRestore target: "+string.Join("; ",selected.Action.Before.Select(o=>o.CheckId+" = "+o.Value?.GetRawText()));
 }
}
