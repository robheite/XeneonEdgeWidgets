[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceUrl = 'http://127.0.0.1:48620'

try {
    $health = Invoke-RestMethod -Uri "$serviceUrl/api/v1/health" -TimeoutSec 2
    if ($health.data.name -eq 'Edge Companion') {
        Write-Host "Edge Companion is already running at $serviceUrl."
        exit 0
    }
} catch {
    # No healthy Edge Companion responded. Check whether another process owns the port.
}

$listener = Get-NetTCPConnection -LocalAddress '127.0.0.1' -LocalPort 48620 -State Listen -ErrorAction SilentlyContinue |
    Select-Object -First 1

if ($listener) {
    $owner = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
    $ownerName = if ($owner) { $owner.ProcessName } else { 'unknown process' }
    Write-Error "Port 48620 is already in use by $ownerName (PID $($listener.OwningProcess)), but it is not a healthy Edge Companion instance."
    exit 1
}

$project = Join-Path $PSScriptRoot '..\companion\src\EdgeCompanion.Host'
& dotnet run --project $project
exit $LASTEXITCODE
