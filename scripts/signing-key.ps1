# Release-key operations. Never write plaintext private keys to disk or logs.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not ('LeigodSigningKeyCodec' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Security.Cryptography;
public static class LeigodSigningKeyCodec {
    public static byte[] Encrypt(string xml, string password) {
        using (var rsa = RSA.Create()) {
            rsa.FromXmlString(xml);
            return rsa.ExportEncryptedPkcs8PrivateKey(password,
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 300000));
        }
    }
    public static string Decrypt(byte[] encrypted, string password) {
        using (var rsa = RSA.Create()) {
            int count;
            rsa.ImportEncryptedPkcs8PrivateKey(password, encrypted, out count);
            if (count != encrypted.Length) throw new CryptographicException("Unexpected backup data.");
            return rsa.ToXmlString(true);
        }
    }
    public static string Fingerprint(string xml) {
        using (var rsa = RSA.Create()) {
            rsa.FromXmlString(xml);
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(rsa.ExportSubjectPublicKeyInfo())).Replace("-", "").ToLowerInvariant();
        }
    }
    public static bool Prove(string xml, string publicXml) {
        byte[] challenge = new byte[32];
        RandomNumberGenerator.Fill(challenge);
        using (var key = RSA.Create()) using (var verifier = RSA.Create()) {
            key.FromXmlString(xml); verifier.FromXmlString(publicXml);
            byte[] signature = key.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return verifier.VerifyData(challenge, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }
}
'@
}
function Get-ReleaseKeyPath {
    if ($env:LEIGOD_SIGNING_KEY_FILE) { return [IO.Path]::GetFullPath($env:LEIGOD_SIGNING_KEY_FILE) }
    return Join-Path $env:LOCALAPPDATA 'LeigodAutoPausePublisher/signing-key.dpapi'
}
function Read-LocalReleaseKey {
    param([string]$Path = (Get-ReleaseKeyPath))
    if (-not (Test-Path -LiteralPath $Path)) { throw 'Release key missing. Restore the encrypted backup first.' }
    $clear = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($Path), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { return [Text.Encoding]::UTF8.GetString($clear) }
    finally { [Array]::Clear($clear, 0, $clear.Length) }
}
function Get-ReleasePublicKey {
    return [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../launcher/release-public-key.xml'))
}
function Assert-ReleaseKey {
    param([Parameter(Mandatory)][string]$Xml)
    if (-not [LeigodSigningKeyCodec]::Prove($Xml, (Get-ReleasePublicKey))) {
        throw 'Signing key does not match this project release identity.'
    }
}
function Export-EncryptedReleaseKey {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][AllowEmptyString()][string]$Password)
    $target = [IO.Path]::GetFullPath($Path)
    $repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if ($target.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep private-key backups outside the source repository.' }
    if (Test-Path -LiteralPath $target) { throw 'Backup already exists. Choose a new filename.' }
    $xml = Read-LocalReleaseKey
    try {
        Assert-ReleaseKey $xml
        $encrypted = [LeigodSigningKeyCodec]::Encrypt($xml, $Password)
        $restored = [LeigodSigningKeyCodec]::Decrypt($encrypted, $Password)
        Assert-ReleaseKey $restored
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        # FileMode.CreateNew never overwrites another backup.
        $stream = [IO.File]::Open($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($encrypted, 0, $encrypted.Length) } finally { $stream.Dispose() }
        $diskXml = [LeigodSigningKeyCodec]::Decrypt([IO.File]::ReadAllBytes($target), $Password)
        Assert-ReleaseKey $diskXml
        $receipt = [ordered]@{
            schema = 1; format = 'Encrypted PKCS#8 DER'; encryption = 'PBES2 AES-256-CBC / PBKDF2-HMAC-SHA256, 300000 iterations'
            publicKeySha256 = [LeigodSigningKeyCodec]::Fingerprint((Get-ReleasePublicKey))
            backupSha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
            verifiedAt = [DateTimeOffset]::UtcNow.ToString('o'); restoredSignatureVerified = $true
        }
        [IO.File]::WriteAllText($target + '.json', ($receipt | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
        return [pscustomobject]@{ path = $target; fingerprint = $receipt.publicKeySha256; verified = $true }
    } finally { $xml = $restored = $diskXml = $null }
}
function Restore-EncryptedReleaseKey {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][AllowEmptyString()][string]$Password, [switch]$VerifyOnly)
    $xml = [LeigodSigningKeyCodec]::Decrypt([IO.File]::ReadAllBytes([IO.Path]::GetFullPath($Path)), $Password)
    try {
        Assert-ReleaseKey $xml
        if (-not $VerifyOnly) {
            $target = Get-ReleaseKeyPath
            if (Test-Path -LiteralPath $target) { Assert-ReleaseKey (Read-LocalReleaseKey $target) }
            $clear = [Text.Encoding]::UTF8.GetBytes($xml)
            try { $protected = [Security.Cryptography.ProtectedData]::Protect($clear, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser) }
            finally { [Array]::Clear($clear, 0, $clear.Length) }
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            $temporary = $target + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
            try {
                [IO.File]::WriteAllBytes($temporary, $protected)
                Assert-ReleaseKey (Read-LocalReleaseKey $temporary)
                [IO.File]::Move($temporary, $target, $true)
            } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
        }
        return [pscustomobject]@{ verified = $true; installed = -not [bool]$VerifyOnly; fingerprint = [LeigodSigningKeyCodec]::Fingerprint($xml) }
    } finally { $xml = $null }
}
