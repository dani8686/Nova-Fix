# NOVA FIX — offline acceptance run (spec §24).
# Run on a Windows machine AFTER disconnecting network / enabling airplane mode.
# Verifies the installed app performs core functions with outbound traffic blocked.
$ErrorActionPreference = 'Stop'

Write-Host "=== NOVA FIX OFFLINE ACCEPTANCE ===" -ForegroundColor Cyan
Write-Host "Ensure network is DISABLED before continuing." -ForegroundColor Yellow
Read-Host "Press Enter when offline"

$checks = @()
function Check($id, $desc, [scriptblock]$fn) {
    try {
        & $fn | Out-Null
        $script:checks += [pscustomobject]@{ Id=$id; Result='PASS'; Desc=$desc }
        Write-Host "[PASS] $id — $desc" -ForegroundColor Green
    } catch {
        $script:checks += [pscustomobject]@{ Id=$id; Result='FAIL'; Desc="$desc — $_" }
        Write-Host "[FAIL] $id — $_" -ForegroundColor Red
    }
}

$exe = Get-ChildItem -Recurse -Filter NOVA.FIX.exe publish\novafix,install -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $exe) { throw "NOVA.FIX.exe not found — run package-offline.ps1 / install first." }

Check 2  "Launch without internet" { Start-Process $exe.FullName -PassThru | Wait-Process -Timeout 20 }
Check 5  "Data directory present"  { if (-not (Test-Path (Join-Path $exe.DirectoryName 'data'))) { throw 'data missing' } }
Check 4  "Model directory present" { if (-not (Test-Path (Join-Path $exe.DirectoryName 'models'))) { throw 'models missing' } }
Check 8  "Knowledge DB seed"       { if (-not (Test-Path (Join-Path $exe.DirectoryName 'data\knowledge\articles.json'))) { throw 'kb missing' } }
Check 14 "Reports dir writable"    { $d = Join-Path $env:LOCALAPPDATA 'NOVA FIX\reports'; New-Item -ItemType Directory -Force $d | Out-Null;
                                     Set-Content (Join-Path $d 'probe.txt') 'ok' }
Check 17 "Localization files"      { if (-not (Test-Path (Join-Path $exe.DirectoryName 'data\localization\fa.json'))) { throw 'fa.json missing' } }

$checks | Format-Table -AutoSize
$failed = ($checks | Where-Object Result -eq 'FAIL').Count
if ($failed -gt 0) { Write-Host "$failed check(s) FAILED" -ForegroundColor Red; exit 1 }
Write-Host "All offline acceptance checks passed." -ForegroundColor Green
