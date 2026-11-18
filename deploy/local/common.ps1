# Shared helpers of the local scripts (dot-sourced: . "$PSScriptRoot/common.ps1"). Not meant to be run directly.

$ErrorActionPreference = 'Stop'

$script:RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$script:ComposeFile = Join-Path $PSScriptRoot 'docker-compose.yml'

# Port defaults: parameter > EE_*_PORT environment variable > built-in default.
function Get-DefaultPort([string] $variable, [int] $default) {
    $value = [Environment]::GetEnvironmentVariable($variable)
    if ($value) { [int] $value } else { $default }
}

# Runs a native command block and fails the script on a non-zero exit code.
function Invoke-Step([string] $title, [scriptblock] $command) {
    Write-Host "==> $title" -ForegroundColor Cyan
    & $command
    if ($LASTEXITCODE -ne 0) { throw "'$title' failed (exit code $LASTEXITCODE)." }
}

function Invoke-Compose {
    docker compose -f $script:ComposeFile @args
}

# Starts postgres if it is not running and waits until it is healthy. Never recreates a running container
# (so a different -DbPort does not move an existing database to another port).
function Start-Postgres {
    Invoke-Step 'Start postgres' { Invoke-Compose up --detach --wait --no-recreate postgres }
}

# Builds the migrator image, applies the migrations, then runs the seed (optionally with --restore).
function Invoke-MigrateAndSeed([switch] $Restore) {
    Invoke-Step 'Build migrator image' { Invoke-Compose build migrate }
    Invoke-Step 'Apply migrations (efbundle)' { Invoke-Compose run --rm --no-deps migrate }
    if ($Restore) {
        Invoke-Step 'Seed demo data (restore deleted)' {
            Invoke-Compose run --rm --no-deps seed dotnet EphemeralEnvs.DbTool.dll seed --restore
        }
    }
    else {
        Invoke-Step 'Seed demo data' { Invoke-Compose run --rm --no-deps seed }
    }
}

# Runs $script with DOCKER_CONTEXT=desktop-linux and the EE_* port variables that compose reads, from the repo root;
# restores the caller's environment and location afterwards.
function Use-LocalDockerEnv([int] $WebPort, [int] $ApiPort, [int] $DbPort, [scriptblock] $Script) {
    $vars = @{
        DOCKER_CONTEXT = 'desktop-linux'
        EE_WEB_PORT    = "$WebPort"
        EE_API_PORT    = "$ApiPort"
        EE_DB_PORT     = "$DbPort"
    }
    $saved = @{}
    foreach ($name in $vars.Keys) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $vars[$name])
    }

    Push-Location $script:RepoRoot
    try {
        & $Script
    }
    finally {
        Pop-Location
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
}
