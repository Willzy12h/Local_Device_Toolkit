[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
try { Update-MpSignature; exit 0 } catch { exit 1 }
