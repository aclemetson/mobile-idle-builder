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

            // Light/mineral are type "Element" (C1 Element Harvester works on them). Metal is retyped
            // "ElementMetal" (Phase 4a) so the C1 harvester can't idle-farm the high-value metals.
            var expectedType = new Dictionary<string, string>
            {
                { "element_field_light",   "Element" },
                { "element_field_mineral", "Element" },
                { "element_field_metal",   "ElementMetal" },
            };
            foreach (var kvp in expectedType)
            {
                string ef = kvp.Key;
                Assert.IsTrue(byField.ContainsKey(ef), $"element field '{ef}' not introduced on site_chem_lab");
                var field = byField[ef].field;
                Assert.Greater(byField[ef].densityMultiplier, 0f, $"{ef} should have a positive count");
                Assert.AreEqual(kvp.Value, field.fieldType, $"{ef}.fieldType");
                Assert.IsNotEmpty(field.drops, $"{ef} should drop items");
                foreach (var d in field.drops)
                {
                    Assert.IsNotNull(d.item, $"{ef} has an unresolved drop");
                    Assert.AreEqual(ItemCategory.Element, d.item.category,
                        $"{ef} should drop Element items (got {d.item.id})");
                }
            }
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
        public void ChemistryLabResearch_GatesWorld()
        {
            var rdb = Resources.Load<ResearchDatabaseSO>("ResearchDatabase");
            Assert.IsNotNull(rdb, "ResearchDatabase missing");
            var lab = System.Array.Find(rdb.allResearch, r => r != null && r.id == "chemistry_lab");
            Assert.IsNotNull(lab, "chemistry_lab research missing");
            Assert.AreEqual(150000, lab.costBaseCurrency, "chemistry_lab gate = 150000e");
            Assert.AreEqual(ResearchBranch.Chemistry, lab.branch);
            Assert.IsNotNull(System.Array.Find(lab.prerequisites, p => p != null && p.id == "mid_elements"),
                "chemistry_lab requires mid_elements");

            var wdb = Resources.Load<WorldDatabaseSO>("WorldDatabase");
            var chem = System.Array.Find(wdb.allWorlds, w => w != null && w.id == "world_chemistry");
            CollectionAssert.Contains(chem.prereqUnlockIds, "chemistry_lab",
                "world_chemistry is now gated by the chemistry_lab research");
            Assert.AreEqual(0, chem.unlockCost, "world unlock cost moved onto the research node");
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
            Assert.AreEqual("chemistry_lab", synth.requiredResearch?.id, "compound_synthesizer gated by chemistry_lab");
            var outputs = System.Array.ConvertAll(synth.supportedRecipes,
                r => r != null && r.outputItem != null ? r.outputItem.id : null);
            foreach (var c in new[] { "carbon_dioxide", "table_salt", "sulfuric_acid" })
                CollectionAssert.Contains(outputs, c, $"compound_synthesizer makes {c}");
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
    }
}
