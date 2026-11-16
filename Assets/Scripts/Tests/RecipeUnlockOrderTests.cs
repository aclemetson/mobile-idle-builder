using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// A recipe must not unlock before the player can make its inputs. Found 2026-11-15: Molecular Synthesis
    /// unlocked Water and Ammonia, whose Oxygen and Nitrogen only arrive five and six research nodes later
    /// (Alpha Process, Mid-Period Elements), so the Molecular Synthesizer showed two dead recipes.
    ///
    /// Walks research in the order the economy model uses (scripts/progression_curve.py): repeatedly take the
    /// ready node with the lowest (block order, cost, id). After each unlock, every unlocked recipe whose inputs
    /// are makeable becomes usable (its output and byproducts become makeable). A recipe that is unlocked but
    /// still unusable at the end of its own unlock step is out of order. Items no recipe makes come from fields.
    ///
    /// Runs on the imported ScriptableObjects (what the game reads), so re-import game_data.json first.
    /// </summary>
    [TestFixture]
    public class RecipeUnlockOrderTests
    {
        /// <summary>
        /// Recipes allowed to unlock before their inputs (a deliberate exception, with a reason next to each).
        /// Empty since 2026-11-15, when the neutron-capture chain was reordered to Heavy Transition Metals ->
        /// r-Process -> Lanthanides -> Exotic Metals -> Precious Metals. <see cref="KnownOutOfOrderList_IsNotStale"/>
        /// fails once a listed recipe unlocks in order.
        /// </summary>
        static readonly HashSet<string> KnownOutOfOrder = new();

        List<RecipeSO>   _recipes;
        ResearchDatabaseSO _db;

        [OneTimeSetUp]
        public void Load()
        {
            _recipes = AssetDatabase.FindAssets("t:RecipeSO", new[] { "Assets/Data/recipes" })
                .Select(g => AssetDatabase.LoadAssetAtPath<RecipeSO>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(r => r != null)
                .ToList();
            var dbGuid = AssetDatabase.FindAssets("t:ResearchDatabaseSO").FirstOrDefault();
            _db = dbGuid == null ? null : AssetDatabase.LoadAssetAtPath<ResearchDatabaseSO>(AssetDatabase.GUIDToAssetPath(dbGuid));
            Assert.That(_recipes.Count, Is.GreaterThan(100), "recipe assets not found; import game data first");
            Assert.IsNotNull(_db, "ResearchDatabaseSO not found; import game data first");
        }

        static IEnumerable<ItemSO> Makes(RecipeSO r)
        {
            if (r.outputItem != null) yield return r.outputItem;
            if (r.byproducts != null)
                foreach (var b in r.byproducts) if (b.item != null) yield return b.item;
        }

        /// <summary>Recipe id -> reason, for every recipe still unusable right after the step that unlocks it.</summary>
        Dictionary<string, string> OutOfOrder()
        {
            var madeByRecipe = new HashSet<ItemSO>(_recipes.SelectMany(Makes));
            bool FromField(ItemSO item) => !madeByRecipe.Contains(item);

            var makeable = new HashSet<ItemSO>();
            var unlocked = new List<RecipeSO>(_recipes.Where(r => r.requiredResearch == null || r.knownFromStart));
            var usable   = new HashSet<RecipeSO>();

            void Settle()
            {
                bool progress = true;
                while (progress)
                {
                    progress = false;
                    foreach (var r in unlocked)
                    {
                        if (usable.Contains(r)) continue;
                        if (r.inputs != null && r.inputs.Any(i => i.item != null && !FromField(i.item) && !makeable.Contains(i.item)))
                            continue;
                        usable.Add(r); progress = true;
                        foreach (var m in Makes(r)) makeable.Add(m);
                    }
                }
            }

            var blockOrder = (_db.blocks ?? new ResearchBlock[0]).ToDictionary(b => b.id ?? "", b => b.order);
            var all   = _db.allResearch.Where(r => r != null).ToList();
            var owned = new HashSet<ResearchSO>();
            var bad   = new Dictionary<string, string>();
            Settle();
            while (true)
            {
                var ready = all.Where(r => !owned.Contains(r)
                                           && (r.prerequisites == null || r.prerequisites.All(p => p == null || owned.Contains(p))))
                               .ToList();
                if (ready.Count == 0) break;
                var node = ready.OrderBy(r => blockOrder.TryGetValue(r.block ?? "", out int o) ? o : 0)
                                .ThenBy(r => r.costBaseCurrency)
                                .ThenBy(r => r.id, System.StringComparer.Ordinal)
                                .First();
                owned.Add(node);
                var fresh = _recipes.Where(r => r.requiredResearch == node && !r.knownFromStart).ToList();
                unlocked.AddRange(fresh);
                Settle();
                foreach (var r in fresh.Where(r => !usable.Contains(r)))
                {
                    var missing = r.inputs.Where(i => i.item != null && !FromField(i.item) && !makeable.Contains(i.item))
                                          .Select(i => i.item.name);
                    bad[r.id] = $"{r.id} (unlocked by {node.id}) is missing {string.Join(", ", missing)}";
                }
            }
            return bad;
        }

        [Test]
        public void EveryRecipe_UnlocksNoEarlierThanItsInputs()
        {
            var bad = OutOfOrder().Where(kv => !KnownOutOfOrder.Contains(kv.Key)).Select(kv => kv.Value).ToList();
            Assert.IsEmpty(bad, "Recipes unlocked before their inputs can be made:\n" + string.Join("\n", bad));
        }

        [Test]
        public void KnownOutOfOrderList_IsNotStale()
        {
            var stillBad = OutOfOrder();
            var fixedNow = KnownOutOfOrder.Where(id => !stillBad.ContainsKey(id)).ToList();
            Assert.IsEmpty(fixedNow, "These now unlock in order; remove them from KnownOutOfOrder: " + string.Join(", ", fixedNow));
        }

        [Test]
        public void NeutronCaptureChain_RunsInInputOrder()
        {
            var db = _db.allResearch.Where(r => r != null).ToDictionary(r => r.id);
            void Requires(string node, string prereq) =>
                Assert.IsTrue(db[node].prerequisites.Any(p => p != null && p.id == prereq), $"{node} should require {prereq}");
            Requires("heavy_transition_metals",      "slow_neutron_capture");   // Samarium from Cerium (fission)
            Requires("rapid_neutron_capture",        "heavy_transition_metals"); // Europium needs Samarium
            Requires("lanthanides_and_heavy_metals", "rapid_neutron_capture");  // Gadolinium..Rhenium from Europium
            Requires("exotic_metals",                "lanthanides_and_heavy_metals"); // Osmium needs Rhenium
            Requires("precious_metals",              "exotic_metals");          // Platinum needs Iridium
            Assert.AreEqual("precious_metals", _recipes.First(r => r.id == "s_capture_mercury").requiredResearch?.id,
                            "Mercury (Gold + neutron) unlocks with Gold");
        }

        [Test]
        public void WaterAndAmmonia_UnlockWithOxygenAndNitrogen()
        {
            var water   = _recipes.First(r => r.id == "water");
            var ammonia = _recipes.First(r => r.id == "ammonia");
            Assert.AreEqual("fusion_ii",    water.requiredResearch?.id,   "Water unlocks with Oxygen (Alpha Process)");
            Assert.AreEqual("mid_elements", ammonia.requiredResearch?.id, "Ammonia unlocks with Nitrogen (Mid-Period Elements)");
        }
    }
}
