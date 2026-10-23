# Runs one AI playtest brief headless with Claude Code (see Playtests/README.md).
# Needs: the Unity Editor open on this project with the MCP for Unity server started, and a
# desktop session with a GPU (screenshots render the Game View). Not for CI or batchmode.
#
#   .\scripts\run-playtest.ps1 -Brief first-session
#   .\scripts\run-playtest.ps1 -Brief automation-loop -McpPort 8080
param(
    [Parameter(Mandatory = $true)][string]$Brief,
    [int]$McpPort = 8080,
    [int]$MaxTurns = 400
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

$Prompt = "Run an AI playtest. Read Playtests/README.md, then follow the brief Playtests/briefs/$Brief.md exactly. " +
          "Write the report to Playtests/reports/ using the README format. Always stop Play Mode before finishing."

Push-Location $ProjectRoot
try {
    claude -p $Prompt `
        --max-turns $MaxTurns `
        --allowedTools "mcp__UnityMCP" "Read" "Glob" "Grep" "Write(Playtests/reports/**)" "Bash(git rev-parse:*)" "Bash(git branch:*)"
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
