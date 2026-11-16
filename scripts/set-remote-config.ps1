<#
.SYNOPSIS
    Sets one Unity Remote Config key in one UGS environment.

.DESCRIPTION
    Used by CI after a store upload to publish the newest build version, which drives the
    in-game update prompt (update.latest.android / update.latest.ios, see AppUpdateCheck.cs).

    Remote Config Admin API (service account, HTTP Basic):
      GET  https://services.api.unity.com/remote-config/v1/projects/{projectId}/environments/{envId}/configs
      PUT  https://services.api.unity.com/remote-config/v1/projects/{projectId}/configs/{configId}
    The PUT replaces the whole settings array, so this script reads the current settings,
    changes only the one key (adding it as a string if missing), and writes all of them back.

    Runs on Windows PowerShell 5.1 and PowerShell 7 (Linux/macOS runners). Keep it ASCII only.

.EXAMPLE
    ./scripts/set-remote-config.ps1 -EnvironmentId $env:UGS_ENV_ID_DEVELOPMENT -Key update.latest.android -Value 0.5.53

    Credentials come from the environment: UGS_SERVICE_KEY_ID, UGS_SERVICE_SECRET, UGS_PROJECT_ID.
#>
param(
    [Parameter(Mandatory = $true)] [string] $EnvironmentId,
    [Parameter(Mandatory = $true)] [string] $Key,
    [Parameter(Mandatory = $true)] [string] $Value,
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'

$keyId     = $env:UGS_SERVICE_KEY_ID
$secret    = $env:UGS_SERVICE_SECRET
$projectId = $env:UGS_PROJECT_ID
if (-not $keyId -or -not $secret -or -not $projectId) {
    Write-Error 'UGS_SERVICE_KEY_ID, UGS_SERVICE_SECRET and UGS_PROJECT_ID must be set.'
    exit 1
}
if (-not $EnvironmentId) { Write-Error 'EnvironmentId is empty.'; exit 1 }

$token   = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${keyId}:${secret}"))
$headers = @{ Authorization = "Basic $token" }
$base    = 'https://services.api.unity.com/remote-config/v1/projects/' + $projectId

$listed   = Invoke-RestMethod -Method Get -Headers $headers -Uri "$base/environments/$EnvironmentId/configs"
$settings = @($listed.configs | Where-Object { $_.type -eq 'settings' })
if ($settings.Count -ne 1) {
    Write-Error "Expected one settings config in environment $EnvironmentId, found $($settings.Count)."
    exit 1
}
$config = $settings[0]

$values = New-Object System.Collections.ArrayList
$found  = $false
foreach ($entry in @($config.value)) {
    if ($null -eq $entry) { continue }
    if ($entry.key -eq $Key) {
        $old = $entry.value
        $entry.value = $Value
        $found = $true
        Write-Host "Remote Config: $Key '$old' -> '$Value' (type $($entry.type))"
    }
    [void]$values.Add($entry)
}
if (-not $found) {
    [void]$values.Add([pscustomobject]@{ key = $Key; type = 'string'; value = $Value })
    Write-Host "Remote Config: $Key added as string '$Value'"
}

$body = @{ type = 'settings'; value = $values.ToArray() } | ConvertTo-Json -Depth 10
if ($DryRun) {
    Write-Host "Dry run: would PUT $($values.Count) settings to config $($config.id)."
    exit 0
}

Invoke-RestMethod -Method Put -Headers $headers -ContentType 'application/json' `
    -Uri "$base/configs/$($config.id)" -Body $body | Out-Null
Write-Host "Remote Config updated: environment $EnvironmentId, $($values.Count) settings."
