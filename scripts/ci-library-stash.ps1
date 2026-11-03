param(
    [Parameter(Mandatory = $true)][ValidateSet('Restore', 'Save')][string]$Action,
    [Parameter(Mandatory = $true)][string]$Name
)

# Keeps the Android build's Library/ warm on the self-hosted runner.
#
# A cold Android build spends ~11 min importing assets and 60+ min compiling IL2CPP C++,
# and Unity caches both in Library/. But every actions/checkout (default clean: true, which
# runs git clean -ffdx) deletes the git-ignored Library/, and pr-tests.yml shares this same
# workspace -- so clean: false on the release workflows alone would not keep it.
#
# Instead, Save moves Library/ OUT of the checkout into a stash beside it after the build,
# and Restore moves it back IN before the next build. Same volume, so both are instant
# renames, and git clean never sees the stash. One stash per $Name, so builds with different
# scripting defines (internal = DEVELOPMENT_BUILD, prod = none) do not thrash each other.
#
# Never fails the job: a missing or unmovable stash just means a cold (slow) build.

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$library     = Join-Path $ProjectRoot "Library"

# RUNNER_WORKSPACE is the parent of the checkout dir (e.g. C:\actions-runner\_work\<repo>).
$stashRoot = if ($env:RUNNER_WORKSPACE) { $env:RUNNER_WORKSPACE } else { Split-Path -Parent $ProjectRoot }
$stash     = Join-Path $stashRoot "_library-stash\$Name"

function Move-Library([string]$From, [string]$To) {
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Move-Item -LiteralPath $From -Destination $To
            return $true
        } catch {
            Write-Host "[stash] Move attempt $attempt failed: $($_.Exception.Message)"
            Start-Sleep -Seconds 5
        }
    }
    return $false
}

try {
    if ($Action -eq 'Restore') {
        if (-not (Test-Path $stash)) {
            Write-Host "[stash] No stash at $stash - this build starts cold."
            exit 0
        }
        if (Test-Path $library) { Remove-Item -LiteralPath $library -Recurse -Force }
        if (Move-Library $stash $library) {
            Write-Host "[stash] Restored Library from $stash"
        } else {
            Write-Host "::warning::Could not restore Library from $stash - this build starts cold."
        }
    } else {
        if (-not (Test-Path $library)) {
            Write-Host "[stash] No Library to save."
            exit 0
        }

        # Anything still running out of the project (a killed Unity's il2cpp/bee/clang children)
        # or Unity's Gradle daemon (it idles for hours) can hold handles that block the move.
        $holders = Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID } | Where-Object {
            ($_.CommandLine -like "*$ProjectRoot*") -or
            ($_.Name -eq 'java.exe' -and $_.CommandLine -like '*GradleDaemon*' -and $_.CommandLine -like '*AndroidPlayer*')
        }
        if ($holders) {
            Write-Host "[stash] Stopping $(@($holders).Count) process(es) still using the project..."
            $holders | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
            Start-Sleep -Seconds 3
        }

        $parent = Split-Path -Parent $stash
        if (-not (Test-Path $parent)) { New-Item -ItemType Directory $parent | Out-Null }
        if (Test-Path $stash) { Remove-Item -LiteralPath $stash -Recurse -Force }
        if (Move-Library $library $stash) {
            Write-Host "[stash] Saved Library to $stash"
        } else {
            Write-Host "::warning::Could not save Library to $stash - the next build starts cold."
        }
    }
} catch {
    Write-Host "::warning::Library stash $Action failed: $($_.Exception.Message)"
}
exit 0
