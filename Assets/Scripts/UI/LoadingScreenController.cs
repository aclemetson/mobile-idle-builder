using System.Collections;
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

            while (op.progress < 0.9f || displayed < 0.9f)
            {
                float target = (op.progress / 0.9f) * 0.9f;
                displayed = Mathf.MoveTowards(displayed, target, Time.deltaTime * fillSpeed);
                UpdateProgress(displayed * 100f);
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
