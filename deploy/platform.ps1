#Requires -Version 7.2
<#
.SYNOPSIS
    Starts or stops the platform shared by all ephemeral environments: Traefik + one PostgreSQL.
.DESCRIPTION
    up   - starts Traefik and Postgres if they are not running and waits until healthy (never recreates a running one).
    down - stops them; envs stay deployed but are unreachable until the next `up`.
           -RemoveData also drops the Postgres volume = the databases of ALL envs.
    Ports: EE_PROXY_PORT (8080), EE_DASHBOARD_PORT (8090), EE_PLATFORM_DB_PORT (5442).
    Postgres superuser password: EE_PG_ADMIN_PASSWORD (local default: postgres).
.EXAMPLE
    ./deploy/platform.ps1 up
.EXAMPLE
    ./deploy/platform.ps1 down -RemoveData
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('up', 'down')]
    [string] $Action,

    [switch] $RemoveData
)

$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/lib/EphemeralEnvs.Deploy.psm1" -Force

Use-DockerEnv {
    if ($Action -eq 'up') {
        Start-Platform

        $proxy = if ($env:EE_PROXY_PORT) { $env:EE_PROXY_PORT } else { '8080' }
        $dashboard = if ($env:EE_DASHBOARD_PORT) { $env:EE_DASHBOARD_PORT } else { '8090' }
        $db = if ($env:EE_PLATFORM_DB_PORT) { $env:EE_PLATFORM_DB_PORT } else { '5442' }
        Write-Host ''
        Write-Host 'Platform is up:' -ForegroundColor Green
        Write-Host "  Envs       http://<env>.localhost:$proxy   http://<env>-api.localhost:$proxy"
        Write-Host "  Dashboard  http://localhost:$dashboard/dashboard/"
        Write-Host "  Postgres   localhost:$db (superuser postgres; per env: ./deploy/db-connect.ps1 -Env <env>)"
        return
    }

    $downArgs = @('down')
    if ($RemoveData) { $downArgs += '--volumes' }
    Invoke-Native 'Stop platform' { Invoke-PlatformCompose @downArgs }
    Write-Host ($(if ($RemoveData) { 'Platform removed, ALL env databases dropped.' } else { 'Platform stopped, databases kept.' })) -ForegroundColor Green
}
