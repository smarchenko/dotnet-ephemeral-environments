#Requires -Version 7.2
<#
.SYNOPSIS
    Lists the deployed ephemeral environments.
.DESCRIPTION
    One row per env, read from its containers' labels: branch, commit (+dirty = uncommitted changes were deployed),
    deploy time (UTC), running/total api and web replicas, Web URL. Databases ee_* without an env are listed as
    "(database only)": remove them with ./deploy/destroy.ps1 -Env <env>.
.EXAMPLE
    ./deploy/list.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/lib/EphemeralEnvs.Deploy.psm1" -Force

Use-DockerEnv {
    $envs = @(Get-DeployedEnvs)
    if ($envs.Count -eq 0) {
        Write-Host 'No environments. Deploy one: ./deploy/deploy.ps1'
        return
    }
    # Explicit width: when the output is redirected, the default width would cut the URLs.
    $envs | Format-Table Env, Branch, Commit, DeployedAt, Api, Web, Url -AutoSize | Out-String -Width 400 | Write-Host
}
