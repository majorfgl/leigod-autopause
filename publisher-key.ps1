$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/signing-key.ps1')
$privateXml = $null
try {
    if ($env:LEIGOD_SIGNING_KEY_XML) { $privateXml = $env:LEIGOD_SIGNING_KEY_XML }
    else { $privateXml = Read-LocalReleaseKey }
    Assert-ReleaseKey $privateXml
    $rsa = [Security.Cryptography.RSACryptoServiceProvider]::new([Security.Cryptography.CspParameters]::new(24))
    $rsa.PersistKeyInCsp = $false
    $rsa.FromXmlString($privateXml)
} finally { $privateXml = $null }
# A missing key is never silently replaced with a new publisher identity.
