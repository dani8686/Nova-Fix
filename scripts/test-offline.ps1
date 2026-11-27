# NOVA FIX — run all test suites (offline; no network needed after restore).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot\..

foreach ($suite in 'UnitTests','IntegrationTests','OfflineTests','UITests') {
    Write-Host "=== $suite ===" -ForegroundColor Cyan
    dotnet test "tests\NOVA.FIX.$suite\NOVA.FIX.$suite.csproj" -c Release `
        --logger "trx;LogFileName=$suite.trx" --results-directory TestResults
    if ($LASTEXITCODE -ne 0) { throw "$suite FAILED" }
}
Write-Host "All suites passed." -ForegroundColor Green
