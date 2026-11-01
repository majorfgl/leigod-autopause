#requires -Version 7.2
param([switch]$Development)
$ErrorActionPreference = 'Stop'
$settings = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'release.config.json') | ConvertFrom-Json
if ($settings.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a three-part numeric release version.' }
$releaseVersion = $settings.version
$buildRoot = Join-Path $PSScriptRoot ('artifacts/build-' + [guid]::NewGuid().ToString('N'))
$stage = Join-Path $buildRoot ('leigod-autopause-v' + $releaseVersion)
[IO.Directory]::CreateDirectory((Join-Path $stage 'plugin')) | Out-Null
$publicXml = Join-Path $buildRoot 'release-public-key.xml'
$rsa = $null
try {
    if ($Development) {
        $rsa = [Security.Cryptography.RSACryptoServiceProvider]::new(3072)
        $rsa.PersistKeyInCsp = $false
        [IO.File]::WriteAllText($publicXml, $rsa.ToXmlString($false), [Text.UTF8Encoding]::new($false))
        $settings.updateFeed = ''
    } else {
        . (Join-Path $PSScriptRoot 'publisher-key.ps1')
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'launcher/release-public-key.xml') -Destination $publicXml
    }
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'plugin') -Filter '*.cjs' -File | Copy-Item -Destination (Join-Path $stage 'plugin')
    foreach ($name in @('LICENSE.txt','README.md','CONTRIBUTING.md','使用说明.txt')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage }
    [IO.Directory]::CreateDirectory((Join-Path $stage 'docs')) | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'docs') | Where-Object Name -ne 'maintainers' | Copy-Item -Destination (Join-Path $stage 'docs') -Recurse
    [IO.File]::WriteAllText((Join-Path $stage 'release.config.json'), ($settings | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $source = Join-Path $stage 'source'; [IO.Directory]::CreateDirectory($source) | Out-Null
    foreach ($folder in @('plugin','launcher','tests','scripts','docs','.github')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $folder) -Destination (Join-Path $source $folder) -Recurse
    }
    foreach ($name in @('LICENSE.txt','README.md','CONTRIBUTING.md','使用说明.txt','build.ps1','export.ps1','publisher-key.ps1','publish-feed.ps1','release.config.json','.gitignore')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $source }
    Copy-Item -LiteralPath $publicXml -Destination (Join-Path $source 'launcher/release-public-key.xml') -Force
    $reference = Join-Path $PSScriptRoot 'reference/original-package.json'
    if ((Get-FileHash -LiteralPath $reference).Hash -ne 'A0009496AA724FC7680F50A3AE1B5BA7DE3FC671E3C1B6AE2094581337FBB975') { throw 'Reference metadata mismatch.' }
    Copy-Item -LiteralPath $reference -Destination (Join-Path $stage 'original-package.json')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'reference') -Destination (Join-Path $source 'reference') -Recurse
    $pluginHashes = Get-ChildItem -LiteralPath (Join-Path $stage 'plugin') -File | Sort-Object Name | ForEach-Object { $_.Name + '|' + (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
    [IO.File]::WriteAllLines((Join-Path $stage 'plugin-hashes.txt'), [string[]]$pluginHashes, [Text.UTF8Encoding]::new($false))
    $assemblyVersion = $releaseVersion + '.0'
    $feedLiteral = $settings.updateFeed.Replace('\','\\').Replace('"','\"')
    $developmentLiteral = ([bool]$Development).ToString().ToLowerInvariant()
    $generated = "using System.Reflection;`n[assembly:AssemblyVersion(`"$assemblyVersion`")]`n[assembly:AssemblyFileVersion(`"$assemblyVersion`")]`nnamespace LeigodAutoPause { static partial class Program { const string ToolVersion=`"$releaseVersion`"; const string DefaultFeedUrl=`"$feedLiteral`"; const bool DevelopmentBuild=$developmentLiteral; } }`n"
    $generatedPath = Join-Path $buildRoot 'BuildInfo.cs'
    [IO.File]::WriteAllText($generatedPath, $generated, [Text.UTF8Encoding]::new($false))
    $manifestPath = Join-Path $buildRoot 'manager.manifest'
    $manifest = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'launcher/manager.manifest')) -replace 'assemblyIdentity version="[^"]+"', ('assemblyIdentity version="' + $assemblyVersion + '"')
    if ($Development) { $manifest = $manifest.Replace('level="requireAdministrator"','level="asInvoker"') }
    [IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'launcher') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
    $compilerArgs = @('/nologo','/target:winexe','/platform:anycpu','/codepage:65001',('/resource:'+$publicXml+',LeigodAutoPause.ReleasePublicKey'),('/win32manifest:'+$manifestPath),('/out:'+(Join-Path $stage '雷神自动暂停.exe')),'/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Core.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll') + $sources + @($generatedPath)
    & $compiler @compilerArgs
    if ($LASTEXITCODE -ne 0) { throw 'Manager compilation failed.' }
    $files = @(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
        @{ path=$_.FullName.Substring($stage.Length+1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant(); bytes=$_.Length }
    })
    $release = @{ schema=1; version=$releaseVersion; compatibilityMode='capability'; development=[bool]$Development; files=$files } | ConvertTo-Json -Depth 8
    $bytes = [Text.Encoding]::UTF8.GetBytes($release)
    [IO.File]::WriteAllBytes((Join-Path $stage 'release.json'), $bytes)
    [IO.File]::WriteAllText((Join-Path $stage 'release.sig'), [Convert]::ToBase64String($rsa.SignData($bytes,[Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'))), [Text.UTF8Encoding]::new($false))
    if ($Development) {
        $fixture = [Text.Encoding]::UTF8.GetBytes((@{schema=1;releases=@(@{version=$releaseVersion;compatibilityMode='capability';url='https://updates.invalid/fixture.zip';sha256=('0'*64);bytes=1})}|ConvertTo-Json -Depth 6))
        $envelope = @{payload=[Convert]::ToBase64String($fixture);signature=[Convert]::ToBase64String($rsa.SignData($fixture,[Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256')))}|ConvertTo-Json
        [IO.File]::WriteAllText((Join-Path $buildRoot 'test-feed.json'),$envelope,[Text.UTF8Encoding]::new($false))
    }
    $result = [ordered]@{ version=$releaseVersion; development=[bool]$Development; stage=$stage; publicKeySha256=(Get-FileHash -LiteralPath $publicXml).Hash.ToLowerInvariant() }
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'artifacts/build-result.json'), ($result | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    [pscustomobject]$result
} finally { if ($rsa) { $rsa.Dispose() } }
