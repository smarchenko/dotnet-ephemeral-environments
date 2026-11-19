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
        Restores the caller's DOCKER_CONTEXT, EE_* variables (Set-EnvComposeVariables) and location afterwards.
    #>
    param([Parameter(Mandatory, Position = 0)] [scriptblock] $ScriptBlock)

    $saved = @{ DOCKER_CONTEXT = $env:DOCKER_CONTEXT }
    Get-ChildItem env: | Where-Object Name -like 'EE_*' | ForEach-Object { $saved[$_.Name] = $_.Value }
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
        Get-ChildItem env: | Where-Object { $_.Name -like 'EE_*' -and -not $saved.ContainsKey($_.Name) } |
            ForEach-Object { Remove-Item "env:$($_.Name)" }
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
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

# ---------------------------------------------------------------------------------------------------------------
# Environment
# ---------------------------------------------------------------------------------------------------------------

$script:EnvImages = [ordered]@{
    api      = 'src/EphemeralEnvs.Api/Dockerfile'
    web      = 'src/EphemeralEnvs.Web/Dockerfile'
    migrator = 'src/EphemeralEnvs.DbTool/Dockerfile'
}

function Get-WorktreePath {
    <# .SYNOPSIS Git worktree of a -Branch env: <repo>/.ee/worktrees/<env>. #>
    [OutputType([string])]
    param([Parameter(Mandatory)] [string] $EnvName)
    Join-Path (Get-RepoRoot) ".ee/worktrees/$EnvName"
}

function Get-EnvComposeArgs {
    <# .SYNOPSIS docker compose arguments of an env: project ee-<env> + its compose file. #>
    param(
        [Parameter(Mandatory)] [string] $EnvName,
        [Parameter(Mandatory)] [string] $ComposeFile
    )
    @('-p', "ee-$EnvName", '-f', $ComposeFile)
}

function Set-EnvComposeVariables {
    <#
    .SYNOPSIS
        Sets the EE_* process variables deploy/env/compose.yml reads (restored by Use-DockerEnv).
        EE_DOMAIN (localhost) and EE_PROXY_PORT (8080) keep a value the caller already set.
    #>
    param(
        [Parameter(Mandatory)] [string] $EnvName,
        [Parameter(Mandatory)] [string] $Tag,
        [Parameter(Mandatory)] [string] $Password,
        [Parameter(Mandatory)] [string] $Branch,
        [Parameter(Mandatory)] [string] $Commit,
        [Parameter(Mandatory)] [ValidateSet('local', 'worktree')] [string] $Source,
        [AllowEmptyString()] [string] $KeysPath = '/keys'
    )
    $env:EE_ENV = $EnvName
    $env:EE_DB = ConvertTo-DbName -EnvName $EnvName
    $env:EE_TAG = $Tag
    if (-not $env:EE_DOMAIN) { $env:EE_DOMAIN = 'localhost' }
    if (-not $env:EE_PROXY_PORT) { $env:EE_PROXY_PORT = '8080' }
    $env:EE_DB_PASSWORD = $Password
    $env:EE_BRANCH = $Branch
    $env:EE_COMMIT = $Commit
    $env:EE_SOURCE = $Source
    $env:EE_DEPLOYED_AT = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    # An empty string removes the variable; compose then reads ${EE_DP_KEYS_PATH:-} as empty.
    $env:EE_DP_KEYS_PATH = $KeysPath
}

# Who deploys an env: the branch of its containers (label ee.branch). $null when nothing is deployed.
function Get-EnvOwner([object[]] $Containers) {
    $Containers | Where-Object { $_.Labels -and $_.Labels['ee.branch'] } |
        Select-Object -First 1 | ForEach-Object { $_.Labels['ee.branch'] }
}

# Checks out a commit into .ee/worktrees/<env> (created on first use, moved on re-deploy); returns its path.
# git's own output goes to the host, so the function returns only the path.
function Update-EnvWorktree([string] $EnvName, [string] $Sha) {
    $root = Get-RepoRoot
    $path = Get-WorktreePath -EnvName $EnvName
    git -C $root worktree prune
    if (Test-Path (Join-Path $path '.git')) {
        Invoke-Native "Check out $($Sha.Substring(0, 12)) in .ee/worktrees/$EnvName" {
            git -C $path checkout --detach --force --quiet $Sha
        } | Out-Host
    }
    else {
        Invoke-Native "Create worktree .ee/worktrees/$EnvName at $($Sha.Substring(0, 12))" {
            git -C $root worktree add --detach --force $path $Sha
        } | Out-Host
    }
    $path
}

function Test-DockerImage([string] $Name) {
    docker image inspect $Name *> $null
    $LASTEXITCODE -eq 0
}

# Waits until GET <url> through Traefik (Host header; PowerShell does not resolve *.localhost) returns 200.
function Wait-HttpOk([string] $Url, [string] $HostName, [int] $TimeoutSeconds = 60) {
    Write-Host "==> Wait for http://$HostName$(([Uri] $Url).PathAndQuery) (up to $TimeoutSeconds s)" -ForegroundColor Cyan
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $status = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $status = (Invoke-WebRequest -Uri $Url -Headers @{ Host = $HostName } -SkipHttpErrorCheck -TimeoutSec 5).StatusCode
            if ($status -eq 200) { return }
        }
        catch { $status = $_.Exception.Message }
        Start-Sleep -Seconds 2
    }
    throw "http://$HostName$(([Uri] $Url).PathAndQuery) did not answer 200 within $TimeoutSeconds s (last: $status)."
}

function Invoke-EnvDeploy {
    <#
    .SYNOPSIS
        Deploys code as env <env>; see deploy/deploy.ps1. Run inside Use-DockerEnv.
        Default: the current branch from the local checkout (uncommitted changes included). -Committed: its HEAD
        commit only (worktree, like CI). -Branch: another branch's commit (worktree).
    #>
    [CmdletBinding(DefaultParameterSetName = 'Current')]
    param(
        [Parameter(ParameterSetName = 'Current')] [switch] $Committed,
        [Parameter(Mandatory, ParameterSetName = 'Branch')] [string] $Branch,
        [string] $Env,
        [switch] $Force,
        [switch] $Rebuild,
        [ValidateRange(1, 10)] [int] $ApiReplicas = 1,
        [ValidateRange(1, 10)] [int] $WebReplicas = 1,
        [switch] $NoSharedKeys
    )
    $root = Get-RepoRoot

    # 1. Which commit and branch, under which name, and is the env free for it.
    if ($PSCmdlet.ParameterSetName -eq 'Branch') {
        $commit = Resolve-Commit -Branch $Branch
        $branchName = $commit.BranchName
        $sha = $commit.Sha
    }
    else {
        $branchName = "$(git -C $root branch --show-current)".Trim()
        if (-not $branchName) {
            if (-not $Env) { throw 'HEAD is detached; pass -Env <name> (or -Branch <name>).' }
            $branchName = '(detached)'
        }
        $sha = "$(git -C $root rev-parse HEAD)".Trim()
    }
    $envName = if ($Env) { Get-EnvName -Env $Env } else { Get-EnvName -Branch $branchName }
    $db = ConvertTo-DbName -EnvName $envName

    $owner = Get-EnvOwner @(Get-EnvContainers -EnvName $envName)
    if ($owner -and $owner -ne $branchName) {
        if (-not $Force) { throw "Env '$envName' runs branch '$owner'. Use -Force to take it over." }
        Write-Host "Env '$envName' runs branch '$owner'; taking it over (-Force)." -ForegroundColor Yellow
    }

    # 2. Code: the local checkout as it is, or a worktree at the commit.
    $dirty = $false
    if ($PSCmdlet.ParameterSetName -eq 'Current' -and -not $Committed) {
        $source = 'local'
        $codeRoot = $root
        $dirty = [bool] (git -C $root status --porcelain)
    }
    else {
        $source = 'worktree'
        $codeRoot = Update-EnvWorktree -EnvName $envName -Sha $sha
    }
    $tag = if ($dirty) { "wt-$envName" } else { $sha.Substring(0, 12) }
    $commitLabel = if ($dirty) { "$sha+dirty" } else { $sha }
    $composeFile = Join-Path $codeRoot 'deploy/env/compose.yml'
    if (-not (Test-Path $composeFile)) {
        throw "'$branchName' at $($sha.Substring(0, 12)) has no deploy/env/compose.yml (older than stage 2), so it cannot be deployed as an env."
    }

    # 3. Images: built once per commit; uncommitted changes are always rebuilt (the layer cache keeps it fast).
    $missing = @($script:EnvImages.Keys | Where-Object { -not (Test-DockerImage "ee/${_}:$tag") })
    if (-not $dirty -and -not $Rebuild -and $missing.Count -eq 0) {
        Write-Host "==> Images ee/{api,web,migrator}:$tag exist, skipping build" -ForegroundColor Cyan
    }
    else {
        foreach ($image in $script:EnvImages.Keys) {
            $dockerfile = Join-Path $codeRoot $script:EnvImages[$image]
            Invoke-Native "Build ee/${image}:$tag" { docker build -f $dockerfile -t "ee/${image}:$tag" $codeRoot }
        }
    }

    # 4. Platform (Traefik + shared Postgres), started only if it is not running.
    Start-Platform

    # 5. Role + database. A running api keeps its password until migrate and seed succeed (spec 4.5).
    $newPassword = New-DbPassword
    $current = Get-RunningApiPassword -EnvName $envName
    $roleExists = "$(Invoke-PlatformSql -Sql "SELECT 1 FROM pg_roles WHERE rolname = '$db';")".Trim() -eq '1'
    if (-not $roleExists) { $current = $null }

    Write-Host "==> Database $db (role, database, connect only for its own role)" -ForegroundColor Cyan
    $roleSql = if (-not $roleExists) { "CREATE ROLE `"$db`" LOGIN PASSWORD '$newPassword';" }
    elseif (-not $current) { "ALTER ROLE `"$db`" PASSWORD '$newPassword';" }
    else { '' }
    Invoke-PlatformSql -Sql @"
$roleSql
SELECT 'CREATE DATABASE "$db" OWNER "$db"' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '$db')\gexec
REVOKE CONNECT ON DATABASE "$db" FROM PUBLIC;
"@ | Out-Null

    # 6. Migrations + demo data, before the apps change: a failure leaves the running version serving.
    $composeArgs = Get-EnvComposeArgs -EnvName $envName -ComposeFile $composeFile
    $keysPath = if ($NoSharedKeys) { '' } else { '/keys' }
    $variables = @{
        EnvName = $envName; Tag = $tag; Branch = $branchName; Commit = $commitLabel; Source = $source; KeysPath = $keysPath
    }
    Set-EnvComposeVariables @variables -Password $(if ($current) { $current } else { $newPassword })
    Invoke-Native 'Apply migrations (efbundle)' { docker compose @composeArgs run --rm migrate }
    Invoke-Native 'Seed demo data' { docker compose @composeArgs run --rm seed }

    # 7. Rotate the password only now.
    if ($current) {
        Write-Host '==> Rotate the database password' -ForegroundColor Cyan
        Invoke-PlatformSql -Sql "ALTER ROLE `"$db`" PASSWORD '$newPassword';" | Out-Null
    }

    # 8. Apps with the new password (containers are recreated: new password, new deployed-at label).
    Set-EnvComposeVariables @variables -Password $newPassword
    Invoke-Native "Start api x$ApiReplicas + web x$WebReplicas" {
        docker compose @composeArgs up --detach --wait --remove-orphans --scale "api=$ApiReplicas" --scale "web=$WebReplicas" api web
    }

    # 9. Through Traefik, as a browser would.
    $proxy = "http://localhost:$env:EE_PROXY_PORT"
    $apiHost = "$envName-api.$env:EE_DOMAIN"
    $webHost = "$envName.$env:EE_DOMAIN"
    Wait-HttpOk -Url "$proxy/health/ready" -HostName $apiHost
    Wait-HttpOk -Url "$proxy/" -HostName $webHost

    # 10. Where it is.
    $dashboard = if ($env:EE_DASHBOARD_PORT) { $env:EE_DASHBOARD_PORT } else { '8090' }
    Write-Host ''
    Write-Host "Env '$envName' is up:" -ForegroundColor Green
    Write-Host "  Web        http://${webHost}:$env:EE_PROXY_PORT"
    Write-Host "  API        http://${apiHost}:$env:EE_PROXY_PORT   (Swagger: /swagger)"
    Write-Host "  Branch     $branchName   (code: $(if ($source -eq 'local') { 'local checkout' } else { ".ee/worktrees/$envName" }))"
    Write-Host "  Commit     $commitLabel   (images ee/*:$tag)"
    Write-Host "  Replicas   api x$ApiReplicas, web x$WebReplicas$(if ($NoSharedKeys) { ', Data Protection keys NOT shared' })"
    Write-Host "  Database   $db   (./deploy/db-connect.ps1 -Env $envName)"
    Write-Host "  Dashboard  http://localhost:$dashboard/dashboard/"
    if ($dirty) {
        Write-Host ''
        Write-Host 'Includes uncommitted changes. To deploy only what is committed (as CI will): -Committed' -ForegroundColor Yellow
    }
}

Export-ModuleMember -Function Get-EnvName, ConvertTo-DbName, New-DbPassword, Get-RepoRoot, Resolve-Commit,
Invoke-Native, Use-DockerEnv, Invoke-PlatformCompose, Start-Platform, Invoke-PlatformSql, Get-EnvContainers,
Get-RunningApiPassword, Get-WorktreePath, Get-EnvComposeArgs, Set-EnvComposeVariables, Invoke-EnvDeploy
