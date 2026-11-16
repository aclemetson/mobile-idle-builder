using System;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Samples <see cref="AutomationIncome"/> once per real second and exposes two rates:
    ///  - <see cref="PerSecond"/>: the live 60 s average of everything earned (Statistics panel).
    ///  - <see cref="RewardPerSecond"/>: a steadier rate the Entropy Surge pays from. It uses
    ///    <see cref="AutomationIncome.Unboosted"/> (no Cyclotron Kick boost), a longer
    ///    <see cref="RewardWindowSeconds"/> window so one lump (a Demon sink drain, an autosell burst, a big
    ///    craft's byproducts) is not stretched into 10 minutes of income, and skips the first
    ///    <see cref="SettleSeconds"/> after any restart, when a load or resume pays out a backlog at once.
    /// Added to the HUD GameObject by HUDController.Awake; runs even while the Statistics panel is closed.
    ///
    /// The history belongs to one stretch of play: it restarts on a new <see cref="PrestigeData.RunCount"/>,
    /// a drop in the total (entity recreated), a gap between samples longer than <see cref="MaxGapSeconds"/>
    /// (app paused / backgrounded: real time passed but the simulation did not), and is held empty while a
    /// prestige reload is pending.
    /// </summary>
    public class IncomeRateTracker : MonoBehaviour
    {
        public const double WindowSeconds = 60.0;

        /// <summary>Window the Entropy Surge's rate averages over.</summary>
        public const double RewardWindowSeconds = 180.0;

        /// <summary>
        /// Seconds of settled income the Entropy Surge needs before it prices itself. Until then the offer is
        /// held back ("Measuring income") instead of guessing from a few seconds.
        /// </summary>
        public const double MinRewardSpanSeconds = 60.0;

        /// <summary>Seconds after a restart whose income the Surge ignores (load / resume backlog).</summary>
        public const double SettleSeconds = 10.0;

        /// <summary>A longer gap between samples means the game was not running: start the history over.</summary>
        public const double MaxGapSeconds = 5.0;

        private readonly RollingRate _rate   = new RollingRate(90);
        private readonly RollingRate _reward = new RollingRate(200);
        private EntityQuery _query;
        private EntityQuery _prestigeQuery;
        private bool        _queryValid;
        private World       _world;
        private float       _nextSample;
        private int         _runCount = -1;
        private double      _lastTime = double.NegativeInfinity;
        private double      _lastTotal;
        private double      _settleUntil;

        /// <summary>Entropy per second earned by automation, averaged over the last 60 s.</summary>
        public double PerSecond => _rate.Rate(WindowSeconds);

        /// <summary>
        /// The rate the Entropy Surge pays from: unboosted income over up to <see cref="RewardWindowSeconds"/>,
        /// once there are <see cref="MinRewardSpanSeconds"/> of settled samples; otherwise 0 (the floor).
        /// </summary>
        public double RewardPerSecond => RewardRate(_reward.Rate(RewardWindowSeconds), RewardSpan);

        internal static double RewardRate(double perSecond, double spanSeconds) =>
            spanSeconds >= MinRewardSpanSeconds ? perSecond : 0d;

        double RewardSpan => _reward.SpanSeconds(RewardWindowSeconds);

        /// <summary>True once <see cref="RewardPerSecond"/> is the real rate rather than the floor.</summary>
        public bool HasRewardRate => RewardSpan >= MinRewardSpanSeconds;

        /// <summary>Seconds of sampling left before <see cref="HasRewardRate"/> turns true (0 once it has).</summary>
        public double SecondsUntilRewardRate =>
            HasRewardRate ? 0d
            : Math.Max(0d, _settleUntil - (double.IsInfinity(_lastTime) ? _settleUntil - SettleSeconds : _lastTime))
              + SecondsUntil(RewardSpan);

        internal static double SecondsUntil(double spanSeconds) => Math.Max(0d, MinRewardSpanSeconds - spanSeconds);

        /// <summary>The live tracker (on the HUD GameObject), or null outside the game scene. Used by the
        /// Entropy Surge ad, whose reward is minutes of this income.</summary>
        public static IncomeRateTracker Current { get; private set; }

        void OnEnable()  => Current = this;
        void OnDisable() { if (Current == this) Current = null; }

        void Restart(double time)
        {
            _rate.Clear();
            _reward.Clear();
            _settleUntil = time + SettleSeconds;
        }

        /// <summary>
        /// Records one sample. <paramref name="total"/> feeds the Statistics rate, <paramref name="unboosted"/>
        /// the Surge rate. A new run, a pending reload, a falling total or a long gap restarts the history.
        /// </summary>
        internal void Sample(double time, double total, double unboosted, int runCount, bool reloadPending)
        {
            if (reloadPending)
            {
                Restart(time);
                _lastTime = double.NegativeInfinity;
                return;
            }

            if (runCount != _runCount || total < _lastTotal || time - _lastTime > MaxGapSeconds)
            {
                Restart(time);
                _runCount = runCount;
            }
            _lastTime  = time;
            _lastTotal = total;

            _rate.Add(time, total);
            if (time >= _settleUntil) _reward.Add(time, unboosted);
        }

        void Update()
        {
            if (PrestigeRunReset.ReloadPending)
            {
                Sample(Time.unscaledTime, 0d, 0d, _runCount, reloadPending: true);
                return;
            }
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
            Sample(Time.unscaledTime, income.Total, income.Unboosted, runCount, false);
        }
    }
}
