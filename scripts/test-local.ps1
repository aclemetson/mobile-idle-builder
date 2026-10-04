param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe",
    [string]$TestPlatform = "editmode",
    [string]$ResultsPath = "",
    [int]$TimeoutMinutes = 30
)

if (-not $ResultsPath) { $ResultsPath = "TestResults\$TestPlatform.xml" }

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
$logFile     = Join-Path $ProjectRoot "TestResults\unity-$TestPlatform.log"

# Delete any results file left by a previous run BEFORE launching Unity.
# Without this, a stale file satisfies the Test-Path check at the end, so a Unity that aborts
# during compilation (and therefore writes no results at all) reports the PREVIOUS run's numbers
# as a pass. That failure mode is silent and survives a branch switch, so the stale numbers can
# even come from different code than the one under test.
if (Test-Path $resultsFile) { Remove-Item $resultsFile -Force }

Write-Host "[tests] Running $TestPlatform tests (timeout: $TimeoutMinutes min)..."

# Use Start-Process so we can reliably wait on Unity.exe, which is a GUI application.
# The & operator does not block on GUI apps in non-interactive PowerShell sessions.
$proc = Start-Process -FilePath $UnityPath `
    -ArgumentList "-batchmode -nographics -projectPath `"$ProjectRoot`" -runTests -testPlatform $TestPlatform -testResults `"$resultsFile`" -logFile `"$logFile`"" `
    -PassThru -NoNewWindow

# Force the Process object to cache its OS handle while the process is still alive. Without this,
# Start-Process -PassThru leaves ExitCode unreadable (blank) afterwards, because .NET never opened
# a handle it can query. That blank value is why the exit code was silently useless before.
try { $null = $proc.Handle } catch { }

$timeoutMs = $TimeoutMinutes * 60 * 1000
$finished  = $proc.WaitForExit($timeoutMs)

$exitCodeKnown = $false
if (-not $finished) {
    Write-Host "[tests] Unity timed out after $TimeoutMinutes minutes - killing process."
    $proc.Kill()
    $unityExitCode = -1
    $exitCodeKnown = $true
} else {
    try {
        $unityExitCode = $proc.ExitCode
        $exitCodeKnown = ($null -ne $unityExitCode) -and ("$unityExitCode" -ne "")
    } catch {
        $exitCodeKnown = $false
    }
}

if ($exitCodeKnown) {
    Write-Host "[tests] Unity exited with code $unityExitCode"
} else {
    # Degrade to the results file rather than failing the run: a hard failure here would break
    # every caller, including CI, on a PowerShell quirk rather than a real test problem.
    Write-Host "[tests] Unity exit code unavailable - falling back to the results file alone."
}

$haveResults = Test-Path $resultsFile

# Dump the log when Unity failed OR when it produced no results at all. The second case is the
# one that used to pass silently: no results means the tests never reported, so the log is the
# only place the reason exists.
if ((($exitCodeKnown -and $unityExitCode -ne 0) -or (-not $haveResults)) -and (Test-Path $logFile)) {
    Write-Host "[tests] --- Last 50 lines of unity.log ---"
    Get-Content $logFile -Tail 50 | ForEach-Object { Write-Host $_ }
    Write-Host "[tests] --- End of log ---"
}

if ($haveResults) {
    $xml    = [xml](Get-Content $resultsFile)
    $run    = $xml.'test-run'
    $passed = $run.passed
    $failed = $run.failed
    $total  = $run.total
    Write-Host "[tests] $passed passed / $failed failed / $total total"
    if ([int]$failed -gt 0) { Start-Process $resultsFile }
    $exitCode = if ([int]$failed -gt 0) { 1 } else { 0 }

    # A results file with zero failures is still NOT a pass if Unity itself failed. The run can
    # abort partway and leave a partial file behind, so the summary above can look clean while
    # whole assemblies never ran. Unity returns non-zero for test failures too, which the failed
    # count already covers; this catches everything else.
    if ($exitCodeKnown -and $unityExitCode -ne 0) {
        Write-Host "[tests] Unity exited non-zero ($unityExitCode) -- treating as FAILED despite the summary above."
        $exitCode = 1
    }
} else {
    Write-Host "[tests] No results file produced at: $resultsFile"
    Write-Host "[tests] Unity wrote no test results, so nothing was verified. Check: $logFile"
    $exitCode = 1
}

if ($exitCode -eq 0) { Write-Host "[tests] PASSED" } else { Write-Host "[tests] FAILED (exit $exitCode)" }
exit $exitCode
