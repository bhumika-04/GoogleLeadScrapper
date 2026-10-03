# Installs or updates DeepLead on a Windows Server. Run from the published artifacts folder, as Administrator:
#   powershell -ExecutionPolicy Bypass -File .\install.ps1 [-WorkerMode Task|Service]
#
# Needs: secrets.json next to this script (copy secrets.example.json), Node.js 20.9+ installed (for the web app).
#
# What runs where:
#   DeepLead API   - Windows service, http://localhost:<ApiPort> (not exposed; the web app proxies /api to it)
#   DeepLead Web   - Windows service, http://<server>:<WebPort>  (open this in the browser)
#   DeepLead Worker - by default a logon task in the signed-in user's desktop session, because "Connect account"
#                    and LinkedIn need a visible browser window. -WorkerMode Service runs it as a service instead
#                    (headless only: account connecting will not work, LinkedIn runs headless).

param([ValidateSet('Task', 'Service')] [string] $WorkerMode = 'Task')

$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script as Administrator.'
}
$secretsPath = Join-Path $src 'secrets.json'
if (-not (Test-Path $secretsPath)) { throw "Missing $secretsPath - copy secrets.example.json and fill it in." }
$s = Get-Content $secretsPath -Raw | ConvertFrom-Json
foreach ($k in 'InstallDir', 'ConnectionString', 'JwtKey', 'EncryptionKey') {
    if ([string]::IsNullOrWhiteSpace($s.$k) -or $s.$k -like '<*') { throw "secrets.json: '$k' is not set." }
}
if ($s.JwtKey.Length -lt 32) { throw 'secrets.json: JwtKey must be at least 32 characters.' }

$node = (Get-Command node -ErrorAction SilentlyContinue).Source
if (-not $node) { throw 'Node.js is not installed (needed for the web app). Install Node.js 20.9+ and re-run.' }

$dir = $s.InstallDir
$webPort = if ($s.WebPort) { [int]$s.WebPort } else { 3000 }
$apiPort = if ($s.ApiPort) { [int]$s.ApiPort } else { 5264 }

function Write-Json($path, $obj) {
    [IO.File]::WriteAllText($path, ($obj | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding $false))
    # Secrets inside: readable by Administrators, SYSTEM and the installing user only.
    icacls $path /inheritance:r /grant:r "Administrators:F" "SYSTEM:F" "$($env:USERDOMAIN)\$($env:USERNAME):F" | Out-Null
}

# ---- Stop what is running (update case) ----
Write-Host 'Stopping existing DeepLead processes...' -ForegroundColor Cyan
foreach ($svc in 'DeepLeadWeb', 'DeepLeadApi', 'DeepLeadWorker') {
    if (Get-Service $svc -ErrorAction SilentlyContinue) { Stop-Service $svc -Force -ErrorAction SilentlyContinue }
}
if (Get-ScheduledTask -TaskName 'DeepLead Worker' -ErrorAction SilentlyContinue) { Stop-ScheduledTask -TaskName 'DeepLead Worker' }
Get-Process DeepLead.* -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2

# ---- Copy files (worker data/ and logs/ are kept) ----
Write-Host "Copying files to $dir..." -ForegroundColor Cyan
foreach ($part in 'api', 'worker', 'migrator', 'webhost', 'web') {
    $target = Join-Path $dir $part
    New-Item -ItemType Directory -Force $target | Out-Null
    robocopy (Join-Path $src $part) $target /E /NFL /NDL /NJH /NJS /NP /XD data logs | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed for $part (robocopy $LASTEXITCODE)" }
}

# ---- Settings ----
Write-Host 'Writing settings...' -ForegroundColor Cyan
Write-Json (Join-Path $dir 'api\appsettings.Production.json') @{
    Urls = "http://localhost:$apiPort"
    ConnectionStrings = @{ DefaultConnection = $s.ConnectionString }
    Jwt = @{ Key = $s.JwtKey }
    Seed = @{ TenantName = $s.SeedTenantName; AdminEmail = $s.SeedAdminEmail; AdminPassword = $s.SeedAdminPassword }
    Cors = @{ Origins = @("http://localhost:$webPort") }
}
Write-Json (Join-Path $dir 'worker\appsettings.Production.json') @{
    ConnectionStrings = @{ DefaultConnection = $s.ConnectionString }
    Secrets = @{ EncryptionKey = $s.EncryptionKey }
    Playwright = @{ BrowsersPath = (Join-Path $dir 'browsers') }
    Scraping = @{ LinkedInHeadless = ($WorkerMode -eq 'Service') }
}
Write-Json (Join-Path $dir 'migrator\appsettings.json') @{ ConnectionStrings = @{ DefaultConnection = $s.ConnectionString } }
Write-Json (Join-Path $dir 'webhost\appsettings.Production.json') @{
    Web = @{ Directory = (Join-Path $dir 'web'); Port = "$webPort"; NodePath = $node; Hostname = '0.0.0.0' }
}

# ---- Database + browsers ----
Write-Host 'Updating database...' -ForegroundColor Cyan
& (Join-Path $dir 'migrator\DeepLead.Migrator.exe')
if ($LASTEXITCODE -ne 0) { throw 'Database migration failed.' }

Write-Host 'Installing Chromium for the worker...' -ForegroundColor Cyan
$env:PLAYWRIGHT_BROWSERS_PATH = Join-Path $dir 'browsers'
& (Join-Path $dir 'worker\DeepLead.Worker.exe') install-browsers
if ($LASTEXITCODE -ne 0) { throw 'Browser install failed.' }

# ---- Services ----
function Install-DeepLeadService($name, $display, $exe) {
    if (-not (Get-Service $name -ErrorAction SilentlyContinue)) {
        New-Service -Name $name -DisplayName $display -BinaryPathName "`"$exe`"" -StartupType Automatic | Out-Null
    }
    sc.exe failure $name reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null   # auto-restart on crash
    Start-Service $name
}

Write-Host 'Registering services...' -ForegroundColor Cyan
Install-DeepLeadService 'DeepLeadApi' 'DeepLead API' (Join-Path $dir 'api\DeepLead.Api.exe')
Install-DeepLeadService 'DeepLeadWeb' 'DeepLead Web' (Join-Path $dir 'webhost\DeepLead.WebHost.exe')

$workerExe = Join-Path $dir 'worker\DeepLead.Worker.exe'
if ($WorkerMode -eq 'Service') {
    if (Get-ScheduledTask -TaskName 'DeepLead Worker' -ErrorAction SilentlyContinue) { Unregister-ScheduledTask -TaskName 'DeepLead Worker' -Confirm:$false }
    Install-DeepLeadService 'DeepLeadWorker' 'DeepLead Worker' $workerExe
}
else {
    if (Get-Service 'DeepLeadWorker' -ErrorAction SilentlyContinue) { sc.exe delete DeepLeadWorker | Out-Null }
    $action = New-ScheduledTaskAction -Execute $workerExe -WorkingDirectory (Join-Path $dir 'worker')
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User "$($env:USERDOMAIN)\$($env:USERNAME)"
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
        -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
    $principal = New-ScheduledTaskPrincipal -UserId "$($env:USERDOMAIN)\$($env:USERNAME)" -LogonType Interactive -RunLevel Highest
    Register-ScheduledTask -TaskName 'DeepLead Worker' -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
    Start-ScheduledTask -TaskName 'DeepLead Worker'
}

# ---- Firewall ----
if (-not (Get-NetFirewallRule -DisplayName 'DeepLead Web' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'DeepLead Web' -Direction Inbound -Protocol TCP -LocalPort $webPort -Action Allow | Out-Null
}

Write-Host ''
Write-Host "DeepLead is installed. Open http://$($env:COMPUTERNAME):$webPort" -ForegroundColor Green
if ($WorkerMode -eq 'Task') {
    Write-Host "The worker runs while $($env:USERNAME) is signed in (it starts automatically at sign-in). Keep the session open (disconnect RDP, don't sign out)." -ForegroundColor Yellow
}
