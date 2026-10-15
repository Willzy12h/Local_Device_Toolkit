using System.IO.Compression;
using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Windows.Deployments;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class DeploymentFormatTests
{
 const string Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";
 static DeploymentProfile Msix=>new("fixture.msix","APP-WIN-001",DeploymentFormat.CurrentUserMsix,"Fixture.App_8wekyb3d8bbwe","1.2.3.0","Fixture.App",Publisher,OperationScope.CurrentUser,"x64","main.msix",new[]{new DeploymentFile("main.msix",new string('A',64),new string('B',64))},TimeSpan.FromMinutes(15),new("Fixture.App",Publisher,"Fixture.App_8wekyb3d8bbwe"));
 [Fact] public void Publisher_family_hash_matches_the_known_Microsoft_publisher_ID()=>Assert.Equal("Fixture.App_8wekyb3d8bbwe",MsixPackageMetadata.FamilyName("Fixture.App",Publisher));
 [Fact] public void MSIX_requires_original_user_scope_and_every_dependency_signed()
 {DeploymentProfiles.Validate(Msix);Assert.Throws<ArgumentException>(()=>DeploymentProfiles.Validate(Msix with{Scope=OperationScope.Machine}));Assert.Throws<ArgumentException>(()=>DeploymentProfiles.Validate(Msix with{Files=Msix.Files.Concat(new[]{new DeploymentFile("dependency.msix",new string('C',64),null)}).ToArray()}));}
 [Theory][InlineData("Wrong.App",Publisher,"1.2.3.0","x64")][InlineData("Fixture.App","CN=Other","1.2.3.0","x64")][InlineData("Fixture.App",Publisher,"1.2.4.0","x64")][InlineData("Fixture.App",Publisher,"1.2.3.0","arm64")]
 public void MSIX_manifest_identity_drift_is_refused(string name,string publisher,string version,string architecture)
 {using var package=new ZipFixture();package.Add("AppxManifest.xml",Manifest(name,publisher,version,architecture));Assert.Throws<InvalidDataException>(()=>MsixPackageMetadata.Validate(Msix,package.Path));}
 [Fact] public void MSIX_original_signed_identity_and_neutral_architecture_are_accepted()
 {using var package=new ZipFixture();package.Add("AppxManifest.xml",Manifest("Fixture.App",Publisher,"1.2.3.0","neutral"));MsixPackageMetadata.Validate(Msix,package.Path);}
 [Fact] public void MSIX_DTD_is_refused()
 {using var package=new ZipFixture();package.Add("AppxManifest.xml","<!DOCTYPE Package [<!ENTITY x 'boom'>]>"+Manifest("Fixture.App",Publisher,"1.2.3.0","x64"));Assert.Throws<InvalidDataException>(()=>MsixPackageMetadata.Validate(Msix,package.Path));}
 [Fact] public void Office_XML_is_closed_to_local_media_and_explicit_authorised_licensing()
 {var p=Office;DeploymentProfiles.Validate(p);var xml=OfficeDeploymentConfiguration.Create(p,"C:\\Approved Media");Assert.Contains("AllowCdnFallback=\"FALSE\"",xml);Assert.Contains("AcceptEULA=\"TRUE\"",xml);Assert.Contains("Value=\"FALSE\"",xml);Assert.DoesNotContain("PIDKEY",xml);Assert.DoesNotContain("Remove",xml);Assert.Throws<ArgumentException>(()=>DeploymentProfiles.Validate(p with{Office=p.Office! with{LicenseAuthorized=false}}));}
 [Theory][InlineData("OtherProduct","MonthlyEnterprise","en-gb")][InlineData("O365BusinessRetail","Current","en-gb")][InlineData("O365BusinessRetail","MonthlyEnterprise","en-us")]
 public void Office_unknown_product_channel_or_language_is_refused(string product,string channel,string language)=>Assert.Throws<ArgumentException>(()=>DeploymentProfiles.Validate(Office with{Office=new(product,channel,language,true)}));
 [Theory][InlineData("x86","en-gb",false)][InlineData("x64","en-us",false)][InlineData("x64","en-gb",true)]
 public void Office_verification_requires_actual_machine_product_platform_culture_and_channel(string platform,string culture,bool verified)
 {var actual=OfficeInstalledInventory.Evaluate(Office,new("O365BusinessRetail",Office.Version,platform,culture,"http://officecdn.microsoft.com/pr/55336b82-a18d-4dd6-b5f6-9e5095c314a6"));Assert.Equal(verified,actual.Available&&actual.Present&&actual.ProductIdentity==Office.ProductIdentity&&actual.Version==Office.Version);}
 [Fact] public void Office_launch_has_only_configure_and_generated_configuration_path()
 {var start=DeploymentLaunch.Create(Office,Path.GetFullPath("fixture/media"));Assert.Equal(new[]{"/configure",Path.GetFullPath("fixture/media")+".config.xml"},start.ArgumentList);Assert.True(DeploymentLaunch.MayDetach(Office));}
 [Fact] public void A_missing_declared_dependency_is_refused_and_a_complete_signed_set_is_accepted()
 {using var package=new ZipFixture();var main=Manifest("Fixture.App",Publisher,"1.2.3.0","x64").Replace("<Dependencies/>","<Dependencies><PackageDependency Name=\"Fixture.Framework\" Publisher=\""+Publisher+"\" MinVersion=\"1.0.0.0\"/></Dependencies>");package.Add("AppxManifest.xml",main);Assert.Throws<InvalidDataException>(()=>MsixPackageMetadata.Validate(Msix,package.Path));package.AddPackage("dependency.msix",Manifest("Fixture.Framework",Publisher,"1.1.0.0","neutral"));var complete=Msix with{Files=Msix.Files.Concat(new[]{new DeploymentFile("dependency.msix",new string('C',64),new string('B',64))}).ToArray()};MsixPackageMetadata.Validate(complete,package.Path);}
 [Fact] public void Bundle_selected_application_manifest_must_match_approved_identity()
 {using var package=new ZipFixture();package.Add("AppxMetadata/AppxBundleManifest.xml","<Bundle><Identity Name=\"Fixture.App\" Publisher=\""+Publisher+"\" Version=\"1.2.3.0\"/><Packages><Package FileName=\"application.msix\" Architecture=\"x64\" Type=\"application\"/></Packages></Bundle>");package.AddNested("application.msix",Manifest("Wrong.App",Publisher,"1.2.3.0","x64"));Assert.Throws<InvalidDataException>(()=>MsixPackageMetadata.Validate(Msix,package.Path));}
 [Theory][InlineData(false,false)][InlineData(true,true)]
 public void Office_policy_presence_or_unreadable_sources_block_local_deployment(bool readable,bool present)
 {Assert.False(OfficePolicyGate.Evaluate(new[]{new OfficePolicySource("synthetic policy",readable,present)}).Accepted);Assert.True(OfficePolicyGate.Evaluate(new[]{new OfficePolicySource("synthetic policy",true,false)}).Accepted);}
 [Fact] public async Task Boot_identity_is_observed_natively_without_an_invented_clock_value()
 {var boot=await new WindowsDeploymentBackend().GetBootSessionAsync(default);if(OperatingSystem.IsWindows()){Assert.NotNull(boot);Assert.NotEqual(Guid.Empty,boot!.Value);}else Assert.Null(boot);}
 [Fact] public void Bundle_with_matching_x64_application_metadata_is_accepted()
 {using var package=new ZipFixture();package.Add("AppxMetadata/AppxBundleManifest.xml","<Bundle><Identity Name=\"Fixture.App\" Publisher=\""+Publisher+"\" Version=\"1.2.3.0\"/><Packages><Package FileName=\"application.msix\" Architecture=\"x64\" Type=\"application\"/></Packages></Bundle>");package.AddNested("application.msix",Manifest("Fixture.App",Publisher,"1.2.3.0","x64"));MsixPackageMetadata.Validate(Msix,package.Path);}
 static DeploymentProfile Office=>new("fixture.office","APP-WIN-001",DeploymentFormat.OfficeOdt,"O365BusinessRetail","16.0.18025.20104","Microsoft Office Deployment Tool","Microsoft Corporation",OperationScope.Machine,"x64","setup.exe",new[]{new DeploymentFile("setup.exe",new string('A',64),new string('B',64)),new DeploymentFile("Office/Data/16.0.18025.20104/stream.x64.x-none.dat",new string('C',64),null),new DeploymentFile("Office/Data/16.0.18025.20104/stream.x64.en-gb.dat",new string('D',64),null),new DeploymentFile("Office/Data/v64.cab",new string('E',64),null)},TimeSpan.FromHours(1),Office:new("O365BusinessRetail","MonthlyEnterprise","en-gb",true));
 static string Manifest(string name,string publisher,string version,string architecture)=>$"<Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\"><Identity Name=\"{name}\" Publisher=\"{publisher}\" Version=\"{version}\" ProcessorArchitecture=\"{architecture}\"/><Dependencies/></Package>";
 sealed class ZipFixture:IDisposable
 {
  readonly string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid().ToString("N"));public string Path=>System.IO.Path.Combine(root,"main.msix");
  public ZipFixture(){Directory.CreateDirectory(root);}
  public void Add(string name,string value){using var zip=ZipFile.Open(Path,ZipArchiveMode.Update);using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(value);}
  public void AddPackage(string name,string manifest){using var zip=ZipFile.Open(System.IO.Path.Combine(root,name),ZipArchiveMode.Create);using var writer=new StreamWriter(zip.CreateEntry("AppxManifest.xml").Open());writer.Write(manifest);}
  public void AddNested(string name,string manifest){using var buffer=new MemoryStream();using(var inner=new ZipArchive(buffer,ZipArchiveMode.Create,true)){using var writer=new StreamWriter(inner.CreateEntry("AppxManifest.xml").Open());writer.Write(manifest);}using var outer=ZipFile.Open(Path,ZipArchiveMode.Update);using var entry=outer.CreateEntry(name).Open();entry.Write(buffer.ToArray());}
  public void Dispose()=>Directory.Delete(root,true);
 }
}
