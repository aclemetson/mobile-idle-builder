using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Populates the Statistics panel (stats-panel): automation income per second, the idle projection
    /// and the idle earnings cap. Sits as a component alongside HUDController on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class StatsSubController : MonoBehaviour
    {
        internal const string Footnote =
            "Idle payout is estimated from your factory layout when you leave, so it can differ slightly.";

        private VisualElement _panel;
        private Label _incomeValue, _incomeSub, _idleValue, _idleSub, _capValue, _footnote;
        private HUDController _hud;
        private IncomeRateTracker _tracker;
        private IVisualElementScheduledItem _ticker;

        public void Init(VisualElement root, HUDController hud)
        {
            _hud         = hud;
            _panel       = root.Q("stats-panel");
            _incomeValue = root.Q<Label>("stats-income-value");
            _incomeSub   = root.Q<Label>("stats-income-sub");
            _idleValue   = root.Q<Label>("stats-idle-value");
            _idleSub     = root.Q<Label>("stats-idle-sub");
            _capValue    = root.Q<Label>("stats-cap-value");
            _footnote    = root.Q<Label>("stats-footnote");
            if (_panel == null) GameLogger.Warning("[Stats] stats-panel not found in GameHUD.uxml");
            _ticker = null; // the UI was rebuilt; schedule on the new panel
        }

        /// <summary>Fills the labels with current values.</summary>
        public void Refresh()
        {
            if (_panel == null) return;
            if (_tracker == null) _tracker = GetComponent<IncomeRateTracker>();

            double perSecond = _tracker != null ? _tracker.PerSecond : 0.0;

            var config   = GameBootstrap.Instance != null ? GameBootstrap.Instance.gameConfig : null;
            var upgrades = PersistentUpgradeService.Instance;
            var save     = SaveManager.Instance != null ? SaveManager.Instance.Current : null;

            float rate   = config != null ? OfflineCollectionService.GetEffectiveCollectionRate(config, upgrades) : 0f;
            float adMult = OfflineCollectionService.GetAdIdleMultiplier(save);
            float cap    = config != null ? OfflineCollectionService.GetEffectiveIdleCap(config, upgrades) : 0f;

            if (_incomeValue != null) _incomeValue.text = IncomeText(perSecond);
            if (_incomeSub   != null) _incomeSub.text   = "Average over the last 60 s";
            if (_idleValue   != null) _idleValue.text   = IncomeText(IdleProjection(perSecond, rate, adMult));
            if (_idleSub     != null) _idleSub.text     = IdleSubtitle(rate, adMult);
            if (_capValue    != null) _capValue.text    = FormatCap(cap);
            if (_footnote    != null) _footnote.text    = Footnote;
        }

        /// <summary>Refresh once a second while the panel is open; pauses itself when it closes.</summary>
        public void StartTicker()
        {
            if (_panel == null) return;
            if (_ticker == null) _ticker = _panel.schedule.Execute(OnTick).Every(1000);
            else _ticker.Resume();
        }

        private void OnTick()
        {
            if (_panel == null || _panel.ClassListContains("hidden")) { _ticker?.Pause(); return; }
            Refresh();
        }

        // ---- Pure helpers (tested) ----

        internal static string IncomeText(double perSecond) => NumberFormat.Entropy(perSecond) + " /s";

        internal static double IdleProjection(double perSecond, float rate, float adMult)
            => perSecond * rate * adMult;

        internal static string IdleSubtitle(float rate, float adMult)
        {
            string s = $"{rate:P0} idle rate";
            if (adMult > 1f) s += " (ad boost active)";
            return s;
        }

        internal static string FormatCap(float seconds)
        {
            int totalMinutes = Mathf.FloorToInt(Mathf.Max(0f, seconds) / 60f);
            return $"{totalMinutes / 60}h {totalMinutes % 60}m";
        }
    }
}
