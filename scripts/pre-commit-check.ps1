param()

$failed = $false

$staged = git diff --cached --name-only --diff-filter=ACM 2>$null | Where-Object { $_ -match '\.ps1$' }

foreach ($file in $staged) {
    if (-not (Test-Path $file)) { continue }
    $content = Get-Content $file -Raw -Encoding UTF8

    if ($content -match "`u{2013}|`u{2014}|`u{2018}|`u{2019}|`u{201C}|`u{201D}") {
        Write-Host "[pre-commit] ERROR: smart punctuation (em/en dash or curly quotes) in $file" -ForegroundColor Red
        Write-Host "  Replace with plain ASCII (-- or '). These break PowerShell parsing." -ForegroundColor Yellow
        $failed = $true
    }

    if ($content -match '\-runTests\b.*\-quit\b|\-quit\b.*\-runTests\b') {
        Write-Host "[pre-commit] ERROR: -quit combined with -runTests in $file" -ForegroundColor Red
        Write-Host "  -quit terminates Unity before tests complete. Remove -quit from test runs." -ForegroundColor Yellow
        $failed = $true
    }
}

if ($failed) {
    Write-Host "[pre-commit] Commit blocked. Fix the issues above." -ForegroundColor Red
    exit 1
}

exit 0
