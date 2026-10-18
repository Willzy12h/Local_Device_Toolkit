[CmdletBinding()]
param([string]$OutputDirectory,[switch]$AllowDirty,[switch]$Lightweight)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# .NET Framework's CreateFromDirectory can write Windows backslashes into ZIP names.
# Explicit canonical entry names keep Windows PowerShell 5.1 and PowerShell 7 packages identical in format.
function ConvertTo-PortableEntryName([string]$RelativePath){
 $name=$RelativePath.Replace('\','/')
 if([string]::IsNullOrWhiteSpace($name) -or $name.StartsWith('/') -or $name.Contains(':') -or $name -match '[\x00-\x1f\x7f-\x9f]'){throw 'Unsafe portable archive relative path.'}
 foreach($part in $name.Split('/')){if($part.Length -eq 0 -or $part -eq '.' -or $part -eq '..' -or $part.EndsWith('.') -or $part.EndsWith(' ')){throw 'Unsafe portable archive path component.'}}
 return $name
}
function Get-PortablePackageFiles([string]$Directory,[int]$Depth=0){
 if($Depth -gt 64){throw 'Portable package directory depth exceeds the supported bound.'}
 $Directory=[IO.Path]::GetFullPath($Directory)
 if($Depth -eq 0){
  $ancestor=$Directory
  while($ancestor){
   if(([IO.File]::Exists($ancestor) -or [IO.Directory]::Exists($ancestor)) -and (([IO.File]::GetAttributes($ancestor) -band [IO.FileAttributes]::ReparsePoint) -ne 0)){throw 'Package paths must not traverse reparse points.'}
   $ancestor=[IO.Path]::GetDirectoryName($ancestor)
  }
 }
 foreach($item in @(Get-ChildItem -LiteralPath $Directory -Force | Sort-Object FullName)){
  if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Package contains a reparse point.'}
  if($item.PSIsContainer){Get-PortablePackageFiles -Directory $item.FullName -Depth ($Depth+1)}else{$item}
 }
}
function New-PortableArchive([string]$SourceDirectory,[string]$ArchivePath){
 Add-Type -AssemblyName System.IO.Compression
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $SourceDirectory=[IO.Path]::GetFullPath($SourceDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
 $ArchivePath=[IO.Path]::GetFullPath($ArchivePath)
 $packageFiles=@(Get-PortablePackageFiles -Directory $SourceDirectory | Sort-Object FullName)
 if($packageFiles.Count -gt 2000){throw 'Portable package exceeds the 2000-entry archive bound.'}
 $names=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
 # Open outside the cleanup block: refusal of an existing file must never delete that file.
 $stream=New-Object IO.FileStream($ArchivePath,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 $archive=$null;$complete=$false
 try{
  $archive=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
  foreach($file in $packageFiles){
   if(($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Package contains a reparse point.'}
   $name=ConvertTo-PortableEntryName $file.FullName.Substring($SourceDirectory.Length+1)
   if(-not $names.Add($name)){throw 'Portable package contains duplicate/case-colliding entry names.'}
   [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$name,[IO.Compression.CompressionLevel]::Optimal)
  }
  $archive.Dispose();$archive=$null;$stream.Flush($true);$complete=$true
 }finally{
  if($null -ne $archive){$archive.Dispose()}
  $stream.Dispose()
  if(-not $complete -and [IO.File]::Exists($ArchivePath)){[IO.File]::Delete($ArchivePath)}
 }
}
$repository=Split-Path -Parent $PSScriptRoot
if([string]::IsNullOrWhiteSpace($OutputDirectory)){$OutputDirectory=Join-Path $repository ('dist/LocalDeviceToolkit-win-x64-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $OutputDirectory){throw 'Output already exists. Choose a new directory; existing packages are never overwritten.'}
& (Join-Path $PSScriptRoot 'Verify-ScriptRegistry.ps1') -RepositoryRoot $repository
$stage=$OutputDirectory+'.staging-'+[Guid]::NewGuid().ToString('N')
[void][IO.Directory]::CreateDirectory($stage)
try{
 Push-Location $repository
 try{
  $changed=& git status --porcelain=1 --untracked-files=normal
  if($LASTEXITCODE -ne 0){throw 'Working tree state could not be checked.'}
  if($changed -and -not $AllowDirty){throw 'Commit source changes before packaging, or explicitly use -AllowDirty for a local review artifact.'}
  $selfContained=if($Lightweight){'false'}else{'true'}
  & dotnet publish 'src/LocalDeviceToolkit.App/LocalDeviceToolkit.App.csproj' -c Release -r win-x64 --self-contained $selfContained -o $stage -p:DebugType=None -p:DebugSymbols=false -p:PublishSingleFile=false -p:RestoreLockedMode=true -warnaserror
  if($LASTEXITCODE -ne 0){throw 'Application publish failed.'}
  & dotnet publish 'src/LocalDeviceToolkit.Worker/LocalDeviceToolkit.Worker.csproj' -c Release -r win-x64 --self-contained $selfContained -o (Join-Path $stage 'worker') -p:DebugType=None -p:DebugSymbols=false -p:PublishSingleFile=false -p:RestoreLockedMode=true -warnaserror
  if($LASTEXITCODE -ne 0){throw 'Worker publish failed.'}
  $commit=& git rev-parse --verify 'HEAD^{commit}'
  if($LASTEXITCODE -ne 0){throw 'Build provenance could not be recorded.'}
 }finally{Pop-Location}
 foreach($name in @('README.md','NOTICE.md','docs','provenance','catalogue')){Copy-Item -LiteralPath (Join-Path $repository $name) -Destination $stage -Recurse}
  foreach($name in @('Start.cmd','Start.ps1','Runtime.ps1','Run-Silent.cmd','Diagnostics.config.example','ProbeInputs.json.example','HOW-TO-USE.txt')){Copy-Item -LiteralPath (Join-Path $repository ('packaging/'+$name)) -Destination (Join-Path $stage $name)}
  $compiler=Join-Path $env:SystemRoot 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
  & $compiler /nologo /target:winexe /platform:x64 /warnaserror (('/out:')+(Join-Path $stage 'Start.exe')) (('/win32manifest:')+(Join-Path $repository 'src/LocalDeviceToolkit.App/app.manifest')) (Join-Path $repository 'packaging/Start.cs')
  if($LASTEXITCODE -ne 0){throw 'Native startup launcher compilation failed.'}
 [void][IO.Directory]::CreateDirectory((Join-Path $stage 'tools'))
 foreach($tool in @('New-DeploymentProfile.ps1','Test-Portable.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $tool) -Destination (Join-Path $stage ('tools/'+$tool))}
 $inventory=@(Get-PortablePackageFiles -Directory $stage | Sort-Object FullName | ForEach-Object {
  if($_.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Package contains a reparse point.'}
  [pscustomobject]@{path=(ConvertTo-PortableEntryName $_.FullName.Substring($stage.Length+1));bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
 })
 $manifest=[pscustomobject]@{schemaVersion=1;commit=[string]$commit;builtAt=[DateTimeOffset]::UtcNow.ToString('o');runtime='win-x64';selfContained=(-not [bool]$Lightweight);workingTreeDirty=[bool]$changed;files=$inventory} | ConvertTo-Json -Depth 8
 [IO.File]::WriteAllText((Join-Path $stage 'package-manifest.json'),$manifest,(New-Object Text.UTF8Encoding($false)))
 Move-Item -LiteralPath $stage -Destination $OutputDirectory
 $archive=$OutputDirectory+'.zip'
 if(Test-Path -LiteralPath $archive){throw 'Archive already exists. The extracted package was preserved.'}
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 New-PortableArchive -SourceDirectory $OutputDirectory -ArchivePath $archive
 $hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
 [IO.File]::WriteAllText($archive+'.sha256',$hash+'  '+[IO.Path]::GetFileName($archive)+[Environment]::NewLine,(New-Object Text.UTF8Encoding($false)))
 Write-Output "Portable package: $archive"
}finally{if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}}
