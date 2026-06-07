using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Manages the three HUD banner types: notification, field proximity, and tutorial hint.
    /// Sibling MonoBehaviour to HUDController on the HUD GameObject.
    /// Call Init(root) before any other method.
    /// </summary>
    public class HUDBannerController : MonoBehaviour
    {
        [SerializeField] private float notificationDuration = 3f;

        private VisualElement _notificationBanner;
        private Label         _notificationIcon;
        private Label         _notificationMessage;
        private Coroutine     _hideNotificationCoroutine;

        private VisualElement _tutorialHintBanner;
        private Label         _tutorialHintMessage;

        private VisualElement _fieldBanner;
        private Label         _fieldBannerName;
        private Label         _fieldBannerType;

        public void Init(VisualElement root)
        {
            _notificationBanner  = root.Q("notification-banner");
            _notificationIcon    = root.Q<Label>("notification-banner__icon");
            _notificationMessage = root.Q<Label>("notification-banner__message");

            _tutorialHintBanner  = root.Q("tutorial-hint-banner");
            _tutorialHintMessage = root.Q<Label>("tutorial-hint-banner__message");

            _fieldBanner     = root.Q("field-proximity-banner");
            _fieldBannerName = root.Q<Label>("field-proximity-banner__name");
            _fieldBannerType = root.Q<Label>("field-proximity-banner__type");
        }

        // ── Notification banner ──────────────────────────────────────────────

        /// <summary>
        /// Shows a transient notification banner that auto-hides after notificationDuration seconds.
        /// modifier can be null (success/green), "warning", or "danger".
        /// </summary>
        public void ShowNotification(string icon, string message, string modifier = null)
        {
            if (_notificationBanner == null) return;

            if (_notificationIcon    != null) _notificationIcon.text    = icon;
            if (_notificationMessage != null) _notificationMessage.text = message;

            _notificationBanner.RemoveFromClassList("notification-banner--warning");
            _notificationBanner.RemoveFromClassList("notification-banner--danger");
            if (modifier != null)
                _notificationBanner.AddToClassList($"notification-banner--{modifier}");

            // Make the element visible (overrides CSS display:none) so the translate
            // transition has something to animate. --shown is added one frame later so
            // the display change settles before the CSS transition fires.
            _notificationBanner.style.display = UnityEngine.UIElements.DisplayStyle.Flex;
            _notificationBanner.schedule.Execute(
                () => _notificationBanner?.AddToClassList("notification-banner--shown"));

            if (_hideNotificationCoroutine != null)
                StopCoroutine(_hideNotificationCoroutine);
            _hideNotificationCoroutine = StartCoroutine(HideNotificationAfterDelay());
        }

        private IEnumerator HideNotificationAfterDelay()
        {
            yield return new WaitForSeconds(notificationDuration);
            if (_notificationBanner == null) yield break;

            // Slide back up; CSS transition (300ms) handles the animation.
            _notificationBanner.RemoveFromClassList("notification-banner--shown");

            // After the transition completes, restore display:none so the element is
            // truly hidden and can't receive stray pointer events.
            yield return new WaitForSeconds(0.35f);
            _notificationBanner.style.display = UnityEngine.UIElements.DisplayStyle.None;
        }

        // ── Field proximity banner ───────────────────────────────────────────

        /// <summary>
        /// Shows the persistent field proximity banner. Remains visible until HideFieldBanner is called.
        /// </summary>
        public void ShowFieldBanner(FieldSO field)
        {
            if (field == null) return;

            if (_fieldBannerName != null) _fieldBannerName.text = field.displayName;
            if (_fieldBannerType != null) _fieldBannerType.text = field.fieldType.ToString();

            if (_fieldBanner != null)
            {
                var accent = _fieldBanner.Q("field-proximity-banner__accent");
                if (accent != null)
                    accent.style.backgroundColor = new StyleColor(field.fieldColor);
            }

            HUDController.SetElementVisible(_fieldBanner, true);
        }

        public void HideFieldBanner() => HUDController.SetElementVisible(_fieldBanner, false);

        // ── Tutorial hint banner ─────────────────────────────────────────────

        /// <summary>
        /// Shows a persistent tutorial hint. Stays visible until HideTutorialHint is called.
        /// </summary>
        public void ShowTutorialHint(string message)
        {
            if (_tutorialHintMessage != null) _tutorialHintMessage.text = message;
            HUDController.SetElementVisible(_tutorialHintBanner, true);
        }

        public void HideTutorialHint() => HUDController.SetElementVisible(_tutorialHintBanner, false);
    }
}
