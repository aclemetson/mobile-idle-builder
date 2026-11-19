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
        public void DisplayName_DropsThePrefix()
        {
            Assert.AreEqual("Chemistry", WorldGate.DisplayName(WorldGate.Chemistry));
            Assert.AreEqual("Biology", WorldGate.DisplayName("world_biology"));
            Assert.AreEqual("", WorldGate.DisplayName(null));
        }
    }
}
