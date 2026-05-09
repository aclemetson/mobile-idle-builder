# Increments bundleVersion patch and AndroidBundleVersionCode in ProjectSettings.asset.
# Called by .git/hooks/prepare-commit-msg. Exits 1 on failure to abort the commit.
param()

$ErrorActionPreference = 'Stop'

$projectRoot  = Split-Path -Parent $PSScriptRoot
$settingsPath = Join-Path $projectRoot "ProjectSettings\ProjectSettings.asset"

if (-not (Test-Path $settingsPath)) {
    Write-Error "[versioning] ProjectSettings.asset not found at: $settingsPath"
    exit 1
}

$content = [System.IO.File]::ReadAllText($settingsPath)

# --- bundleVersion (X.Y.Z) ---
$bundlePattern = '(?m)^(\s*bundleVersion:\s*)(\d+)\.(\d+)\.(\d+)(\s*)$'
if ($content -notmatch $bundlePattern) {
    Write-Error "[versioning] Could not find bundleVersion in ProjectSettings.asset"
    exit 1
}
$major    = [int]$Matches[2]
$minor    = [int]$Matches[3]
$patch    = [int]$Matches[4]
$newPatch = $patch + 1
$newVersion = "$major.$minor.$newPatch"

$content = [regex]::Replace(
    $content,
    $bundlePattern,
    "`${1}$newVersion`${5}",
    [System.Text.RegularExpressions.RegexOptions]::Multiline
)

# --- AndroidBundleVersionCode ---
$codePattern = '(?m)^(\s*AndroidBundleVersionCode:\s*)(\d+)(\s*)$'
if ($content -notmatch $codePattern) {
    Write-Error "[versioning] Could not find AndroidBundleVersionCode in ProjectSettings.asset"
    exit 1
}
$newCode = [int]$Matches[2] + 1

$content = [regex]::Replace(
    $content,
    $codePattern,
    "`${1}$newCode`${3}",
    [System.Text.RegularExpressions.RegexOptions]::Multiline
)

[System.IO.File]::WriteAllText($settingsPath, $content, [System.Text.UTF8Encoding]::new($false))

$gitResult = & git -C $projectRoot add "ProjectSettings/ProjectSettings.asset" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "[versioning] git add failed: $gitResult"
    exit 1
}

Write-Host "[versioning] $major.$minor.$patch -> $newVersion (BundleVersionCode: $newCode)"
exit 0
