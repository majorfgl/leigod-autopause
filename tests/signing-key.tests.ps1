#requires -Version 7.2
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../scripts/signing-key.ps1')
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('leigod-key-test-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
$oldKeyFile = $env:LEIGOD_SIGNING_KEY_FILE
$env:LEIGOD_SIGNING_KEY_FILE = Join-Path $fixtureRoot 'original.dpapi'
$fixtureKey = [Security.Cryptography.RSA]::Create(3072)
$fixtureXml = $fixtureKey.ToXmlString($true)
$fixturePublic = $fixtureKey.ToXmlString($false)
function Get-ReleasePublicKey { return $fixturePublic }
$count = 0
function Check([bool]$condition, [string]$name) { if (-not $condition) { throw ('FAIL: ' + $name) }; $script:count++; Write-Output ('PASS: ' + $name) }
function Rejected([scriptblock]$action) { try { & $action | Out-Null; return $false } catch { return $true } }
$password = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
try {
    $clear = [Text.Encoding]::UTF8.GetBytes($fixtureXml)
    [IO.File]::WriteAllBytes($env:LEIGOD_SIGNING_KEY_FILE, [Security.Cryptography.ProtectedData]::Protect($clear,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))
    [Array]::Clear($clear,0,$clear.Length)
    $backup = Join-Path $fixtureRoot 'backup.p8'
    $result = Export-EncryptedReleaseKey -Path $backup -Password $password
    Check $result.verified 'encrypted backup is read back and signature verified'
    $pinnedKeyPath = $env:LEIGOD_SIGNING_KEY_FILE
    $env:LEIGOD_SIGNING_KEY_FILE = Join-Path $fixtureRoot 'missing-user-key.dpapi'
    try {
        $pinned = Export-EncryptedReleaseKey -Path (Join-Path $fixtureRoot 'pinned.p8') -Password $password -KeyPath $pinnedKeyPath
        Check $pinned.verified 'pinned key path survives a changed lookup environment'
        Check (Rejected { Read-LocalReleaseKey }) 'missing default key is reported without creating a replacement'
    } finally { $env:LEIGOD_SIGNING_KEY_FILE = $pinnedKeyPath }
    $decoded = [LeigodSigningKeyCodec]::Decrypt([IO.File]::ReadAllBytes($backup), $password)
    Check ([LeigodSigningKeyCodec]::Prove($decoded, $fixturePublic)) 'decrypted key signs under original identity'
    Check ([LeigodSigningKeyCodec]::Fingerprint($decoded) -eq [LeigodSigningKeyCodec]::Fingerprint($fixturePublic)) 'public fingerprint survives recovery'
    Check (Rejected { [LeigodSigningKeyCodec]::Decrypt([IO.File]::ReadAllBytes($backup), 'wrong-password') }) 'wrong password is rejected'
    $tampered = [IO.File]::ReadAllBytes($backup); $tampered[0] = $tampered[0] -bxor 1
    Check (Rejected { [LeigodSigningKeyCodec]::Decrypt($tampered, $password) }) 'damaged backup is rejected'
    Check (Rejected { Export-EncryptedReleaseKey -Path $backup -Password $password }) 'existing backup is not overwritten'
    $passwordCases = @(@{name='one-character';value='a'},@{name='Unicode';value='密'},@{name='empty';value=''},@{name='long';value=('a'*128)})
    foreach ($case in $passwordCases) {
        $casePath = Join-Path $fixtureRoot ($case.name+'.p8')
        $caseResult = Export-EncryptedReleaseKey -Path $casePath -Password $case.value
        $caseRestore = Restore-EncryptedReleaseKey -Path $casePath -Password $case.value -VerifyOnly
        Check ($caseResult.verified -and $caseRestore.verified) ($case.name+' password backs up and verifies without a length restriction')
    }
    Check (Rejected { Export-EncryptedReleaseKey -Path (Join-Path $PSScriptRoot '../private-test.p8') -Password $password }) 'backup inside source repository is rejected'
    $env:LEIGOD_SIGNING_KEY_FILE = Join-Path $fixtureRoot 'new-user.dpapi'
    $verified = Restore-EncryptedReleaseKey -Path $backup -Password $password -VerifyOnly
    Check ($verified.verified -and -not (Test-Path -LiteralPath $env:LEIGOD_SIGNING_KEY_FILE)) 'verify-only does not install or change a key'
    $restored = Restore-EncryptedReleaseKey -Path $backup -Password $password
    Check ($restored.installed -and [LeigodSigningKeyCodec]::Prove((Read-LocalReleaseKey),$fixturePublic)) 'recovery installs matching DPAPI key'
    $before = (Get-FileHash -LiteralPath $env:LEIGOD_SIGNING_KEY_FILE).Hash
    Check (Rejected { Restore-EncryptedReleaseKey -Path $backup -Password 'wrong-password' }) 'bad restore is rejected'
    Check ((Get-FileHash -LiteralPath $env:LEIGOD_SIGNING_KEY_FILE).Hash -eq $before) 'failed recovery preserves installed key'
    $otherKey = [Security.Cryptography.RSA]::Create(2048)
    try { $foreign = [LeigodSigningKeyCodec]::Encrypt($otherKey.ToXmlString($true),$password) } finally { $otherKey.Dispose() }
    $foreignPath = Join-Path $fixtureRoot 'foreign.p8'; [IO.File]::WriteAllBytes($foreignPath,$foreign)
    Check (Rejected { Restore-EncryptedReleaseKey -Path $foreignPath -Password $password }) 'different publisher key is rejected'
    Check ((Get-FileHash -LiteralPath $env:LEIGOD_SIGNING_KEY_FILE).Hash -eq $before) 'foreign-key recovery preserves original key'
    $receipt = Get-Content -Raw -LiteralPath ($backup+'.json') | ConvertFrom-Json
    Check ($receipt.restoredSignatureVerified -and $receipt.backupSha256 -eq (Get-FileHash -LiteralPath $backup).Hash.ToLowerInvariant()) 'backup receipt matches file'
    Write-Output ('Signing-key tests: ' + $count + ' passed.')
} finally {
    $fixtureKey.Dispose(); $fixtureXml = $decoded = $password = $null
    $env:LEIGOD_SIGNING_KEY_FILE = $oldKeyFile
    # The only recursive cleanup is this verified unique temporary test directory.
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolved).StartsWith('leigod-key-test-')) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
