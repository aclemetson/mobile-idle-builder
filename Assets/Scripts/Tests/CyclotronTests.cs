using System;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Cyclotron Kick (docs/agents/tasks/archive/feature-cyclotron-kick.md): the run's kick rules, the charge clock,
    /// the timed entropy boost, the Insight it pays and the upgrades Insight buys.
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
        public void Intro_PlaysOnce_WhenUnlocked_AndNothingElseIsTalking()
        {
            Assert.IsFalse(CyclotronController.ShouldPlayIntro(new SaveData { prestigeCount = 1 }, false), "still locked");
            Assert.IsTrue(CyclotronController.ShouldPlayIntro(new SaveData { prestigeCount = 2 }, false));
            Assert.IsFalse(CyclotronController.ShouldPlayIntro(new SaveData { prestigeCount = 2 }, true), "waits for the open dialogue");
            Assert.IsFalse(CyclotronController.ShouldPlayIntro(new SaveData { prestigeCount = 2, cyclotronIntroSeen = true }, false), "once");
            Assert.IsFalse(CyclotronController.ShouldPlayIntro(
                new SaveData { prestigeCount = 3, cyclotronLastRunUtc = "2026-11-19T12:00:00.0000000Z" }, false),
                "a player who already ran it needs no intro");
        }

        [Test]
        public void IntroDialogue_Exists()
        {
            var db = UnityEngine.Resources.Load<DialogueDatabaseSO>("DialogueDatabase");
            Assert.IsNotNull(db?.Get(CyclotronController.IntroDialogueId), "run Import Game Data");
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
        public void FasterRecharge_ShortensTheCooldown()
        {
            var save = new SaveData { cyclotronCooldownLevel = 2 };   // 60 - 2 x 5 = 50 min
            Assert.AreEqual(50.0, CyclotronProgress.CooldownMinutes(save), 1e-9);
            CyclotronProgress.TryStartRun(save, Noon);
            Assert.IsFalse(CyclotronProgress.IsReady(save, Noon.AddMinutes(49)));
            Assert.IsTrue(CyclotronProgress.IsReady(save, Noon.AddMinutes(50)));
            Assert.AreEqual(30.0, CyclotronProgress.CooldownMinutes(99), 1e-9, "capped at the max level");
        }

        [Test]
        public void Cooldown_RunsWhileAway()
        {
            var save = new SaveData();
            CyclotronProgress.TryStartRun(save, Noon);
            Assert.IsTrue(CyclotronProgress.IsReady(save, Noon.AddHours(5)));
        }

        // ── Boost ──────────────────────────────────────────────────────────

        [Test]
        public void Multiplier_ScalesWithEnergy()
        {
            Assert.AreEqual(1.5f, CyclotronProgress.MultiplierFor(0f), 1e-5f);
            Assert.AreEqual(3.0f, CyclotronProgress.MultiplierFor(1f), 1e-5f);
            Assert.AreEqual(2.3f, CyclotronProgress.MultiplierFor(0.55f), 1e-5f, "rounded to one decimal");
            Assert.AreEqual(3.0f, CyclotronProgress.MultiplierFor(5f), 1e-5f, "clamped");
        }

        [Test]
        public void Collect_StartsTheBoost_ForFiveMinutes()
        {
            var save = new SaveData();
            CyclotronProgress.Collect(save, 1f, roll: 0.99, Noon, out float mult, out _, out _);
            Assert.AreEqual(3f, mult, 1e-5f);
            Assert.AreEqual(3f, CyclotronProgress.BoostMultiplier(save, Noon.AddMinutes(4)), 1e-5f);
            Assert.AreEqual(TimeSpan.FromMinutes(1), CyclotronProgress.BoostLeft(save, Noon.AddMinutes(4)));
            Assert.AreEqual(1f, CyclotronProgress.BoostMultiplier(save, Noon.AddMinutes(5)), "over after 5 min");
        }

        [Test]
        public void LongerBoost_AddsAMinuteALevel()
        {
            var save = new SaveData { cyclotronBoostLevel = 3 };
            Assert.AreEqual(8.0, CyclotronProgress.BoostMinutes(save), 1e-9);
            CyclotronProgress.Collect(save, 0f, roll: 0.99, Noon, out _, out _, out _);
            Assert.AreEqual(1.5f, CyclotronProgress.BoostMultiplier(save, Noon.AddMinutes(7.9)), 1e-5f);
            Assert.AreEqual(1f, CyclotronProgress.BoostMultiplier(save, Noon.AddMinutes(8)));
            Assert.AreEqual(15.0, CyclotronProgress.BoostMinutes(99), 1e-9, "capped at the max level");
        }

        [Test]
        public void LongestBoost_IsShorterThanTheShortestCooldown()
        {
            Assert.Less(CyclotronProgress.BoostMinutes(CyclotronProgress.MaxBoostLevel),
                        CyclotronProgress.CooldownMinutes(CyclotronProgress.MaxCooldownLevel),
                        "two boosts must never overlap");
        }

        [Test]
        public void NoBoost_IsTimesOne()
        {
            Assert.AreEqual(1f, CyclotronProgress.BoostMultiplier(new SaveData(), Noon));
            Assert.AreEqual(1f, CyclotronProgress.BoostMultiplier(null, Noon));
        }

        // ── Insight and upgrades ───────────────────────────────────────────

        [Test]
        public void Insight_OnePlusOnePerQuarter()
        {
            Assert.AreEqual(1, CyclotronProgress.InsightFor(0f));
            Assert.AreEqual(1, CyclotronProgress.InsightFor(0.24f));
            Assert.AreEqual(2, CyclotronProgress.InsightFor(0.25f));
            Assert.AreEqual(4, CyclotronProgress.InsightFor(0.8f));
            Assert.AreEqual(5, CyclotronProgress.InsightFor(1f));
        }

        [Test]
        public void Collect_BanksInsight()
        {
            var save = new SaveData();
            CyclotronProgress.Collect(save, 1f, roll: 0.99, Noon, out _, out int insight, out _);
            CyclotronProgress.Collect(save, 0.5f, roll: 0.99, Noon.AddHours(1), out _, out _, out _);
            Assert.AreEqual(5, insight);
            Assert.AreEqual(8, save.cyclotronInsight);
        }

        [Test]
        public void Upgrade_SpendsInsight_AndRaisesTheLevel()
        {
            var save = new SaveData { cyclotronInsight = 10 };
            Assert.IsTrue(CyclotronProgress.TryUpgrade(save, CyclotronProgress.Upgrade.Boost));       // costs 3
            Assert.AreEqual(1, save.cyclotronBoostLevel);
            Assert.AreEqual(7, save.cyclotronInsight);
            Assert.IsTrue(CyclotronProgress.TryUpgrade(save, CyclotronProgress.Upgrade.Cooldown));    // costs 4
            Assert.AreEqual(1, save.cyclotronCooldownLevel);
            Assert.AreEqual(3, save.cyclotronInsight);
            Assert.IsFalse(CyclotronProgress.TryUpgrade(save, CyclotronProgress.Upgrade.Boost), "next costs 5");
            Assert.AreEqual(3, save.cyclotronInsight, "a refused upgrade spends nothing");
        }

        [Test]
        public void Upgrade_StopsAtTheMaxLevel()
        {
            var save = new SaveData { cyclotronInsight = 999, cyclotronCooldownLevel = CyclotronProgress.MaxCooldownLevel };
            Assert.IsTrue(CyclotronProgress.IsMaxed(save, CyclotronProgress.Upgrade.Cooldown));
            Assert.IsFalse(CyclotronProgress.TryUpgrade(save, CyclotronProgress.Upgrade.Cooldown));
            Assert.AreEqual(999, save.cyclotronInsight);
        }

        [Test]
        public void UpgradeCosts_Rise()
        {
            Assert.AreEqual(3, CyclotronProgress.UpgradeCost(CyclotronProgress.Upgrade.Boost, 0));
            Assert.AreEqual(5, CyclotronProgress.UpgradeCost(CyclotronProgress.Upgrade.Boost, 1));
            Assert.AreEqual(4, CyclotronProgress.UpgradeCost(CyclotronProgress.Upgrade.Cooldown, 0));
            Assert.AreEqual(7, CyclotronProgress.UpgradeCost(CyclotronProgress.Upgrade.Cooldown, 1));
        }

        [Test]
        public void BonusCrystals_OnlyForAGreatRun_AndALuckyRoll()
        {
            Assert.AreEqual(CyclotronProgress.BonusCrystals, CyclotronProgress.BonusCrystalsFor(0.95f, 0.1));
            Assert.AreEqual(0, CyclotronProgress.BonusCrystalsFor(0.95f, 0.9));
            Assert.AreEqual(0, CyclotronProgress.BonusCrystalsFor(0.5f, 0.0));

            var save = new SaveData();
            CyclotronProgress.Collect(save, 1f, roll: 0.0, Noon, out _, out _, out int crystals);
            Assert.AreEqual(CyclotronProgress.BonusCrystals, crystals);
            Assert.AreEqual(CyclotronProgress.BonusCrystals, save.paidCurrency);
        }

        [Test]
        public void ResearchPrice_NoLongerTakesACyclotronDiscount()
        {
            // Insight used to discount research; it is now an upgrade currency only.
            Assert.AreEqual(9500L, ResearchCost.Effective(10000, 0.05f));
        }

        // ── Save ───────────────────────────────────────────────────────────

        [Test]
        public void OldSave_WithoutCyclotronFields_LoadsAsNeverPlayed()
        {
            var save = UnityEngine.JsonUtility.FromJson<SaveData>("{\"prestigeCount\": 3, \"researchInsight\": 0.3}");
            Assert.IsTrue(string.IsNullOrEmpty(save.cyclotronLastRunUtc));
            Assert.AreEqual(0, save.cyclotronInsight);
            Assert.AreEqual(0, save.cyclotronBoostLevel);
            Assert.AreEqual(1f, CyclotronProgress.BoostMultiplier(save, Noon));
            Assert.IsTrue(CyclotronProgress.IsReady(save, Noon), "a first run is available straight away");
        }

        [Test]
        public void CyclotronFields_RoundTrip()
        {
            var save = new SaveData
            {
                cyclotronLastRunUtc      = "2026-11-14T12:00:00.0000000Z",
                cyclotronBoostExpiryUtc  = "2026-11-14T12:05:00.0000000Z",
                cyclotronBoostMultiplier = 2.4f,
                cyclotronInsight         = 7,
                cyclotronBoostLevel      = 2,
                cyclotronCooldownLevel   = 1,
            };
            var back = UnityEngine.JsonUtility.FromJson<SaveData>(UnityEngine.JsonUtility.ToJson(save));
            Assert.AreEqual(7, back.cyclotronInsight);
            Assert.AreEqual(2, back.cyclotronBoostLevel);
            Assert.AreEqual(1, back.cyclotronCooldownLevel);
            Assert.AreEqual(2.4f, CyclotronProgress.BoostMultiplier(back, Noon.AddMinutes(2)), 1e-6f);
            Assert.AreEqual(TimeSpan.FromMinutes(40), CyclotronProgress.CooldownLeft(back, Noon.AddMinutes(15)), "55 min at level 1");
        }

        // ── UI text ────────────────────────────────────────────────────────

        [Test]
        public void StatusText_ShowsCountdownAndBoost()
        {
            string s = CyclotronController.StatusText(TimeSpan.FromSeconds(754), 55, 5);
            StringAssert.Contains("Next run in 12m 34s (one run every 55 min)", s);
            StringAssert.Contains("boost lasts 5 min", s);
            string active = CyclotronController.StatusText(TimeSpan.FromMinutes(50), 60, 5, 2.4f, TimeSpan.FromSeconds(192));
            StringAssert.Contains("Boost active: x2.4 entropy for 3m 12s", active);
            StringAssert.Contains("Ready to run", CyclotronController.StatusText(TimeSpan.Zero, 60, 5));
        }

        [Test]
        public void IntroText_DescribesTheBoostAndInsight()
        {
            StringAssert.Contains("crosses the vertical gap", CyclotronController.IntroText);
            StringAssert.Contains("entropy boost", CyclotronController.IntroText);
            StringAssert.DoesNotContain("research", CyclotronController.IntroText);
            StringAssert.DoesNotContain("dees", CyclotronController.IntroText);
        }

        [Test]
        public void DrawerLabel_ShowsBoostReadyOrMinutesLeft()
        {
            Assert.AreEqual("Cyclotron (ready)", CyclotronController.DrawerLabel(TimeSpan.Zero));
            Assert.AreEqual("Cyclotron (42m)", CyclotronController.DrawerLabel(TimeSpan.FromSeconds(41 * 60 + 5)), "rounds up");
            Assert.AreEqual("Cyclotron (1m)", CyclotronController.DrawerLabel(TimeSpan.FromSeconds(20)));
            Assert.AreEqual("Cyclotron (x2.4 4m)",
                CyclotronController.DrawerLabel(TimeSpan.FromMinutes(50), 2.4f, TimeSpan.FromSeconds(200)));
        }

        [Test]
        public void FormatMult_DropsTheDecimalForWholeNumbers()
        {
            Assert.AreEqual("x3", CyclotronController.FormatMult(3f));
            Assert.AreEqual("x1.5", CyclotronController.FormatMult(1.5f));
        }

        [Test]
        public void ResultText_NamesTheBoostAndInsight()
        {
            string s = CyclotronController.ResultText(1f, 3, 2, 6);
            StringAssert.Contains("(3 kicks, 2 perfect)", s);
            StringAssert.Contains("x3 entropy for 6 min", s);
            StringAssert.Contains("+5 Insight", s);
            StringAssert.Contains("(1 kick, 1 perfect)", CyclotronController.ResultText(0.02f, 1, 1, 5));
        }

        [Test]
        public void UpgradeText_ShowsNowAndNext_OrMax()
        {
            Assert.AreEqual("Longer Boost (Lv 2): boost lasts 7 min -> 8 min",
                CyclotronController.UpgradeText(CyclotronProgress.Upgrade.Boost, 2));
            Assert.AreEqual("Faster Recharge (Lv 6, max): one run every 30 min",
                CyclotronController.UpgradeText(CyclotronProgress.Upgrade.Cooldown, 6));
        }

        [TestCase("btn-cyclotron")]
        [TestCase("cyclotron-modal")]
        [TestCase("cyclotron__view")]
        [TestCase("cyclotron__start")]
        [TestCase("cyclotron__collect")]
        [TestCase("cyclotron__insight")]
        [TestCase("cyclotron__boost-upgrade")]
        [TestCase("cyclotron__cooldown-upgrade")]
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
