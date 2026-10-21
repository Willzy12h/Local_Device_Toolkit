# This small WPF startup window uses Windows' built-in .NET Framework.
# The main application starts only after both .NET 10 runtimes are available.
param([string]$SmokeOutput,[string]$PreviewOutput,[switch]$Silent,[string]$Preset='all',[string]$ConfigPath,[string]$InputsPath)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Runtime.ps1')
function Initialize-ToolkitRuntime([string]$Root,[scriptblock]$Progress){
 if(Test-Path -LiteralPath (Join-Path $Root 'coreclr.dll')){& $Progress 'Using the bundled offline runtime.';return}
 $runtime=Find-ToolkitRuntime $Root
 if(-not $runtime){Install-ToolkitRuntime $Root $Progress;$runtime=Find-ToolkitRuntime $Root}
 if(-not $runtime){throw 'A complete .NET 10 Desktop Runtime could not be verified.'}
}
function Invoke-ToolkitSilent([string]$Root,[string]$Preset,[string]$ConfigPath,[string]$InputsPath){
 $log=$null
 try{
  $logs=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'LocalDeviceToolkit/logs'
  Assert-ToolkitRuntimePath $logs
  [void][IO.Directory]::CreateDirectory($logs)
  $path=Join-Path $logs ('startup-silent-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')+'-'+[Guid]::NewGuid().ToString('N')+'.log')
  $log=New-Object IO.StreamWriter((New-Object IO.FileStream($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)),(New-Object Text.UTF8Encoding($false)))
  $log.AutoFlush=$true
  if($Preset -cnotin @('all','fast')){$log.WriteLine('Invalid preset. Use all or fast.');return 64}
  if([IntPtr]::Size -ne 8){throw 'Use 64-bit Windows PowerShell.'}
  $arguments='--silent --preset '+$Preset
  foreach($item in @(@('--config',$ConfigPath),@('--inputs',$InputsPath))){
   if($item[1]){if($item[1] -match '["\x00-\x1f]'){return 64};$arguments+=' '+$item[0]+' "'+[IO.Path]::GetFullPath($item[1]).TrimEnd('\')+'"'}
  }
  Initialize-ToolkitRuntime $Root {param($text) $log.WriteLine([DateTimeOffset]::UtcNow.ToString('o')+' '+$text)}
  $start=New-Object Diagnostics.ProcessStartInfo
  $start.FileName=Join-Path $Root 'LocalDeviceToolkit.App.exe';$start.WorkingDirectory=$Root;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
  $start.Arguments=$arguments;$start.EnvironmentVariables['DOTNET_DISABLE_GUI_ERRORS']='1'
  foreach($variable in @('DOTNET_ROOT','DOTNET_ROOT_X64','DOTNET_ROLL_FORWARD','DOTNET_ROLL_FORWARD_TO_PRERELEASE','DOTNET_ROLL_FORWARD_ON_NO_CANDIDATE_FX')){$start.EnvironmentVariables.Remove($variable)}
  $log.WriteLine([DateTimeOffset]::UtcNow.ToString('o')+' Starting read-only diagnostics: '+$Preset)
  $app=[Diagnostics.Process]::Start($start);if($null -eq $app){throw 'Windows did not start diagnostic checks.'}
  try{$app.WaitForExit();$code=$app.ExitCode}finally{$app.Dispose()}
  $log.WriteLine([DateTimeOffset]::UtcNow.ToString('o')+' Diagnostics exit='+$code);return $code
 }catch{if($null -ne $log){$log.WriteLine([DateTimeOffset]::UtcNow.ToString('o')+' ERROR '+$_.Exception.ToString())};return 1}
 finally{if($null -ne $log){$log.Dispose()}}
}
function Show-ToolkitStartup([string]$Root,[string]$SmokeOutput,[string]$PreviewOutput){
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
$script:cancelled=$false;$script:started=$false;$script:failed=$false;$script:lastStatus=''
$logs=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'LocalDeviceToolkit/logs'
[void][IO.Directory]::CreateDirectory($logs)
$logPath=Join-Path $logs ('startup-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')+'-'+[Guid]::NewGuid().ToString('N')+'.log')
$stream=New-Object IO.FileStream($logPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
$script:log=New-Object IO.StreamWriter($stream,(New-Object Text.UTF8Encoding($false)))
$script:log.AutoFlush=$true
[xml]$xaml=@'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Title="Local Device Toolkit" Width="620" Height="340" WindowStartupLocation="CenterScreen" ResizeMode="NoResize" FontFamily="Segoe UI" FontSize="14" Background="White">
 <StackPanel Margin="24">
  <TextBlock Text="Starting Local Device Toolkit" FontSize="23" FontWeight="SemiBold" Margin="0,0,0,16"/>
  <TextBlock Name="Status" Text="Checking .NET 10 Desktop Runtime..." TextWrapping="Wrap" Height="72"/>
  <ProgressBar Name="Progress" Height="6" IsIndeterminate="True" Margin="0,8,0,12"/>
  <TextBlock Text="Runs as your current account. Missing runtime components install automatically for this toolkit. No Windows restart is needed." TextWrapping="Wrap" Foreground="#555555" Margin="0,0,0,12"/>
  <Button Name="CloseButton" Content="Cancel" Width="90" Height="30" HorizontalAlignment="Right"/>
 </StackPanel>
</Window>
'@
$window=[Windows.Markup.XamlReader]::Load((New-Object Xml.XmlNodeReader($xaml)))
$status=$window.FindName('Status');$bar=$window.FindName('Progress');$close=$window.FindName('CloseButton')
function Update-ToolkitStartup([string]$Text){
 if($script:cancelled){throw (New-Object OperationCanceledException('Startup cancelled.'))}
 $status.Text=$Text
 if($Text -cne $script:lastStatus){$script:log.WriteLine([DateTimeOffset]::UtcNow.ToString('o')+' '+$Text);$script:lastStatus=$Text}
 # Downloads are asynchronous; pump WPF while waiting without a background PowerShell runspace.
 [void]$window.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::Background)
 if($script:cancelled){throw (New-Object OperationCanceledException('Startup cancelled.'))}
}
$close.Add_Click({$window.Close()})
$window.Add_Closing({$script:cancelled=$true})
$window.Add_ContentRendered({
 if($script:started){return};$script:started=$true
 try{
  if($PreviewOutput){
   $path=[IO.Path]::GetFullPath($PreviewOutput);if(Test-Path -LiteralPath $path){throw 'Preview output already exists.'}
   $bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap([int]$window.ActualWidth,[int]$window.ActualHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32)
   $bitmap.Render($window);$encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder;$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
   $output=[IO.File]::Open($path,[IO.FileMode]::CreateNew);try{$encoder.Save($output)}finally{$output.Dispose()};$window.Close();return
  }
  if([IntPtr]::Size -ne 8){throw 'Start.cmd must run 64-bit Windows PowerShell.'}
  Update-ToolkitStartup 'Checking .NET 10 Desktop and Core runtimes...'
  Initialize-ToolkitRuntime $Root {param($text) Update-ToolkitStartup $text}
  Update-ToolkitStartup 'Runtime ready. Opening the toolkit; no restart is required.'
  $start=New-Object Diagnostics.ProcessStartInfo
  $start.FileName=Join-Path $Root 'LocalDeviceToolkit.App.exe';$start.WorkingDirectory=$Root;$start.UseShellExecute=$false
  # Apphosts use the private relative folder or Windows' registered global location.
  # Ignore inherited custom runtime roots for consistent UI/UAC-worker discovery.
  foreach($variable in @('DOTNET_ROOT','DOTNET_ROOT_X64','DOTNET_ROLL_FORWARD','DOTNET_ROLL_FORWARD_TO_PRERELEASE','DOTNET_ROLL_FORWARD_ON_NO_CANDIDATE_FX')){
   [Environment]::SetEnvironmentVariable($variable,$null,[EnvironmentVariableTarget]::Process)
   $start.EnvironmentVariables.Remove($variable)
  }
  if($SmokeOutput){if($SmokeOutput -match '["\x00-\x1f]'){throw 'Invalid smoke output path.'};$start.Arguments='--smoke-output "'+[IO.Path]::GetFullPath($SmokeOutput)+'"'}
  $app=[Diagnostics.Process]::Start($start);if($null -eq $app){throw 'Windows did not start the toolkit.'}
  try{if($SmokeOutput){if(-not $app.WaitForExit(60000)){throw 'Native toolkit smoke timed out.'};if($app.ExitCode -ne 0){throw 'Native toolkit smoke failed.'}}}finally{$app.Dispose()}
  $window.Close()
 }catch{
  $script:failed=$true;$bar.IsIndeterminate=$false;$close.Content='Close'
  $message=$_.Exception.GetBaseException().Message
  $script:log.WriteLine([DateTimeOffset]::UtcNow.ToString('o')+' ERROR '+$message)
  if(-not $script:cancelled){$status.Text=$message+' Startup log: '+$logPath}
  if($SmokeOutput -or $PreviewOutput){$window.Close()}
 }
})
try{[void]$window.ShowDialog()}finally{$script:log.Dispose()}
if($script:failed){return 2} # An error was already displayed in the startup window.
return 0
}
if($MyInvocation.InvocationName -ne '.'){
 if($Silent){if($SmokeOutput -or $PreviewOutput){exit 64};exit (Invoke-ToolkitSilent $PSScriptRoot $Preset $ConfigPath $InputsPath)}
 exit (Show-ToolkitStartup $PSScriptRoot $SmokeOutput $PreviewOutput)
}
