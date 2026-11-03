using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Detects maintenance that turns on <em>after</em> a player is already in GameScene (the splash gate
    /// handles everyone at launch). Polls Remote Config on an interval; if maintenance becomes active it
    /// flushes the save and returns to the splash, whose gate then shows the maintenance overlay.
    ///
    /// Also the second-chance net for the fail-open case (player entered while offline, connectivity then
    /// returns during maintenance). Self-bootstraps and persists; only acts while GameScene is active, so
    /// scene re-entry is handled without re-spawning.
    /// </summary>
    public class MaintenanceWatcher : SingletonMonoBehaviour<MaintenanceWatcher>
    {
        protected override bool PersistAcrossScenes => true;

        const float  PollSeconds   = 300f;       // 5 minutes
        const string GameSceneName = "GameScene";
        const string SplashSceneName = "SplashScene";

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
            StartCoroutine(PollLoop());
        }

        IEnumerator PollLoop()
        {
            var wait = new WaitForSecondsRealtime(PollSeconds);
            while (true)
            {
                yield return wait;

                // Only relevant while actively in the game; idle on splash/loading.
                if (SceneManager.GetActiveScene().name != GameSceneName) continue;

                var svc = FeatureFlagService.Instance;
                if (svc == null) continue;

                var fetch = svc.FetchAsync();
                yield return new WaitUntil(() => fetch.IsCompleted);

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
