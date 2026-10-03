# Builds everything needed on the server into <repo>\artifacts (self-contained: the server needs no .NET runtime).
# Run on the build machine from the repo root:  powershell -ExecutionPolicy Bypass -File .\deploy\publish.ps1
# Requires: .NET 10 SDK, Node.js 20.9+.

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'artifacts'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

$projects = @{
    'api'      = 'backend\src\DeepLead.Api\DeepLead.Api.csproj'
    'worker'   = 'backend\src\DeepLead.Worker\DeepLead.Worker.csproj'
    'migrator' = 'backend\src\DeepLead.Migrator\DeepLead.Migrator.csproj'
    'webhost'  = 'backend\src\DeepLead.WebHost\DeepLead.WebHost.csproj'
}
foreach ($name in $projects.Keys) {
    Write-Host "Publishing $name..." -ForegroundColor Cyan
    dotnet publish (Join-Path $root $projects[$name]) -c Release -r win-x64 --self-contained true -o (Join-Path $out $name) -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $name" }
}

Write-Host "Building web app..." -ForegroundColor Cyan
Push-Location (Join-Path $root 'frontend')
try {
    if (-not (Test-Path node_modules)) { npm ci; if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' } }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'next build failed' }

    # Standalone server + the static assets it does not copy by itself.
    $web = Join-Path $out 'web'
    Copy-Item '.next\standalone' $web -Recurse
    Copy-Item '.next\static' (Join-Path $web '.next\static') -Recurse
    if (Test-Path 'public') { Copy-Item 'public' (Join-Path $web 'public') -Recurse }
}
finally { Pop-Location }

Copy-Item (Join-Path $PSScriptRoot 'install.ps1'), (Join-Path $PSScriptRoot 'uninstall.ps1'), (Join-Path $PSScriptRoot 'secrets.example.json') $out
Write-Host "Done -> $out  (copy this folder to the server and run install.ps1 as Administrator)" -ForegroundColor Green
