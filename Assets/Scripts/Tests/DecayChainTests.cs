using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #322 phase 1: working back from Uranium. The Decay Chamber turns an isotope into the element it decays
    /// into; the Isotopic Manipulator adds or strips neutrons. These keep the data physically consistent.
    /// </summary>
    [TestFixture]
    public class DecayChainTests
    {
        [Serializable] class GdIng      { public string item; public int quantity; }
        [Serializable] class GdItem     { public string id; public string category; public int atomic_number; public int atomic_mass; public string decay_type; public float base_sell_value; }
        [Serializable] class GdRecipe   { public string id; public string output_item; public List<GdIng> inputs; public List<GdIng> byproducts; public List<string> valid_buildings; public string required_research; }
        [Serializable] class GdBuilding { public string id; public bool assignable_outputs; public List<string> supported_recipes; }
        [Serializable] class GameData   { public List<GdItem> items; public List<GdRecipe> recipes; public List<GdBuilding> buildings; }

        GameData _gd;
        Dictionary<string, GdItem> _items;

        [OneTimeSetUp]
        public void Load()
        {
            _gd    = JsonUtility.FromJson<GameData>(File.ReadAllText("Assets/Data/game_data.json"));
            _items = _gd.items.ToDictionary(i => i.id);
        }

        IEnumerable<GdRecipe> In(string building) => _gd.recipes.Where(r => r.valid_buildings.Contains(building));

        [Test]
        public void EveryElementFromThalliumToProtactinium_ComesFromTheDecayChamber()
        {
            for (int z = 81; z <= 91; z++)
            {
                var el = _gd.items.Single(i => i.category == "Element" && i.atomic_number == z);
                var sources = _gd.recipes.Where(r => r.output_item == el.id).ToList();
                Assert.IsNotEmpty(sources, el.id);
                Assert.IsTrue(sources.All(r => r.valid_buildings.SequenceEqual(new[] { "radioactive_containment" })),
                    $"{el.id} must only come from decay, not the Atom Generator");
            }
        }

        [Test]
        public void Decays_ChangeTheElementTheWayTheirDecayTypeSays()
        {
            foreach (var r in In("radioactive_containment"))
            {
                var input  = _items[r.inputs.Single().item];
                var output = _items[r.output_item];
                int dz = output.atomic_number - input.atomic_number;
                var particles = r.byproducts.Select(b => b.item).ToList();

                if (particles.Contains("alpha_particle"))
                    Assert.AreEqual(-2, dz, $"{r.id}: alpha decay loses two protons");
                else if (input.decay_type == "Beta")
                    Assert.AreEqual(+1, dz, $"{r.id}: beta-minus turns a neutron into a proton");
                else if (input.decay_type == "BetaPlus")
                    Assert.AreEqual(-1, dz, $"{r.id}: beta-plus / electron capture turns a proton into a neutron");
                else
                    Assert.Fail($"{r.id}: no alpha particle and input {input.id} has decay type {input.decay_type}");
            }
        }

        [Test]
        public void Manipulator_AddsOneNeutron_OrReleasesExactlyTheNeutronsItStrips()
        {
            foreach (var r in In("isotopic_manipulator").Where(r => r.id.StartsWith("irradiate_") || r.id.StartsWith("strip_")))
            {
                var parent  = _items[r.inputs.First(i => i.item != "neutron").item];
                var isotope = _items[r.output_item];
                Assert.AreEqual(parent.atomic_number, isotope.atomic_number, $"{r.id}: same element, different neutrons");

                if (r.id.StartsWith("irradiate_"))
                {
                    Assert.AreEqual(1, r.inputs.Single(i => i.item == "neutron").quantity);
                    Assert.AreEqual(parent.atomic_mass + 1, isotope.atomic_mass, r.id);
                }
                else
                {
                    int released = r.byproducts.Single(b => b.item == "neutron").quantity;
                    Assert.AreEqual(parent.atomic_mass - released, isotope.atomic_mass,
                        $"{r.id}: every stripped neutron comes out as an item");
                }
            }
        }

        [Test]
        public void DecayChamber_LetsThePlayerAssignItsOutputs_AndANeutronFieldExists()
        {
            Assert.IsTrue(_gd.buildings.Single(b => b.id == "radioactive_containment").assignable_outputs);
            Assert.IsTrue(_gd.recipes.Any(r => r.id == "collect_neutron" && r.output_item == "neutron"
                                             && r.valid_buildings.Contains("harvester")));
        }

        // ── Runtime: real second outputs and per-side routing ────────────────

        [Test]
        public void ProducedOutputs_KeepsRealByproducts_DropsEscapingOnes()
        {
            var thorium = ScriptableObject.CreateInstance<ItemSO>();
            var alpha   = ScriptableObject.CreateInstance<ItemSO>();
            var nu      = ScriptableObject.CreateInstance<ItemSO>();
            nu.escapesAsEntropy = true;
            var r = ScriptableObject.CreateInstance<RecipeSO>();
            r.outputItem = thorium; r.outputQuantity = 1;
            r.byproducts = new[] { new RecipeIngredient { item = alpha, quantity = 1 }, new RecipeIngredient { item = nu, quantity = 1 } };
            try
            {
                var outs = r.ProducedOutputs();
                Assert.AreEqual(2, outs.Count);
                Assert.AreSame(thorium, outs[0].item);
                Assert.AreSame(alpha, outs[1].item, "the alpha particle is a real item that leaves the building");
            }
            finally
            {
                foreach (var o in new UnityEngine.Object[] { thorium, alpha, nu, r }) UnityEngine.Object.DestroyImmediate(o);
            }
        }

        [Test]
        public void BuildingWithAssignableOutputs_EachSideTakesOnlyItsProduct()
        {
            using var world = new World("DecayRouting");
            var em  = world.EntityManager;
            var sim = world.CreateSystemManaged<SimulationSystemGroup>();
            sim.AddSystemToUpdateList(world.CreateSystem<ConveyorSystem>());
            sim.SortSystems();

            const int East = 1, South = 2, Thorium = 90, Alpha = 18;
            var b = em.CreateEntity();
            em.AddComponentData(b, new BuildingData { IsActive = true, ProductionSpeed = 1f });
            em.AddComponentData(b, new GridPosition { Cell = new int2(1, 1) });
            em.AddComponentData(b, new BuildingInventoryConfig { InputCapacity = 10, OutputCapacity = 10 });
            em.AddBuffer<BuildingInputSlot>(b);
            em.AddBuffer<BuildingOutputSlot>(b);
            em.AddBuffer<PlacedPortData>(b);
            em.AddBuffer<OutputPortFilter>(b);
            var ports = em.GetBuffer<PlacedPortData>(b);
            ports.Add(new PlacedPortData { PortType = (int)PortType.Output, CellX = 1, CellY = 1, Facing = East });
            ports.Add(new PlacedPortData { PortType = (int)PortType.Output, CellX = 1, CellY = 1, Facing = South });
            FilterSystem.WriteRules(em.GetBuffer<OutputPortFilter>(b),
                new List<(int, int)> { (East, PortFilterRules.Everything), (South, Alpha) });
            var output = em.GetBuffer<BuildingOutputSlot>(b);
            SlotBufferUtils.AddToOutputBuffer(output, Alpha, 1);      // first in the buffer
            SlotBufferUtils.AddToOutputBuffer(em.GetBuffer<BuildingOutputSlot>(b), Thorium, 1);

            Entity Head(int2 cell, int dir)
            {
                var e = em.CreateEntity();
                em.AddComponentData(e, new ConveyorSegmentData { Cell = cell, EntryDir = dir, ExitDir = dir, TransportTime = 1f, IsChainHead = true });
                return e;
            }
            var east  = Head(new int2(2, 1), East);
            var south = Head(new int2(1, 0), South);

            world.SetTime(new TimeData(0, 0.1f));
            world.Update();

            Assert.AreEqual(Thorium, ConveyorItems.Front(em, east).ItemID, "the element leaves East");
            Assert.AreEqual(Alpha,   ConveyorItems.Front(em, south).ItemID, "the alpha particle leaves South");
        }
    }
}
