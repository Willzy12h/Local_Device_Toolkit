# Native Windows PowerShell 5.1 bootstrap; no downloaded script is executed.
# Release pins: https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json
Set-StrictMode -Version Latest
$script:ToolkitRuntimeVersion='10.0.13'
$script:ToolkitRuntimeMinimumVersion='10.0.0'
function Get-ToolkitRuntimeDownloads {
 return @(
  [pscustomobject]@{url='https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.13/dotnet-runtime-10.0.13-win-x64.zip';sha512='2b98792ce86adc50899764ad2e4d21bd25cfda5432eeb0b4dfe7944f3ea049f9e0570a8f4aee92314d97cdb5f8b7be1363d501fc5012e680371e607af648ef40';bytes=36970749L}
  [pscustomobject]@{url='https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.13/windowsdesktop-runtime-10.0.13-win-x64.zip';sha512='7cd78051b34f12239852e3685de57630aaeca0fd36e6ef3b41df3f256cf45a820377f316696c5e11316f0bb6e4e84e07fa99367e71472623b33c5d07405c4568';bytes=39169839L}
 )
}
function Assert-ToolkitRuntimePath([string]$Path){
 $current=[IO.Path]::GetFullPath($Path)
 while($current){
  if(([IO.File]::Exists($current) -or [IO.Directory]::Exists($current)) -and (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0)){throw 'Runtime paths must not contain links or junctions.'}
  $current=[IO.Path]::GetDirectoryName($current)
 }
}
function Test-ToolkitRuntime([string]$Root){
 try{
  Assert-ToolkitRuntimePath $Root
  $hostPath=Join-Path $Root 'dotnet.exe';if(-not [IO.File]::Exists($hostPath)){return $false}
  Assert-ToolkitRuntimePath $hostPath
  $reader=New-Object IO.BinaryReader([IO.File]::OpenRead($hostPath))
  try{
   if($reader.ReadUInt16() -ne 0x5a4d){return $false};$reader.BaseStream.Position=60;$offset=$reader.ReadInt32()
   if($offset -lt 64 -or $offset -gt 1048576 -or $offset+6 -gt $reader.BaseStream.Length){return $false}
   $reader.BaseStream.Position=$offset;if($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664){return $false}
  }finally{$reader.Dispose()}
  $desktop=Join-Path $Root 'shared/Microsoft.WindowsDesktop.App'
  if(-not [IO.Directory]::Exists($desktop)){return $false}
  foreach($directory in [IO.Directory]::GetDirectories($desktop)){
   $version=[IO.Path]::GetFileName($directory)
   if($version -cnotmatch '^10\.0\.\d+$' -or [version]$version -lt [version]$script:ToolkitRuntimeMinimumVersion){continue}
   $desktopFile=Join-Path $directory 'PresentationFramework.dll';Assert-ToolkitRuntimePath $desktopFile
   if(-not [IO.File]::Exists($desktopFile)){continue}
   $coreReady=$false;$hostReady=$false
   foreach($core in [IO.Directory]::GetDirectories((Join-Path $Root 'shared/Microsoft.NETCore.App'))){
    $coreVersion=[IO.Path]::GetFileName($core)
    if($coreVersion -cnotmatch '^10\.0\.\d+$' -or [version]$coreVersion -lt [version]$version){continue}
    $complete=$true
    foreach($leaf in @('coreclr.dll','System.Private.CoreLib.dll')){$path=Join-Path $core $leaf;Assert-ToolkitRuntimePath $path;if(-not [IO.File]::Exists($path)){$complete=$false}}
    if($complete){$coreReady=$true;break}
   }
   foreach($hostDirectory in [IO.Directory]::GetDirectories((Join-Path $Root 'host/fxr'))){
    $hostVersion=[IO.Path]::GetFileName($hostDirectory)
    if($hostVersion -cnotmatch '^\d+\.\d+\.\d+$' -or [version]$hostVersion -lt [version]'10.0.0'){continue}
    $path=Join-Path $hostDirectory 'hostfxr.dll';Assert-ToolkitRuntimePath $path
    if([IO.File]::Exists($path)){$hostReady=$true;break}
   }
   if($coreReady -and $hostReady){return $true}
  }
 }catch{return $false}
 return $false
}
function Find-ToolkitRuntime([string]$Root){
 $private=Join-Path $Root '.dotnet'
 if([IO.Directory]::Exists($private)){
  if(Test-ToolkitRuntime $private){return $private}
  throw 'The private .dotnet folder is incomplete or outdated. Close the toolkit and move that folder aside before starting again.'
 }
 $globalRoot=Get-ToolkitGlobalRuntimeRoot
 if(Test-ToolkitRuntime $globalRoot){return $globalRoot}
 return $null
}
function Get-ToolkitGlobalRuntimeRoot {
 # A registered location takes precedence, even when incomplete. In that case
 # install privately; don't claim a different root the native apphost won't use.
 $registry=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64)
 try{
  $key=$registry.OpenSubKey('SOFTWARE\dotnet\Setup\InstalledVersions\x64')
  if($null -ne $key){try{$location=$key.GetValue('InstallLocation');if($location -is [string] -and [IO.Path]::IsPathRooted($location)){return $location}}finally{$key.Dispose()}}
 }finally{$registry.Dispose()}
 return (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'dotnet')
}
function Test-ToolkitElevation {
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
 try{return (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)}finally{$identity.Dispose()}
}
function Receive-ToolkitRuntimeArchive($Item,[string]$Destination,[scriptblock]$Progress){
 $uri=[uri]$Item.url
 if($uri.Scheme -cne 'https' -or $uri.Host -cne 'builds.dotnet.microsoft.com' -or $Item.sha512 -cnotmatch '^[0-9a-f]{128}$'){throw 'The runtime download pin is invalid.'}
 [Net.ServicePointManager]::SecurityProtocol=[Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
 $client=New-Object Net.WebClient
 if($null -ne $client.Proxy){$client.Proxy.Credentials=[Net.CredentialCache]::DefaultNetworkCredentials}
 $clock=[Diagnostics.Stopwatch]::StartNew()
 try{
  $task=$client.DownloadFileTaskAsync($uri,$Destination)
  while(-not $task.IsCompleted){
   & $Progress 'Downloading verified Microsoft .NET 10 components. No administrator rights or restart are needed.'
   if($clock.Elapsed.TotalMinutes -gt 5 -or ([IO.File]::Exists($Destination) -and (Get-Item -LiteralPath $Destination).Length -gt $Item.bytes)){throw 'Runtime download exceeded its time or size limit.'}
   Start-Sleep -Milliseconds 100
  }
  $task.GetAwaiter().GetResult()
  if((Get-Item -LiteralPath $Destination).Length -ne $Item.bytes){throw 'Runtime download has an unexpected size.'}
 }finally{$client.CancelAsync();$client.Dispose();$clock.Stop()}
}
function Expand-ToolkitRuntimeArchive([string]$Zip,[string]$Destination,[scriptblock]$Progress){
 Add-Type -AssemblyName System.IO.Compression
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $archive=[IO.Compression.ZipFile]::OpenRead($Zip)
 try{
  if($archive.Entries.Count -gt 2000){throw 'Runtime archive has too many entries.'}
  $names=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase);$total=0L
  foreach($entry in $archive.Entries){
   $name=$entry.FullName.Replace('\','/');$clean=$name.TrimEnd('/')
   if($clean.Length -eq 0 -or $name.Length -gt 1024 -or $name.StartsWith('/') -or $name.Contains(':') -or $name -match '[\x00-\x1f\x7f-\x9f]' -or -not $names.Add($clean)){throw 'Unsafe or duplicate runtime archive path.'}
   foreach($part in $clean.Split('/')){if($part -eq '' -or $part -eq '.' -or $part -eq '..' -or $part.EndsWith('.') -or $part.EndsWith(' ')){throw 'Unsafe runtime archive path component.'}}
   $attributes=[long]$entry.ExternalAttributes
   $total+=$entry.Length
   if($total -gt 536870912L -or ($attributes -band 0x400) -ne 0 -or (($attributes -shr 16) -band 0xf000) -eq 0xa000){throw 'Runtime archive exceeds its bound or contains links.'}
  }
  $index=0
  foreach($entry in $archive.Entries){
   $destinationPath=Join-Path $Destination ($entry.FullName.Replace('\','/'));Assert-ToolkitRuntimePath $destinationPath
   if($entry.FullName.EndsWith('/')){[void][IO.Directory]::CreateDirectory($destinationPath);continue}
   [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destinationPath))
   $inputStream=$entry.Open();$outputStream=$null
   try{
    $outputStream=New-Object IO.FileStream($destinationPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    $buffer=New-Object byte[] 65536;$count=0L
    while(($read=$inputStream.Read($buffer,0,$buffer.Length)) -gt 0){$count+=$read;if($count -gt $entry.Length){throw 'Runtime entry exceeded its declared length.'};$outputStream.Write($buffer,0,$read)}
    if($count -ne $entry.Length){throw 'Runtime entry was truncated.'}
   }finally{if($null -ne $outputStream){$outputStream.Dispose()};$inputStream.Dispose()}
   $index++;if($index%32 -eq 0){& $Progress 'Installing private .NET 10 components. The toolkit will open automatically.'}
  }
 }finally{$archive.Dispose()}
}
function Install-ToolkitRuntime([string]$Root,[scriptblock]$Progress){
 if(Test-ToolkitElevation){throw 'Start the toolkit normally, without administrator rights, to install its private runtime.'}
 Assert-ToolkitRuntimePath $Root
 $target=Join-Path $Root '.dotnet';$stage=Join-Path $Root ('.dotnet-stage-'+[Guid]::NewGuid().ToString('N'))
 $lock=$null
 try{
  $lockPath=Join-Path $Root '.dotnet-install.lock';Assert-ToolkitRuntimePath $lockPath
  $lock=New-Object IO.FileStream($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  if(Test-ToolkitRuntime $target){return}
  if([IO.Directory]::Exists($target) -or [IO.File]::Exists($target)){throw 'An incomplete private runtime already exists. Move .dotnet aside before trying again.'}
  [void][IO.Directory]::CreateDirectory($stage)
  $payload=Join-Path $stage 'payload';[void][IO.Directory]::CreateDirectory($payload)
  $index=0
  foreach($item in @(Get-ToolkitRuntimeDownloads)){
   $zip=Join-Path $stage ('download-'+$index+'.zip');$index++
   & $Progress ('Downloading .NET 10 component '+$index+'. No administrator rights or restart are needed.')
   Receive-ToolkitRuntimeArchive $item $zip $Progress
   if((Get-FileHash -LiteralPath $zip -Algorithm SHA512).Hash.ToLowerInvariant() -cne $item.sha512){throw 'Runtime SHA-512 verification failed. No runtime was installed.'}
   Expand-ToolkitRuntimeArchive $zip $payload $Progress
  }
  if(-not (Test-ToolkitRuntime $payload)){throw 'Downloaded components do not form a complete x64 .NET 10 Desktop Runtime.'}
  & $Progress 'Runtime verified. Opening the toolkit; no restart is required.'
  [IO.Directory]::Move($payload,$target)
 }finally{
  try{if([IO.Directory]::Exists($stage)){Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction Stop}}
  finally{if($null -ne $lock){$lock.Dispose()}}
 }
}
