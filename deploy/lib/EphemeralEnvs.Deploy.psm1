#Requires -Version 7.2

# Shared helpers of the multi-env scripts in deploy/ (deploy, destroy, list, platform, db-*).
# Import: Import-Module "$PSScriptRoot/lib/EphemeralEnvs.Deploy.psm1" -Force
# Keep this file ASCII-only: Windows PowerShell reads BOM-less files as cp1252.

$ErrorActionPreference = 'Stop'

$script:MaxEnvNameLength = 30
$script:ShortHashLength = 4
# Branch type prefixes dropped from env names: they are shared by most branches and only make URLs longer.
$script:BranchTypePrefixes = @('feature', 'bugfix', 'hotfix', 'fix', 'story', 'chore')
$script:ReservedEnvNames = @('platform')
$script:PlatformProject = 'ee-platform'

# ---------------------------------------------------------------------------------------------------------------
# Names (pure)
# ---------------------------------------------------------------------------------------------------------------

# Drops a leading branch type prefix (feature/, bugfix/, ...). Lowercase; every run outside [a-z0-9] -> '-';
# trim '-'. Longer than 30 -> cut to 25 chars at a word boundary (a single long word is cut hard) + '-' + first
# 4 hex of SHA-1 of the full sanitized name, so different long branches stay apart.
function ConvertTo-NormalEnvName([string] $Value) {
    $prefixes = $script:BranchTypePrefixes -join '|'
    $withoutType = $Value -replace "(?i)^($prefixes)/", ''
    $sanitized = ($withoutType.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')
    if ($sanitized.Length -le $script:MaxEnvNameLength) { return $sanitized }

    $hash = [System.Security.Cryptography.SHA1]::HashData([System.Text.Encoding]::UTF8.GetBytes($sanitized))
    $suffix = [Convert]::ToHexString($hash).Substring(0, $script:ShortHashLength).ToLowerInvariant()
    $headLength = $script:MaxEnvNameLength - $script:ShortHashLength - 1
    $head = $sanitized.Substring(0, $headLength)
    $wordCut = $sanitized[$headLength] -ne '-' -and $head.Contains('-')
    if ($wordCut) { $head = $head.Substring(0, $head.LastIndexOf('-')) }
    "$($head.TrimEnd('-'))-$suffix"
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
        # ConvertFrom-Json turns ISO date strings (ee.deployed-at) into DateTime; keep labels as written.
        foreach ($key in @($c.Config.Labels.Keys)) {
            if ($c.Config.Labels[$key] -is [DateTime]) {
                $c.Config.Labels[$key] = $c.Config.Labels[$key].ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
            }
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
        [AllowEmptyString()] [string] $KeysPath = '/keys',
        # Keep the original deploy time (db-update/db-reset do not deploy new code). Default: now.
        [string] $DeployedAt
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
    $env:EE_DEPLOYED_AT = if ($DeployedAt) { $DeployedAt } else { [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ') }
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

function Test-DbRole([string] $Db) {
    "$(Invoke-PlatformSql -Sql "SELECT 1 FROM pg_roles WHERE rolname = '$Db';")".Trim() -eq '1'
}

# Role + database ee_<env>, idempotent. -Password: create the role with it or change it; empty: keep the password.
# CONNECT is revoked from PUBLIC, so another env's role cannot open this database (owner and superuser still can).
function Set-EnvDatabase([string] $Db, [string] $Password) {
    Write-Host "==> Database $Db (role, database, connect only for its own role)" -ForegroundColor Cyan
    $lines = @()
    if ($Password) {
        $lines += ('DO $do$ BEGIN IF EXISTS (SELECT FROM pg_roles WHERE rolname = ''{0}'') ' +
            'THEN ALTER ROLE "{0}" PASSWORD ''{1}''; ELSE CREATE ROLE "{0}" LOGIN PASSWORD ''{1}''; END IF; END $do$;') -f $Db, $Password
    }
    $lines += 'SELECT ''CREATE DATABASE "{0}" OWNER "{0}"'' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = ''{0}'')\gexec' -f $Db
    $lines += 'REVOKE CONNECT ON DATABASE "{0}" FROM PUBLIC;' -f $Db
    Invoke-PlatformSql -Sql ($lines -join "`n") | Out-Null
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
    if (-not (Test-DbRole -Db $db)) { $current = $null }
    Set-EnvDatabase -Db $db -Password $(if ($current) { '' } else { $newPassword })

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

# ---------------------------------------------------------------------------------------------------------------
# After deploy: list, db-update, db-reset, db-connect, destroy. The running env is the state: everything is read
# back from its containers' labels and environment (no state files).
# ---------------------------------------------------------------------------------------------------------------

function Get-ConnectionPassword([object] $Container) {
    $connectionString = if ($Container) { $Container.Env['ConnectionStrings__Default'] }
    if ($connectionString -match '(?i)(?:^|;)\s*Password\s*=\s*([^;]*)') { $Matches[1] } else { $null }
}

function Get-EnvState {
    <#
    .SYNOPSIS
        What is deployed as env <env>, from its api/web containers: tag, branch, commit, code source, deploy time,
        replica counts, Data Protection keys mode, database password, compose file. Throws if nothing is deployed.
    #>
    param([Parameter(Mandatory)] [string] $EnvName)

    $containers = @(Get-EnvContainers -EnvName $EnvName | Where-Object { $_.Service -in 'api', 'web' })
    if ($containers.Count -eq 0) { throw "Env '$EnvName' is not deployed. Deploy it first: ./deploy/deploy.ps1" }
    $api = @($containers | Where-Object Service -eq 'api')
    $web = @($containers | Where-Object Service -eq 'web')
    if ($api.Count -eq 0) { throw "Env '$EnvName' has no api container. Deploy it again: ./deploy/deploy.ps1" }
    $labels = $api[0].Labels

    # Envs deployed before the ee.source label existed: a worktree means the code came from there.
    $worktree = Get-WorktreePath -EnvName $EnvName
    $source = $labels['ee.source']
    if (-not $source) { $source = if (Test-Path (Join-Path $worktree '.git')) { 'worktree' } else { 'local' } }
    $composeRoot = if ($source -eq 'worktree') { $worktree } else { Get-RepoRoot }
    $composeFile = Join-Path $composeRoot 'deploy/env/compose.yml'
    if (-not (Test-Path $composeFile)) {
        throw "Env '$EnvName' was deployed from $composeRoot, but its deploy/env/compose.yml is gone. Deploy it again."
    }

    [pscustomobject]@{
        EnvName     = $EnvName
        Db          = ConvertTo-DbName -EnvName $EnvName
        Tag         = ($api[0].Image -split ':')[-1]
        Branch      = $labels['ee.branch']
        Commit      = $labels['ee.commit']
        Source      = $source
        DeployedAt  = $labels['ee.deployed-at']
        ApiReplicas = [Math]::Max(1, @($api | Where-Object State -eq 'running').Count)
        WebReplicas = [Math]::Max(1, @($web | Where-Object State -eq 'running').Count)
        KeysPath    = if ($web.Count -gt 0) { "$($web[0].Env['DataProtection__KeysPath'])" } else { '/keys' }
        Password    = Get-ConnectionPassword $api[0]
        ComposeFile = $composeFile
    }
}

function Assert-EnvImages([object] $State) {
    foreach ($image in $script:EnvImages.Keys) {
        if (-not (Test-DockerImage "ee/${image}:$($State.Tag)")) {
            throw "Image ee/${image}:$($State.Tag) of env '$($State.EnvName)' is gone (docker image prune?). Deploy it again."
        }
    }
}

# Sets the compose variables so that compose sees the env exactly as deployed (same tag, labels, keys mode).
function Set-EnvComposeVariablesFromState([object] $State, [string] $Password) {
    $variables = @{
        EnvName = $State.EnvName; Tag = $State.Tag; Branch = $State.Branch; Commit = $State.Commit
        Source = $State.Source; KeysPath = $State.KeysPath; DeployedAt = $State.DeployedAt; Password = $Password
    }
    Set-EnvComposeVariables @variables
}

function Get-DeployedEnvs {
    <#
    .SYNOPSIS
        One row per deployed env (from container labels), plus databases ee_* that have no env (Orphan = $true).
    #>
    param()
    $proxy = if ($env:EE_PROXY_PORT) { $env:EE_PROXY_PORT } else { '8080' }
    $domain = if ($env:EE_DOMAIN) { $env:EE_DOMAIN } else { 'localhost' }
    $names = @(docker ps -a --filter label=ee.env --format '{{.Label "ee.env"}}' | Sort-Object -Unique)

    $rows = @(foreach ($name in $names) {
            $containers = @(Get-EnvContainers -EnvName $name | Where-Object { $_.Service -in 'api', 'web' })
            if ($containers.Count -eq 0) { continue }
            $labels = $containers[0].Labels
            $replicas = foreach ($service in 'api', 'web') {
                $all = @($containers | Where-Object Service -eq $service)
                "$(@($all | Where-Object State -eq 'running').Count)/$($all.Count)"
            }
            $commit = "$($labels['ee.commit'])"
            [pscustomobject]@{
                Env        = $name
                Branch     = $labels['ee.branch']
                Commit     = $commit.Substring(0, [Math]::Min(12, $commit.Length)) + $(if ($commit.EndsWith('+dirty')) { '+dirty' })
                DeployedAt = $labels['ee.deployed-at']
                Api        = $replicas[0]
                Web        = $replicas[1]
                Url        = "http://$name.${domain}:$proxy"
                Orphan     = $false
            }
        })

    $databases = @()
    try { $databases = @(Invoke-PlatformSql -Sql "SELECT datname FROM pg_database WHERE datname LIKE 'ee\_%' ORDER BY 1;") }
    catch { Write-Host "Databases not listed: $($_.Exception.Message)" -ForegroundColor Yellow }
    $known = @($rows | ForEach-Object { ConvertTo-DbName -EnvName $_.Env })
    $orphans = @(foreach ($db in $databases | Where-Object { $_ -and $_ -notin $known }) {
            [pscustomobject]@{
                Env = ($db -replace '^ee_', '' -replace '_', '-'); Branch = '(database only)'; Commit = ''; DeployedAt = ''; Api = ''; Web = ''; Url = ''; Orphan = $true
            }
        })
    $rows + $orphans
}

function Invoke-EnvDbUpdate {
    <#
    .SYNOPSIS
        New migrations + new seed sets (or seed --restore) on an env's database, with the deployed migrator image
        and the current password. The apps keep running; the password is not rotated.
    #>
    param(
        [Parameter(Mandatory)] [string] $Env,
        [switch] $Restore
    )
    $envName = Get-EnvName -Env $Env
    $state = Get-EnvState -EnvName $envName
    Assert-EnvImages $state
    Start-Platform

    $composeArgs = Get-EnvComposeArgs -EnvName $envName -ComposeFile $state.ComposeFile
    Set-EnvComposeVariablesFromState $state -Password $state.Password
    Invoke-Native 'Apply migrations (efbundle)' { docker compose @composeArgs run --rm migrate }
    if ($Restore) {
        Invoke-Native 'Seed demo data (restore deleted)' {
            docker compose @composeArgs run --rm seed dotnet EphemeralEnvs.DbTool.dll seed --restore
        }
    }
    else {
        Invoke-Native 'Seed demo data' { docker compose @composeArgs run --rm seed }
    }
    Write-Host ''
    Write-Host "Database $($state.Db) of env '$envName' is up to date." -ForegroundColor Green
}

function Invoke-EnvDbReset {
    <#
    .SYNOPSIS
        Puts an env's database back to the just-deployed state: drop, create (new password), all migrations, all
        seed sets, then recreate api/web with the deployed images, replica counts and keys mode.
    #>
    param([Parameter(Mandatory)] [string] $Env)
    $envName = Get-EnvName -Env $Env
    $state = Get-EnvState -EnvName $envName
    Assert-EnvImages $state
    Start-Platform

    $newPassword = New-DbPassword
    Write-Host "==> Drop database $($state.Db)" -ForegroundColor Cyan
    Invoke-PlatformSql -Sql "DROP DATABASE IF EXISTS `"$($state.Db)`" WITH (FORCE);" | Out-Null
    Set-EnvDatabase -Db $state.Db -Password $newPassword

    $composeArgs = Get-EnvComposeArgs -EnvName $envName -ComposeFile $state.ComposeFile
    Set-EnvComposeVariablesFromState $state -Password $newPassword
    Invoke-Native 'Apply migrations (efbundle)' { docker compose @composeArgs run --rm migrate }
    Invoke-Native 'Seed demo data' { docker compose @composeArgs run --rm seed }
    Invoke-Native "Recreate api x$($state.ApiReplicas) + web x$($state.WebReplicas) with the new password" {
        docker compose @composeArgs up --detach --wait --force-recreate --remove-orphans `
            --scale "api=$($state.ApiReplicas)" --scale "web=$($state.WebReplicas)" api web
    }
    Wait-HttpOk -Url "http://localhost:$env:EE_PROXY_PORT/health/ready" -HostName "$envName-api.$env:EE_DOMAIN"
    Write-Host ''
    Write-Host "Database $($state.Db) of env '$envName' reset: schema from all migrations, demo data from all seed sets." -ForegroundColor Green
}

function Invoke-EnvDbConnect {
    <#
    .SYNOPSIS
        Prints how to connect to an env's database (as the env role, valid until the next deploy; and as the
        superuser); -Psql opens psql on it inside the platform Postgres container (no port, no password).
    #>
    param(
        [Parameter(Mandatory)] [string] $Env,
        [switch] $Psql
    )
    $envName = Get-EnvName -Env $Env
    $db = ConvertTo-DbName -EnvName $envName
    $container = Get-PlatformPostgresContainer
    if ("$(Invoke-PlatformSql -Sql "SELECT 1 FROM pg_database WHERE datname = '$db';")".Trim() -ne '1') {
        throw "Database $db does not exist. Deploy the env first: ./deploy/deploy.ps1"
    }
    if ($Psql) {
        Write-Host "psql on $db as postgres (\q to quit)" -ForegroundColor Cyan
        docker exec -it $container psql -U postgres -d $db
        return
    }

    $port = if ($env:EE_PLATFORM_DB_PORT) { $env:EE_PLATFORM_DB_PORT } else { '5442' }
    $admin = if ($env:EE_PG_ADMIN_PASSWORD) { $env:EE_PG_ADMIN_PASSWORD } else { 'postgres' }
    $password = Get-RunningApiPassword -EnvName $envName
    Write-Host "Database $db on localhost:$port" -ForegroundColor Green
    if ($password) {
        Write-Host '  As the env role (valid until the next deploy or db-reset):'
        Write-Host "    Host=localhost;Port=$port;Database=$db;Username=$db;Password=$password"
    }
    else {
        Write-Host '  The env api is not running, so its role password is unknown; use the superuser.' -ForegroundColor Yellow
    }
    Write-Host '  As the superuser (local only):'
    Write-Host "    Host=localhost;Port=$port;Database=$db;Username=postgres;Password=$admin"
    Write-Host "  Without a port or password: ./deploy/db-connect.ps1 -Env $envName -Psql"
}

function Invoke-EnvDestroy {
    <#
    .SYNOPSIS
        Removes everything of an env: containers, network, volumes, database, role, worktree, wt-<env> images.
        Every step tolerates "already gone", so a half-deployed env can be destroyed too.
    #>
    param([Parameter(Mandatory)] [string] $Env)
    $envName = Get-EnvName -Env $Env
    $db = ConvertTo-DbName -EnvName $envName
    $root = Get-RepoRoot

    # The project name is enough: compose finds containers, networks and volumes by their project label.
    Invoke-Native "Remove containers, network and volumes of ee-$envName" {
        docker compose -p "ee-$envName" down --volumes --remove-orphans
    }

    Start-Platform
    Write-Host "==> Drop database and role $db" -ForegroundColor Cyan
    Invoke-PlatformSql -Sql "DROP DATABASE IF EXISTS `"$db`" WITH (FORCE);`nDROP ROLE IF EXISTS `"$db`";" | Out-Null

    $worktree = Get-WorktreePath -EnvName $envName
    Write-Host "==> Remove worktree .ee/worktrees/$envName" -ForegroundColor Cyan
    if (Test-Path (Join-Path $worktree '.git')) { git -C $root worktree remove --force $worktree }
    git -C $root worktree prune
    if (Test-Path $worktree) { Remove-Item -Recurse -Force $worktree }

    Write-Host "==> Remove images ee/*:wt-$envName" -ForegroundColor Cyan
    foreach ($image in $script:EnvImages.Keys) {
        if (Test-DockerImage "ee/${image}:wt-$envName") { docker image rm "ee/${image}:wt-$envName" | Out-Null }
    }

    Write-Host ''
    Write-Host "Env '$envName' destroyed." -ForegroundColor Green
    Write-Host "Images ee/*:<sha12> stay (shared by envs on the same commit). Remove unused ones: ./deploy/prune-images.ps1"
}

function Select-UnusedEnvImages {
    <#
    .SYNOPSIS
        From image references (repo:tag), the ee/* ones whose tag no deployed env uses. The tag, not a running
        container, decides: the migrator of a deployed env has no container (migrate/seed run with --rm) but
        db-update and db-reset need it. Untagged images (<none>) are skipped.
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Images,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $UsedTags
    )

    foreach ($image in $Images) {
        if ($image -notmatch '^ee/[^/:]+:(?<tag>.+)$') { continue }
        $tag = $Matches.tag
        if ($tag -eq '<none>' -or $tag -in $UsedTags) { continue }
        $image
    }
}

function Invoke-EnvImagePrune {
    <#
    .SYNOPSIS
        Removes ee/* images whose tag no deployed env (any container labelled ee.env) uses.
    #>
    $usedTags = @(docker ps -a --filter 'label=ee.env' --format '{{.Image}}' |
            Where-Object { $_ -match '^ee/[^/:]+:(?<tag>.+)$' } | ForEach-Object { $Matches.tag } | Sort-Object -Unique)
    $images = @(docker images 'ee/*' --format '{{.Repository}}:{{.Tag}}')
    $unused = @(Select-UnusedEnvImages -Images $images -UsedTags $usedTags)

    Write-Host "Tags in use by deployed envs: $(if ($usedTags) { $usedTags -join ', ' } else { '(none)' })"
    if ($unused.Count -eq 0) {
        Write-Host 'No unused ee/* images.' -ForegroundColor Green
        return
    }
    foreach ($image in $unused) {
        Invoke-Native "Remove image $image" { docker image rm $image } | Out-Null
    }
    Write-Host "Removed $($unused.Count) image(s). Docker frees the space of layers no other image shares." -ForegroundColor Green
}

Export-ModuleMember -Function Get-EnvName, ConvertTo-DbName, New-DbPassword, Get-RepoRoot, Resolve-Commit,
Invoke-Native, Use-DockerEnv, Invoke-PlatformCompose, Start-Platform, Invoke-PlatformSql, Get-EnvContainers,
Get-RunningApiPassword, Get-WorktreePath, Get-EnvComposeArgs, Set-EnvComposeVariables, Invoke-EnvDeploy,
Get-EnvState, Get-DeployedEnvs, Invoke-EnvDbUpdate, Invoke-EnvDbReset, Invoke-EnvDbConnect, Invoke-EnvDestroy,
Select-UnusedEnvImages, Invoke-EnvImagePrune
