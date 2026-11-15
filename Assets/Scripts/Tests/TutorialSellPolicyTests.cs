using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Selling quarks on a tutorial craft step (collection paused) left the player unable to craft or
    /// collect. Maxwell's Demon now only takes what the current step asks for during the hand-crafting
    /// phase (TutorialSellPolicy), and a save already stuck gets its fields back (TutorialCraftRescue).
    /// </summary>
    [TestFixture]
    public class TutorialSellPolicyTests
    {
        const int Up = 1, Down = 2, Electron = 3, Proton = 4, Neutron = 5, Hydrogen = 6;

        static readonly Dictionary<int, string> Keys = new()
        {
            { Up, "up_quark" }, { Down, "down_quark" }, { Electron, "electron" },
            { Proton, "proton" }, { Neutron, "neutron" }, { Hydrogen, "hydrogen" },
        };

        static readonly Dictionary<string, string> Fields = new()
        {
            { "up_quark", "Quark" }, { "down_quark", "Quark" }, { "electron", "Lepton" },
        };

        static readonly Dictionary<string, RecipeJson> Recipes = new()
        {
            { "proton",   Recipe("proton",   ("up_quark", 2), ("down_quark", 1)) },
            { "neutron",  Recipe("neutron",  ("up_quark", 1), ("down_quark", 2)) },
            { "hydrogen", Recipe("hydrogen", ("proton", 1),   ("electron", 1)) },
        };

        TutorialFlowSO _flow;

        [TearDown]
        public void TearDown()
        {
            if (_flow != null) UnityEngine.Object.DestroyImmediate(_flow);
        }

        // ── Sell policy ───────────────────────────────────────────────────

        TutorialFlowSO SyntheticFlow()
        {
            _flow = ScriptableObject.CreateInstance<TutorialFlowSO>();
            _flow.steps = new[]
            {
                Step("collect", ConditionType.InventoryMin, Up, Down),     // 0
                Step("sell_quarks", ConditionType.InventoryZero, Up, Down),// 1
                Step("craft", ConditionType.InventoryMin, Proton),         // 2
                Step("sell_nucleons", ConditionType.InventoryZero, Proton),// 3
                Step("after", ConditionType.UiEvent),                      // 4
            };
            return _flow;
        }

        [Test]
        public void SellStep_TakesOnlyItsItems()
        {
            var flow = SyntheticFlow();
            Assert.IsTrue(TutorialSellPolicy.CanSell(flow, true, 1, Up));
            Assert.IsTrue(TutorialSellPolicy.CanSell(flow, true, 1, Down));
            Assert.IsFalse(TutorialSellPolicy.CanSell(flow, true, 1, Electron));
            Assert.IsTrue(TutorialSellPolicy.CanSell(flow, true, 3, Proton));
            Assert.IsFalse(TutorialSellPolicy.CanSell(flow, true, 3, Up), "leftover quarks wait");
        }

        [Test]
        public void OtherStepsInThePhase_TakeNothing()
        {
            var flow = SyntheticFlow();
            foreach (int step in new[] { 0, 2 })
                foreach (int item in Keys.Keys)
                    Assert.IsFalse(TutorialSellPolicy.CanSell(flow, true, step, item), $"step {step} item {item}");
        }

        [Test]
        public void AfterTheLastSellStep_OrOutsideTheTutorial_EverythingSells()
        {
            var flow = SyntheticFlow();
            Assert.AreEqual(3, TutorialSellPolicy.LastSellStep(flow));
            Assert.IsTrue(TutorialSellPolicy.CanSell(flow, true, 4, Up));
            Assert.IsTrue(TutorialSellPolicy.CanSell(flow, false, 2, Up), "tutorial inactive");
            Assert.IsTrue(TutorialSellPolicy.CanSell(null, true, 2, Up), "no flow");
            Assert.IsTrue(TutorialSellPolicy.CanSell(flow, true, 99, Up), "past the end");
        }

        [Test]
        public void RealFlow_CraftStepsKeepQuarks_SellStepsTakeTheirItems()
        {
            var flow = UnityEditor.AssetDatabase.LoadAssetAtPath<TutorialFlowSO>("Assets/Data/tutorial/tutorial_flow.asset");
            Assert.IsNotNull(flow, "tutorial_flow.asset");
            int Index(string id) => Array.FindIndex(flow.steps, s => s.id == id);

            foreach (var id in new[] { "craft_two_protons", "craft_two_neutrons", "craft_protons_for_hydrogen", "craft_hydrogen" })
            {
                int i = Index(id);
                Assert.GreaterOrEqual(i, 0, id);
                Assert.IsFalse(TutorialSellPolicy.CanSell(flow, true, i, Up), $"{id}: up quark");
                Assert.IsFalse(TutorialSellPolicy.CanSell(flow, true, i, Down), $"{id}: down quark");
            }

            int nucleons = Index("sell_protons_and_neutrons");
            Assert.IsTrue(TutorialSellPolicy.CanSell(flow, true, nucleons, Proton));
            Assert.IsTrue(TutorialSellPolicy.CanSell(flow, true, nucleons, Neutron));
            Assert.IsFalse(TutorialSellPolicy.CanSell(flow, true, nucleons, Up));

            int automation = Index("place_first_building");
            Assert.Greater(automation, TutorialSellPolicy.LastSellStep(flow));
            Assert.IsTrue(TutorialSellPolicy.CanSell(flow, true, automation, Up));
        }

        // ── Craft rescue ──────────────────────────────────────────────────

        static string Rescue(TutorialStepDef step, Dictionary<string, int> have) =>
            TutorialCraftRescue.FieldsNeeded(step,
                id => Keys.TryGetValue(id, out var k) ? k : null,
                id => have.TryGetValue(id, out int n) ? n : 0,
                id => Recipes.TryGetValue(id, out var r) ? r : null,
                id => Fields.TryGetValue(id, out var f) ? f : FieldTypes.None);

        static TutorialStepDef CraftStep(int item, int qty)
        {
            var s = Step("craft", ConditionType.InventoryMin);
            s.advanceCondition.items = new List<ItemCountReq> { new() { itemId = item, quantity = qty } };
            s.onEnter.blockCollection = true;
            return s;
        }

        [Test]
        public void Rescue_ReopensQuarks_WhenProtonsCannotBeCrafted()
        {
            Assert.AreEqual("Quark", Rescue(CraftStep(Proton, 2), new()));
            Assert.AreEqual("Quark", Rescue(CraftStep(Proton, 2), new() { { "proton", 1 }, { "up_quark", 1 }, { "down_quark", 1 } }));
        }

        [Test]
        public void Rescue_StaysOff_WhenTheInputsAreThere()
        {
            Assert.IsNull(Rescue(CraftStep(Proton, 2), new() { { "proton", 1 }, { "up_quark", 2 }, { "down_quark", 1 } }));
            Assert.IsNull(Rescue(CraftStep(Proton, 2), new() { { "up_quark", 4 }, { "down_quark", 2 } }));
            // Queued crafts count: the caller adds them to what the player has.
            Assert.IsNull(Rescue(CraftStep(Proton, 2), new() { { "proton", 2 } }));
        }

        [Test]
        public void Rescue_OpensEveryField_WhenSeveralKindsAreShort()
        {
            Assert.AreEqual(FieldTypes.None, Rescue(CraftStep(Hydrogen, 2), new()));
            Assert.AreEqual("Lepton", Rescue(CraftStep(Hydrogen, 2), new() { { "proton", 2 } }));
        }

        [Test]
        public void Rescue_IgnoresStepsThatDoNotPauseCollection()
        {
            var step = CraftStep(Proton, 2);
            step.onEnter.blockCollection = false;
            Assert.IsNull(Rescue(step, new()));
            Assert.IsNull(Rescue(Step("sell", ConditionType.InventoryZero, Up), new()));
        }

        // ── Helpers ───────────────────────────────────────────────────────

        static TutorialStepDef Step(string id, ConditionType type, params int[] items) => new()
        {
            id = id,
            advanceCondition = new TutorialConditionDef
            {
                type  = type,
                items = items.Select(i => new ItemCountReq { itemId = i, quantity = 1 }).ToList(),
            },
            onEnter = new TutorialOnEnter(),
        };

        static RecipeJson Recipe(string output, params (string id, int qty)[] inputs) => new()
        {
            id     = output,
            inputs = inputs.Select(i => new RecipeIngredientJson { id = i.id, quantity = i.qty }).ToList(),
            output = new RecipeOutputJson { id = output, quantity = 1 },
        };
    }
}
