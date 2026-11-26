using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The Entropy Surge pays minutes of IncomeRateTracker's reward rate. That rate must belong to the
    /// current run (a prestige or pending reload clears it) and must be steady: no load backlog, no
    /// Cyclotron boost, no pause gap, and one lump of income must not dominate it.
    /// </summary>
    [TestFixture]
    public class IncomeRateTrackerTests
    {
        GameObject _go;
        IncomeRateTracker _tracker;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("IncomeRateTrackerTests");
            _tracker = _go.AddComponent<IncomeRateTracker>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        /// <summary>Steady income; <paramref name="boost"/> is the Cyclotron multiplier on the boosted total.</summary>
        void Feed(int fromSecond, int toSecond, double perSecond, double baseTotal, int run, double boost = 1.0)
        {
            for (int t = fromSecond; t <= toSecond; t++)
            {
                double unboosted = baseTotal + (t - fromSecond) * perSecond;
                _tracker.Sample(t, unboosted * boost, unboosted, run, reloadPending: false);
            }
        }

        [Test]
        public void SteadyIncome_PaysFromTheRate()
        {
            Feed(0, 100, 100.0, 0.0, run: 1);
            Assert.AreEqual(100.0, _tracker.PerSecond, 1e-6);
            Assert.AreEqual(100.0, _tracker.RewardPerSecond, 1e-6);
        }

        [Test]
        public void NewRun_ClearsTheOldRunsIncome_EvenIfTheTotalKeepsClimbing()
        {
            Feed(0, 100, 1000.0, 0.0, run: 1);
            Assert.AreEqual(1000.0, _tracker.RewardPerSecond, 1e-6);

            // Prestige: the run count goes up while the running total does not drop (the player entity
            // can outlive the reload), so only the run check can tell the runs apart.
            Feed(101, 103, 1.0, 100000.0, run: 2);
            Assert.AreEqual(1.0, _tracker.PerSecond, 1e-6, "rate is the new run's only");
            Assert.AreEqual(0.0, _tracker.RewardPerSecond, "too little of the new run yet: the Surge pays its floor");
        }

        [Test]
        public void ReloadPending_DropsTheHistory()
        {
            Feed(0, 100, 1000.0, 0.0, run: 1);
            _tracker.Sample(101, 101000.0, 101000.0, 1, reloadPending: true);
            Assert.AreEqual(0.0, _tracker.PerSecond);
            Assert.AreEqual(0.0, _tracker.RewardPerSecond);
        }

        [Test]
        public void LoadBacklog_IsIgnoredByTheReward()
        {
            _tracker.Sample(0, 0.0, 0.0, 1, false);
            _tracker.Sample(1, 50000.0, 50000.0, 1, false);   // backlog paid out on the first frames
            Feed(2, 90, 100.0, 50000.0, run: 1);
            Assert.AreEqual(100.0, _tracker.RewardPerSecond, 1e-6, "the settle period skips the backlog");
        }

        [Test]
        public void OneLump_IsDilutedByTheLongerWindow()
        {
            Feed(0, 200, 100.0, 0.0, run: 1);
            Feed(201, 260, 100.0, 26100.0, run: 1);   // +6000 lump at t=201 on top of 100/s

            Assert.AreEqual(200.0, _tracker.PerSecond, 1e-6, "Statistics: 60 s window feels the lump fully");
            Assert.AreEqual(24000.0 / 180.0, _tracker.RewardPerSecond, 1e-6, "Surge: spread over 180 s");
        }

        [Test]
        public void CyclotronBoost_DoesNotInflateTheReward()
        {
            Feed(0, 100, 100.0, 0.0, run: 1, boost: 3.0);
            Assert.AreEqual(300.0, _tracker.PerSecond, 1e-6, "Statistics shows what is earned");
            Assert.AreEqual(100.0, _tracker.RewardPerSecond, 1e-6, "the Surge pays the unboosted rate");
        }

        [Test]
        public void PauseGap_RestartsTheHistory()
        {
            Feed(0, 100, 100.0, 0.0, run: 1);
            Assert.IsTrue(_tracker.HasRewardRate);
            _tracker.Sample(400, 10100.0, 10100.0, 1, false);   // app came back from the background
            Assert.IsFalse(_tracker.HasRewardRate);
            Assert.AreEqual(0.0, _tracker.PerSecond);
        }

        [Test]
        public void RewardRate_NeedsTheMinimumSpan()
        {
            Assert.AreEqual(0.0, IncomeRateTracker.RewardRate(50.0, IncomeRateTracker.MinRewardSpanSeconds - 1));
            Assert.AreEqual(50.0, IncomeRateTracker.RewardRate(50.0, IncomeRateTracker.MinRewardSpanSeconds));
        }

        [Test]
        public void SecondsUntil_CountsDownToZero()
        {
            Assert.AreEqual(IncomeRateTracker.MinRewardSpanSeconds, IncomeRateTracker.SecondsUntil(0), 1e-9);
            Assert.AreEqual(12.0, IncomeRateTracker.SecondsUntil(IncomeRateTracker.MinRewardSpanSeconds - 12), 1e-9);
            Assert.AreEqual(0.0, IncomeRateTracker.SecondsUntil(IncomeRateTracker.MinRewardSpanSeconds + 5), 1e-9);
        }

        [Test]
        public void SecondsUntilRewardRate_IncludesTheSettlePeriod()
        {
            _tracker.Sample(0, 0.0, 0.0, 1, false);
            Assert.AreEqual(IncomeRateTracker.SettleSeconds + IncomeRateTracker.MinRewardSpanSeconds,
                            _tracker.SecondsUntilRewardRate, 1e-9);
        }

        [Test]
        public void HasRewardRate_FalseUntilTheMinimumSpan_ThenTrue()
        {
            Feed(0, 20, 100.0, 0.0, run: 1);            // 10 s settle + 10 s of samples: under the minimum
            Assert.IsFalse(_tracker.HasRewardRate);
            Feed(21, 80, 100.0, 2100.0, run: 1);
            Assert.IsTrue(_tracker.HasRewardRate);
            Assert.AreEqual(0.0, _tracker.SecondsUntilRewardRate, 1e-9);
        }
    }
}
