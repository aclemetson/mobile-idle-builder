# CI/CD Setup Guide

One-time setup steps to activate the GitHub Actions pipeline.

## Branch → Track Model

The pipeline maps each branch to a Google Play track:

| Branch event | Workflow | Track / action |
|--------------|----------|----------------|
| Any PR (`develop` / `main` / `release/**`) | `pr-tests.yml` | edit + play mode tests (+ version-code guard on release PRs) |
| PR merged **into** `release/**` | `release-internal.yml` | `.aab` → Google Play **internal** |
| `release/**` merged into `develop` | `bump-version.yml` | bump minor version on `develop` |
| Manual (`workflow_dispatch`) | `prod-release.yml` | `.aab` → Google Play **production** (draft) — *future, parked* |

Tests and the version-code guard run **once**, as required checks on the PR (`pr-tests.yml`)
before the merge is allowed. The merge then triggers `release-internal.yml`, which only
builds and uploads — it does not re-run tests or the guard.

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
the Google Play maximum and the version code on the base release branch tip. It runs as a
**pre-merge check** on PRs into `release/**` (`pr-tests.yml`) — a red check tells you to bump
the code before merging.

The Play upload in `release-internal.yml` is the final backstop: if a duplicate code somehow
reaches it (e.g. two PRs merged back-to-back without the guard re-running), Google Play
rejects the upload, so a bad code can never silently ship. Recover by pushing a one-line
`AndroidBundleVersionCode` bump. To close that window entirely, enable branch-protection
"Require branches to be up to date before merging" on `release/**` so stale PRs must rebase
and re-run the guard (needs GitHub Pro on private repos).

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

### Self-Hosted Runner Approval Gate

Because the runner executes PR code on a physical machine, `pr-tests.yml` requires an
explicit approval before the `test` job runs on it. Protections, from outer to inner:

1. **Fork check** — `if: github.event.pull_request.head.repo.full_name == github.repository`
   on both the gate and the `test` job. Fork PRs never reach the runner.
2. **`self-hosted-approval` environment** — a protected GitHub Environment whose only
   protection rule is a required reviewer (the repo owner). The `approval-gate` job is bound
   to it, so it **pauses in "Waiting"** until the owner clicks **Approve** in the PR / Actions
   UI. It runs on `ubuntu-latest`, so the self-hosted machine is untouched while waiting.
3. **Owner bypass** — the gate's `if` includes `author_association != 'OWNER'`, so the owner's
   own PRs **skip** the gate and run immediately. The `test` job depends on the gate via
   `needs` + `always() && (result == 'success' || result == 'skipped')`.

Net behavior:

| PR author | Gate | Tests on runner |
|-----------|------|-----------------|
| Repo owner | skipped | run immediately |
| Any other same-repo author | waits for owner approval | run only after **Approve** |
| Fork | n/a (blocked by fork check) | never |

This applies to **all** PRs that hit the runner (`develop`, `main`, `release/**`) — the gate
has no branch filter. The `version-guard` job is unaffected; it runs only on `ubuntu-latest`.

**One-time setup** (already done; recreate if the environment is deleted):

```bash
# Add the owner (user id from `gh api users/<login> --jq .id`) as required reviewer.
gh api -X PUT repos/<owner>/<repo>/environments/self-hosted-approval \
  -F "reviewers[][type]=User" -F "reviewers[][id]=<owner-user-id>"
```

Or in the UI: **Settings → Environments → New environment → `self-hosted-approval` →
Required reviewers → add yourself**.

**Defense-in-depth (settings only):** set **Settings → Actions → General → Fork pull request
workflows from outside collaborators** to **"Require approval for all external contributors"**.
This backstops the fork check above for any future workflow edit that drops it.

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
