using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The Entropy Surge pays minutes of IncomeRateTracker's rate, so the tracker's history must not
    /// outlive the run it was measured in: a prestige (new RunCount) or a pending reload clears it.
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

        void Feed(int fromSecond, int toSecond, double perSecond, double baseTotal, int run)
        {
            for (int t = fromSecond; t <= toSecond; t++)
                _tracker.Sample(t, baseTotal + (t - fromSecond) * perSecond, run, reloadPending: false);
        }

        [Test]
        public void SteadyIncome_PaysFromTheRate()
        {
            Feed(0, 60, 100.0, 0.0, run: 1);
            Assert.AreEqual(100.0, _tracker.PerSecond, 1e-6);
            Assert.AreEqual(100.0, _tracker.RewardPerSecond, 1e-6);
        }

        [Test]
        public void NewRun_ClearsTheOldRunsIncome_EvenIfTheTotalKeepsClimbing()
        {
            Feed(0, 60, 1000.0, 0.0, run: 1);
            Assert.AreEqual(1000.0, _tracker.RewardPerSecond, 1e-6);

            // Prestige: the run count goes up while the running total does not drop (the player entity
            // can outlive the reload), so only the run check can tell the runs apart.
            Feed(61, 63, 1.0, 60000.0, run: 2);
            Assert.AreEqual(1.0, _tracker.PerSecond, 1e-6, "rate is the new run's only");
            Assert.AreEqual(0.0, _tracker.RewardPerSecond, "too little of the new run yet: the Surge pays its floor");
        }

        [Test]
        public void ReloadPending_DropsTheHistory()
        {
            Feed(0, 60, 1000.0, 0.0, run: 1);
            _tracker.Sample(61, 61000.0, 1, reloadPending: true);
            Assert.AreEqual(0.0, _tracker.PerSecond);
            Assert.AreEqual(0.0, _tracker.RewardPerSecond);
        }

        [Test]
        public void EarlyBurst_IsNotStretchedIntoTheReward()
        {
            _tracker.Sample(0, 0.0, 1, false);
            _tracker.Sample(1, 5000.0, 1, false);   // one burst in the first second
            Assert.AreEqual(5000.0, _tracker.PerSecond, 1e-6, "Statistics still shows the live rate");
            Assert.AreEqual(0.0, _tracker.RewardPerSecond, "the Surge waits for 30 s of history");
        }

        [Test]
        public void RewardRate_NeedsTheMinimumSpan()
        {
            Assert.AreEqual(0.0, IncomeRateTracker.RewardRate(50.0, IncomeRateTracker.MinRewardSpanSeconds - 1));
            Assert.AreEqual(50.0, IncomeRateTracker.RewardRate(50.0, IncomeRateTracker.MinRewardSpanSeconds));
        }

        [Test]
        public void SurgeReward_AfterPrestige_IsTheFloor()
        {
            Feed(0, 60, 1000.0, 0.0, run: 1);
            long before = AdRewardCalculator.CalcIncomeReward(AdPlacement.EntropySurge, _tracker.RewardPerSecond);
            Feed(61, 65, 1.0, 60000.0, run: 2);
            long after = AdRewardCalculator.CalcIncomeReward(AdPlacement.EntropySurge, _tracker.RewardPerSecond);

            Assert.AreEqual(600000, before, "10 min of 1000/s");
            Assert.AreEqual(AdRewardCalculator.Def(AdPlacement.EntropySurge).MinimumFloor, after);
        }
    }
}
