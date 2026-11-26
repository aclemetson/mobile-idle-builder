using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Quantum Domains retired (2026-11-24): Quark Sea, Lepton Storm and Actinide Vein stay in the site list
    /// (saved indices) but are never unlockable, travelled to or paid idle; saves get their unlock price back.
    /// </summary>
    public class SiteRetirementTests
    {
        readonly List<SiteSO> _made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var s in _made) Object.DestroyImmediate(s);
            _made.Clear();
        }

        SiteSO Site(string id, long cost = 0, bool retired = false)
        {
            var s = ScriptableObject.CreateInstance<SiteSO>();
            s.id = id; s.unlockCost = cost; s.retired = retired;
            _made.Add(s);
            return s;
        }

        List<SiteSO> Sites() => new()
        {
            Site("site_origin"),
            Site("site_quark_sea", 250_000, retired: true),
            Site("site_lepton_storm", 10_000_000, retired: true),
            Site("site_actinide_vein", 250_000, retired: true),
            Site("site_chem_lab"),
        };

        static SaveData SaveWith(params string[] unlocked)
        {
            var save = new SaveData { unlockedSites = new List<string>(unlocked) };
            for (int i = 0; i < 5; i++)
            {
                var grid = new GridSaveData();
                grid.buildings.Add(new BuildingSaveData());
                save.currentRun.grids.Add(grid);
                save.siteSnapshots.Add(new IdleCollectionSnapshot { chains = new List<IdleChainEntry> { new IdleChainEntry() } });
            }
            return save;
        }

        [Test]
        public void UnlockedDomains_AreRefunded_Cleared_AndForgotten()
        {
            var save = SaveWith("site_quark_sea", "site_lepton_storm", "site_chem_lab");
            long refund = SiteRetirement.Migrate(save, Sites());

            Assert.AreEqual(10_250_000, refund, "Quark Sea + Lepton Storm; Actinide Vein was never bought");
            CollectionAssert.AreEqual(new[] { "site_chem_lab" }, save.unlockedSites);
            for (int i = 1; i <= 3; i++)
            {
                Assert.IsEmpty(save.currentRun.grids[i].buildings, $"grid {i} cleared");
                Assert.IsEmpty(save.siteSnapshots[i].chains, $"snapshot {i} pays no idle");
            }
            Assert.IsNotEmpty(save.currentRun.grids[0].buildings, "Origin untouched");
            Assert.IsNotEmpty(save.currentRun.grids[4].buildings, "Chemistry Lab untouched (indices kept)");
            Assert.IsNotEmpty(save.siteSnapshots[4].chains);
        }

        [Test]
        public void Migration_IsIdempotent()
        {
            var save = SaveWith("site_quark_sea");
            Assert.AreEqual(250_000, SiteRetirement.Migrate(save, Sites()));
            Assert.AreEqual(0, SiteRetirement.Migrate(save, Sites()), "refunded once");
        }

        [Test]
        public void PlayerOnARetiredDomain_IsMovedToOrigin()
        {
            var save = SaveWith("site_actinide_vein");
            save.currentRun.activeSiteIndex = 3;
            SiteRetirement.Migrate(save, Sites());
            Assert.AreEqual(0, save.currentRun.activeSiteIndex);
            Assert.AreEqual(0, save.currentRun.activeWorldIndex);

            var onChem = SaveWith();
            onChem.currentRun.activeSiteIndex = 4;
            onChem.currentRun.activeWorldIndex = 1;
            SiteRetirement.Migrate(onChem, Sites());
            Assert.AreEqual(4, onChem.currentRun.activeSiteIndex, "other worlds stay where they are");
            Assert.AreEqual(1, onChem.currentRun.activeWorldIndex);
        }

        [Test]
        public void RetiredSites_AreNeverUnlocked()
        {
            var sites = Sites();
            var save = new SaveData { unlockedSites = new List<string> { "site_quark_sea", "site_chem_lab" } };
            Assert.IsFalse(SiteService.IsUnlocked(save, sites, 1), "retired even if the save still lists it");
            Assert.IsTrue(SiteService.IsUnlocked(save, sites, 4));
            Assert.IsTrue(SiteService.IsUnlocked(save, sites, 0));
        }

        [Test]
        public void NothingToMigrate_IsANoOp()
        {
            Assert.AreEqual(0, SiteRetirement.Migrate(null, Sites()));
            Assert.AreEqual(0, SiteRetirement.Migrate(new SaveData(), null));
            Assert.AreEqual(0, SiteRetirement.Migrate(new SaveData(), Sites()));
        }
    }
}
