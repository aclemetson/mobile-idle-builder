using System.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    [RequireComponent(typeof(UIDocument))]
    public class LoadingScreenController : MonoBehaviour
    {
        private ProgressBar _progressBar;
        private Label _percentLabel;

        private void Start()
        {
            GameLogger.Info($"[LoadingScreen] Start — target: '{SceneLoader.TargetScene ?? "null"}'");

            var doc = GetComponent<UIDocument>();
            if (doc != null)
            {
                // Ensure this overlay renders above all game UI (DontDestroyOnLoad means it
                // survives into the destination scene where other UIDocuments exist at order 0).
                doc.sortingOrder = 100;

                var root = doc.rootVisualElement;
                _progressBar  = root?.Q<ProgressBar>("progress-bar");
                _percentLabel = root?.Q<Label>("percent-label");
            }

            if (string.IsNullOrEmpty(SceneLoader.TargetScene))
            {
                GameLogger.Info("[LoadingScreen] No target scene set — cannot load.");
                return;
            }

            StartCoroutine(LoadAsync(SceneLoader.TargetScene));
        }

        private IEnumerator LoadAsync(string targetScene)
        {
            // Pre-Phase0: wait for the previous GameScene's ECS SubScene to fully unload.
            // SceneManager.LoadScene (synchronous) destroys MonoBehaviours immediately but
            // DefaultGameObjectInjectionWorld persists — its SubScene entities tear down
            // asynchronously over several frames.  Calling LoadSceneAsync("GameScene") while
            // those GPU assets are still being released deadlocks Vulkan on Android.
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                EntityQuery drainQuery = world.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<PlayerProgressData>());
                bool hadEntities = !drainQuery.IsEmpty;
                if (hadEntities)
                {
                    GameLogger.Info("[LoadingScreen] Pre-Phase0 — waiting for ECS SubScene to unload");
                    const float kDrainTimeout = 5f;
                    float drainElapsed = 0f;
                    while (!drainQuery.IsEmpty && drainElapsed < kDrainTimeout)
                    {
                        drainElapsed += Time.deltaTime;
                        yield return null;
                    }
                    if (drainQuery.IsEmpty)
                        GameLogger.Info($"[LoadingScreen] Pre-Phase0 complete — drained in {drainElapsed:F2}s");
                    else
                        GameLogger.Warning($"[LoadingScreen] Pre-Phase0 timeout after {drainElapsed:F1}s — proceeding anyway");
                }
                drainQuery.Dispose();
            }

            // Two-step GPU drain before LoadSceneAsync to avoid a Vulkan deadlock on Android.
            // WaitForSecondsRealtime lets OS events (keyboard dismiss → WINDOW_INSETS_CHANGED →
            // swapchain resize) settle; WaitForEndOfFrame guarantees the GPU has finished
            // presenting the last frame (WaitForSecondsRealtime resumes at Update time, not
            // end-of-frame).
            yield return new WaitForSecondsRealtime(0.15f);
            yield return new WaitForEndOfFrame();

            GameLogger.Info($"[LoadingScreen] Phase1 — beginning async load of '{targetScene}'");
            AsyncOperation op = SceneManager.LoadSceneAsync(targetScene);
            if (op == null)
            {
                GameLogger.Error($"[LoadingScreen] Scene '{targetScene}' not found in Build Profile.");
                SceneLoader.CompleteTransition();
                Destroy(gameObject);
                yield break;
            }
            op.allowSceneActivation = false;

            // Phase 1 — scene file loading (op.progress: 0→0.9, displayed: 0%→90%).
            float displayed = 0f;
            const float fillSpeed = 0.6f;
            float phase1LogTimer = 0f;

            while (op.progress < 0.9f || displayed < 0.9f)
            {
                float target = (op.progress / 0.9f) * 0.9f;
                displayed = Mathf.MoveTowards(displayed, target, Time.deltaTime * fillSpeed);
                UpdateProgress(displayed * 100f);
                phase1LogTimer += Time.deltaTime;
                if (phase1LogTimer >= 2f)
                {
                    phase1LogTimer = 0f;
                    GameLogger.Info($"[LoadingScreen] Phase1 wait — op.progress={op.progress:F2}  displayed={displayed:F2}");
                }
                yield return null;
            }
            GameLogger.Info($"[LoadingScreen] Phase1 complete — op.progress={op.progress:F2}  displayed={displayed:F2}");

            // Phase 2 — ECS SubScene entity initialisation (90%→100%).
            DontDestroyOnLoad(gameObject);
            op.allowSceneActivation = true;
            yield return null; // one frame for Awake calls in the incoming scene to run

            bool hasBridge = ECSLoadBridge.Instance != null;
            GameLogger.Info($"[LoadingScreen] Phase2 — ECSLoadBridge.Instance={(hasBridge ? "found" : "NULL")}");

            if (hasBridge)
            {
                float logTimer = 0f;
                while (!ECSLoadBridge.Instance.IsLoaded)
                {
                    displayed = Mathf.MoveTowards(displayed, 0.99f, Time.deltaTime * 0.05f);
                    UpdateProgress(displayed * 100f);
                    logTimer += Time.deltaTime;
                    if (logTimer >= 2f)
                    {
                        logTimer = 0f;
                        GameLogger.Info($"[LoadingScreen] Phase2 still waiting — IsLoaded=false  displayed={displayed:F2}");
                    }
                    yield return null;
                }
                GameLogger.Info("[LoadingScreen] Phase2 complete — ECSLoadBridge.IsLoaded=true");
            }

            // Phase 3 — done; snap to 100%, brief hold, reveal destination UI, remove overlay.
            GameLogger.Info("[LoadingScreen] Phase3 — snapping to 100%");
            try { UpdateProgress(100f); }
            catch (System.Exception ex) { GameLogger.Error($"[LoadingScreen] UpdateProgress(100f) threw: {ex.Message}"); }

            GameLogger.Info("[LoadingScreen] Phase3 — pre-yield");
            yield return new WaitForSecondsRealtime(0.3f);

            GameLogger.Info("[LoadingScreen] Phase3 — post-yield, calling CompleteTransition");
            SceneLoader.CompleteTransition();

            GameLogger.Info("[LoadingScreen] Phase3 — post-CompleteTransition, calling Destroy");
            Destroy(gameObject);
            GameLogger.Info("[LoadingScreen] Overlay destroyed — transition complete");
        }

        private void UpdateProgress(float pct)
        {
            if (_progressBar  != null) _progressBar.value  = pct;
            if (_percentLabel != null) _percentLabel.text  = $"{Mathf.RoundToInt(pct)}%";
        }
    }
}
