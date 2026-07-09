using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Biology track content (Chem/Bio Biology track), B1 slice: the new Biology research branch,
    /// the biology_lab world gate, the organic fields + site_bio_lab, the Organic Harvester /
    /// Biosynthesizer buildings, and the B1 Biomolecule items. Loads generated assets, so it also
    /// exercises the Biology enum additions end-to-end. Run MobileIdleBuilder > Import Game Data first.
    /// </summary>
    [TestFixture]
    public class BiologyContentTests
    {
        // ── Enum additions ─────────────────────────────────────────────────────

        [Test]
        public void BiologyEnums_Exist()
        {
            Assert.IsTrue(System.Enum.IsDefined(typeof(ResearchBranch), "Biology"), "ResearchBranch.Biology");
            Assert.IsTrue(System.Enum.IsDefined(typeof(ItemCategory), "Biomolecule"), "ItemCategory.Biomolecule");
            Assert.IsTrue(System.Enum.IsDefined(typeof(ItemCategory), "CellPart"), "ItemCategory.CellPart");
            Assert.IsTrue(System.Enum.IsDefined(typeof(ItemCategory), "Organism"), "ItemCategory.Organism");
        }

        // ── biology_lab research gates the Biology world ───────────────────────

        [Test]
        public void BiologyLabResearch_GatesWorld()
        {
            var rdb = Resources.Load<ResearchDatabaseSO>("ResearchDatabase");
            Assert.IsNotNull(rdb, "ResearchDatabase missing — run Import Game Data");
            var lab = System.Array.Find(rdb.allResearch, r => r != null && r.id == "biology_lab");
            Assert.IsNotNull(lab, "biology_lab research missing");
            Assert.AreEqual(10000000, lab.costBaseCurrency, "biology_lab gate = 10,000,000e");
            Assert.AreEqual(ResearchBranch.Biology, lab.branch, "biology_lab is on the Biology branch");
            Assert.IsNotNull(System.Array.Find(lab.prerequisites, p => p != null && p.id == "biochem_precursors"),
                "biology_lab requires biochem_precursors (Chemistry C4)");

            var wdb = Resources.Load<WorldDatabaseSO>("WorldDatabase");
            var bio = System.Array.Find(wdb.allWorlds, w => w != null && w.id == "world_biology");
            Assert.IsNotNull(bio, "world_biology missing");
            CollectionAssert.Contains(bio.prereqUnlockIds, "biology_lab",
                "world_biology is gated by the biology_lab research");
            Assert.AreEqual(0, bio.unlockCost, "world unlock cost moved onto the research node");
            CollectionAssert.Contains(bio.siteIds, "site_bio_lab", "world_biology owns site_bio_lab");
        }

        // ── site_bio_lab introduces the organic fields, zeroes the physics fields ─

        [Test]
        public void BiologySite_IntroducesOrganicFields()
        {
            var siteDb = Resources.Load<SiteDatabaseSO>("SiteDatabase");
            Assert.IsNotNull(siteDb, "Resources/SiteDatabase.asset missing — run Import Game Data");
            var bio = System.Array.Find(siteDb.allSites, s => s != null && s.id == "site_bio_lab");
            Assert.IsNotNull(bio, "site_bio_lab missing from SiteDatabase");

            var byField = new Dictionary<string, SiteFieldOverride>();
            foreach (var ov in bio.fieldOverrides)
                if (ov?.field != null) byField[ov.field.id] = ov;

            // Physics fields removed.
            foreach (var removed in new[] { "quark_field", "electron_field" })
            {
                Assert.IsTrue(byField.ContainsKey(removed), $"expected {removed} override present");
                Assert.AreEqual(0f, byField[removed].densityMultiplier, $"{removed} should be removed (0)");
            }

            // Organic fields introduced with type "Organic", each dropping its OrganicCompound.
            var expectedDrop = new Dictionary<string, string>
            {
                { "sugar_field",      "glucose" },
                { "amino_acid_field", "amino_acid" },
                { "lipid_field",      "fatty_acid" },
                { "nucleotide_field", "nucleotide" },
            };
            foreach (var kvp in expectedDrop)
            {
                string fid = kvp.Key;
                Assert.IsTrue(byField.ContainsKey(fid), $"organic field '{fid}' not introduced on site_bio_lab");
                var field = byField[fid].field;
                Assert.Greater(byField[fid].densityMultiplier, 0f, $"{fid} should have a positive count");
                Assert.AreEqual("Organic", field.fieldType, $"{fid}.fieldType should be 'Organic'");
                Assert.IsNotEmpty(field.drops, $"{fid} should drop items");
                bool dropsExpected = false;
                foreach (var d in field.drops)
                {
                    Assert.IsNotNull(d.item, $"{fid} has an unresolved drop");
                    Assert.AreEqual(ItemCategory.OrganicCompound, d.item.category,
                        $"{fid} should drop OrganicCompound items (got {d.item.id})");
                    if (d.item.id == kvp.Value) dropsExpected = true;
                }
                Assert.IsTrue(dropsExpected, $"{fid} should drop {kvp.Value}");
            }
        }

        // ── Buildings gated by biology_lab, harvester on Organic fields ────────

        [Test]
        public void BiologyBuildings_ExistAndAreGated()
        {
            var bdb = Resources.Load<BuildingDatabaseSO>("BuildingDatabase");
            Assert.IsNotNull(bdb, "BuildingDatabase missing");

            var harvester = System.Array.Find(bdb.allBuildings, b => b != null && b.id == "organic_harvester");
            Assert.IsNotNull(harvester, "organic_harvester missing");
            CollectionAssert.Contains(harvester.compatibleFields, "Organic",
                "organic_harvester places on Organic fields");
            Assert.AreEqual("biology_lab", harvester.requiredResearch?.id, "organic_harvester gated by biology_lab");
            Assert.IsNotEmpty(harvester.supportedRecipes, "organic_harvester has harvest recipes");

            var synth = System.Array.Find(bdb.allBuildings, b => b != null && b.id == "biosynthesizer");
            Assert.IsNotNull(synth, "biosynthesizer missing");
            Assert.AreEqual("biology_lab", synth.requiredResearch?.id, "biosynthesizer gated by biology_lab");
            var outputs = System.Array.ConvertAll(synth.supportedRecipes,
                r => r != null && r.outputItem != null ? r.outputItem.id : null);
            foreach (var b in new[] { "protein", "carbohydrate", "lipid_membrane", "nucleic_acid" })
                CollectionAssert.Contains(outputs, b, $"biosynthesizer makes {b}");
        }

        // ── B1 items are Biomolecules ──────────────────────────────────────────

        [Test]
        public void B1BiomoleculeItems_ExistWithCategory()
        {
            var items = Resources.LoadAll<ItemSO>("Items");
            foreach (var id in new[] { "protein", "carbohydrate", "lipid_membrane", "nucleic_acid" })
            {
                var item = System.Array.Find(items, i => i.id == id);
                Assert.IsNotNull(item, $"B1 biomolecule '{id}' missing");
                Assert.AreEqual(ItemCategory.Biomolecule, item.category, $"{id} is a Biomolecule");
            }
        }
    }
}
