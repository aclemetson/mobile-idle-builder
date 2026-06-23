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
    /// out) and account-link-safe (a throwaway anonymous session created just to read the flag is signed
    /// out with credentials cleared, so GameScene's normal Google/anon sign-in is untouched). See the
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

            // Establish a session for the fetch. Remember if we minted a throwaway one (brand-new user).
            bool createdThrowaway = false;
            if (!auth.IsSignedIn)
            {
                createdThrowaway = !auth.SessionTokenExists;
                await auth.SignInAnonymouslyAsync();
            }

            await svc.FetchAsync();
            bool maintenance = FeatureFlags.MaintenanceEnabled;

            // Wipe a throwaway identity so GameScene's Google/anon flow runs on a clean slate.
            // (Only when maintenance is OFF — otherwise we stay on the splash and never enter GameScene.)
            if (createdThrowaway && !maintenance)
            {
                try { auth.SignOut(clearCredentials: true); }
                catch (Exception ex) { GameLogger.Warning($"[Maintenance] Throwaway sign-out failed: {ex.Message}"); }
            }

            return maintenance;
        }

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
