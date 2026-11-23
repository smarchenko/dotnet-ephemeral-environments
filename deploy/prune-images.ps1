#Requires -Version 7.2
<#
.SYNOPSIS
    Removes ee/* images that no deployed environment uses.
.DESCRIPTION
    destroy.ps1 keeps images ee/*:<sha12> because another env may run the same commit. This script removes every
    ee/* image whose tag is not the tag of a deployed env (its containers). The migrator image of a deployed env is
    kept although no container runs it: db-update and db-reset need it. Other images are not touched.
.EXAMPLE
    ./deploy/prune-images.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/lib/EphemeralEnvs.Deploy.psm1" -Force

Use-DockerEnv { Invoke-EnvImagePrune }
