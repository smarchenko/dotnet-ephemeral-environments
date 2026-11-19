#Requires -Version 7.2
<#
.SYNOPSIS
    Deploys a branch (or the working tree) as an ephemeral environment: http://<env>.localhost:8080.
.DESCRIPTION
    -Branch <b>        committed code of a branch (local, else <remote>/<b>; no fetch), checked out into
                       .ee/worktrees/<env>; images ee/{api,web,migrator}:<sha12> are built once per commit.
    -FromWorkingTree   the current checkout as it is (uncommitted changes too); images ee/*:wt-<env>, always rebuilt.
    Env name: -Env, else the branch name normalized (feature/Add-X -> feature-add-x). One env = one branch:
    deploying another branch onto a taken name is refused unless -Force.
    Steps: images -> platform (Traefik + Postgres) -> database ee_<env> -> migrate -> seed -> new DB password ->
    api + web -> wait for /health/ready and / through Traefik. A failing migrate/seed leaves the running version up.
    See docs/environments.md.
.EXAMPLE
    ./deploy/deploy.ps1 -Branch master
.EXAMPLE
    ./deploy/deploy.ps1 -FromWorkingTree -Env my-wip
.EXAMPLE
    ./deploy/deploy.ps1 -Branch master -WebReplicas 2 -NoSharedKeys    # the antiforgery experiment
#>
[CmdletBinding(DefaultParameterSetName = 'Branch')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Branch')]
    [string] $Branch,

    [Parameter(Mandatory, ParameterSetName = 'WorkingTree')]
    [switch] $FromWorkingTree,

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
