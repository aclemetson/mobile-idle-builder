using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Detects maintenance that turns on <em>after</em> a player is already in GameScene (the splash gate
    /// handles everyone at launch). Re-fetches Remote Config every <c>maintenance.pollSeconds</c> (5 min by
    /// default) and as soon as the app returns from the background. A window scheduled with
    /// <c>maintenance.startsUtc</c> is armed from the fetched values and trips at its start with no fetch
    /// (#224; timing in <see cref="MaintenancePollSchedule"/>). When maintenance is active it flushes the
    /// save and returns to the splash, whose gate then shows the maintenance overlay.
    ///
    /// Also the second-chance net for the fail-open case (player entered while offline, connectivity then
    /// returns during maintenance). Self-bootstraps and persists; only acts while GameScene is active, so
    /// scene re-entry is handled without re-spawning.
    /// </summary>
    public class MaintenanceWatcher : SingletonMonoBehaviour<MaintenanceWatcher>
    {
        protected override bool PersistAcrossScenes => true;

        const string GameSceneName   = "GameScene";
        const string SplashSceneName = "SplashScene";

        float _lastCheck;
        bool  _resumeRequested;
        bool  _scheduleArmed;   // saw maintenance switched on with a start still ahead
        int   _seenFetchCount;  // FeatureFlagService.FetchCount last acted on

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            new GameObject(nameof(MaintenanceWatcher)).AddComponent<MaintenanceWatcher>();
        }

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            _lastCheck = Time.realtimeSinceStartup;   // the splash gate just checked
            StartCoroutine(PollLoop());
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) _resumeRequested = true;
        }

        IEnumerator PollLoop()
        {
            var tick = new WaitForSecondsRealtime(1f);
            while (true)
            {
                yield return tick;

                // Scene first: outside GameScene the arm is kept, so a start passed during loading still trips.
                if (SceneManager.GetActiveScene().name == GameSceneName && ScheduledStartReached())
                {
                    TripToMaintenance("scheduled start");
                    continue;
                }

                // A fetch made elsewhere (SaveManager's first fetch after sign-in, which is the only check a new
                // install gets, #209) is fresh data: act on it now instead of at the next poll.
                if (SceneManager.GetActiveScene().name == GameSceneName && NewFetchSaysActive())
                {
                    TripToMaintenance("in-game fetch");
                    continue;
                }

                float poll = MaintenancePollSchedule.ClampPollSeconds(FeatureFlags.MaintenancePollSeconds);
                if (!MaintenancePollSchedule.IsDue(Time.realtimeSinceStartup, _lastCheck, _resumeRequested, poll))
                    continue;

                bool onResume = _resumeRequested;
                _resumeRequested = false;
                _lastCheck = Time.realtimeSinceStartup;

                // Only relevant while actively in the game; idle on splash/loading.
                if (SceneManager.GetActiveScene().name != GameSceneName) continue;

                var svc = FeatureFlagService.Instance;
                if (svc == null) continue;

                if (onResume) GameLogger.Debug("[Maintenance] App resumed; checking Remote Config now.");
                var fetch = svc.FetchAsync();
                yield return new WaitUntil(() => fetch.IsCompleted);
                _lastCheck = Time.realtimeSinceStartup;   // the next poll counts from the finished check
                _seenFetchCount = svc.FetchCount;

                if (MaintenancePollSchedule.IsActive(FeatureFlags.MaintenanceEnabled,
                        FeatureFlags.MaintenanceStartsUtc, MaintenanceGate.NowUtc()))
                    TripToMaintenance(onResume ? "resume check" : "poll");
            }
        }

        /// <summary>
        /// True when a successful fetch happened since the last one this watcher handled and its values put
        /// maintenance in force. Cached values alone never trip, which keeps the splash gate's fail-open.
        /// </summary>
        bool NewFetchSaysActive()
        {
            var svc = FeatureFlagService.Instance;
            if (svc == null || svc.FetchCount == _seenFetchCount) return false;
            _seenFetchCount = svc.FetchCount;
            return MaintenancePollSchedule.IsActive(FeatureFlags.MaintenanceEnabled,
                FeatureFlags.MaintenanceStartsUtc, MaintenanceGate.NowUtc());
        }

        /// <summary>
        /// Arms while the cached flags say maintenance starts later; returns true once, when that start
        /// passes and maintenance is still on. Only a schedule seen ahead of time trips here, so a player
        /// who got in through the gate's fail-open with stale cached flags is not bounced back to the splash.
        /// </summary>
        bool ScheduledStartReached()
        {
            bool   enabled = FeatureFlags.MaintenanceEnabled;
            string starts  = FeatureFlags.MaintenanceStartsUtc;
            var    now     = MaintenanceGate.NowUtc();

            if (MaintenancePollSchedule.IsScheduledAhead(enabled, starts, now))
            {
                if (!_scheduleArmed) GameLogger.Info($"[Maintenance] Window scheduled for {starts}; armed.");
                _scheduleArmed = true;
                return false;
            }
            if (!_scheduleArmed) return false;
            _scheduleArmed = false;
            return MaintenancePollSchedule.IsActive(enabled, starts, now);
        }

        void TripToMaintenance(string reason)
        {
            // Logged with the lag behind a scheduled start, for the #224 latency measurement.
            string lag = MaintenancePollSchedule.TryParseUtc(FeatureFlags.MaintenanceStartsUtc, out var start)
                ? $", {(MaintenanceGate.NowUtc() - start).TotalSeconds:F0}s after the scheduled start" : "";
            GameLogger.Info($"[Maintenance] Active mid-session ({reason}{lag}) — flushing save and returning to splash.");

            var sm = SaveManager.Instance;
            if (sm != null)
            {
                sm.SaveLocal();                          // synchronous — guarantees no progress loss
                sm.StartCoroutine(sm.SaveToCloud());     // best-effort cloud push (survives the scene load)
            }

            SceneManager.LoadScene(SplashSceneName);
        }
    }
}
