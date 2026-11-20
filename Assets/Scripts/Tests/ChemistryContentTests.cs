using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Chem/Bio Phase 2 content: the OrganicCompound items, the element fields, and the first
    /// Chemistry site (site_chem_lab). Loads generated assets, so it also exercises the field-type
    /// string refactor end-to-end (FieldSO.fieldType is a string "Element", resolved through the
    /// importer). Depends on generated assets — run MobileIdleBuilder > Import Game Data first.
    /// </summary>
    [TestFixture]
    public class ChemistryContentTests
    {
        [Test]
        public void OrganicCompoundItems_ExistWithCategory()
        {
            var items = Resources.LoadAll<ItemSO>("Items");
            Assert.IsNotEmpty(items, "No ItemSO assets found in Resources/Items/");

            foreach (var id in new[] { "glucose", "amino_acid", "fatty_acid", "nucleotide" })
            {
                var item = items.FirstOrDefault(i => i.id == id);
                Assert.IsNotNull(item, $"OrganicCompound item '{id}' missing");
                Assert.AreEqual(ItemCategory.OrganicCompound, item.category,
                    $"item '{id}' should be category OrganicCompound");
            }
        }

        [Test]
        public void ChemistrySite_IntroducesElementFields_WithStringType()
        {
            var siteDb = Resources.Load<SiteDatabaseSO>("SiteDatabase");
            Assert.IsNotNull(siteDb, "Resources/SiteDatabase.asset missing — run Import Game Data");

            var chem = System.Array.Find(siteDb.allSites, s => s != null && s.id == "site_chem_lab");
            Assert.IsNotNull(chem, "site_chem_lab missing from SiteDatabase");

            // Collect the resolved field overrides by field id.
            var byField = new Dictionary<string, SiteFieldOverride>();
            foreach (var ov in chem.fieldOverrides)
                if (ov?.field != null) byField[ov.field.id] = ov;

            // Physics fields removed (density 0), element fields introduced (absolute count > 0).
            foreach (var removed in new[] { "quark_field", "electron_field" })
            {
                Assert.IsTrue(byField.ContainsKey(removed), $"expected {removed} override present");
                Assert.AreEqual(0f, byField[removed].densityMultiplier, $"{removed} should be removed (0)");
            }

            // 2026-11-19 natural feedstock: the map starts with hydrogen and oxygen gas only. Graphite, nitrogen,
            // minerals and metals are listed at density 0 (known, so they can be bought and restored from a save)
            // and bought from the Build menu after their research. Metal stays "ElementMetal" (its own harvester).
            var expected = new (string id, string type, bool onMapAtStart, string research)[]
            {
                ("hydrogen_gas_field",    "Element",      true,  ""),
                ("oxygen_gas_field",      "Element",      true,  ""),
                ("graphite_field",        "Element",      false, "molecular_synthesis"),
                ("nitrogen_gas_field",    "Element",      false, "nitrogen_fixation"),
                ("element_field_mineral", "Element",      false, "reaction_engineering"),
                ("element_field_metal",   "ElementMetal", false, "reaction_engineering"),
            };
            foreach (var (ef, type, atStart, research) in expected)
            {
                Assert.IsTrue(byField.ContainsKey(ef), $"field '{ef}' not listed on site_chem_lab");
                var field = byField[ef].field;
                if (atStart) Assert.Greater(byField[ef].densityMultiplier, 0f, $"{ef} is on the map from the start");
                else
                {
                    Assert.AreEqual(0f, byField[ef].densityMultiplier, $"{ef} is bought, not placed up front");
                    Assert.AreEqual(research, field.purchaseResearchId, $"{ef} is for sale after {research}");
                    Assert.Greater(field.purchaseCostBase, 0, $"{ef} has a price");
                }
                Assert.AreEqual(type, field.fieldType, $"{ef}.fieldType");
                Assert.AreEqual(WorldGate.Chemistry, field.world, $"{ef} belongs to Chemistry");
                Assert.IsNotEmpty(field.drops, $"{ef} should drop items");
                foreach (var d in field.drops) Assert.IsNotNull(d.item, $"{ef} has an unresolved drop");
            }
            Assert.IsFalse(byField.ContainsKey("element_field_light"), "the atom seam is retired");
        }

        [Test]
        public void ChemistryWorld_OwnsChemSite()
        {
            var worldDb = Resources.Load<WorldDatabaseSO>("WorldDatabase");
            Assert.IsNotNull(worldDb, "Resources/WorldDatabase.asset missing");
            var chem = System.Array.Find(worldDb.allWorlds, w => w != null && w.id == "world_chemistry");
            Assert.IsNotNull(chem, "world_chemistry missing");
            CollectionAssert.Contains(chem.siteIds, "site_chem_lab",
                "world_chemistry must own site_chem_lab (Phase 2 wiring)");
        }

        // ── Phase 4a: C1 research / buildings / compounds ──────────────────────

        [Test]
        public void ChemistryLabResearch_IsFirstNodeInsideChemistry()
        {
            var rdb = Resources.Load<ResearchDatabaseSO>("ResearchDatabase");
            Assert.IsNotNull(rdb, "ResearchDatabase missing");
            var lab = System.Array.Find(rdb.allResearch, r => r != null && r.id == "chemistry_lab");
            Assert.IsNotNull(lab, "chemistry_lab research missing");
            Assert.AreEqual(ResearchBranch.Chemistry, lab.branch);
            Assert.AreEqual("world_chemistry", lab.requiredWorld, "chemistry_lab is the first node inside Chemistry");
            Assert.IsTrue(lab.prerequisites == null || lab.prerequisites.Length == 0, "the world is its only gate");

            // 2026-11-19: the world opens from the delivery tasks (WorldTaskSO), not from research or entropy.
            var wdb = Resources.Load<WorldDatabaseSO>("WorldDatabase");
            var chem = System.Array.Find(wdb.allWorlds, w => w != null && w.id == "world_chemistry");
            Assert.IsTrue(chem.unlockByTasks, "world_chemistry unlocks from its delivery tasks");
            CollectionAssert.IsEmpty(chem.prereqUnlockIds);
            Assert.AreEqual(0, chem.unlockCost, "no entropy fee: the tasks are the cost");
        }

        [Test]
        public void ChemistryBuildings_ExistAndAreGated()
        {
            var bdb = Resources.Load<BuildingDatabaseSO>("BuildingDatabase");
            Assert.IsNotNull(bdb, "BuildingDatabase missing");

            var harvester = System.Array.Find(bdb.allBuildings, b => b != null && b.id == "element_harvester");
            Assert.IsNotNull(harvester, "element_harvester missing");
            CollectionAssert.Contains(harvester.compatibleFields, "Element",
                "element_harvester places on Element fields");
            Assert.AreEqual("chemistry_lab", harvester.requiredResearch?.id, "element_harvester gated by chemistry_lab");
            Assert.IsNotEmpty(harvester.supportedRecipes, "element_harvester has harvest recipes");

            var synth = System.Array.Find(bdb.allBuildings, b => b != null && b.id == "compound_synthesizer");
            Assert.IsNotNull(synth, "compound_synthesizer missing");
            Assert.AreEqual("reaction_engineering", synth.requiredResearch?.id, "compound_synthesizer comes with the minerals");
            var outputs = System.Array.ConvertAll(synth.supportedRecipes,
                r => r != null && r.outputItem != null ? r.outputItem.id : null);
            foreach (var c in new[] { "table_salt", "sulfuric_acid" })
                CollectionAssert.Contains(outputs, c, $"compound_synthesizer makes {c}");

            // CO2 is graphite burnt in oxygen, so it moved to the Molecular Synthesizer with Carbon Chemistry.
            var mol = System.Array.Find(bdb.allBuildings, b => b != null && b.id == "molecular_synthesizer");
            Assert.AreEqual("chemistry_lab", mol.requiredResearch?.id, "the Molecular Synthesizer opens Chemistry (water)");
            CollectionAssert.Contains(System.Array.ConvertAll(mol.supportedRecipes, r => r?.outputItem?.id), "carbon_dioxide");
        }

        [Test]
        public void C1CompoundItems_ExistAsMolecules()
        {
            var items = Resources.LoadAll<ItemSO>("Items");
            foreach (var id in new[] { "carbon_dioxide", "table_salt", "sulfuric_acid" })
            {
                var item = System.Array.Find(items, i => i.id == id);
                Assert.IsNotNull(item, $"C1 compound '{id}' missing");
                Assert.AreEqual(ItemCategory.Molecule, item.category, $"{id} is a Molecule");
            }
        }

        // ── Phase 4b: C2-C4 research chain / buildings / precursor recipes ─────

        [Test]
        public void ChemistryC2C4Research_IsChainedAfterLab()
        {
            var rdb = Resources.Load<ResearchDatabaseSO>("ResearchDatabase");
            Assert.IsNotNull(rdb, "ResearchDatabase missing");

            ResearchSO Find(string id) => System.Array.Find(rdb.allResearch, r => r != null && r.id == id);

            // chemistry_lab (H2, O2, water) -> molecular_synthesis (Carbon Chemistry) -> nitrogen_fixation
            // -> reaction_engineering -> organic_chemistry -> biochem_precursors
            var expected = new (string id, string prereq, int cost, ResearchBranch branch)[]
            {
                ("molecular_synthesis",  "chemistry_lab",        0, ResearchBranch.Chemistry),
                ("nitrogen_fixation",    "molecular_synthesis",  0, ResearchBranch.Chemistry),
                ("reaction_engineering", "nitrogen_fixation",    0, ResearchBranch.Chemistry),
                ("organic_chemistry",    "reaction_engineering", 0, ResearchBranch.Chemistry),
                ("biochem_precursors",   "organic_chemistry",    0, ResearchBranch.Chemistry),
            };
            foreach (var (id, prereq, cost, branch) in expected)
            {
                var node = Find(id);
                Assert.IsNotNull(node, $"{id} research missing");
                Assert.AreEqual(branch, node.branch, $"{id} branch");
                Assert.Greater(node.costBaseCurrency, Find(prereq).costBaseCurrency, $"{id} costs more than {prereq}");
                Assert.IsNotNull(System.Array.Find(node.prerequisites, p => p != null && p.id == prereq),
                    $"{id} must require {prereq}");
            }

            // Each step points at the next in the research tree's "unlocks" list.
            foreach (var (from, to) in new[] { ("chemistry_lab", "molecular_synthesis"), ("molecular_synthesis", "nitrogen_fixation"),
                                               ("nitrogen_fixation", "reaction_engineering") })
                Assert.IsNotNull(System.Array.Find(Find(from).unlocksResearch, r => r != null && r.id == to),
                    $"{from} must unlock {to}");
        }

        [Test]
        public void ChemistryC2C4Buildings_ExistAndAreGated()
        {
            var bdb = Resources.Load<BuildingDatabaseSO>("BuildingDatabase");
            Assert.IsNotNull(bdb, "BuildingDatabase missing");

            BuildingSO Find(string id) => System.Array.Find(bdb.allBuildings, b => b != null && b.id == id);

            var metal = Find("metal_harvester");
            Assert.IsNotNull(metal, "metal_harvester missing");
            CollectionAssert.Contains(metal.compatibleFields, "ElementMetal",
                "metal_harvester places on ElementMetal fields");
            Assert.AreEqual("reaction_engineering", metal.requiredResearch?.id, "metal_harvester gated by reaction_engineering");

            var reactor = Find("catalytic_reactor");
            Assert.IsNotNull(reactor, "catalytic_reactor missing");
            Assert.AreEqual("reaction_engineering", reactor.requiredResearch?.id, "catalytic_reactor gated by reaction_engineering");

            var organic = Find("organic_synthesizer");
            Assert.IsNotNull(organic, "organic_synthesizer missing");
            Assert.AreEqual("organic_chemistry", organic.requiredResearch?.id, "organic_synthesizer gated by organic_chemistry");
        }

        [Test]
        public void C4Recipes_ProduceOrganicCompounds_GatedByBiochemPrecursors()
        {
            var bdb = Resources.Load<BuildingDatabaseSO>("BuildingDatabase");
            var organic = System.Array.Find(bdb.allBuildings, b => b != null && b.id == "organic_synthesizer");
            Assert.IsNotNull(organic, "organic_synthesizer missing");

            // The four forward-declared OrganicCompound items are produced here, gated by biochem_precursors.
            foreach (var outId in new[] { "glucose", "amino_acid", "fatty_acid", "nucleotide" })
            {
                var recipe = System.Array.Find(organic.supportedRecipes,
                    r => r != null && r.outputItem != null && r.outputItem.id == outId);
                Assert.IsNotNull(recipe, $"organic_synthesizer must make {outId}");
                Assert.AreEqual("biochem_precursors", recipe.requiredResearch?.id,
                    $"{outId} recipe gated by biochem_precursors");
                Assert.AreEqual(ItemCategory.OrganicCompound, recipe.outputItem.category,
                    $"{outId} is an OrganicCompound");
            }
        }
    }
}
