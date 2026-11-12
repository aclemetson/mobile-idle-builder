using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Full-input autosell (docs/agents/tasks/feature-full-input-autosell.md): an item the recipe uses that
    /// reaches a full input is sold at the surplus price. The decision and belt behaviour are covered in
    /// InputAcceptanceTests and ConveyorHandoffTests; this covers the inspector rate and offline earnings.
    /// </summary>
    [TestFixture]
    public class FullInputAutosellTests
    {
        [TearDown]
        public void TearDown() => SurplusSaleLog.Clear();

        // ── Inspector: recent sales per building ─────────────────────────────

        [Test]
        public void SaleLog_RatesOverItsWindow_AndForgetsOldSales()
        {
            var b = new Entity { Index = 7, Version = 1 };
            for (int i = 0; i < 10; i++) SurplusSaleLog.Record(b, 0.5, now: i * 0.5f); // 2 items/s for 5 s

            var (items, paid) = SurplusSaleLog.Rate(b, now: 4.5f);
            Assert.AreEqual(10f / SurplusSaleLog.WindowSeconds, items, 1e-4f);
            Assert.AreEqual(5d / SurplusSaleLog.WindowSeconds, paid, 1e-6);

            Assert.AreEqual(0f, SurplusSaleLog.Rate(b, now: 100f).itemsPerSecond, "old sales drop out");
        }

        [Test]
        public void SaleLog_IsPerBuilding()
        {
            var a = new Entity { Index = 1, Version = 1 };
            var b = new Entity { Index = 2, Version = 1 };
            SurplusSaleLog.Record(a, 1, now: 1f);
            Assert.AreEqual(0f, SurplusSaleLog.Rate(b, now: 1f).itemsPerSecond);
        }

        [Test]
        public void SurplusLine_ReadsPlainly()
        {
            Assert.AreEqual("Surplus: none (inputs keep up)", HUDBuildingInspectorSubController.SurplusLine(0f, 0d, 0.5f));
            Assert.AreEqual("Selling surplus: 0.5/s for 0.25 ◈/s (50% of value)",
                HUDBuildingInspectorSubController.SurplusLine(0.5f, 0.25d, 0.5f));
        }

        // ── Offline: the excess an over-supplied synth can't use is sold, as live ──

        private const int UpQuark = 1, DownQuark = 2, Proton = 3;

        /// <summary>
        /// Up harvester (type 1) and down harvester (type 2) each belted into a Combiner (type 3) on proton
        /// (2 up + 1 down, 1 s). Harvesters make 2/s: the Combiner uses 2 up/s and 1 down/s, so 1 down/s is
        /// surplus. The Combiner's protons belt into a sink (type 4).
        /// </summary>
        private static IdleCollectionSnapshot Snapshot(float surplusFraction)
        {
            var grid = new GridSaveData
            {
                buildings = new List<BuildingSaveData>
                {
                    new BuildingSaveData { buildingId = 1, recipeId = 0, position = new[] { 0, 5 } },
                    new BuildingSaveData { buildingId = 2, recipeId = 0, position = new[] { 5, 0 } },
                    new BuildingSaveData { buildingId = 3, recipeId = 0, position = new[] { 5, 5 } },
                    new BuildingSaveData { buildingId = 4, recipeId = -1, position = new[] { 10, 5 } },
                },
                conveyors = new List<ConveyorSaveData>
                {
                    new ConveyorSaveData { cells = new[] { 1, 5, 2, 5, 3, 5, 4, 5 } },   // up → Combiner (from W)
                    new ConveyorSaveData { cells = new[] { 5, 1, 5, 2, 5, 3, 5, 4 } },   // down → Combiner (from S)
                    new ConveyorSaveData { cells = new[] { 6, 5, 7, 5, 8, 5, 9, 5 } },   // protons → sink
                }
            };

            return IdleGraphAnalyzer.BuildSnapshot(
                grid,
                id => id == 1 || id == 2,
                id => id == 4,
                _ => Vector2Int.one,
                (b, _) => b == 1 ? UpQuark : b == 2 ? DownQuark : -1,
                _ => 2f,
                item => item == Proton ? 3f : 1f,
                1f,
                DateTime.UtcNow.ToString("O"),
                null,
                (b, _) => b == 3
                    ? new IdleRecipeInfo
                    {
                        Valid = true, OutputItemId = Proton, OutputQuantity = 1, CraftTimeSeconds = 1f,
                        InputItemIds = new[] { UpQuark, DownQuark }, InputQuantities = new[] { 2, 1 }
                    }
                    : default,
                surplusFraction);
        }

        [Test]
        public void Offline_OverSuppliedInput_SellsTheExcess_AtTheSurplusPrice()
        {
            var snap = Snapshot(0.5f);
            var downSale = snap.chains.Find(c => c.itemId == DownQuark);
            Assert.IsNotNull(downSale, "the down quarks the Combiner can't use are sold offline");
            Assert.AreEqual(1f, downSale.itemsPerSecond, 1e-4f);
            Assert.IsTrue(downSale.endsAtEntropySink);
            Assert.AreEqual(0.5f, downSale.baseSellValue, 1e-5f);

            Assert.IsNull(snap.chains.Find(c => c.itemId == UpQuark), "every up quark is used: no surplus");
            var protons = snap.chains.Find(c => c.itemId == Proton);
            Assert.AreEqual(1f, protons.itemsPerSecond, 1e-4f, "the Combiner still crafts 1 proton/s");
        }

        [Test]
        public void Offline_WithoutSurplusSales_NothingExtraIsCredited()
        {
            var snap = Snapshot(0f);
            Assert.IsNull(snap.chains.Find(c => c.itemId == DownQuark));
        }
    }
}
