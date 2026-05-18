param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe",
    [string]$ResultsPath = "TestResults\editmode.xml"
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path $UnityPath)) {
    Write-Error "Unity not found at: $UnityPath`nOverride with: .\scripts\test-local.ps1 -UnityPath 'C:\path\to\Unity.exe'"
    exit 1
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

Write-Host "[tests] Running edit mode tests..."
& $UnityPath -batchmode -nographics `
    -projectPath $ProjectRoot `
    -runTests -testPlatform editmode `
    -testResults $resultsFile `
    -logFile $logFile

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
