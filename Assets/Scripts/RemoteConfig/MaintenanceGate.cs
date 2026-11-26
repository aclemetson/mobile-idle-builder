using System;
using System.Globalization;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Decides at startup (and on demand) whether the app is under remote maintenance, before GameScene
    /// is ever loaded. Backed by the same Remote Config flags as the rest of the game.
    ///
    /// Safety: fail-open (any error/timeout => not in maintenance, so an offline player is never locked
    /// out) and account-link-safe: it only restores an existing cached session and never creates a player,
    /// so a new install's first sign-in is GameScene's normal Google/anon flow (#209). See the
    /// maintenance-mode plan and project_auth_design.
    /// </summary>
    public static class MaintenanceGate
    {
        /// <summary>
        /// Returns true if maintenance is active. Races the whole check against <paramref name="timeoutSeconds"/>
        /// and returns false on timeout or any failure (fail-open). Safe to call repeatedly (Retry button).
        /// </summary>
        public static async Task<bool> IsUnderMaintenanceAsync(float timeoutSeconds)
        {
            try
            {
                var gate    = RunGateAsync();
                var timeout = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
                var first   = await Task.WhenAny(gate, timeout);

                if (first != gate)
                {
                    // Don't leave the slow task's exception unobserved.
                    _ = gate.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    GameLogger.Info("[Maintenance] Gate timed out — failing open (loading game).");
                    return false;
                }
                return await gate;
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[Maintenance] Gate failed — failing open. Reason: {ex.Message}");
                return false;
            }
        }

        static async Task<bool> RunGateAsync()
        {
            if (Application.internetReachability == NetworkReachability.NotReachable)
                return false; // can't know status -> fail open

            var svc = FeatureFlagService.Instance;
            if (svc == null) return false;

            await UnityServices.InitializeAsync(
                new InitializationOptions().SetOption("com.unity.services.core.environment-name", UgsEnvironment.Name));

            var auth = AuthenticationService.Instance;

            // Brand-new install (no cached session): don't sign in here (#209). Remote Config needs a signed-in
            // token, and a throwaway anonymous player would be a stray account plus a second sign-in. GameScene's
            // real sign-in fetches the flags, and MaintenanceWatcher acts on that fetch straight away.
            if (!auth.IsSignedIn && !auth.SessionTokenExists)
            {
                GameLogger.Info("[Maintenance] New install: no session yet, so the first in-game fetch decides.");
                return false;
            }

            // Returning player: restore the cached session once. GameScene's UGS init sees IsSignedIn and keeps
            // it, and SaveManager reuses this fetch instead of fetching again.
            if (!auth.IsSignedIn)
                await auth.SignInAnonymouslyAsync();

            await svc.FetchAsync();
            // A window scheduled for later (maintenance.startsUtc, #224) lets the player in; MaintenanceWatcher
            // then sends them back to the splash when it starts.
            bool maintenance = MaintenancePollSchedule.IsActive(
                FeatureFlags.MaintenanceEnabled, FeatureFlags.MaintenanceStartsUtc, NowUtc());

            return maintenance;
        }

        /// <summary>Server time when synced, else the device clock (only used to compare against a schedule).</summary>
        public static DateTime NowUtc() => ServerClock.UtcNow ?? DateTime.UtcNow;

        /// <summary>
        /// Pure: formats an ISO-8601 UTC timestamp as a local "Estimated back: MMM d, h:mm tt" line.
        /// Returns "" for blank or unparseable input (so the UI simply omits the time row).
        /// </summary>
        public static string FormatEstimatedReturn(string isoUtc)
        {
            if (string.IsNullOrWhiteSpace(isoUtc)) return "";
            if (!DateTime.TryParse(isoUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
                return "";
            return $"Estimated back: {utc.ToLocalTime():MMM d, h:mm tt}";
        }
    }
}
