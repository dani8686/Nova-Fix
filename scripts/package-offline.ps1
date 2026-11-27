# NOVA FIX — full offline package: publish + stage model + zip + installer.
# CONNECTED machine: stages the bundled baseline model (one-time download).
# Then everything else is local. Run Inno Setup (iscc) for the installer.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot\..

$modelDir = 'models'
New-Item -ItemType Directory -Force $modelDir | Out-Null

# --- Stage bundled baseline model (balanced tier) if absent ---
$balanced = Join-Path $modelDir 'novafix-balanced.gguf'
if (-not (Test-Path $balanced)) {
    Write-Host "Staging bundled baseline model (balanced)..." -ForegroundColor Cyan
    # Qwen2.5-1.5B-Instruct Q4_K_M — Apache-2.0, ~1 GB, CPU-friendly.
    $url = 'https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf'
    try {
        Invoke-WebRequest -Uri $url -OutFile $balanced -UseBasicParsing
    } catch {
        Write-Warning "Model download failed: $_"
        Write-Warning "Place any GGUF at $balanced manually, then re-run."
        throw
    }
}

# --- Publish self-contained x64 ---
dotnet publish src\NOVA.FIX.App\NOVA.FIX.App.csproj -c Release -r win-x64 `
    --self-contained true -o publish\novafix
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Copy-Item -Recurse data publish\novafix\data -Force
New-Item -ItemType Directory -Force publish\novafix\models | Out-Null
Copy-Item $balanced publish\novafix\models\ -Force
Copy-Item -Recurse docs\offline publish\novafix\docs -Force -ErrorAction SilentlyContinue

# --- Portable zip ---
Compress-Archive -Path publish\novafix\* -DestinationPath publish\NOVA-FIX-2.0-win-x64-portable.zip -Force
Write-Host "Portable package: publish\NOVA-FIX-2.0-win-x64-portable.zip" -ForegroundColor Green

# --- Installer (requires Inno Setup 6) ---
$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if ($iscc) {
    & iscc installer\novafix.iss
    Write-Host "Installer: installer\Output\NOVA-FIX-Setup-2.0.0.exe" -ForegroundColor Green
} else {
    Write-Warning "Inno Setup (iscc) not found — installer skipped. Portable zip is ready."
}
