#requires -Version 7.2
param([Parameter(Mandatory)][string]$Archive, [Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
$settings = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'release.config.json') | ConvertFrom-Json
$name = [IO.Path]::GetFileName($Archive)
$item = [ordered]@{ version=$settings.version; compatibilityMode='capability'; url=('https://github.com/'+$settings.repository+'/releases/download/v'+$settings.version+'/'+[uri]::EscapeDataString($name)); sha256=(Get-FileHash -LiteralPath $Archive).Hash.ToLowerInvariant(); bytes=(Get-Item -LiteralPath $Archive).Length }
. (Join-Path $PSScriptRoot 'publisher-key.ps1')
try {
    $payload = [Text.Encoding]::UTF8.GetBytes((@{schema=1;releases=@($item)}|ConvertTo-Json -Depth 6))
    $envelope = @{ payload=[Convert]::ToBase64String($payload); signature=[Convert]::ToBase64String($rsa.SignData($payload,[Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'))) } | ConvertTo-Json
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($Output), $envelope, [Text.UTF8Encoding]::new($false))
} finally { $rsa.Dispose() }
