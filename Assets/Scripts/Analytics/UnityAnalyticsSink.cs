using System;
using System.Collections.Generic;
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
    /// philosophy as <see cref="FeatureFlagService"/> and <see cref="UGSCloudSaveService"/>.
    ///
    /// Requires UGS to be initialized and signed in before <see cref="StartCollection"/>; that
    /// already happens in <see cref="UGSCloudSaveService.InitializeAsync"/>, which SaveManager
    /// awaits before <see cref="TelemetryService.StartIfEnabled"/> is called.
    /// </summary>
    public sealed class UnityAnalyticsSink : IAnalyticsSink
    {
        public void StartCollection()
        {
#if ANALYTICS
            try
            {
                AnalyticsService.Instance.StartDataCollection();
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[Telemetry] StartDataCollection failed: {ex.Message}");
            }
#endif
        }

        public void RecordEvent(string eventName, IDictionary<string, object> parameters)
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
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[Telemetry] RecordEvent '{eventName}' failed: {ex.Message}");
            }
#endif
        }
    }
}
