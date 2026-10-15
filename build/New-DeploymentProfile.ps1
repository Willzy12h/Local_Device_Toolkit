#requires -Version 5.1
<#
.SYNOPSIS
Creates a draft local deployment profile from trusted, already acquired media.
.DESCRIPTION
Reads metadata, hashes every source file and checks Windows Authenticode trust.
Runs as the current user. It does not launch installers, install software, acquire
media, change execution policy, unblock files or bypass certificate validation.
The resulting draft is not deployment authorization: import and review it in the
GUI. The approved profile store finalizes its content ID, and the UAC worker
independently verifies the media and Windows trust before any installation.
.PARAMETER SourceDirectory
Local media directory (no UNC paths, links or reparse points). Every file is pinned.
.PARAMETER EntryPoint
Exact case-sensitive relative path using forward slashes, for example setup.msi.
.PARAMETER OutputPath
New local JSON file outside SourceDirectory. Existing files are never overwritten.
.EXAMPLE
.\build\New-DeploymentProfile.ps1 -SourceDirectory C:\Media\App -EntryPoint app.msi -ControlId APP-WIN-001 -Format MachineMsi -OutputPath C:\Drafts\app.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $SourceDirectory,
    [Parameter(Mandatory = $true)][string] $EntryPoint,
    [Parameter(Mandatory = $true)][ValidatePattern('^APP-WIN-00[1-8]$')][string] $ControlId,
    [Parameter(Mandatory = $true)][ValidateSet('MachineMsi', 'OneDriveMachineExe')][string] $Format,
    [Parameter(Mandatory = $true)][string] $OutputPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Deployment profile creation requires Windows and Windows Authenticode trust.'
}

function Assert-SafeRelativePath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.Length -gt 240 -or
        $Path -match '[\\:\x00-\x1f\x7f*?"<>|]' -or $Path.StartsWith('/')) {
        throw 'An exact safe relative path using forward slashes is required.'
    }
    foreach ($part in $Path.Split('/')) {
        if ($part.Length -eq 0 -or $part -eq '.' -or $part -eq '..' -or
            $part.EndsWith('.') -or $part.EndsWith(' ')) {
            throw 'Relative paths cannot contain empty, parent or ambiguous components.'
        }
    }
}
function Get-LocalFullPath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.StartsWith('\\') -or $Path.StartsWith('//')) {
        throw 'Only local filesystem paths are supported; UNC and device paths are refused.'
    }
    $full = [IO.Path]::GetFullPath($Path)
    if ($full -notmatch '^[A-Za-z]:\\' -or $full.Substring(2).Contains(':')) {
        throw 'Only local drive paths are supported.'
    }
    $drive = New-Object IO.DriveInfo($full.Substring(0, 3))
    if ($drive.DriveType -eq [IO.DriveType]::Network -or $drive.DriveType -eq [IO.DriveType]::NoRootDirectory) {
        throw 'Network-mapped drives and unavailable drive roots are not supported.'
    }
    return $full
}
function Assert-NoReparsePath([string] $Path) {
    $current = $Path
    while (-not [string]::IsNullOrEmpty($current)) {
        if ([IO.File]::Exists($current) -or [IO.Directory]::Exists($current)) {
            if (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Links and reparse points are not accepted in source or output paths.'
            }
        }
        $current = [IO.Path]::GetDirectoryName($current.TrimEnd('\'))
    }
}
function Get-Sha256([byte[]] $Bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}
function Release-ComObject($Object) {
    if ($null -ne $Object -and [Runtime.InteropServices.Marshal]::IsComObject($Object)) {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($Object)
    }
}
function Read-MsiProperty($Database, [string] $Query) {
    $view = $null; $record = $null
    try {
        $view = $Database.OpenView($Query)
        $view.Execute()
        $record = $view.Fetch()
        if ($null -eq $record) { throw 'Required MSI product metadata is absent.' }
        return [string] $record.StringData(1)
    }
    finally {
        if ($null -ne $view) { $view.Close() }
        Release-ComObject $record
        Release-ComObject $view
    }
}
function Assert-MetadataText([string] $Value) {
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Length -gt 512 -or $Value -match '[\x00-\x1f\x7f]') {
        throw 'Required product metadata is missing or invalid.'
    }
}

Assert-SafeRelativePath $EntryPoint
$source = Get-LocalFullPath $SourceDirectory
$sourcePrefix = $source.TrimEnd('\') + '\'
$output = Get-LocalFullPath $OutputPath
Assert-NoReparsePath $source
Assert-NoReparsePath $output
if (-not [IO.Directory]::Exists($source)) { throw 'The source directory does not exist.' }
if ([IO.File]::Exists($output) -or [IO.Directory]::Exists($output)) { throw 'Output already exists; overwriting is refused.' }
if (-not [IO.Directory]::Exists([IO.Path]::GetDirectoryName($output))) { throw 'The output parent directory must already exist.' }
if ($output.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'The draft output must be outside the source media directory.' }
if (-not $EntryPoint.EndsWith($(if ($Format -eq 'MachineMsi') { '.msi' } else { '.exe' }), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The entry point extension does not match the selected format.'
}
if ($Format -eq 'OneDriveMachineExe' -and $ControlId -ne 'APP-WIN-005') { throw 'OneDrive machine setup belongs only to APP-WIN-005.' }

$streams = New-Object 'Collections.Generic.List[IO.FileStream]'
$files = New-Object 'Collections.Generic.List[object]'
$relativePaths = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$pending = New-Object 'Collections.Generic.Stack[object]'
$pending.Push(@{ Path = $source; Depth = 0 })
$entries = 0
$entryFullPath = $null
$primarySignerName = $null
try {
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        Assert-NoReparsePath $directory.Path
        foreach ($path in [IO.Directory]::EnumerateFileSystemEntries($directory.Path)) {
            $entries++
            if ($entries -gt 4096) { throw 'The source exceeds the 4096-entry traversal limit.' }
            Assert-NoReparsePath $path
            $attributes = [IO.File]::GetAttributes($path)
            if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) {
                if ($directory.Depth -ge 32) { throw 'The source exceeds the 32-directory depth limit.' }
                $pending.Push(@{ Path = $path; Depth = $directory.Depth + 1 })
                continue
            }
            if ($files.Count -ge 256) { throw 'The source exceeds the 256-file manifest limit.' }
            $relative = $path.Substring($sourcePrefix.Length).Replace('\', '/')
            Assert-SafeRelativePath $relative
            if (-not $relativePaths.Add($relative)) { throw 'Case-insensitive duplicate relative file paths are refused.' }
            $isEntry = [string]::Equals($relative, $EntryPoint, [StringComparison]::Ordinal)
            if ([string]::Equals($relative, $EntryPoint, [StringComparison]::OrdinalIgnoreCase) -and -not $isEntry) {
                throw 'EntryPoint must match the actual relative filename and case exactly.'
            }
            # Keep all source files open without write/delete sharing until the draft is durable.
            $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            $streams.Add($stream)
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
            finally { $sha.Dispose() }
            $certificateHash = $null
            if ($isEntry -or $relative.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase) -or $relative.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
                $signature = $null
                try { $signature = Get-AuthenticodeSignature -LiteralPath $path -ErrorAction Stop }
                catch { if ($isEntry) { throw 'The primary installer Authenticode trust check failed; no trust bypass is available.' } }
                if ($null -ne $signature -and $signature.Status -eq [Management.Automation.SignatureStatus]::Valid -and $null -ne $signature.SignerCertificate) {
                    $certificateHash = Get-Sha256 $signature.SignerCertificate.RawData
                }
                elseif ($isEntry) { throw 'The primary installer must have a valid Windows Authenticode signature; no trust bypass is available.' }
            }
            if ($isEntry) {
                $entryFullPath = $path
                $primarySignerName = $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
            }
            $files.Add([ordered]@{ relativePath = $relative; sha256 = $hash; signerCertificateSha256 = $certificateHash })
        }
    }
    if ($null -eq $entryFullPath) { throw 'The exact entry point was not found in the source directory.' }
    if ($Format -eq 'OneDriveMachineExe' -and $files.Count -ne 1) { throw 'OneDrive requires a directory containing only its single self-contained setup executable.' }

    if ($Format -eq 'MachineMsi') {
        $installer = $null; $database = $null; $summary = $null
        try {
            $installer = New-Object -ComObject WindowsInstaller.Installer
            # MSIDBOPEN_READONLY (0): never install, repair, mutate or execute database actions.
            $database = $installer.OpenDatabase($entryFullPath, 0)
            $productIdentity = Read-MsiProperty $database 'SELECT `Value` FROM `Property` WHERE `Property` = ''ProductCode'''
            $version = Read-MsiProperty $database 'SELECT `Value` FROM `Property` WHERE `Property` = ''ProductVersion'''
            $productName = Read-MsiProperty $database 'SELECT `Value` FROM `Property` WHERE `Property` = ''ProductName'''
            $publisher = Read-MsiProperty $database 'SELECT `Value` FROM `Property` WHERE `Property` = ''Manufacturer'''
            $summary = $database.SummaryInformation(0)
            if (([string] $summary.Property(7)).Split(';')[0] -cne 'x64') { throw 'The MSI summary template must specify x64.' }
            $productCode = [Guid]::Empty
            if (-not [Guid]::TryParseExact($productIdentity, 'B', [ref] $productCode)) { throw 'The MSI ProductCode is invalid.' }
        }
        finally { Release-ComObject $summary; Release-ComObject $database; Release-ComObject $installer }
    }
    else {
        $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($entryFullPath)
        if ($primarySignerName -cne 'Microsoft Corporation' -or $info.ProductName -cne 'Microsoft OneDrive' -or $info.CompanyName -cne 'Microsoft Corporation' -or
            -not [string]::Equals($info.OriginalFilename, 'OneDriveSetup.exe', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The executable is not the Microsoft OneDrive setup product.'
        }
        $productIdentity = 'Microsoft.OneDrive'; $productName = $info.ProductName; $publisher = $info.CompanyName
        $version = ([string] $info.ProductVersion).Split(' ')[0].Replace(',', '.')
        $binary = $null; $reader = $null
        try {
            $binary = [IO.File]::Open($entryFullPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            $reader = New-Object IO.BinaryReader($binary)
            if ($binary.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5a4d) { throw 'The setup executable has no valid DOS header.' }
            $binary.Position = 0x3c; $offset = $reader.ReadInt32()
            if ($offset -lt 0 -or $offset -gt $binary.Length - 6) { throw 'The setup executable has an invalid PE header offset.' }
            $binary.Position = $offset
            if ($reader.ReadUInt32() -ne 0x00004550 -or $reader.ReadUInt16() -ne 0x8664) { throw 'The OneDrive setup executable must be PE AMD64.' }
        }
        finally { if ($null -ne $reader) { $reader.Dispose() } elseif ($null -ne $binary) { $binary.Dispose() } }
    }
    Assert-MetadataText $productName
    Assert-MetadataText $publisher
    $parsedVersion = $null
    if (-not [Version]::TryParse($version, [ref] $parsedVersion)) { throw 'The product version is not a valid deployment profile version.' }
    $draft = [ordered]@{
        id = '';  controlId = $ControlId; format = $Format; productIdentity = $productIdentity
        version = $version; productName = $productName; publisher = $publisher
        scope = 'Machine'; architecture = 'x64'; entryPoint = $EntryPoint
        files = @($files | Sort-Object -Property relativePath); timeout = '00:30:00'
    }
    $json = $draft | ConvertTo-Json -Depth 8
    $bytes = (New-Object Text.UTF8Encoding($false, $true)).GetBytes($json)
    if ($bytes.Length -gt 262144) { throw 'The draft exceeds the 256 KiB import limit.' }
    Assert-NoReparsePath $output
    $destination = $null
    try {
        $destination = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $destination.Write($bytes, 0, $bytes.Length)
        $destination.Flush($true)
    }
    finally { if ($null -ne $destination) { $destination.Dispose() } }
    Write-Output ('Created draft profile: ' + $output + '. Import and review in the GUI; this draft does not authorize installation.')
}
finally { foreach ($stream in $streams) { $stream.Dispose() } }
