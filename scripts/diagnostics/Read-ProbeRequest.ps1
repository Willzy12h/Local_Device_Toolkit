# Shared read-only protocol helpers. Loaded only from a separately verified registered file.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$WarningPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
if((Get-Item -LiteralPath $RequestFile).Length -gt 16384){throw 'Probe request exceeds the supported limit.'}
$request=Get-Content -LiteralPath $RequestFile -Raw | ConvertFrom-Json
if($request.schemaVersion -ne 2 -or $request.targetDigest -notmatch '^[a-f0-9]{64}$'){throw 'Invalid probe protocol.'}
$inputValues=$request.parameters
$details=[ordered]@{}
$limitations=New-Object 'Collections.Generic.List[string]'
$complete=$true
$returned=1
$hasMore=$false
$nextCursor=$null
$current='Observed'
$status='Observed'
function Write-Probe([string]$State='Known',[string]$Reason='Observed locally.'){
 $value=$null
 if($State -eq 'Known'){$value=[ordered]@{current=$current;expected=$null;status=$status;details=$details}}
 else{$script:complete=$false}
 [ordered]@{schemaVersion=2;checkId=[string]$request.checkId;targetDigest=[string]$request.targetDigest;state=$State;value=$value;source='Registered local Windows probe';capturedAt=[DateTimeOffset]::UtcNow.ToString('o');coverage=[ordered]@{complete=$complete;hasMore=$hasMore;returnedCount=$returned;nextCursor=$nextCursor;limitations=@($limitations.ToArray())};reason=$Reason} | ConvertTo-Json -Depth 12 -Compress
}
function Write-Unavailable {
 # Never expose provider exception text, raw output, request data or recovery material.
 Write-Probe 'Unavailable' 'The selected provider could not establish this observation. No value was inferred.'
}
function Check-Return($Result){if($null -eq $Result -or $Result.ReturnValue -ne 0){throw 'The native read did not complete.'}}
function Get-Input([string]$Name,$Default=$null){$p=$inputValues.PSObject.Properties[$Name];if($null -eq $p){return $Default};return $p.Value}
function Get-VolumeLetter { $letter=[string](Get-Input 'volumeId');if($letter -notmatch '^[A-Za-z]:$'){throw 'Choose one local volume.'};return $letter }
function Safe-Count($Values,[int]$Limit=200){if(@($Values).Count -gt $Limit){$script:complete=$false;$script:limitations.Add('Collection reached its displayed record limit. Remaining items were not collected.')};return @($Values | Select-Object -First $Limit)}
