using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The Ignition rebalance (docs/agents/tasks/feature-ignition-rebalance.md): Harvester speed upgrades
    /// apply, and quark-to-Carbon craft times are matched to their suppliers so every line runs full.
    /// </summary>
    [TestFixture]
    public class IgnitionRebalanceTests
    {
        // ── Harvester speed levels apply ─────────────────────────────────────

        private static BuildingSO Building(string id) =>
            AssetDatabase.LoadAssetAtPath<BuildingSO>($"Assets/Data/buildings/{id}.asset");

        [TestCase("harvester", 1, 1f)]
        [TestCase("harvester", 2, 2f)]
        [TestCase("harvester", 3, 4f)]
        [TestCase("harvester", 4, 8f)]
        [TestCase("nucleon_harvester", 2, 2f)]
        public void CollectorRate_FollowsTheSpeedLevel(string id, int level, float itemsPerSecond)
        {
            var so = Building(id);
            Assert.IsNotNull(so, id);
            Assert.AreEqual(itemsPerSecond, BuildingSO.CollectorRateForLevel(so, level), 1e-5f);
        }

        [Test]
        public void CollectorRate_UnknownLevelOrNoData_FallsBackToTheBaseRate()
        {
            var so = Building("harvester");
            Assert.AreEqual(so.baseOutputRate, BuildingSO.CollectorRateForLevel(so, 99), 1e-5f);
            Assert.AreEqual(1f, BuildingSO.CollectorRateForLevel(null, 3), 1e-5f);
        }

        [Test]
        public void OfflineEarnings_UseEachHarvestersLevel()
        {
            // One collector (type 1) at Lv3 belted into a sink (type 2). Offline used the type's base rate
            // for every harvester, so an upgraded one earned like a Lv1 while you were away.
            var grid = new GridSaveData
            {
                buildings = new List<BuildingSaveData>
                {
                    new BuildingSaveData { buildingId = 1, recipeId = 0, level = 3, position = new[] { 5, 5 } },
                    new BuildingSaveData { buildingId = 2, recipeId = -1, position = new[] { 9, 5 } },
                },
                conveyors = new List<ConveyorSaveData>
                    { new ConveyorSaveData { cells = new[] { 6, 5, 7, 5, 8, 5 } } }
            };
            var rates = new Dictionary<int, float> { { 1, 1f }, { 3, 4f } };

            var snap = IdleGraphAnalyzer.BuildSnapshot(
                grid,
                id => id == 1,
                id => id == 2,
                _ => Vector2Int.one,
                (_, __) => 10,
                _ => 1f,                     // the type's base rate: what offline used before
                _ => 1f,
                1f,
                DateTime.UtcNow.ToString("O"),
                getCollectorRate: bsd => rates[bsd.level]);

            Assert.AreEqual(1, snap.chains.Count);
            Assert.AreEqual(4f, snap.chains[0].itemsPerSecond, 1e-4f, "a Lv3 harvester earns 4/s offline, not 1/s");
        }

        // ── Craft times matched to suppliers ─────────────────────────────────

        [Serializable] class GdRecipe { public string id; public float base_craft_time; }
        [Serializable] class GameData { public List<GdRecipe> recipes; }

        [TestCase("proton", 2f)]
        [TestCase("neutron", 2f)]
        [TestCase("hydrogen", 2f)]
        [TestCase("assemble_deuterium", 2f)]
        [TestCase("helium_4", 4f)]
        [TestCase("fuse_deuterium", 4f)]
        [TestCase("fuse_helium", 4f)]
        [TestCase("fuse_carbon", 6f)]
        public void IgnitionCraftTimes_ArePinned(string recipeId, float seconds)
        {
            var gd = JsonUtility.FromJson<GameData>(File.ReadAllText("Assets/Data/game_data.json"));
            var r = gd.recipes.FirstOrDefault(x => x.id == recipeId);
            Assert.IsNotNull(r, recipeId);
            Assert.AreEqual(seconds, r.base_craft_time, 1e-5f,
                "Ignition craft times are matched to their suppliers; change them with the balance editor, not by hand");
        }

        [Test]
        public void OneLv1Harvester_FeedsOneLv1Combiner_Exactly()
        {
            // A proton takes 2 up quarks; at 2 s a Lv1 Combiner uses 1 up quark/s, one Lv1 Harvester's output.
            var gd = JsonUtility.FromJson<GameData>(File.ReadAllText("Assets/Data/game_data.json"));
            float proton = gd.recipes.First(x => x.id == "proton").base_craft_time;
            float harvester = BuildingSO.CollectorRateForLevel(Building("harvester"), 1);
            Assert.AreEqual(harvester, 2f / proton, 1e-5f);
        }
    }
}
