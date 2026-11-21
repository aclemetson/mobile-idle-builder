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

        // ── ComputeForDuration (Time Warp instant collection) ─────────────────

        [Test]
        public void ComputeForDuration_DoesNotMutateSave()
        {
            var save = MakeSave(OneChain(
                new IdleChainEntry { itemId = 7, itemsPerSecond = 1f, endsAtEntropySink = true, baseSellValue = 3f }));
            // 1/s × 7200 s × 3 sell = 21600 entropy (full production, not the 0.5 idle rate)
            var result = OfflineCollectionService.ComputeForDuration(save, _cfg, null, 7200f);

            Assert.IsNotNull(result);
            Assert.AreEqual(21600L, result.EntropyEarned);
            Assert.AreEqual(0L, save.currentRun.baseCurrency, "ComputeForDuration must not mutate the save");
            Assert.IsNull(save.idleCollectionApplied, "ComputeForDuration must not stamp the idempotency guard");
        }

        [Test]
        public void ComputeForDuration_IgnoresIdleCap()
        {
            var save = MakeSave(OneChain(
                new IdleChainEntry { itemId = 7, itemsPerSecond = 1f, endsAtEntropySink = true, baseSellValue = 3f }));
            // 24 h far exceeds the cfg cap (2 h) but Time Warp pays the full requested duration.
            var result = OfflineCollectionService.ComputeForDuration(save, _cfg, null, 86400f);

            Assert.IsNotNull(result);
            Assert.AreEqual(259200L, result.EntropyEarned, "1/s × 86400 s × 3 — no cap, full rate");
        }

        [Test]
        public void ComputeForDuration_NoProduction_ReturnsNull()
        {
            var save = MakeSave(new IdleCollectionSnapshot { chains = new List<IdleChainEntry>() });
            Assert.IsNull(OfflineCollectionService.ComputeForDuration(save, _cfg, null, 7200f));
        }

        // ── World delivery tasks from bonuses (Time Warp, Double Offline, Entropy Surge) ──────

        [Test]
        public void Payout_EntropyOnlyChains_DeliverNothingToTheDemon()
        {
            var save = MakeSave(new IdleCollectionSnapshot { chains = new List<IdleChainEntry>
            {
                new IdleChainEntry { itemId = 6, itemsPerSecond = 1f, endsAtEntropySink = true, baseSellValue = 1f },
                new IdleChainEntry { itemId = 7, itemsPerSecond = 1f, endsAtEntropySink = true, baseSellValue = 1f,
                                     notDelivered = true }, // byproduct entropy / surplus sale
            }});
            var result = OfflineCollectionService.ComputeForDuration(save, _cfg, null, 100f);
            Assert.AreEqual(200L, result.EntropyEarned, "both pay entropy");
            Assert.AreEqual(100L, result.DemonDelivered[6]);
            Assert.IsFalse(result.DemonDelivered.ContainsKey(7), "only items that reach the Demon count");
        }

        [Test]
        public void TimeWarp_CreditsTheWorldTasks_AndQueuesTheFinishedStage()
        {
            var prev = WorldTasks.Data;
            var tasks = ScriptableObject.CreateInstance<WorldTaskSO>();
            var carbon = ScriptableObject.CreateInstance<ItemSO>();
            try
            {
                carbon.itemId = 6;
                tasks.worldId = "world_chemistry";
                tasks.stages = new[]
                {
                    new WorldTaskStage { id = "s1", items = new[] { carbon }, quantities = new[] { 100 } },
                    new WorldTaskStage { id = "s2", items = new[] { carbon }, quantities = new[] { 1000 } },
                };
                WorldTasks.Data = tasks;
                WorldTasks.PendingAnnouncements.Clear();
                int rev = WorldTasks.Revision;

                var save = MakeSave(OneChain(
                    new IdleChainEntry { itemId = 6, itemsPerSecond = 1f, endsAtEntropySink = true, baseSellValue = 1f }));
                var result = OfflineCollectionService.ComputeForDuration(save, _cfg, null, 300f); // 300 carbon, full rate
                long counted = OfflineCollectionService.ApplyResultToSave(save, result);

                Assert.AreEqual(300L, counted, "100 finish stage 1, the other 200 go on to stage 2 as they would live");
                Assert.AreEqual(1, save.worldTaskStage);
                Assert.AreEqual(200, WorldTasks.Delivered(save, 6));
                Assert.AreEqual(new[] { "s1" }, WorldTasks.PendingAnnouncements.ConvertAll(s => s.id).ToArray(),
                                "the service announces a stage a bonus finished");
                Assert.AreNotEqual(rev, WorldTasks.Revision, "the UI is told progress moved");
            }
            finally
            {
                WorldTasks.Data = prev;
                WorldTasks.PendingAnnouncements.Clear();
                UnityEngine.Object.DestroyImmediate(tasks);
                UnityEngine.Object.DestroyImmediate(carbon);
            }
        }

        [Test]
        public void LiveDemonDeliveries_AreFullRate_ForEntropySurge()
        {
            var save = MakeSave(new IdleCollectionSnapshot { chains = new List<IdleChainEntry>
            {
                new IdleChainEntry { itemId = 6, itemsPerSecond = 2f, endsAtEntropySink = true,  baseSellValue = 1f },
                new IdleChainEntry { itemId = 8, itemsPerSecond = 5f, endsAtEntropySink = false, baseSellValue = 1f },
            }});
            var delivered = OfflineCollectionService.LiveDemonDeliveries(save, 600f);
            Assert.AreEqual(1200L, delivered[6], "10 minutes of 2/s, no idle-rate cut (the income is live income)");
            Assert.IsFalse(delivered.ContainsKey(8), "items going to inventory are not Demon deliveries");
        }

        [Test]
        public void TaskSuffix_ShowsOnlyWhenItemsCounted()
        {
            Assert.AreEqual("", AdService.TaskSuffix(0));
            Assert.AreEqual(" (+1,200 task items)", AdService.TaskSuffix(1200));
        }

        // ── Rewarded-ad idle-rate boost (+50%) ────────────────────────────────

        [Test]
        public void AdIdleMultiplier_IsOne_WhenNoBoost()
        {
            var save = MakeSave(null);
            save.adsIdleBoostExpiryUtc = null;
            Assert.AreEqual(1f, OfflineCollectionService.GetAdIdleMultiplier(save));
        }

        [Test]
        public void AdIdleMultiplier_IsBoosted_WhenActive()
        {
            var save = MakeSave(null);
            save.adsIdleBoostExpiryUtc = DateTime.UtcNow.AddHours(1).ToString("O");
            Assert.AreEqual(AdRewardCalculator.IdleRateBoostMultiplier,
                            OfflineCollectionService.GetAdIdleMultiplier(save));
        }

        [Test]
        public void AdIdleMultiplier_IsOne_WhenExpired()
        {
            var save = MakeSave(null);
            save.adsIdleBoostExpiryUtc = DateTime.UtcNow.AddHours(-1).ToString("O");
            Assert.AreEqual(1f, OfflineCollectionService.GetAdIdleMultiplier(save));
        }

        [Test]
        public void ComputeForDuration_IsFullProduction_IdleRateAndIdleBoostDoNotApply()
        {
            var save = MakeSave(OneChain(
                new IdleChainEntry { itemId = 7, itemsPerSecond = 1f, endsAtEntropySink = true, baseSellValue = 3f }));
            save.adsIdleBoostExpiryUtc = DateTime.UtcNow.AddHours(1).ToString("O");

            // A warp banks real production: the 0.5 idle rate and the +50% idle-rate ad boost are for time away.
            var result = OfflineCollectionService.ComputeForDuration(save, _cfg, null, 3600f);

            Assert.IsNotNull(result);
            Assert.AreEqual(10800L, result.EntropyEarned, "1/s × 3600 s × 3 sell");
            Assert.AreEqual(3600L, result.DemonDelivered[7], "an hour of Demon deliveries, all counting for tasks");
        }

        // ── Multi-site aggregation (siteSnapshots) ────────────────────────────

        [Test]
        public void SiteSnapshots_AggregatesAcrossAllSites()
        {
            // Site 0: 1 item/s entropy sink, sell 2 → 1×100×0.5×2 = 100 entropy
            // Site 1: 2 items/s entropy sink, sell 1 → 2×100×0.5×1 = 100 entropy
            var save = MakeSave(null, secondsAgo: 100f);
            save.siteSnapshots = new List<IdleCollectionSnapshot>
            {
                OneChain(new IdleChainEntry { itemId = 1, itemsPerSecond = 1f, endsAtEntropySink = true, baseSellValue = 2f }),
                OneChain(new IdleChainEntry { itemId = 2, itemsPerSecond = 2f, endsAtEntropySink = true, baseSellValue = 1f }),
            };

            var result = OfflineCollectionService.CalculateAndApply(save, _cfg, null);

            Assert.IsNotNull(result);
            Assert.AreEqual(200L, result.EntropyEarned, "100 from site 0 + 100 from site 1");
            Assert.AreEqual(200L, save.currentRun.baseCurrency);
        }

        [Test]
        public void SiteSnapshots_PopulatedTakesPrecedenceOverIdleSnapshot_NoDoubleCount()
        {
            // idleSnapshot mirrors site 0; when siteSnapshots is present it must NOT be added again.
            var activeChain = OneChain(new IdleChainEntry
                { itemId = 1, itemsPerSecond = 1f, endsAtEntropySink = true, baseSellValue = 1f });
            var save = MakeSave(activeChain, secondsAgo: 100f);
            save.siteSnapshots = new List<IdleCollectionSnapshot> { activeChain }; // same object as idleSnapshot

            var result = OfflineCollectionService.CalculateAndApply(save, _cfg, null);

            Assert.IsNotNull(result);
            Assert.AreEqual(50L, result.EntropyEarned, "1×100×0.5×1 = 50, counted once");
        }

        [Test]
        public void SiteSnapshots_EmptyFallsBackToIdleSnapshot()
        {
            var save = MakeSave(OneChain(new IdleChainEntry
                { itemId = 1, itemsPerSecond = 1f, endsAtEntropySink = true, baseSellValue = 1f }),
                secondsAgo: 100f);
            save.siteSnapshots = new List<IdleCollectionSnapshot>(); // empty → use idleSnapshot

            var result = OfflineCollectionService.CalculateAndApply(save, _cfg, null);

            Assert.IsNotNull(result);
            Assert.AreEqual(50L, result.EntropyEarned);
        }

        [Test]
        public void SiteSnapshots_AllEmptyChains_ReturnsNull()
        {
            var save = MakeSave(null, secondsAgo: 100f);
            save.siteSnapshots = new List<IdleCollectionSnapshot>
            {
                new IdleCollectionSnapshot { chains = new List<IdleChainEntry>() },
                new IdleCollectionSnapshot { chains = new List<IdleChainEntry>() },
            };
            Assert.IsNull(OfflineCollectionService.CalculateAndApply(save, _cfg, null));
        }

        // ── GetEffectiveIdleCap ───────────────────────────────────────────────

        [Test]
        public void GetEffectiveIdleCap_NoUpgrades_ReturnsBase()
        {
            float cap = OfflineCollectionService.GetEffectiveIdleCap(_cfg, null);
            Assert.AreEqual(7200f, cap, 0.01f);
        }

        [Test]
        public void ShippedConfig_IdleCapStartsAtThirtyMinutes()
        {
            var cfg = ScriptableObject.CreateInstance<GameConfigSO>();
            try { Assert.AreEqual(1800f, OfflineCollectionService.GetEffectiveIdleCap(cfg, null), 0.01f); }
            finally { UnityEngine.Object.DestroyImmediate(cfg); }
        }

        [Test]
        public void DormantResonance_IsCheapEarly_AndReachesTheAbsoluteMax()
        {
            var def = System.Array.Find(PersistentUpgradeService.All, d => d.id == "idle_time_cap");
            Assert.AreEqual(0, def.prereqs.Length, "no prerequisite: buyable right after the first prestige");
            Assert.AreEqual(5, def.levelCosts[0]);
            Assert.LessOrEqual(def.levelCosts[0] + def.levelCosts[1] + def.levelCosts[2] + def.levelCosts[3], 50,
                "first four levels (back to the old 2.5 h) fit in a first prestige's currency");
            Assert.AreEqual(def.maxLevel, def.levelCosts.Length);
            Assert.AreEqual(43200f, 1800f + def.maxLevel * def.effectPerLevel, 0.01f, "30 min base + max levels = 12 h");
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
        public void Analyzer_ManagerOutputMultiplier_ScalesSourceCollectorRate()
        {
            var grid = MakeGridChain(new Vector2Int(5, 5),
                                     new[] { new Vector2Int(6,5), new Vector2Int(7,5), new Vector2Int(8,5) },
                                     sinkCell: null);

            // base rate is 1f (getOutputRate => 1f); a 3x OutputQuantity manager on the source.
            var snap = IdleGraphAnalyzer.BuildSnapshot(
                grid,
                id => id == 1,
                _ => false,
                _ => Vector2Int.one,
                (_, __) => 10,
                _ => 1f,
                _ => 5f,
                1f,
                DateTime.UtcNow.ToString("O"),
                _ => 3f);

            Assert.AreEqual(1, snap.chains.Count);
            Assert.AreEqual(3f, snap.chains[0].itemsPerSecond, 1e-4f,
                "OutputQuantity manager must scale the source collector's offline rate");
        }

        [Test]
        public void Analyzer_RotatedMultiCellSink_ConnectsBeltAtItsRotatedCell()
        {
            // A 2x1 sink rotated 90° covers (5,5) and (5,6). The belt ends at (4,6), which only touches
            // the sink's rotated second cell, so the flow must still be credited as entropy.
            var grid = new GridSaveData
            {
                buildings = new List<BuildingSaveData>
                {
                    new BuildingSaveData { buildingId = 1, recipeId = 0,  position = new[] { 0, 6 } },
                    new BuildingSaveData { buildingId = 2, recipeId = -1, position = new[] { 5, 5 }, rotation = 1 },
                },
                conveyors = new List<ConveyorSaveData>
                {
                    new ConveyorSaveData { cells = new[] { 1,6, 2,6, 3,6, 4,6 } },
                }
            };

            var snap = IdleGraphAnalyzer.BuildSnapshot(
                grid,
                id => id == 1,
                id => id == 2,
                id => id == 2 ? new Vector2Int(2, 1) : Vector2Int.one,
                (_, __) => 10,
                _ => 1f,
                _ => 5f,
                1f,
                DateTime.UtcNow.ToString("O"));

            Assert.AreEqual(1, snap.chains.Count);
            Assert.IsTrue(snap.chains[0].endsAtEntropySink,
                "belt into a rotated multi-cell sink must earn entropy, not fall back to inventory");
        }

        [Test]
        public void Analyzer_SynthByproductEntropy_PaysPerCraftEvenIntoInventory()
        {
            // Collector (item 10 at 2/s) -> belt -> synth (2 x item 10 -> item 20, 1s, +5 entropy per craft)
            // -> belt -> nowhere. The synth crafts 1/s: item 20 goes to inventory, the bonus to entropy.
            var grid = new GridSaveData
            {
                buildings = new List<BuildingSaveData>
                {
                    new BuildingSaveData { buildingId = 1, recipeId = 0, position = new[] { 0, 0 } },
                    new BuildingSaveData { buildingId = 3, recipeId = 7, position = new[] { 4, 0 } },
                },
                conveyors = new List<ConveyorSaveData>
                {
                    new ConveyorSaveData { cells = new[] { 1,0, 2,0, 3,0 } },
                    new ConveyorSaveData { cells = new[] { 5,0, 6,0 } },
                }
            };

            var snap = IdleGraphAnalyzer.BuildSnapshot(
                grid,
                id => id == 1,
                _ => false,
                _ => Vector2Int.one,
                (_, __) => 10,
                _ => 2f,
                _ => 1f,
                1f,
                DateTime.UtcNow.ToString("O"),
                null,
                (b, _) => b == 3
                    ? new IdleRecipeInfo
                    {
                        Valid = true, OutputItemId = 20, OutputQuantity = 1, CraftTimeSeconds = 1f,
                        InputItemIds = new[] { 10 }, InputQuantities = new[] { 2 }, ByproductEntropy = 5f
                    }
                    : default);

            var bonus = snap.chains.Find(c => c.endsAtEntropySink);
            Assert.IsNotNull(bonus, "the synth's escaping byproducts must be an entropy chain");
            Assert.AreEqual(1f, bonus.itemsPerSecond, 1e-4f, "one bonus per craft, 1 craft/s");
            Assert.AreEqual(5f, bonus.baseSellValue, 1e-4f);
            Assert.IsTrue(bonus.notDelivered, "escape entropy is not item 20 reaching the Demon (world tasks)");
            Assert.IsTrue(snap.chains.Exists(c => !c.endsAtEntropySink && c.itemId == 20),
                "the main output still goes to inventory");
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

        // ── Input-limited synthesizer chains (collector → synth → sink) ────────

        [Test]
        public void Analyzer_SynthesizerBetweenCollectorAndSink_CreditsSynthesizedItemToSink()
        {
            // Collector (quark) → synthesizer (quark → hydrogen) → Maxwell's Demon. The demon must be
            // credited the HYDROGEN's value, at a rate limited by the collector's throughput.
            var snap = AnalyzeSynth(MakeCollectorSynthSinkGrid(),
                                    collectorRate: 2f, inputQty: 1, craftTime: 0.001f);

            Assert.AreEqual(1, snap.chains.Count, "exactly one terminal flow (synth → sink)");
            Assert.IsTrue(snap.chains[0].endsAtEntropySink, "the flow ends at the entropy sink");
            Assert.AreEqual(HydrogenItem, snap.chains[0].itemId, "the sink consumes the synthesized hydrogen");
            Assert.AreEqual(2f, snap.chains[0].itemsPerSecond, 1e-3f, "input-limited to the collector's 2/s of quarks");
            Assert.AreEqual(50f, snap.chains[0].baseSellValue, 1e-3f, "hydrogen's sell value, not the quark's");
            Assert.IsFalse(snap.chains[0].notDelivered, "hydrogen really reaches the Demon, so it counts for world tasks");
        }

        [Test]
        public void Analyzer_SynthesizerOutput_IsInputLimited_ByCollectorThroughput()
        {
            // 0.5 quark/s, 2 quarks per craft → 0.25 crafts/s → 0.25 hydrogen/s.
            var snap = AnalyzeSynth(MakeCollectorSynthSinkGrid(),
                                    collectorRate: 0.5f, inputQty: 2, craftTime: 0.001f);

            Assert.AreEqual(1, snap.chains.Count);
            Assert.AreEqual(0.25f, snap.chains[0].itemsPerSecond, 1e-4f,
                "offline synth output cannot exceed what the upstream collector supplies");
        }

        [Test]
        public void Analyzer_SynthesizerOutput_IsCraftRateLimited_WhenInputAbundant()
        {
            // Abundant quarks (100/s) but a 1s craft → capped at 1 craft/s → 1 hydrogen/s.
            var snap = AnalyzeSynth(MakeCollectorSynthSinkGrid(),
                                    collectorRate: 100f, inputQty: 1, craftTime: 1f);

            Assert.AreEqual(1, snap.chains.Count);
            Assert.AreEqual(1f, snap.chains[0].itemsPerSecond, 1e-4f,
                "with inputs abundant the synth is capped by its craft time");
        }

        [Test]
        public void Analyzer_SynthesizerWithNoUpstreamFeed_ProducesNoChain()
        {
            // Synth → sink but nothing feeds the synth: no offline output at all.
            var grid = new GridSaveData
            {
                buildings = new List<BuildingSaveData>
                {
                    new BuildingSaveData { buildingId = 3, recipeId = 0,  position = new[] { 0, 3 } },
                    new BuildingSaveData { buildingId = 2, recipeId = -1, position = new[] { 0, 6 } },
                },
                conveyors = new List<ConveyorSaveData>
                {
                    new ConveyorSaveData { cells = new[] { 0,4, 0,5 } }, // synth → sink
                }
            };

            var snap = AnalyzeSynth(grid, collectorRate: 0f, inputQty: 1, craftTime: 1f);
            Assert.AreEqual(0, snap.chains.Count, "a starved synthesizer earns nothing offline");
        }

        [Test]
        public void Analyzer_StarvedSynthesizer_CreditsRawInputToInventory_NonRegressive()
        {
            // Collector(quark) → synth(needs quark + electron) → sink, but nothing supplies the electron, so
            // the synth is starved (0 output). The collector's quark must NOT be silently lost — it falls
            // back to an inventory credit, guaranteeing the offline notification never vanishes entirely.
            const int ElectronItem = 11;
            var snap = IdleGraphAnalyzer.BuildSnapshot(
                MakeCollectorSynthSinkGrid(),
                id => id == 1, id => id == 2, _ => Vector2Int.one,
                (bid, _) => bid == 1 ? QuarkItem : -1,
                id => id == 1 ? 2f : 0f,
                itemId => itemId == HydrogenItem ? 50f : 1f,
                1f, DateTime.UtcNow.ToString("O"), null,
                (bid, _) => bid == 3
                    ? new IdleRecipeInfo
                    {
                        Valid = true, OutputItemId = HydrogenItem, OutputQuantity = 1, CraftTimeSeconds = 0.001f,
                        InputItemIds = new[] { QuarkItem, ElectronItem }, InputQuantities = new[] { 1, 1 }
                    }
                    : default);

            Assert.AreEqual(1, snap.chains.Count, "the collector's output is not lost when the synth starves");
            Assert.IsFalse(snap.chains[0].endsAtEntropySink, "a stuck raw input falls back to an inventory credit");
            Assert.AreEqual(QuarkItem, snap.chains[0].itemId);
            Assert.AreEqual(2f, snap.chains[0].itemsPerSecond, 1e-3f, "credited at the collector's raw rate");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private const int QuarkItem    = 10;
        private const int HydrogenItem = 99;

        // Collector(1) → synthesizer(3) → sink(2), stacked on the y axis with a 2-cell belt between each.
        private static GridSaveData MakeCollectorSynthSinkGrid() => new GridSaveData
        {
            buildings = new List<BuildingSaveData>
            {
                new BuildingSaveData { buildingId = 1, recipeId = 0,  position = new[] { 0, 0 } }, // collector
                new BuildingSaveData { buildingId = 3, recipeId = 0,  position = new[] { 0, 3 } }, // synthesizer
                new BuildingSaveData { buildingId = 2, recipeId = -1, position = new[] { 0, 6 } }, // sink
            },
            conveyors = new List<ConveyorSaveData>
            {
                new ConveyorSaveData { cells = new[] { 0,1, 0,2 } }, // collector → synth
                new ConveyorSaveData { cells = new[] { 0,4, 0,5 } }, // synth → sink
            }
        };

        private static IdleCollectionSnapshot AnalyzeSynth(
            GridSaveData grid, float collectorRate, int inputQty, float craftTime,
            int outputQty = 1, float hydrogenSell = 50f) =>
            IdleGraphAnalyzer.BuildSnapshot(
                grid,
                id => id == 1,                                   // collector
                id => id == 2,                                   // entropy sink
                _ => Vector2Int.one,
                (bid, _) => bid == 1 ? QuarkItem : -1,           // collector outputs quark
                id => id == 1 ? collectorRate : 0f,
                itemId => itemId == HydrogenItem ? hydrogenSell : 1f,
                1f,                                              // global speed multiplier
                DateTime.UtcNow.ToString("O"),
                null,                                            // no manager multiplier
                (bid, _) => bid == 3
                    ? new IdleRecipeInfo
                    {
                        Valid = true, OutputItemId = HydrogenItem, OutputQuantity = outputQty,
                        CraftTimeSeconds = craftTime,
                        InputItemIds = new[] { QuarkItem }, InputQuantities = new[] { inputQty }
                    }
                    : default);

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
