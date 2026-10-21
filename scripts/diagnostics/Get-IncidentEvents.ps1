[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RequestFile,[Parameter(Mandatory=$true)][string]$HelpersPath)
. $HelpersPath
function Read-Events([string]$Channel,[string]$XPath,[int]$Count=201,[switch]$Oldest){
 try{if($XPath){return @(Get-WinEvent -LogName $Channel -FilterXPath $XPath -MaxEvents $Count)};return @(Get-WinEvent -LogName $Channel -MaxEvents $Count -Oldest:$Oldest)}
 catch{if(([string]$_.FullyQualifiedErrorId).StartsWith('NoMatchingEventsFound',[StringComparison]::Ordinal)){return @()};throw}
}
try{
 if($request.checkId -cne 'local.events.incident'){throw 'Unregistered incident check.'}
 $from=([DateTimeOffset](Get-Input 'fromUtc')).ToUniversalTime();$to=([DateTimeOffset](Get-Input 'toUtc')).ToUniversalTime();$symptom=[string](Get-Input 'symptom')
 if($to -le $from -or ($to-$from).TotalDays -gt 7){throw 'Incident window is outside the supported bound.'}
 $channels=@('System','Application');$providers=@()
 switch -CaseSensitive ($symptom){
  'network' {$channels=@('System','Microsoft-Windows-DNS-Client/Operational');$providers=@('Microsoft-Windows-DNS-Client','Tcpip','Microsoft-Windows-TCPIP','Microsoft-Windows-NDIS','RasClient','Microsoft-Windows-Dhcp-Client')}
  'printing' {$channels=@('Microsoft-Windows-PrintService/Admin','Microsoft-Windows-PrintService/Operational');$providers=@('Microsoft-Windows-PrintService')}
  'updates' {$channels=@('System','Setup');$providers=@('Microsoft-Windows-WindowsUpdateClient','Microsoft-Windows-Servicing')}
  'crash' {$providers=@('Microsoft-Windows-Kernel-Power','EventLog','Application Error','Application Hang','Windows Error Reporting','Microsoft-Windows-WER-SystemErrorReporting')}
  'power' {$providers=@('Microsoft-Windows-Kernel-Power','Microsoft-Windows-Power-Troubleshooter','Microsoft-Windows-Kernel-General')}
  'applications' {$providers=@('Application Error','Application Hang','Windows Error Reporting','Microsoft-Windows-DistributedCOM')}
  'performance' {$providers=@('Application Hang','Microsoft-Windows-Diagnostics-Performance','Microsoft-Windows-DistributedCOM','disk','storahci','stornvme')}
  default {throw 'Choose a registered symptom.'}
 }
 $base=$symptom+'|'+$from.ToString('o')+'|'+$to.ToString('o');$sha=[Security.Cryptography.SHA256]::Create();try{$queryHash=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($base)))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
 $cursor=$null;$rawCursor=Get-Input 'cursor';if($rawCursor){if(([string]$rawCursor).Length -gt 2048){throw 'Invalid cursor.'};$cursor=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string]$rawCursor)) | ConvertFrom-Json;if($cursor.query -cne $queryHash -or (@($cursor.channels).Count -lt 1 -or @($cursor.channels).Count -gt $channels.Count -or @($cursor.channels | Where-Object {$channels -cnotcontains $_.name}).Count -gt 0 -or @($cursor.channels.name | Select-Object -Unique).Count -ne @($cursor.channels).Count)){throw 'Cursor belongs to a different query.'};$complete=$false;$limitations.Add('Continuation page only. It does not independently establish full-window coverage.')}
 $states=New-Object 'Collections.Generic.List[object]';$all=New-Object 'Collections.Generic.List[object]';$generations=New-Object 'Collections.Generic.List[string]'
 foreach($channel in $channels){
  try{
   $oldest=@(Read-Events $channel '' 1 -Oldest);$newest=@(Read-Events $channel '' 1)
   if($oldest.Count -eq 0 -or $newest.Count -eq 0){$complete=$false;$limitations.Add($channel+': empty channel; retention coverage is not established.');continue}
   $generation=[string]$oldest[0].RecordId+'@'+([DateTimeOffset]$oldest[0].TimeCreated).ToUniversalTime().ToString('o');$generations.Add($channel+'='+$generation)
   $before=[long]$newest[0].RecordId+1
   if($null -ne $cursor){$saved=@($cursor.channels | Where-Object {$_.name -ceq $channel});if($saved.Count -ne 1 -or $saved[0].generation -cne $generation -or [long]$saved[0].before -le 0 -or [long]$saved[0].before -gt $before){$complete=$false;$limitations.Add($channel+': cursor invalid or retained log changed; collect a fresh first page.');continue};$before=[long]$saved[0].before}
   if(([DateTimeOffset]$oldest[0].TimeCreated).ToUniversalTime() -gt $from){$complete=$false;$limitations.Add($channel+': available records start after the incident window begins.')}
   $providerClause='('+(@($providers | ForEach-Object {"Provider[@Name='"+$_+"']"}) -join ' or ')+')'
   $xpath='*[System['+$providerClause+" and TimeCreated[@SystemTime >= '"+$from.ToString('yyyy-MM-ddTHH:mm:ss.fffffffZ')+"'] and TimeCreated[@SystemTime <= '"+$to.ToString('yyyy-MM-ddTHH:mm:ss.fffffffZ')+"'] and EventRecordID < "+$before+']]'
   $rows=@(Read-Events $channel $xpath);foreach($row in $rows){$all.Add([pscustomobject]@{channel=$channel;record=$row})}
   $states.Add([pscustomobject]@{name=$channel;generation=$generation;before=$before})
  }catch{$complete=$false;$limitations.Add($channel+': channel inaccessible, disabled or query unavailable.')}
 }
 # Consume only a contiguous newest-record prefix per channel. Clock changes can make timestamps non-monotonic.
 $queues=@($all | Group-Object channel | ForEach-Object {[pscustomobject]@{name=$_.Name;rows=@($_.Group | Sort-Object @{Expression={$_.record.RecordId};Descending=$true});index=0}})
 $selected=New-Object 'Collections.Generic.List[object]'
 while($selected.Count -lt 200){
  $heads=@($queues | Where-Object {$_.index -lt $_.rows.Count} | ForEach-Object {[pscustomobject]@{queue=$_;entry=$_.rows[$_.index]}} | Sort-Object @{Expression={$_.entry.record.TimeCreated};Descending=$true},@{Expression={$_.queue.name};Descending=$false})
  if($heads.Count -eq 0){break};$selected.Add($heads[0].entry);$heads[0].queue.index++
 }
 $projected=New-Object 'Collections.Generic.List[object]'
 foreach($entry in $selected){
  $record=$entry.record;$channel=[string]$entry.channel
  $saved=@($states | Where-Object {$_.name -ceq $channel});if($saved.Count -eq 1 -and [long]$record.RecordId -lt $saved[0].before){$saved[0].before=[long]$record.RecordId}
  if($null -eq $record.TimeCreated){$complete=$false;$limitations.Add($channel+': record '+[string]$record.RecordId+' has no usable time.');continue}
  $fields=[ordered]@{};$resource='record:'+[string]$record.RecordId
  if($record.ProviderName -ceq 'Microsoft-Windows-DistributedCOM' -and $record.Id -eq 10016 -and $record.Properties.Count -ge 8){
   $cls=[Guid]::Empty;$app=[Guid]::Empty
   if([Guid]::TryParse([string]$record.Properties[3].Value,[ref]$cls) -and [Guid]::TryParse([string]$record.Properties[4].Value,[ref]$app)){$fields.clsid=$cls.ToString('D');$fields.appid=$app.ToString('D');$resource=$fields.clsid+'/'+$fields.appid}
   $sid=[string]$record.Properties[7].Value;if($sid -match '^S-1-5-(18|19|20)$'){$fields.sid=$sid}
  }
  if($record.ProviderName -ceq 'Application Error' -and $record.Id -eq 1000 -and $record.Properties.Count -ge 7){
   foreach($pair in @(@('app',0),@('module',3))){$name=[IO.Path]::GetFileName([string]$record.Properties[$pair[1]].Value);if($name -match '^[A-Za-z0-9 _().-]+\.(exe|dll)$' -and $name.Length -le 255){$fields[$pair[0]]=$name}}
   if($fields.Contains('app')){$resource=$fields.app};$code=[string]$record.Properties[6].Value;if($code -match '^(0x)?[a-fA-F0-9]{1,8}$'){$fields.exceptionCode=$code}
  }
  $projected.Add([ordered]@{channel=$channel;provider=[string]$record.ProviderName;eventId=[int]$record.Id;recordId=[long]$record.RecordId;timeUtc=([DateTimeOffset]$record.TimeCreated).ToUniversalTime().ToString('o');resource=$resource;fields=$fields})
 }
 $records=ConvertTo-Json -InputObject @($projected.ToArray()) -Depth 8 -Compress
 if([Text.Encoding]::UTF8.GetByteCount($records) -gt 120000){throw 'Projected incident page exceeds the size bound.'}
 $hasMore=$all.Count -gt $selected.Count
 if($hasMore){$complete=$false;$nativeCursor=[ordered]@{query=$queryHash;channels=@($states.ToArray())} | ConvertTo-Json -Depth 5 -Compress;$nextCursor=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($nativeCursor));if($nextCursor.Length -gt 2048){$nextCursor=$null;$limitations.Add('Continuation metadata exceeds the supported limit. Narrow the incident range.')}}
 $returned=$projected.Count;$current=[string]$returned+' incident event(s) collected';$details.eventSummary='Selected channels: '+($channels -join ', ')+'. Provider-scoped metadata only; no event messages or dumps collected. Each channel keeps newest-record order across clock changes.';$details.eventRecords=$records;$details.channelGeneration=$generations -join '; ';$details.fromUtc=$from.ToString('o');$details.toUtc=$to.ToString('o')
 if($states.Count -eq 0){Write-Probe 'Unavailable' 'No selected channel could be read.'}else{Write-Probe}
}catch{Write-Unavailable}
