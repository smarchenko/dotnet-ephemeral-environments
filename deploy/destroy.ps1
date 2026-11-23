#Requires -Version 7.2
<#
.SYNOPSIS
    Removes an ephemeral environment completely.
.DESCRIPTION
    Containers, network and volumes of ee-<env> -> database and role ee_<env> -> worktree .ee/worktrees/<env> ->
    images ee/*:wt-<env>. Every step tolerates "already gone", so a half-deployed env can be destroyed and running it
    twice is fine. Images ee/*:<sha12> stay (other envs may use the same commit): remove unused ones with ./deploy/prune-images.ps1.
.EXAMPLE
    ./deploy/destroy.ps1 -Env demo-x
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Env
)

$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/lib/EphemeralEnvs.Deploy.psm1" -Force

Use-DockerEnv { Invoke-EnvDestroy -Env $Env }
