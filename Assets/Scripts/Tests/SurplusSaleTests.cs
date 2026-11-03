using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>#312: items a building can't use are sold at part of the Demon price, raised by a permanent upgrade.</summary>
    [TestFixture]
    public class SurplusSaleTests
    {
        // ── Price ────────────────────────────────────────────────────────────

        [Test]
        public void Fraction_StartsAtHalf_RisesWithTheUpgrade_CapsAtFull()
        {
            Assert.AreEqual(0.5f, SurplusSale.Fraction(0f), 1e-6f);
            Assert.AreEqual(0.8f, SurplusSale.Fraction(0.3f), 1e-6f);
            Assert.AreEqual(1f,   SurplusSale.Fraction(0.5f), 1e-6f);
            Assert.AreEqual(1f,   SurplusSale.Fraction(2f),   1e-6f, "never pays more than the Demon");
            Assert.AreEqual(0.5f, SurplusSale.Fraction(-1f),  1e-6f, "a bad bonus can't push it below base");
        }

        [Test]
        public void Value_IsDemonPriceTimesQuantityTimesFraction_RoundedDown()
        {
            Assert.AreEqual(80, SurplusSale.Value(160f, 1, 0.5f));
            Assert.AreEqual(5,  SurplusSale.Value(1f, 11, 0.5f));
            Assert.AreEqual(0,  SurplusSale.Value(10f, 0, 0.5f));
            Assert.AreEqual(0.5d, SurplusSale.ExactValue(1f, 1, 0.5f), 1e-9);
        }

        [Test]
        public void SurplusResaleUpgrade_ExistsWithFiveTenPercentLevels()
        {
            var def = Array.Find(PersistentUpgradeService.All,u => u.effectType == UpgradeEffectType.SurplusResale);
            Assert.IsNotNull(def, "the penalty must be reduced through the permanent upgrade shop");
            Assert.AreEqual(0.10f, def.effectPerLevel, 1e-6f);
            Assert.AreEqual(5, def.maxLevel);
            Assert.AreEqual(1f, SurplusSale.Fraction(def.effectPerLevel * def.maxLevel), 1e-6f,
                "max level removes the penalty entirely");
        }

        // ── Recipe change sells unused inputs ────────────────────────────────

        [Test]
        public void SellUnusedInputs_SellsOnlyWhatTheRecipeDoesNotUse()
        {
            using var world = new World("SurplusSaleTest");
            var em = world.EntityManager;
            var e  = em.CreateEntity();
            em.AddBuffer<BuildingInputSlot>(e);
            em.AddBuffer<RecipeInputSlot>(e);   // add both before taking handles: a structural change invalidates them
            var input  = em.GetBuffer<BuildingInputSlot>(e);
            var recipe = em.GetBuffer<RecipeInputSlot>(e);
            recipe.Add(new RecipeInputSlot { ItemID = 7, Quantity = 3 });   // new recipe uses item 7
            input.Add(new BuildingInputSlot { ItemID = 7, Quantity = 2 });   // kept
            input.Add(new BuildingInputSlot { ItemID = 4, Quantity = 3 });   // sold: 3 x 3e x 0.5 = 4.5
            input.Add(new BuildingInputSlot { ItemID = 5, Quantity = 1 });   // sold: 1 x 3e x 0.5 = 1.5

            long earned = SurplusSale.SellUnusedInputs(em.GetBuffer<BuildingInputSlot>(e),
                                                       em.GetBuffer<RecipeInputSlot>(e), _ => 3f, 0.5f);

            Assert.AreEqual(6, earned, "4.5 + 1.5 rounds once over the whole sale");
            var left = em.GetBuffer<BuildingInputSlot>(e);
            Assert.AreEqual(1, left.Length);
            Assert.AreEqual(7, left[0].ItemID);
            Assert.AreEqual(2, left[0].Quantity);
        }

        // ── Offline: stuck items sell at the same rate ───────────────────────

        [Test]
        public void IdleGraph_ItemsStuckAtAStarvedBuilding_SellAtTheSurplusPrice()
        {
            // Collector (item 10, 1/s) -> belt -> synth that needs item 99, which nothing makes: starved.
            var grid = new GridSaveData
            {
                buildings = new List<BuildingSaveData>
                {
                    new BuildingSaveData { buildingId = 1, recipeId = 0, position = new[] { 0, 0 } },
                    new BuildingSaveData { buildingId = 3, recipeId = 7, position = new[] { 4, 0 } },
                },
                conveyors = new List<ConveyorSaveData> { new ConveyorSaveData { cells = new[] { 1,0, 2,0, 3,0 } } }
            };
            IdleRecipeInfo Starved(int b, int _) => b == 3
                ? new IdleRecipeInfo { Valid = true, OutputItemId = 20, OutputQuantity = 1, CraftTimeSeconds = 1f,
                                       InputItemIds = new[] { 99 }, InputQuantities = new[] { 1 } }
                : default;

            IdleCollectionSnapshot Build(float fraction) => IdleGraphAnalyzer.BuildSnapshot(
                grid, id => id == 1, _ => false, _ => Vector2Int.one, (_, __) => 10, _ => 1f, _ => 8f, 1f,
                DateTime.UtcNow.ToString("O"), null, Starved, fraction);

            var sold = Build(0.5f).chains;
            Assert.AreEqual(1, sold.Count);
            Assert.IsTrue(sold[0].endsAtEntropySink, "stuck items are sold, not sent to inventory");
            Assert.AreEqual(4f, sold[0].baseSellValue, 1e-4f, "8e item at 50%");

            var legacy = Build(0f).chains;
            Assert.IsFalse(legacy[0].endsAtEntropySink, "with no surplus rate the old inventory fallback stands");
            Assert.AreEqual(8f, legacy[0].baseSellValue, 1e-4f);
        }
    }
}
