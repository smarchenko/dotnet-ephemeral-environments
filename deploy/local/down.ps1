<#
.SYNOPSIS
    Stops the local environment.
.DESCRIPTION
    Removes the containers and the network. The database volume is kept unless -RemoveData is given.
.EXAMPLE
    ./deploy/local/down.ps1
.EXAMPLE
    ./deploy/local/down.ps1 -RemoveData     # also drop the database (next up.ps1 migrates and seeds from scratch)
#>
[CmdletBinding()]
param(
    [switch] $RemoveData
)

. "$PSScriptRoot/common.ps1"

Use-LocalDockerEnv -WebPort (Get-DefaultPort 'EE_WEB_PORT' 5080) -ApiPort (Get-DefaultPort 'EE_API_PORT' 5081) -DbPort (Get-DefaultPort 'EE_DB_PORT' 5432) -Script {
    $downArgs = @('down')
    if ($RemoveData) { $downArgs += '--volumes' }
    Invoke-Step 'Stop containers' { Invoke-Compose @downArgs }

    Write-Host ($(if ($RemoveData) { 'Environment removed, database volume dropped.' } else { 'Environment stopped, database volume kept.' })) -ForegroundColor Green
}
