using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Validates the Power Relay building + its Power Distribution research gate authored in
    /// game_data.json (the single source of truth), and the generator/relay radius split. Parses the raw
    /// JSON directly so it does not depend on the Editor importer having generated the assets (same
    /// approach as ManagerDataTests).
    /// </summary>
    [TestFixture]
    public class PowerRelayDataTests
    {
        [Serializable] private class Root
        {
            public List<Building> buildings = new();
            public List<Research> research  = new();
        }

        [Serializable] private class Building
        {
            public string id;
            public int    building_id;
            public string category;
            public bool   is_power_source;
            public float  base_output_ev;
            public float  influence_radius_tiles;
            public int[]  footprint;
            public string structure_kind;
            public string required_research;
            public bool   available_from_start;
        }

        [Serializable] private class Research
        {
            public string id;
            public List<string> unlocks_buildings = new();
            public List<string> prerequisites     = new();
        }

        static Root LoadData()
        {
            string path = Path.Combine(Application.dataPath, "Data/game_data.json");
            Assert.IsTrue(File.Exists(path), $"game_data.json not found at {path}");
            return JsonUtility.FromJson<Root>(File.ReadAllText(path));
        }

        static Building Find(Root data, string id)
        {
            var b = data.buildings.Find(x => x.id == id);
            Assert.IsNotNull(b, $"building '{id}' missing from game_data.json");
            return b;
        }

        // ── Structure enum ────────────────────────────────────────────────────

        [Test]
        public void BuildingStructureKind_HasPowerRelay()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(BuildingStructureKind), "PowerRelay"),
                "BuildingStructureKind must define PowerRelay so BuildingVisualizer can build its structure");
        }

        // ── Power Relay building ──────────────────────────────────────────────

        [Test]
        public void PowerRelay_IsASpreaderNotASource()
        {
            var relay = Find(LoadData(), "power_relay");
            Assert.IsTrue(relay.is_power_source, "relay must be a power node so its radius is a coverage box");
            Assert.AreEqual(0f, relay.base_output_ev, 0.0001f, "relay must add NO eV to the shared supply");
            Assert.AreEqual("PowerRelay", relay.structure_kind, "relay must use the PowerRelay structure");
            Assert.AreEqual("Power", relay.category);
        }

        [Test]
        public void PowerRelay_HasLargerRadiusThanGenerator()
        {
            var data = LoadData();
            var relay = Find(data, "power_relay");
            var gen   = Find(data, "basic_generator");
            Assert.Greater(relay.influence_radius_tiles, gen.influence_radius_tiles,
                "the spreader (relay) must reach further than the generator");
        }

        [Test]
        public void PowerRelay_HasUniqueTallFootprint()
        {
            var data = LoadData();
            var relay = Find(data, "power_relay");
            Assert.AreEqual(new[] { 1, 2 }, relay.footprint, "relay footprint should be the distinct 1x2 mast");

            // No other building shares the 1x2 footprint (visual/shape uniqueness).
            foreach (var b in data.buildings)
            {
                if (b.id == "power_relay" || b.footprint == null || b.footprint.Length != 2) continue;
                bool same = b.footprint[0] == 1 && b.footprint[1] == 2;
                Assert.IsFalse(same, $"building '{b.id}' also uses the 1x2 footprint — relay shape is no longer unique");
            }
        }

        [Test]
        public void PowerRelay_IsGatedByPowerDistribution()
        {
            var data = LoadData();
            var relay = Find(data, "power_relay");
            Assert.IsFalse(relay.available_from_start, "relay must not be available from start");
            Assert.AreEqual("power_distribution", relay.required_research, "relay must be gated by power_distribution");

            var research = data.research.Find(r => r.id == "power_distribution");
            Assert.IsNotNull(research, "power_distribution research missing from game_data.json");
            Assert.Contains("power_relay", research.unlocks_buildings, "power_distribution must unlock power_relay");
            Assert.Contains("atomic_assembly", research.prerequisites, "power_distribution should follow atomic_assembly");
        }

        [Test]
        public void PowerRelay_HasUniqueBuildingId()
        {
            var data = LoadData();
            var relay = Find(data, "power_relay");
            foreach (var b in data.buildings)
                if (b.id != "power_relay")
                    Assert.AreNotEqual(relay.building_id, b.building_id,
                        $"building '{b.id}' collides with the relay's building_id {relay.building_id}");
        }

        // ── Generator radius shrank ───────────────────────────────────────────

        [Test]
        public void BasicGenerator_RadiusShrank()
        {
            var gen = Find(LoadData(), "basic_generator");
            Assert.LessOrEqual(gen.influence_radius_tiles, 2f,
                "the generator's L1 radius was reduced so it generates power but reaches short");
        }
    }
}
