#Requires -Version 7.2
<#
.SYNOPSIS
    Shows how to connect to an environment's database, or opens psql on it.
.DESCRIPTION
    Prints connection strings for localhost:<EE_PLATFORM_DB_PORT> (5442): as the env role (the password is new on
    every deploy, so the string is valid until the next deploy or db-reset) and as the superuser postgres.
    -Psql opens psql on ee_<env> inside the platform Postgres container: no port and no password needed.
.EXAMPLE
    ./deploy/db-connect.ps1 -Env demo-x
.EXAMPLE
    ./deploy/db-connect.ps1 -Env demo-x -Psql
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Env,

    [switch] $Psql
)

$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/lib/EphemeralEnvs.Deploy.psm1" -Force

Use-DockerEnv { Invoke-EnvDbConnect -Env $Env -Psql:$Psql }
