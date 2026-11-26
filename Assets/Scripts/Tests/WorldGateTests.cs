using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// WorldGate (2026-11-19 Chemistry gate): research with a required world waits for that world, and a
    /// building tagged with a world is only offered while that world is active.
    /// </summary>
    [TestFixture]
    public class WorldGateTests
    {
        readonly List<Object> _made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) Object.DestroyImmediate(o);
            _made.Clear();
        }

        ResearchSO Research(string world)
        {
            var r = ScriptableObject.CreateInstance<ResearchSO>(); _made.Add(r);
            r.id = "r"; r.requiredWorld = world;
            return r;
        }

        BuildingSO Building(string world)
        {
            var b = ScriptableObject.CreateInstance<BuildingSO>(); _made.Add(b);
            b.world = world;
            return b;
        }

        [Test]
        public void Physics_AndNoWorld_AreAlwaysOpen()
        {
            Assert.IsTrue(WorldGate.IsOpen(null, null));
            Assert.IsTrue(WorldGate.IsOpen("", new List<string>()));
            Assert.IsTrue(WorldGate.IsOpen(WorldGate.Physics, null));
        }

        [Test]
        public void Chemistry_OpensOnlyOnceUnlocked()
        {
            Assert.IsFalse(WorldGate.IsOpen(WorldGate.Chemistry, null));
            Assert.IsFalse(WorldGate.IsOpen(WorldGate.Chemistry, new List<string> { "world_biology" }));
            Assert.IsTrue(WorldGate.IsOpen(WorldGate.Chemistry, new List<string> { WorldGate.Chemistry }));
        }

        [Test]
        public void ResearchOpen_FollowsItsWorld()
        {
            Assert.IsTrue(WorldGate.ResearchOpen(Research(""), null));
            Assert.IsFalse(WorldGate.ResearchOpen(Research(WorldGate.Chemistry), new List<string>()));
            Assert.IsTrue(WorldGate.ResearchOpen(Research(WorldGate.Chemistry), new List<string> { WorldGate.Chemistry }));
            Assert.IsFalse(WorldGate.ResearchOpen(null, null));
        }

        [Test]
        public void SharedBuilding_IsInEveryWorld()
        {
            var belt = Building("");
            Assert.IsTrue(WorldGate.BuildingInWorld(belt, WorldGate.Physics));
            Assert.IsTrue(WorldGate.BuildingInWorld(belt, WorldGate.Chemistry));
        }

        [Test]
        public void WorldBuilding_IsOnlyInItsWorld()
        {
            var synth = Building(WorldGate.Chemistry);
            Assert.IsTrue(WorldGate.BuildingInWorld(synth, WorldGate.Chemistry));
            Assert.IsFalse(WorldGate.BuildingInWorld(synth, WorldGate.Physics));
            Assert.IsFalse(WorldGate.BuildingInWorld(synth, null), "no active world means Physics");
            Assert.IsTrue(WorldGate.BuildingInWorld(Building(WorldGate.Physics), null));
            Assert.IsFalse(WorldGate.BuildingInWorld(null, WorldGate.Physics));
        }

        [Test]
        public void RecipeInWorld_FollowsItsBuildings()
        {
            var shared = Building(""); shared.id = "combiner";
            var synth  = Building(WorldGate.Chemistry); synth.id = "synth";
            var map = new Dictionary<string, BuildingSO> { ["combiner"] = shared, ["synth"] = synth };
            BuildingSO Lookup(string id) => map.TryGetValue(id, out var b) ? b : null;

            var water = new List<string> { "synth" };
            Assert.IsFalse(WorldGate.RecipeInWorld(water, Lookup, WorldGate.Physics), "Chemistry recipe listed in Physics");
            Assert.IsFalse(WorldGate.RecipeInWorld(water, Lookup, null), "no active world means Physics");
            Assert.IsTrue(WorldGate.RecipeInWorld(water, Lookup, WorldGate.Chemistry));

            var proton = new List<string> { RecipeDatabase.InventoryMenuSource, "combiner" };
            Assert.IsTrue(WorldGate.RecipeInWorld(proton, Lookup, WorldGate.Physics));
            Assert.IsTrue(WorldGate.RecipeInWorld(proton, Lookup, WorldGate.Chemistry));

            // The inventory menu alone, no buildings, or an unknown building: shared.
            Assert.IsTrue(WorldGate.RecipeInWorld(new List<string> { RecipeDatabase.InventoryMenuSource }, Lookup, WorldGate.Physics));
            Assert.IsTrue(WorldGate.RecipeInWorld(null, Lookup, WorldGate.Physics));
            Assert.IsTrue(WorldGate.RecipeInWorld(new List<string> { "unknown" }, Lookup, WorldGate.Physics));
            // Made in a Chemistry building OR a shared one: available in both.
            Assert.IsTrue(WorldGate.RecipeInWorld(new List<string> { "synth", "combiner" }, Lookup, WorldGate.Physics));
        }

        /// <summary>On the real data: no Chemistry or Biology recipe reaches the Physics Recipes panel.</summary>
        [Test]
        public void RealData_NoMoleculeRecipe_InPhysics()
        {
            var recipes  = Resources.Load<RecipeDatabaseSO>("RecipeDatabase")?.allRecipes;
            Assert.IsNotNull(recipes, "RecipeDatabase missing: run Import Game Data");
            foreach (var r in recipes)
            {
                if (r?.requiredResearch == null) continue;
                var branch = r.requiredResearch.branch;
                if (branch != ResearchBranch.Chemistry && branch != ResearchBranch.Biology) continue;
                var craftedIn = new List<string>();
                if (r.validBuildings != null) foreach (var b in r.validBuildings) if (b != null) craftedIn.Add(b.id);
                var byId = new Dictionary<string, BuildingSO>();
                if (r.validBuildings != null) foreach (var b in r.validBuildings) if (b != null) byId[b.id] = b;
                Assert.IsFalse(WorldGate.RecipeInWorld(craftedIn, id => byId.TryGetValue(id, out var b) ? b : null, WorldGate.Physics),
                    $"{r.id} ({branch}) would be listed in the Physics Recipes panel");
            }
        }

        [Test]
        public void DisplayName_DropsThePrefix()
        {
            Assert.AreEqual("Chemistry", WorldGate.DisplayName(WorldGate.Chemistry));
            Assert.AreEqual("Biology", WorldGate.DisplayName("world_biology"));
            Assert.AreEqual("", WorldGate.DisplayName(null));
        }
    }
}
