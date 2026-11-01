#requires -Version 7.2
param([string]$Stage)
$ErrorActionPreference = 'Stop'
if (-not $Stage) { $Stage = (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'artifacts/build-result.json') | ConvertFrom-Json).stage }
$Stage = [IO.Path]::GetFullPath($Stage)
$release = Get-Content -Raw -LiteralPath (Join-Path $Stage 'release.json') | ConvertFrom-Json
$forbidden = Get-ChildItem -LiteralPath $Stage -Recurse -File | Where-Object { $_.Extension -in @('.p8','.pfx','.p12','.dpapi','.pem','.key') -or $_.Name -match '^(installation|client-profile|plugin-health|diagnostics|qa-|events|operation-error)' }
if ($forbidden) { throw 'Private or diagnostic files found in release stage.' }
foreach ($file in Get-ChildItem -LiteralPath $Stage -Recurse -File | Where-Object Extension -notin @('.exe')) {
    if ([IO.File]::ReadAllText($file.FullName) -match '(?im)[A-Z]:[\\/]Users[\\/](?!Public\b)[^\\/\s]+|^\s*<RSAKeyValue>\s*<Modulus>[^\r\n]*<D>|^-----BEGIN (?:RSA )?PRIVATE KEY-----') { throw ('Private content found in ' + $file.Name) }
}
$destination = Join-Path $PSScriptRoot ('artifacts/release-v' + $release.version)
if ($release.development) { $destination += '-development-' + (Split-Path (Split-Path $Stage) -Leaf) }
[IO.Directory]::CreateDirectory($destination) | Out-Null
$zip = Join-Path $destination ('leigod-autopause-v' + $release.version + '.zip')
if (Test-Path -LiteralPath $zip) { throw 'Release archive already exists. Do not overwrite a published version.' }
Compress-Archive -LiteralPath $Stage -DestinationPath $zip
$hash = (Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zip+'.sha256', $hash+' *'+[IO.Path]::GetFileName($zip)+[Environment]::NewLine, [Text.UTF8Encoding]::new($false))
$result = [ordered]@{ version=$release.version; zip=$zip; sha256=$hash; bytes=(Get-Item -LiteralPath $zip).Length; development=[bool]$release.development }
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'artifacts/export-result.json'), ($result|ConvertTo-Json), [Text.UTF8Encoding]::new($false))
[pscustomobject]$result
