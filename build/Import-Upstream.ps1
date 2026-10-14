[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$revision = '80f01a9482c0ae20e995f9df357c9fca9217f623'
if (Test-Path -LiteralPath $Destination) { throw 'Destination must be a new directory; existing files will not be overwritten.' }
& git clone --no-checkout 'https://github.com/Willzy12h/M365-Buildstandards.git' $Destination
if ($LASTEXITCODE -ne 0) { throw 'Upstream clone failed.' }
& git -C $Destination cat-file -e ($revision + '^{commit}')
if ($LASTEXITCODE -ne 0) { throw 'The pinned source revision is unavailable.' }
& git -C $Destination checkout --detach $revision
if ($LASTEXITCODE -ne 0) { throw 'Pinned source checkout failed.' }
$head = & git -C $Destination rev-parse HEAD
if ($head -ne $revision) { throw 'Source revision mismatch.' }
Write-Output ('Preserved upstream source at ' + $revision + '. Review provenance/import-manifest.json before adapting files.')
