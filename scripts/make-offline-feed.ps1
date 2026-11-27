# NOVA FIX — assemble the offline NuGet feed on a CONNECTED preparation machine.
# Run once with internet access; afterwards builds run fully offline.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot\..

New-Item -ItemType Directory -Force packages\offline | Out-Null

Write-Host "Restoring to populate the global cache..." -ForegroundColor Cyan
dotnet restore NOVA.FIX.sln
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

$cache = Join-Path $env:USERPROFILE '.nuget\packages'
$feed  = Resolve-Path packages\offline

# Copy every package the solution needs as .nupkg (flat feed).
$packages = @(
  'microsoft.data.sqlite','sqlitepclraw.bundle_e_sqlite3','sqlitepclraw.provider.dynamic_cdecl',
  'sqlitepclraw.core','e_sqlite3',
  'system.management','system.diagnostics.eventlog','system.diagnostics.performancecounter',
  'system.serviceprocess.servicecontroller',
  'llamasharp','llamasharp.backend.cpu',
  'microsoft.net.test.sdk','xunit','xunit.runner.visualstudio','coverlet.collector',
  'microsoft.extensions.logging.abstractions','microsoft.bcl.asyncinterfaces',
  'communitytoolkit.highperformance','system.linq.async','microsoft.extensions.ai.abstractions'
)

foreach ($pkg in $packages) {
    $dir = Join-Path $cache $pkg
    if (-not (Test-Path $dir)) { Write-Warning "not in cache: $pkg"; continue }
    Get-ChildItem $dir -Recurse -Filter *.nupkg | ForEach-Object {
        Copy-Item $_.FullName $feed -Force
    }
}
Write-Host "Offline feed ready: $feed ($((Get-ChildItem $feed -Filter *.nupkg).Count) packages)" -ForegroundColor Green
