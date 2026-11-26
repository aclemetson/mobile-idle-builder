#!/usr/bin/env bash
# Builds, tests and deploys the PvE Cloud Code module (CloudCode/PveModule) to one UGS environment.
#
# Used by .github/workflows/release-internal.yml when integration merges into release/** (the module ships
# to the development environment with the internal Android build). Also runs locally:
#
#   UGS_CLI_SERVICE_KEY_ID=... UGS_CLI_SERVICE_SECRET_KEY=... UGS_PROJECT_ID=... \
#     ./scripts/deploy-cloud-code.sh <environment-id> [--dry-run]
#
# Needs: .NET 9 SDK, the UGS CLI (npm i -g ugs) on PATH, python3, zip. The service account needs the
# "Cloud Code Editor" project role (docs/runbooks/pve-backend-setup.md, "CI deploy").
#
# Same build as the Editor's Deployment window: dotnet publish -c Release -r linux-x64, the output zipped into
# PveModule.ccm (the module's name is the archive's name). The module embeds Assets/Data/game_data.json and
# compiles the shared PvE rules, so it always matches the client built from the same commit.
set -euo pipefail

ENV_ARG="${1:?usage: deploy-cloud-code.sh <environment-name | environment-id> [--dry-run]}"
DRY_RUN="${2:-}"
: "${UGS_CLI_SERVICE_KEY_ID:?UGS_CLI_SERVICE_KEY_ID is not set}"
: "${UGS_CLI_SERVICE_SECRET_KEY:?UGS_CLI_SERVICE_SECRET_KEY is not set}"
: "${UGS_PROJECT_ID:?UGS_PROJECT_ID is not set}"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SLN_DIR="$ROOT/CloudCode/PveModule"
OUT="${CCM_OUT_DIR:-$(mktemp -d)}"
mkdir -p "$OUT"

echo "== Testing the module"
dotnet test "$SLN_DIR/PveModule.sln" -c Release --nologo

echo "== Publishing (linux-x64)"
rm -rf "$OUT/publish" "$OUT/PveModule.ccm"
dotnet publish "$SLN_DIR/PveModule/PveModule.csproj" -c Release -r linux-x64 --self-contained false \
  -o "$OUT/publish" --nologo
(cd "$OUT/publish" && zip -qr "$OUT/PveModule.ccm" .)
echo "Built $OUT/PveModule.ccm ($(du -h "$OUT/PveModule.ccm" | cut -f1))"

if [[ "$ENV_ARG" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
  ENV_ID="$ENV_ARG"
  echo "== Resolving the environment name for $ENV_ID"
  ENV_NAME="$(ugs env list --project-id "$UGS_PROJECT_ID" --json | python3 -c '
import json, sys
env_id = sys.argv[1].lower()
def walk(o):
    if isinstance(o, dict):
        low = {str(k).lower(): v for k, v in o.items()}
        if str(low.get("id", "")).lower() == env_id and low.get("name"):
            print(low["name"]); sys.exit(0)
        for v in o.values(): walk(v)
    elif isinstance(o, list):
        for v in o: walk(v)
walk(json.load(sys.stdin))
sys.exit("environment id not found in this project")
  ' "$ENV_ID")"
else
  ENV_NAME="$ENV_ARG"
fi
echo "Environment: $ENV_NAME"

DEPLOY_ARGS=(deploy "$OUT/PveModule.ccm" --project-id "$UGS_PROJECT_ID" --environment-name "$ENV_NAME" --json)
if [ "$DRY_RUN" = "--dry-run" ]; then DEPLOY_ARGS+=(--dry-run); echo "== Dry run"; fi

echo "== Deploying"
ugs "${DEPLOY_ARGS[@]}" | tee "$OUT/deploy-result.json"

# The CLI lists each file under a status; fail the job if anything is listed as failed.
python3 - "$OUT/deploy-result.json" <<'PY'
import json, sys
try:
    data = json.load(open(sys.argv[1]))
except ValueError:
    sys.exit("unexpected deploy output (not JSON)")
failed = []
def walk(o):
    if isinstance(o, dict):
        for k, v in o.items():
            if str(k).lower() == "failed" and isinstance(v, list):
                failed.extend(v)
            walk(v)
    elif isinstance(o, list):
        for v in o:
            walk(v)
walk(data)
if failed:
    sys.exit("deploy failed: " + json.dumps(failed)[:800])
print("Deploy OK")
PY
