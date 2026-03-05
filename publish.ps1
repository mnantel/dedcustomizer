# publish.ps1 — Local release build
# Usage: .\publish.ps1 [-Version 1.0.0]

param(
    [string]$Version = "0.0.0-local"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "Building DedCustomizer $Version..." -ForegroundColor Cyan

dotnet publish "$root/DcsDedGui/DcsDedGui.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:PublishReadyToRun=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -o "$root/publish/gui"

dotnet publish "$root/DcsDedBridge/DcsDedBridge.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:PublishReadyToRun=true `
    -p:Version=$Version `
    -o "$root/publish/bridge"

Compress-Archive -Force `
    -Path "$root/publish/gui/DcsDedGui.exe" `
    -DestinationPath "$root/publish/DedCustomizer-$Version.zip"

Compress-Archive -Force `
    -Path "$root/publish/bridge/DcsDedBridge.exe" `
    -DestinationPath "$root/publish/DedCustomizer-Bridge-$Version.zip"

Write-Host ""
Write-Host "Done. Artifacts in ./publish/" -ForegroundColor Green
Get-Item "$root/publish/*.zip" | ForEach-Object { Write-Host "  $_" }
