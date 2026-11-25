using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Buying a research out of order on a tutorial step with collection paused spent the entropy the
    /// tutorial's research needed, with no way to earn it back. TutorialResearchPolicy only lets the
    /// tutorial's next research be bought while collection is restricted, and a save already stuck on
    /// that research gets it for what it holds.
    /// </summary>
    [TestFixture]
    public class TutorialResearchPolicyTests
    {
        TutorialFlowSO _flow;
        readonly HashSet<string> _unlocked = new();

        bool IsUnlocked(string id) => _unlocked.Contains(id);

        [SetUp]
        public void SetUp()
        {
            _unlocked.Clear();
            _flow = ScriptableObject.CreateInstance<TutorialFlowSO>();
            _flow.steps = new[]
            {
                Step(ConditionType.UiEvent, block: true),                         // 0 dialogue
                Step(ConditionType.InventoryMin, filter: "Quark"),                // 1 collect quarks
                Step(ConditionType.ResearchUnlocked, block: true, research: "a"), // 2 buy a
                Step(ConditionType.InventoryMin, block: true),                    // 3 craft
                Step(ConditionType.ResearchUnlocked, block: true, research: "b"), // 4 buy b
                Step(ConditionType.BuildingMin),                                  // 5 open play
                Step(ConditionType.ResearchUnlocked, research: "c"),              // 6 buy c (fields open)
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (_flow != null) Object.DestroyImmediate(_flow);
        }

        static TutorialStepDef Step(ConditionType type, bool block = false, string filter = null, string research = null) =>
            new()
            {
                advanceCondition = new TutorialConditionDef { type = type, researchId = research },
                onEnter          = new TutorialOnEnter { blockCollection = block, collectionFilter = filter ?? FieldTypes.None },
            };

        [Test]
        public void BeforeAResearchStep_OnlyThatResearchCanBeBought()
        {
            foreach (int step in new[] { 0, 1, 2 })
            {
                Assert.AreEqual("a", TutorialResearchPolicy.RequiredResearch(_flow, true, step, IsUnlocked), $"step {step}");
                Assert.IsTrue(TutorialResearchPolicy.CanBuy(_flow, true, step, "a", IsUnlocked));
                Assert.IsFalse(TutorialResearchPolicy.CanBuy(_flow, true, step, "other", IsUnlocked));
            }
        }

        [Test]
        public void AnUnlockedResearch_PointsAtTheNextOne()
        {
            _unlocked.Add("a");
            Assert.AreEqual("b", TutorialResearchPolicy.RequiredResearch(_flow, true, 3, IsUnlocked));
            Assert.IsFalse(TutorialResearchPolicy.CanBuy(_flow, true, 3, "c", IsUnlocked));
        }

        [Test]
        public void OnceCollectionIsOpen_AnythingCanBeBought()
        {
            Assert.IsNull(TutorialResearchPolicy.RequiredResearch(_flow, true, 5, IsUnlocked));
            Assert.IsNull(TutorialResearchPolicy.RequiredResearch(_flow, true, 6, IsUnlocked));
            Assert.IsTrue(TutorialResearchPolicy.CanBuy(_flow, true, 6, "other", IsUnlocked));
        }

        [Test]
        public void OutsideTheTutorial_AnythingCanBeBought()
        {
            Assert.IsTrue(TutorialResearchPolicy.CanBuy(_flow, false, 0, "other", IsUnlocked));
            Assert.IsTrue(TutorialResearchPolicy.CanBuy(null, true, 0, "other", IsUnlocked));
        }

        [Test]
        public void Rescue_OnlyOnTheBlockedResearchStep_WhenShort()
        {
            Assert.IsTrue(TutorialResearchPolicy.IsRescue(_flow, true, 2, "a", balance: 10, cost: 50));
            Assert.IsFalse(TutorialResearchPolicy.IsRescue(_flow, true, 2, "a", balance: 50, cost: 50), "affordable");
            Assert.IsFalse(TutorialResearchPolicy.IsRescue(_flow, true, 2, "b", balance: 10, cost: 50), "other research");
            Assert.IsFalse(TutorialResearchPolicy.IsRescue(_flow, true, 1, "a", balance: 10, cost: 50), "fields open here");
            Assert.IsFalse(TutorialResearchPolicy.IsRescue(_flow, true, 6, "c", balance: 10, cost: 50), "collection open");
            Assert.IsFalse(TutorialResearchPolicy.IsRescue(_flow, false, 2, "a", balance: 10, cost: 50), "tutorial off");
        }
    }
}
