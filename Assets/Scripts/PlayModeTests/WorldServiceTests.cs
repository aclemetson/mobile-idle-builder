using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// Phase 3 (worlds) tests for the pure gating layer: world unlock state and the economic
    /// prerequisite check. The ECS/scene side of WorldService (entropy deduction, the SwitchTo grid
    /// handoff which delegates to SiteService) is exercised by SiteServiceTests + the manual playtest.
    /// </summary>
    public class WorldServiceTests
    {
        private readonly List<WorldSO> _created = new();

        private WorldSO MakeWorld(string id, long cost = 0, params string[] prereqs)
        {
            var w = ScriptableObject.CreateInstance<WorldSO>();
            w.id = id;
            w.unlockCost = cost;
            w.prereqUnlockIds = new List<string>(prereqs);
            _created.Add(w);
            return w;
        }

        [TearDown]
        public void Teardown()
        {
            foreach (var w in _created)
                if (w != null) Object.DestroyImmediate(w);
            _created.Clear();
        }

        // ── IsUnlocked ─────────────────────────────────────────────────────────

        [Test]
        public void IsUnlocked_Index0_AlwaysUnlocked()
        {
            var worlds = new[] { MakeWorld("world_physics"), MakeWorld("world_chemistry", 150000) };
            Assert.IsTrue(WorldService.IsUnlocked(new SaveData(), worlds, 0), "physics (index 0) is implicit");
        }

        [Test]
        public void IsUnlocked_OtherWorld_RequiresUnlockedWorldsEntry()
        {
            var worlds = new[] { MakeWorld("world_physics"), MakeWorld("world_chemistry", 150000) };
            var save = new SaveData();
            Assert.IsFalse(WorldService.IsUnlocked(save, worlds, 1), "chemistry locked by default");

            save.unlockedWorlds.Add("world_chemistry");
            Assert.IsTrue(WorldService.IsUnlocked(save, worlds, 1), "chemistry unlocked once recorded");
        }

        [Test]
        public void IsUnlocked_OutOfRangeOrNull_False()
        {
            var worlds = new[] { MakeWorld("world_physics") };
            Assert.IsFalse(WorldService.IsUnlocked(new SaveData(), worlds, 5));
            Assert.IsFalse(WorldService.IsUnlocked(null, worlds, 1));
        }

        // ── PrereqsMet ─────────────────────────────────────────────────────────

        [Test]
        public void PrereqsMet_NoPrereqs_AlwaysTrue()
        {
            var physics = MakeWorld("world_physics");
            Assert.IsTrue(WorldService.PrereqsMet(new SaveData(), physics));
            Assert.IsTrue(WorldService.PrereqsMet(null, physics), "no prereqs = met even with null save");
        }

        [Test]
        public void PrereqsMet_MissingResearch_False()
        {
            var chem = MakeWorld("world_chemistry", 150000, "mid_elements");
            Assert.IsFalse(WorldService.PrereqsMet(new SaveData(), chem), "prereq research not yet unlocked");
        }

        [Test]
        public void PrereqsMet_ResearchUnlocked_True()
        {
            var chem = MakeWorld("world_chemistry", 150000, "mid_elements");
            var save = new SaveData();
            save.unlockedResearch.Add("mid_elements");
            Assert.IsTrue(WorldService.PrereqsMet(save, chem), "prereq satisfied once research is unlocked");
        }

        [Test]
        public void PrereqsMet_AllPrereqsRequired()
        {
            var world = MakeWorld("world_x", 0, "a", "b");
            var save = new SaveData();
            save.unlockedResearch.Add("a");
            Assert.IsFalse(WorldService.PrereqsMet(save, world), "one of two prereqs is not enough");
            save.unlockedResearch.Add("b");
            Assert.IsTrue(WorldService.PrereqsMet(save, world), "both prereqs satisfied");
        }
    }
}
