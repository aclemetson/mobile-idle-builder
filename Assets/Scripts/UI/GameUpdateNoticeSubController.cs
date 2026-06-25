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

        public void Show(string title, string message)
        {
            if (_modal == null) return;
            if (_title   != null) _title.text   = string.IsNullOrEmpty(title)   ? "Game Updated" : title;
            if (_message != null) _message.text = message ?? "";
            _modal.RemoveFromClassList("hidden");
        }

        private void Hide()
        {
            _modal?.AddToClassList("hidden");
        }
    }
}
