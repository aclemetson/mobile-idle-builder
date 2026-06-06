using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// EditMode tests for OfflineCollectionService and IdleGraphAnalyzer.
    /// Both are pure C# with no ECS or Unity lifecycle dependencies.
    ///
    /// PersistentUpgradeService is a MonoBehaviour; upgrade-effect tests use
    /// AddComponent on a temporary GameObject (destroyed in TearDown).
    /// </summary>
    [TestFixture]
    public class OfflineCollectionServiceTests
    {
        private GameConfigSO             _cfg;
        private GameObject               _svcGo;
        private PersistentUpgradeService _svc;

        [SetUp]
        public void Setup()
        {
            _cfg = ScriptableObject.CreateInstance<GameConfigSO>();
            _cfg.idleBaseMaxSeconds     = 7200f;
            _cfg.idleAbsoluteMaxSeconds = 43200f;
            _cfg.idleBaseCollectionRate = 0.5f;

            _svcGo = new GameObject("TestUpgradeSvc");
            _svc   = _svcGo.AddComponent<PersistentUpgradeService>();
        }

        [TearDown]
        public void Teardown()
        {
            UnityEngine.Object.DestroyImmediate(_svcGo);
            UnityEngine.Object.DestroyImmediate(_cfg);
        }

        // ── Guard conditions ──────────────────────────────────────────────────

        [Test]
        public void NullSnapshot_ReturnsNull()
        {
            var save = MakeSave(null);
            Assert.IsNull(OfflineCollectionService.CalculateAndApply(save, _cfg, null));
        }

        [Test]
        public void EmptyChains_ReturnsNull()
        {
            var save = MakeSave(new IdleCollectionSnapshot { chains = new List<IdleChainEntry>() });
            Assert.IsNull(OfflineCollectionService.CalculateAndApply(save, _cfg, null));
        }

        [Test]
        public void AlreadyApplied_ReturnsNull()
        {
            string ts = DateTime.UtcNow.AddHours(-1).ToString("O");
            var save = MakeSave(SingleInventoryChain(), lastSaved: ts);
            save.idleCollectionApplied = ts;   // already applied this timestamp
            Assert.IsNull(OfflineCollectionService.CalculateAndApply(save, _cfg, null));
        }

        [Test]
        public void MissingLastSaved_ReturnsNull()
        {
            var save = MakeSave(SingleInventoryChain());
            save.lastSaved = null;
            Assert.IsNull(OfflineCollectionService.CalculateAndApply(save, _cfg, null));
        }

        [Test]
        public void FutureLastSaved_ReturnsNull()
        {
            var save = MakeSave(SingleInventoryChain(),
                                lastSaved: DateTime.UtcNow.AddHours(1).ToString("O"));
            Assert.IsNull(OfflineCollectionService.CalculateAndApply(save, _cfg, null));
        }

        // ── Inventory chain ───────────────────────────────────────────────────

        [Test]
        public void InventoryChain_AddsItemsToSave()
        {
            // 1 item/s × 100 s × 50% rate = 50 items
            var chain = new IdleChainEntry
                { itemId = 42, itemsPerSecond = 1f, endsAtEntropySink = false, baseSellValue = 5f };
            var save  = MakeSave(OneChain(chain), secondsAgo: 100f);
            var result = OfflineCollectionService.CalculateAndApply(save, _cfg, null);

            Assert.IsNotNull(result);
            Assert.AreEqual(50, result.ItemsEarned[42]);
            Assert.AreEqual("42", save.currentRun.inventoryKeys[0]);
            Assert.AreEqual(50, save.currentRun.inventoryValues[0]);
        }

        [Test]
        public void InventoryMerge_AddsToExistingSlot()
        {
            var chain = new IdleChainEntry
                { itemId = 9, itemsPerSecond = 1f, endsAtEntropySink = false, baseSellValue = 1f };
            var save  = MakeSave(OneChain(chain), secondsAgo: 100f);
            save.currentRun.inventoryKeys.Add("9");
            save.currentRun.inventoryValues.Add(10);

            OfflineCollectionService.CalculateAndApply(save, _cfg, null);

            int idx = save.currentRun.inventoryKeys.IndexOf("9");
            Assert.GreaterOrEqual(idx, 0, "Slot must exist");
            Assert.AreEqual(60, save.currentRun.inventoryValues[idx], "10 existing + 50 new");
        }

        // ── Entropy-sink chain ────────────────────────────────────────────────

        [Test]
        public void EntropySinkChain_AddsCurrencyToSave()
        {
            // 2 items/s × 100 s × 50% = 100 items × sellValue 3 = 300 entropy
            var chain = new IdleChainEntry
                { itemId = 7, itemsPerSecond = 2f, endsAtEntropySink = true, baseSellValue = 3f };
            var save  = MakeSave(OneChain(chain), secondsAgo: 100f);
            var result = OfflineCollectionService.CalculateAndApply(save, _cfg, null);

            Assert.IsNotNull(result);
            Assert.AreEqual(300L, result.EntropyEarned);
            Assert.AreEqual(300L, save.currentRun.baseCurrency);
        }

        // ── Rate and cap ──────────────────────────────────────────────────────

        [Test]
        public void CollectionRate50pct_HalvesOutput()
        {
            var chain = new IdleChainEntry
                { itemId = 1, itemsPerSecond = 10f, endsAtEntropySink = false, baseSellValue = 1f };
            var save  = MakeSave(OneChain(chain), secondsAgo: 60f);
            var result = OfflineCollectionService.CalculateAndApply(save, _cfg, null);

            Assert.IsNotNull(result);
            Assert.AreEqual(300, result.ItemsEarned[1], "10 items/s × 60 s × 50% = 300");
        }

        [Test]
        public void ElapsedExceedsCap_ClampsToCap()
        {
            // 3 hours elapsed, cap 2 hours → only 7200 s collected
            var chain = new IdleChainEntry
                { itemId = 5, itemsPerSecond = 1f, endsAtEntropySink = true, baseSellValue = 1f };
            var save  = MakeSave(OneChain(chain), secondsAgo: 10800f);
            var result = OfflineCollectionService.CalculateAndApply(save, _cfg, null);

            Assert.IsNotNull(result);
            Assert.AreEqual(7200f, result.CappedSeconds, 1f);
            Assert.AreEqual(3600L, result.EntropyEarned, "1 item/s × 7200 s × 50%");
        }

        [Test]
        public void MultipleChains_AccumulatesCorrectly()
        {
            var snapshot = new IdleCollectionSnapshot
            {
                chains = new List<IdleChainEntry>
                {
                    new IdleChainEntry { itemId = 1, itemsPerSecond = 2f, endsAtEntropySink = false, baseSellValue = 1f },
                    new IdleChainEntry { itemId = 2, itemsPerSecond = 1f, endsAtEntropySink = true,  baseSellValue = 4f },
                }
            };
            var save   = MakeSave(snapshot, secondsAgo: 100f);
            var result = OfflineCollectionService.CalculateAndApply(save, _cfg, null);

            Assert.IsNotNull(result);
            Assert.AreEqual(100, result.ItemsEarned[1],    "2 items/s × 100 s × 0.5 = 100");
            Assert.AreEqual(200L, result.EntropyEarned,    "1 × 100 × 0.5 × 4 = 200");
        }

        [Test]
        public void SetsIdleCollectionApplied_ToLastSaved()
        {
            var save = MakeSave(OneChain(
                new IdleChainEntry { itemId = 1, itemsPerSecond = 1f, endsAtEntropySink = true, baseSellValue = 1f }),
                secondsAgo: 100f);
            Assert.IsNull(save.idleCollectionApplied);
            OfflineCollectionService.CalculateAndApply(save, _cfg, null);
            Assert.AreEqual(save.lastSaved, save.idleCollectionApplied,
                "idleCollectionApplied must equal lastSaved so the same snapshot is not re-applied");
        }

        // ── GetEffectiveIdleCap ───────────────────────────────────────────────

        [Test]
        public void GetEffectiveIdleCap_NoUpgrades_ReturnsBase()
        {
            float cap = OfflineCollectionService.GetEffectiveIdleCap(_cfg, null);
            Assert.AreEqual(7200f, cap, 0.01f);
        }

        [Test]
        public void GetEffectiveIdleCap_WithUpgrades_AddsCorrectly()
        {
            // 3 levels of idle_time_cap: 3 × 35/40/46 → levels 1,2,3; effect = 3 × 1800 = 5400
            _svc.LoadFromSave(new List<string> { "idle_time_cap:3" });
            float cap = OfflineCollectionService.GetEffectiveIdleCap(_cfg, _svc);
            Assert.AreEqual(7200f + 5400f, cap, 0.01f);
        }

        [Test]
        public void GetEffectiveIdleCap_NeverExceedsAbsoluteMax()
        {
            // Max 20 levels: 20 × 1800 = 36000 bonus; 7200 + 36000 = 43200 = absoluteMax
            _svc.LoadFromSave(new List<string> { "idle_time_cap:20" });
            float cap = OfflineCollectionService.GetEffectiveIdleCap(_cfg, _svc);
            Assert.AreEqual(43200f, cap, 0.01f);
        }

        // ── GetEffectiveCollectionRate ────────────────────────────────────────

        [Test]
        public void GetEffectiveCollectionRate_NoUpgrades_ReturnsBase()
        {
            float rate = OfflineCollectionService.GetEffectiveCollectionRate(_cfg, null);
            Assert.AreEqual(0.5f, rate, 0.001f);
        }

        [Test]
        public void GetEffectiveCollectionRate_WithUpgrades_AddsCorrectly()
        {
            // Need idle_time_cap ≥ 1 as prereq for idle_collection_rate
            _svc.LoadFromSave(new List<string> { "idle_time_cap:1", "idle_collection_rate:4" });
            float rate = OfflineCollectionService.GetEffectiveCollectionRate(_cfg, _svc);
            Assert.AreEqual(0.5f + 4 * 0.05f, rate, 0.001f, "base 50% + 4 × 5%");
        }

        [Test]
        public void GetEffectiveCollectionRate_MaxesAtOne()
        {
            // 10 levels × 0.05 = 0.50 bonus → 0.50 + 0.50 = 1.00 (≤ 1 clamp)
            _svc.LoadFromSave(new List<string> { "idle_time_cap:1", "idle_collection_rate:10" });
            float rate = OfflineCollectionService.GetEffectiveCollectionRate(_cfg, _svc);
            Assert.AreEqual(1.0f, rate, 0.001f);
        }

        // ── IdleGraphAnalyzer ─────────────────────────────────────────────────

        [Test]
        public void Analyzer_EmptyGrid_ReturnsNoChains()
        {
            var snap = Analyze(new GridSaveData(),
                               collectorIds: new HashSet<int>(), sinkIds: new HashSet<int>());
            Assert.AreEqual(0, snap.chains.Count);
        }

        [Test]
        public void Analyzer_CollectorNoConveyor_ReturnsNoChains()
        {
            var grid = new GridSaveData
            {
                buildings = new List<BuildingSaveData>
                {
                    new BuildingSaveData { buildingId = 1, recipeId = 0, position = new[] { 5, 5 } }
                },
                conveyors = new List<ConveyorSaveData>()
            };
            var snap = Analyze(grid, new HashSet<int> { 1 }, new HashSet<int>());
            Assert.AreEqual(0, snap.chains.Count);
        }

        [Test]
        public void Analyzer_CollectorLinkedToInventory_ReturnsInventoryChain()
        {
            // Collector at (5,5), belt flows East: head (6,5)→(7,5)→(8,5), no building at (9,5)
            var grid = MakeGridChain(new Vector2Int(5, 5),
                                     new[] { new Vector2Int(6,5), new Vector2Int(7,5), new Vector2Int(8,5) },
                                     sinkCell: null);
            var snap = Analyze(grid, new HashSet<int> { 1 }, new HashSet<int>());
            Assert.AreEqual(1, snap.chains.Count);
            Assert.IsFalse(snap.chains[0].endsAtEntropySink);
        }

        [Test]
        public void Analyzer_CollectorLinkedToEntropySink_ReturnsSinkChain()
        {
            var grid = MakeGridChain(new Vector2Int(5, 5),
                                     new[] { new Vector2Int(6,5), new Vector2Int(7,5), new Vector2Int(8,5) },
                                     sinkCell: new Vector2Int(9, 5));
            var snap = Analyze(grid, new HashSet<int> { 1 }, new HashSet<int> { 2 });
            Assert.AreEqual(1, snap.chains.Count);
            Assert.IsTrue(snap.chains[0].endsAtEntropySink);
        }

        [Test]
        public void Analyzer_NonCollectorBuildingAtHead_NotIncluded()
        {
            var grid = MakeGridChain(new Vector2Int(5, 5),
                                     new[] { new Vector2Int(6,5), new Vector2Int(7,5) },
                                     sinkCell: null);
            // Pass empty collector set → building is not a collector
            var snap = Analyze(grid, new HashSet<int>(), new HashSet<int>());
            Assert.AreEqual(0, snap.chains.Count);
        }

        [Test]
        public void Analyzer_MultipleCollectors_AllCaptured()
        {
            var grid = new GridSaveData
            {
                buildings = new List<BuildingSaveData>
                {
                    new BuildingSaveData { buildingId = 1, recipeId = 0, position = new[] { 0, 0 } },
                    new BuildingSaveData { buildingId = 1, recipeId = 0, position = new[] { 0, 5 } },
                },
                conveyors = new List<ConveyorSaveData>
                {
                    new ConveyorSaveData { cells = new[] { 1,0, 2,0, 3,0 } },
                    new ConveyorSaveData { cells = new[] { 1,5, 2,5, 3,5 } },
                }
            };
            var snap = Analyze(grid, new HashSet<int> { 1 }, new HashSet<int>());
            Assert.AreEqual(2, snap.chains.Count);
        }

        [Test]
        public void Analyzer_SetsSnapshotTimestamp()
        {
            string ts = "2026-06-06T12:00:00.000Z";
            var snap  = IdleGraphAnalyzer.BuildSnapshot(
                new GridSaveData(),
                _ => false, _ => false, _ => Vector2Int.one,
                (_, __) => -1, _ => 0f, _ => 0f,
                1f, ts);
            Assert.AreEqual(ts, snap.snapshotTimestampUtc);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static SaveData MakeSave(IdleCollectionSnapshot snapshot,
                                         float secondsAgo = 3600f,
                                         string lastSaved  = null)
        {
            return new SaveData
            {
                lastSaved    = lastSaved ?? DateTime.UtcNow.AddSeconds(-secondsAgo).ToString("O"),
                currentRun   = new CurrentRunData(),
                idleSnapshot = snapshot
            };
        }

        private static IdleCollectionSnapshot SingleInventoryChain() =>
            OneChain(new IdleChainEntry
                { itemId = 1, itemsPerSecond = 1f, endsAtEntropySink = false, baseSellValue = 1f });

        private static IdleCollectionSnapshot OneChain(IdleChainEntry chain) =>
            new IdleCollectionSnapshot { chains = new List<IdleChainEntry> { chain } };

        private static IdleCollectionSnapshot Analyze(
            GridSaveData grid, HashSet<int> collectorIds, HashSet<int> sinkIds) =>
            IdleGraphAnalyzer.BuildSnapshot(
                grid,
                id => collectorIds.Contains(id),
                id => sinkIds.Contains(id),
                _ => Vector2Int.one,
                (_, __) => 10,
                _ => 1f,
                _ => 5f,
                1f,
                DateTime.UtcNow.ToString("O"));

        private static GridSaveData MakeGridChain(
            Vector2Int collectorCell,
            Vector2Int[] conveyorCells,
            Vector2Int? sinkCell)
        {
            var buildings = new List<BuildingSaveData>
            {
                new BuildingSaveData
                {
                    buildingId = 1, recipeId = 0,
                    position   = new[] { collectorCell.x, collectorCell.y }
                }
            };
            if (sinkCell.HasValue)
                buildings.Add(new BuildingSaveData
                {
                    buildingId = 2, recipeId = -1,
                    position   = new[] { sinkCell.Value.x, sinkCell.Value.y }
                });

            var cells = new List<int>();
            foreach (var c in conveyorCells) { cells.Add(c.x); cells.Add(c.y); }

            return new GridSaveData
            {
                buildings = buildings,
                conveyors = new List<ConveyorSaveData>
                    { new ConveyorSaveData { cells = cells.ToArray() } }
            };
        }
    }
}
