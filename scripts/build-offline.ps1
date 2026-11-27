# NOVA FIX — offline build (restores ONLY from packages/offline).
# Run on a Windows machine with the .NET 8 SDK installed. No internet required
# when packages/offline is populated (scripts/make-offline-feed.ps1).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot\..

Write-Host "=== NOVA FIX offline build ===" -ForegroundColor Cyan

# Force offline restore: clear other sources for this invocation.
$env:RestoreSources = (Resolve-Path packages\offline).Path
dotnet restore NOVA.FIX.sln --source $env:RestoreSources
if ($LASTEXITCODE -ne 0) { throw "Restore failed. Populate packages\offline first (make-offline-feed.ps1)." }

dotnet build NOVA.FIX.sln -c Release --no-restore -p:PlatformTarget=x64
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Write-Host "Build OK: bin\Release" -ForegroundColor Green
