using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Detects maintenance that turns on <em>after</em> a player is already in GameScene (the splash gate
    /// handles everyone at launch). Re-fetches Remote Config every 5 minutes and as soon as the app returns
    /// from the background (#224; timing in <see cref="MaintenancePollSchedule"/>). If maintenance became
    /// active it flushes the save and returns to the splash, whose gate then shows the maintenance overlay.
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
                if (!MaintenancePollSchedule.IsDue(Time.realtimeSinceStartup, _lastCheck, _resumeRequested))
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

                if (FeatureFlags.MaintenanceEnabled)
                    TripToMaintenance();
            }
        }

        void TripToMaintenance()
        {
            GameLogger.Info("[Maintenance] Enabled mid-session — flushing save and returning to splash.");

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
