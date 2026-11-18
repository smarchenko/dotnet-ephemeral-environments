<#
.SYNOPSIS
    Starts the local environment: PostgreSQL → migrations → demo data → API → Web.
.DESCRIPTION
    One `docker compose up --build --wait`: postgres becomes healthy, the one-off steps `migrate` (EF migration bundle)
    and `seed` (DbTool) run from the `migrator` image and must exit 0, then api and web start and become healthy.
    Needs only Docker (no dotnet SDK on the host). If a step fails, the apps are not started and its logs are printed.
    Ports default to the EE_*_PORT environment variables, then to 5080 / 5081 / 5432.
.EXAMPLE
    ./deploy/local/up.ps1
.EXAMPLE
    ./deploy/local/up.ps1 -DbPort 5433     # 5432 is taken by another Postgres
#>
[CmdletBinding()]
param(
    [int] $WebPort,
    [int] $ApiPort,
    [int] $DbPort
)

. "$PSScriptRoot/common.ps1"
if (-not $WebPort) { $WebPort = Get-DefaultPort 'EE_WEB_PORT' 5080 }
if (-not $ApiPort) { $ApiPort = Get-DefaultPort 'EE_API_PORT' 5081 }
if (-not $DbPort) { $DbPort = Get-DefaultPort 'EE_DB_PORT' 5432 }

Use-LocalDockerEnv -WebPort $WebPort -ApiPort $ApiPort -DbPort $DbPort -Script {
    # --wait accepts one-off services that exited 0 (checked on compose v2.30); a failed step fails `up`.
    try {
        Invoke-Step 'Build and start (postgres → migrate → seed → api → web; first build takes a few minutes)' {
            Invoke-Compose up --build --detach --wait
        }
    }
    catch {
        Write-Host ''
        Write-Host 'Logs of the database steps:' -ForegroundColor Yellow
        Invoke-Compose logs --no-log-prefix migrate seed
        throw
    }

    Write-Host ''
    Write-Host 'Environment is up:' -ForegroundColor Green
    Write-Host "  Web       http://localhost:$WebPort"
    Write-Host "  Widget    http://localhost:$WebPort/Home/Widget"
    Write-Host "  Swagger   http://localhost:$ApiPort/swagger"
    Write-Host "  Health    http://localhost:$ApiPort/health/ready"
    Write-Host "  Postgres  localhost:$DbPort (ephemeral_envs / ephemeral_envs)"
    Write-Host '  Data:     ./deploy/local/db-update.ps1 [-Restore]   ./deploy/local/db-reset.ps1'
    Write-Host '  Stop:     ./deploy/local/down.ps1   (add -RemoveData to drop the database volume)'
}
