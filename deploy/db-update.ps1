#Requires -Version 7.2
<#
.SYNOPSIS
    Applies new migrations and new seed sets to an environment's database; -Restore brings deleted demo data back.
.DESCRIPTION
    Uses the env's deployed migrator image (ee/migrator:<tag> from its containers) and its current database
    password; the apps keep running and the password is not rotated. Data changed by hand is never overwritten:
    seed only inserts rows whose key is missing. To get new code, deploy instead.
.EXAMPLE
    ./deploy/db-update.ps1 -Env demo-x -Restore
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Env,

    [switch] $Restore
)

$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/lib/EphemeralEnvs.Deploy.psm1" -Force

Use-DockerEnv { Invoke-EnvDbUpdate -Env $Env -Restore:$Restore }
