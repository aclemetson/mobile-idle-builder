using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Free Research (#491): a prestige-shop upgrade whose levels let the first N research of each run skip
    /// their timer (the entropy is still paid). The per-run count lives on CurrentRunData, so prestige resets it.
    /// </summary>
    public class FreeResearchTests
    {
        [Test]
        public void Upgrade_IsTenLevels_OneResearchEach_AfterResearchOverdrive()
        {
            var def = PersistentUpgradeService.FindDef("free_research");
            Assert.IsNotNull(def);
            Assert.AreEqual(UpgradeEffectType.FreeResearch, def.effectType);
            Assert.AreEqual(1f, def.effectPerLevel);
            Assert.AreEqual(10, def.maxLevel);
            Assert.AreEqual(def.maxLevel, def.levelCosts.Length);
            CollectionAssert.Contains(def.prereqs, ("research_overdrive", 1));
            for (int i = 1; i < def.levelCosts.Length; i++)
                Assert.Greater(def.levelCosts[i], def.levelCosts[i - 1], "each level costs more than the last");
        }

        [TestCase(0f, 0, 0)]
        [TestCase(3f, 0, 3)]
        [TestCase(3f, 2, 1)]
        [TestCase(3f, 5, 0)]
        [TestCase(2.9999f, 0, 3)]
        [TestCase(1f, -4, 1)]
        public void Remaining_IsLevelsLessUsed_NeverNegative(float granted, int used, int expected) =>
            Assert.AreEqual(expected, FreeResearchRule.Remaining(granted, used));

        [Test]
        public void UsedCount_ResetsWithTheRun_AndSurvivesASave()
        {
            var save = new SaveData();
            save.currentRun.freeResearchUsed = 2;
            var loaded = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            Assert.AreEqual(2, loaded.currentRun.freeResearchUsed);

            Assert.AreEqual(0, new CurrentRunData().freeResearchUsed, "prestige builds a new run: the count restarts");
        }

        [Test]
        public void CardTime_SaysInstant_WhileFreeOnesRemain()
        {
            Assert.AreEqual("Instant", HUDController.ResearchTimeText(0, 0));
            Assert.AreEqual("Instant", HUDController.ResearchTimeText(0, 2), "already instant: no free one is used");
            Assert.AreEqual("Instant · 2 free left", HUDController.ResearchTimeText(90, 2));
            Assert.AreEqual("⏳ " + HUDController.FormatResearchTimer(90), HUDController.ResearchTimeText(90, 0));
        }
    }
}
