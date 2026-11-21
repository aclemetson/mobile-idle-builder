using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// 2026-11-19: Feedstock Buffers was merged into Surplus Containment, which now unlocks both the Output and
    /// the Input Capacity upgrades and costs 2x its curve position (cost_multiplier, rebalance_curve.py).
    /// Saves that bought or were researching Feedstock Buffers get Surplus Containment instead.
    /// </summary>
    [TestFixture]
    public class SurplusContainmentMergeTests
    {
        [System.Serializable] class GdResearch { public string id; public float cost_multiplier; public List<string> unlocks_research; }
        [System.Serializable] class GdRoot { public List<GdResearch> research = new(); }

        GdRoot _data;

        [OneTimeSetUp]
        public void Load() =>
            _data = JsonUtility.FromJson<GdRoot>(System.IO.File.ReadAllText("Assets/Data/game_data.json"));

        [Test]
        public void FeedstockBuffers_IsGone_AndNothingPointsAtIt()
        {
            Assert.IsFalse(_data.research.Any(r => r.id == "feedstock_buffers"));
            Assert.IsFalse(_data.research.Any(r => r.unlocks_research != null && r.unlocks_research.Contains("feedstock_buffers")));
        }

        [Test]
        public void SurplusContainment_CostsMoreThanItsPosition() =>
            Assert.Greater(_data.research.Single(r => r.id == "surplus_containment").cost_multiplier, 1f);

        [Test]
        public void EveryMergedId_IsGone_AndPointsAtALiveNode()
        {
            foreach (var kv in ResearchService.MergedResearch)
            {
                Assert.IsFalse(_data.research.Any(r => r.id == kv.Key), kv.Key);
                Assert.IsTrue(_data.research.Any(r => r.id == kv.Value), kv.Value);
            }
        }

        [Test]
        public void Migrate_OwnedFeedstock_BecomesSurplusContainment()
        {
            var save = new SaveData { unlockedResearch = new List<string> { "automation_i", "feedstock_buffers" } };
            ResearchService.MigrateMergedResearch(save);
            CollectionAssert.AreEquivalent(new[] { "automation_i", "surplus_containment" }, save.unlockedResearch);
        }

        [Test]
        public void Migrate_OwnedBoth_KeepsOneSurplusContainment()
        {
            var save = new SaveData { unlockedResearch = new List<string> { "surplus_containment", "feedstock_buffers" } };
            ResearchService.MigrateMergedResearch(save);
            CollectionAssert.AreEquivalent(new[] { "surplus_containment" }, save.unlockedResearch);
        }

        [Test]
        public void Migrate_ResearchingFeedstock_ResearchesSurplusContainment()
        {
            var save = new SaveData
            {
                unlockedResearch = new List<string> { "automation_i" },
                activeResearchId = "feedstock_buffers",
                activeResearchCompleteUtc = "2026-11-19T12:00:00.0000000Z",
            };
            ResearchService.MigrateMergedResearch(save);
            Assert.AreEqual("surplus_containment", save.activeResearchId);
            Assert.IsNotNull(save.activeResearchCompleteUtc);
        }

        [Test]
        public void Migrate_ResearchingFeedstock_WhenAlreadyOwned_DropsTheTimer()
        {
            var save = new SaveData
            {
                unlockedResearch = new List<string> { "surplus_containment" },
                activeResearchId = "feedstock_buffers",
                activeResearchCompleteUtc = "2026-11-19T12:00:00.0000000Z",
            };
            ResearchService.MigrateMergedResearch(save);
            Assert.IsTrue(string.IsNullOrEmpty(save.activeResearchId));
            Assert.IsTrue(string.IsNullOrEmpty(save.activeResearchCompleteUtc));
        }

        [Test]
        public void Migrate_NullAndCleanSaves_AreUntouched()
        {
            Assert.DoesNotThrow(() => ResearchService.MigrateMergedResearch(null));
            var save = new SaveData { unlockedResearch = new List<string> { "automation_i" } };
            ResearchService.MigrateMergedResearch(save);
            CollectionAssert.AreEqual(new[] { "automation_i" }, save.unlockedResearch);
        }
    }
}
