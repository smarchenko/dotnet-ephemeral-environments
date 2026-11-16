<#
.SYNOPSIS
    Stops the stage 0 local environment.
.DESCRIPTION
    Removes the containers and the network. The database volume is kept unless -RemoveData is given.
.EXAMPLE
    ./deploy/local/down.ps1
.EXAMPLE
    ./deploy/local/down.ps1 -RemoveData     # also drop the database (next up.ps1 starts from an empty schema)
#>
[CmdletBinding()]
param(
    [switch] $RemoveData
)

$ErrorActionPreference = 'Stop'
$composeFile = Join-Path $PSScriptRoot 'docker-compose.yml'

$savedContext = $env:DOCKER_CONTEXT
$env:DOCKER_CONTEXT = 'desktop-linux'
try {
    $composeArgs = @('compose', '-f', $composeFile, 'down')
    if ($RemoveData) { $composeArgs += '--volumes' }

    docker @composeArgs
    if ($LASTEXITCODE -ne 0) { throw "docker compose down failed (exit code $LASTEXITCODE)." }

    Write-Host ($(if ($RemoveData) { 'Environment removed, database volume dropped.' } else { 'Environment stopped, database volume kept.' })) -ForegroundColor Green
}
finally {
    $env:DOCKER_CONTEXT = $savedContext
}
