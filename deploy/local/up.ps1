<#
.SYNOPSIS
    Starts the stage 0 local environment (Web + API + PostgreSQL) and applies the schema.
.DESCRIPTION
    1. docker compose up --build, waits until every container is running and postgres is healthy;
    2. dotnet ef database update against the exposed Postgres port (the apps never migrate on startup);
    3. prints the URLs.
    Ports default to the EE_*_PORT environment variables, then to 5080 / 5081 / 5432.
.EXAMPLE
    ./deploy/local/up.ps1
.EXAMPLE
    ./deploy/local/up.ps1 -DbPort 5433     # 5432 is taken by another Postgres
#>
[CmdletBinding()]
param(
    [int] $WebPort = $(if ($env:EE_WEB_PORT) { [int] $env:EE_WEB_PORT } else { 5080 }),
    [int] $ApiPort = $(if ($env:EE_API_PORT) { [int] $env:EE_API_PORT } else { 5081 }),
    [int] $DbPort = $(if ($env:EE_DB_PORT) { [int] $env:EE_DB_PORT } else { 5432 })
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$composeFile = Join-Path $PSScriptRoot 'docker-compose.yml'

function Invoke-Step([string] $title, [scriptblock] $command) {
    Write-Host "==> $title" -ForegroundColor Cyan
    & $command
    if ($LASTEXITCODE -ne 0) { throw "'$title' failed (exit code $LASTEXITCODE)." }
}

# Compose and dotnet-ef read these; restore the caller's values afterwards.
$saved = @{}
$vars = @{
    DOCKER_CONTEXT = 'desktop-linux'
    EE_WEB_PORT    = "$WebPort"
    EE_API_PORT    = "$ApiPort"
    EE_DB_PORT     = "$DbPort"
    EE_DB          = "Host=localhost;Port=$DbPort;Database=ephemeral_envs;Username=ephemeral_envs;Password=ephemeral_envs"
}
foreach ($name in $vars.Keys) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $vars[$name])
}

Push-Location $repoRoot
try {
    Invoke-Step 'Start containers (first build takes a few minutes)' {
        docker compose -f $composeFile up --build --detach --wait
    }
    Invoke-Step 'Restore local .NET tools' { dotnet tool restore }
    Invoke-Step "Apply migrations (localhost:$DbPort)" {
        dotnet ef database update --project src/EphemeralEnvs.Data
    }

    Write-Host ''
    Write-Host 'Environment is up:' -ForegroundColor Green
    Write-Host "  Web       http://localhost:$WebPort"
    Write-Host "  Widget    http://localhost:$WebPort/Home/Widget"
    Write-Host "  Swagger   http://localhost:$ApiPort/swagger"
    Write-Host "  Health    http://localhost:$ApiPort/health/ready"
    Write-Host "  Postgres  localhost:$DbPort (ephemeral_envs / ephemeral_envs)"
    Write-Host '  Stop:     ./deploy/local/down.ps1   (add -RemoveData to drop the database volume)'
}
finally {
    Pop-Location
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}
