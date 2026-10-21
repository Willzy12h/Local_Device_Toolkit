using System.Diagnostics;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class AtomicUserScriptTests
{
 [Fact] public void Live_registry_exposes_atomic_UK_actions_and_refuses_the_legacy_composite()
 {
  var registry=LocalDeviceToolkit.Windows.WindowsOperations.Create(LocalDeviceToolkit.Engine.Catalogue.WorkflowCatalogue.LoadShipped());
  foreach(var suffix in new[]{"formats","home-country","input-language","keyboard","default-keyboard"})Assert.Equal("settings.uk-"+suffix,registry.Find("settings.uk-"+suffix).Id);
  Assert.Throws<LocalDeviceToolkit.Engine.Execution.PlanValidationException>(()=>registry.Find("settings.uk-region-keyboard"));
  Assert.Equal(0,(int)LocalDeviceToolkit.Engine.Settings.UserSettingId.UkRegionKeyboard);Assert.Equal(1,(int)LocalDeviceToolkit.Engine.Settings.UserSettingId.RemoveConsumerCopilot);
 }
 [Fact] public async Task Fixed_UK_script_actions_parse_and_mutate_only_the_selected_target()
 {
  var script=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!,"../../scripts/settings/Invoke-UserSetting.ps1"));
  var root=Path.Combine(Path.GetTempPath(),"atomic-user-"+Guid.NewGuid());Directory.CreateDirectory(root);
  try
  {
   var harness=Path.Combine(root,"test.ps1");await File.WriteAllTextAsync(harness,"""
$ErrorActionPreference='Stop'
$t=$null;$e=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($env:TOOLKIT_USER_SCRIPT,[ref]$t,[ref]$e)
if($e.Count){throw 'User script syntax failed.'}
$switch=$ast.Find({param($n) $n -is [Management.Automation.Language.SwitchStatementAst]},$true)
$targets=@{ApplyUkFormats='Set-Culture';ApplyUkHomeCountry='Set-WinHomeLocation';ApplyUkInputLanguage='Set-WinUserLanguageList';ApplyUkKeyboard='Set-WinUserLanguageList';ApplyUkDefaultKeyboard='Set-WinDefaultInputMethodOverride'}
foreach($name in $targets.Keys){
 $clause=@($switch.Clauses | Where-Object {$_.Item1.Value -ceq $name})
 if($clause.Count -ne 1){throw ('Missing atomic action '+$name)}
 $writes=@($clause[0].Item2.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -like 'Set-*'},$true)|ForEach-Object {$_.GetCommandName()})
 if($writes.Count -ne 1 -or $writes[0] -cne $targets[$name]){throw ('Unexpected write scope '+$name)}
}
if(@($switch.Clauses|Where-Object {$_.Item1.Value -ceq 'ApplyRegion'}).Count){throw 'Legacy composite mutation remains live.'}
$language=@($switch.Clauses|Where-Object {$_.Item1.Value -ceq 'ApplyUkInputLanguage'})[0].Item2.Extent.Text
if($language -notmatch 'InputMethodTips.Clear\(\)'){throw 'New input language must explicitly clear implicit default keyboards.'}
# Execute the actual fixed language branch with synthetic Windows cmdlets.
# This proves New-WinUserLanguageList's implicit keyboard is removed before the write.
function Confirm-PolicySources {}
function Confirm-Before($Current) {}
function Read-Region {return [pscustomobject]@{languages=$script:languages.ToArray()}}
function Get-WinUserLanguageList {return ,$script:languages}
function New-WinUserLanguageList($Language) {
 $tips=New-Object 'System.Collections.Generic.List[string]';$tips.Add('0809:00000809')
 return ,@([pscustomobject]@{LanguageTag='en-GB';InputMethodTips=$tips})
}
function Set-WinUserLanguageList($LanguageList,[switch]$Force,$WarningAction) {$script:writes++;$script:written=$LanguageList}
function Set-WinDefaultInputMethodOverride($InputTip,$WarningAction) {$script:writes++}
$script:languages=New-Object 'System.Collections.Generic.List[object]';$script:writes=0
$branch=@($switch.Clauses|Where-Object {$_.Item1.Value -ceq 'ApplyUkInputLanguage'})[0].Item2
& ([scriptblock]::Create($branch.Extent.Text.Substring(1,$branch.Extent.Text.Length-2)))
if($script:writes -ne 1 -or $script:written.Count -ne 1 -or $script:written[0].InputMethodTips.Count -ne 0){throw 'Adding a UK language also added a keyboard.'}
foreach($name in @('ApplyUkKeyboard','ApplyUkDefaultKeyboard')){
 $script:languages=New-Object 'System.Collections.Generic.List[object]';$script:writes=0;$refused=$false
 $branch=@($switch.Clauses|Where-Object {$_.Item1.Value -ceq $name})[0].Item2
 try{& ([scriptblock]::Create($branch.Extent.Text.Substring(1,$branch.Extent.Text.Length-2)))}catch{$refused=$true}
 if(-not $refused -or $script:writes -ne 0 -or $script:languages.Count -ne 0){throw ('Missing prerequisite was silently added: '+$name)}
}
'PASS: atomic user script' 
""");
   var shell=OperatingSystem.IsWindows()?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell/v1.0/powershell.exe"):Environment.GetEnvironmentVariable("TOOLKIT_TEST_PWSH")??"pwsh";
   var start=new ProcessStartInfo(shell){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};start.Environment["TOOLKIT_USER_SCRIPT"]=script;
   foreach(var arg in new[]{"-NoProfile","-NonInteractive","-File",harness})start.ArgumentList.Add(arg);
   using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();var text=await output+await errors;Assert.True(process.ExitCode==0,text);Assert.Contains("PASS: atomic user script",text);
  }
  finally{Directory.Delete(root,true);}
 }
 static string Source([System.Runtime.CompilerServices.CallerFilePath]string source="")=>source;
}
