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

            SceneLoader.GoTo("GameScene");
        }
    }
}
