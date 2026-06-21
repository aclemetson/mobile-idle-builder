# CI/CD Setup Guide

One-time setup steps to activate the GitHub Actions pipeline.

## Branch → Track Model

The pipeline maps each branch to a Google Play track:

| Branch event | Workflow | Track / action |
|--------------|----------|----------------|
| PR merged **into** `release/**` | `release-internal.yml` | tests → `.aab` → Google Play **internal** |
| `release/**` merged into `develop` | `bump-version.yml` | bump minor version on `develop` |
| Manual (`workflow_dispatch`) | `prod-release.yml` | `.aab` → Google Play **production** (draft) — *future, parked* |
| Any PR (`develop` / `main` / `release/**`) | `pr-tests.yml` | edit + play mode tests (+ version-code guard on release PRs) |

Builds and tests run on the self-hosted Windows runner (zero cloud minutes). Only the
lightweight version-code guard and the Play upload run on `ubuntu-latest` (the
`r0adkll/upload-google-play` action is Linux/Docker only).

### Release Branch → Develop: Automatic Version Bump

When a `release/**` branch is merged into `develop`, `bump-version.yml`:

1. Increments the **minor** version and resets the **patch** to `0` (e.g. `0.1.43` → `0.2.0`)
2. Commits the change back to `develop` as `github-actions[bot]` with `[skip ci]`

It intentionally does **not** touch `AndroidBundleVersionCode` — see below.

## Version Code Rules

`AndroidBundleVersionCode` must be **strictly increasing and globally unique for the
lifetime of the app**. Google Play permanently rejects a code it has seen before, so it
must never be reset. It is owned by the local pre-commit hook (`scripts/bump-version.ps1`),
not by the minor-version bump.

`scripts/check-version-code.py` enforces this. It authenticates with the Google Play
service account, lists every uploaded bundle's version code (read-only — it opens an edit
and deletes it, never commits), and fails unless the committed value is greater than both
the Google Play maximum and the version code on the base release branch tip. It runs:

- as a **pre-merge check** on PRs into `release/**` (`pr-tests.yml`) — gives a red check so
  you bump the code before merging, and
- as the **first job** of `release-internal.yml` — a fast backstop so a bad code fails
  before any Unity build minutes are spent.

If a duplicate code somehow merges, the build fails on the guard — push a one-line
`AndroidBundleVersionCode` bump to recover. Optionally enable branch-protection
"Require branches to be up to date before merging" on `release/**` to force stale PRs to
rebase and re-run the guard (needs GitHub Pro on private repos).

## 1. Self-Hosted Runner (Windows build machine)

The build and test jobs run on your local machine to avoid consuming GitHub Actions cloud minutes.

1. Go to: **GitHub repo → Settings → Actions → Runners → New self-hosted runner**
2. Select **Windows** platform
3. Follow the download + configure instructions shown
4. When prompted for labels, use: `self-hosted,windows,unity`
5. Install as a Windows service so it starts automatically:
   ```powershell
   .\svc.cmd install
   .\svc.cmd start
   ```
6. Set the `UNITY_PATH` environment variable on the runner machine if Unity is not installed at the default path:
   ```
   C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe
   ```
   To set permanently: System → Advanced system settings → Environment Variables → add `UNITY_PATH`.

## 2. PR Tests

Edit and play mode tests run directly on the self-hosted runner via `scripts/test-local.ps1`.
No license secrets needed — Unity is already activated on the machine.

## 3. Android Keystore Secret

The keystore file is gitignored. Encode it to base64 and store it as a secret.

```powershell
# Run from the project root
$bytes = [System.IO.File]::ReadAllBytes("secrets\user.keystore")
[System.Convert]::ToBase64String($bytes) | Set-Clipboard
```

Add to GitHub Secrets:
- `ANDROID_KEYSTORE_BASE64` — paste the base64 string from clipboard
- `ANDROID_KEYSTORE_PASS` — keystore password
- `ANDROID_KEY_ALIAS` — `mobile-idle-builder`
- `ANDROID_KEY_PASS` — key password

## 4. Google Play (Internal Testing)

1. In **Google Play Console**: ensure the app has at least one release in any track (required to use the API)

2. Create a service account:
   - Google Play Console → Setup → API access → Create new service account
   - Link to a Google Cloud project → Create key (JSON type)
   - Grant the service account **Release manager** permission in Play Console

3. Add to GitHub Secrets:
   - `GOOGLE_PLAY_JSON_KEY` — paste the full JSON content of the service account key file

This same service account is used by both the internal upload and the version-code guard.

## 5. GitHub Actions Permissions

Ensure the repo allows Actions to create tags, releases, and the version bump commit:
- Settings → Actions → General → Workflow permissions → **Read and write permissions**

## Secrets Summary

| Secret | Source | Used By |
|--------|--------|---------|
| `ANDROID_KEYSTORE_BASE64` | base64 of `secrets/user.keystore` | builds |
| `ANDROID_KEYSTORE_PASS` | keystore password | builds |
| `ANDROID_KEY_ALIAS` | `mobile-idle-builder` | builds |
| `ANDROID_KEY_PASS` | key password | builds |
| `GOOGLE_PLAY_JSON_KEY` | GCP service account JSON | internal upload + version guard + prod (future) |

## Local Testing

Run Unity edit mode tests locally without triggering CI:

```powershell
.\scripts\test-local.ps1
```

Override Unity path if needed:
```powershell
.\scripts\test-local.ps1 -UnityPath "C:\path\to\Unity.exe"
```

Results are saved to `TestResults\editmode.xml`.
