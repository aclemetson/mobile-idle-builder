using System;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Cyclotron Kick (docs/agents/tasks/feature-cyclotron-kick.md): the run's kick rules, the charge clock,
    /// the Insight it pays and how Insight stacks into the research price.
    /// </summary>
    [TestFixture]
    public class CyclotronTests
    {
        static readonly DateTime Noon = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // ── Run ────────────────────────────────────────────────────────────

        /// <summary>Advances in small steps until the particle sits at <paramref name="angle"/> (±1°).</summary>
        static void AdvanceTo(CyclotronRun run, float angle)
        {
            for (int i = 0; i < 100000; i++)
            {
                float d = Math.Abs(((run.Angle - angle) % 360f + 540f) % 360f - 180f);
                if (d <= 1f) return;
                run.Tick(0.001f);
            }
            Assert.Fail("never reached the angle");
        }

        [Test]
        public void TapOnTheGap_IsAPerfectKick_AndAddsEnergyAndSpeed()
        {
            var run = new CyclotronRun();
            AdvanceTo(run, 90f);
            float speed = run.Speed;
            Assert.AreEqual(CyclotronRun.Kick.Perfect, run.Tap());
            Assert.Greater(run.Energy, 0f);
            Assert.Greater(run.Speed, speed);
            Assert.AreEqual(1, run.Combo);
        }

        [Test]
        public void TapAwayFromTheGap_IsAMiss_AndCostsEnergy()
        {
            var run = new CyclotronRun();
            AdvanceTo(run, 90f); run.Tap();
            float energy = run.Energy;
            AdvanceTo(run, 180f);
            Assert.AreEqual(CyclotronRun.Kick.Miss, run.Tap());
            Assert.Less(run.Energy, energy);
            Assert.AreEqual(0, run.Combo);
        }

        [Test]
        public void SecondTapInTheSameCrossing_IsAMiss()
        {
            var run = new CyclotronRun();
            AdvanceTo(run, 90f);
            run.Tap();
            Assert.AreEqual(CyclotronRun.Kick.Miss, run.Tap());
        }

        [Test]
        public void EachCrossing_CanBeKickedOnce_BothGapsPerLap()
        {
            var run = new CyclotronRun();
            AdvanceTo(run, 90f);
            Assert.AreNotEqual(CyclotronRun.Kick.Miss, run.Tap());
            AdvanceTo(run, 270f);
            Assert.AreNotEqual(CyclotronRun.Kick.Miss, run.Tap(), "the top crossing is a new pass");
            AdvanceTo(run, 90f);
            Assert.AreNotEqual(CyclotronRun.Kick.Miss, run.Tap(), "the bottom again, a lap later");
            Assert.AreEqual(3, run.Combo);
        }

        [Test]
        public void ACrossingLeftUnkicked_BreaksTheCombo()
        {
            var run = new CyclotronRun();
            AdvanceTo(run, 90f); run.Tap();
            Assert.AreEqual(1, run.Combo);
            AdvanceTo(run, 300f);   // went past the top crossing without a tap
            Assert.AreEqual(0, run.Combo);
        }

        [Test]
        public void EnergyWidensTheOrbit()
        {
            var run = new CyclotronRun();
            float before = run.OrbitFraction;
            AdvanceTo(run, 90f); run.Tap();
            Assert.Greater(run.OrbitFraction, before);
        }

        [Test]
        public void RunEndsAfterItsDuration_AndIgnoresTaps()
        {
            var run = new CyclotronRun();
            for (int i = 0; i < 4000; i++) run.Tick(0.01f);
            Assert.IsTrue(run.Done);
            Assert.AreEqual(CyclotronRun.Kick.Miss, run.Tap());
            Assert.AreEqual(0f, run.Energy);
        }

        [Test]
        public void PerfectPlay_CanReachFullEnergy()
        {
            var run = new CyclotronRun();
            float target = 90f;
            while (!run.Done)
            {
                float d = Math.Abs(((run.Angle - target) % 360f + 540f) % 360f - 180f);
                if (d <= 1f) { run.Tap(); target = target == 90f ? 270f : 90f; }
                run.Tick(0.001f);   // < 1° per step even at top speed, so no crossing is skipped
            }
            Assert.GreaterOrEqual(run.Energy, 0.99f, "a flawless run should max out");
        }

        // ── Cooldown ────────────────────────────────────────────────────────

        [Test]
        public void Unlocks_AtTheSecondPrestige()
        {
            Assert.IsFalse(CyclotronProgress.IsUnlocked(new SaveData { prestigeCount = 1 }));
            Assert.IsTrue(CyclotronProgress.IsUnlocked(new SaveData { prestigeCount = 2 }));
        }

        [Test]
        public void NeverPlayed_IsReady()
        {
            var save = new SaveData();
            Assert.IsTrue(CyclotronProgress.IsReady(save, Noon));
            Assert.AreEqual(TimeSpan.Zero, CyclotronProgress.CooldownLeft(save, Noon));
        }

        [Test]
        public void OneRunAnHour_CountedFromTheRunStart()
        {
            var save = new SaveData();
            Assert.IsTrue(CyclotronProgress.TryStartRun(save, Noon));
            Assert.IsFalse(CyclotronProgress.TryStartRun(save, Noon.AddMinutes(30)), "still cooling down");
            Assert.AreEqual(TimeSpan.FromMinutes(30), CyclotronProgress.CooldownLeft(save, Noon.AddMinutes(30)));
            Assert.IsFalse(CyclotronProgress.IsReady(save, Noon.AddMinutes(59)));
            Assert.IsTrue(CyclotronProgress.TryStartRun(save, Noon.AddMinutes(60)), "ready an hour after the last start");
        }

        [Test]
        public void Cooldown_RunsWhileAway()
        {
            var save = new SaveData();
            CyclotronProgress.TryStartRun(save, Noon);
            Assert.IsTrue(CyclotronProgress.IsReady(save, Noon.AddHours(5)));
        }

        // ── Rewards ────────────────────────────────────────────────────────

        [Test]
        public void Insight_ScalesWithEnergy()
        {
            Assert.AreEqual(0.04f, CyclotronProgress.InsightFor(0f), 1e-5f);
            Assert.AreEqual(0.15f, CyclotronProgress.InsightFor(1f), 1e-5f);
            Assert.Greater(CyclotronProgress.InsightFor(0.6f), CyclotronProgress.InsightFor(0.3f));
        }

        [Test]
        public void Collect_StacksInsight_UpToTheCap()
        {
            var save = new SaveData();
            for (int i = 0; i < 10; i++) CyclotronProgress.Collect(save, 1f, roll: 0.99, out _);
            Assert.AreEqual(CyclotronProgress.MaxInsight, save.researchInsight, 1e-5f);
        }

        [Test]
        public void BonusCrystals_OnlyForAGreatRun_AndALuckyRoll()
        {
            Assert.AreEqual(CyclotronProgress.BonusCrystals, CyclotronProgress.BonusCrystalsFor(0.95f, 0.1));
            Assert.AreEqual(0, CyclotronProgress.BonusCrystalsFor(0.95f, 0.9));
            Assert.AreEqual(0, CyclotronProgress.BonusCrystalsFor(0.5f, 0.0));

            var save = new SaveData();
            CyclotronProgress.Collect(save, 1f, roll: 0.0, out int crystals);
            Assert.AreEqual(CyclotronProgress.BonusCrystals, crystals);
            Assert.AreEqual(CyclotronProgress.BonusCrystals, save.paidCurrency);
        }

        [Test]
        public void Insight_DiscountsTheResearchPrice_AndStacks()
        {
            Assert.AreEqual(8500L, ResearchCost.Effective(10000, 0f, 0f, false, 0.15f));
            // 5% Memory Resonance and 15% Insight multiply: 10,000 x 0.95 x 0.85.
            Assert.AreEqual(8075L, ResearchCost.Effective(10000, 0.05f, 0f, false, 0.15f));
            Assert.AreEqual(ResearchCost.Effective(10000, 0.05f, 0f, false),
                            ResearchCost.Effective(10000, 0.05f, 0f, false, 0f), "old overload unchanged");
        }

        [Test]
        public void ConsumeInsight_ResetsIt()
        {
            var save = new SaveData { researchInsight = 0.3f };
            CyclotronProgress.ConsumeInsight(save);
            Assert.AreEqual(0f, save.researchInsight);
        }

        // ── Save ───────────────────────────────────────────────────────────

        [Test]
        public void OldSave_WithoutCyclotronFields_LoadsAsNeverPlayed()
        {
            var save = UnityEngine.JsonUtility.FromJson<SaveData>("{\"prestigeCount\": 3}");
            Assert.IsTrue(string.IsNullOrEmpty(save.cyclotronLastRunUtc));
            Assert.AreEqual(0f, save.researchInsight);
            Assert.IsTrue(CyclotronProgress.IsReady(save, Noon), "a first run is available straight away");
        }

        [Test]
        public void CyclotronFields_RoundTrip()
        {
            var save = new SaveData { cyclotronLastRunUtc = "2026-11-14T12:00:00.0000000Z", researchInsight = 0.27f };
            var back = UnityEngine.JsonUtility.FromJson<SaveData>(UnityEngine.JsonUtility.ToJson(save));
            Assert.AreEqual(save.cyclotronLastRunUtc, back.cyclotronLastRunUtc);
            Assert.AreEqual(0.27f, back.researchInsight, 1e-6f);
            Assert.AreEqual(TimeSpan.FromMinutes(45), CyclotronProgress.CooldownLeft(back, Noon.AddMinutes(15)));
        }

        // ── UI text ────────────────────────────────────────────────────────

        [Test]
        public void StatusText_ShowsCountdownAndInsight()
        {
            string s = CyclotronController.StatusText(TimeSpan.FromSeconds(754), 0.12f);
            StringAssert.Contains("Next run in 12m 34s", s);
            StringAssert.Contains("12% off your next research", s);
            StringAssert.Contains("Ready to run", CyclotronController.StatusText(TimeSpan.Zero, 0f));
        }

        [Test]
        public void IntroText_PointsAtTheVerticalGap()
        {
            StringAssert.Contains("crosses the vertical gap", CyclotronController.IntroText);
            StringAssert.DoesNotContain("dees", CyclotronController.IntroText);
        }

        [Test]
        public void DrawerLabel_ShowsReadyOrMinutesLeft()
        {
            Assert.AreEqual("Cyclotron (ready)", CyclotronController.DrawerLabel(TimeSpan.Zero));
            Assert.AreEqual("Cyclotron (42m)", CyclotronController.DrawerLabel(TimeSpan.FromSeconds(41 * 60 + 5)), "rounds up");
            Assert.AreEqual("Cyclotron (1m)", CyclotronController.DrawerLabel(TimeSpan.FromSeconds(20)));
        }

        [Test]
        public void ResultText_SingularKick()
        {
            StringAssert.Contains("(1 kick, 1 perfect)", CyclotronController.ResultText(0.02f, 1, 1));
            StringAssert.Contains("(3 kicks,", CyclotronController.ResultText(0.1f, 3, 0));
        }

        [TestCase("btn-cyclotron")]
        [TestCase("cyclotron-modal")]
        [TestCase("cyclotron__view")]
        [TestCase("cyclotron__start")]
        [TestCase("cyclotron__collect")]
        public void GameHudUxml_HasCyclotronElement(string name)
        {
            var doc = System.Xml.Linq.XDocument.Load("Assets/UI/GameHUD.uxml");
            bool found = false;
            foreach (var e in doc.Descendants())
                if ((string)e.Attribute("name") == name) { found = true; break; }
            Assert.IsTrue(found, $"{name} missing from GameHUD.uxml");
        }
    }
}
