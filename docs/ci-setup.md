# CI/CD Setup Guide

One-time setup steps to activate the GitHub Actions pipeline.

## Branch → Track Model

The pipeline maps each branch to a Google Play track:

| Branch event | Workflow | Track / action |
|--------------|----------|----------------|
| Any PR (`develop` / `main` / `release/**`) | `pr-tests.yml` | edit + play mode tests (+ version-code guard on release PRs) |
| PR merged **into** `release/**` | `release-internal.yml` | `.aab` → Google Play **internal** |
| Push to `develop` (i.e. a `release/**` merged down) | `release-ios.yml` | `.ipa` → **TestFlight** |
| `release/**` branch **created** | `bump-version.yml` | stamp version from the branch name onto that branch |
| Manual (`workflow_dispatch`) | `prod-release.yml` | `.aab` → Google Play **production** (draft) — *future, parked* |

iOS builds far less often than Android on purpose: Android ships to its internal track on
every merge into `release/**`, while iOS ships to TestFlight only when a release branch is
merged down into `develop` (roughly once per release cycle). They build from points one
merge apart, but it is the same code.

Tests and the version-code guard run **once**, as required checks on the PR (`pr-tests.yml`)
before the merge is allowed. The merge then triggers `release-internal.yml`, which only
builds and uploads — it does not re-run tests or the guard.

Builds and tests run on the self-hosted Windows runner (zero cloud minutes). Only the
lightweight version-code guard and the Play upload run on `ubuntu-latest` (the
`r0adkll/upload-google-play` action is Linux/Docker only).

### Release Branch Created: Automatic Version Stamp

When a `release/**` branch is **created**, `bump-version.yml` derives the version from the
branch name and commits it to that branch as `github-actions[bot]`:

| Branch | `bundleVersion` |
|--------|-----------------|
| `release/0.4` | `0.4.0` |
| `release/0.4.1` | `0.4.1` |

Anything else (`release/foo`) fails the workflow rather than guessing. The stamp reaches
`develop` through the normal release PR — nothing ever pushes to `develop` directly.

It intentionally does **not** touch `AndroidBundleVersionCode` — see below.

Two constraints pin this design; both are easy to regress:

- **Never push to `develop`.** It is protected (PR + 1 review) *and* carries the "Unity
  Tests" ruleset. Both are bypassable only by a **repository admin**, and
  `github-actions[bot]` is not one — no `permissions:` grant changes that, because that
  key controls API scope, not rule bypass. The workflow originally bumped `develop` after
  the release PR merged and was rejected with `GH013: Repository rule violations`.
- **Stamp at branch creation, not on the release PR.** A push made with `GITHUB_TOKEN`
  does not trigger workflows. Committing the bump once the PR exists would move the PR head
  to a commit that "Unity Tests" never runs on, leaving the required check stuck on
  *Expected* forever. For the same reason the commit message must **not** carry `[skip ci]`
  — GitHub honours that marker on a PR head commit and would skip `pr-tests`.

Because the stamp now lands *before* the release ships, a release branch carries its own
version. Under the old post-merge design the bump minted the *next* version, so the branch
named `release/0.4` would have shipped the version created when `release/0.3` merged —
release 0.3 in fact went to the stores as `0.2.199`.

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

## 5. iOS / TestFlight

The iOS lane (`release-ios.yml`) runs on **GitHub-hosted** runners (not the self-hosted
Windows machine -- iOS archiving requires macOS). To keep the expensive macOS minutes
small, it splits the work, following GameCI's recommended iOS flow:

1. **`build` (ubuntu-latest)** -- `game-ci/unity-builder@v4` generates the Xcode project.
   Unity activation works here via the Docker image (Personal license). Output is uploaded
   as the `ios-xcode-project` artifact.
2. **`release` (macos-latest)** -- downloads the artifact and runs
   `scripts/archive-upload-ios.sh`: `xcodebuild archive` + `-exportArchive` +
   `xcrun altool --upload-app` to TestFlight. Signing is **automatic / cloud-managed**
   via an App Store Connect API key (`-allowProvisioningUpdates`) -- no `.p12` or
   provisioning profile to manage.

> Cost note: macOS minutes bill at a **10x** multiplier. Triggering only on `develop`
> keeps this to ~once per release. The Library cache + the Ubuntu/macOS split keep each
> run modest, but this lane is not $0 like Android.

### CocoaPods: who owns the Podfile

**The Mobile Dependency Resolver (EDM4U) owns the Podfile. Never write it yourself.**

EDM4U collects every `<iosPod>` across the `*Dependencies.xml` files and generates the
Podfile at `PostProcessBuild` order **40**. `archive-upload-ios.sh` then runs `pod install`
on the macOS runner and archives the `.xcworkspace`. Today that means three pods:

| Pod | Declared in |
|-----|-------------|
| `GoogleSignIn` | `Assets/GoogleSignIn/Editor/GoogleSignInDependencies.xml` |
| `IronSourceSDK` | `Assets/LevelPlay/Editor/IronSourceSDKDependencies.xml` |
| `IronSourceUnityAdsAdapter` | `Assets/LevelPlay/Editor/ISUnityAdsAdapterDependencies.xml` |

EDM4U already emits `platform :ios` (from the PlayerSettings deployment target) and
`use_frameworks! :linkage => :static` — its default since 1.2.170 — so a hand-written
Podfile adds nothing and can only lose pods.

`IOSGoogleSignInPostProcess` used to overwrite the generated Podfile with a
GoogleSignIn-only one at order 100. That silently deleted both IronSource pods and the
archive died on the macOS runner half an hour later with `'IronSource/LPMAdInfo.h' file not
found`. If you must modify the Podfile, **append** to it between orders **40 and 50** (the
window EDM4U documents), after generation and before `pod install`.

`IOSPodfileVerifier` (order 45) now guards this: it re-reads the `<iosPod>` entries from the
`*Dependencies.xml` files and fails the build if any of them is absent from the generated
Podfile, so a dropped pod fails the cheap Ubuntu job with the pod named instead of the 10x
macOS one.

### Build number

`BuildScript.BuildIOS()` sets `CFBundleVersion` from the existing
`AndroidBundleVersionCode`, so one monotonic counter feeds both stores. There is no
separate pre-merge iOS guard; TestFlight rejecting a duplicate build number is the
backstop (the same role the Play upload plays for Android).

### One-time Unity license (`.ulf`) for game-ci

The hosted Ubuntu runner is not pre-activated, so game-ci needs a Personal license file:

1. Add a temporary workflow step (or run the `game-ci/unity-request-activation-file@v2`
   action) to produce a `Unity_v6000.x.alf` file; download it from the run artifacts.
2. Go to <https://license.unity3d.com/manual>, upload the `.alf`, and download the
   returned `Unity_v6000.x.ulf`.
3. Paste the **entire contents** of the `.ulf` into the `UNITY_LICENSE` secret.
4. Also set `UNITY_EMAIL` and `UNITY_PASSWORD` (your Unity ID) -- game-ci Personal needs
   all three.

### Apple-side setup (one-time)

In the **Apple Developer** portal:
1. Register the App ID `com.clemtek.mobileidlebuilder` (Identifiers) and enable the
   **In-App Purchase** capability (plus any other capability the app uses).
2. Note your **Team ID** (Membership) -> store it as the `APPLE_TEAM_ID` repo *variable*.

In **App Store Connect**:
3. Create the app (My Apps -> +) against that bundle ID.
4. Create an **App Store Connect API key** (Users and Access -> Integrations -> App Store
   Connect API) with the **App Manager** role. Download the `.p8` (one-time) and note the
   **Key ID** and **Issuer ID**.
5. TestFlight -> Internal Testing: add internal testers (no Beta App Review required).
6. For IAP testing: sign the Paid Apps agreement, create the IAP products, and add a
   Sandbox tester.

### iOS GitHub secrets / variables

| Name | Kind | Source |
|------|------|--------|
| `UNITY_LICENSE` | secret | contents of the `.ulf` (steps above) |
| `UNITY_EMAIL` | secret | Unity ID email |
| `UNITY_PASSWORD` | secret | Unity ID password |
| `APP_STORE_CONNECT_KEY_ID` | secret | ASC API Key ID |
| `APP_STORE_CONNECT_ISSUER_ID` | secret | ASC API Issuer ID |
| `APP_STORE_CONNECT_API_KEY_P8` | secret | base64 of the `.p8` (`base64 -i AuthKey_XXX.p8 \| pbcopy`) |
| `APPLE_TEAM_ID` | **variable** | Apple Developer Team ID |

## 6. GitHub Actions Permissions

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
| `UNITY_LICENSE` | contents of the `.ulf` | iOS build (game-ci activation) |
| `UNITY_EMAIL` | Unity ID email | iOS build (game-ci activation) |
| `UNITY_PASSWORD` | Unity ID password | iOS build (game-ci activation) |
| `APP_STORE_CONNECT_KEY_ID` | ASC API Key ID | iOS archive + TestFlight upload |
| `APP_STORE_CONNECT_ISSUER_ID` | ASC API Issuer ID | iOS archive + TestFlight upload |
| `APP_STORE_CONNECT_API_KEY_P8` | base64 of the `.p8` | iOS archive + TestFlight upload |
| `APPLE_TEAM_ID` (variable) | Apple Developer Team ID | iOS build + archive |

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
