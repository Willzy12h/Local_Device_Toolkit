[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RequestFile)
$ErrorActionPreference='Stop';Set-StrictMode -Version 2.0
function Read-Fields($Items,[string]$Property){@($Items | Where-Object {$null -ne $_} | ForEach-Object {$member=$_.PSObject.Properties[$Property];if($null -ne $member){$member.Value}})}
try{
 if((Get-Item -LiteralPath $RequestFile).Length -gt 8192){throw 'Oversized request'}
 $r=Get-Content -LiteralPath $RequestFile -Raw | ConvertFrom-Json
 $identity=[ordered]@{};$observed=[ordered]@{};$ready=$false;$reason='Required state unavailable.'
 if($r.kind -ceq 'DnsCache'){
  $adapters=@(Get-NetIPConfiguration -All | Where-Object {$_.NetAdapter.Status -eq 'Up'} | Select-Object -First 129)
  if($adapters.Count -gt 128){throw 'Adapter bound exceeded'}
  $identity.adapters=ConvertTo-Json -InputObject @($adapters | ForEach-Object {[ordered]@{id=[string]$_.NetAdapter.InterfaceGuid;description=[string]$_.NetAdapter.InterfaceDescription;addresses=@(Read-Fields $_.IPv4Address IPAddress)+@(Read-Fields $_.IPv6Address IPAddress);resolvers=@(Read-Fields $_.DNSServer ServerAddresses);gateway=@(Read-Fields $_.IPv4DefaultGateway NextHop)+@(Read-Fields $_.IPv6DefaultGateway NextHop)}} | Sort-Object id) -Depth 8 -Compress
  $routes=@(Get-NetRoute -PolicyStore ActiveStore | Select-Object -First 513);if($routes.Count -gt 512){throw 'Route bound exceeded'}
  $identity.routes=ConvertTo-Json -InputObject @($routes | Select-Object DestinationPrefix,NextHop,InterfaceIndex,RouteMetric | Sort-Object DestinationPrefix,InterfaceIndex,NextHop,RouteMetric) -Depth 6 -Compress
  $interfaces=@(Get-NetIPInterface | Select-Object -First 257);if($interfaces.Count -gt 256){throw 'Interface bound exceeded'}
  $identity.interfaces=ConvertTo-Json -InputObject @($interfaces | Select-Object InterfaceIndex,AddressFamily,InterfaceMetric,ConnectionState | Sort-Object InterfaceIndex,AddressFamily) -Depth 6 -Compress
  $nrpt=@(Get-DnsClientNrptPolicy -Effective | Select-Object -First 129);if($nrpt.Count -gt 128){throw 'Policy bound exceeded'}
  $identity.nrpt=ConvertTo-Json -InputObject @($nrpt | Select-Object Namespace,NameServers,DnsSecValidationRequired | Sort-Object Namespace) -Depth 6 -Compress
  $identity.hosts=(Get-FileHash -LiteralPath (Join-Path $env:SystemRoot 'System32\drivers\etc\hosts') -Algorithm SHA256).Hash
  $cache=@(Get-DnsClientCache | Where-Object {$_.Entry -ieq $r.input.hostname} | Select-Object -First 201);if($cache.Count -gt 200){throw 'Cache bound exceeded'}
  $addresses=@($cache | ForEach-Object { $ip=$null;if([Net.IPAddress]::TryParse([string]$_.Data,[ref]$ip)){$ip.ToString()} } | Sort-Object -Unique)
  $negative=@($cache | Where-Object {$_.Status -in @('NotExist','NoRecords')}).Count -gt 0
  $identity.cachedAddresses=$addresses -join ', ';$identity.negative=[string]$negative
  $observed.cachedAddresses=$identity.cachedAddresses;$observed.negative=$identity.negative
  $expected=[Net.IPAddress]::Parse([string]$r.input.expectedAddress).ToString()
  $ready=$adapters.Count -gt 0 -and (($r.input.hypothesis -ceq 'negative-cache-after-record-created' -and $negative) -or ($r.input.hypothesis -ceq 'configuration-change' -and $addresses.Count -gt 0 -and $addresses -notcontains $expected))
  $reason=if($ready){'Explicit stale-cache hypothesis and stable network context are available. Whole-device DNS client cache; no exact restoration.'}else{'No matching stale-cache evidence remains; no write is appropriate.'}
 }elseif($r.kind -ceq 'DefenderSignatures'){
  $status=Get-MpComputerStatus;$preference=Get-MpPreference
  $order=[string]$preference.SignatureFallbackOrder
  if([string]::IsNullOrWhiteSpace($order)){$order='Windows configured/default source order (no override)'}else{foreach($source in $order.Split('|')){if($source -notin @('InternalDefinitionUpdateServer','MicrosoftUpdateServer','MMPC','FileShares')){throw 'Unrecognized update source'}}}
  $identity.mode=[string]$status.AMRunningMode;$identity.sourceOrder=$order;$identity.enabled=[string]($status.AMServiceEnabled -and $status.AntivirusEnabled)
  $observed.signatureVersion=[string]$status.AntivirusSignatureVersion;$observed.signatureTime=([DateTimeOffset]$status.AntivirusSignatureLastUpdated).ToUniversalTime().ToString('o')
  $ready=$status.AMRunningMode -ceq 'Normal' -and $status.AMServiceEnabled -and $status.AntivirusEnabled
  $reason=if($ready){'Active Defender and readable configured/default sources. Network access may occur; no security settings or source overrides.'}else{'Defender is passive, disabled or not established as active. No request is permitted.'}
 }else{throw 'Unregistered read'}
 [ordered]@{schemaVersion=1;kind=[string]$r.kind;identity=$identity;observed=$observed;ready=$ready;reason=$reason} | ConvertTo-Json -Depth 12 -Compress
}catch{[ordered]@{schemaVersion=1;kind=[string]$r.kind;identity=@{};observed=@{};ready=$false;reason='Required local state or policy could not be read; no defaults were inferred.'} | ConvertTo-Json -Depth 6 -Compress}
