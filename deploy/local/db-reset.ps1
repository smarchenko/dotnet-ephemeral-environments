<#
.SYNOPSIS
    Puts the local database back to the just-deployed state: drop, create, all migrations, all seed sets.
.DESCRIPTION
    Everything in the database is lost (QA data, edits, journal). Use it between test rounds of a feature.
    Open connections are closed (DROP DATABASE ... WITH (FORCE)); the API is restarted so it does not keep
    connections to the dropped database. Starts postgres if needed.
.EXAMPLE
    ./deploy/local/db-reset.ps1
#>
[CmdletBinding()]
param(
    [int] $DbPort
)

. "$PSScriptRoot/common.ps1"
if (-not $DbPort) { $DbPort = Get-DefaultPort 'EE_DB_PORT' 5432 }

Use-LocalDockerEnv -WebPort (Get-DefaultPort 'EE_WEB_PORT' 5080) -ApiPort (Get-DefaultPort 'EE_API_PORT' 5081) -DbPort $DbPort -Script {
    Start-Postgres
    Invoke-Step 'Drop and create database ephemeral_envs' {
        Invoke-Compose exec -T postgres psql -U ephemeral_envs -d postgres -v ON_ERROR_STOP=1 `
            -c 'DROP DATABASE IF EXISTS ephemeral_envs WITH (FORCE);' `
            -c 'CREATE DATABASE ephemeral_envs OWNER ephemeral_envs;'
    }
    Invoke-MigrateAndSeed

    $apiRunning = Invoke-Compose ps --status running --services | Where-Object { $_ -eq 'api' }
    if ($apiRunning) {
        Invoke-Step 'Restart api (drop pooled connections to the old database)' { Invoke-Compose restart api }
    }

    Write-Host ''
    Write-Host 'Database reset: schema from all migrations, demo data from all seed sets.' -ForegroundColor Green
}
