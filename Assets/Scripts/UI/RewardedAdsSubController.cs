using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// View layer for the "Free Rewards" panel: one row per rewarded-ad placement (DoubleOffline is
    /// excluded here — it is offered inline on the idle-return modal). Sits as a sibling component on the
    /// HUD GameObject and is driven via Init/Refresh like the other HUD sub-controllers. All reward and
    /// limit logic lives in AdService; this controller only renders and forwards clicks.
    /// </summary>
    public class RewardedAdsSubController : MonoBehaviour
    {
        // Placements surfaced in the panel, in display order (DoubleOffline handled by the idle modal).
        static readonly AdPlacement[] PanelPlacements =
        {
            AdPlacement.EntropyBoost,
            AdPlacement.IdleRateBoost,
            AdPlacement.SpeedBoost,
            AdPlacement.TimeWarp,
            AdPlacement.Crystals,
        };

        HUDController _hud;
        VisualElement _panel;
        VisualElement _rows;
        Label         _info;

        public void Init(VisualElement root, HUDController hud)
        {
            _hud   = hud;
            _panel = root.Q("rewards-panel");
            _rows  = root.Q("rewards-rows");
            _info  = root.Q<Label>("rewards-info");

            if (_panel == null)
                GameLogger.Warning("[Ads] rewards-panel not found in GameHUD.uxml — Free Rewards panel will not display.");
        }

        /// <summary>Rebuilds the panel rows from current AdService state.</summary>
        public void Refresh()
        {
            if (_rows == null) return;
            _rows.Clear();

            // Development-only: open the LevelPlay Test Suite to verify mediation on a device without
            // registering advertising IDs. Never present in a release build.
            if (UnityEngine.Debug.isDebugBuild)
            {
                var testBtn = new Button { text = "🧪 Launch Ad Test Suite" };
                testBtn.AddToClassList("craft-btn");
                testBtn.clicked += () => AdService.Instance?.LaunchTestSuite();
                _rows.Add(testBtn);
            }

            var ads = AdService.Instance;
            if (_info != null)
            {
                _info.text = (ads != null && FeatureFlags.AdsEnabled)
                    ? "Watch a short ad for a bonus. Limited per day."
                    : "Free rewards are currently unavailable.";
            }

            foreach (var placement in PanelPlacements)
            {
                var def      = AdRewardCalculator.Def(placement);
                int remaining = ads?.Remaining(placement) ?? 0;
                bool canWatch = ads != null && ads.CanWatch(placement);

                var row = new VisualElement();
                row.AddToClassList("upgrade-row");
                if (remaining <= 0) row.AddToClassList("upgrade-row--maxed");

                var nameLabel = new Label(def.Name);
                nameLabel.AddToClassList("recipe-name");
                row.Add(nameLabel);

                var descLabel = new Label($"{def.Description}   ({remaining}/{def.DailyCap} left today)");
                descLabel.AddToClassList("recipe-inputs");
                row.Add(descLabel);

                var watchBtn = new Button { text = remaining > 0 ? "▶ Watch" : "Maxed" };
                watchBtn.AddToClassList("craft-btn");
                watchBtn.SetEnabled(canWatch);

                var captured = placement;
                watchBtn.clicked += () => OnWatchPressed(captured);
                row.Add(watchBtn);

                _rows.Add(row);
            }
        }

        void OnWatchPressed(AdPlacement placement)
        {
            var ads = AdService.Instance;
            if (ads == null) return;

            ads.Show(placement, result =>
            {
                if (result.Granted)
                    _hud?.ShowNotification("🎬", $"Reward: {result.Message}");
                else if (!string.IsNullOrEmpty(result.Message))
                    _hud?.ShowNotification("▶", result.Message);
                Refresh();
            });
        }
    }
}
