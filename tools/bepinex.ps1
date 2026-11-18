# The official BepInEx release that ships next to KeelSetup.exe, unchanged, as its own zip. Dot-sourced by package.ps1 and ci-build.ps1.
# The zip is checked against a pinned SHA-256, so a build never ships anything but that release: not a copy from a game folder
# (up to 0.5.4 package.ps1 took the developer's game copy, which was a different build of the same version; found 2026-11-13).
$BepInExVersion = '5.4.23.2'
$BepInExSha256 = 'f752ce4e838f4c305b9da1404b6745f2cff23b8bfd494f79f0c84d0a01f59b46'

function Get-OfficialBepInEx([string]$root) {
    $cache = Join-Path $root 'obj\bepinex'
    New-Item -ItemType Directory -Force $cache | Out-Null
    $name = "BepInEx_win_x64_$BepInExVersion.zip"
    $zip = Join-Path $cache $name
    if (-not (Test-Path $zip)) {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/$name" -OutFile $zip
    }
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $BepInExSha256) { throw "$name is not the official release (SHA-256 $hash, expected $BepInExSha256). Delete $zip and build again." }
    $dir = Join-Path $cache 'official'
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    Expand-Archive $zip $dir
    return $dir
}

# The checked official zip itself (KeelSetup.exe unpacks only the Doorstop files and BepInEx\core from it).
function Get-OfficialBepInExZip([string]$root) {
    Get-OfficialBepInEx $root | Out-Null
    return Join-Path $root "obj\bepinex\BepInEx_win_x64_$BepInExVersion.zip"
}
