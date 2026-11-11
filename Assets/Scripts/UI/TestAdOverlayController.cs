using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The test-ad card shown in ad test mode (<c>ads.test_mode</c>, dev builds only) in place of a real
    /// ad. Finish counts as watching the ad (the reward is granted); Skip and Android back count as
    /// closing it early (no reward). Registers itself as <see cref="TestAdProvider.Presenter"/>.
    ///
    /// Added to the HUD GameObject by <see cref="HUDController"/> (GetComponent ?? AddComponent), so no
    /// scene wiring is needed.
    /// </summary>
    public class TestAdOverlayController : MonoBehaviour
    {
        VisualElement _card;
        Action<bool>  _onClosed;
        readonly object _modalOwner = new();

        public bool IsOpen => _onClosed != null;

        public void Init(VisualElement root)
        {
            _card = root?.Q("test-ad-modal");
            if (_card == null)
            {
                GameLogger.Warning("[Ads] test-ad-modal missing from GameHUD.uxml. Test ads will count as watched without a card.");
                return;
            }

            _card.Q<Button>("test-ad__finish").clicked += () => Close(true);
            _card.Q<Button>("test-ad__skip")  .clicked += Skip;
            HUDController.SetElementVisible(_card, false);
            TestAdProvider.Presenter = Present;
        }

        void Present(Action<bool> onClosed)
        {
            // A second show while one is open closes the first without a reward.
            _onClosed?.Invoke(false);
            _onClosed = onClosed;
            HUDController.SetElementVisible(_card, true);
            UIInputBlocker.SetModal(_modalOwner, true);
        }

        /// <summary>Skip button / Android back: close the test ad early, no reward.</summary>
        public void Skip() => Close(false);

        void Close(bool earned)
        {
            var cb = _onClosed;
            _onClosed = null;
            HUDController.SetElementVisible(_card, false);
            UIInputBlocker.SetModal(_modalOwner, false);
            cb?.Invoke(earned);
        }

        void OnDisable() => UIInputBlocker.SetModal(_modalOwner, false);

        void OnDestroy()
        {
            if (TestAdProvider.Presenter == (Action<Action<bool>>)Present)
                TestAdProvider.Presenter = null;
            // Never leave AdService waiting on a card that is gone.
            var cb = _onClosed;
            _onClosed = null;
            cb?.Invoke(false);
        }
    }
}
