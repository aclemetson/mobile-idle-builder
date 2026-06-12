using NUnit.Framework;
using Unity.Entities;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode ECS tests for PrestigeSystem.
    ///
    /// Each test creates an isolated World, sets up the minimal singletons the system
    /// requires, runs one update tick, and asserts on the resulting component state.
    /// </summary>
    [TestFixture]
    public class PrestigeSystemTests
    {
        private World _world;
        private EntityManager _em;
        private Entity _playerEntity;

        [SetUp]
        public void Setup()
        {
            _world = new World("PrestigeTest");
            _em = _world.EntityManager;

            // Register system into SimulationSystemGroup so World.Update() drives it
            var simGroup = _world.CreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(_world.CreateSystem<PrestigeSystem>());
            simGroup.SortSystems();

            // Single player entity holds all required singletons
            _playerEntity = _em.CreateEntity();
            _em.AddComponent<PlayerInventoryTag>(_playerEntity);
            _em.AddComponent<PlayerProgressData>(_playerEntity);
            _em.AddComponent<PrestigeData>(_playerEntity);
            _em.AddBuffer<InventorySlot>(_playerEntity);
        }

        [TearDown]
        public void Teardown()
        {
            if (_world.IsCreated) _world.Dispose();
        }

        // ── PrestigeRequested = false ────────────────────────────────────────

        [Test]
        public void WhenNotRequested_StateIsUnchanged()
        {
            _em.SetComponentData(_playerEntity, new PlayerProgressData
            {
                NetWorth = 1000f,
                PrestigeRequested = false
            });

            _world.Update();

            var progress = _em.GetComponentData<PlayerProgressData>(_playerEntity);
            var prestige = _em.GetComponentData<PrestigeData>(_playerEntity);
            Assert.AreEqual(1000f, progress.NetWorth,   "NetWorth must not change when prestige is not requested");
            Assert.AreEqual(0,     prestige.RunCount,   "RunCount must not change when prestige is not requested");
            Assert.AreEqual(0L,    prestige.PrestigeCurrency, "PrestigeCurrency must not change when prestige is not requested");
        }

        // ── Currency calculation ─────────────────────────────────────────────

        [Test]
        public void WhenRequested_AwardsCurrencyViaLogFormula()
        {
            // Formula: floor(max(0, log10(netWorth / pbase) * pscale))
            // GameBootstrap is null in tests → defaults: pbase=5000, pscale=50
            // At netWorth = 50000 (= wall = 5000 × 10): log10(10) × 50 = 50
            _em.SetComponentData(_playerEntity, new PlayerProgressData
            {
                NetWorth = 50000f,
                PrestigeRequested = true
            });

            _world.Update();

            var prestige = _em.GetComponentData<PrestigeData>(_playerEntity);
            Assert.AreEqual(50L, prestige.PrestigeCurrency);
        }

        [Test]
        public void WhenRequested_ZeroNetWorthAwardsZeroCurrency()
        {
            _em.SetComponentData(_playerEntity, new PlayerProgressData
            {
                NetWorth = 0f,
                PrestigeRequested = true
            });

            _world.Update();

            var prestige = _em.GetComponentData<PrestigeData>(_playerEntity);
            Assert.AreEqual(0L, prestige.PrestigeCurrency);
        }

        [Test]
        public void WhenRequested_CurrencyAccumulatesAcrossRuns()
        {
            // Simulate a second prestige on top of already-held currency
            // At netWorth = 50000: log10(10) × 50 = 50 → total = 3000 + 50 = 3050
            _em.SetComponentData(_playerEntity, new PrestigeData { PrestigeCurrency = 3000L });
            _em.SetComponentData(_playerEntity, new PlayerProgressData
            {
                NetWorth = 50000f,
                PrestigeRequested = true
            });

            _world.Update();

            var prestige = _em.GetComponentData<PrestigeData>(_playerEntity);
            Assert.AreEqual(3050L, prestige.PrestigeCurrency, "Currency should accumulate: 3000 + 50 = 3050");
        }

        // ── Run count ────────────────────────────────────────────────────────

        [Test]
        public void WhenRequested_RunCountIncrements()
        {
            _em.SetComponentData(_playerEntity, new PrestigeData { RunCount = 2 });
            _em.SetComponentData(_playerEntity, new PlayerProgressData { PrestigeRequested = true });

            _world.Update();

            var prestige = _em.GetComponentData<PrestigeData>(_playerEntity);
            Assert.AreEqual(3, prestige.RunCount);
        }

        // ── Progress reset ───────────────────────────────────────────────────

        [Test]
        public void WhenRequested_PlayerProgressIsReset()
        {
            _em.SetComponentData(_playerEntity, new PlayerProgressData
            {
                NetWorth          = 9999f,
                BaseCurrency      = 500,
                CurrentTier       = 4,
                PrestigeAvailable = true,
                PrestigeRequested = true
            });

            _world.Update();

            var progress = _em.GetComponentData<PlayerProgressData>(_playerEntity);
            Assert.AreEqual(0f,    progress.NetWorth,          "NetWorth should reset to 0");
            Assert.AreEqual(0L,    progress.BaseCurrency,       "BaseCurrency resets to cfgEntropy (0 in tests — no GameBootstrap)");
            Assert.AreEqual(1,     progress.CurrentTier,        "CurrentTier should reset to 1");
            Assert.IsFalse(progress.PrestigeAvailable,          "PrestigeAvailable should reset to false");
            Assert.IsFalse(progress.PrestigeRequested,          "PrestigeRequested should clear after processing");
        }

        // ── Inventory cleared ────────────────────────────────────────────────

        [Test]
        public void WhenRequested_InventoryIsCleared()
        {
            var inv = _em.GetBuffer<InventorySlot>(_playerEntity);
            inv.Add(new InventorySlot { ItemID = 1, Quantity = 50 });
            inv.Add(new InventorySlot { ItemID = 4, Quantity = 20 });

            _em.SetComponentData(_playerEntity, new PlayerProgressData { PrestigeRequested = true });

            _world.Update();

            var invAfter = _em.GetBuffer<InventorySlot>(_playerEntity);
            Assert.AreEqual(0, invAfter.Length, "Inventory must be fully cleared on prestige");
        }

        // ── Buildings destroyed ──────────────────────────────────────────────

        [Test]
        public void WhenRequested_BuildingEntitiesAreDestroyed()
        {
            // Place two building entities
            var b1 = _em.CreateEntity();
            _em.AddComponent<BuildingData>(b1);
            var b2 = _em.CreateEntity();
            _em.AddComponent<BuildingData>(b2);

            _em.SetComponentData(_playerEntity, new PlayerProgressData { PrestigeRequested = true });

            _world.Update();

            Assert.IsFalse(_em.Exists(b1), "Building entity b1 must be destroyed on prestige");
            Assert.IsFalse(_em.Exists(b2), "Building entity b2 must be destroyed on prestige");
        }

        [Test]
        public void WhenRequested_NoBuildingsPresent_StillCompletes()
        {
            // No building entities — should still prestige cleanly
            _em.SetComponentData(_playerEntity, new PlayerProgressData
            {
                NetWorth = 100f,
                PrestigeRequested = true
            });

            Assert.DoesNotThrow(() => _world.Update());

            var prestige = _em.GetComponentData<PrestigeData>(_playerEntity);
            Assert.AreEqual(1, prestige.RunCount);
        }

        // ── Prestige wall detection ──────────────────────────────────────────

        [Test]
        public void WhenNetWorthBelowWall_PrestigeNotAvailable()
        {
            _em.SetComponentData(_playerEntity, new PlayerProgressData
            {
                NetWorth         = 49999f,
                PrestigeWallValue = 50000f
            });

            _world.Update();

            var progress = _em.GetComponentData<PlayerProgressData>(_playerEntity);
            Assert.IsFalse(progress.PrestigeAvailable,
                "PrestigeAvailable must remain false when NetWorth < PrestigeWallValue");
        }

        [Test]
        public void WhenNetWorthAtWall_PrestigeBecomesAvailable()
        {
            _em.SetComponentData(_playerEntity, new PlayerProgressData
            {
                NetWorth          = 50000f,
                PrestigeWallValue = 50000f
            });

            _world.Update();

            var progress = _em.GetComponentData<PlayerProgressData>(_playerEntity);
            Assert.IsTrue(progress.PrestigeAvailable,
                "PrestigeAvailable must be true when NetWorth >= PrestigeWallValue");
        }

        [Test]
        public void WhenNetWorthAboveWall_PrestigeStaysAvailable()
        {
            _em.SetComponentData(_playerEntity, new PlayerProgressData
            {
                NetWorth          = 100000f,
                PrestigeWallValue = 50000f
            });

            _world.Update();

            var progress = _em.GetComponentData<PlayerProgressData>(_playerEntity);
            Assert.IsTrue(progress.PrestigeAvailable,
                "PrestigeAvailable must be true when NetWorth > PrestigeWallValue");
        }

        [Test]
        public void WhenAlreadyAvailable_SecondUpdateDoesNotFlip()
        {
            _em.SetComponentData(_playerEntity, new PlayerProgressData
            {
                NetWorth          = 50000f,
                PrestigeWallValue = 50000f,
                PrestigeAvailable = true   // already triggered
            });

            _world.Update();
            _world.Update();

            var progress = _em.GetComponentData<PlayerProgressData>(_playerEntity);
            Assert.IsTrue(progress.PrestigeAvailable,
                "PrestigeAvailable must remain true once set; a second update must not clear it");
        }

        // ── Multi-grids reset (PrestigeSaveWatcher.ResetSitesForPrestige) ────

        [Test]
        public void ResetSitesForPrestige_ClearsAllGridsAndReturnsToSiteZero()
        {
            var save = new SaveData();
            save.currentRun.grids = new System.Collections.Generic.List<GridSaveData>
            {
                GridWithBuilding(1), GridWithBuilding(2), GridWithBuilding(3)
            };
            save.currentRun.activeSiteIndex = 2;

            PrestigeSaveWatcher.ResetSitesForPrestige(save);

            Assert.AreEqual(0, save.currentRun.grids.Count, "all sites' grids cleared");
            Assert.AreEqual(0, save.currentRun.activeSiteIndex, "active site returns to origin");
        }

        [Test]
        public void ResetSitesForPrestige_ClearsActiveAndPerSiteIdleSnapshots()
        {
            var save = new SaveData();
            save.idleSnapshot  = SnapshotWithChain();
            save.siteSnapshots = new System.Collections.Generic.List<IdleCollectionSnapshot>
            {
                SnapshotWithChain(), SnapshotWithChain()
            };

            PrestigeSaveWatcher.ResetSitesForPrestige(save);

            Assert.AreEqual(0, save.idleSnapshot.chains.Count, "active idle snapshot wiped");
            Assert.AreEqual(0, save.siteSnapshots.Count, "per-site snapshots wiped");
        }

        [Test]
        public void ResetSitesForPrestige_PreservesSiteUnlocks()
        {
            var save = new SaveData();
            save.unlockedSites = new System.Collections.Generic.List<string>
                { "site_quark_sea", "site_lepton_storm" };

            PrestigeSaveWatcher.ResetSitesForPrestige(save);

            CollectionAssert.AreEqual(new[] { "site_quark_sea", "site_lepton_storm" },
                save.unlockedSites, "site unlocks must survive prestige");
        }

        [Test]
        public void ResetSitesForPrestige_NullSave_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => PrestigeSaveWatcher.ResetSitesForPrestige(null));
        }

        private static GridSaveData GridWithBuilding(int buildingId)
        {
            var g = new GridSaveData();
            g.buildings.Add(new BuildingSaveData { buildingId = buildingId, position = new[] { 1, 1 } });
            return g;
        }

        private static IdleCollectionSnapshot SnapshotWithChain() =>
            new IdleCollectionSnapshot
            {
                chains = new System.Collections.Generic.List<IdleChainEntry>
                    { new IdleChainEntry { itemId = 1, itemsPerSecond = 1f, endsAtEntropySink = true, baseSellValue = 1f } }
            };

        [Test]
        public void WhenWallIsZero_PrestigeNeverAvailable()
        {
            _em.SetComponentData(_playerEntity, new PlayerProgressData
            {
                NetWorth          = 999999f,
                PrestigeWallValue = 0f      // 0 means no wall configured
            });

            _world.Update();

            var progress = _em.GetComponentData<PlayerProgressData>(_playerEntity);
            Assert.IsFalse(progress.PrestigeAvailable,
                "PrestigeWallValue == 0 must disable wall detection entirely");
        }
    }
}
