param([string]$Repository,[string]$Scenario)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $Repository 'packaging/Runtime.ps1')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=Join-Path ([IO.Path]::GetTempPath()) ('runtime-fixture-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
function Assert($value,[string]$reason){if(-not $value){throw $reason}}
function HostBytes {
 $bytes=New-Object byte[] 256;$bytes[0]=0x4d;$bytes[1]=0x5a;$bytes[60]=0x80;$bytes[128]=0x50;$bytes[129]=0x45;$bytes[132]=0x64;$bytes[133]=0x86
 return ,$bytes
}
function Layout([string]$directory,[string]$version=$script:ToolkitRuntimeVersion){
 foreach($relative in @('dotnet.exe',('host/fxr/'+$version+'/hostfxr.dll'),('shared/Microsoft.NETCore.App/'+$version+'/coreclr.dll'),('shared/Microsoft.NETCore.App/'+$version+'/System.Private.CoreLib.dll'),('shared/Microsoft.WindowsDesktop.App/'+$version+'/PresentationFramework.dll'))){
  $path=Join-Path $directory $relative;[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path));[IO.File]::WriteAllBytes($path,(HostBytes))
 }
}
function Test-ToolkitElevation {return $false}
try{
 if($Scenario -eq 'discovery'){
  Assert (-not (Test-ToolkitRuntime $root)) 'Empty runtime accepted'
  Layout $root '9.0.9';Assert (-not (Test-ToolkitRuntime $root)) 'Wrong major runtime accepted'
  Layout $root;Assert (Test-ToolkitRuntime $root) 'Complete runtime rejected'
  [IO.File]::Delete((Join-Path $root ('shared/Microsoft.NETCore.App/'+$script:ToolkitRuntimeVersion+'/coreclr.dll')))
  Assert (-not (Test-ToolkitRuntime $root)) 'Incomplete Core runtime accepted'
 }elseif($Scenario -eq 'compatiblepatch'){
  Layout $root '10.0.12'
  Assert (Test-ToolkitRuntime $root) 'Already compatible .NET 10 was treated as missing'
 }elseif($Scenario -eq 'rollforward'){
  Layout $root
  foreach($relative in @('shared/Microsoft.NETCore.App/','host/fxr/')){[IO.Directory]::Move((Join-Path $root ($relative+$script:ToolkitRuntimeVersion)),(Join-Path $root ($relative+'10.0.14')))}
  Assert (Test-ToolkitRuntime $root) 'Compatible independently serviced patches were rejected'
 }elseif($Scenario -eq 'oldercore'){
  Layout $root
  [IO.Directory]::Move((Join-Path $root ('shared/Microsoft.WindowsDesktop.App/'+$script:ToolkitRuntimeVersion)),(Join-Path $root 'shared/Microsoft.WindowsDesktop.App/10.0.14'))
  Assert (-not (Test-ToolkitRuntime $root)) 'Desktop framework accepted an older Core runtime'
 }elseif($Scenario -eq 'selection'){
  $script:globalRoot=Join-Path $root 'global';Layout $script:globalRoot
  function Get-ToolkitGlobalRuntimeRoot {return $script:globalRoot}
  $kit=Join-Path $root 'kit';[void][IO.Directory]::CreateDirectory($kit)
  Assert ((Find-ToolkitRuntime $kit) -eq $script:globalRoot) 'Registered global runtime not reused'
  [void][IO.Directory]::CreateDirectory((Join-Path $kit '.dotnet'))
  $blocked=$false;try{Find-ToolkitRuntime $kit}catch{$blocked=$true};Assert $blocked 'Broken private runtime was ignored despite apphost priority'
  Layout (Join-Path $kit '.dotnet');Assert ((Find-ToolkitRuntime $kit) -eq (Join-Path $kit '.dotnet')) 'Private and apphost selection differ'
 }elseif($Scenario -eq 'elevated'){
  function Test-ToolkitElevation {return $true}
  $blocked=$false;try{Install-ToolkitRuntime $root {param($text)}}catch{$blocked=$_.Exception.Message -match 'without administrator'}
  Assert $blocked 'Elevated runtime installation was allowed'
  Assert (-not (Test-Path (Join-Path $root '.dotnet'))) 'Elevated installation wrote a runtime'
 }else{
  $source=Join-Path $root 'fixture-source';Layout $source
  if($Scenario -eq 'partial'){[IO.File]::Delete((Join-Path $source ('shared/Microsoft.WindowsDesktop.App/'+$script:ToolkitRuntimeVersion+'/PresentationFramework.dll')))}
  $script:fixtureZip=Join-Path $root 'fixture.zip';[IO.Compression.ZipFile]::CreateFromDirectory($source,$script:fixtureZip)
  if($Scenario -eq 'traversal'){
   $archive=[IO.Compression.ZipFile]::Open($script:fixtureZip,[IO.Compression.ZipArchiveMode]::Update)
   try{$entry=$archive.CreateEntry('../escape.txt');$stream=$entry.Open();$stream.WriteByte(1);$stream.Dispose()}finally{$archive.Dispose()}
  }
  $script:fixtureHash=(Get-FileHash $script:fixtureZip -Algorithm SHA512).Hash.ToLowerInvariant()
  if($Scenario -eq 'corrupt'){$script:fixtureHash='0'*128}
  function Get-ToolkitRuntimeDownloads {return ,@([pscustomobject]@{url='https://builds.dotnet.microsoft.com/fixture.zip';sha512=$script:fixtureHash;bytes=(Get-Item $script:fixtureZip).Length})}
  $script:downloads=0
  function Receive-ToolkitRuntimeArchive($item,$destination,$progress){$script:downloads++;[IO.File]::Copy($script:fixtureZip,$destination)}
  $target=Join-Path $root 'kit';[void][IO.Directory]::CreateDirectory($target)
  $failed=$false;try{Install-ToolkitRuntime $target {param($text)}}catch{$failed=$true}
  if($Scenario -eq 'install'){
   Assert (-not $failed) 'Valid private install failed';Assert (Test-ToolkitRuntime (Join-Path $target '.dotnet')) 'Installed runtime is incomplete'
   Install-ToolkitRuntime $target {param($text)};Assert ($script:downloads -eq 1) 'Second launch downloaded again'
  }else{
   Assert $failed 'Invalid runtime installation succeeded';Assert (-not (Test-Path (Join-Path $target '.dotnet'))) 'Failed installation published a runtime'
   Assert (-not (Test-Path (Join-Path $target 'escape.txt'))) 'ZIP escaped staging'
  }
  Assert (@(Get-ChildItem $target -Filter '.dotnet-stage-*').Count -eq 0) 'Temporary runtime files remain'
 }
 Write-Output ('PASS '+$Scenario)
}finally{if(Test-Path $root){Remove-Item -LiteralPath $root -Recurse -Force}}
