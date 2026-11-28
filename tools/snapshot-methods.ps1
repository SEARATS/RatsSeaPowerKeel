param(
    [Parameter(Mandatory)][string]$BuildVersion,
    [string]$GameDir = $(if ($env:GameDir) { $env:GameDir } else { 'E:\Games\steamapps\common\Sea Power' }),
    [string]$ManagedDir,
    [string]$BinariesRepo = (Join-Path $PSScriptRoot '..\external\binaries')
)

$ErrorActionPreference = 'Stop'
if (!$ManagedDir) { $ManagedDir = Join-Path $GameDir 'Sea Power_Data\Managed' }
if (!(Test-Path -LiteralPath (Join-Path $BinariesRepo '.git'))) {
    throw 'Initialize external/binaries or pass -BinariesRepo pointing at a SEARATS/binaries checkout.'
}

dotnet run -c Release --project (Join-Path $PSScriptRoot 'offline-check') "-p:GameDir=$GameDir" -- `
    --snapshot-methods $BuildVersion $ManagedDir (Join-Path $BinariesRepo 'keel\method-history-v1')
if ($LASTEXITCODE -ne 0) { throw 'Could not generate the method history snapshot.' }
