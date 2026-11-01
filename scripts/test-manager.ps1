#requires -Version 7.2
param()
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$build = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'artifacts/build-result.json') | ConvertFrom-Json
$export = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'artifacts/export-result.json') | ConvertFrom-Json
if (-not $build.development -or -not $export.development) { throw 'Automated fixture tests require an isolated development build.' }
$stage = $build.stage
$feed = Join-Path (Split-Path $stage) 'test-feed.json'
if (-not (Test-Path -LiteralPath $feed)) { throw 'Development build did not provide its signed fixture feed.' }
$report = Join-Path $repoRoot 'artifacts/manager-tests.json'
$program = Join-Path $stage '雷神自动暂停.exe'
$arguments = @('--maintenance-self-test','--quiet','--zip',('"'+$export.zip+'"'),'--feed-fixture',('"'+$feed+'"'),'--output',('"'+$report+'"'))
$process = Start-Process -FilePath $program -ArgumentList $arguments -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit(90000)) { $process.Kill(); throw 'Manager tests timed out.' }
if ($process.ExitCode -ne 0) {
    $errorPath = Join-Path $stage 'operation-error.txt'
    if (Test-Path -LiteralPath $errorPath) { throw ([IO.File]::ReadAllText($errorPath)) }
    throw ('Manager test exit code: ' + $process.ExitCode)
}
$result = Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
if (-not $result.ok) { throw 'Manager tests did not pass.' }
$result
