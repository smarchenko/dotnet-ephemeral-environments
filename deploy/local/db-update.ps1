<#
.SYNOPSIS
    Applies new migrations and new seed sets to the local database; -Restore also brings deleted demo data back.
.DESCRIPTION
    Builds the `migrator` image from the current code, runs `efbundle`, then `seed` (or `seed --restore`).
    Data changed by hand is never overwritten: seed only inserts rows whose key is missing.
    Starts postgres if needed (an already running postgres is used as is). The apps keep running.
.EXAMPLE
    ./deploy/local/db-update.ps1             # after pulling new migrations / seed sets
.EXAMPLE
    ./deploy/local/db-update.ps1 -Restore    # deleted demo products come back, edited ones stay edited
#>
[CmdletBinding()]
param(
    [switch] $Restore,
    [int] $DbPort
)

. "$PSScriptRoot/common.ps1"
if (-not $DbPort) { $DbPort = Get-DefaultPort 'EE_DB_PORT' 5432 }

Use-LocalDockerEnv -WebPort (Get-DefaultPort 'EE_WEB_PORT' 5080) -ApiPort (Get-DefaultPort 'EE_API_PORT' 5081) -DbPort $DbPort -Script {
    Start-Postgres
    Invoke-MigrateAndSeed -Restore:$Restore
    Write-Host ''
    Write-Host 'Database is up to date.' -ForegroundColor Green
}
