#requires -Version 5.1
<#
.SYNOPSIS
Creates a draft local deployment profile from trusted, already acquired media.
.DESCRIPTION
Reads metadata, hashes every source file and checks Windows Authenticode trust.
Runs as the current user. It does not launch installers, install software, acquire
media, change execution policy, unblock files or bypass certificate validation.
The resulting draft is not deployment authorization: import and review it in the
GUI. The approved profile store finalizes its content ID. The engine independently
verifies media and Windows trust, using a UAC worker for machine installations.
MSIX profiles use original-user scope; nested bundles and the complete dependency
graph receive independent engine validation before execution.
.PARAMETER SourceDirectory
Local media directory (no UNC paths, links or reparse points). Every file is pinned.
.PARAMETER EntryPoint
Exact case-sensitive relative path using forward slashes, for example setup.msi.
.PARAMETER OfficeProductId
OfficeOdt only: O365BusinessRetail or O365ProPlusRetail, with fixed MonthlyEnterprise/en-gb settings.
.PARAMETER TargetOfficeVersion
OfficeOdt only: exact four-part 16.0 Office media version, independent of the ODT binary version.
.PARAMETER LicenseAuthorized
OfficeOdt requires explicit confirmation that the selected product and EULA are authorised.
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
    [Parameter(Mandatory = $true)][ValidateSet('MachineMsi', 'OneDriveMachineExe', 'CurrentUserMsix', 'OfficeOdt')][string] $Format,
    [Parameter(Mandatory = $true)][string] $OutputPath,
    [ValidateSet('O365BusinessRetail', 'O365ProPlusRetail')][string] $OfficeProductId,
    [string] $TargetOfficeVersion,
    [switch] $LicenseAuthorized
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ControlId = $ControlId.ToUpperInvariant()
$Format = switch ($Format) {
    'MachineMsi' { 'MachineMsi' }; 'OneDriveMachineExe' { 'OneDriveMachineExe' }
    'CurrentUserMsix' { 'CurrentUserMsix' }; 'OfficeOdt' { 'OfficeOdt' }
}
if ($OfficeProductId) { $OfficeProductId = if ($OfficeProductId -ieq 'O365BusinessRetail') { 'O365BusinessRetail' } else { 'O365ProPlusRetail' } }
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Deployment profile creation requires Windows and Windows Authenticode trust.'
}

function Assert-SafeRelativePath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.Length -gt 240 -or
        $Path -match '[\\:\p{Cc}*?"<>|]' -or $Path.StartsWith('/')) {
        throw 'An exact safe relative path using forward slashes is required.'
    }
    foreach ($part in $Path.Split('/')) {
        if ($part.Length -eq 0 -or $part -eq '.' -or $part -eq '..' -or
            $part.EndsWith('.') -or $part.EndsWith(' ')) {
            throw 'Relative paths cannot contain empty, parent or ambiguous components.'
        }
        if ($part.Split('.')[0].TrimEnd(' ') -match '^(CON|PRN|AUX|NUL|CLOCK\$|CONIN\$|CONOUT\$|COM[1-9\u00b9\u00b2\u00b3]|LPT[1-9\u00b9\u00b2\u00b3])$') {
            throw 'Reserved Windows device names are not safe relative file paths.'
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
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Length -gt 512 -or $Value -match '\p{Cc}') {
        throw 'Required product metadata is missing or invalid.'
    }
}

function Assert-FormatOptions([string] $Format, [string] $OfficeProductId, [string] $TargetOfficeVersion, [bool] $LicenseAuthorized) {
    if ($Format -eq 'OfficeOdt') {
        if (-not $LicenseAuthorized) { throw 'Office requires explicit -LicenseAuthorized confirmation for the selected product and EULA.' }
        if ($OfficeProductId -cnotin @('O365BusinessRetail', 'O365ProPlusRetail')) { throw 'A supported -OfficeProductId is required for Office.' }
        $target = $null
        if (-not [Version]::TryParse($TargetOfficeVersion, [ref] $target) -or
            -not $TargetOfficeVersion.StartsWith('16.0.', [StringComparison]::Ordinal) -or $target.Revision -lt 0) {
            throw 'Office requires an explicit four-part -TargetOfficeVersion beginning 16.0.; the ODT binary version is not the Office target.'
        }
    }
    elseif ($OfficeProductId -or $TargetOfficeVersion -or $LicenseAuthorized) { throw 'Office options are accepted only with OfficeOdt.' }
}
function Get-MsixFamilyName([string] $Name, [string] $Publisher) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $bytes = $sha.ComputeHash([Text.Encoding]::Unicode.GetBytes($Publisher)) }
    finally { $sha.Dispose() }
    $alphabet = '0123456789abcdefghjkmnpqrstvwxyz'
    $suffix = New-Object Text.StringBuilder
    for ($group = 0; $group -lt 13; $group++) {
        $value = 0
        for ($bit = 0; $bit -lt 5; $bit++) {
            $index = $group * 5 + $bit
            $next = 0
            if ($index -lt 64) { $next = ($bytes[[int][Math]::Floor($index / 8)] -shr (7 - ($index % 8))) -band 1 }
            $value = ($value -shl 1) -bor $next
        }
        [void] $suffix.Append($alphabet[$value])
    }
    return $Name + '_' + $suffix.ToString()
}
function Test-MsixExtension([string] $Path) {
    return [IO.Path]::GetExtension($Path).ToLowerInvariant() -in @('.msix', '.appx', '.msixbundle', '.appxbundle')
}
function Read-MsixMetadata([string] $Path) {
    $stream = $null; $archive = $null; $manifestStream = $null; $reader = $null
    try {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
        if ($archive.Entries.Count -gt 4096) { throw 'The MSIX archive exceeds the 4096-entry limit.' }
        $names = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        [long] $expanded = 0
        foreach ($entry in $archive.Entries) {
            Assert-SafeRelativePath $entry.FullName.TrimEnd('/')
            if (-not $names.Add($entry.FullName)) { throw 'Duplicate MSIX archive entries are refused.' }
            $expanded += $entry.Length
            if ($entry.Length -gt 4294967296 -or $expanded -gt 17179869184) { throw 'MSIX expanded size exceeds the inspection limits.' }
        }
        $manifest = $archive.GetEntry('AppxManifest.xml')
        $bundle = $archive.GetEntry('AppxMetadata/AppxBundleManifest.xml')
        if (($null -eq $manifest) -eq ($null -eq $bundle)) { throw 'Exactly one primary package or bundle manifest is required.' }
        $isBundle = $null -ne $bundle
        if ($isBundle) { $manifest = $bundle }
        if ($manifest.Length -gt 1048576) { throw 'MSIX manifest exceeds the one MiB limit.' }
        $settings = New-Object Xml.XmlReaderSettings
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $settings.MaxCharactersInDocument = 1048576
        $settings.MaxCharactersFromEntities = 0
        $manifestStream = $manifest.Open()
        $reader = [Xml.XmlReader]::Create($manifestStream, $settings)
        $document = New-Object Xml.XmlDocument
        $document.XmlResolver = $null
        $document.Load($reader)
        $expectedRoot = if ($isBundle) { 'Bundle' } else { 'Package' }
        if ($document.DocumentElement.LocalName -cne $expectedRoot) { throw 'Unexpected MSIX manifest root.' }
        $identities = @($document.DocumentElement.SelectNodes("./*[local-name()='Identity']"))
        if ($identities.Count -ne 1) { throw 'Exactly one MSIX identity is required.' }
        $identity = $identities[0]
        $name = $identity.GetAttribute('Name'); $publisher = $identity.GetAttribute('Publisher'); $version = $identity.GetAttribute('Version')
        $parsed = $null
        if ($name -cnotmatch '^[A-Za-z0-9.-]{3,50}$' -or [string]::IsNullOrWhiteSpace($publisher) -or $publisher.Length -gt 1024 -or
            $publisher -match '\p{Cc}' -or -not [Version]::TryParse($version, [ref] $parsed) -or $parsed.Revision -lt 0) {
            throw 'The MSIX name, publisher or four-part version is invalid.'
        }
        if ($isBundle) {
            $selected = @($document.DocumentElement.SelectNodes("./*[local-name()='Packages']/*[local-name()='Package']") | Where-Object { $_.GetAttribute('Architecture') -cin @('x64', 'neutral') })
            if ($selected.Count -lt 1 -or $selected.Count -gt 64 -or @($selected | Where-Object { $_.GetAttribute('Type') -ceq 'application' }).Count -eq 0) { throw 'The MSIX bundle contains no supported x64/neutral application.' }
            foreach ($package in $selected) {
                $nestedPath = $package.GetAttribute('FileName')
                Assert-SafeRelativePath $nestedPath
                if ($null -eq $archive.GetEntry($nestedPath)) { throw 'A selected MSIX bundled package is missing.' }
            }
        }
        elseif ($identity.GetAttribute('ProcessorArchitecture') -cnotin @('x64', 'neutral')) { throw 'Only x64/neutral MSIX packages are supported.' }
        # Nested bundle payloads and the full dependency graph are independently checked by the engine before execution.
        return [pscustomobject]@{ Name = $name; Publisher = $publisher; Version = $version; FamilyName = Get-MsixFamilyName $name $publisher }
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
        if ($null -ne $manifestStream) { $manifestStream.Dispose() }
        if ($null -ne $archive) { $archive.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

Assert-FormatOptions $Format $OfficeProductId $TargetOfficeVersion ([bool] $LicenseAuthorized)
if ($Format -eq 'CurrentUserMsix') {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
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
$validExtension = if ($Format -eq 'CurrentUserMsix') { Test-MsixExtension $EntryPoint } else {
    $EntryPoint.EndsWith($(if ($Format -eq 'MachineMsi') { '.msi' } else { '.exe' }), [StringComparison]::OrdinalIgnoreCase)
}
if (-not $validExtension) { throw 'The entry point extension does not match the selected format.' }
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
            if ($Format -eq 'CurrentUserMsix' -and -not (Test-MsixExtension $relative)) { throw 'MSIX media may contain only the signed primary package and its signed package dependencies.' }
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
            if ($Format -eq 'CurrentUserMsix' -or $isEntry -or $relative.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase) -or $relative.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
                $signature = $null
                try { $signature = Get-AuthenticodeSignature -LiteralPath $path -ErrorAction Stop }
                catch { if ($isEntry -or $Format -eq 'CurrentUserMsix') { throw 'The primary installer Authenticode trust check failed; no trust bypass is available.' } }
                if ($null -ne $signature -and $signature.Status -eq [Management.Automation.SignatureStatus]::Valid -and $null -ne $signature.SignerCertificate) {
                    $certificateHash = Get-Sha256 $signature.SignerCertificate.RawData
                }
                elseif ($isEntry -or $Format -eq 'CurrentUserMsix') { throw 'The primary installer must have a valid Windows Authenticode signature; no trust bypass is available.' }
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

    $msixOptions = $null; $officeOptions = $null; $scope = 'Machine'
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
    elseif ($Format -eq 'CurrentUserMsix') {
        $primary = Read-MsixMetadata $entryFullPath
        foreach ($file in $files) {
            if ($file.relativePath -cne $EntryPoint) {
                [void] (Read-MsixMetadata ([IO.Path]::Combine($source, $file.relativePath.Replace('/', '\'))))
            }
        }
        $productIdentity = $primary.FamilyName; $productName = $primary.Name; $publisher = $primary.Publisher; $version = $primary.Version
        $scope = 'CurrentUser'
        $msixOptions = [ordered]@{ name = $primary.Name; publisher = $primary.Publisher; familyName = $primary.FamilyName }
    }
    elseif ($Format -eq 'OfficeOdt') {
        $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($entryFullPath)
        if ($primarySignerName -cne 'Microsoft Corporation' -or $info.CompanyName -cne 'Microsoft Corporation' -or
            $info.ProductName -cne 'Microsoft Office Deployment Tool' -or -not [string]::Equals($info.OriginalFilename, 'setup.exe', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The executable is not the signed Microsoft Office Deployment Tool setup.exe.'
        }
        foreach ($required in @(('Office/Data/' + $TargetOfficeVersion + '/stream.x64.x-none.dat'), ('Office/Data/' + $TargetOfficeVersion + '/stream.x64.en-gb.dat'), 'Office/Data/v64.cab')) {
            if (-not $relativePaths.Contains($required)) { throw ('Required offline Office media is missing: ' + $required) }
        }
        $productIdentity = $OfficeProductId; $productName = $info.ProductName; $publisher = $info.CompanyName
        $version = $TargetOfficeVersion
        $officeOptions = [ordered]@{ productId = $OfficeProductId; channel = 'MonthlyEnterprise'; language = 'en-gb'; licenseAuthorized = $true }
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
    if ($Format -ne 'CurrentUserMsix') { Assert-MetadataText $publisher }
    $parsedVersion = $null
    if (-not [Version]::TryParse($version, [ref] $parsedVersion)) { throw 'The product version is not a valid deployment profile version.' }
    $draft = [ordered]@{
        id = '';  controlId = $ControlId; format = $Format; productIdentity = $productIdentity
        version = $version; productName = $productName; publisher = $publisher
        scope = $scope; architecture = 'x64'; entryPoint = $EntryPoint
        files = @($files | Sort-Object -Property relativePath); timeout = '00:30:00'
        msix = $msixOptions; office = $officeOptions
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
