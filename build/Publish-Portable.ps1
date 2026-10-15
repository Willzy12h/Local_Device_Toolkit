[CmdletBinding()]
param([string]$OutputDirectory,[switch]$AllowDirty)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
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
  & dotnet publish 'src/LocalDeviceToolkit.App/LocalDeviceToolkit.App.csproj' -c Release -r win-x64 --self-contained true -o $stage -p:DebugType=None -p:DebugSymbols=false -p:PublishSingleFile=false -p:RestoreLockedMode=true -warnaserror
  if($LASTEXITCODE -ne 0){throw 'Application publish failed.'}
  & dotnet publish 'src/LocalDeviceToolkit.Worker/LocalDeviceToolkit.Worker.csproj' -c Release -r win-x64 --self-contained true -o (Join-Path $stage 'worker') -p:DebugType=None -p:DebugSymbols=false -p:PublishSingleFile=false -p:RestoreLockedMode=true -warnaserror
  if($LASTEXITCODE -ne 0){throw 'Worker publish failed.'}
  $commit=& git rev-parse --verify 'HEAD^{commit}'
  if($LASTEXITCODE -ne 0){throw 'Build provenance could not be recorded.'}
 }finally{Pop-Location}
 foreach($name in @('README.md','NOTICE.md','docs','provenance','catalogue')){Copy-Item -LiteralPath (Join-Path $repository $name) -Destination $stage -Recurse}
 $inventory=@(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
  if($_.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Package contains a reparse point.'}
  [pscustomobject]@{path=$_.FullName.Substring($stage.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
 })
 $manifest=[pscustomobject]@{schemaVersion=1;commit=[string]$commit;builtAt=[DateTimeOffset]::UtcNow.ToString('o');runtime='win-x64';selfContained=$true;workingTreeDirty=[bool]$changed;files=$inventory} | ConvertTo-Json -Depth 8
 [IO.File]::WriteAllText((Join-Path $stage 'package-manifest.json'),$manifest,(New-Object Text.UTF8Encoding($false)))
 Move-Item -LiteralPath $stage -Destination $OutputDirectory
 $archive=$OutputDirectory+'.zip'
 if(Test-Path -LiteralPath $archive){throw 'Archive already exists. The extracted package was preserved.'}
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 [IO.Compression.ZipFile]::CreateFromDirectory($OutputDirectory,$archive,[IO.Compression.CompressionLevel]::Optimal,$false)
 $hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
 [IO.File]::WriteAllText($archive+'.sha256',$hash+'  '+[IO.Path]::GetFileName($archive)+[Environment]::NewLine,(New-Object Text.UTF8Encoding($false)))
 Write-Output "Portable package: $archive"
}finally{if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}}
