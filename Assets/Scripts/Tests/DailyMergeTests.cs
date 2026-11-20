using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// 2026-11-20: the Daily panel's rotating challenges were merged into the Daily achievements (four of the six
    /// were the same task as a Daily achievement, paying about 5x more). These guard the merged set on the real
    /// AchievementDatabase asset.
    /// </summary>
    [TestFixture]
    public class DailyMergeTests
    {
        AchievementSO[] _all;

        [OneTimeSetUp]
        public void Load()
        {
            var db = AssetDatabase.LoadAssetAtPath<AchievementDatabase>("Assets/Data/AchievementDatabase.asset");
            Assert.IsNotNull(db, "AchievementDatabase asset missing");
            _all = db.All.Where(a => a != null).ToArray();
        }

        AchievementSO[] Daily => _all.Where(a => a.category == AchievementCategory.Daily).ToArray();

        [Test]
        public void NoTwoAchievementsInACategory_AskForTheSameThing()
        {
            var dupes = _all.GroupBy(a => (a.category, a.triggerType, a.triggerTargetId ?? "", a.triggerQuantity))
                            .Where(g => g.Count() > 1)
                            .Select(g => string.Join(" = ", g.Select(a => a.id)))
                            .ToList();
            Assert.IsEmpty(dupes, "Duplicate achievements:\n" + string.Join("\n", dupes));
        }

        [Test]
        public void DailyTab_HasEveryOldChallengeTask()
        {
            // The six old challenges (login, craft 50, craft 200, place 3, research 1, spend 500) plus Daily's own.
            void Has(AchievementTrigger t, int qty) =>
                Assert.IsTrue(Daily.Any(a => a.triggerType == t && a.triggerQuantity == qty), $"no daily {t} x{qty}");
            Has(AchievementTrigger.Login, 1);
            Has(AchievementTrigger.CraftItem, 50);
            Has(AchievementTrigger.CraftItem, 200);
            Has(AchievementTrigger.PlaceBuilding, 3);
            Has(AchievementTrigger.CompleteResearch, 1);
            Has(AchievementTrigger.SpendEntropy, 500);
            Has(AchievementTrigger.Prestige, 1);
            Assert.IsTrue(Daily.Any(a => a.id == "daily_complete"), "the Day Well Spent bonus");
        }

        [Test]
        public void DailyTab_PaysAboutWhatTheOldPanelPaid()
        {
            // The old panel paid 3 challenges x 15 crystals = 45 a day; the merged tab keeps that income.
            Assert.AreEqual(45, Daily.Sum(a => a.paidCurrencyReward));
        }
        // Daily counting before the first prestige (and the rest waiting) is covered by AchievementResetTests.
    }
}
