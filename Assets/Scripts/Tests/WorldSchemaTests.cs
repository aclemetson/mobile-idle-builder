using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Phase 1 (Worlds) tests: WorldSO/WorldDatabaseSO schema + defaults, the WorldLayout
    /// world &lt;-&gt; site mapping, the generated WorldDatabase content, and the additive SaveData
    /// fields (round-trip + legacy default). The prestige-survival case lives with the other
    /// ResetSitesForPrestige tests in PrestigeSystemTests.
    /// </summary>
    [TestFixture]
    public class WorldSchemaTests
    {
        // ── Schema ────────────────────────────────────────────────────────────

        [Test]
        public void WorldSO_HasRequiredFields()
        {
            var type = typeof(WorldSO);
            Assert.IsNotNull(type.GetField("id"),              "WorldSO missing: id");
            Assert.IsNotNull(type.GetField("displayName"),     "WorldSO missing: displayName");
            Assert.IsNotNull(type.GetField("unlockCost"),      "WorldSO missing: unlockCost");
            Assert.IsNotNull(type.GetField("prereqUnlockIds"), "WorldSO missing: prereqUnlockIds");
            Assert.IsNotNull(type.GetField("siteIds"),         "WorldSO missing: siteIds");
            Assert.IsNotNull(type.GetField("theme"),           "WorldSO missing: theme");
        }

        [Test]
        public void WorldDatabaseSO_HasAllWorldsField()
        {
            Assert.IsNotNull(typeof(WorldDatabaseSO).GetField("allWorlds"),
                "WorldDatabaseSO missing: allWorlds");
        }

        [Test]
        public void WorldSO_DefaultsAreSane()
        {
            var so = ScriptableObject.CreateInstance<WorldSO>();
            Assert.AreEqual(0, so.unlockCost, "Physics-style default unlock cost should be 0");
            Assert.IsNotNull(so.prereqUnlockIds, "prereqUnlockIds should be initialized, not null");
            Assert.IsNotNull(so.siteIds,         "siteIds should be initialized, not null");
            Assert.IsNotNull(so.theme,           "theme should be initialized, not null");
            Object.DestroyImmediate(so);
        }

        // ── WorldLayout mapping (pure) ────────────────────────────────────────

        private static WorldSO MakeWorld(string id, params string[] siteIds)
        {
            var w = ScriptableObject.CreateInstance<WorldSO>();
            w.id = id;
            w.siteIds = new List<string>(siteIds);
            return w;
        }

        private static SiteSO MakeSite(string id)
        {
            var s = ScriptableObject.CreateInstance<SiteSO>();
            s.id = id;
            return s;
        }

        [Test]
        public void WorldLayout_MapsSiteIdToOwningWorld()
        {
            var db      = ScriptableObject.CreateInstance<WorldDatabaseSO>();
            var physics = MakeWorld("world_physics", "site_origin", "site_quark_sea");
            var chem    = MakeWorld("world_chemistry", "site_chem_lab");
            db.allWorlds = new[] { physics, chem };

            Assert.AreEqual(0, WorldLayout.WorldIndexForSite(db, "site_origin"));
            Assert.AreEqual(0, WorldLayout.WorldIndexForSite(db, "site_quark_sea"));
            Assert.AreEqual(1, WorldLayout.WorldIndexForSite(db, "site_chem_lab"));
            Assert.AreEqual(0, WorldLayout.WorldIndexForSite(db, "unknown_site"),
                "unknown site defaults to Physics (0)");
            Assert.AreSame(chem, WorldLayout.WorldForSite(db, "site_chem_lab"));
            Assert.IsNull(WorldLayout.WorldForSite(db, "unknown_site"));

            Object.DestroyImmediate(physics);
            Object.DestroyImmediate(chem);
            Object.DestroyImmediate(db);
        }

        [Test]
        public void WorldLayout_MapsGlobalSiteIndexThroughSiteDatabase()
        {
            var worlds  = ScriptableObject.CreateInstance<WorldDatabaseSO>();
            var physics = MakeWorld("world_physics", "site_origin");
            var chem    = MakeWorld("world_chemistry", "site_chem_lab");
            worlds.allWorlds = new[] { physics, chem };

            var sites = ScriptableObject.CreateInstance<SiteDatabaseSO>();
            var s0 = MakeSite("site_origin");
            var s1 = MakeSite("site_chem_lab");
            sites.allSites = new[] { s0, s1 };

            Assert.AreEqual(0, WorldLayout.WorldIndexForSiteIndex(worlds, sites, 0));
            Assert.AreEqual(1, WorldLayout.WorldIndexForSiteIndex(worlds, sites, 1));
            Assert.AreEqual(0, WorldLayout.WorldIndexForSiteIndex(worlds, sites, 99),
                "out-of-range index defaults to Physics (0)");

            Object.DestroyImmediate(physics);
            Object.DestroyImmediate(chem);
            Object.DestroyImmediate(worlds);
            Object.DestroyImmediate(s0);
            Object.DestroyImmediate(s1);
            Object.DestroyImmediate(sites);
        }

        [Test]
        public void WorldLayout_NullDatabases_DefaultToPhysics()
        {
            Assert.AreEqual(0, WorldLayout.WorldIndexForSite(null, "site_origin"));
            Assert.IsNull(WorldLayout.WorldForSite(null, "site_origin"));
            Assert.AreEqual(0, WorldLayout.WorldIndexForSiteIndex(null, null, 0));
        }

        // ── Generated WorldDatabase content (produced by GameDataImporter) ─────

        [Test]
        public void GeneratedWorldDatabase_HasPhysicsAndChemistry()
        {
            var db = Resources.Load<WorldDatabaseSO>("WorldDatabase");
            Assert.IsNotNull(db, "Resources/WorldDatabase.asset missing — run MobileIdleBuilder > Import Game Data");
            Assert.IsNotNull(db.allWorlds, "WorldDatabase.allWorlds is null");
            Assert.GreaterOrEqual(db.allWorlds.Length, 2, "expected at least physics + chemistry");

            var physics = System.Array.Find(db.allWorlds, w => w != null && w.id == "world_physics");
            var chem    = System.Array.Find(db.allWorlds, w => w != null && w.id == "world_chemistry");
            Assert.IsNotNull(physics, "world_physics missing from WorldDatabase");
            Assert.IsNotNull(chem,    "world_chemistry missing from WorldDatabase");

            Assert.AreEqual(0, physics.unlockCost, "world_physics is free/implicit");
            Assert.AreEqual(0, System.Array.IndexOf(db.allWorlds, physics), "world_physics must be index 0");

            // Phase 4a moved the 150K/mid_elements gate onto the chemistry_lab research node:
            // the world now unlocks for free once that research is done (see ChemistryContentTests).
            Assert.AreEqual(0, chem.unlockCost, "world_chemistry cost moved to the chemistry_lab research");
            CollectionAssert.Contains(chem.prereqUnlockIds, "chemistry_lab",
                "world_chemistry is gated by the chemistry_lab research");

            // Every site in the SiteDatabase belongs to exactly one world (worlds partition the flat
            // site list). world_physics owns the original physics sites; site_chem_lab belongs to chem.
            var sites = Resources.Load<SiteDatabaseSO>("SiteDatabase");
            Assert.IsNotNull(sites, "Resources/SiteDatabase.asset missing");
            var owned = new HashSet<string>();
            foreach (var w in db.allWorlds)
                if (w?.siteIds != null)
                    foreach (var sid in w.siteIds) owned.Add(sid);
            foreach (var s in sites.allSites)
                Assert.IsTrue(owned.Contains(s.id),
                    $"site '{s.id}' must be owned by some world");
            CollectionAssert.Contains(physics.siteIds, "site_origin",
                "world_physics owns the origin site");

            // Biology track: world_biology exists, is gated by the biology_lab research, and owns its site.
            var bio = System.Array.Find(db.allWorlds, w => w != null && w.id == "world_biology");
            Assert.IsNotNull(bio, "world_biology missing from WorldDatabase");
            Assert.AreEqual(0, bio.unlockCost, "world_biology cost moved to the biology_lab research");
            CollectionAssert.Contains(bio.prereqUnlockIds, "biology_lab",
                "world_biology is gated by the biology_lab research");
            CollectionAssert.Contains(bio.siteIds, "site_bio_lab", "world_biology owns site_bio_lab");
        }

        // ── SaveData additive fields ──────────────────────────────────────────

        [Test]
        public void SaveData_WorldFields_RoundTrip()
        {
            var save = new SaveData();
            save.unlockedWorlds.Add("world_chemistry");
            save.currentRun.activeWorldIndex = 1;

            var json   = JsonUtility.ToJson(save);
            var loaded = JsonUtility.FromJson<SaveData>(json);

            CollectionAssert.Contains(loaded.unlockedWorlds, "world_chemistry");
            Assert.AreEqual(1, loaded.currentRun.activeWorldIndex);
        }

        [Test]
        public void SaveData_LegacyJsonWithoutWorldFields_DefaultsToPhysics()
        {
            // A save written before Phase 1 has neither field.
            const string legacy = "{\"playerId\":\"abc\",\"prestigeCount\":2}";
            var loaded = JsonUtility.FromJson<SaveData>(legacy);

            Assert.IsNotNull(loaded.unlockedWorlds, "unlockedWorlds must default to empty list, not null");
            Assert.AreEqual(0, loaded.unlockedWorlds.Count, "no worlds unlocked in a legacy save");
            Assert.AreEqual(0, loaded.currentRun.activeWorldIndex, "legacy save is World 0 (Physics)");
        }
    }
}
