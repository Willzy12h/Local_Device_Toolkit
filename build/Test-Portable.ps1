# Integrity inspection only: never extracts, loads an assembly, or starts a packaged executable.
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$ZipPath,
 [Parameter(Mandatory=$true)][string]$ResultPath,
 [string]$ExpectedCommit,
 [string]$ExpectedSha256
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Closed data parser, compiled from this fixed source only. No manifest text becomes code.
# Unlike ConvertFrom-Json in Windows PowerShell 5.1 it refuses duplicate member names.
if(-not ('ToolkitPortableJson' -as [type])){
 Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
public sealed class ToolkitPortableJson {
 readonly string text; int at;
 ToolkitPortableJson(string value) { text=value; }
 public static object Read(string text) { var p=new ToolkitPortableJson(text); var result=p.Value(0); p.Space(); if(p.at!=text.Length)throw new FormatException("Trailing JSON content."); return result; }
 void Space(){while(at<text.Length && (text[at]==' '||text[at]=='\t'||text[at]=='\r'||text[at]=='\n'))at++;}
 bool Eat(char c){Space();if(at<text.Length&&text[at]==c){at++;return true;}return false;}
 void Need(char c){if(!Eat(c))throw new FormatException("Invalid JSON delimiter.");}
 object Value(int depth){
  if(depth>64)throw new FormatException("JSON depth limit exceeded."); Space(); if(at>=text.Length)throw new FormatException("Incomplete JSON.");
  char c=text[at];
  if(c=='{'){at++;var map=new Dictionary<string,object>(StringComparer.Ordinal);if(Eat('}'))return map;do{Space();string key=String();Need(':');object value=Value(depth+1);if(map.ContainsKey(key))throw new FormatException("Duplicate JSON member.");map.Add(key,value);if(Eat('}'))return map;Need(',');}while(true);}
  if(c=='['){at++;var list=new List<object>();if(Eat(']'))return list;do{list.Add(Value(depth+1));if(Eat(']'))return list;Need(',');}while(true);}
  if(c=='"')return String();
  foreach(string literal in new[]{"true","false","null"})if(at+literal.Length<=text.Length&&System.String.CompareOrdinal(text,at,literal,0,literal.Length)==0){at+=literal.Length;if(literal=="null")return null;return literal=="true";}
  var m=Regex.Match(text.Substring(at),@"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?");decimal number;
  if(!m.Success||!Decimal.TryParse(m.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out number))throw new FormatException("Invalid JSON value.");at+=m.Length;return number;
 }
 string String(){
  if(at>=text.Length||text[at++]!='"')throw new FormatException("JSON string required.");var b=new StringBuilder();
  while(at<text.Length){char c=text[at++];if(c=='"')return b.ToString();if(c<32)throw new FormatException("Control character in JSON string.");
   if(c!='\\'){b.Append(c);continue;}if(at>=text.Length)throw new FormatException("Incomplete JSON escape.");c=text[at++];
   switch(c){case '"':b.Append('"');break;case '\\':b.Append('\\');break;case '/':b.Append('/');break;case 'b':b.Append('\b');break;case 'f':b.Append('\f');break;case 'n':b.Append('\n');break;case 'r':b.Append('\r');break;case 't':b.Append('\t');break;case 'u':
    if(at+4>text.Length)throw new FormatException("Incomplete Unicode escape.");ushort code;if(!UInt16.TryParse(text.Substring(at,4),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out code))throw new FormatException("Invalid Unicode escape.");b.Append((char)code);at+=4;break;
    default:throw new FormatException("Invalid JSON escape.");}
  }throw new FormatException("Unterminated JSON string.");
 }
}
'@
}
function Assert-SafePath([string]$Path){
 if([string]::IsNullOrWhiteSpace($Path) -or $Path.Length -gt 1024 -or $Path.StartsWith('/') -or $Path.Contains('\') -or $Path.Contains(':') -or $Path -match '[\x00-\x1f\x7f-\x9f]'){throw 'Unsafe ZIP/manifest relative path.'}
 $clean=$Path.TrimEnd('/')
 foreach($part in $clean.Split('/')){if($part.Length -eq 0 -or $part -eq '.' -or $part -eq '..' -or $part.EndsWith('.') -or $part.EndsWith(' ')){throw 'Unsafe ZIP/manifest path component.'}}
 return $clean
}
function Assert-NoReparse([string]$Path){
 $current=[IO.Path]::GetFullPath($Path)
 while($current){if(([IO.File]::Exists($current) -or [IO.Directory]::Exists($current)) -and (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0)){throw 'Input/result paths must not traverse reparse points.'};$current=[IO.Path]::GetDirectoryName($current)}
}
function Read-EntryBytes($Entry,[long]$Limit){
 if($Entry.Length -gt $Limit){throw 'ZIP entry exceeds its read limit.'}
 $inputStream=$Entry.Open();$memory=New-Object IO.MemoryStream
 try{
  $buffer=New-Object byte[] 65536;$count=0L
  while(($read=$inputStream.Read($buffer,0,$buffer.Length)) -gt 0){$count+=$read;if($count -gt $Limit -or $count -gt $Entry.Length){throw 'ZIP entry expanded beyond its declared limit.'};$memory.Write($buffer,0,$read)}
  if($count -ne $Entry.Length){throw 'ZIP entry length differs from its declaration.'}
  return ,$memory.ToArray()
 }finally{$inputStream.Dispose();$memory.Dispose()}
}
function Read-JsonEntry($Entry,[long]$Limit){
 $bytes=Read-EntryBytes $Entry $Limit
 $utf8=New-Object Text.UTF8Encoding($false,$true)
 return ,([ToolkitPortableJson]::Read($utf8.GetString($bytes)))
}
function Assert-Members($Object,[string[]]$Allowed){
 if($Object -isnot [Collections.Generic.Dictionary[string,object]]){throw 'JSON object required.'}
 foreach($key in $Object.Keys){if(-not ($Allowed -ccontains $key)){throw "Unknown JSON field: $key"}}
 foreach($key in $Allowed){if(-not $Object.ContainsKey($key)){throw "Required JSON field is missing: $key"}}
}
function Get-EntryDigest($Entry){
 $inputStream=$Entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
 try{
  $buffer=New-Object byte[] 65536;$count=0L
  while(($read=$inputStream.Read($buffer,0,$buffer.Length)) -gt 0){$count+=$read;if($count -gt $Entry.Length -or $count -gt 1073741824L){throw 'ZIP file expanded beyond its declared bounded length.'};[void]$sha.TransformBlock($buffer,0,$read,$buffer,0)}
  [void]$sha.TransformFinalBlock((New-Object byte[] 0),0,0)
  if($count -ne $Entry.Length){throw 'ZIP file length does not match.'}
  return ([BitConverter]::ToString($sha.Hash).Replace('-','').ToLowerInvariant())
 }finally{$sha.Dispose();$inputStream.Dispose()}
}
function Assert-Amd64($Entry){
 # Header inspection only; no native image is loaded or executed.
 if($Entry.Length -lt 90){throw 'Application/worker PE header is truncated.'}
 $stream=$Entry.Open();$reader=New-Object IO.BinaryReader($stream)
 try{
  if($reader.ReadUInt16() -ne 0x5a4d){throw 'Application/worker lacks an MZ header.'}
  [void]$reader.ReadBytes(58);$offset=$reader.ReadInt32()
  if($offset -lt 64 -or $offset -gt 1048576 -or $offset+6 -gt $Entry.Length){throw 'Application/worker PE offset is invalid.'}
  $remaining=$offset-64
  while($remaining -gt 0){$take=[Math]::Min($remaining,65536);if($reader.ReadBytes($take).Length -ne $take){throw 'Truncated PE header.'};$remaining-=$take}
  if($reader.ReadUInt32() -ne 0x00004550 -or $reader.ReadUInt16() -ne 0x8664){throw 'Application/worker must have an AMD64 PE header.'}
 }finally{$reader.Dispose();$stream.Dispose()}
}
if($PSBoundParameters.ContainsKey('ExpectedCommit') -and $ExpectedCommit -cnotmatch '\A[0-9a-f]{40}\z'){throw 'ExpectedCommit must be exactly 40 lowercase hexadecimal characters.'}
if($PSBoundParameters.ContainsKey('ExpectedSha256') -and $ExpectedSha256 -cnotmatch '\A[0-9a-f]{64}\z'){throw 'ExpectedSha256 must be exactly 64 lowercase hexadecimal characters.'}
$ZipPath=[IO.Path]::GetFullPath($ZipPath);$ResultPath=[IO.Path]::GetFullPath($ResultPath)
Assert-NoReparse $ZipPath;Assert-NoReparse $ResultPath
if([IO.File]::Exists($ResultPath) -or [IO.Directory]::Exists($ResultPath)){throw 'Existing result outputs are never overwritten.'}
$inputFile=New-Object IO.FileStream($ZipPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
$archive=$null
try{
 $sha=[Security.Cryptography.SHA256]::Create()
 try{$zipSha=[BitConverter]::ToString($sha.ComputeHash($inputFile)).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
 if($ExpectedSha256 -and $zipSha -cne $ExpectedSha256){throw 'Whole ZIP SHA-256 does not match ExpectedSha256.'}
 $inputFile.Position=0
 $archive=New-Object IO.Compression.ZipArchive($inputFile,[IO.Compression.ZipArchiveMode]::Read,$true)
 if($archive.Entries.Count -gt 2000){throw 'ZIP exceeds the 2000-entry limit.'}
 $names=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
 $files=New-Object 'Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
 $total=0L
 foreach($entry in $archive.Entries){
  $clean=Assert-SafePath $entry.FullName
  if(-not $names.Add($clean)){throw 'ZIP contains a duplicate/case-colliding path.'}
  $attrs=[long]$entry.ExternalAttributes
  if(($attrs -band 0x400) -ne 0 -or (($attrs -shr 16) -band 0xf000) -eq 0xa000){throw 'ZIP contains a link/reparse entry.'}
  $total+=$entry.Length;if($entry.Length -lt 0 -or $total -gt 1073741824L){throw 'ZIP exceeds the 1 GiB uncompressed limit.'}
  if($entry.FullName.EndsWith('/')){if($entry.Length -ne 0){throw 'ZIP directory contains file data.'};continue}
  $files.Add($entry.FullName,$entry)
 }
 if(-not $files.ContainsKey('package-manifest.json')){throw 'Root package-manifest.json is required.'}
 $manifest=Read-JsonEntry $files['package-manifest.json'] 8388608
 Assert-Members $manifest @('schemaVersion','commit','builtAt','runtime','selfContained','workingTreeDirty','files')
 if($manifest['schemaVersion'] -isnot [decimal] -or $manifest['schemaVersion'] -ne 1 -or $manifest['runtime'] -cne 'win-x64' -or $manifest['selfContained'] -isnot [bool] -or $manifest['workingTreeDirty'] -isnot [bool]){throw 'Manifest must declare schema 1, win-x64 and a boolean selfContained with an explicit dirty flag.'}
 if($manifest['commit'] -isnot [string] -or $manifest['commit'] -cnotmatch '\A[0-9a-f]{40}\z'){throw 'Manifest commit must be a complete lowercase Git commit.'}
 if($ExpectedCommit -and $manifest['commit'] -cne $ExpectedCommit){throw 'Manifest commit does not match ExpectedCommit.'}
 $builtAt=[DateTimeOffset]::MinValue
 if($manifest['builtAt'] -isnot [string] -or -not [DateTimeOffset]::TryParse($manifest['builtAt'],[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$builtAt) -or $builtAt.Offset -ne [TimeSpan]::Zero){throw 'Manifest builtAt must be a UTC date/time.'}
 if($manifest['files'] -isnot [Collections.Generic.List[object]] -or $manifest['files'].Count -gt 1999){throw 'Manifest file list is invalid or oversized.'}
 $listed=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
 $attestedBytes=0L
 foreach($item in $manifest['files']){
  Assert-Members $item @('path','bytes','sha256')
  if($item['path'] -isnot [string]){throw 'Manifest file path must be text.'}
  [void](Assert-SafePath $item['path'])
  if($item['path'].EndsWith('/') -or $item['path'] -ieq 'package-manifest.json' -or -not $listed.Add($item['path'])){throw 'Manifest contains a directory, self-reference, or duplicate file.'}
  if($item['bytes'] -isnot [decimal] -or $item['bytes'] -lt 0 -or $item['bytes'] -gt 1073741824L -or [decimal]::Truncate($item['bytes']) -ne $item['bytes'] -or $item['sha256'] -isnot [string] -or $item['sha256'] -cnotmatch '\A[0-9a-f]{64}\z'){throw 'Manifest requires exact byte lengths and lowercase SHA-256 hashes.'}
  if(-not $files.ContainsKey($item['path'])){throw 'Manifest lists a missing ZIP file.'}
  $entry=$files[$item['path']]
  if($entry.Length -ne $item['bytes'] -or (Get-EntryDigest $entry) -cne $item['sha256']){throw "Manifest bytes/hash mismatch: $($item['path'])"}
  $attestedBytes+=$entry.Length
 }
 if($listed.Count -ne $files.Count-1){throw 'ZIP contains files absent from the manifest.'}
 # Source assets and registered script bodies are independently cross-checked inside the package.
 foreach($required in @('NOTICE.md','docs/LOCAL-USAGE.md','tools/New-DeploymentProfile.ps1','tools/Test-Portable.ps1','provenance/import-manifest.json','catalogue/reference.json','catalogue/local-windows.json','catalogue/tasks.json','catalogue/workflows.json','catalogue/pins.json')){
  if(-not $files.ContainsKey($required)){throw "Critical source asset is missing: $required"}
 }
 [void](Read-JsonEntry $files['provenance/import-manifest.json'] 8388608)
 $pins=Read-JsonEntry $files['catalogue/pins.json'] 1048576
 if($pins -isnot [Collections.Generic.Dictionary[string,object]] -or $pins.Count -gt 100){throw 'Catalogue pin map is invalid or oversized.'}
 foreach($required in @('reference.json','local-windows.json','tasks.json','workflows.json')){if(-not $pins.ContainsKey($required)){throw 'A required catalogue raw-file pin is missing.'}}
 foreach($pin in $pins.Keys){
  [void](Assert-SafePath $pin)
  if($pin -ceq 'pins.json' -or $pins[$pin] -isnot [string] -or $pins[$pin] -cnotmatch '\A[0-9a-f]{64}\z' -or -not $files.ContainsKey('catalogue/'+$pin)){throw 'Invalid catalogue pin or missing pinned file.'}
  if((Get-EntryDigest $files['catalogue/'+$pin]) -cne $pins[$pin]){throw "Catalogue raw-file pin mismatch: $pin"}
 }
 $expectedScripts=@{
  'device.facts'=@{path='diagnostics/Get-DeviceFacts.ps1';readOnly=$true}
  'settings.user'=@{path='settings/Invoke-UserSetting.ps1';readOnly=$false}
  'deployments.msix'=@{path='deployments/Invoke-CurrentUserMsix.ps1';readOnly=$false}
  'probes.common'=@{path='diagnostics/Read-ProbeRequest.ps1';readOnly=$true}
  'probes.readiness'=@{path='diagnostics/Get-ReadinessFacts.ps1';readOnly=$true}
  'probes.hardware'=@{path='diagnostics/Get-HardwareFacts.ps1';readOnly=$true}
  'probes.network'=@{path='diagnostics/Get-NetworkFacts.ps1';readOnly=$true}
  'probes.services'=@{path='diagnostics/Get-SecurityServiceFacts.ps1';readOnly=$true}
  'probes.events'=@{path='diagnostics/Get-IncidentEvents.ps1';readOnly=$true}
  'repair.read'=@{path='diagnostics/Get-RepairState.ps1';readOnly=$true}
  'repair.defender'=@{path='diagnostics/Invoke-DefenderSignatureUpdate.ps1';readOnly=$false}
 }
 foreach($scriptPrefix in @('scripts/','worker/scripts/')){
  if(-not $files.ContainsKey($scriptPrefix+'registry.json')){throw 'Critical script registry is missing.'}
  $registry=Read-JsonEntry $files[$scriptPrefix+'registry.json'] 1048576
  Assert-Members $registry @('schemaVersion','scripts')
  if($registry['schemaVersion'] -isnot [decimal] -or $registry['schemaVersion'] -ne 1 -or $registry['scripts'] -isnot [Collections.Generic.List[object]] -or $registry['scripts'].Count -ne $expectedScripts.Count){throw 'Script registry must declare exactly the known script inventory in schema 1.'}
  $scriptIds=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
  foreach($registered in $registry['scripts']){
   Assert-Members $registered @('id','path','sha256','readOnly')
   if($registered['id'] -isnot [string] -or -not $expectedScripts.ContainsKey($registered['id']) -or -not $scriptIds.Add($registered['id'])){throw 'Unknown or duplicate registered script identity.'}
   $expected=$expectedScripts[$registered['id']]
   if($registered['path'] -cne $expected.path -or $registered['readOnly'] -isnot [bool] -or $registered['readOnly'] -ne $expected.readOnly -or $registered['sha256'] -isnot [string] -or $registered['sha256'] -cnotmatch '\A[0-9a-f]{64}\z'){throw 'Registered script path, read-only flag or hash is invalid.'}
   $scriptPath=$scriptPrefix+$registered['path']
   if(-not $files.ContainsKey($scriptPath)){throw "Critical registered script body is missing: $scriptPath"}
   if((Get-EntryDigest $files[$scriptPath]) -cne $registered['sha256']){throw "Script registry pin mismatch: $scriptPath"}
  }
 }
 if(-not $manifest['selfContained']){
  foreach($leaf in @('Start.exe','Start.cmd','Start.ps1','Runtime.ps1')){if(-not $files.ContainsKey($leaf)){throw "Lightweight startup asset is missing: $leaf"}}
  Assert-Amd64 $files['Start.exe']
 }
 foreach($prefix in @('','worker/')){
  $name=if($prefix.Length -eq 0){'LocalDeviceToolkit.App'}else{'LocalDeviceToolkit.Worker'}
  $requiredRuntime=@(($name+'.exe'),($name+'.dll'),($name+'.runtimeconfig.json'))
  if($manifest['selfContained']){$requiredRuntime+=@('coreclr.dll','PresentationFramework.dll','System.Private.CoreLib.dll','hostfxr.dll','hostpolicy.dll')}
  elseif($files.ContainsKey($prefix+'coreclr.dll')){throw 'Lightweight package unexpectedly bundles a private Core runtime.'}
  foreach($leaf in $requiredRuntime){
   if(-not $files.ContainsKey($prefix+$leaf)){throw "Critical bundled application/worker/runtime file is missing: $prefix$leaf"}
  }
  Assert-Amd64 $files[$prefix+$name+'.exe']
  $config=Read-JsonEntry $files[$prefix+$name+'.runtimeconfig.json'] 1048576
  if($config -isnot [Collections.Generic.Dictionary[string,object]] -or -not $config.ContainsKey('runtimeOptions')){throw 'runtimeOptions is missing.'}
  $options=$config['runtimeOptions']
  if($options -isnot [Collections.Generic.Dictionary[string,object]]){throw 'Invalid runtimeOptions.'}
  if($manifest['selfContained']){
   if($options.ContainsKey('framework') -or $options.ContainsKey('frameworks') -or -not $options.ContainsKey('includedFrameworks')){throw 'Framework-dependent or unproven bundled runtime configuration.'}
   $frameworks=$options['includedFrameworks']
  }else{
   if($options.ContainsKey('includedFrameworks') -or $options.ContainsKey('framework') -or -not $options.ContainsKey('frameworks')){throw 'Lightweight package must declare both required shared frameworks.'}
   $frameworks=$options['frameworks']
  }
  if($frameworks -isnot [Collections.Generic.List[object]]){throw 'Framework declarations must be an array.'}
  $included=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
  foreach($framework in $frameworks){
   Assert-Members $framework @('name','version')
   if(-not $manifest['selfContained'] -and ($framework['version'] -cnotmatch '^10\.0\.\d+$' -or $framework['name'] -cnotin @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App'))){throw 'Lightweight package requires .NET 10 Desktop and Core frameworks.'}
   if($framework['name'] -isnot [string] -or $framework['version'] -isnot [string] -or $framework['version'] -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$' -or -not $included.Add($framework['name'])){throw 'Invalid/duplicate included framework identity.'}
  }
  if(-not $included.Contains('Microsoft.NETCore.App') -or -not $included.Contains('Microsoft.WindowsDesktop.App')){throw 'Both bundled NETCore and WindowsDesktop frameworks are required.'}
 }
 $result=[pscustomobject][ordered]@{
  SchemaVersion=1;ZipPath=$ZipPath;Sha256=$zipSha;Commit=$manifest['commit'];BuiltAt=$manifest['builtAt'];Files=$listed.Count;Bytes=$attestedBytes;Runtime='win-x64';SelfContained=$manifest['selfContained'];WorkingTreeDirty=$manifest['workingTreeDirty'];
  ArtifactKind=$(if($manifest['workingTreeDirty']){'DirtyReviewArtifact'}else{'CleanSourceIntegrityArtifact'});
  CompletedUtc=[DateTimeOffset]::UtcNow.ToString('o');IntegrityVerified=$true;NativeLaunchVerified=$false;SignatureTrustVerified=$false;CatalogueTrustVerified=$false;
  Limitations=@('ZIP bytes and manifest consistency only; native GUI launch and device behavior require separate Windows acceptance.','Manifest hashes do not prove code-signing, publisher approval, catalogue trust or release authenticity.','This package inspection does not approve or validate vendor installer media.','A dirty source tree is a review artifact, not a clean release.')
 }
}finally{if($null -ne $archive){$archive.Dispose()};$inputFile.Dispose()}
# Write a new result only after every check; failed verification creates no output.
$parent=[IO.Path]::GetDirectoryName($ResultPath)
if(-not [IO.Directory]::Exists($parent)){throw 'Result parent directory must already exist.'}
Assert-NoReparse $ResultPath
$temporary=Join-Path $parent ('.portable-result-'+[Guid]::NewGuid().ToString('N')+'.tmp')
try{
 $bytes=(New-Object Text.UTF8Encoding($false)).GetBytes(($result|ConvertTo-Json -Depth 8))
 $output=New-Object IO.FileStream($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::WriteThrough)
 try{$output.Write($bytes,0,$bytes.Length);$output.Flush($true)}finally{$output.Dispose()}
 Assert-NoReparse $ResultPath
 [IO.File]::Move($temporary,$ResultPath)
}finally{if([IO.File]::Exists($temporary)){[IO.File]::Delete($temporary)}}
Write-Output "Integrity verified only: $ZipPath ; result: $ResultPath ; NativeLaunchVerified=false"
