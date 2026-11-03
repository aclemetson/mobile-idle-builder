param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe",
    [int]$TimeoutMinutes = 150,
    [string]$ExecuteMethod = "MobileIdleBuilder.Editor.BuildScript.BuildAndroid"
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

Write-Host "[build] Building Android App Bundle via $ExecuteMethod (timeout: $TimeoutMinutes min)..."

# Cold CI checkouts have an empty Library, so -buildTarget Android makes Unity
# start directly in Android mode instead of switching mid-run. BuildScript calls
# EditorApplication.Exit() itself, so no -quit is needed.
$proc = Start-Process -FilePath $UnityPath `
    -WorkingDirectory $ProjectRoot `
    -ArgumentList "-batchmode -nographics -projectPath `"$ProjectRoot`" -buildTarget Android -executeMethod $ExecuteMethod -logFile `"$logFile`"" `
    -PassThru -NoNewWindow

# Force the Process object to cache its OS handle while Unity is still alive. Without this,
# Start-Process -PassThru leaves ExitCode blank afterwards, and "exit $null" exits 0 -- so a
# failed build reported success (same fix as scripts/test-local.ps1).
try { $null = $proc.Handle } catch { }

$timeoutMs = $TimeoutMinutes * 60 * 1000
$finished  = $proc.WaitForExit($timeoutMs)

if (-not $finished) {
    Write-Host "[build] Unity timed out after $TimeoutMinutes minutes - killing process."
    $proc.Kill()
    $exitCode = -1
} else {
    $exitCode = $proc.ExitCode
    if ($null -eq $exitCode -or "$exitCode" -eq "") {
        # Fail closed: BuildScript always calls EditorApplication.Exit(0|1), so an unreadable
        # code means we cannot prove the build succeeded.
        Write-Host "[build] Unity exit code unavailable - treating as FAILED."
        $exitCode = -2
    }
}
Write-Host "[build] Unity exited with code $exitCode"

if ($exitCode -ne 0 -and (Test-Path $logFile)) {
    Write-Host "[build] --- Last 50 lines of unity log ---"
    Get-Content $logFile -Tail 50 | ForEach-Object { Write-Host $_ }
    Write-Host "[build] --- End of log ---"
}

exit $exitCode
