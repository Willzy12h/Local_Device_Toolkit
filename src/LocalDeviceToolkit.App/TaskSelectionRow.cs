using System.ComponentModel;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Workbench;
namespace LocalDeviceToolkit.App;
public sealed class TaskSelectionRow:INotifyPropertyChanged
{
 public TaskSelectionRow(TaskDefinition definition,WorkflowOrigin origin){Definition=definition;Origin=origin;Preview=ActionPreviewFormatter.Describe(definition,null,[]);}
 public TaskDefinition Definition{get;} public WorkflowOrigin Origin{get;set;}
 bool selected,change;public bool Selected {get=>selected;set {if(selected==value)return;selected=value;Changed(nameof(Selected));Changed(nameof(ActionLabel));}}
 public bool ChangeRequested {get=>change;set {if(change==value)return;change=value;Changed(nameof(ChangeRequested));Changed(nameof(ActionLabel));}}
 public string ActionLabel=>Selected?(ChangeRequested?"Change":"Check only"):CanChange?"Change":"Check only";
 public bool CanChange=>Definition.OperationId is not null;
 public bool NeedsInputs=>Definition.OperationId is "diagnostics.refresh-dns-cache" or "diagnostics.update-defender-signatures"||Definition.OperationId?.StartsWith("settings.power.",StringComparison.Ordinal)==true||LocalProbeValues.IsRegistered(Definition.Id)&&LocalDeviceToolkit.Engine.Catalogue.LocalProbeCatalogue.Resolve(Definition.Id).InputSchema!="none"||Definition.OperationId is "packages.install" or "settings.restore-recorded";
 public ActionPreview Preview{get;private set;}
 public string Current=>Preview.Current;public string Expected=>Preview.Expected;public string Status=>ActionPreviewFormatter.StatusLabel(Preview.Status);public string Account=>Preview.Account;
 public void Update(ActionPreview preview){Preview=preview;foreach(var name in new[]{nameof(Current),nameof(Expected),nameof(Status),nameof(Account)})Changed(name);}
 public event PropertyChangedEventHandler? PropertyChanged;void Changed(string name)=>PropertyChanged?.Invoke(this,new(name));
}
