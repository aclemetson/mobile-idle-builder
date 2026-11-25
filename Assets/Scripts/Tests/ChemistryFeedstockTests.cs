using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Chemistry runs on natural feedstock (2026-11-19, scripts/chemistry_feedstock.py): hydrogen, oxygen and
    /// nitrogen as gases and carbon as graphite, mined from Chemistry's own fields, never loose atoms. Items and
    /// machines belong to one world: Physics production buildings are built in Physics only, Chemistry's in
    /// Chemistry only, and each world sells only its own fields.
    /// </summary>
    [TestFixture]
    public class ChemistryFeedstockTests
    {
        RecipeSO[] _recipes;
        BuildingSO[] _buildings;
        FieldSO[] _fields;

        [OneTimeSetUp]
        public void Load()
        {
            _recipes   = Resources.Load<RecipeDatabaseSO>("RecipeDatabase")?.allRecipes?.Where(r => r != null).ToArray();
            _buildings = Resources.Load<BuildingDatabaseSO>("BuildingDatabase")?.allBuildings?.Where(b => b != null).ToArray();
            Assert.IsNotNull(_recipes, "RecipeDatabase missing: run Import Game Data");
            Assert.IsNotNull(_buildings, "BuildingDatabase missing: run Import Game Data");
            var sites = Resources.Load<SiteDatabaseSO>("SiteDatabase");
            _fields = sites != null
                ? sites.allSites.Where(s => s?.fieldOverrides != null).SelectMany(s => s.fieldOverrides)
                       .Select(o => o.field).Where(f => f != null).Distinct().ToArray()
                : new FieldSO[0];
        }

        static bool InWorld(BuildingSO b, string world) => b.world == world;

        IEnumerable<RecipeSO> MadeIn(string world) =>
            _recipes.Where(r => r.validBuildings != null && r.validBuildings.Any(b => b != null && InWorld(b, world)));

        [Test]
        public void ChemistryAndBiology_NeverUseLooseAtoms()
        {
            var atoms = new HashSet<string> { "hydrogen", "carbon", "nitrogen", "oxygen" };
            foreach (var r in MadeIn(WorldGate.Chemistry).Concat(MadeIn("world_biology")))
            {
                foreach (var i in r.inputs ?? new RecipeIngredient[0])
                    Assert.IsFalse(i.item != null && atoms.Contains(i.item.id),
                        $"{r.id} takes atomic {i.item?.id}; use hydrogen_gas / oxygen_gas / nitrogen_gas / graphite");
                Assert.IsFalse(r.outputItem != null && atoms.Contains(r.outputItem.id), $"{r.id} mines atomic {r.outputItem?.id}");
            }
        }

        /// <summary>Atom counts from an item's symbol ("C₁₀H₁₆N₅O₁₃P₃"); null when it is not a plain formula.</summary>
        static Dictionary<string, int> Atoms(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return null;
            var plain = new string(symbol.Select(ch => ch >= '₀' && ch <= '₉' ? (char)('0' + (ch - '₀')) : ch).ToArray());
            if (!System.Text.RegularExpressions.Regex.IsMatch(plain, @"^([A-Z][a-z]?\d*)+$")) return null;
            var counts = new Dictionary<string, int>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(plain, @"([A-Z][a-z]?)(\d*)"))
            {
                int n = m.Groups[2].Value.Length > 0 ? int.Parse(m.Groups[2].Value) : 1;
                counts[m.Groups[1].Value] = (counts.TryGetValue(m.Groups[1].Value, out var c) ? c : 0) + n;
            }
            return counts;
        }

        /// <summary>
        /// Every Chemistry reaction that makes a molecule balances atom for atom, byproducts included (2026-11-23
        /// life chemistry: 36 real equations, scripts/item_timeline.py CHEM). Formulas come from the item symbols.
        /// </summary>
        [Test]
        public void EveryChemistryReaction_Balances()
        {
            int checkedCount = 0;
            foreach (var r in MadeIn(WorldGate.Chemistry).Where(r => r.outputItem != null
                         && (r.outputItem.category == ItemCategory.Molecule || r.outputItem.category == ItemCategory.OrganicCompound)
                         && r.inputs != null && r.inputs.Length > 0))
            {
                var left = new Dictionary<string, int>();
                var right = new Dictionary<string, int>();
                void Add(Dictionary<string, int> side, ItemSO item, int q)
                {
                    var f = Atoms(item.symbol);
                    Assert.IsNotNull(f, $"{r.id}: {item.id} has no plain formula symbol ('{item.symbol}')");
                    foreach (var kv in f) side[kv.Key] = (side.TryGetValue(kv.Key, out var c) ? c : 0) + kv.Value * q;
                }
                foreach (var i in r.inputs) Add(left, i.item, i.quantity);
                Add(right, r.outputItem, Mathf.Max(1, r.outputQuantity));
                foreach (var b in r.byproducts ?? new RecipeIngredient[0]) Add(right, b.item, b.quantity);
                CollectionAssert.AreEquivalent(left, right, $"{r.id}: inputs and outputs don't balance");
                checkedCount++;
            }
            Assert.GreaterOrEqual(checkedCount, 36, "the life-chemistry reactions are all checked");
        }

        [Test]
        public void PhysicsProductionBuildings_StayInPhysics()
        {
            foreach (var id in new[] { "harvester", "nucleon_harvester", "strong_force_combiner", "atomic_assembler",
                                       "isotopic_manipulator", "radioactive_containment", "fusion_reactor",
                                       "fission_reactor", "neutron_capture" })
            {
                var b = _buildings.Single(x => x.id == id);
                Assert.IsTrue(WorldGate.BuildingInWorld(b, WorldGate.Physics), $"{id} is built in Physics");
                Assert.IsFalse(WorldGate.BuildingInWorld(b, WorldGate.Chemistry), $"{id} is not built in Chemistry");
            }
            // Logistics and the Demon work everywhere (the Demon is switched off at runtime in stream worlds).
            foreach (var b in _buildings.Where(x => x.isItemFilter || x.IsFixedSink))
                Assert.IsTrue(string.IsNullOrEmpty(b.world), $"{b.id} is shared by every world");
            // Chemistry runs on streams, not eV: power is Physics-only.
            foreach (var b in _buildings.Where(x => x.isPowerSource || x.id == "power_relay"))
                Assert.AreEqual(WorldGate.Physics, b.world, $"{b.id} is built in Physics only");
        }

        [Test]
        public void NoMolecule_IsMadeInPhysics()
        {
            foreach (var r in _recipes.Where(r => r.outputItem != null && r.outputItem.category == ItemCategory.Molecule))
                Assert.IsFalse(r.validBuildings.Any(b => b != null && WorldGate.BuildingInWorld(b, WorldGate.Physics)),
                    $"{r.id} makes a molecule in a building Physics can build");
        }

        [Test]
        public void EveryField_BelongsToOneWorld()
        {
            Assert.IsNotEmpty(_fields, "no site fields found");
            foreach (var f in _fields)
                Assert.IsFalse(string.IsNullOrEmpty(f.world), $"{f.id} has no world, so every world would sell it");
            var quarks = _fields.Single(f => f.id == "quark_field");
            Assert.IsTrue(WorldGate.FieldInWorld(quarks, null));
            Assert.IsFalse(WorldGate.FieldInWorld(quarks, WorldGate.Chemistry), "quark pairs aren't sold in Chemistry");
            var graphite = _fields.Single(f => f.id == "graphite_field");
            Assert.IsFalse(WorldGate.FieldInWorld(graphite, WorldGate.Physics), "graphite isn't sold in Physics");
        }
    }
}
