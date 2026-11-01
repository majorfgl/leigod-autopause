#requires -Version 7.2
param([Parameter(Mandatory)][string]$Backup, [switch]$VerifyOnly)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'signing-key.ps1')
$secret = Read-Host 'Backup password' -AsSecureString
$password = [Net.NetworkCredential]::new('', $secret).Password
try { Restore-EncryptedReleaseKey -Path $Backup -Password $password -VerifyOnly:$VerifyOnly }
finally { $password = $null; $secret.Dispose() }
