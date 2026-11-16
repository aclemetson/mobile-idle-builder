using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    [RequireComponent(typeof(UIDocument))]
    public class SplashScreenController : MonoBehaviour
    {
        // Minimum time the splash stays up. The maintenance gate runs in parallel, so the splash
        // ends at max(totalDuration, gate finished) instead of totalDuration + gate (#190).
        [SerializeField] private float totalDuration  = 1.25f;
        [SerializeField] private float fadeInDuration = 0.5f;
        [SerializeField] private float maintenanceTimeoutSeconds = 4f;

        private void Awake()
        {
            // SplashScene shares gamePanelSettings with LoadingScreen and GameScene, whose UIScaler
            // components set its scale at runtime. Without this the splash (and its maintenance
            // overlay) rendered at the asset's saved 1x on a fresh launch but at the scaled size
            // after a mid-session maintenance kick-out (#195).
            var doc = GetComponent<UIDocument>();
            if (doc != null && doc.panelSettings != null)
                doc.panelSettings.scale = UIScaler.ComputeScale(Screen.width);
        }

        private IEnumerator Start()
        {
            var root    = GetComponent<UIDocument>().rootVisualElement;
            var content = root.Q("splash-content");
            float started = Time.realtimeSinceStartup;

            // Maintenance gate: never load GameScene while maintenance is enabled and reachable.
            // Fail-open inside the gate keeps offline players from being locked out. Started now so
            // UGS init + Remote Config fetch overlap the splash instead of following it.
            var maintTask = MaintenanceGate.IsUnderMaintenanceAsync(maintenanceTimeoutSeconds);

            // Background is immediately visible; only text fades in
            float elapsed = 0f;
            while (elapsed < fadeInDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fadeInDuration);
                if (content != null) content.style.opacity = t;
                yield return null;
            }

            if (content != null) content.style.opacity = 1f;

            // Hand off once both the minimum splash time has passed and the gate has answered.
            yield return new WaitUntil(() =>
                maintTask.IsCompleted && Time.realtimeSinceStartup - started >= totalDuration);
            GameLogger.Info($"[Splash] Leaving after {Time.realtimeSinceStartup - started:F2}s");

            if (maintTask.Result)
                ShowMaintenance(root);
            else
                ContinueOrPromptUpdate(root);
        }

        /// <summary>
        /// Update prompt (<see cref="AppUpdateCheck"/>, flag <c>update.enabled</c>): uses the flags the
        /// maintenance gate just fetched, or the cached ones when it timed out. An older build pauses here
        /// with an Update link; "Later" continues unless the build is below the minimum.
        /// </summary>
        private void ContinueOrPromptUpdate(VisualElement root)
        {
            string current = Application.version;
            var kind = AppUpdateCheck.Evaluate(current);
            GameLogger.Info($"[Update] current={current} latest={AppUpdateCheck.LatestVersion} " +
                            $"min={AppUpdateCheck.MinimumVersion} enabled={FeatureFlags.UpdateEnabled} -> {kind}");

            if (kind == AppUpdateKind.None || !ShowUpdate(root, kind, current))
                SceneLoader.GoTo("GameScene");
        }

        /// <summary>Shows the update panel. False if the panel is missing from the UXML (then the game just loads).</summary>
        private bool ShowUpdate(VisualElement root, AppUpdateKind kind, string current)
        {
            var panel    = root.Q("update-panel");
            var message  = root.Q<Label>("update-message");
            var versions = root.Q<Label>("update-versions");
            var update   = root.Q<Button>("btn-update");
            var later    = root.Q<Button>("btn-update-later");
            if (panel == null)
            {
                GameLogger.Warning("[Update] update-panel missing from SplashScreen.uxml; loading the game.");
                return false;
            }

            string url = AppUpdateCheck.UpdateUrl;
            bool required = kind == AppUpdateKind.Required;

            var content = root.Q("splash-content");
            if (content != null) content.style.display = DisplayStyle.None;

            if (message != null)
            {
                string body = FeatureFlags.UpdateMessage;
                if (required) body += "\nThis version is no longer supported. Please update to keep playing.";
                if (string.IsNullOrEmpty(url))
                    body += AppUpdateCheck.IsIos ? "\nUpdate from the App Store." : "\nUpdate from the Play Store.";
                message.text = body;
            }
            if (versions != null)
            {
                string target = required && string.IsNullOrEmpty(AppUpdateCheck.LatestVersion)
                    ? AppUpdateCheck.MinimumVersion : AppUpdateCheck.LatestVersion;
                versions.text = $"Installed {current}  ·  Available {target}";
            }

            if (update != null)
            {
                update.style.display = string.IsNullOrEmpty(url) ? DisplayStyle.None : DisplayStyle.Flex;
                update.clicked += () =>
                {
                    GameLogger.Info($"[Update] Opening {url}");
                    Application.OpenURL(url);
                };
            }
            if (later != null)
            {
                later.style.display = required ? DisplayStyle.None : DisplayStyle.Flex;
                later.clicked += () => SceneLoader.GoTo("GameScene");
            }

            panel.RemoveFromClassList("hidden");
            return true;
        }

        private void ShowMaintenance(VisualElement root)
        {
            GameLogger.Info("[Maintenance] Active — showing maintenance screen, not loading GameScene.");

            var content = root.Q("splash-content");
            var panel   = root.Q("maintenance-panel");
            var message = root.Q<Label>("maintenance-message");
            var time    = root.Q<Label>("maintenance-time");
            var retry   = root.Q<Button>("btn-maintenance-retry");

            if (content != null) content.style.display = DisplayStyle.None;

            if (message != null) message.text = FeatureFlags.MaintenanceMessage;
            if (time != null)
            {
                string t = MaintenanceGate.FormatEstimatedReturn(FeatureFlags.MaintenanceUntilUtc);
                time.text = t;
                time.style.display = string.IsNullOrEmpty(t) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (retry != null)
                retry.clicked += () => StartCoroutine(RetryRoutine(root));

            if (panel != null) panel.RemoveFromClassList("hidden");
        }

        private IEnumerator RetryRoutine(VisualElement root)
        {
            var retry = root.Q<Button>("btn-maintenance-retry");
            if (retry != null) retry.SetEnabled(false);

            var task = MaintenanceGate.IsUnderMaintenanceAsync(maintenanceTimeoutSeconds);
            yield return new WaitUntil(() => task.IsCompleted);

            if (!task.Result)
            {
                root.Q("maintenance-panel")?.AddToClassList("hidden");
                ContinueOrPromptUpdate(root);
            }
            else if (retry != null)
                retry.SetEnabled(true); // still under maintenance — allow another retry
        }
    }
}
