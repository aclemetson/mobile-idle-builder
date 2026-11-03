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
   - **Android:** enable symbol generation (Player Settings / Build → Debug Symbols → `symbols.zip` with
     line numbers). Upload the produced `symbols.zip` for that version code to the Cloud Diagnostics dashboard.
   - **iOS:** upload the `.dSYM` files produced by the Xcode archive (`scripts/archive-upload-ios.sh`) to the
     dashboard so iOS frames symbolicate.
   - Symbols are per build version; automate the upload later if it proves fiddly.

## Verify end-to-end

1. Make an internal device build (Android internal track via `release-internal.yml`, or TestFlight via
   `release-ios.yml`).
2. Force an uncaught exception on device (a temporary `throw`, or a dev-console command), then confirm the
   crash/exception appears in the dashboard within a few minutes **with a symbolicated stack trace** (validates
   the symbol upload). Remove the temporary throw afterward.
3. Cross-check `adb logcat -s Unity` on Android to confirm clean UGS init and no exceptions on normal launch.

## Deferred / optional follow-ups

- **Player-context metadata on crashes.** `CrashReportHandler.SetUserMetadata(key, value)` (up to 64 entries)
  can stamp `player_id` + build version so a dashboard crash is traceable to a save/session. Not wired yet:
  the `com.unity.modules.crashreport` engine module is not in the manifest, so confirm the `CrashReportHandler`
  API resolves before adding the call (a good spot is `SaveManager.ReconcileWithCloud` after auth, in a
  no-throw guarded block). Low priority — native reports already carry device/OS/app-version.
- **Automated symbol upload** as a CI step after each build.
- Migrate to the newer built-in **Diagnostics** experience (Unity 6.2+) for richer reports incl. Android ANRs.
