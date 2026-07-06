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

            foreach (var ef in new[] { "element_field_light", "element_field_metal", "element_field_mineral" })
            {
                Assert.IsTrue(byField.ContainsKey(ef), $"element field '{ef}' not introduced on site_chem_lab");
                var field = byField[ef].field;
                Assert.Greater(byField[ef].densityMultiplier, 0f, $"{ef} should have a positive count");
                Assert.AreEqual("Element", field.fieldType,
                    $"{ef}.fieldType must be the string 'Element' (field-type refactor)");
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
    }
}
