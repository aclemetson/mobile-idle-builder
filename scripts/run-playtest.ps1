# Runs one AI playtest brief headless with Claude Code (see Playtests/README.md).
# Needs: the Unity Editor open on this project with the MCP for Unity server started, and a
# desktop session with a GPU (screenshots render the Game View). Not for CI or batchmode.
#
#   .\scripts\run-playtest.ps1 -Brief casual-idler
#   .\scripts\run-playtest.ps1 -Brief active-grinder -Minutes 20
# Scenario briefs: stability-sweep, casual-idler, active-grinder, whale-speedrun.
param(
    [Parameter(Mandatory = $true)][string]$Brief,
    [int]$McpPort = 8080,
    [int]$Minutes = 0,
    [int]$MaxTurns = 3000
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$BriefPath = Join-Path $ProjectRoot "Playtests\briefs\$Brief.md"

if (-not (Test-Path $BriefPath)) {
    $names = Get-ChildItem (Join-Path $ProjectRoot "Playtests\briefs") -Filter *.md |
        Where-Object { $_.Name -notlike '_*' } | ForEach-Object { $_.BaseName }
    Write-Error "No brief '$Brief'. Available: $($names -join ', ')"
}

if (-not (Get-Process -Name Unity -ErrorAction SilentlyContinue)) {
    Write-Error "Unity Editor is not running. Open the project and start the MCP server first."
}

$client = New-Object System.Net.Sockets.TcpClient
try {
    $client.Connect('127.0.0.1', $McpPort)
} catch {
    Write-Error "Nothing is listening on 127.0.0.1:$McpPort. In Unity: Window > MCP for Unity > Start Server."
} finally {
    $client.Dispose()
}

if (-not (Get-Command claude -ErrorAction SilentlyContinue)) {
    Write-Error "The claude CLI is not on PATH."
}

$ReportDir = Join-Path $ProjectRoot "Playtests\reports"
New-Item -ItemType Directory -Force $ReportDir | Out-Null

# The /playtest skill picks the driver and reviewer models itself; the orchestrator only routes,
# so it runs on the cheapest model.
$Prompt = "/playtest $Brief"
if ($Minutes -gt 0) { $Prompt += " for $Minutes minutes" }

Push-Location $ProjectRoot
try {
    claude -p $Prompt `
        --model haiku `
        --max-turns $MaxTurns `
        --allowedTools "mcp__UnityMCP" "Agent" "ToolSearch" "Read" "Glob" "Grep" "Write(Playtests/reports/**)" `
            "Bash(git rev-parse:*)" "Bash(git branch:*)" "Bash(date:*)" "Bash(python scripts/benchmark-report.py:*)"
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
