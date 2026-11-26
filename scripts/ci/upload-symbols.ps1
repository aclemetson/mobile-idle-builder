# Uploads the Android IL2CPP symbols of the build that just ran to Unity Diagnostics.
#
# Wraps usymtool.ps1 (downloaded unedited from Dashboard > View Diagnostics > Debugging Symbols >
# Download CI script). That script is configured by placeholder lines, so this fills them into a temp
# copy (never the committed file, so no secret lands in the repo) and runs it.
#
# Run right after the Android build, BEFORE "Save Library" moves Library/ out of the checkout: the
# symbols are read from Library/Bee/Android/Prj/IL2CPP.
#
# Environment (GitHub secrets of the ci-cd-automation service account):
#   UGS_SERVICE_KEY_ID, UGS_SERVICE_SECRET, UGS_PROJECT_ID
# Missing credentials skip with a warning (exit 0). See docs/runbooks/cloud-diagnostics-setup.md.
param(
    [string]$EditorPath = "C:\Program Files\Unity\Hub\Editor\6000.3.6f1",
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

$ErrorActionPreference = "Stop"

$keyId   = $env:UGS_SERVICE_KEY_ID
$secret  = $env:UGS_SERVICE_SECRET
$project = $env:UGS_PROJECT_ID
if (-not $keyId -or -not $secret -or -not $project) {
    Write-Host "::warning::Symbol upload skipped: UGS service account secrets are not set."
    exit 0
}

$source = Join-Path $PSScriptRoot "usymtool.ps1"
if (-not (Test-Path $source)) {
    Write-Host "::warning::Symbol upload skipped: $source not found."
    exit 0
}

$basic  = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${keyId}:${secret}"))
$header = "Basic $basic"
Write-Host "::add-mask::$basic"   # derived from secrets, so GitHub would not mask it on its own

# Each placeholder line must exist verbatim; a re-downloaded script that changed them fails loudly
# instead of uploading with a half-filled config.
$content = Get-Content -LiteralPath $source -Raw -Encoding UTF8
$replacements = [ordered]@{
    '$PLATFORM = "android"'                                            = '$PLATFORM = "android"'
    '$UNITY_PROJECT_ID = "<your-unity-project-id>"'                    = ('$UNITY_PROJECT_ID = "' + $project + '"')
    '$UNITY_SERVICE_ACCOUNT_AUTH_HEADER = "<your-service-account-auth-header>"' = ('$UNITY_SERVICE_ACCOUNT_AUTH_HEADER = "' + $header + '"')
    '$UNITY_EDITOR_PATH = "C:\Program Files\Unity\Hub\Editor\6000.3.10f1"' = ('$UNITY_EDITOR_PATH = "' + $EditorPath + '"')
    '$UNITY_PROJECT_PATH = "<your-unity-project-path>"'                = ('$UNITY_PROJECT_PATH = "' + $ProjectPath + '"')
}
foreach ($k in $replacements.Keys) {
    if (-not $content.Contains($k)) {
        Write-Host "::error::usymtool.ps1 no longer contains the line: $k (re-check the wrapper against the new script)"
        exit 1
    }
    $content = $content.Replace($k, $replacements[$k])
}

$tempDir = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$filled  = Join-Path $tempDir "usymtool-filled.ps1"
try {
    # UTF-8 with BOM so Windows PowerShell 5.1 reads the script's box-drawing comments correctly.
    [IO.File]::WriteAllText($filled, $content, (New-Object Text.UTF8Encoding $true))
    & powershell -NoProfile -ExecutionPolicy Bypass -File $filled
    $code = $LASTEXITCODE
} finally {
    Remove-Item -LiteralPath $filled -Force -ErrorAction SilentlyContinue
}

if ($code -ne 0) {
    Write-Host "::error::Symbol upload failed (exit $code). Log: $env:LOCALAPPDATA\Unity\Editor\symbol_upload.log"
    exit $code
}
Write-Host "Symbol upload finished."
