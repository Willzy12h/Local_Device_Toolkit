using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Planning;
namespace LocalDeviceToolkit.Windows;
public static class WindowsOperations
{
 public static OperationRegistry Create(WorkflowCatalogue workflows,int? originalProcessId=null,LocalDeviceToolkit.Engine.Evidence.IEvidenceStore? settingSource=null,bool protectedAuthority=false,Deployments.ApprovedProfileStore? deploymentProfiles=null,LocalDeviceToolkit.Engine.Deployments.IDeploymentPendingStore? deploymentPending=null)
 {
  var settings=LocalDeviceToolkit.Engine.Settings.FixedSettingOperation.Definitions.Select(d=>(LocalDeviceToolkit.Engine.Execution.ILocalOperation)new LocalDeviceToolkit.Engine.Settings.FixedSettingOperation(d,new Settings.WindowsSettingsBackend(originalProcessId)));
  var users=Enum.GetValues<LocalDeviceToolkit.Engine.Settings.UserSettingId>().Where(id=>id!=LocalDeviceToolkit.Engine.Settings.UserSettingId.UkRegionKeyboard).Select(id=>(LocalDeviceToolkit.Engine.Execution.ILocalOperation)new LocalDeviceToolkit.Engine.Settings.UserSettingOperation(id,new Settings.WindowsUserSettingsBackend(Path.Combine(AppContext.BaseDirectory,"scripts"))));
  var operations=settings.Concat(users);
  if(settingSource is not null)operations=operations.Append(new LocalDeviceToolkit.Engine.Settings.RecordedSettingRestorationOperation(new LocalDeviceToolkit.Engine.Settings.EvidenceRecordedSettingSource(settingSource,protectedAuthority),new Settings.WindowsSettingsBackend(originalProcessId),CatalogueLoader.LoadShipped().Identity));
  if(deploymentProfiles is not null&&deploymentPending is not null)operations=operations.Append(new Deployments.ApprovedPackageOperation(deploymentProfiles,deploymentPending,!protectedAuthority));
  return new(workflows,operations);
 }
}
