[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDir,
    [Parameter(Mandatory)][string]$Cli,
    [Parameter(Mandatory)][string]$LegacyStub,
    [Parameter(Mandatory)][string]$LegacyLoader
)

$ErrorActionPreference = 'Stop'
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$Cli = (Resolve-Path -LiteralPath $Cli).Path
if (Get-Process -Name 'Sea Power' -ErrorAction SilentlyContinue) { throw 'Close Sea Power first.' }
$work = Join-Path ([IO.Path]::GetTempPath()) "keel-cleanup-check-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
$wrapper = Join-Path $work 'launch-failure.ps1'
Set-Content -LiteralPath $wrapper -Value @'
$config = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'case.json') -Raw | ConvertFrom-Json
$result = & $config.Cli @args
if ($LASTEXITCODE -ne 0) { throw "Real launch failed: $result" }
if ($config.Mode -eq 'exit') { $result; exit 42 }
'not-json'
exit 0
'@

$streaming = Join-Path $GameDir 'Sea Power_Data/StreamingAssets'
foreach ($mode in 'exit', 'json') {
    @{ Cli = $Cli; Mode = $mode } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'case.json')
    $started = [DateTime]::UtcNow
    $failure = $null
    try {
        & (Join-Path $PSScriptRoot 'test-loaders.ps1') -GameDir $GameDir -Cli $wrapper -LegacyStub $LegacyStub -LegacyLoader $LegacyLoader
    }
    catch { $failure = $_.Exception.Message }
    if (!$failure -or ($mode -eq 'exit' -and $failure -notlike '*Sea Power launch failed*') -or
        ($mode -eq 'json' -and $failure -notlike '*Conversion from JSON failed*')) {
        throw "The $mode case did not reach the intended failure: $failure"
    }
    $backup = Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Filter 'keel-loader-check-*' |
        Where-Object { $_.CreationTimeUtc -ge $started } | Sort-Object CreationTimeUtc -Descending | Select-Object -First 1
    if (!$backup) { throw 'Missing rollback manifest.' }
    $files = Get-Content -LiteralPath (Join-Path $backup.FullName 'original-files.json') -Raw | ConvertFrom-Json
    foreach ($file in $files) {
        if ($file.Hash) {
            if (!(Test-Path -LiteralPath $file.Path -PathType Leaf) -or
                (Get-FileHash -LiteralPath $file.Path -Algorithm SHA256).Hash -ne $file.Hash) {
                throw "Original file was not restored: $($file.Path)"
            }
        } elseif (Test-Path -LiteralPath $file.Path) { throw "Unexpected file remains: $($file.Path)" }
    }
    $extra = Get-ChildItem -LiteralPath (Join-Path $GameDir 'BepInEx') -File -Filter 'Keel*' |
        Where-Object { $_.FullName -notin $files.Path }
    if ($extra) { throw "Unexpected Keel state remains: $($extra.Name -join ', ')" }
    $settingsDir = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData/LocalLow/Triassic Games/Sea Power'
    if (Get-Process -Name 'Sea Power' -ErrorAction SilentlyContinue) { throw 'The test game remains open.' }
    if (Test-Path -LiteralPath (Join-Path $settingsDir 'launch_incomplete.marker')) { throw 'A recovery prompt remains.' }
    foreach ($name in 'KeelCompatibilityTest', 'KeelEarlyFixture', 'KeelLateFixture', 'KeelDisabledFixture') {
        if (Test-Path -LiteralPath (Join-Path $streaming $name)) { throw "Test folder remains: $name" }
    }
    Write-Output "PASS $mode failure: process closed, files restored by SHA-256, no test folders or recovery marker."
}
Write-Output "Failure-injection wrapper: $work"
