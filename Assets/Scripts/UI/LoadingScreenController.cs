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
            GameLogger.Info($"[LoadingScreen] Starting async load of '{targetScene}'");
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
            // Animate toward real progress so the bar moves even when the load is instant.
            float displayed = 0f;
            const float fillSpeed = 0.6f; // 0→1 normalised/sec

            while (op.progress < 0.9f || displayed < 0.9f)
            {
                float target = (op.progress / 0.9f) * 0.9f;
                displayed = Mathf.MoveTowards(displayed, target, Time.deltaTime * fillSpeed);
                UpdateProgress(displayed * 100f);
                yield return null;
            }

            // Phase 2 — ECS SubScene entity initialisation (90%→100%).
            // Keep the loading screen alive across the scene boundary so it overlays
            // SampleScene while ECSLoadBridge streams and applies the save data.
            DontDestroyOnLoad(gameObject);
            op.allowSceneActivation = true;
            yield return null; // one frame for Awake calls in the incoming scene to run

            if (ECSLoadBridge.Instance != null)
            {
                // Crawl the bar slowly toward 99% while ECS entities load.
                while (!ECSLoadBridge.Instance.IsLoaded)
                {
                    displayed = Mathf.MoveTowards(displayed, 0.99f, Time.deltaTime * 0.05f);
                    UpdateProgress(displayed * 100f);
                    yield return null;
                }
            }

            // Phase 3 — done; snap to 100%, brief hold, reveal destination UI, remove overlay.
            UpdateProgress(100f);
            yield return new WaitForSeconds(0.3f);
            SceneLoader.CompleteTransition();
            Destroy(gameObject);
        }

        private void UpdateProgress(float pct)
        {
            if (_progressBar  != null) _progressBar.value  = pct;
            if (_percentLabel != null) _percentLabel.text  = $"{Mathf.RoundToInt(pct)}%";
        }
    }
}
