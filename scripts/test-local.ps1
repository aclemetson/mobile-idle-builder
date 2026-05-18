param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe",
    [string]$ResultsPath = "TestResults\editmode.xml",
    [int]$TimeoutMinutes = 30
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path $UnityPath)) {
    Write-Error "Unity not found at: $UnityPath`nOverride with: .\scripts\test-local.ps1 -UnityPath 'C:\path\to\Unity.exe'"
    exit 1
}

# Kill any orphaned Unity processes from a previous CI run on this project path.
# Only matches batch-mode Unity instances using this project (not the developer's local editor).
$orphans = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
    Where-Object { $_.CommandLine -like "*$ProjectRoot*" }
if ($orphans) {
    Write-Host "[tests] Killing $($orphans.Count) orphaned Unity process(es) from a previous run..."
    $orphans | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 3
}

$runningEditors = Get-Process -Name "Unity" -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match "mobile-idle-builder" }
if ($runningEditors) {
    Write-Error "Unity Editor is open with this project. Close it before running tests."
    exit 1
}

$ResultsDir = Join-Path $ProjectRoot (Split-Path $ResultsPath -Parent)
if (-not (Test-Path $ResultsDir)) { New-Item -ItemType Directory $ResultsDir | Out-Null }

$resultsFile = Join-Path $ProjectRoot $ResultsPath
$logFile     = Join-Path $ProjectRoot "TestResults\unity.log"

Write-Host "[tests] Running edit mode tests (timeout: $TimeoutMinutes min)..."

# Use Start-Process so we can reliably wait on Unity.exe, which is a GUI application.
# The & operator does not block on GUI apps in non-interactive PowerShell sessions.
$proc = Start-Process -FilePath $UnityPath `
    -ArgumentList "-batchmode -nographics -quit -projectPath `"$ProjectRoot`" -runTests -testPlatform editmode -testResults `"$resultsFile`" -logFile `"$logFile`"" `
    -PassThru -NoNewWindow
$timeoutMs = $TimeoutMinutes * 60 * 1000
$finished  = $proc.WaitForExit($timeoutMs)

if (-not $finished) {
    Write-Host "[tests] Unity timed out after $TimeoutMinutes minutes — killing process."
    $proc.Kill()
    $unityExitCode = -1
} else {
    $unityExitCode = $proc.ExitCode
}
Write-Host "[tests] Unity exited with code $unityExitCode"

if ($unityExitCode -ne 0 -and (Test-Path $logFile)) {
    Write-Host "[tests] --- Last 50 lines of unity.log ---"
    Get-Content $logFile -Tail 50 | ForEach-Object { Write-Host $_ }
    Write-Host "[tests] --- End of log ---"
}

if (Test-Path $resultsFile) {
    $xml    = [xml](Get-Content $resultsFile)
    $run    = $xml.'test-run'
    $passed = $run.passed
    $failed = $run.failed
    $total  = $run.total
    Write-Host "[tests] $passed passed / $failed failed / $total total"
    if ([int]$failed -gt 0) { Start-Process $resultsFile }
    $exitCode = if ([int]$failed -gt 0) { 1 } else { 0 }
} else {
    Write-Host "[tests] No results file found -- check: $logFile"
    $exitCode = 1
}

if ($exitCode -eq 0) { Write-Host "[tests] PASSED" } else { Write-Host "[tests] FAILED (exit $exitCode)" }
exit $exitCode
