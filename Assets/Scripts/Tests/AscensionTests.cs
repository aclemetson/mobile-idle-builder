using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Item Timeline phase 4 (2026-11-23): Ascension pays megastructure material and resets more than a prestige;
    /// material buys Expansion levels whose cost grows faster than the payout. And the Engineering world.
    /// </summary>
    [TestFixture]
    public class AscensionTests
    {
        [Test]
        public void Ascension_PaysMoreEachTime_ExpansionCostsGrowFaster()
        {
            Assert.AreEqual(10, AscensionRules.MaterialFor(0));
            Assert.Greater(AscensionRules.MaterialFor(5), AscensionRules.MaterialFor(4));
            Assert.AreEqual(10, AscensionRules.ExpansionCost(0));
            double payoutGrowth = (double)AscensionRules.MaterialFor(20) / AscensionRules.MaterialFor(10);
            double costGrowth   = (double)AscensionRules.ExpansionCost(20) / AscensionRules.ExpansionCost(10);
            Assert.Greater(costGrowth, payoutGrowth, "stages slow down over the years");
            Assert.AreEqual(0.21f, AscensionRules.ExpansionBonus(2), 1e-4f, "+10% compounding");
        }

        [Test]
        public void Available_OnlyWhenEveryResearchIsUnlocked()
        {
            var ids = new[] { "a", "b", "c" };
            var save = new SaveData { unlockedResearch = new List<string> { "a", "b" } };
            Assert.IsFalse(AscensionRules.IsAvailable(save, ids));
            save.unlockedResearch.Add("c");
            Assert.IsTrue(AscensionRules.IsAvailable(save, ids));
            Assert.IsFalse(AscensionRules.IsAvailable(save, new string[0]), "no research data: never");
        }

        [Test]
        public void Apply_PaysMaterial_LocksWorlds_RestartsStages_SpendsHeldCurrency_KeepsTheRest()
        {
            var save = new SaveData
            {
                unlockedWorlds = new List<string> { WorldGate.Chemistry, WorldGate.Metallurgy },
                worldTaskStage = 5, prestigeCurrency = 900, prestigeCurrencySpent = 600,
                permanentUpgrades = new List<string> { "assembly_line:3" }, megastructureStage = 2,
                megastructureExpansion = 1, megastructureMaterial = 4,
            };
            save.worldTaskSets.Add(new WorldTaskProgressData { worldId = WorldGate.Metallurgy, stage = 4 });

            long paid = AscensionRules.Apply(save);

            Assert.AreEqual(10, paid);
            Assert.AreEqual(14, save.megastructureMaterial);
            Assert.AreEqual(1, save.ascensionCount);
            Assert.IsEmpty(save.unlockedWorlds);
            Assert.AreEqual(0, save.worldTaskStage);
            Assert.IsEmpty(save.worldTaskSets);
            Assert.AreEqual(save.prestigeCurrencySpent, save.prestigeCurrency, "nothing held");
            CollectionAssert.AreEqual(new[] { "assembly_line:3" }, save.permanentUpgrades, "the shop keeps its upgrades");
            Assert.AreEqual(2, save.megastructureStage);
            Assert.AreEqual(1, save.megastructureExpansion);
            Assert.AreEqual(12, AscensionRules.MaterialFor(save.ascensionCount), "the next one pays more (10 x 1.18)");
        }

        [Test]
        public void BuyExpansion_SpendsMaterial_OnlyWhenEnough()
        {
            var save = new SaveData { megastructureMaterial = 9 };
            Assert.IsFalse(AscensionRules.BuyExpansion(save));
            save.megastructureMaterial = 25;
            Assert.IsTrue(AscensionRules.BuyExpansion(save));
            Assert.AreEqual(15, save.megastructureMaterial);
            Assert.AreEqual(1, save.megastructureExpansion);
            Assert.IsFalse(AscensionRules.BuyExpansion(save), "level 2 costs 16");
        }

        [Test]
        public void EngineeringWorld_UnlocksFromMetallurgyDeliveries_AndBuildsTheDysonNode()
        {
            var tasks = Resources.LoadAll<WorldTaskSO>("WorldTaskSets").SingleOrDefault(t => t.worldId == WorldGate.Engineering);
            Assert.IsNotNull(tasks, "run Import Game Data");
            Assert.AreEqual(WorldGate.Metallurgy, tasks.deliverIn);
            var world = Resources.Load<WorldDatabaseSO>("WorldDatabase").allWorlds.Single(w => w != null && w.id == WorldGate.Engineering);
            Assert.IsTrue(world.unlockByTasks);
            Assert.IsNotNull(world.tutorialFlow);
            var fab = Resources.Load<BuildingDatabaseSO>("BuildingDatabase").allBuildings.Single(b => b != null && b.id == "component_fabricator");
            Assert.AreEqual(WorldGate.Engineering, fab.world);
            Assert.IsFalse(fab.requiresPower);
            CollectionAssert.Contains(fab.supportedRecipes.Select(r => r.outputItem.id).ToList(), "dyson_node");
            var mega = Resources.Load<MegastructureSO>("Megastructure");
            Assert.AreEqual("dyson_engineering", mega?.requiredResearch, "the Dyson Sphere opens with Dyson Engineering");
        }
    }
}
