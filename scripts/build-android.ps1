param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe",
    [int]$TimeoutMinutes = 60
)

# Builds the signed Android App Bundle headlessly for CI.
# Mirrors scripts/test-local.ps1: uses Start-Process so we reliably WAIT on
# Unity.exe. The & call operator does not block on GUI applications in a
# non-interactive PowerShell session, which makes the build step "succeed"
# while Unity is still starting and no .aab has been produced.

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot

if ($env:UNITY_PATH) { $UnityPath = $env:UNITY_PATH }

if (-not (Test-Path $UnityPath)) {
    Write-Error "Unity not found at: $UnityPath`nOverride with -UnityPath or the UNITY_PATH env var."
    exit 1
}

# Kill any orphaned batch-mode Unity processes from a previous run on THIS
# project path (matches the CI checkout dir only, never the dev's local editor).
$orphans = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
    Where-Object { $_.CommandLine -like "*$ProjectRoot*" }
if ($orphans) {
    Write-Host "[build] Killing $($orphans.Count) orphaned Unity process(es) from a previous run..."
    $orphans | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 3
}

$logFile = Join-Path $ProjectRoot "build-android.log"

Write-Host "[build] Building Android App Bundle (timeout: $TimeoutMinutes min)..."

# Cold CI checkouts have an empty Library, so -buildTarget Android makes Unity
# start directly in Android mode instead of switching mid-run. BuildScript calls
# EditorApplication.Exit() itself, so no -quit is needed.
$proc = Start-Process -FilePath $UnityPath `
    -WorkingDirectory $ProjectRoot `
    -ArgumentList "-batchmode -nographics -projectPath `"$ProjectRoot`" -buildTarget Android -executeMethod MobileIdleBuilder.Editor.BuildScript.BuildAndroid -logFile `"$logFile`"" `
    -PassThru -NoNewWindow
$timeoutMs = $TimeoutMinutes * 60 * 1000
$finished  = $proc.WaitForExit($timeoutMs)

if (-not $finished) {
    Write-Host "[build] Unity timed out after $TimeoutMinutes minutes - killing process."
    $proc.Kill()
    $exitCode = -1
} else {
    $exitCode = $proc.ExitCode
}
Write-Host "[build] Unity exited with code $exitCode"

if ($exitCode -ne 0 -and (Test-Path $logFile)) {
    Write-Host "[build] --- Last 50 lines of unity log ---"
    Get-Content $logFile -Tail 50 | ForEach-Object { Write-Host $_ }
    Write-Host "[build] --- End of log ---"
}

exit $exitCode
