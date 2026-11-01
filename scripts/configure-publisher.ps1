#requires -Version 7.2
param([Parameter(Mandatory)][string]$BackupReceipt, [string]$GitHubCli = 'gh')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'signing-key.ps1')
$settings = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '../release.config.json') | ConvertFrom-Json
$receiptPath = [IO.Path]::GetFullPath($BackupReceipt)
if (-not $receiptPath.EndsWith('.p8.json',[StringComparison]::OrdinalIgnoreCase)) { throw 'Choose the verified .p8.json backup receipt.' }
$backupPath = $receiptPath.Substring(0,$receiptPath.Length-5)
$receipt = Get-Content -Raw -LiteralPath $receiptPath | ConvertFrom-Json
if (-not $receipt.restoredSignatureVerified -or $receipt.publicKeySha256 -ne [LeigodSigningKeyCodec]::Fingerprint((Get-ReleasePublicKey)) -or $receipt.backupSha256 -ne (Get-FileHash -LiteralPath $backupPath).Hash.ToLowerInvariant()) { throw 'Backup receipt or public identity does not match.' }
$keyXml = Read-LocalReleaseKey
try {
    Assert-ReleaseKey $keyXml
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $GitHubCli; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($argument in @('secret','set','LEIGOD_SIGNING_KEY_XML','--env','release','--repo',$settings.repository)) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $process.StandardInput.WriteLine($keyXml); $process.StandardInput.Close()
        $outputTask = $process.StandardOutput.ReadToEndAsync(); $errorTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'GitHub secret configuration timed out.' }
        if ($process.ExitCode -ne 0) { throw 'GitHub rejected the secret configuration. Check account and environment permissions.' }
    } finally { $process.Dispose() }
    Write-Output ('Release signing secret configured for ' + $settings.repository + ' / release. Private-key content was not written to disk or logs.')
} finally { $keyXml = $null }
