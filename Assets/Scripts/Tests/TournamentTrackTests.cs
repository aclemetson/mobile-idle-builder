using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #500 part 2: the tournament track, bought with Strings. Star Resonance (+2% per star), Lucky Draw (+1 star
    /// per reward), Surge Luck (+5% surge crystal chance) and Harmonics (+10% Strings).
    /// </summary>
    public class TournamentTrackTests
    {
        [TearDown]
        public void TearDown() => ItemStars.Invalidate();

        static SaveData With(params string[] levels) =>
            new SaveData { pveTrack = new List<string>(levels) };

        [Test]
        public void Catalogue_MatchesThePlan()
        {
            CollectionAssert.AreEqual(new[] { 20, 40, 80, 160, 320 }, TournamentTrack.Find(TournamentTrack.StarResonance).costs);
            CollectionAssert.AreEqual(new[] { 50, 150, 400 }, TournamentTrack.Find(TournamentTrack.LuckyDraw).costs);
            CollectionAssert.AreEqual(new[] { 30, 90, 270 }, TournamentTrack.Find(TournamentTrack.SurgeLuck).costs);
            CollectionAssert.AreEqual(new[] { 40, 120, 360 }, TournamentTrack.Find(TournamentTrack.Harmonics).costs);
        }

        [Test]
        public void TryBuy_SpendsStrings_AndRaisesTheLevel_UntilMax()
        {
            var save = new SaveData { pveStrings = 70 };
            Assert.IsTrue(TournamentTrack.TryBuy(save, TournamentTrack.StarResonance));   // 20
            Assert.IsTrue(TournamentTrack.TryBuy(save, TournamentTrack.StarResonance));   // 40
            Assert.AreEqual(10, save.pveStrings);
            Assert.AreEqual(2, TournamentTrack.Level(save, TournamentTrack.StarResonance));
            Assert.IsFalse(TournamentTrack.TryBuy(save, TournamentTrack.StarResonance), "80 > 10: can't afford");
            Assert.AreEqual(1, save.pveTrack.Count, "one entry per upgrade");

            var rich = With("lucky_draw:3");
            rich.pveStrings = 10_000;
            Assert.AreEqual(-1, TournamentTrack.NextCost(rich, TournamentTrack.LuckyDraw));
            Assert.IsFalse(TournamentTrack.TryBuy(rich, TournamentTrack.LuckyDraw), "maxed");
            Assert.AreEqual(10_000, rich.pveStrings);
        }

        [Test]
        public void StarResonance_RaisesEveryStarsValue()
        {
            Assert.AreEqual(0f, TournamentTrack.StarBonus(new SaveData()));
            Assert.AreEqual(0.06f, TournamentTrack.StarBonus(With("star_resonance:3")), 1e-6f);
            Assert.AreEqual(2f, ItemStars.MultiplierFor(5, ItemStars.BaseBonusPerStar), 1e-6f, "5 stars double the value");
            Assert.AreEqual(2.3f, ItemStars.MultiplierFor(5, ItemStars.BaseBonusPerStar + 0.06f), 1e-5f, "+6% x 5 stars");
        }

        [Test]
        public void LuckyDraw_AndHarmonics_ApplyOnTheClaim()
        {
            var save = With("lucky_draw:2", "harmonics:3");
            var reward = new PveReward { week = "w1", stars = 3, strings = 60 };
            var landed = TournamentRules.ApplyReward(save, reward, new List<string> { "carbon_12" }, new System.Random(2));
            Assert.AreEqual(5, landed.Count, "3 + 2 Lucky Draw");
            Assert.AreEqual(78, save.pveStrings, "60 x 1.3");
        }

        [Test]
        public void SurgeLuck_RaisesTheCrystalChance()
        {
            Assert.AreEqual(0.20f, AdRewardCalculator.SurgeCrystalChance(new SaveData()), 1e-6f);
            var save = With("surge_luck:3");
            Assert.AreEqual(0.35f, AdRewardCalculator.SurgeCrystalChance(save), 1e-6f);
            Assert.IsTrue(AdRewardCalculator.SurgeRollsCrystals(0.30, save));
            Assert.IsFalse(AdRewardCalculator.SurgeRollsCrystals(0.30));
            Assert.IsFalse(AdRewardCalculator.SurgeRollsCrystals(0.35, save));
            StringAssert.Contains("(65%) or 10 ◆ crystals (35%)",
                EntropySurgeOfferController.MessageText(1000, AdRewardCalculator.Def(AdPlacement.EntropySurge), false, 0.35f));
        }

        [Test]
        public void Track_SurvivesASave_AndPrestige()
        {
            var save = With("harmonics:2");
            var loaded = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            loaded.currentRun = new CurrentRunData();
            Assert.AreEqual(2, TournamentTrack.Level(loaded, TournamentTrack.Harmonics));
        }

        [Test]
        public void LevelText()
        {
            Assert.AreEqual("Lv 2 / 5", TournamentPanelText.TrackLevelText(2, 5));
            Assert.AreEqual("Lv 3 / 3  MAX", TournamentPanelText.TrackLevelText(3, 3));
        }
    }
}
