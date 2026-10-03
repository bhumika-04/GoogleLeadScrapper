# Starts DeepLead for local development: API, Worker and the Next.js frontend, each in its own window.
# Usage (from the repo root):  powershell -ExecutionPolicy Bypass -File .\run-dev.ps1
# Stop: close the three windows (or Ctrl+C in each).

$root = $PSScriptRoot

Write-Host "Building backend..." -ForegroundColor Cyan
dotnet build "$root\backend\DeepLead.sln" -v q
if ($LASTEXITCODE -ne 0) { Write-Host "Backend build failed." -ForegroundColor Red; exit 1 }

if (-not (Test-Path "$root\frontend\node_modules")) {
    Write-Host "Installing frontend packages..." -ForegroundColor Cyan
    Push-Location "$root\frontend"; npm install; Pop-Location
}

Start-Process powershell -WorkingDirectory $root -ArgumentList '-NoExit', '-Command',
    "`$Host.UI.RawUI.WindowTitle = 'DeepLead API'; dotnet run --no-build --project backend/src/DeepLead.Api --launch-profile http"

Start-Process powershell -WorkingDirectory $root -ArgumentList '-NoExit', '-Command',
    "`$Host.UI.RawUI.WindowTitle = 'DeepLead Worker'; dotnet run --no-build --project backend/src/DeepLead.Worker"

Start-Process powershell -WorkingDirectory "$root\frontend" -ArgumentList '-NoExit', '-Command',
    "`$Host.UI.RawUI.WindowTitle = 'DeepLead Web'; npm run dev"

Write-Host "Waiting for the web app..." -ForegroundColor Cyan
for ($i = 0; $i -lt 60; $i++) {
    try { Invoke-WebRequest http://localhost:3000/login -UseBasicParsing -TimeoutSec 3 | Out-Null; break } catch { Start-Sleep 2 }
}
Start-Process "http://localhost:3000"
Write-Host "DeepLead is running: web http://localhost:3000  ·  API http://localhost:5264" -ForegroundColor Green
