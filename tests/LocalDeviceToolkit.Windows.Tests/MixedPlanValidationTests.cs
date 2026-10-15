using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Engine.Execution;
using LocalDeviceToolkit.Engine.Planning;
using LocalDeviceToolkit.Windows.Deployments;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class MixedPlanValidationTests
{
 [Fact] public async Task Worker_validates_the_whole_mixed_plan_without_authorizing_user_package_execution()
 {
  var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
  try
  {
   var name="Toolkit.Synthetic";var publisher="CN=Toolkit Synthetic";var family=MsixIdentity.FamilyName(name,publisher);
   var profile=ApprovedProfileStore.WithContentId(new("","APP-WIN-002",DeploymentFormat.CurrentUserMsix,family,"1.0.0.0","Synthetic",publisher,OperationScope.CurrentUser,"x64","app.msix",[new("app.msix",new string('a',64),new string('b',64))],TimeSpan.FromMinutes(10),new(name,publisher,family)));
   var store=new ApprovedProfileStore(root);await store.SaveAsync(profile,default);var package=new ApprovedPackageOperation(store,new FileDeploymentPendingStore(Path.Combine(root,"pending")),false);
   var context=new DeviceContext("device","PC","S-1-5-21-123","engineer",false,new Version(10,0,26200),"x64",true);var now=DateTimeOffset.UtcNow;
   var before=new ProbeObservation[]{new("CFG-WIN-007","setting.LongPaths",ObservationState.Known,StrictJson.Value(new{exists=false,value=(int?)null}),"fixture",now,"fixture")};
   var machine=new PlannedAction(Guid.NewGuid(),new("control.cfg-win-007","settings.long-paths",StrictJson.Value(new{confirmManagedPolicyReview=true}),new("setup","windows")),new(true,OperationScope.Machine,PrivilegeRequirement.Administrator,"fixture"),before,ObservationFingerprint.Of(before));
   var user=machine with{Id=Guid.NewGuid(),Request=new("apps.installation","packages.install",StrictJson.Value(new DeploymentParameters(profile.Id,Path.GetFullPath("media"))),new("setup","applications")),Readiness=new(true,OperationScope.CurrentUser,PrivilegeRequirement.CurrentUser,"fixture")};
   var catalogue=CatalogueLoader.LoadShipped();var plan=new ReviewedPlan(Guid.NewGuid(),context,catalogue.Identity,now,now.AddMinutes(10),[machine,user],"");plan=plan with{Digest=CanonicalJson.Digest(plan)};
   var registry=new OperationRegistry(WorkflowCatalogue.LoadShipped(),[new MachineOperation(),package]);var validation=new PlanService(registry,TimeProvider.System).ValidateAction(plan,machine,context,catalogue.Identity,before,now);
   Assert.True(validation.IsValid,string.Join(" ",validation.Reasons));
   Assert.False((await package.InspectAsync(context,user.Request.Parameters,default)).CanExecute);
   Assert.Equal(ActionOutcome.Failed,(await package.ExecuteAsync(context,user,default)).Outcome);
  }
  finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
 sealed class MachineOperation:ILocalOperation
 {
  public string Id=>"settings.long-paths";public void ValidateParameters(System.Text.Json.JsonElement p){}
  public string GetTargetKey(DeviceContext c,System.Text.Json.JsonElement p)=>c.DeviceId+"/LongPaths";
  public Task<ActionReadiness> InspectAsync(DeviceContext c,System.Text.Json.JsonElement p,CancellationToken ct)=>throw new NotSupportedException();
  public Task<IReadOnlyList<ProbeObservation>> CaptureBeforeAsync(DeviceContext c,System.Text.Json.JsonElement p,CancellationToken ct)=>throw new NotSupportedException();
  public Task<ActionResult> ExecuteAsync(DeviceContext c,PlannedAction p,CancellationToken ct)=>throw new NotSupportedException();
 }
}
