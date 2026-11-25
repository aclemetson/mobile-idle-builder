using System.Linq;
using MobileIdleBuilder.Dev;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The dev console's `block load`: a save for a player who has just reached a research block (game phase).
    /// Data comes from game_data.json research_blocks (start_* fields, scripts/block_starts.py).
    /// </summary>
    [TestFixture]
    public class BlockStartPresetsTests
    {
        ResearchDatabaseSO _db;
        WorldDatabaseSO _worlds;

        [OneTimeSetUp]
        public void Load()
        {
            _db = Resources.Load<ResearchDatabaseSO>("ResearchDatabase");
            _worlds = Resources.Load<WorldDatabaseSO>("WorldDatabase");
            Assert.IsNotNull(_db, "ResearchDatabase missing: run Import Game Data");
            Assert.IsNotEmpty(_db.blocks, "research blocks were not imported");
        }

        SaveData Build(string id, SaveData from = null) =>
            BlockStartPresets.Build(from ?? new SaveData { playerId = "tester" }, _db, _worlds, BlockStartPresets.Find(_db, id));

        [Test]
        public void EveryResearch_HasAnImportedBlock_AndTreeFields()
        {
            var ids = _db.blocks.Select(b => b.id).ToHashSet();
            foreach (var r in _db.allResearch)
                Assert.IsTrue(ids.Contains(r.block), $"{r.id} has block '{r.block}'");
        }

        [Test]
        public void Find_AcceptsIdNameOrNumber()
        {
            var forge = BlockStartPresets.Find(_db, "stellar_forge");
            Assert.IsNotNull(forge);
            Assert.AreSame(forge, BlockStartPresets.Find(_db, "Stellar Forge"));
            Assert.AreSame(forge, BlockStartPresets.Find(_db, forge.order.ToString()));
            Assert.IsNull(BlockStartPresets.Find(_db, "no_such_block"));
        }

        [Test]
        public void FirstBlock_IsAFreshGame_KeepingOnlyThePlayer()
        {
            var save = Build(_db.blocks.OrderBy(b => b.order).First().id, new SaveData { playerId = "p1", prestigeCount = 9 });
            Assert.AreEqual("p1", save.playerId);
            Assert.AreEqual(0, save.prestigeCount);
            Assert.IsEmpty(save.unlockedResearch);
            Assert.IsFalse(save.tutorial.hasCompletedFirstRun, "the tutorial plays again");
        }

        [Test]
        public void LaterBlock_HasEverythingBeforeIt_ItsUpgrades_AndAnEmptyRun()
        {
            var forge = BlockStartPresets.Find(_db, "stellar_forge");
            var current = new SaveData { playerId = "p1", paidCurrency = 42 };
            current.currentRun.baseCurrency = 999_999;
            current.currentRun.grid.buildings.Add(new BuildingSaveData());
            var save = BlockStartPresets.Build(current, _db, _worlds, forge);

            var expected = _db.allResearch.Where(r => _db.blocks.First(b => b.id == r.block).order < forge.order).Select(r => r.id);
            CollectionAssert.AreEquivalent(expected, save.unlockedResearch);
            Assert.IsFalse(save.unlockedResearch.Any(id => _db.allResearch.First(r => r.id == id).block == forge.id),
                "nothing from the block itself is researched yet");

            Assert.AreEqual(forge.startPrestigeCount, save.prestigeCount);
            CollectionAssert.AreEquivalent(forge.startPermanentUpgrades, save.permanentUpgrades);
            // prestigeCurrency is lifetime earned (held = earned - spent), never negative held.
            Assert.LessOrEqual(save.prestigeCurrencySpent, forge.startPrestigeCurrencyEarned);
            Assert.AreEqual(forge.startPrestigeCurrencyEarned, save.prestigeCurrency);
            Assert.GreaterOrEqual(save.prestigeCurrency - save.prestigeCurrencySpent, 0);
            Assert.Greater(save.prestigeSpeedMultiplier, 1f, "Assembly Line is part of the expected upgrades");
            Assert.GreaterOrEqual(save.currentRun.baseCurrency, forge.startEntropy);

            Assert.IsTrue(save.tutorial.hasCompletedFirstRun);
            Assert.IsEmpty(save.currentRun.grid.buildings, "the run starts empty, like after a prestige");
            Assert.AreEqual(42, save.paidCurrency, "crystals and other non-progression state are kept");
            Assert.AreEqual(1, current.currentRun.grid.buildings.Count, "the current save is not modified");
        }

        [Test]
        public void LateBlocks_OpenTheWorldsTheirEarlierResearchUnlocked()
        {
            var bio = Build("bio_cells");
            CollectionAssert.DoesNotContain(bio.unlockedSites, "site_actinide_vein", "Quantum Domains are retired (2026-11-24)");
            var chemistry = _worlds.allWorlds.FirstOrDefault(w => w != null && w.id == "world_chemistry");
            Assert.IsNotNull(chemistry);
            CollectionAssert.Contains(bio.unlockedWorlds, chemistry.id);
            foreach (var s in chemistry.siteIds) CollectionAssert.Contains(bio.unlockedSites, s);

            var forge = Build("stellar_forge");
            CollectionAssert.DoesNotContain(forge.unlockedWorlds, "world_chemistry");
        }

        [Test]
        public void ExpectedUpgrades_GrowBlockByBlock()
        {
            long prev = -1;
            foreach (var b in _db.blocks.OrderBy(b => b.order))
            {
                Assert.GreaterOrEqual(b.startPrestigeCurrencyEarned, prev, $"{b.id}: a later phase never expects fewer ✦");
                prev = b.startPrestigeCurrencyEarned;
                long spent = BlockStartPresets.CurrencySpent(BlockStartPresets.ParseLevels(b.startPermanentUpgrades));
                Assert.LessOrEqual(spent, b.startPrestigeCurrencyEarned, $"{b.id}: upgrades cost more than the ✦ earned");
            }
        }
    
        [Test]
        public void ChemistryBlock_OpensChemistry_AndStartsThere_TutorialDone()
        {
            // 2026-11-23: 'block fresh chem_organics' drops you on the Chemistry site, tutorial behind you.
            var save = Build("chem_organics");
            int chem = System.Array.FindIndex(_worlds.allWorlds, w => w != null && w.id == WorldGate.Chemistry);
            CollectionAssert.Contains(save.unlockedWorlds, WorldGate.Chemistry);
            Assert.AreEqual(chem, save.currentRun.activeWorldIndex);
            var site = Resources.Load<SiteDatabaseSO>("SiteDatabase").allSites[save.currentRun.activeSiteIndex];
            CollectionAssert.Contains(_worlds.allWorlds[chem].siteIds, site.id, "on a Chemistry site");
            Assert.IsTrue(TutorialFlows.Slot(save, WorldGate.Chemistry, create: false)?.done ?? false, "tutorial already done");
        }

        [Test]
        public void FirstChemistryBlock_StartsInChemistry_WithItsTutorialToPlay()
        {
            var save = Build("chemistry_entry");
            CollectionAssert.Contains(save.unlockedWorlds, WorldGate.Chemistry);
            Assert.IsFalse(TutorialFlows.Slot(save, WorldGate.Chemistry, create: false)?.done ?? false,
                "the Chemistry tutorial plays on the world's first block");
        }

        [Test]
        public void PhysicsBlock_StaysInPhysics()
        {
            var save = Build("metal_descent");
            Assert.AreEqual(0, save.currentRun.activeWorldIndex);
            Assert.IsNull(BlockStartPresets.BlockWorld(_db, BlockStartPresets.Find(_db, "metal_descent"), _worlds));
        }
}
}
