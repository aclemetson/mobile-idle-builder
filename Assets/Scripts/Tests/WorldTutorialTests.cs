using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Per-world tutorials (2026-11-22): Chemistry runs its own short tutorial (world_tutorials ->
    /// WorldSO.tutorialFlow) and the Physics tutorial never runs there. <see cref="TutorialFlows"/> keeps
    /// each flow's progress in its own save slot.
    /// </summary>
    [TestFixture]
    public class WorldTutorialTests
    {
        TutorialFlowSO _main, _chem;
        TutorialFlowSO _savedCurrent, _savedDefault;

        static TutorialFlowSO Flow(string worldId, params string[] ids)
        {
            var f = ScriptableObject.CreateInstance<TutorialFlowSO>();
            f.worldId = worldId;
            f.steps = ids.Select(id => new TutorialStepDef { id = id }).ToArray();
            return f;
        }

        [SetUp]
        public void SetUp()
        {
            _savedCurrent = TutorialFlowSO.Current;
            _savedDefault = TutorialFlowSO.Default;
            _main = Flow("", "a", "b", "c");
            _chem = Flow("world_chemistry", "w1", "w2", "w3");
            TutorialFlowSO.SetDefaultForTesting(_main);
            TutorialFlowSO.SetCurrentForTesting(_main);
        }

        [TearDown]
        public void TearDown()
        {
            TutorialFlowSO.SetDefaultForTesting(_savedDefault);
            TutorialFlowSO.SetCurrentForTesting(_savedCurrent);
            Object.DestroyImmediate(_main);
            Object.DestroyImmediate(_chem);
        }

        [Test]
        public void FirstVisit_StartsTheWorldTutorialAtTheBeginning()
        {
            var save = new SaveData();
            save.tutorial.hasCompletedFirstRun = true;   // Physics tutorial long done
            var ts = new TutorialStateData { IsActive = false, CurrentStepIndex = 99, FirstRunComplete = true };

            TutorialFlows.Load(ref ts, save, _chem);

            Assert.IsTrue(ts.IsActive);
            Assert.AreEqual(0, ts.CurrentStepIndex);
            Assert.IsTrue(ts.FirstRunComplete, "the Physics milestone is untouched");
        }

        [Test]
        public void WorldProgress_IsStoredInItsOwnSlot_NotThePhysicsOne()
        {
            var save = new SaveData();
            save.tutorial.currentStepId = "b";
            TutorialFlowSO.SetCurrentForTesting(_chem);

            bool stored = TutorialFlows.Store(save, new TutorialStateData { IsActive = true, CurrentStepIndex = 1 });

            Assert.IsTrue(stored);
            Assert.AreEqual("b", save.tutorial.currentStepId, "Physics progress is not overwritten");
            var slot = TutorialFlows.Slot(save, "world_chemistry", create: false);
            Assert.AreEqual("w2", slot.currentStepId);
            Assert.IsFalse(slot.done);
        }

        [Test]
        public void MainTutorialRunning_StoreLeavesItToTheCaller()
        {
            var save = new SaveData();
            Assert.IsFalse(TutorialFlows.Store(save, new TutorialStateData { IsActive = true }));
            Assert.IsEmpty(save.worldTutorials);
        }

        [Test]
        public void FinishedWorldTutorial_StaysFinished()
        {
            var save = new SaveData();
            TutorialFlowSO.SetCurrentForTesting(_chem);
            TutorialFlows.Store(save, new TutorialStateData { IsActive = false, CurrentStepIndex = 3 });

            var ts = new TutorialStateData();
            TutorialFlows.Load(ref ts, save, _chem);

            Assert.IsFalse(ts.IsActive);
            Assert.AreEqual(3, ts.CurrentStepIndex);
        }

        [Test]
        public void ResetWorld_StartsThatWorldsTutorialOver_OnlyForThatWorld()
        {
            var save = new SaveData();
            TutorialFlows.Slot(save, "world_chemistry", create: true).done = true;
            TutorialFlows.Slot(save, "world_biology", create: true).currentStepId = "x";

            TutorialFlows.ResetWorld(save, "world_chemistry");

            var ts = new TutorialStateData();
            TutorialFlows.Load(ref ts, save, _chem);
            Assert.IsTrue(ts.IsActive);
            Assert.AreEqual(0, ts.CurrentStepIndex);
            Assert.IsNotNull(TutorialFlows.Slot(save, "world_biology", create: false), "other worlds untouched");
        }

        [Test]
        public void ReturningToPhysics_RestoresThePhysicsState()
        {
            var save = new SaveData();
            save.tutorial.isActive = true;
            save.tutorial.currentStepId = "c";
            var ts = new TutorialStateData { IsActive = true, CurrentStepIndex = 1 };   // was mid-Chemistry

            TutorialFlows.Load(ref ts, save, _main);

            Assert.IsTrue(ts.IsActive);
            Assert.AreEqual(2, ts.CurrentStepIndex);
        }

        [Test]
        public void WorldTaskStrip_ShowsOnlyWhereTheTasksAreDelivered()
        {
            var tasks = ScriptableObject.CreateInstance<WorldTaskSO>();
            tasks.worldId = WorldGate.Chemistry;
            tasks.deliverIn = WorldGate.Physics;
            Assert.IsTrue(WorldTasks.ShownIn(tasks, WorldGate.Physics));
            Assert.IsTrue(WorldTasks.ShownIn(tasks, null), "null = Physics");
            Assert.IsFalse(WorldTasks.ShownIn(tasks, WorldGate.Chemistry), "Physics' deliveries are not Chemistry's tasks");
            Object.DestroyImmediate(tasks);
            Assert.AreEqual(WorldGate.Physics, Resources.Load<WorldTaskSO>("WorldTasks").deliverIn);
        }

        [Test]
        public void ChemistryWorld_HasItsOwnTutorial_AndPhysicsDoesNot()
        {
            var worlds = Resources.Load<WorldDatabaseSO>("WorldDatabase")?.allWorlds;
            Assert.IsNotNull(worlds, "WorldDatabase missing: run Import Game Data");
            var chem = worlds.Single(w => w != null && w.id == WorldGate.Chemistry);
            var phys = worlds.Single(w => w != null && w.id == WorldGate.Physics);

            Assert.IsNotNull(chem.tutorialFlow, "Chemistry has its own tutorial");
            Assert.AreEqual(WorldGate.Chemistry, chem.tutorialFlow.worldId);
            Assert.IsNotEmpty(chem.tutorialFlow.steps);
            Assert.IsNotEmpty(chem.tutorialFlow.completionMessage);
            Assert.IsNull(phys.tutorialFlow, "Physics runs the main tutorial");
            foreach (var s in chem.tutorialFlow.steps)
                if (s.onEnter?.dialogue == null)
                    Assert.AreNotEqual(ConditionType.UiEvent, s.advanceCondition.type == ConditionType.UiEvent
                        && s.advanceCondition.uiEventId == "dialogue_complete" ? ConditionType.UiEvent : ConditionType.Auto,
                        $"{s.id} waits for a dialogue it never plays");
        }
    }
}
