using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// Phase 2 (multi-grids) tests: site unlock rules, per-site grid/snapshot reservation,
    /// the switch-and-back data round-trip, and siteSnapshots JSON persistence. These cover
    /// the pure data layer — the ECS/scene side of SiteService.SwitchTo (ClearGrid/LoadGrid,
    /// field regen, entropy deduction) is exercised by the in-editor manual playtest.
    /// </summary>
    public class SiteServiceTests
    {
        private readonly List<SiteSO> _created = new();

        private readonly List<FieldSO> _createdFields = new();

        private SiteSO MakeSite(string id, long cost = 0)
        {
            var s = ScriptableObject.CreateInstance<SiteSO>();
            s.id = id;
            s.unlockCost = cost;
            _created.Add(s);
            return s;
        }

        private FieldSO MakeField(string id)
        {
            var f = ScriptableObject.CreateInstance<FieldSO>();
            f.id = id;
            _createdFields.Add(f);
            return f;
        }

        [TearDown]
        public void Teardown()
        {
            foreach (var s in _created)
                if (s != null) Object.DestroyImmediate(s);
            _created.Clear();
            foreach (var f in _createdFields)
                if (f != null) Object.DestroyImmediate(f);
            _createdFields.Clear();
        }

        private static GridSaveData GridWithBuilding(int buildingId)
        {
            var g = new GridSaveData();
            g.buildings.Add(new BuildingSaveData { buildingId = buildingId, position = new[] { 1, 1 } });
            return g;
        }

        // ── IsUnlocked ───────────────────────────────────────────────────────

        [Test]
        public void IsUnlocked_OriginAlwaysUnlocked_EvenWithNullSave()
        {
            Assert.IsTrue(SiteService.IsUnlocked(null, null, 0));
        }

        [Test]
        public void IsUnlocked_NonOrigin_RequiresIdInUnlockedSites()
        {
            var sites = new List<SiteSO> { MakeSite("site_origin"), MakeSite("site_quark_sea", 250000) };
            var save  = new SaveData();

            Assert.IsFalse(SiteService.IsUnlocked(save, sites, 1), "locked until id present");

            save.unlockedSites.Add("site_quark_sea");
            Assert.IsTrue(SiteService.IsUnlocked(save, sites, 1), "unlocked once id present");
        }

        [Test]
        public void IsUnlocked_OutOfRangeIndex_ReturnsFalse()
        {
            var sites = new List<SiteSO> { MakeSite("site_origin") };
            Assert.IsFalse(SiteService.IsUnlocked(new SaveData(), sites, 5));
        }

        // ── EnsureSiteGrid ───────────────────────────────────────────────────

        [Test]
        public void EnsureSiteGrid_PadsGridsList_UpToIndex()
        {
            var save = new SaveData();
            save.currentRun.grid = GridWithBuilding(42); // legacy grid → grids[0]

            var grid2 = GridSaveService.EnsureSiteGrid(save, 2);

            Assert.AreEqual(3, save.currentRun.grids.Count, "grids padded to indices 0,1,2");
            Assert.AreSame(save.currentRun.grids[2], grid2);
            Assert.AreEqual(42, save.currentRun.grids[0].buildings[0].buildingId, "grids[0] keeps legacy building");
        }

        [Test]
        public void EnsureSiteGrid_ExistingIndex_ReturnsSameGrid()
        {
            var save = new SaveData();
            save.currentRun.grids = new List<GridSaveData> { GridWithBuilding(1), GridWithBuilding(2) };

            var grid1 = GridSaveService.EnsureSiteGrid(save, 1);
            Assert.AreSame(save.currentRun.grids[1], grid1);
            Assert.AreEqual(2, save.currentRun.grids.Count, "no extra padding");
        }

        // ── Switch-and-back data round-trip ──────────────────────────────────

        [Test]
        public void SwitchAndBack_DataLayer_PreservesEachSiteGrid()
        {
            // Origin grid has a building; switch to a fresh site 1, then back — grids[0] intact.
            var save = new SaveData();
            save.currentRun.grids = new List<GridSaveData> { GridWithBuilding(7) };
            save.currentRun.activeSiteIndex = 0;

            // Switch to site 1 (fresh, empty grid reserved).
            GridSaveService.EnsureSiteGrid(save, 1);
            save.currentRun.activeSiteIndex = 1;
            Assert.AreEqual(0, save.currentRun.grids[1].buildings.Count, "site 1 starts empty");

            // Place something on site 1 to prove independence.
            save.currentRun.grids[1].buildings.Add(new BuildingSaveData { buildingId = 9, position = new[] { 2, 2 } });

            // Switch back to origin.
            save.currentRun.activeSiteIndex = 0;
            Assert.AreEqual(7, save.currentRun.ActiveGrid.buildings[0].buildingId, "origin building survived");

            // And site 1 still holds its own.
            Assert.AreEqual(9, save.currentRun.grids[1].buildings[0].buildingId);
        }

        // ── siteSnapshots mirroring + persistence ────────────────────────────

        [Test]
        public void MirrorActiveSiteSnapshot_WritesActiveIndexEntry()
        {
            var save = new SaveData();
            save.currentRun.activeSiteIndex = 2;
            save.idleSnapshot = new IdleCollectionSnapshot
                { chains = new List<IdleChainEntry> { new IdleChainEntry { itemId = 5 } } };

            GridSaveService.MirrorActiveSiteSnapshot(save);

            Assert.AreEqual(3, save.siteSnapshots.Count, "padded to active index");
            Assert.AreSame(save.idleSnapshot, save.siteSnapshots[2], "active entry aliases idleSnapshot");
            Assert.AreEqual(5, save.siteSnapshots[2].chains[0].itemId);
        }

        [Test]
        public void SiteSnapshots_JsonRoundTrip_Preserved()
        {
            var save = new SaveData();
            save.siteSnapshots = new List<IdleCollectionSnapshot>
            {
                new IdleCollectionSnapshot { chains = new List<IdleChainEntry>
                    { new IdleChainEntry { itemId = 1, itemsPerSecond = 2f, endsAtEntropySink = true, baseSellValue = 3f } } },
                new IdleCollectionSnapshot { chains = new List<IdleChainEntry>
                    { new IdleChainEntry { itemId = 9, itemsPerSecond = 1f, endsAtEntropySink = false, baseSellValue = 0f } } },
            };

            var loaded = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));

            Assert.AreEqual(2, loaded.siteSnapshots.Count);
            Assert.AreEqual(1, loaded.siteSnapshots[0].chains[0].itemId);
            Assert.IsTrue(loaded.siteSnapshots[0].chains[0].endsAtEntropySink);
            Assert.AreEqual(9, loaded.siteSnapshots[1].chains[0].itemId);
        }

        [Test]
        public void LegacySaveWithoutSiteSnapshots_DefaultsEmpty()
        {
            const string legacyJson = "{\"currentRun\":{\"activeSiteIndex\":0}}";
            var save = JsonUtility.FromJson<SaveData>(legacyJson);
            Assert.IsNotNull(save.siteSnapshots, "field initializer gives empty list");
            Assert.AreEqual(0, save.siteSnapshots.Count);
        }

        // ── Per-site field density scaling (FieldGenerator.EffectiveFieldCount) ──

        [Test]
        public void EffectiveFieldCount_DoubleDensity_DoublesCount()
        {
            // Quark Sea: quark_field ×2 → 2 default quark fields become 4.
            Assert.AreEqual(4, FieldGenerator.EffectiveFieldCount(2, 2.0f));
        }

        [Test]
        public void EffectiveFieldCount_ZeroDensity_RemovesField()
        {
            // Quark Sea: electron_field ×0 → field absent.
            Assert.AreEqual(0, FieldGenerator.EffectiveFieldCount(1, 0.0f));
        }

        [Test]
        public void EffectiveFieldCount_UnitDensity_KeepsCount()
        {
            // Origin / no override → multiplier 1 leaves the default count.
            Assert.AreEqual(3, FieldGenerator.EffectiveFieldCount(3, 1.0f));
        }

        // ── Site override can INTRODUCE a non-default field (FieldGenerator.IntroducedFields) ──

        [Test]
        public void IntroducedFields_FieldNotInDefaults_IntroducedWithMultiplierAsCount()
        {
            // Actinide Vein: uranium_field is NOT in the default list, so its multiplier is the count.
            var uranium = MakeField("uranium_field");
            var site = MakeSite("actinide_vein");
            site.fieldOverrides.Add(new SiteFieldOverride { field = uranium, densityMultiplier = 3.0f });

            var result = new List<(FieldSO field, int count)>(
                FieldGenerator.IntroducedFields(site, new HashSet<string>()));

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("uranium_field", result[0].field.id);
            Assert.AreEqual(3, result[0].count);
        }

        [Test]
        public void IntroducedFields_FieldAlreadyInDefaults_NotIntroduced()
        {
            // A field already in the default set is scaled in place by EffectiveEntries, not introduced.
            var quark = MakeField("quark_field");
            var site = MakeSite("origin");
            site.fieldOverrides.Add(new SiteFieldOverride { field = quark, densityMultiplier = 2.0f });

            var result = new List<(FieldSO field, int count)>(
                FieldGenerator.IntroducedFields(site, new HashSet<string> { "quark_field" }));

            Assert.IsEmpty(result);
        }

        [Test]
        public void IntroducedFields_ZeroMultiplier_NotIntroduced()
        {
            var plutonium = MakeField("plutonium_field");
            var site = MakeSite("origin");
            site.fieldOverrides.Add(new SiteFieldOverride { field = plutonium, densityMultiplier = 0.0f });

            var result = new List<(FieldSO field, int count)>(
                FieldGenerator.IntroducedFields(site, new HashSet<string>()));

            Assert.IsEmpty(result);
        }
    }
}
