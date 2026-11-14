using System.Collections;
using System.Collections.Generic;
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
        private bool          _notificationUp;       // shown and not on its way out
        private int           _swipePointerId = NoSwipe;
        private Vector2       _swipeStart;
        private const int     NoSwipe = -1;
        private const float   ControlsGapPx = 6f;

        // Mode bars (placement, conveyor, deconstruct) start where the banner does, so they move down for it.
        private readonly List<VisualElement> _pushedControls = new();
        // The inspector starts there too, but moving it would shift its buttons under the finger, and its upgrades
        // are what raise the banner. So while it is open the banner sits over the top bar instead.
        private VisualElement _inspectorPanel;
        private VisualElement _topBar;
        private bool          _inspectorOpen;

        private VisualElement _tutorialHintBanner;
        private Label         _tutorialHintMessage;
        private Label         _tutorialHintProgress;
        private Button        _tutorialHintRestore;
        private string        _tutorialHintText;
        private bool          _tutorialHintShown;
        private bool          _tutorialHintMinimized;
        private bool          _menuOpen;             // the left drawer is open: the hint must not cover its rows
        private bool          _autoMinimized;        // the menu, not the player, collapsed the hint

        /// <summary>True while a hint is active but collapsed to the "Show hint" pill.</summary>
        public bool IsTutorialHintMinimized => _tutorialHintShown && _tutorialHintMinimized;

        private VisualElement _fieldBanner;
        private Label         _fieldBannerName;
        private Label         _fieldBannerType;

        public void Init(VisualElement root)
        {
            _notificationBanner  = root.Q("notification-banner");
            _notificationIcon    = root.Q<Label>("notification-banner__icon");
            _notificationMessage = root.Q<Label>("notification-banner__message");
            _pushedControls.Clear();
            _pushedControls.AddRange(root.Query(className: "placement-overlay").ToList());
            _inspectorPanel = root.Q("building-inspector-panel");
            _topBar         = root.Q("top-bar");
            if (_notificationBanner != null)
            {
                // A wrapped message is taller, so the push follows the banner's real height.
                _notificationBanner.RegisterCallback<GeometryChangedEvent>(_ => ApplyNotificationClearance());
                _notificationBanner.RegisterCallback<PointerDownEvent>(OnBannerPointerDown);
                _notificationBanner.RegisterCallback<PointerMoveEvent>(OnBannerPointerMove);
                _notificationBanner.RegisterCallback<PointerUpEvent>(OnBannerPointerUp);
                _notificationBanner.RegisterCallback<PointerCancelEvent>(_ => CancelSwipe(resumeAutoHide: true));
            }

            _tutorialHintBanner  = root.Q("tutorial-hint-banner");
            _tutorialHintMessage  = root.Q<Label>("tutorial-hint-banner__message");
            _tutorialHintProgress = root.Q<Label>("tutorial-hint-banner__progress");
            _tutorialHintRestore = root.Q<Button>("tutorial-hint-restore");

            var minimize = root.Q<Button>("tutorial-hint-banner__minimize");
            if (minimize != null)             minimize.clicked             += MinimizeTutorialHint;
            if (_tutorialHintRestore != null) _tutorialHintRestore.clicked += RestoreTutorialHint;
            ApplyTutorialHintVisibility();

            _fieldBanner     = root.Q("field-proximity-banner");
            _fieldBannerName = root.Q<Label>("field-proximity-banner__name");
            _fieldBannerType = root.Q<Label>("field-proximity-banner__type");
        }

        // ── Notification banner ──────────────────────────────────────────────

        /// <summary>
        /// Shows a transient notification banner that auto-hides after notificationDuration seconds.
        /// modifier can be null (success/green), "warning", or "danger". The player can swipe it away sooner.
        /// </summary>
        public void ShowNotification(string icon, string message, string modifier = null)
        {
            if (_notificationBanner == null) return;

            CancelSwipe(resumeAutoHide: false);
            _notificationBanner.style.translate = StyleKeyword.Null;

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

            _notificationUp = true;
            ApplyNotificationClearance();
            RestartAutoHide();
        }

        private void RestartAutoHide()
        {
            StopAutoHide();
            _hideNotificationCoroutine = StartCoroutine(HideNotificationAfterDelay());
        }

        private void StopAutoHide()
        {
            if (_hideNotificationCoroutine != null)
                StopCoroutine(_hideNotificationCoroutine);
            _hideNotificationCoroutine = null;
        }

        private IEnumerator HideNotificationAfterDelay()
        {
            yield return new WaitForSeconds(notificationDuration);
            if (_notificationBanner == null) yield break;
            yield return SlideOutAndHide();
        }

        private IEnumerator SlideOutAndHide()
        {
            // Slide away; CSS transition (300ms) handles the animation. Controls return at the same time.
            _notificationBanner.RemoveFromClassList("notification-banner--shown");
            _notificationUp = false;
            ApplyNotificationClearance();

            // After the transition completes, restore display:none so the element is
            // truly hidden and can't receive stray pointer events.
            yield return new WaitForSeconds(0.35f);
            _notificationBanner.style.display = UnityEngine.UIElements.DisplayStyle.None;
            _notificationBanner.style.translate = StyleKeyword.Null;
        }

        // ── Notification clearance ───────────────────────────────────────────

        /// <summary>Called every frame by HUDController, because the inspector can open while the banner is up.</summary>
        public void RefreshNotificationClearance()
        {
            bool open = _inspectorPanel != null && !_inspectorPanel.ClassListContains("hidden");
            if (open == _inspectorOpen) return;
            _inspectorOpen = open;
            ApplyNotificationClearance();
        }

        private void ApplyNotificationClearance()
        {
            if (_notificationBanner == null) return;

            if (_inspectorOpen && _topBar != null && _notificationBanner.parent != null)
                _notificationBanner.style.top = _topBar.worldBound.yMin - _notificationBanner.parent.worldBound.yMin;
            else
                _notificationBanner.style.top = StyleKeyword.Null;

            float height = _notificationBanner.layout.height;
            bool  push   = _notificationUp && !_inspectorOpen && height > 0f;   // false for an unresolved NaN too
            ApplyControlsPush(push ? height + ControlsGapPx : 0f);
        }

        /// <summary>Moves the mode bars down by this many panel pixels.</summary>
        internal void ApplyControlsPush(float px)
        {
            foreach (var control in _pushedControls)
                control.style.paddingTop = px;
        }

        // ── Notification swipe ───────────────────────────────────────────────

        private void OnBannerPointerDown(PointerDownEvent e)
        {
            if (_swipePointerId != NoSwipe || !_notificationUp) return;
            _swipePointerId = e.pointerId;
            _swipeStart     = e.position;
            StopAutoHide();     // it must not vanish from under the finger
            _notificationBanner.AddToClassList("notification-banner--dragging");
            _notificationBanner.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void OnBannerPointerMove(PointerMoveEvent e)
        {
            if (e.pointerId != _swipePointerId) return;
            var moved = BannerSwipe.Follow((Vector2)e.position - _swipeStart);
            _notificationBanner.style.translate = new Translate(moved.x, moved.y);
            e.StopPropagation();
        }

        private void OnBannerPointerUp(PointerUpEvent e)
        {
            if (e.pointerId != _swipePointerId) return;
            Vector2 drag = (Vector2)e.position - _swipeStart;
            e.StopPropagation();

            if (!BannerSwipe.Dismisses(drag))
            {
                CancelSwipe(resumeAutoHide: true);
                return;
            }

            EndSwipe();
            var exit = BannerSwipe.ExitOffset(drag, _notificationBanner.layout.width);
            _notificationBanner.style.translate = new Translate(exit.x, exit.y);
            _hideNotificationCoroutine = StartCoroutine(SlideOutAndHide());
        }

        /// <summary>Lets go of an unfinished swipe, so the banner slides back home.</summary>
        private void CancelSwipe(bool resumeAutoHide)
        {
            if (_swipePointerId == NoSwipe) return;
            EndSwipe();
            _notificationBanner.style.translate = StyleKeyword.Null;
            if (resumeAutoHide) RestartAutoHide();
        }

        private void EndSwipe()
        {
            if (_notificationBanner.HasPointerCapture(_swipePointerId))
                _notificationBanner.ReleasePointer(_swipePointerId);
            _swipePointerId = NoSwipe;
            _notificationBanner.RemoveFromClassList("notification-banner--dragging");
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

        public void HideFieldBanner()
        {
            HUDController.SetElementVisible(_fieldBanner, false);
        }

        // ── Tutorial hint banner ─────────────────────────────────────────────

        /// <summary>
        /// Shows a persistent tutorial hint. Stays visible until HideTutorialHint is called.
        /// The player can minimize it to a "Show hint" pill so it stops covering menus; a
        /// hint with different text (a new tutorial step) re-expands so new guidance is seen.
        /// </summary>
        public void ShowTutorialHint(string message)
        {
            if (message != _tutorialHintText)
            {
                _tutorialHintMinimized = _menuOpen;
                _autoMinimized         = _menuOpen;
            }
            _tutorialHintText  = message;
            _tutorialHintShown = true;
            if (_tutorialHintMessage != null) _tutorialHintMessage.text = message;
            ApplyTutorialHintVisibility();
        }

        public void HideTutorialHint()
        {
            _tutorialHintShown = false;
            ApplyTutorialHintVisibility();
        }

        /// <summary>
        /// Sets the small have/need line under the hint (null or empty hides it). Unlike a new hint text,
        /// this never re-expands a minimized hint: it changes on every collect tap.
        /// </summary>
        public void SetTutorialHintProgress(string progress)
        {
            if (_tutorialHintProgress == null) return;
            bool show = !string.IsNullOrEmpty(progress);
            _tutorialHintProgress.text = show ? progress : "";
            HUDController.SetElementVisible(_tutorialHintProgress, show);
        }

        public void MinimizeTutorialHint()
        {
            _tutorialHintMinimized = true;
            ApplyTutorialHintVisibility();
        }

        public void RestoreTutorialHint()
        {
            _tutorialHintMinimized = false;
            _autoMinimized         = false;
            ApplyTutorialHintVisibility();
        }

        /// <summary>
        /// Collapses the hint to its "Show hint" pill (bottom-right) while the left drawer is open, because
        /// the full-width banner sits on the drawer's bottom rows and hides their Place buttons. Expands it
        /// again when the drawer closes, unless the player had already minimized it.
        /// </summary>
        public void SetMenuOpen(bool open)
        {
            _menuOpen = open;
            if (open && _tutorialHintShown && !_tutorialHintMinimized)
            {
                _tutorialHintMinimized = true;
                _autoMinimized         = true;
            }
            else if (!open && _autoMinimized)
            {
                _tutorialHintMinimized = false;
                _autoMinimized         = false;
            }
            ApplyTutorialHintVisibility();
        }

        private void ApplyTutorialHintVisibility()
        {
            HUDController.SetElementVisible(_tutorialHintBanner,  _tutorialHintShown && !_tutorialHintMinimized);
            HUDController.SetElementVisible(_tutorialHintRestore, _tutorialHintShown &&  _tutorialHintMinimized);
        }
    }
}
