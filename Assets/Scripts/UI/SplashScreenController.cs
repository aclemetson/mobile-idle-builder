using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    [RequireComponent(typeof(UIDocument))]
    public class SplashScreenController : MonoBehaviour
    {
        [SerializeField] private float totalDuration  = 2f;
        [SerializeField] private float fadeInDuration = 0.5f;
        [SerializeField] private float maintenanceTimeoutSeconds = 4f;

        private IEnumerator Start()
        {
            var root    = GetComponent<UIDocument>().rootVisualElement;
            var content = root.Q("splash-content");
            var versionLabel = root.Q<Label>("splash-version");

            if (versionLabel != null)
                versionLabel.text = $"v{Application.version}";

            // Background is immediately visible; only text fades in
            float elapsed = 0f;
            while (elapsed < fadeInDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fadeInDuration);
                if (content      != null) content.style.opacity      = t;
                if (versionLabel != null) versionLabel.style.opacity  = t;
                yield return null;
            }

            if (content      != null) content.style.opacity      = 1f;
            if (versionLabel != null) versionLabel.style.opacity  = 1f;

            // Hold for the remainder of totalDuration, then hand off to SceneLoader
            float holdTime = totalDuration - fadeInDuration;
            if (holdTime > 0f)
                yield return new WaitForSeconds(holdTime);

            // Maintenance gate: never load GameScene while maintenance is enabled and reachable.
            // Fail-open inside the gate keeps offline players from being locked out.
            var maintTask = MaintenanceGate.IsUnderMaintenanceAsync(maintenanceTimeoutSeconds);
            yield return new WaitUntil(() => maintTask.IsCompleted);

            if (maintTask.Result)
                ShowMaintenance(root);
            else
                SceneLoader.GoTo("GameScene");
        }

        private void ShowMaintenance(VisualElement root)
        {
            GameLogger.Info("[Maintenance] Active — showing maintenance screen, not loading GameScene.");

            var content = root.Q("splash-content");
            var version = root.Q<Label>("splash-version");
            var panel   = root.Q("maintenance-panel");
            var message = root.Q<Label>("maintenance-message");
            var time    = root.Q<Label>("maintenance-time");
            var retry   = root.Q<Button>("btn-maintenance-retry");

            if (content != null) content.style.display = DisplayStyle.None;
            if (version != null) version.style.display = DisplayStyle.None;

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
                SceneLoader.GoTo("GameScene");
            else if (retry != null)
                retry.SetEnabled(true); // still under maintenance — allow another retry
        }
    }
}
