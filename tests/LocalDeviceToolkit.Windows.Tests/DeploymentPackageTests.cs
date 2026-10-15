using System.Security.Cryptography;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Windows.Deployments;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class DeploymentPackageTests
{
 [Fact] public async Task Staged_dependency_swap_is_refused_even_when_entry_point_is_unchanged()
 {using var temp=new Media();var verifier=new PackageVerifier(new Trust(),new Metadata());await using(var valid=await verifier.OpenVerifiedAsync(temp.Profile,temp.Root,default)){Assert.Equal(2,valid.Files.Count);}File.WriteAllText(Path.Combine(temp.Root,"data.cab"),"substituted cabinet");await Assert.ThrowsAsync<InvalidDataException>(()=>verifier.OpenVerifiedAsync(temp.Profile,temp.Root,default));}
 [Fact] public async Task Unlisted_side_load_file_is_refused()
 {using var temp=new Media();File.WriteAllText(Path.Combine(temp.Root,"helper.dll"),"unreviewed");await Assert.ThrowsAsync<InvalidDataException>(()=>new PackageVerifier(new Trust(),new Metadata()).OpenVerifiedAsync(temp.Profile,temp.Root,default));}
 [Fact] public async Task Trust_failure_is_preserved_and_prevents_staging()
 {using var temp=new Media();await Assert.ThrowsAsync<InvalidDataException>(()=>new PackageVerifier(new Trust(false),new Metadata()).OpenVerifiedAsync(temp.Profile,temp.Root,default));}
 [Fact] public async Task Identity_or_architecture_mismatch_is_refused()
 {using var temp=new Media();await Assert.ThrowsAsync<InvalidDataException>(()=>new PackageVerifier(new Trust(),new Metadata(false)).OpenVerifiedAsync(temp.Profile,temp.Root,default));}
 [Fact] public async Task Reparse_input_is_refused()
 {using var temp=new Media();var original=Path.Combine(temp.Root,"data.cab");var outside=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".cab");File.Move(original,outside);try{File.CreateSymbolicLink(original,outside);await Assert.ThrowsAsync<InvalidDataException>(()=>new PackageVerifier(new Trust(),new Metadata()).OpenVerifiedAsync(temp.Profile,temp.Root,default));}finally{File.Delete(outside);}}
 [Fact] public async Task Stage_reverifies_full_manifest_and_holds_verified_bytes()
 {using var temp=new Media();var destination=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));try{var stager=new PackageStager(new(new Trust(),new Metadata()));await using var staged=await stager.StageIntoAsync(temp.Profile,temp.Root,destination,default);Assert.Equal(temp.Profile.Files.Count,staged.Files.Count);Assert.NotEqual(temp.Root,staged.Root);Assert.Equal("fixture cabinet",await File.ReadAllTextAsync(Path.Combine(staged.Root,"data.cab")));}finally{if(Directory.Exists(destination))Directory.Delete(destination,true);}}
 [Fact] public void MSI_arguments_are_closed_and_never_shell_text()
 {using var temp=new Media();var start=DeploymentLaunch.Create(temp.Profile,temp.Root);Assert.False(start.UseShellExecute);Assert.Contains("/norestart",start.ArgumentList);Assert.Contains("REBOOT=ReallySuppress",start.ArgumentList);Assert.Contains("ALLUSERS=1",start.ArgumentList);Assert.Equal(Path.Combine(temp.Root,"setup.msi"),start.ArgumentList[1]);}
 [Fact] public void OneDrive_is_explicitly_per_machine_and_warns_of_detached_children()
 {using var temp=new Media();var p=temp.Profile with{Format=DeploymentFormat.OneDriveMachineExe,ControlId="APP-WIN-005",ProductIdentity="Microsoft.OneDrive",ProductName="Microsoft OneDrive",Publisher="Microsoft Corporation",EntryPoint="OneDriveSetup.exe",Files=new[]{new DeploymentFile("OneDriveSetup.exe",new string('A',64),new string('B',64))}};DeploymentProfiles.Validate(p);var start=DeploymentLaunch.Create(p,temp.Root);Assert.Equal(new[]{"/allusers","/silent"},start.ArgumentList);Assert.True(DeploymentLaunch.MayDetach(p));}
 [Fact] public async Task Reconciliation_checks_start_time_and_executable_rather_than_only_PID()
 {using var process=System.Diagnostics.Process.GetCurrentProcess();var backend=new WindowsDeploymentBackend();var observation=new DeploymentProcessObservation(DeploymentProcessEnd.StillRunning,process.Id,new DateTimeOffset(process.StartTime.ToUniversalTime()),process.MainModule!.FileName,null,false);Assert.Equal(DeploymentProcessStatus.RunningSameIdentity,await backend.InspectProcessAsync(observation,default));Assert.Equal(DeploymentProcessStatus.IdentityMismatch,await backend.InspectProcessAsync(observation with{StartedAt=observation.StartedAt!.Value.AddSeconds(-1)},default));Assert.Equal(DeploymentProcessStatus.Unknown,await backend.InspectProcessAsync(observation with{ExecutablePath=Path.Combine(Path.GetTempPath(),"different.exe")},default));Assert.Equal(DeploymentProcessStatus.Unknown,await backend.InspectProcessAsync(null,default));}
 [Fact] public void Actual_unsigned_file_is_never_trusted()
 {using var temp=new Media();Assert.False(new WindowsSignatureTrust().Verify(Path.Combine(temp.Root,"setup.msi"),new string('B',64)).Accepted);}
 [Fact] public async Task Deep_unlisted_directory_tree_is_bounded_before_media_inspection()
 {using var temp=new Media();var path=temp.Root;for(var i=0;i<35;i++){path=Path.Combine(path,"nested");Directory.CreateDirectory(path);}await Assert.ThrowsAsync<InvalidDataException>(()=>new PackageVerifier(new Trust(),new Metadata()).OpenVerifiedAsync(temp.Profile,temp.Root,default));}
 [Fact] public async Task Oversized_media_is_refused_before_an_unbounded_hash_read()
 {using var temp=new Media();using(var file=new FileStream(Path.Combine(temp.Root,"data.cab"),FileMode.Open,FileAccess.Write))file.SetLength(1025);using var stop=new CancellationTokenSource(TimeSpan.FromMilliseconds(100));await Assert.ThrowsAsync<InvalidDataException>(()=>new PackageVerifier(new Trust(),new Metadata(),maximumFileBytes:1024).OpenVerifiedAsync(temp.Profile,temp.Root,stop.Token));}
 [Fact] public async Task Closed_Microsoft_vendor_profile_rejects_a_trusted_different_publisher()
 {using var temp=new Media();File.Move(Path.Combine(temp.Root,"setup.msi"),Path.Combine(temp.Root,"setup.exe"));File.Delete(Path.Combine(temp.Root,"data.cab"));var p=temp.Profile with{Format=DeploymentFormat.OneDriveMachineExe,ControlId="APP-WIN-005",ProductIdentity="Microsoft.OneDrive",ProductName="Microsoft OneDrive",Publisher="Microsoft Corporation",EntryPoint="setup.exe",Files=new[]{temp.Profile.Files[0] with{RelativePath="setup.exe"}}};await Assert.ThrowsAsync<InvalidDataException>(()=>new PackageVerifier(new Trust(true,"Other Vendor"),new Metadata()).OpenVerifiedAsync(p,temp.Root,default));}
 sealed class Trust(bool accepted=true,string? organization=null):IPackageSignatureTrust{public SignatureTrust Verify(string path,string expected)=>new(accepted,accepted?"fixture trust":"fixture trust rejected",organization);}
 sealed class Metadata(bool accepted=true):IInstallerMetadata{public void Validate(DeploymentProfile p,string path){if(!accepted)throw new InvalidDataException("Package metadata mismatch.");}}
 sealed class Media:IDisposable
 {
  public string Root{get;}=Path.Combine(Path.GetTempPath(),"部署 media "+Guid.NewGuid().ToString("N"));public DeploymentProfile Profile{get;}
  public Media(){Directory.CreateDirectory(Root);File.WriteAllText(Path.Combine(Root,"setup.msi"),"fixture signed msi");File.WriteAllText(Path.Combine(Root,"data.cab"),"fixture cabinet");Profile=new("example.msi","APP-WIN-001",DeploymentFormat.MachineMsi,"{11111111-2222-3333-4444-555555555555}","1.2.3","Example","Example Ltd",OperationScope.Machine,"x64","setup.msi",new[]{new DeploymentFile("setup.msi",Hash("setup.msi"),new string('B',64)),new DeploymentFile("data.cab",Hash("data.cab"),null)},TimeSpan.FromMinutes(15));}
  string Hash(string name)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root,name))));public void Dispose(){if(Directory.Exists(Root))Directory.Delete(Root,true);}
 }
}
