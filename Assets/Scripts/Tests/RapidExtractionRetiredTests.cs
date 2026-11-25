using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// 2026-11-24: Rapid Extraction I/II (field tap cooldown) left the tree; nobody taps fields once Automation I's
    /// harvesters arrive. Transportation I now follows Automation I, and saves drop the retired nodes (a running
    /// timer on one is refunded).
    /// </summary>
    public class RapidExtractionRetiredTests
    {
        [System.Serializable] class GdResearch { public string id; public List<string> prerequisites = new(); public List<string> unlocks_research = new(); }
        [System.Serializable] class GdRoot { public List<GdResearch> research = new(); }

        static GdRoot Data => JsonUtility.FromJson<GdRoot>(File.ReadAllText("Assets/Data/game_data.json"));

        [Test]
        public void Nodes_AreGone_AndNothingPointsAtThem()
        {
            var data = Data;
            Assert.IsFalse(data.research.Any(r => r.id.StartsWith("rapid_extraction")));
            Assert.IsFalse(data.research.Any(r =>
                (r.prerequisites ?? new List<string>()).Concat(r.unlocks_research ?? new List<string>())
                .Any(x => x.StartsWith("rapid_extraction"))));
        }

        [Test]
        public void TransportationI_FollowsAutomationI()
        {
            var data = Data;
            var t1 = data.research.Single(r => r.id == "transportation_1");
            CollectionAssert.AreEqual(new[] { "automation_i" }, t1.prerequisites);
            CollectionAssert.Contains(data.research.Single(r => r.id == "automation_i").unlocks_research, "transportation_1");
        }

        [Test]
        public void Save_DropsFinishedRetiredResearch_WithoutRefund()
        {
            var save = new SaveData { unlockedResearch = new List<string> { "recombination_i", "rapid_extraction_i", "rapid_extraction_ii" } };
            Assert.AreEqual(0, ResearchService.MigrateMergedResearch(save));
            CollectionAssert.AreEqual(new[] { "recombination_i" }, save.unlockedResearch);
        }

        [Test]
        public void Save_RunningRetiredResearch_IsCancelledAndRefunded()
        {
            var save = new SaveData
            {
                unlockedResearch = new List<string> { "rapid_extraction_i" },
                activeResearchId = "rapid_extraction_ii",
                activeResearchCompleteUtc = "2026-11-24T12:00:00.0000000Z",
            };
            Assert.AreEqual(48000, ResearchService.MigrateMergedResearch(save));
            Assert.IsNull(save.activeResearchId);
            Assert.IsNull(save.activeResearchCompleteUtc);
            Assert.IsEmpty(save.unlockedResearch);
            Assert.AreEqual(0, ResearchService.MigrateMergedResearch(save), "idempotent");
        }
    }
}
