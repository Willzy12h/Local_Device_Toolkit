using System.Diagnostics;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class DeploymentProfileDraftTests:IDisposable
{
 readonly string root=Path.Combine(Path.GetTempPath(),"deployment-draft-"+Guid.NewGuid().ToString("N"));
 public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
 static string Script=>ScriptFromSource();
 static string ScriptFromSource([System.Runtime.CompilerServices.CallerFilePath]string source="")
 {
  var script=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"..","..","build","New-DeploymentProfile.ps1"));
  if(!File.Exists(script))throw new FileNotFoundException("Deployment draft helper is missing from the source checkout.",script);
  return script;
 }
 static void ConfigureWindowsPowerShellEnvironment(ProcessStartInfo start)
 {
  // CI invokes dotnet from PowerShell 7; its module path cannot supply Windows PowerShell 5.1 modules.
  start.Environment["PSModulePath"]=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","Modules");
  start.Environment.Remove("PSExecutionPolicyPreference");
 }
 static async Task<(int Exit,string Output)> PowerShell(params string[] arguments)
 {
  var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
  ConfigureWindowsPowerShellEnvironment(start);
  start.Environment["TOOLKIT_DRAFT_SCRIPT"]=Script;
  foreach(var argument in new[]{"-NoProfile","-NonInteractive"}.Concat(arguments))start.ArgumentList.Add(argument);
  using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);throw;}
  return(process.ExitCode,(await output)+(await error));
 }
 [Fact] public void Child_environment_uses_Windows_modules_and_removes_inherited_execution_policy_override()
 {
  var start=new ProcessStartInfo("unused");start.Environment["PSModulePath"]="inherited-powershell-7-modules";start.Environment["PSExecutionPolicyPreference"]="Bypass";
  ConfigureWindowsPowerShellEnvironment(start);
  Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","Modules"),start.Environment["PSModulePath"]);
  Assert.False(start.Environment.ContainsKey("PSExecutionPolicyPreference"));
 }
 [Fact] public async Task Helper_parses_with_installed_Windows_PowerShell_51()
 {
  if(!OperatingSystem.IsWindows())return;
  // Pass the path as process environment data, never interpolate it as PowerShell source.
  var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
  ConfigureWindowsPowerShellEnvironment(start);
  start.Environment["TOOLKIT_DRAFT_SCRIPT"]=Script;
  foreach(var argument in new[]{"-NoProfile","-NonInteractive","-Command","$t=$null;$e=$null;[void][Management.Automation.Language.Parser]::ParseFile($env:TOOLKIT_DRAFT_SCRIPT,[ref]$t,[ref]$e);if($e.Count){$e|Out-String|Write-Error;exit 1};if($PSVersionTable.PSVersion.Major -ne 5){throw 'Windows PowerShell 5.1 required'}"})start.ArgumentList.Add(argument);
  using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();Assert.True(process.ExitCode==0,(await output)+(await error));
 }
 [Theory][InlineData("../setup.msi")][InlineData("folder\\setup.msi")][InlineData("/setup.msi")]
 public async Task Unsafe_relative_entry_points_are_refused_before_output(string entry)
 {
  if(!OperatingSystem.IsWindows())return;
  var source=Path.Combine(root,"source");Directory.CreateDirectory(source);var output=Path.Combine(root,"profile.json");
  var result=await PowerShell("-File",Script,"-SourceDirectory",source,"-EntryPoint",entry,"-ControlId","APP-WIN-001","-Format","MachineMsi","-OutputPath",output);
  Assert.NotEqual(0,result.Exit);Assert.False(File.Exists(output));Assert.Contains("relative",result.Output,StringComparison.OrdinalIgnoreCase);
 }
 [Fact] public async Task Unsigned_installer_is_refused_without_executing_or_creating_a_draft()
 {
  if(!OperatingSystem.IsWindows())return;
  var source=Path.Combine(root,"source");Directory.CreateDirectory(source);var installer=Path.Combine(source,"setup.msi");var bytes=new byte[]{1,2,3,4};await File.WriteAllBytesAsync(installer,bytes);var output=Path.Combine(root,"profile.json");
  var result=await PowerShell("-File",Script,"-SourceDirectory",source,"-EntryPoint","setup.msi","-ControlId","APP-WIN-001","-Format","MachineMsi","-OutputPath",output);
  Assert.NotEqual(0,result.Exit);Assert.Contains("Authenticode",result.Output,StringComparison.OrdinalIgnoreCase);Assert.False(File.Exists(output));Assert.Equal(bytes,await File.ReadAllBytesAsync(installer));
 }
 [Fact] public async Task Msi_metadata_result_excludes_automation_method_status_values()
 {
  if(!OperatingSystem.IsWindows())return;
  Directory.CreateDirectory(root);var harness=Path.Combine(root,"Msi-OutputContract.ps1");
  await File.WriteAllTextAsync(harness,"""
$ErrorActionPreference='Stop'
$t=$null;$e=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($env:TOOLKIT_DRAFT_SCRIPT,[ref]$t,[ref]$e)
foreach($name in @('Release-ComObject','Read-MsiProperty')){
 $definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
 . ([scriptblock]::Create($definition.Extent.Text))
}
$script:fakeRecord=[pscustomobject]@{}
$script:fakeRecord|Add-Member ScriptMethod StringData {param($field) '1.2.3'}
$script:fakeView=[pscustomobject]@{}
$script:fakeView|Add-Member ScriptMethod Execute {0}
$script:fakeView|Add-Member ScriptMethod Close {0}
$script:fakeView|Add-Member ScriptMethod Fetch {$script:fakeRecord}
$database=[pscustomobject]@{}
$database|Add-Member ScriptMethod OpenView {param($query) $script:fakeView}
$actual=Read-MsiProperty $database 'fixed query'
if($actual -isnot [string] -or $actual -cne '1.2.3'){
 $description=[string]::Join('; ',@($actual|ForEach-Object {$_.GetType().FullName+':'+[string]$_}))
 throw ('Expected only scalar string 1.2.3; actual count='+@($actual).Count+' ['+$description+']')
}
'PASS: MSI property reader returns only exact scalar metadata, ignoring Execute/Close return values.'

""");
  var result=await PowerShell("-File",harness);Assert.True(result.Exit==0,result.Output);
 }
 [Fact] public async Task Actual_msi_metadata_reader_uses_read_only_COM_against_a_synthetic_database()
 {
  if(!OperatingSystem.IsWindows())return;
  Directory.CreateDirectory(root);var harness=Path.Combine(root,"Read-MsiFixture.ps1");
  await File.WriteAllTextAsync(harness,"""
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$t=$null;$e=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($env:TOOLKIT_DRAFT_SCRIPT,[ref]$t,[ref]$e)
if($e.Count){throw 'Production helper does not parse'}
foreach($name in @('Release-ComObject','Read-MsiProperty')){
 $definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
 . ([scriptblock]::Create($definition.Extent.Text))
}
function Assert-ExactMsiText($Actual,[string]$Expected,[string]$Label) {
 if($Actual -isnot [string] -or $Actual -cne $Expected){
  $description=[string]::Join('; ',@($Actual|ForEach-Object {if($null -eq $_){'(null)'}else{$_.GetType().FullName+':'+[string]$_}}))
  throw ('Wrong '+$Label+'; expected scalar string '+$Expected+'; actual count='+@($Actual).Count+' ['+$description+']')
 }
}
$path=Join-Path $PSScriptRoot 'synthetic-metadata.msi'
$installer=$null;$database=$null;$summary=$null
try {
 $installer=New-Object -ComObject WindowsInstaller.Installer
 # Creating the fixture never launches an installer or runs MSI actions.
 $database=$installer.OpenDatabase($path,3)
 $view=$database.OpenView('CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` CHAR(0) LOCALIZABLE PRIMARY KEY `Property`)')
 try{$view.Execute();$view.Close()}finally{Release-ComObject $view}
 foreach($query in @(
  'INSERT INTO `Property` (`Property`,`Value`) VALUES (''ProductCode'',''{11111111-2222-3333-4444-555555555555}'')',
  'INSERT INTO `Property` (`Property`,`Value`) VALUES (''ProductVersion'',''1.2.3'')',
  'INSERT INTO `Property` (`Property`,`Value`) VALUES (''ProductName'',''Synthetic metadata fixture'')',
  'INSERT INTO `Property` (`Property`,`Value`) VALUES (''Manufacturer'',''Synthetic publisher'')')){
  $view=$database.OpenView($query)
  try{$view.Execute();$view.Close()}finally{Release-ComObject $view}
 }
 $summary=$database.SummaryInformation(20);$summary.Property(7)='x64;1033';$summary.Persist()
 Release-ComObject $summary;$summary=$null
 $database.Commit();Release-ComObject $database;$database=$null
 $before=(Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
 $database=$installer.OpenDatabase($path,0)
 Assert-ExactMsiText (Read-MsiProperty $database 'SELECT `Value` FROM `Property` WHERE `Property` = ''ProductVersion''') '1.2.3' 'version'
 Assert-ExactMsiText (Read-MsiProperty $database 'SELECT `Value` FROM `Property` WHERE `Property` = ''ProductCode''') '{11111111-2222-3333-4444-555555555555}' 'product code'
 Assert-ExactMsiText (Read-MsiProperty $database 'SELECT `Value` FROM `Property` WHERE `Property` = ''ProductName''') 'Synthetic metadata fixture' 'name'
 Assert-ExactMsiText (Read-MsiProperty $database 'SELECT `Value` FROM `Property` WHERE `Property` = ''Manufacturer''') 'Synthetic publisher' 'publisher'
 $summary=$database.SummaryInformation(0)
 if(([string]$summary.Property(7)).Split(';')[0] -cne 'x64'){throw 'Wrong architecture'}
 Release-ComObject $summary;$summary=$null;Release-ComObject $database;$database=$null
 if((Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $before){throw 'Read-only metadata queries changed fixture bytes'}
}finally{Release-ComObject $summary;Release-ComObject $database;Release-ComObject $installer}
""");
  var result=await PowerShell("-File",harness);Assert.True(result.Exit==0,result.Output);
 }
 [Fact] public async Task Actual_msix_metadata_reader_checks_package_bundle_and_bounded_xml()
 {
  if(!OperatingSystem.IsWindows())return;
  Directory.CreateDirectory(root);var harness=Path.Combine(root,"Read-MsixFixture.ps1");
  await File.WriteAllTextAsync(harness,"""
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$t=$null;$e=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($env:TOOLKIT_DRAFT_SCRIPT,[ref]$t,[ref]$e)
if($e.Count){throw ($e|Out-String)}
foreach($name in @('Assert-SafeRelativePath','Get-MsixFamilyName','Read-MsixMetadata')){
 $definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
 . ([scriptblock]::Create($definition.Extent.Text))
}
$root=Join-Path ([IO.Path]::GetTempPath()) ('msix-draft-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
try{
 function New-Package([string]$Leaf,[string]$Xml,[bool]$Bundle=$false){
  $path=Join-Path $root $Leaf
  $stream=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  $zip=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
  try{
   $name=if($Bundle){'AppxMetadata/AppxBundleManifest.xml'}else{'AppxManifest.xml'}
   $entry=$zip.CreateEntry($name);$writer=New-Object IO.StreamWriter($entry.Open())
   try{$writer.Write($Xml)}finally{$writer.Dispose()}
   if($Bundle){$entry=$zip.CreateEntry('application.msix');$nested=$entry.Open();$nested.Dispose()}
  }finally{$zip.Dispose();$stream.Dispose()}
  return $path
 }
 $xml='<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Identity Name="Fixture.App" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" Version="1.2.3.4" ProcessorArchitecture="x64"/></Package>'
 $path=New-Package 'good.msix' $xml
 $metadata=Read-MsixMetadata $path
 if($metadata.FamilyName -cne 'Fixture.App_8wekyb3d8bbwe' -or $metadata.Version -cne '1.2.3.4'){throw 'Wrong package metadata'}
 $bundle='<Bundle><Identity Name="Fixture.App" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" Version="1.2.3.4"/><Packages><Package Architecture="x64" Type="application" FileName="application.msix"/></Packages></Bundle>'
 $path=New-Package 'good.msixbundle' $bundle $true
 if((Read-MsixMetadata $path).FamilyName -cne 'Fixture.App_8wekyb3d8bbwe'){throw 'Wrong bundle metadata'}
 foreach($bad in @($xml.Replace('x64','arm64'),('<!DOCTYPE Package [<!ENTITY bomb "invalid">]>'+$xml),('<Package>' + ('x'*1048576) + '</Package>'))){
  $path=New-Package ([Guid]::NewGuid().ToString('N')+'.msix') $bad
  $rejected=$false;try{[void](Read-MsixMetadata $path)}catch{$rejected=$true}
  if(-not $rejected){throw 'Unsafe manifest accepted'}
 }
 'PASS: real synthetic ZIP package/bundle identities and Microsoft family; arm64/DTD/oversized manifest rejected; no extraction or installer execution.'
}finally{[IO.Directory]::Delete($root,$true)}

""");
  var result=await PowerShell("-File",harness);Assert.True(result.Exit==0,result.Output);
 }
 [Theory]
 [InlineData("OfficeOdt","O365BusinessRetail","16.0.18025.20104",false,"LicenseAuthorized")]
 [InlineData("OfficeOdt","O365BusinessRetail","1.2.3.4",true,"TargetOfficeVersion")]
 [InlineData("OfficeOdt","","16.0.18025.20104",true,"OfficeProductId")]
 [InlineData("CurrentUserMsix","O365BusinessRetail","",false,"Office options")]
 public async Task New_format_options_fail_closed_before_reading_media(string format,string product,string target,bool licensed,string expected)
 {
  if(!OperatingSystem.IsWindows())return;
  var source=Path.Combine(root,"source");Directory.CreateDirectory(source);var output=Path.Combine(root,"profile.json");
  var args=new List<string>{"-File",Script,"-SourceDirectory",source,"-EntryPoint",format=="OfficeOdt"?"setup.exe":"primary.msix","-ControlId","APP-WIN-001","-Format",format,"-OutputPath",output};
  if(product.Length>0){args.Add("-OfficeProductId");args.Add(product);}
  if(target.Length>0){args.Add("-TargetOfficeVersion");args.Add(target);}
  if(licensed)args.Add("-LicenseAuthorized");
  var result=await PowerShell(args.ToArray());Assert.NotEqual(0,result.Exit);Assert.Contains(expected,result.Output,StringComparison.OrdinalIgnoreCase);Assert.False(File.Exists(output));
 }
 [Fact] public async Task Existing_output_is_never_overwritten()
 {
  if(!OperatingSystem.IsWindows())return;
  var source=Path.Combine(root,"source");Directory.CreateDirectory(source);var output=Path.Combine(root,"profile.json");await File.WriteAllTextAsync(output,"keep this exact content");
  var result=await PowerShell("-File",Script,"-SourceDirectory",source,"-EntryPoint","setup.msi","-ControlId","APP-WIN-001","-Format","MachineMsi","-OutputPath",output);
  Assert.NotEqual(0,result.Exit);Assert.Contains("overwriting",result.Output,StringComparison.OrdinalIgnoreCase);Assert.Equal("keep this exact content",await File.ReadAllTextAsync(output));
 }
}
