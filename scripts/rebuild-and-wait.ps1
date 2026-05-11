#Requires -Version 5.1
<#
.SYNOPSIS
    Rebuild one or more docker compose services and wait until each .NET service
    is healthy before returning. Also reloads nginx so it re-resolves the API
    container IP after a rebuild.

.PARAMETER Services
    Docker Compose service names to rebuild. Defaults to all four app services.

.PARAMETER TimeoutSeconds
    Seconds to wait for each .NET service to reach /health/ready. Default: 120.

.EXAMPLE
    pwsh scripts/rebuild-and-wait.ps1
    pwsh scripts/rebuild-and-wait.ps1 -Services api
    pwsh scripts/rebuild-and-wait.ps1 -Services api,stream-worker
#>
[CmdletBinding()]
param(
    [string[]]$Services = @('api', 'stream-worker', 'jobs-worker', 'frontend'),
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'

Write-Host "Rebuilding: $($Services -join ', ')"
docker compose build @Services
docker compose up -d @Services

# Poll /health/ready for each .NET service
$dotnetServices = $Services | Where-Object { $_ -in 'api', 'stream-worker', 'jobs-worker' }
foreach ($svc in $dotnetServices) {
    Write-Host "Waiting for $svc to become healthy..."
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $ready = $false
    do {
        try {
            $exit = docker compose exec -T $svc curl -fsS http://localhost:8080/health/ready 2>&1
            if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        }
        catch { }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)

    if (-not $ready) {
        throw "Timed out waiting for $svc to reach /health/ready after $TimeoutSeconds s"
    }
    Write-Host "$svc is healthy"
}

# Reload nginx so it re-resolves the api container IP (nginx caches DNS at startup)
if ('frontend' -in $Services -or 'api' -in $Services) {
    docker compose exec -T frontend nginx -s reload
    Write-Host "nginx reloaded"
}

Write-Host "All services ready."
