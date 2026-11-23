#Requires -Version 7.2
<#
.SYNOPSIS
    Puts an environment's database back to the just-deployed state: drop, create, all migrations, all seed sets.
.DESCRIPTION
    Everything in ee_<env> is lost (QA data, edits). The role gets a new password, then api and web are recreated
    with the same images, replica counts and Data Protection keys mode as deployed. Use it between test rounds.
.EXAMPLE
    ./deploy/db-reset.ps1 -Env demo-x
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Env
)

$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/lib/EphemeralEnvs.Deploy.psm1" -Force

Use-DockerEnv { Invoke-EnvDbReset -Env $Env }
