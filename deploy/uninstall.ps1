# Removes DeepLead services, the worker task and the firewall rule. Files are kept unless -RemoveFiles is given.
#   powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 [-InstallDir C:\DeepLead] [-RemoveFiles]
param([string] $InstallDir = 'C:\DeepLead', [switch] $RemoveFiles)

$ErrorActionPreference = 'Stop'

foreach ($svc in 'DeepLeadWeb', 'DeepLeadApi', 'DeepLeadWorker') {
    if (Get-Service $svc -ErrorAction SilentlyContinue) {
        Stop-Service $svc -Force -ErrorAction SilentlyContinue
        sc.exe delete $svc | Out-Null
        Write-Host "Removed service $svc"
    }
}
if (Get-ScheduledTask -TaskName 'DeepLead Worker' -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName 'DeepLead Worker'
    Unregister-ScheduledTask -TaskName 'DeepLead Worker' -Confirm:$false
    Write-Host 'Removed worker task'
}
Get-Process DeepLead.* -ErrorAction SilentlyContinue | Stop-Process -Force
Get-NetFirewallRule -DisplayName 'DeepLead Web' -ErrorAction SilentlyContinue | Remove-NetFirewallRule

if ($RemoveFiles -and (Test-Path $InstallDir)) {
    Remove-Item $InstallDir -Recurse -Force
    Write-Host "Deleted $InstallDir"
}
Write-Host 'DeepLead uninstalled. The database is untouched.' -ForegroundColor Green
