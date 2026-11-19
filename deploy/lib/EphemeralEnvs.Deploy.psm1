#Requires -Version 7.2

# Shared helpers of the multi-env scripts in deploy/ (deploy, destroy, list, platform, db-*).
# Import: Import-Module "$PSScriptRoot/lib/EphemeralEnvs.Deploy.psm1" -Force
# Keep this file ASCII-only: Windows PowerShell reads BOM-less files as cp1252.

$ErrorActionPreference = 'Stop'

$script:MaxEnvNameLength = 40
$script:ShortHeadLength = 33
$script:ReservedEnvNames = @('platform')
$script:PlatformProject = 'ee-platform'

# ---------------------------------------------------------------------------------------------------------------
# Names (pure)
# ---------------------------------------------------------------------------------------------------------------

# Lowercase; every run outside [a-z0-9] -> '-'; trim '-'; longer than 40 -> 33-char head (trailing '-' trimmed)
# + '-' + first 6 hex of SHA-1 of the full sanitized name.
function ConvertTo-NormalEnvName([string] $Value) {
    $sanitized = ($Value.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')
    if ($sanitized.Length -le $script:MaxEnvNameLength) { return $sanitized }

    $hash = [System.Security.Cryptography.SHA1]::HashData([System.Text.Encoding]::UTF8.GetBytes($sanitized))
    $suffix = [Convert]::ToHexString($hash).Substring(0, 6).ToLowerInvariant()
    $head = $sanitized.Substring(0, $script:ShortHeadLength).TrimEnd('-')
    "$head-$suffix"
}

function Get-EnvName {
    <#
    .SYNOPSIS
        The environment name: normalized from -Branch, or an explicit -Env that must already be in normal form.
    #>
    [CmdletBinding(DefaultParameterSetName = 'Branch')]
    [OutputType([string])]
    param(
        [Parameter(Mandatory, ParameterSetName = 'Branch')] [string] $Branch,
        [Parameter(Mandatory, ParameterSetName = 'Env')] [string] $Env
    )

    $given = if ($PSCmdlet.ParameterSetName -eq 'Env') { $Env } else { $Branch }
    $name = ConvertTo-NormalEnvName $given

    if (-not $name) { throw "Env name from '$given' is empty; pass -Env <name>." }
    if ($name -in $script:ReservedEnvNames) { throw "Env name '$name' is reserved; pass another -Env <name>." }
    if ($PSCmdlet.ParameterSetName -eq 'Env' -and $name -cne $Env) {
        throw "Env name '$Env' is not valid; use '$name'."
    }
    $name
}

function ConvertTo-DbName {
    <# .SYNOPSIS Database and role name of an env: ee_<env> with '-' -> '_'. #>
    [OutputType([string])]
    param([Parameter(Mandatory)] [string] $EnvName)
    'ee_' + ($EnvName -replace '-', '_')
}

function New-DbPassword {
    <# .SYNOPSIS A random password: 32 lowercase hex chars (128 bits). #>
    [OutputType([string])]
    param()
    [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(16)).ToLowerInvariant()
}

# ---------------------------------------------------------------------------------------------------------------
# Repo, git
# ---------------------------------------------------------------------------------------------------------------

function Get-RepoRoot {
    [OutputType([string])]
    param()
    (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

# Full SHA of a ref, or $null if it does not exist.
function Get-RefCommit([string] $Ref) {
    $sha = git -C (Get-RepoRoot) rev-parse --verify --quiet "$Ref^{commit}" 2>$null
    if ($LASTEXITCODE -eq 0 -and $sha) { $sha.Trim() } else { $null }
}

function Resolve-Commit {
    <#
    .SYNOPSIS
        Resolves a branch to a commit: the local branch, else <remote>/<name> over all remotes (no fetch).
        -Branch <remote>/<name> picks the remote explicitly. BranchName is the name without the remote.
    #>
    param([Parameter(Mandatory)] [string] $Branch)

    $result = {
        param([string] $Sha, [string] $Name)
        [pscustomobject]@{ Sha = $Sha; Sha12 = $Sha.Substring(0, 12); BranchName = $Name }
    }

    $local = Get-RefCommit "refs/heads/$Branch"
    if ($local) { return & $result $local $Branch }

    $remotes = @(git -C (Get-RepoRoot) remote)

    # Explicit <remote>/<name>.
    foreach ($remote in $remotes) {
        if ($Branch.StartsWith("$remote/", [StringComparison]::Ordinal)) {
            $sha = Get-RefCommit "refs/remotes/$Branch"
            if ($sha) { return & $result $sha $Branch.Substring($remote.Length + 1) }
        }
    }

    # <name> on any remote.
    $found = @(foreach ($remote in $remotes) {
            $sha = Get-RefCommit "refs/remotes/$remote/$Branch"
            if ($sha) { [pscustomobject]@{ Remote = $remote; Sha = $sha } }
        })
    $commits = @($found.Sha | Sort-Object -Unique)

    if ($commits.Count -eq 0) {
        throw "Branch '$Branch' not found locally or on any remote ($($remotes -join ', ')). Run 'git fetch' if it was pushed recently."
    }
    if ($commits.Count -gt 1) {
        $refs = ($found | ForEach-Object { "$($_.Remote)/$Branch" }) -join ', '
        throw "Branch '$Branch' points to different commits on several remotes ($refs); pass -Branch <remote>/$Branch."
    }
    & $result $commits[0] $Branch
}

# ---------------------------------------------------------------------------------------------------------------
# Docker
# ---------------------------------------------------------------------------------------------------------------

function Invoke-Native {
    <# .SYNOPSIS Runs a native command block and throws on a non-zero exit code. #>
    param(
        [Parameter(Mandatory)] [string] $Title,
        [Parameter(Mandatory)] [scriptblock] $ScriptBlock
    )
    Write-Host "==> $Title" -ForegroundColor Cyan
    $global:LASTEXITCODE = 0
    & $ScriptBlock
    if ($LASTEXITCODE -ne 0) { throw "'$Title' failed (exit code $LASTEXITCODE)." }
}

function Use-DockerEnv {
    <#
    .SYNOPSIS
        Runs a block from the repo root against a Linux Docker engine: DOCKER_CONTEXT=desktop-linux unless
        DOCKER_HOST is set (a remote engine). Fails early if Docker is not running or is not a Linux engine.
        Restores the caller's DOCKER_CONTEXT and location afterwards.
    #>
    param([Parameter(Mandatory, Position = 0)] [scriptblock] $ScriptBlock)

    $savedContext = $env:DOCKER_CONTEXT
    if (-not $env:DOCKER_HOST) { $env:DOCKER_CONTEXT = 'desktop-linux' }
    Push-Location (Get-RepoRoot)
    try {
        $osType = docker info --format '{{.OSType}}' 2>$null
        if ($LASTEXITCODE -ne 0) {
            throw 'Docker is not reachable. Start Docker Desktop (or check DOCKER_HOST) and try again.'
        }
        if ("$osType".Trim() -ne 'linux') {
            throw "Docker engine is '$osType', expected 'linux'. Switch Docker Desktop to Linux containers."
        }
        & $ScriptBlock
    }
    finally {
        Pop-Location
        $env:DOCKER_CONTEXT = $savedContext
    }
}

function Invoke-PlatformCompose {
    <# .SYNOPSIS docker compose of the platform (project ee-platform, deploy/platform/compose.yml). #>
    docker compose -p $script:PlatformProject -f (Join-Path (Get-RepoRoot) 'deploy/platform/compose.yml') @args
}

function Start-Platform {
    <#
    .SYNOPSIS
        Starts Traefik + shared Postgres if they are not running and waits until healthy.
        Never recreates a running container (--no-recreate), so env databases and routes stay up.
    #>
    param()
    Invoke-Native 'Start platform (Traefik + Postgres)' { Invoke-PlatformCompose up --detach --wait --no-recreate }
}

function Get-PlatformPostgresContainer {
    [OutputType([string])]
    param()
    $id = docker ps -q --filter "label=com.docker.compose.project=$script:PlatformProject" `
        --filter 'label=com.docker.compose.service=postgres'
    if (-not $id) { throw 'Platform Postgres is not running. Start it: ./deploy/platform.ps1 up' }
    @($id)[0]
}

function Invoke-PlatformSql {
    <#
    .SYNOPSIS
        Runs SQL (stdin) with psql as superuser in the platform Postgres container. Stops on the first error.
        Output is unaligned and tuples-only, so a query result can be compared as text.
    #>
    param(
        [Parameter(Mandatory)] [string] $Sql,
        [string] $Database = 'postgres'
    )
    $container = Get-PlatformPostgresContainer
    $output = $Sql | docker exec -i $container psql -U postgres -X -q -A -t -v ON_ERROR_STOP=1 -d $Database 2>&1
    if ($LASTEXITCODE -ne 0) { throw "SQL on '$Database' failed (exit code $LASTEXITCODE): $($output -join "`n")" }
    $output
}

function Get-EnvContainers {
    <#
    .SYNOPSIS
        Containers of compose project ee-<env> (also stopped ones): Name, Service, State, Health, Image,
        Labels (incl. ee.env, ee.branch, ee.commit, ee.deployed-at) and Env (hashtable of environment variables).
    #>
    param([Parameter(Mandatory)] [string] $EnvName)

    $ids = @(docker ps -a -q --filter "label=com.docker.compose.project=ee-$EnvName")
    if ($ids.Count -eq 0) { return @() }

    $inspected = docker inspect @ids | ConvertFrom-Json -AsHashtable
    foreach ($c in $inspected) {
        $vars = @{}
        foreach ($pair in $c.Config.Env) {
            $name, $value = $pair -split '=', 2
            $vars[$name] = $value
        }
        [pscustomobject]@{
            Name    = $c.Name.TrimStart('/')
            Service = $c.Config.Labels['com.docker.compose.service']
            State   = $c.State.Status
            Health  = if ($c.State.Health) { $c.State.Health.Status } else { $null }
            Image   = $c.Config.Image
            Labels  = $c.Config.Labels
            Env     = $vars
        }
    }
}

function Get-RunningApiPassword {
    <#
    .SYNOPSIS
        The database password the env's running api uses (from its ConnectionStrings__Default), or $null.
        Lets a re-deploy migrate with the current password before rotating it.
    #>
    param([Parameter(Mandatory)] [string] $EnvName)

    $api = Get-EnvContainers -EnvName $EnvName |
        Where-Object { $_.Service -eq 'api' -and $_.State -eq 'running' } |
        Select-Object -First 1
    if (-not $api) { return $null }

    $connectionString = $api.Env['ConnectionStrings__Default']
    if ($connectionString -match '(?i)(?:^|;)\s*Password\s*=\s*([^;]*)') { $Matches[1] } else { $null }
}

Export-ModuleMember -Function Get-EnvName, ConvertTo-DbName, New-DbPassword, Get-RepoRoot, Resolve-Commit,
Invoke-Native, Use-DockerEnv, Invoke-PlatformCompose, Start-Platform, Invoke-PlatformSql, Get-EnvContainers,
Get-RunningApiPassword
