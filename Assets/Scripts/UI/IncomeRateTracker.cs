using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Samples <see cref="AutomationIncome"/> once per real second and exposes a rolling 60 s rate.
    /// Added to the HUD GameObject by HUDController.Awake; runs even while the Statistics panel is closed.
    ///
    /// The history belongs to one run: a change in <see cref="PrestigeData.RunCount"/> clears it, and no
    /// samples are taken while a prestige reload is pending. Otherwise the Entropy Surge ad, which pays
    /// minutes of this income, could pay a fresh run at the old run's rate.
    /// </summary>
    public class IncomeRateTracker : MonoBehaviour
    {
        public const double WindowSeconds = 60.0;

        /// <summary>
        /// Seconds of this run's income the Entropy Surge needs before it pays from the rate rather than
        /// its floor. Without it, one burst in the first second or two would be stretched to 10 minutes.
        /// </summary>
        public const double MinRewardSpanSeconds = 30.0;

        private readonly RollingRate _rate = new RollingRate(90);
        private EntityQuery _query;
        private EntityQuery _prestigeQuery;
        private bool        _queryValid;
        private World       _world;
        private float       _nextSample;
        private int         _runCount = -1;

        /// <summary>Entropy per second earned by automation, averaged over the last 60 s.</summary>
        public double PerSecond => _rate.Rate(WindowSeconds);

        /// <summary>
        /// The rate the Entropy Surge pays from: <see cref="PerSecond"/> once this run has at least
        /// <see cref="MinRewardSpanSeconds"/> of samples, otherwise 0 (so the ad pays its floor).
        /// </summary>
        public double RewardPerSecond => RewardRate(PerSecond, _rate.SpanSeconds(WindowSeconds));

        internal static double RewardRate(double perSecond, double spanSeconds) =>
            spanSeconds >= MinRewardSpanSeconds ? perSecond : 0d;

        /// <summary>The live tracker (on the HUD GameObject), or null outside the game scene. Used by the
        /// Entropy Surge ad, whose reward is minutes of this income.</summary>
        public static IncomeRateTracker Current { get; private set; }

        void OnEnable()  => Current = this;
        void OnDisable() { if (Current == this) Current = null; }

        /// <summary>
        /// Records one sample. A new <paramref name="runCount"/> (a prestige happened) or a pending
        /// prestige reload throws the history away, so nothing from the old run reaches the new one.
        /// </summary>
        internal void Sample(double time, double total, int runCount, bool reloadPending)
        {
            if (reloadPending) { _rate.Clear(); return; }
            if (runCount != _runCount)
            {
                _rate.Clear();
                _runCount = runCount;
            }
            _rate.Add(time, total);
        }

        void Update()
        {
            if (PrestigeRunReset.ReloadPending) { _rate.Clear(); return; }
            if (Time.unscaledTime < _nextSample) return;
            _nextSample = Time.unscaledTime + 1f;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) { _queryValid = false; return; }

            if (!_queryValid || _world != world)
            {
                _world = world;
                _query         = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<AutomationIncome>());
                _prestigeQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PrestigeData>());
                _queryValid = true;
            }

            if (_query.CalculateEntityCount() != 1) return;   // singleton not baked yet (SubScene race)
            var income = _query.GetSingleton<AutomationIncome>();
            int runCount = _prestigeQuery.CalculateEntityCount() == 1
                ? _prestigeQuery.GetSingleton<PrestigeData>().RunCount
                : _runCount;
            Sample(Time.unscaledTime, income.Total, runCount, false);
        }
    }
}
