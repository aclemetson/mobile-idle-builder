using System;
using System.Collections.Generic;
using Unity.Services.Authentication;
using Unity.Services.Core;
#if ANALYTICS
using Unity.Services.Analytics;
#endif

namespace MobileIdleBuilder
{
    /// <summary>
    /// Live <see cref="IAnalyticsSink"/> backed by UGS Analytics (com.unity.services.analytics).
    /// Compiled to no-ops when the package is absent (the ANALYTICS define is set by the
    /// MobileIdleBuilder asmdef versionDefines, mirroring REMOTE_CONFIG). Every call is wrapped
    /// in try/catch so a telemetry failure can never disrupt gameplay — same graceful-degradation
    /// philosophy as <see cref="FeatureFlagService"/> and <see cref="UGSCloudSaveService"/>. The
    /// failure is *reported* (false + a warning) rather than swallowed silently.
    ///
    /// Requires UGS to be initialized and signed in before <see cref="StartCollection"/>; that
    /// already happens in <see cref="UGSCloudSaveService.InitializeAsync"/>, which SaveManager
    /// awaits before <see cref="TelemetryService.StartIfEnabled"/> is called.
    /// </summary>
    public sealed class UnityAnalyticsSink : IAnalyticsSink
    {
        public bool StartCollection()
        {
#if ANALYTICS
            try
            {
                AnalyticsService.Instance.StartDataCollection();
                return true;
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[Telemetry] StartDataCollection failed: {ex.Message}");
                return false;
            }
#else
            GameLogger.Warning("[Telemetry] ANALYTICS define missing — analytics package not compiled in.");
            return false;
#endif
        }

        public bool RecordEvent(string eventName, IDictionary<string, object> parameters)
        {
#if ANALYTICS
            try
            {
                var evt = new CustomEvent(eventName);
                if (parameters != null)
                {
                    foreach (var kvp in parameters)
                    {
                        if (kvp.Value == null) continue; // CustomEvent rejects null values
                        evt.Add(kvp.Key, kvp.Value);
                    }
                }
                AnalyticsService.Instance.RecordEvent(evt);
                return true;
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[Telemetry] RecordEvent '{eventName}' failed: {ex.Message}");
                return false;
            }
#else
            return false;
#endif
        }

        public bool Flush()
        {
#if ANALYTICS
            try
            {
                AnalyticsService.Instance.Flush();
                return true;
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[Telemetry] Flush failed: {ex.Message}");
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// Reports the state the SDK actually needs to deliver an event: package compiled in, UGS
        /// initialized, player signed in, and which dashboard environment the events land in. Reading
        /// AuthenticationService.Instance before UnityServices is initialized throws, so both reads
        /// are guarded — an unknown value is itself the diagnosis.
        /// </summary>
        public string Describe()
        {
#if ANALYTICS
            string state;
            try { state = UnityServices.State.ToString(); }
            catch (Exception ex) { state = $"unknown ({ex.Message})"; }

            string signedIn;
            try { signedIn = AuthenticationService.Instance.IsSignedIn.ToString(); }
            catch (Exception ex) { signedIn = $"unknown ({ex.Message})"; }

            return $"UGS Analytics — services={state}, signedIn={signedIn}, environment={UgsEnvironment.Name}";
#else
            return "UGS Analytics — ANALYTICS define MISSING: package not compiled in, every call is a no-op.";
#endif
        }
    }
}
