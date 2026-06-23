#!/usr/bin/env bash
set -euo pipefail

# Archives the Unity-generated iOS Xcode project, signs it with App Store Connect
# automatic (cloud-managed) signing, and uploads the .ipa to TestFlight.
#
# This is the macOS half of the iOS lane. Unity has already produced the Xcode
# project on the Ubuntu build job; only the Xcode archive/export/upload needs a
# Mac, which keeps the (10x-billed) macOS runner job small. It mirrors the way
# scripts/sign-aab.ps1 handles the Android signing step separately from the build.
#
# Signing uses an App Store Connect API key (.p8) with -allowProvisioningUpdates,
# so Xcode creates/downloads a cloud-managed distribution certificate and profile
# on the fly. There is no .p12 or provisioning profile to manage or rotate.
#
# Required environment variables:
#   APP_STORE_CONNECT_KEY_ID       App Store Connect API key id
#   APP_STORE_CONNECT_ISSUER_ID    App Store Connect API issuer id
#   APP_STORE_CONNECT_API_KEY_P8   base64-encoded contents of the .p8 private key
#   APPLE_TEAM_ID                  Apple developer team id

LOG=archive-ios.log
exec > >(tee "$LOG") 2>&1

for v in APP_STORE_CONNECT_KEY_ID APP_STORE_CONNECT_ISSUER_ID APP_STORE_CONNECT_API_KEY_P8 APPLE_TEAM_ID; do
    if [ -z "${!v:-}" ]; then
        echo "[ios] Missing required environment variable: $v" >&2
        exit 1
    fi
done

PROJECT_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BUILD_DIR="$PROJECT_ROOT/build/iOS"

# Locate the Xcode project. Unity writes it to build/iOS, but tolerate a nested
# layout just in case.
if [ -d "$BUILD_DIR/Unity-iPhone.xcodeproj" ]; then
    PROJ_DIR="$BUILD_DIR"
else
    found="$(find "$BUILD_DIR" -maxdepth 3 -name 'Unity-iPhone.xcodeproj' -type d | head -1 || true)"
    PROJ_DIR="$(dirname "$found")"
fi
if [ ! -d "$PROJ_DIR/Unity-iPhone.xcodeproj" ]; then
    echo "[ios] Could not locate Unity-iPhone.xcodeproj under $BUILD_DIR" >&2
    exit 1
fi
echo "[ios] Xcode project dir: $PROJ_DIR"
cd "$PROJ_DIR"

# The artifact came from a Linux (Docker) build job, so the helper scripts Unity
# emits lose their executable bit in transit. Xcode build phases invoke them.
find "$PROJ_DIR" -type f -name '*.sh' -exec chmod +x {} \; 2>/dev/null || true
find "$PROJ_DIR" -type f -iname 'usymtool*' -exec chmod +x {} \; 2>/dev/null || true
find "$PROJ_DIR" -type f -iname 'process_symbols*' -exec chmod +x {} \; 2>/dev/null || true

# Write the API key where xcodebuild / altool look it up by id.
KEY_DIR="$HOME/.appstoreconnect/private_keys"
mkdir -p "$KEY_DIR"
P8_PATH="$KEY_DIR/AuthKey_${APP_STORE_CONNECT_KEY_ID}.p8"
echo "$APP_STORE_CONNECT_API_KEY_P8" | base64 --decode > "$P8_PATH"

# Declare non-exempt encryption up front so TestFlight does not block each build
# on the export-compliance question. Unity does not expose this PlayerSetting.
INFO_PLIST="$PROJ_DIR/Info.plist"
if [ -f "$INFO_PLIST" ]; then
    /usr/libexec/PlistBuddy -c "Set :ITSAppUsesNonExemptEncryption false" "$INFO_PLIST" 2>/dev/null \
        || /usr/libexec/PlistBuddy -c "Add :ITSAppUsesNonExemptEncryption bool false" "$INFO_PLIST"
fi

# Some Unity packages generate a Podfile; if present, archive the workspace.
BUILD_TARGET_ARGS=(-project "Unity-iPhone.xcodeproj")
if [ -f "Podfile" ]; then
    echo "[ios] Podfile found - running pod install"
    pod install
    BUILD_TARGET_ARGS=(-workspace "Unity-iPhone.xcworkspace")
fi

ARCHIVE_PATH="$PROJECT_ROOT/build/iOS-archive/Unity-iPhone.xcarchive"
EXPORT_DIR="$PROJECT_ROOT/build/iOS-ipa"
mkdir -p "$(dirname "$ARCHIVE_PATH")" "$EXPORT_DIR"

AUTH_ARGS=(
    -allowProvisioningUpdates
    -authenticationKeyPath "$P8_PATH"
    -authenticationKeyID "$APP_STORE_CONNECT_KEY_ID"
    -authenticationKeyIssuerID "$APP_STORE_CONNECT_ISSUER_ID"
)

echo "[ios] Archiving..."
xcodebuild archive \
    "${BUILD_TARGET_ARGS[@]}" \
    -scheme "Unity-iPhone" \
    -configuration Release \
    -archivePath "$ARCHIVE_PATH" \
    -destination "generic/platform=iOS" \
    "${AUTH_ARGS[@]}" \
    CODE_SIGN_STYLE=Automatic \
    DEVELOPMENT_TEAM="$APPLE_TEAM_ID"

# Substitute the team id into a copy of the export options.
EXPORT_OPTS="$PROJECT_ROOT/build/ExportOptions.plist"
sed "s/__APPLE_TEAM_ID__/$APPLE_TEAM_ID/" "$PROJECT_ROOT/scripts/ios/ExportOptions.plist" > "$EXPORT_OPTS"

echo "[ios] Exporting .ipa..."
# On failure, xcodebuild only prints a generic "Cloud signing permission error".
# The actionable detail (the App Store Connect API response: insufficient role,
# certificate limit, capability mismatch, etc.) is written to IDEDistribution
# .standard.log inside a temp .xcdistributionlogs bundle that is otherwise
# discarded. Capture the export output, and on failure dump that detail log.
set +e
xcodebuild -exportArchive \
    -archivePath "$ARCHIVE_PATH" \
    -exportPath "$EXPORT_DIR" \
    -exportOptionsPlist "$EXPORT_OPTS" \
    "${AUTH_ARGS[@]}" > export-archive.log 2>&1
EXPORT_STATUS=$?
set -e
cat export-archive.log

if [ "$EXPORT_STATUS" -ne 0 ]; then
    echo "[ios] exportArchive failed with status $EXPORT_STATUS; dumping distribution logs:"
    # standard.log only records the step pipeline; the raw App Store Connect API
    # refusal lives in a sibling file (critical.log / *-error logs), so dump every
    # file in the bundle.
    DIST_LOGS="$(grep -o '/var/folders/[^"]*\.xcdistributionlogs' export-archive.log | head -1 || true)"
    if [ -n "$DIST_LOGS" ] && [ -d "$DIST_LOGS" ]; then
        echo "[ios] Distribution log bundle: $DIST_LOGS"
        find "$DIST_LOGS" -type f | while read -r f; do
            echo "===== $f ====="
            cat "$f"
            echo
        done
    else
        echo "[ios] Could not locate the .xcdistributionlogs bundle; searching TMPDIR:"
        find "${TMPDIR:-/var/folders}" -path '*.xcdistributionlogs/*' -type f 2>/dev/null | while read -r f; do
            echo "===== $f ====="
            cat "$f"
            echo
        done
    fi
    exit "$EXPORT_STATUS"
fi

IPA="$(find "$EXPORT_DIR" -name '*.ipa' | head -1 || true)"
if [ -z "$IPA" ]; then
    echo "[ios] No .ipa produced" >&2
    exit 1
fi
echo "[ios] Built $IPA"

# altool ships with Xcode and reads the .p8 from ~/.appstoreconnect/private_keys
# by key id. It is the lightest built-in uploader; if Apple removes it, swap to
# Transporter (iTMSTransporter) or fastlane pilot.
echo "[ios] Uploading to TestFlight..."
# altool has been observed to exit 0 even when App Store validation REJECTS the
# build (e.g. a 409 "Missing app icon"), which would let this job report success
# on a build that never reaches TestFlight. Capture the output and treat a
# non-zero status OR a failure marker in the log as a hard failure so the lane
# goes red. `tee` keeps the output visible in the run log; PIPESTATUS reads
# altool's real exit code past the pipe.
set +e
xcrun altool --upload-app \
    --type ios \
    --file "$IPA" \
    --apiKey "$APP_STORE_CONNECT_KEY_ID" \
    --apiIssuer "$APP_STORE_CONNECT_ISSUER_ID" 2>&1 | tee upload.log
UPLOAD_STATUS=${PIPESTATUS[0]}
set -e

if [ "$UPLOAD_STATUS" -ne 0 ] || grep -qiE "UPLOAD FAILED|Validation failed" upload.log; then
    echo "[ios] TestFlight upload FAILED (altool status $UPLOAD_STATUS). See the errors above." >&2
    exit 1
fi

echo "[ios] Upload complete."
