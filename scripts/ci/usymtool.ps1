# =============================================================================
# usymtool.ps1 - Upload Unity debug symbols (Windows Host)
#
# Supports: windows, android
# For a macOS host (ios, macos, android), use usymtool.sh
# =============================================================================

$ErrorActionPreference = "Stop"

# █████████████████████████████████████████████████████████████████████████████
# █  USER CONFIGURATION - Set these values before running                    █
# █████████████████████████████████████████████████████████████████████████████

# Platform to upload symbols for (windows, android)
$PLATFORM = "android"

# Set to $true if your build uses the IL2CPP scripting backend.
# Enables C# line numbers in exception reports.
$USE_IL2CPP = $true

# Unity project ID (from Unity Dashboard)
# Example: "12345678-abcd-1234-5678-12345678abcd"
$UNITY_PROJECT_ID = "<your-unity-project-id>"

# Service account auth header (from Unity Dashboard > Service Accounts)
# Example: "Basic dXNlcm5hbWU6cGFzc3dvcmQ="
$UNITY_SERVICE_ACCOUNT_AUTH_HEADER = "<your-service-account-auth-header>"

# Path to the Unity Editor installation
$UNITY_EDITOR_PATH = "C:\Program Files\Unity\Hub\Editor\6000.3.10f1"

# Path to the Unity project
# Example: "C:\Users\yourname\UnityProjects\MyGame"
$UNITY_PROJECT_PATH = "<your-unity-project-path>"

# Path to the build output directory
# For Windows, this is the folder containing the built app.
# Not needed for Android (leave blank).
# Example: "C:\Users\yourname\UnityProjects\MyGame\Build"
$BUILD_OUTPUT_PATH = "<your-build-output-path>"

# Name of the build (used to locate the _BackUpThisFolder directory)
# For Windows, this is typically the product name.
# Not needed for Android (leave blank).
# Example: "MyGame"
$BUILD_NAME = "<your-build-name>"

# █████████████████████████████████████████████████████████████████████████████
# █  ADVANCED - You should not normally need to change anything below        █
# █████████████████████████████████████████████████████████████████████████████

# Override any derived path (leave empty to use platform defaults)
$USYMTOOL_PATH_OVERRIDE = ""
$SYMBOL_PATH_OVERRIDE = ""
$IL2CPP_OUTPUT_PATH_OVERRIDE = ""
$IL2CPP_FILE_ROOT_OVERRIDE = ""
$LOG_PATH_OVERRIDE = ""
$FILTER_OVERRIDE = ""

# Service URLs
$USYM_UPLOAD_AUTH_TOKEN_URL = "https://services.unity.com/api/cloud-diagnostics/crash-service/v1/projects"
$USYM_UPLOAD_URL_SOURCE = "https://perf-events.cloud.unity3d.com/url"

# =============================================================================
# Derive platform-specific defaults
# =============================================================================
$BACKUP_FOLDER = Join-Path $BUILD_OUTPUT_PATH "${BUILD_NAME}_BackUpThisFolder_ButDontShipItWithYourGame"

switch ($PLATFORM) {
    "windows" {
        $DEFAULT_USYMTOOL_PATH = Join-Path $UNITY_EDITOR_PATH "Editor\Data\Tools\usymtool.exe"
        $DEFAULT_SYMBOL_PATH = $BACKUP_FOLDER
        $DEFAULT_IL2CPP_OUTPUT_PATH = Join-Path $BACKUP_FOLDER "il2cppOutput"
        $DEFAULT_IL2CPP_FILE_ROOT = Join-Path $UNITY_PROJECT_PATH "Library\Bee\artifacts\WinPlayerBuildProgram\il2cppOutput\cpp"
        $DEFAULT_LOG_PATH = Join-Path $env:LOCALAPPDATA "Unity\Editor\symbol_upload.log"
        $DEFAULT_FILTER = "GameAssembly.pdb"
    }
    "android" {
        $DEFAULT_USYMTOOL_PATH = Join-Path $UNITY_EDITOR_PATH "Editor\Data\Tools\usymtool.exe"
        $DEFAULT_SYMBOL_PATH = Join-Path $UNITY_PROJECT_PATH "Library\Bee\Android\Prj\IL2CPP\Gradle\unityLibrary\symbols"
        $DEFAULT_IL2CPP_OUTPUT_PATH = Join-Path $UNITY_PROJECT_PATH "Library\Bee\Android\Prj\IL2CPP\Il2CppBackup\il2cppOutput"
        $DEFAULT_IL2CPP_FILE_ROOT = Join-Path $UNITY_PROJECT_PATH "Library\Bee\artifacts\Android\il2cppOutput\cpp"
        $DEFAULT_LOG_PATH = Join-Path $env:LOCALAPPDATA "Unity\Editor\symbol_upload.log"
        $DEFAULT_FILTER = ""
    }
    default {
        Write-Error "Unknown PLATFORM '$PLATFORM'. Must be one of: windows, android"
        Write-Error "For macOS (ios, macos, android), use usymtool.sh"
        exit 1
    }
}

# Apply overrides
$USYMTOOL_PATH = if ($USYMTOOL_PATH_OVERRIDE) { $USYMTOOL_PATH_OVERRIDE } else { $DEFAULT_USYMTOOL_PATH }
$SYMBOL_PATH = if ($SYMBOL_PATH_OVERRIDE) { $SYMBOL_PATH_OVERRIDE } else { $DEFAULT_SYMBOL_PATH }
$IL2CPP_OUTPUT_PATH = if ($IL2CPP_OUTPUT_PATH_OVERRIDE) { $IL2CPP_OUTPUT_PATH_OVERRIDE } else { $DEFAULT_IL2CPP_OUTPUT_PATH }
$IL2CPP_FILE_ROOT = if ($IL2CPP_FILE_ROOT_OVERRIDE) { $IL2CPP_FILE_ROOT_OVERRIDE } else { $DEFAULT_IL2CPP_FILE_ROOT }
$LOG_PATH = if ($LOG_PATH_OVERRIDE) { $LOG_PATH_OVERRIDE } else { $DEFAULT_LOG_PATH }
$FILTER = if ($FILTER_OVERRIDE) { $FILTER_OVERRIDE } else { $DEFAULT_FILTER }

# =============================================================================
# Validate paths
# =============================================================================
$errors = @()
if (-not (Test-Path $USYMTOOL_PATH)) { $errors += "usymtool not found at: $USYMTOOL_PATH" }
if (-not (Test-Path $SYMBOL_PATH)) { $errors += "Symbol path not found: $SYMBOL_PATH" }
if ($USE_IL2CPP) {
    if (-not (Test-Path $IL2CPP_OUTPUT_PATH)) { $errors += "IL2CPP output path not found: $IL2CPP_OUTPUT_PATH" }
    if ($IL2CPP_FILE_ROOT -and -not (Test-Path $IL2CPP_FILE_ROOT)) {
        $errors += "IL2CPP file root not found: $IL2CPP_FILE_ROOT"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "Path validation failed:" -ForegroundColor Red
    foreach ($err in $errors) { Write-Host "  - $err" -ForegroundColor Red }
    Write-Error "Path validation failed. See details above."
    exit 1
}

# =============================================================================
# Fetch auth token
# =============================================================================
Write-Host "Fetching auth token for project $UNITY_PROJECT_ID..."

try {
    $response = Invoke-RestMethod `
        -Uri "$USYM_UPLOAD_AUTH_TOKEN_URL/$UNITY_PROJECT_ID/symbols/token" `
        -Headers @{ Authorization = $UNITY_SERVICE_ACCOUNT_AUTH_HEADER } `
        -Method Get
} catch {
    Write-Error "Failed to fetch auth token. Check project ID and auth header."
    Write-Error $_.Exception.Message
    exit 1
}

$USYM_UPLOAD_AUTH_TOKEN = $response.AuthToken
if (-not $USYM_UPLOAD_AUTH_TOKEN) {
    Write-Error "Failed to extract AuthToken from response: $($response | ConvertTo-Json)"
    exit 1
}

Write-Host "Auth token acquired successfully."

# =============================================================================
# Build and run usymtool command
# =============================================================================
$env:USYM_UPLOAD_AUTH_TOKEN = $USYM_UPLOAD_AUTH_TOKEN
$env:USYM_UPLOAD_URL_SOURCE = $USYM_UPLOAD_URL_SOURCE

$args = @(
    "-symbolPath", $SYMBOL_PATH,
    "-log", $LOG_PATH
)
if ($FILTER) {
    $args += @("-filter", $FILTER)
}
if ($USE_IL2CPP) {
    $args += @("-il2cppOutputPath", $IL2CPP_OUTPUT_PATH, "-il2cppFileRoot", $IL2CPP_FILE_ROOT)
}

Write-Host ""
Write-Host "Running usymtool ($PLATFORM):"
Write-Host "  $USYMTOOL_PATH $($args -join ' ')"
Write-Host ""

& $USYMTOOL_PATH @args
