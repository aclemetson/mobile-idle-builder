param(
    [Parameter(Mandatory = $true)][string]$AabPath,
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe"
)

# Signs an Android App Bundle with jarsigner using the upload keystore.
#
# Unity's batchmode build does not reliably apply the custom keystore to the
# .aab (it emits an unsigned bundle), so Google Play rejects the upload with
# "All uploaded bundles must be signed." We sign the bundle explicitly here,
# which is exactly what that error recommends. Runs on the Windows build runner
# where secrets/user.keystore already exists.
#
# Required environment variables:
#   ANDROID_KEYSTORE_PASS  store password
#   ANDROID_KEY_ALIAS      key alias to sign with
#   ANDROID_KEY_PASS       key (alias) password

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot

if ($env:UNITY_PATH) { $UnityPath = $env:UNITY_PATH }

if (-not (Test-Path $AabPath)) {
    Write-Error "AAB not found at: $AabPath"
    exit 1
}

foreach ($name in @('ANDROID_KEYSTORE_PASS', 'ANDROID_KEY_ALIAS', 'ANDROID_KEY_PASS')) {
    if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($name))) {
        Write-Error "Missing required environment variable: $name"
        exit 1
    }
}

# Locate jarsigner. Prefer Unity's bundled OpenJDK (JDK 17): it is modern enough
# to read the PKCS12 keystore Unity produces (HmacPBESHA256), whereas a stale
# JAVA_HOME (e.g. JDK 11) fails with "Algorithm HmacPBESHA256 not available".
# Fall back to JAVA_HOME, then PATH.
function Find-Jarsigner {
    $editorDir = Split-Path -Parent $UnityPath
    $bundled = Join-Path $editorDir "Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\jarsigner.exe"
    if (Test-Path $bundled) { return $bundled }
    if ($env:JAVA_HOME) {
        $p = Join-Path $env:JAVA_HOME "bin\jarsigner.exe"
        if (Test-Path $p) { return $p }
    }
    $cmd = Get-Command jarsigner -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "jarsigner not found (checked Unity bundled OpenJDK, JAVA_HOME, and PATH)."
}

$jarsigner = Find-Jarsigner
$keystore  = Join-Path $ProjectRoot "secrets\user.keystore"

if (-not (Test-Path $keystore)) {
    Write-Error "Keystore not found at: $keystore"
    exit 1
}

Write-Host "[sign] jarsigner: $jarsigner"
Write-Host "[sign] Signing $AabPath ..."

# Passwords are read from the environment by jarsigner (:env modifier) so they
# never appear on the command line / process list. The alias is not secret.
& $jarsigner -keystore $keystore `
    -storepass:env ANDROID_KEYSTORE_PASS `
    -keypass:env ANDROID_KEY_PASS `
    -sigalg SHA256withRSA -digestalg SHA-256 `
    "$AabPath" $env:ANDROID_KEY_ALIAS
if ($LASTEXITCODE -ne 0) {
    Write-Error "jarsigner signing failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "[sign] Verifying signature ..."
& $jarsigner -verify "$AabPath"
if ($LASTEXITCODE -ne 0) {
    Write-Error "jarsigner verification failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "[sign] AAB signed and verified."
