<#
.SYNOPSIS
  Runs the full lektor pipeline in one go: build tools -> encode audio -> patch game files ->
  repack into a mod. See README.md for what each stage actually does and what you need to have
  ready first (game copy, .usmap, GameReader audio, Wwise, UnrealReZen).

.PARAMETER Deploy
  If set, also copies the finished mod straight into "<gameRoot>\Stalker2\Content\Paks\~mods\"
  (clearing any previous files there first). Without this switch, the build just ends up in
  build\Release\ and you copy it yourself.

.EXAMPLE
  .\build.ps1
  .\build.ps1 -Deploy
#>
param(
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$modName = "LektorMainOnly_P"

function Step($msg) {
    Write-Host ""
    Write-Host "=== $msg ===" -ForegroundColor Cyan
}

if (-not (Test-Path "$root\config.json")) {
    Write-Host "config.json not found. Copy config.example.json to config.json and fill in your paths first (see README.md)." -ForegroundColor Red
    exit 1
}
$config = Get-Content "$root\config.json" -Raw | ConvertFrom-Json

Step "1/4 - Building BatchEncoder and FullPatcher"
dotnet build "$root\scripts\BatchEncoder" -c Release
if ($LASTEXITCODE -ne 0) { exit 1 }
dotnet build "$root\scripts\FullPatcher" -c Release
if ($LASTEXITCODE -ne 0) { exit 1 }

Step "2/4 - Encoding your GameReader audio (this can take several minutes for the full set)"
dotnet run --project "$root\scripts\BatchEncoder" -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit 1 }

Step "3/4 - Patching the game's own dialogue files"
dotnet run --project "$root\scripts\FullPatcher" -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit 1 }

Step "4/4 - Repacking into a mod (UnrealReZen)"
if (-not $config.unrealRezenExe -or -not (Test-Path $config.unrealRezenExe)) {
    Write-Host "unrealRezenExe in config.json doesn't point at a real UnrealReZen.exe." -ForegroundColor Red
    Write-Host "Build it first - see docs/unrealrezen-build.md." -ForegroundColor Red
    exit 1
}
if (-not $config.gameRoot) {
    Write-Host "gameRoot missing from config.json (the game's top-level install folder, not the Paks subfolder)." -ForegroundColor Red
    exit 1
}
$releaseDir = "$root\build\Release"
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
& $config.unrealRezenExe `
    --game-dir $config.gameRoot `
    --content-path "$root\build\ModOutput" `
    --engine-version GAME_UE5_5 `
    --compression-format Oodle `
    --output-path "$releaseDir\$modName.utoc"
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host ""
Write-Host "Done. Mod files: $releaseDir\$modName.pak / .ucas / .utoc" -ForegroundColor Green

if ($Deploy) {
    Step "Deploying to ~mods"
    $modsDir = "$($config.gameRoot)\Stalker2\Content\Paks\~mods"
    New-Item -ItemType Directory -Force -Path $modsDir | Out-Null
    Remove-Item "$modsDir\*" -Force -ErrorAction SilentlyContinue
    Copy-Item "$releaseDir\$modName.pak", "$releaseDir\$modName.ucas", "$releaseDir\$modName.utoc" -Destination $modsDir -Force
    Write-Host "Copied to $modsDir - set voice language to English in-game and test." -ForegroundColor Green
} else {
    Write-Host "Not deployed (run with -Deploy to also copy into ~mods automatically)." -ForegroundColor Yellow
}
