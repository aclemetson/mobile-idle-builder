using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Sibling MonoBehaviour on the HUD GameObject. Shows a one-shot modal telling the player that
    /// game values changed (driven by the remote <c>gamedata.updatedUtc</c> stamp). Mirrors
    /// <see cref="IdleReturnSubController"/>: Init() is called by HUDController.OnEnable() after the
    /// UIDocument is ready; Show() is called via HUDController.MaybeShowGameUpdateNotice() from
    /// ECSLoadBridge after the save is applied.
    /// </summary>
    public class GameUpdateNoticeSubController : MonoBehaviour
    {
        private VisualElement _modal;
        private Label         _title;
        private Label         _message;
        private Button        _okBtn;

        public void Init(VisualElement root, HUDController hud)
        {
            _modal   = root.Q("game-update-modal");
            _title   = root.Q<Label>("game-update-title");
            _message = root.Q<Label>("game-update-message");
            _okBtn   = root.Q<Button>("btn-game-update-ok");

            if (_okBtn != null)
                _okBtn.clicked += Hide;
        }

        /// <param name="leftAligned">Release notes (#502) are bullet lists, which read better left-aligned.</param>
        public void Show(string title, string message, bool leftAligned = false)
        {
            if (_modal == null) return;
            if (_title   != null) _title.text   = string.IsNullOrEmpty(title)   ? "Game Updated" : title;
            if (_message != null)
            {
                _message.text = message ?? "";
                if (leftAligned) _message.style.unityTextAlign = TextAnchor.UpperLeft;
                else             _message.style.unityTextAlign = StyleKeyword.Null;
            }
            var scroll = _modal.Q<ScrollView>("game-update-scroll");
            if (scroll != null) scroll.scrollOffset = Vector2.zero;   // a long note opens at its start
            _modal.RemoveFromClassList("hidden");
        }

        /// <summary>True while the update notice is on screen.</summary>
        public bool IsOpen => _modal != null && !_modal.ClassListContains("hidden");

        /// <summary>Back button: dismiss exactly as the OK button does.</summary>
        public void Close() => Hide();

        private void Hide()
        {
            _modal?.AddToClassList("hidden");
        }
    }
}
