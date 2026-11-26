# Unity Cloud Diagnostics — Crash & Exception Reporting Runbook

How debug/diagnostic data (device crashes + uncaught exceptions) reaches Unity Cloud, and the manual
dashboard steps to finish turning it on. Paired with the analytics pipeline in `docs/agents/analytics.md`
and its dashboard workflow in `docs/analytics/README.md`.

## What ships in the repo

Cloud Diagnostics **Crash and Exception Reporting** is enabled at the project level:

- `ProjectSettings/UnityConnectSettings.asset` → `CrashReportingSettings.m_EnableCloudDiagnosticsReporting: 1`
- `ProjectSettings/ProjectSettings.asset` → `enableCrashReportAPI: 1`
- `Packages/manifest.json` → `com.unity.services.cloud-diagnostics`

Once enabled, uncaught C# exceptions and native crashes are captured automatically on device builds — no
gameplay code required. Data appears in the Unity Dashboard under **Cloud Diagnostics** (on Unity 6.2+ the
successor **Diagnostics** experience reports to the same place; classic Cloud Diagnostics is deprecated but
still functional). The Android Internet permission (already present) is all the runtime needs.

The project is linked to UGS: `cloudProjectId 1018413c-79e4-42b3-b114-9fdd70b5d380`, org `clemtek1`.

## Environment

Both platforms report to the **`development`** UGS environment for internal builds. `UgsEnvironment.Name`
(`Assets/Scripts/SaveSystem/UgsEnvironment.cs`) returns `"development"` when `UNITY_EDITOR || DEV_ENVIRONMENT`.
`DEV_ENVIRONMENT` is committed for **both** Android and iPhone in `ProjectSettings.asset`
(`scriptingDefineSymbols`), so device builds land in `development`, not `production`.

> Before a real **public store release**, strip both `DEV_ENVIRONMENT` defines so builds report to
> `production`. Crash reporting itself is environment-agnostic, but you want production crashes segmented
> from internal-test noise.

## Manual dashboard steps (one-time)

1. **Enable the service.** Unity Editor: **Window > General > Services > Cloud Diagnostics** → toggle
   **Crashes and Exceptions** on (this is what flipped the flags above; confirm it shows enabled). Then in the
   **Unity Dashboard → Cloud Diagnostics / Diagnostics**, confirm the service is enabled for the project so
   incoming reports are accepted.

2. **Upload symbols for readable stack traces.** Both platforms build with **IL2CPP** (Android is ARM64-only),
   so without symbols crash frames are raw addresses.
   - **Android:** symbol generation is on in code: `BuildScript.ConfigureAndroidDebugSymbols` sets
     Debug Symbols to **Full** (line numbers) as a **`symbols.zip`** for every Android build. CI keeps it as the
     `android-symbols-<run>` artifact (90 days) in `release-internal.yml` and `prod-release.yml`. CI uploads
     the symbols automatically to both places: Unity Diagnostics (the "Upload symbols to Unity Diagnostics"
     step, see Follow-ups below) and Play Console (with the AAB). Only a build made outside CI
     needs a manual upload. The zip is deliberately not embedded in the AAB (full IL2CPP symbols would trip
     Play's size check).
   - **iOS:** upload the `.dSYM` files produced by the Xcode archive (`scripts/archive-upload-ios.sh`) to the
     dashboard so iOS frames symbolicate.
   - Symbols are per build: a build without its own upload shows raw addresses.

## Verify end-to-end

1. Make an internal device build (Android internal track via `release-internal.yml`, or TestFlight via
   `release-ios.yml`).
2. Open the dev console on device and run `diag exception` (an uncaught C# exception on the next frame), then
   `diag crash` (a native abort: the app closes; reopen it so the report is sent). Both should appear in the
   dashboard within a few minutes. The native crash shows whether the symbol upload worked: its
   `libil2cpp.so` frames should show method names and line numbers, not raw addresses.
3. Cross-check `adb logcat -s Unity` on Android to confirm clean UGS init and no exceptions on normal launch.

## Follow-ups

- **Player-context metadata on crashes.** `CrashReportHandler.SetUserMetadata(key, value)` (up to 64 entries)
  can stamp `player_id` + build version so a dashboard crash is traceable to a save/session. Not wired yet:
  the `com.unity.modules.crashreport` engine module is not in the manifest, so confirm the `CrashReportHandler`
  API resolves before adding the call (a good spot is `SaveManager.ReconcileWithCloud` after auth, in a
  no-throw guarded block). Low priority — native reports already carry device/OS/app-version.
- **Automated symbol upload (live).** `release-internal.yml` and `prod-release.yml` run
  `scripts/ci/upload-symbols.ps1` right after the build, before Save Library moves `Library/` away (the symbols
  are read from `Library/Bee/Android/Prj/IL2CPP`). The wrapper fills the downloaded `scripts/ci/usymtool.ps1`
  placeholders into a temp copy and runs it; the committed script stays unedited, so a newer download can
  replace it as is (the wrapper fails loudly if a placeholder line changed). The step is `continue-on-error`
  and skips when the secrets are missing. Unity docs: "Upload debugging symbols from
  CI/CD pipelines". It reuses the `ci-cd-automation` service account, which needs the project role
  **LiveOps > Observability Symbols Uploader** (project level, or uploads get 403). No new secrets: the
  workflow builds the Authorization header (`Basic ` + base64 of `keyId:secret`) from
  `UGS_GAME_CONFIG_SERVICE_KEY_ID` / `..._SECRET_KEY` and takes the project ID from `UGS_PROJECT_ID`. The
  Windows script comes from Dashboard > project > View Diagnostics > Debugging Symbols > Download CI script,
  and is committed as `scripts/ci/usymtool.ps1`. The script fetches a fresh 7-day
  upload token on every run and calls `Editor/Data/Tools/usymtool.exe` with `USYM_UPLOAD_AUTH_TOKEN` and
  `USYM_UPLOAD_URL_SOURCE=https://perf-events.cloud.unity3d.com/url`. Do not pass `-projectId` to usymtool:
  that switches to a different upload mode and ignores the token. Success prints
  `Auth token acquired successfully` and `Failed: 0`. Symbols uploaded this way do not appear on the
  Dashboard's Debugging Symbols page.
- Migrate to the newer built-in **Diagnostics** experience (Unity 6.2+) for richer reports incl. Android ANRs.
