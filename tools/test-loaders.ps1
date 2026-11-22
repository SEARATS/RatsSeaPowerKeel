[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDir,
    [Parameter(Mandatory)][string]$Cli,
    [Parameter(Mandatory)][string]$LegacyStub,
    [Parameter(Mandatory)][string]$LegacyLoader,
    [string]$AnchorChainPreloader,
    [string]$AnchorChainLoader,
    [switch]$StartupOnly,
    [ValidateRange(10, 600)][int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$Cli = (Resolve-Path -LiteralPath $Cli).Path
$LegacyStub = (Resolve-Path -LiteralPath $LegacyStub).Path
$LegacyLoader = (Resolve-Path -LiteralPath $LegacyLoader).Path
if ([bool]$AnchorChainPreloader -ne [bool]$AnchorChainLoader) {
    throw 'Provide both Anchor Chain DLLs to run the local Anchor Chain case.'
}
if ($AnchorChainLoader) {
    $AnchorChainPreloader = (Resolve-Path -LiteralPath $AnchorChainPreloader).Path
    $AnchorChainLoader = (Resolve-Path -LiteralPath $AnchorChainLoader).Path
}

$repo = Split-Path -Parent $PSScriptRoot
foreach ($component in 'SeaPowerKeel', 'SeaPowerKeel.Stub') {
    & dotnet build (Join-Path $repo "src/$component") -c Release "-p:GameDir=$GameDir" -p:Deploy=false --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $component." }
}
foreach ($phase in 'Early', 'Late') {
    & dotnet build (Join-Path $repo 'tools/startup-fixtures/Plugins') -c Release "-p:GameDir=$GameDir" "-p:FixturePhase=$phase" -p:Deploy=false --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $phase fixtures." }
}
foreach ($name in 'Failure', 'Success', 'Disabled') {
    & dotnet build (Join-Path $repo 'tools/startup-fixtures/Preparation') -c Release "-p:FixtureName=$name" --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $name preparation fixture." }
}

$newLoader = Join-Path $repo 'src/SeaPowerKeel/bin/Release/netstandard2.1/SeaPowerKeel.dll'
$newStub = Join-Path $repo 'src/SeaPowerKeel.Stub/bin/Release/netstandard2.1/SeaPowerKeel.Stub.dll'
$plugins = Join-Path $GameDir 'BepInEx/plugins'
$keelTarget = Join-Path $plugins 'SeaPowerKeel.Stub.dll'
$anchorTarget = Join-Path $plugins 'AnchorChain.Preloader.dll'
$streaming = Join-Path $GameDir 'Sea Power_Data/StreamingAssets'
$modName = 'KeelCompatibilityTest'
$mod = Join-Path $streaming $modName
$earlyMod = Join-Path $streaming 'KeelEarlyFixture'
$lateMod = Join-Path $streaming 'KeelLateFixture'
$disabledMod = Join-Path $streaming 'KeelDisabledFixture'
$testFolders = @($mod, $earlyMod, $lateMod, $disabledMod)
$settings = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData/LocalLow/Triassic Games/Sea Power/usersettings.ini'
$bepinLog = Join-Path $GameDir 'BepInEx/LogOutput.log'
$config = Join-Path $GameDir 'BepInEx/config/dev.sewerlabs.seapowerkeel.cfg'
$bepin = Join-Path $GameDir 'BepInEx'
$paths = @($keelTarget, $anchorTarget, $settings, $config, (Join-Path $bepin 'cache/chainloader_typeloader.dat'))

if ((Get-Process -Name 'Sea Power' -ErrorAction SilentlyContinue) -or ($testFolders | Where-Object { Test-Path -LiteralPath $_ })) {
    throw 'Close Sea Power and remove leftover Keel test folders before running.'
}
$recoveryMarker = Join-Path (Split-Path -Parent $settings) 'launch_incomplete.marker'
if (Test-Path -LiteralPath $recoveryMarker) {
    throw 'Sea Power has a pending startup recovery prompt. Complete a normal launch before running the matrix.'
}
$original = [IO.File]::ReadAllText($settings)
$section = [regex]::new('(?m)^\[LoadOrder\][^\S\r\n]*\r?\n(?:^(?!\[)[^\r\n]*(?:\r?\n|\z))*')
if (!$section.IsMatch($original)) { throw 'Sea Power must have saved its mod list at least once.' }
$testSettings = $section.Replace($original, "[LoadOrder]$([Environment]::NewLine)Mod1Directory=$modName,True,0$([Environment]::NewLine)NumberOfModFiles=1$([Environment]::NewLine)$([Environment]::NewLine)", 1)
$startupSettings = $section.Replace($original, @"
[LoadOrder]
Mod1Directory=$modName,True,0
Mod2Directory=KeelEarlyFixture,True,0
Mod3Directory=KeelLateFixture,True,0
Mod4Directory=KeelDisabledFixture,False,0
NumberOfModFiles=4

"@, 1)

$backup = Join-Path ([IO.Path]::GetTempPath()) "keel-loader-check-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $backup | Out-Null
$paths += @(Get-ChildItem -LiteralPath $bepin -File -Filter 'Keel*' | ForEach-Object FullName)
$savedFiles = @()
foreach ($path in $paths | Select-Object -Unique) {
    $saved = Join-Path $backup "$($savedFiles.Count).bak"
    $hash = $null
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Copy-Item -LiteralPath $path -Destination $saved
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    $savedFiles += [pscustomobject]@{ Path = $path; Backup = $saved; Hash = $hash }
}
$savedFiles | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backup 'original-files.json')
$sessionStarted = [DateTime]::UtcNow

function Stop-TestGame([DateTime]$since) {
    foreach ($game in Get-Process -Name 'Sea Power' -ErrorAction SilentlyContinue) {
        if ($game.StartTime.ToUniversalTime() -lt $since -or $game.Path -ne (Join-Path $GameDir 'Sea Power.exe')) {
            throw "Sea Power process $($game.Id) was not launched by this test; it will not be stopped."
        }
        if (!$game.CloseMainWindow() -or !$game.WaitForExit(15000)) {
            Stop-Process -Id $game.Id
            if (!$game.WaitForExit(10000)) { throw "Test process $($game.Id) did not exit." }
        }
    }
}

function Test-Loader($name, $stub, $loader, [switch]$AnchorChain, [switch]$Startup, [switch]$Preparation) {
    $stubFile = if ($AnchorChain) { $anchorTarget } else { $keelTarget }
    $loaderName = if ($AnchorChain) { 'AnchorChain.dll' } else { 'SeaPowerKeel.dll' }
    $version = [Reflection.AssemblyName]::GetAssemblyName($loader).Version.ToString(3)
    $boot = if ($AnchorChain) { "Loaded AnchorChain V$version!" } else {
        $stubVersion = [Reflection.AssemblyName]::GetAssemblyName($stub).Version.ToString(3)
        "R.A.T.S. Keel $version (stub $stubVersion)"
    }
    $done = if ($AnchorChain) { $boot } else { '[Info   :R.A.T.S. Keel] Done:' }
    $destination = Join-Path $mod $loaderName
    $launched = [DateTime]::UtcNow
    try {
        Copy-Item -LiteralPath $stub -Destination $stubFile -Force
        Copy-Item -LiteralPath $loader -Destination $destination -Force
        [IO.File]::WriteAllText($settings, $(if ($Startup) { $startupSettings } else { $testSettings }))
        [IO.File]::WriteAllText((Join-Path $earlyMod 'expect-preparation.txt'), $Preparation.ToString().ToLowerInvariant())
        $launch = & $Cli launch --game-dir $GameDir --confirm --timeout 45 --json
        if ($LASTEXITCODE -ne 0) { throw "Sea Power launch failed for $name." }
        $result = $launch | ConvertFrom-Json
        $gameId = $result.processIds[0]
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $contents = ''
        do {
            Start-Sleep -Seconds 1
            $file = Get-Item -LiteralPath $bepinLog -ErrorAction SilentlyContinue
            if ($file -and $file.LastWriteTimeUtc -ge $launched.ToUniversalTime()) {
                $contents = Get-Content -LiteralPath $bepinLog -Raw -ErrorAction SilentlyContinue
                if ($null -eq $contents) { $contents = '' }
            }
            $complete = $contents.Contains($done) -and (!$Startup -or $contents.Contains('KEEL_FIXTURE PASS summary'))
            if ($complete -or $contents.Contains('The previous game launch did not finish loading') -or
                !(Get-Process -Id $gameId -ErrorAction SilentlyContinue)) { break }
        } while ((Get-Date) -lt $deadline)
        $capture = Join-Path $backup "$name.log"
        Set-Content -LiteralPath $capture -Value $contents
        if (!$contents.Contains($boot) -or !$contents.Contains($done)) { throw "Loader did not finish for $name. See $capture." }
        if ($Startup) {
            $markers = @('early-dependency', 'early-consumer', 'normal-dependency', 'deferred-consumer', 'progress-handoff', 'summary')
            if ($Preparation) { $markers += 'preparation' }
            foreach ($marker in $markers) {
                if (!$contents.Contains("KEEL_FIXTURE PASS $marker")) { throw "Missing $marker in $capture." }
            }
            if ($contents.Contains('KEEL_FIXTURE disabled callback ran') -or
                ($Preparation -and !$contents.Contains('KEEL_FIXTURE intentional preparation failure'))) {
                throw "Preparation isolation failed. See $capture."
            }
        }
        Write-Output "PASS $name ($version)"
    }
    finally {
        Stop-TestGame $launched
        if (Test-Path -LiteralPath $bepinLog -PathType Leaf) {
            Copy-Item -LiteralPath $bepinLog -Destination (Join-Path $backup "$name.log") -Force
        }
        foreach ($path in $stubFile, $destination, $recoveryMarker) {
            if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path }
        }
    }
}

try {
    foreach ($path in Get-ChildItem -LiteralPath $bepin -File -Filter 'Keel*') { Remove-Item -LiteralPath $path.FullName }
    [IO.File]::WriteAllText($config, "[Loading]`nDisabledPlugins=`n[WhatsNew]`nShow=false`n")
    foreach ($path in $keelTarget,$anchorTarget) {
        if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path }
    }
    foreach ($folder in $testFolders) {
        New-Item -ItemType Directory -Path $folder | Out-Null
        Set-Content -LiteralPath (Join-Path $folder '_info.ini') -Value @('[Language_en]', 'Name=Keel compatibility test', '[Compatibility]', 'GreaterThanEqualToVersion=0.9.0')
    }
    New-Item -ItemType Directory -Path (Join-Path $mod 'lib'), (Join-Path $earlyMod 'nested') | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'lib/AnchorChain.keel.bin') -Destination (Join-Path $mod 'lib')
    $fixtureBin = Join-Path $repo 'tools/startup-fixtures/Plugins/bin/Release'
    Copy-Item -LiteralPath (Join-Path $fixtureBin 'Early/netstandard2.1/KeelFixture.Early.dll') -Destination $earlyMod
    Copy-Item -LiteralPath (Join-Path $fixtureBin 'Late/netstandard2.1/KeelFixture.Late.dll') -Destination $lateMod
    $preparationBin = Join-Path $repo 'tools/startup-fixtures/Preparation/bin/Release'
    Copy-Item -LiteralPath (Join-Path $preparationBin 'Failure/netstandard2.1/KeelFixture.Failure.Preloader.dll') -Destination (Join-Path $earlyMod 'a.Failure.Preloader.dll')
    Copy-Item -LiteralPath (Join-Path $preparationBin 'Success/netstandard2.1/KeelFixture.Success.Preloader.dll') -Destination (Join-Path $earlyMod 'nested/b.Success.Preloader.dll')
    Copy-Item -LiteralPath (Join-Path $preparationBin 'Disabled/netstandard2.1/KeelFixture.Disabled.Preloader.dll') -Destination $disabledMod
    [IO.File]::WriteAllText((Join-Path $earlyMod 'fixture-input.txt'), 'fixture-input')
    if (!$StartupOnly) {
        Test-Loader 'old-stub-old-loader' $LegacyStub $LegacyLoader
        Test-Loader 'old-stub-new-loader' $LegacyStub $newLoader
        Test-Loader 'new-stub-old-loader' $newStub $LegacyLoader
        Test-Loader 'new-stub-new-loader' $newStub $newLoader
    }
    Test-Loader 'startup-old-stub' $LegacyStub $newLoader -Startup
    Test-Loader 'startup-new-stub' $newStub $newLoader -Startup -Preparation
    if ($AnchorChainLoader -and !$StartupOnly) {
        Test-Loader 'anchor-chain' $AnchorChainPreloader $AnchorChainLoader -AnchorChain
    }
}
finally {
    Stop-TestGame $sessionStarted
    foreach ($file in Get-ChildItem -LiteralPath $bepin -File -Filter 'Keel*') {
        if ($file.FullName -notin $savedFiles.Path) { Remove-Item -LiteralPath $file.FullName }
    }
    foreach ($file in $savedFiles) {
        if ($file.Hash) {
            Copy-Item -LiteralPath $file.Backup -Destination $file.Path -Force
            if ((Get-FileHash -LiteralPath $file.Path -Algorithm SHA256).Hash -ne $file.Hash) { throw "Restore failed for $($file.Path). Originals: $backup" }
        } elseif (Test-Path -LiteralPath $file.Path -PathType Leaf) {
            Remove-Item -LiteralPath $file.Path
        }
    }
    if (Test-Path -LiteralPath $recoveryMarker -PathType Leaf) { Remove-Item -LiteralPath $recoveryMarker }
    foreach ($path in $testFolders) {
        if (Test-Path -LiteralPath $path) {
            $folder = Get-Item -LiteralPath $path
            if ($folder.Parent.FullName -ne (Get-Item -LiteralPath $streaming).FullName) { throw 'Unexpected test folder.' }
            Move-Item -LiteralPath $folder.FullName -Destination $backup
        }
    }
    Write-Output "Test logs and original files: $backup"
}
