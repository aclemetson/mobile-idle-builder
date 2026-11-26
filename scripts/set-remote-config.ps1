<#
.SYNOPSIS
    Sets Unity Remote Config keys in one UGS environment.

.DESCRIPTION
    Used by CI after a store upload to publish the newest build version, which drives the
    in-game update prompt (update.latest.android / update.latest.ios, see AppUpdateCheck.cs).

    Remote Config Admin API (service account, HTTP Basic):
      GET  https://services.api.unity.com/remote-config/v1/projects/{projectId}/environments/{envId}/configs
      PUT  https://services.api.unity.com/remote-config/v1/projects/{projectId}/configs/{configId}
    The PUT replaces the whole settings array, so this script reads the current settings,
    changes only the one key (adding it as a string if missing), and writes all of them back.

    -JsonFile sets several string keys in one PUT (a JSON object of key -> string value). The release
    workflow uses it for the what's-new notes (whatsnew.notes + whatsnew.version, #502) so both change
    together. The body is sent as UTF-8 bytes, so notes may hold non-ASCII text.

    Runs on Windows PowerShell 5.1 and PowerShell 7 (Linux/macOS runners). Keep it ASCII only.

.EXAMPLE
    ./scripts/set-remote-config.ps1 -EnvironmentId $env:UGS_ENV_ID_DEVELOPMENT -Key update.latest.android -Value 0.5.53

    ./scripts/set-remote-config.ps1 -EnvironmentId $env:UGS_ENV_ID_DEVELOPMENT -JsonFile whatsnew.json

    Credentials come from the environment: UGS_SERVICE_KEY_ID, UGS_SERVICE_SECRET, UGS_PROJECT_ID.
#>
param(
    [Parameter(Mandatory = $true)] [string] $EnvironmentId,
    [string] $Key,
    [string] $Value,
    [string] $JsonFile,
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

# The keys to write: one -Key/-Value pair, or every entry of -JsonFile.
$updates = [ordered]@{}
if ($JsonFile) {
    $raw = [IO.File]::ReadAllText((Resolve-Path $JsonFile), [Text.Encoding]::UTF8)
    $parsed = $raw | ConvertFrom-Json
    foreach ($p in $parsed.PSObject.Properties) { $updates[$p.Name] = [string]$p.Value }
} elseif ($Key) {
    $updates[$Key] = $Value
}
if ($updates.Count -eq 0) { Write-Error 'Pass -Key and -Value, or -JsonFile.'; exit 1 }

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
$found  = @{}
foreach ($entry in @($config.value)) {
    if ($null -eq $entry) { continue }
    if ($updates.Contains($entry.key)) {
        $entry.value = $updates[$entry.key]
        $found[$entry.key] = $true
        Write-Host "Remote Config: $($entry.key) updated (type $($entry.type), $($updates[$entry.key].Length) chars)"
    }
    [void]$values.Add($entry)
}
foreach ($k in $updates.Keys) {
    if ($found.ContainsKey($k)) { continue }
    [void]$values.Add([pscustomobject]@{ key = $k; type = 'string'; value = $updates[$k] })
    Write-Host "Remote Config: $k added as string ($($updates[$k].Length) chars)"
}

$body = @{ type = 'settings'; value = $values.ToArray() } | ConvertTo-Json -Depth 10
if ($DryRun) {
    Write-Host "Dry run: would PUT $($values.Count) settings to config $($config.id)."
    exit 0
}

# Bytes, not a string: Windows PowerShell 5.1 would send a string body as ISO-8859-1.
Invoke-RestMethod -Method Put -Headers $headers -ContentType 'application/json; charset=utf-8' `
    -Uri "$base/configs/$($config.id)" -Body ([Text.Encoding]::UTF8.GetBytes($body)) | Out-Null
Write-Host "Remote Config updated: environment $EnvironmentId, $($values.Count) settings."
