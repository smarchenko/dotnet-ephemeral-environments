#Requires -Version 7.2
<#
.SYNOPSIS
    Deploys code as an ephemeral environment: http://<env>.localhost:8080.
.DESCRIPTION
    (no switch)    the current branch as it is on disk, uncommitted changes included. Nothing uncommitted ->
                   images ee/{api,web,migrator}:<sha12> (built once per commit, reused); otherwise ee/*:wt-<env>,
                   always rebuilt, and the env is labelled <sha>+dirty.
    -Committed     the current branch's HEAD commit only, checked out into .ee/worktrees/<env> (what CI deploys).
    -Branch <b>    another branch's commit (local, else <remote>/<b>; no fetch), in .ee/worktrees/<env>;
                   your checkout is not touched.
    Env name: -Env, else the branch name normalized (feature/Add-X -> feature-add-x). One env = one branch:
    deploying another branch onto a taken name is refused unless -Force.
    Steps: images -> platform (Traefik + Postgres) -> database ee_<env> -> migrate -> seed -> new DB password ->
    api + web -> wait for /health/ready and / through Traefik. A failing migrate/seed leaves the running version up.
    See docs/environments.md.
.EXAMPLE
    ./deploy/deploy.ps1
.EXAMPLE
    ./deploy/deploy.ps1 -Committed
.EXAMPLE
    ./deploy/deploy.ps1 -Branch feature-y
.EXAMPLE
    ./deploy/deploy.ps1 -WebReplicas 2 -NoSharedKeys    # the antiforgery experiment
#>
[CmdletBinding(DefaultParameterSetName = 'Current')]
param(
    # Only the committed code of the current branch (ignore local changes).
    [Parameter(ParameterSetName = 'Current')]
    [switch] $Committed,

    # Another branch (its committed code).
    [Parameter(Mandatory, ParameterSetName = 'Branch')]
    [string] $Branch,

    # Env name in normal form (lowercase, [a-z0-9-]); default: from the branch.
    [string] $Env,

    # Take over an env that runs another branch.
    [switch] $Force,

    # Build the images even if ee/*:<sha12> exist.
    [switch] $Rebuild,

    [ValidateRange(1, 10)] [int] $ApiReplicas = 1,
    [ValidateRange(1, 10)] [int] $WebReplicas = 1,

    # Do not share the Data Protection keys between web replicas (shows the antiforgery problem).
    [switch] $NoSharedKeys
)

$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/lib/EphemeralEnvs.Deploy.psm1" -Force

$deployArgs = $PSBoundParameters
Use-DockerEnv { Invoke-EnvDeploy @deployArgs }
