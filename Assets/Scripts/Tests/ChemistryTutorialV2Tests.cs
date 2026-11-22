using System.Collections.Generic;
using System.Linq;
using MobileIdleBuilder.Dev;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Chemistry tutorial v2 (2026-11-22, scripts/chemistry_tutorial_v2.py): fixed layout around a
    /// Calorimeter the site starts with, player-placed gas vents, an entropy grant, tile highlights
    /// (cells:), world-specific items, and the dev 'visit world' / 'tutorial restart' builder.
    /// </summary>
    [TestFixture]
    public class ChemistryTutorialV2Tests
    {
        // ── cells: highlight target ───────────────────────────────────────────

        [Test]
        public void Cells_OneTile_IsAPlacementSuggestion()
        {
            Assert.IsTrue(TutorialRouteGuide.TryParseCells("cells:6,9", out var a, out var b));
            Assert.AreEqual(new Vector2Int(6, 9), a);
            Assert.IsNull(b);
            Assert.IsTrue(TutorialRouteGuide.IsPlacementSuggestion("cells:6,9", _ => false));
        }

        [Test]
        public void Cells_TwoTiles_AreABeltsEnds()
        {
            Assert.IsTrue(TutorialRouteGuide.TryParseCells("cells:5,10>8,10", out var a, out var b));
            Assert.AreEqual(new Vector2Int(5, 10), a);
            Assert.AreEqual(new Vector2Int(8, 10), b);
            Assert.IsFalse(TutorialRouteGuide.IsPlacementSuggestion("cells:5,10>8,10", _ => false));
        }

        [TestCase("cells:")] [TestCase("cells:6")] [TestCase("cells:a,b")] [TestCase("route:a>b")]
        public void Cells_RejectsOtherText(string id)
        {
            Assert.IsFalse(TutorialRouteGuide.TryParseCells(id, out _, out _));
        }

        // ── World items ───────────────────────────────────────────────────────

        [Test]
        public void ItemInWorld_SharedEverywhere_OwnOnlyAtHome()
        {
            var shared = ScriptableObject.CreateInstance<ItemSO>();
            var water  = ScriptableObject.CreateInstance<ItemSO>();
            water.world = WorldGate.Chemistry;
            var quark  = ScriptableObject.CreateInstance<ItemSO>();
            quark.world = WorldGate.Physics;

            Assert.IsTrue(WorldGate.ItemInWorld(shared, WorldGate.Chemistry));
            Assert.IsTrue(WorldGate.ItemInWorld(water, WorldGate.Chemistry));
            Assert.IsFalse(WorldGate.ItemInWorld(water, null), "null = Physics");
            Assert.IsTrue(WorldGate.ItemInWorld(quark, null));
            Assert.IsFalse(WorldGate.ItemInWorld(quark, WorldGate.Chemistry));

            Object.DestroyImmediate(shared); Object.DestroyImmediate(water); Object.DestroyImmediate(quark);
        }

        [Test]
        public void GeneratedItems_CarryTheirWorld()
        {
            var items = Resources.LoadAll<ItemSO>("Items");
            Assert.IsNotEmpty(items, "Items missing: run Import Game Data");
            string W(string id) => items.Single(i => i.id == id).world;
            Assert.AreEqual(WorldGate.Chemistry, W("water"));
            Assert.AreEqual(WorldGate.Chemistry, W("hydrogen_gas"));
            Assert.AreEqual(WorldGate.Physics, W("up_quark"));
            Assert.AreEqual(WorldGate.Physics, W("hydrogen"));
        }

        // ── Content ───────────────────────────────────────────────────────────

        [Test]
        public void ChemLab_StartsWithACalorimeterInTheCentre_AndNoGasVents()
        {
            var site = Resources.Load<SiteDatabaseSO>("SiteDatabase").allSites.Single(s => s != null && s.id == "site_chem_lab");
            var cal  = site.startingBuildings.Single();
            Assert.AreEqual("calorimeter", cal.buildingId);
            Assert.AreEqual((9, 9), (cal.x, cal.y));
            foreach (var ov in site.fieldOverrides.Where(o => o.field != null &&
                         (o.field.id == "hydrogen_gas_field" || o.field.id == "oxygen_gas_field")))
            {
                Assert.AreEqual(0f, ov.densityMultiplier, $"{ov.field.id} is placed by the player");
                Assert.Greater(ov.field.purchaseCostBase, 0, $"{ov.field.id} is bought from the Build menu");
            }
        }

        [Test]
        public void Tutorial_GrantCoversTheChemistryLab()
        {
            var chem = Resources.Load<WorldDatabaseSO>("WorldDatabase").allWorlds.Single(w => w != null && w.id == WorldGate.Chemistry);
            var steps = chem.tutorialFlow.steps;
            long grant = steps.Sum(s => s.rewardEntropy);
            var lab = Resources.Load<ResearchDatabaseSO>("ResearchDatabase").allResearch.Single(r => r != null && r.id == "chemistry_lab");
            Assert.GreaterOrEqual(grant, lab.costBaseCurrency + 19500, "grant covers the lab plus the first stream's buildings");

            int grantAt = System.Array.FindIndex(steps, s => s.rewardEntropy > 0);
            int labAt   = System.Array.FindIndex(steps, s => s.id == "chem_research_lab");
            Assert.Less(grantAt, labAt, "the entropy arrives before the research step");
            Assert.AreNotEqual(ConditionType.UiEvent, steps[grantAt].advanceCondition.type, "rewards are paid on state-driven steps only");
        }

        [Test]
        public void Tutorial_PointsAtTheBuilderCards()
        {
            var steps = Resources.Load<WorldDatabaseSO>("WorldDatabase").allWorlds
                .Single(w => w != null && w.id == WorldGate.Chemistry).tutorialFlow.steps;
            var pulses = steps.Select(s => s.onEnter?.pulseButtonId).Where(p => !string.IsNullOrEmpty(p)).ToList();
            foreach (var id in new[] { "btn-place-spring", "btn-place-conveyor", "btn-place-hydrogen_gas_field",
                                       "btn-place-oxygen_gas_field", "btn-place-element_harvester",
                                       "btn-place-molecular_synthesizer" })
                CollectionAssert.Contains(pulses, id);
        }

        // ── Fresh-arrival builder (visit world / tutorial restart) ────────────

        static WorldSO World(string id, params string[] sites)
        {
            var w = ScriptableObject.CreateInstance<WorldSO>();
            w.id = id;
            w.siteIds = sites.ToList();
            return w;
        }

        [Test]
        public void FreshArrival_InAnotherWorld_ClearsOnlyThatWorld()
        {
            var save = new SaveData();
            var run  = save.currentRun;
            GridSaveService.EnsureSiteGrid(save, 2);
            run.grids[0].buildings.Add(new BuildingSaveData { buildingId = 1 });
            run.grids[2].buildings.Add(new BuildingSaveData { buildingId = 8 });
            run.grids[2].startersPlaced = true;
            run.fieldPurchaseIds    = new List<string> { "quark_field", "hydrogen_gas_field" };
            run.fieldPurchaseCounts = new List<int> { 2, 1 };
            save.unlockedResearch = new List<string> { "recombination_i", "chemistry_lab" };
            TutorialFlows.Slot(save, "world_chemistry", create: true).done = true;
            var chem = World("world_chemistry", "site_chem");

            WorldVisit.PrepareFreshArrival(save, chem, 1, new[] { "site_origin", "x", "site_chem" },
                r => r == "chemistry_lab" ? "world_chemistry" : "",
                f => f == "hydrogen_gas_field" ? "world_chemistry" : "world_physics", 250);

            Assert.AreEqual(2, run.activeSiteIndex);
            Assert.AreEqual(1, run.activeWorldIndex);
            Assert.IsEmpty(run.grids[2].buildings);
            Assert.IsFalse(run.grids[2].startersPlaced, "the Calorimeter comes back");
            Assert.AreEqual(1, run.grids[0].buildings.Count, "Physics is untouched");
            CollectionAssert.AreEqual(new[] { "quark_field" }, run.fieldPurchaseIds);
            CollectionAssert.AreEqual(new[] { "recombination_i" }, save.unlockedResearch);
            CollectionAssert.Contains(save.unlockedWorlds, "world_chemistry");
            Assert.IsNull(TutorialFlows.Slot(save, "world_chemistry", create: false), "tutorial starts over");
            Object.DestroyImmediate(chem);
        }

        [Test]
        public void FreshArrival_InPhysics_IsAFreshRunWithTheMainTutorial()
        {
            var save = new SaveData();
            save.tutorial.hasCompletedFirstRun = true;
            save.tutorial.isActive = false;
            save.unlockedResearch = new List<string> { "recombination_i" };
            save.prestigeCount = 4;
            var phys = World("world_physics", "site_origin");

            WorldVisit.PrepareFreshArrival(save, phys, 0, new[] { "site_origin" }, _ => "", _ => "", 250);

            Assert.IsTrue(save.tutorial.isActive);
            Assert.IsFalse(save.tutorial.hasCompletedFirstRun);
            Assert.AreEqual("", save.tutorial.currentStepId);
            Assert.IsEmpty(save.unlockedResearch);
            Assert.AreEqual(250, save.currentRun.baseCurrency);
            Assert.AreEqual(4, save.prestigeCount, "prestige progress is kept");
            Object.DestroyImmediate(phys);
        }
    }
}
