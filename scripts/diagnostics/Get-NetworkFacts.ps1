[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RequestFile,[Parameter(Mandatory=$true)][string]$HelpersPath)
. $HelpersPath
try{
 $adapters=@(Get-NetAdapter | Where-Object {$_.Status -eq 'Up'})
 $selected=[string](Get-Input 'adapterId' '')
 if($selected){$guid=[Guid]$selected;$adapters=@($adapters | Where-Object {$_.InterfaceGuid -eq $guid});if($adapters.Count -ne 1){throw 'Selected adapter is not available.'}}
 $context=New-Object 'Collections.Generic.List[string]';$resolvers=New-Object 'Collections.Generic.List[string]';$addresses=New-Object 'Collections.Generic.List[string]'
 foreach($adapter in $adapters){$context.Add([string]$adapter.InterfaceGuid+' | '+[string]$adapter.Name);foreach($dns in @(Get-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex)){$resolvers.Add(([string]$dns.AddressFamily)+': '+(@($dns.ServerAddresses) -join ', '))};foreach($ip in @(Get-NetIPAddress -InterfaceIndex $adapter.ifIndex)){$addresses.Add([string]$ip.IPAddress+'/'+[string]$ip.PrefixLength)}}
 switch -CaseSensitive ([string]$request.checkId){
  'local.network.adapter' {
   $details.adapter=$context -join '; ';$details.address=$addresses -join '; ';$details.resolvers=$resolvers -join '; '
   $routes=@(Get-NetRoute | Where-Object {$_.DestinationPrefix -eq '0.0.0.0/0' -or $_.DestinationPrefix -eq '::/0'} | Select-Object -First 20)
   $details.route=(@($routes | ForEach-Object {[string]$_.InterfaceIndex+' | '+[string]$_.NextHop+' | metric '+[string]$_.RouteMetric}) -join '; ')
   $details.vpn='Windows/third-party VPN status is not established by adapter presence.';$current=[string]$adapters.Count+' active adapter(s) observed';$limitations.Add('Routes, link and addresses do not prove application or corporate connectivity.')
  }
  'local.network.dns' {
   $hostName=[string](Get-Input 'hostname');$queryType=[string](Get-Input 'queryType')
   if($hostName.Length -gt 253 -or $hostName -match '[\s/@\\]' -or $queryType -cnotin @('A','AAAA','CNAME')){throw 'Invalid DNS target.'}
   $answers=@(Resolve-DnsName -Name $hostName -Type $queryType -DnsOnly -QuickTimeout)
   $rows=foreach($answer in $answers){$parts=New-Object 'Collections.Generic.List[string]';foreach($property in @('Name','Type','IPAddress','NameHost','TTL')){$member=$answer.PSObject.Properties[$property];if($null -ne $member){$parts.Add($property+'='+[string]$member.Value)}};$parts -join '; '}
   $details.hostname=$hostName;$details.queryType=$queryType;$details.answers=@($rows) -join "`n";$details.adapter=$context -join '; ';$details.resolvers=$resolvers -join '; ';$details.vpn='System resolver policy is used. Adapter context does not force query routing.'
   $returned=$answers.Count;$current=[string]$answers.Count+' DNS record(s) returned';$limitations.Add('Different round-robin/split-DNS responses alone do not establish stale cache. Third-party VPN policy may affect this query.')
  }
  'local.network.tcp' {
   $hostName=[string](Get-Input 'host');$port=[int](Get-Input 'port')
   if($hostName.Length -gt 253 -or $hostName -match '[\s/@\\]' -or $port -lt 1 -or $port -gt 65535){throw 'Invalid connection target.'}
   $client=New-Object Net.Sockets.TcpClient
   try{$connect=$client.ConnectAsync($hostName,$port);if(-not $connect.Wait(10000)){throw 'Connection timed out.'};$connected=$client.Connected;$endpoint=[string]$client.Client.RemoteEndPoint;$local=[string]$client.Client.LocalEndPoint}finally{$client.Dispose()}
   $details.endpoint=$hostName;$details.port=[string]$port;$details.connected=[string]$connected;$details.adapter=$context -join '; ';$details.route='Local '+$local+'; remote '+$endpoint
   $current=if($connected){'TCP connection succeeded'}else{'TCP connection was not established'};$limitations.Add('Hostname targets include DNS resolution. TCP does not verify TLS, sign-in or application behavior; no adapter binding was forced.')
  }
  default {throw 'Unregistered network check.'}
 }
 Write-Probe
}catch{Write-Unavailable}
