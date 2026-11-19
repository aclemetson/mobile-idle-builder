using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// World delivery tasks (Chemistry gate, 2026-11-19): items reaching the Demon count toward the current
    /// stage, stages survive prestige, and finishing them opens the world.
    /// </summary>
    [TestFixture]
    public class WorldTaskTests
    {
        readonly List<Object> _made = new();
        WorldTaskSO _tasks;
        ItemSO _carbon, _lithium, _boron, _iron;

        [SetUp]
        public void SetUp()
        {
            _carbon  = Item("carbon", 6);
            _lithium = Item("lithium", 3);
            _boron   = Item("boron", 5);
            _iron    = Item("iron", 26);
            _tasks = Make<WorldTaskSO>();
            _tasks.worldId = "world_chemistry";
            _tasks.stages = new[]
            {
                new WorldTaskStage { id = "s1", items = new[] { _carbon }, quantities = new[] { 100 } },
                new WorldTaskStage { id = "s2", items = new[] { _lithium, _boron }, quantities = new[] { 50, 20 } },
            };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) Object.DestroyImmediate(o);
            _made.Clear();
        }

        T Make<T>() where T : ScriptableObject { var o = ScriptableObject.CreateInstance<T>(); _made.Add(o); return o; }
        ItemSO Item(string id, int itemId) { var i = Make<ItemSO>(); i.id = id; i.itemId = itemId; return i; }

        [Test]
        public void Credit_CountsOnlyWhatTheStageNeeds()
        {
            var save = new SaveData();
            Assert.AreEqual(0, WorldTasks.Credit(save, _tasks, _iron.itemId, 500, out _), "iron isn't asked for");
            Assert.AreEqual(40, WorldTasks.Credit(save, _tasks, _carbon.itemId, 40, out var done));
            Assert.IsNull(done);
            Assert.AreEqual(40, WorldTasks.Delivered(save, _carbon.itemId));
            Assert.AreEqual(0.4f, WorldTasks.StageFraction(save, _tasks.stages[0]), 1e-4f);
        }

        [Test]
        public void Credit_CapsAtTheStage_AndAdvances()
        {
            var save = new SaveData();
            Assert.AreEqual(100, WorldTasks.Credit(save, _tasks, _carbon.itemId, 250, out var done));
            Assert.AreSame(_tasks.stages[0], done);
            Assert.AreEqual(1, save.worldTaskStage);
            Assert.AreEqual(0, WorldTasks.Delivered(save, _carbon.itemId), "the next stage starts from zero");
            Assert.AreSame(_tasks.stages[1], WorldTasks.CurrentStage(save, _tasks));
        }

        [Test]
        public void MultiItemStage_NeedsEveryItem()
        {
            var save = new SaveData { worldTaskStage = 1 };
            WorldTasks.Credit(save, _tasks, _lithium.itemId, 999, out var done);
            Assert.IsNull(done, "boron still missing");
            Assert.AreEqual(50f / 70f, WorldTasks.StageFraction(save, _tasks.stages[1]), 1e-4f);
            WorldTasks.Credit(save, _tasks, _boron.itemId, 20, out done);
            Assert.AreSame(_tasks.stages[1], done);
            Assert.IsTrue(WorldTasks.IsComplete(save, _tasks));
            Assert.IsNull(WorldTasks.CurrentStage(save, _tasks));
            Assert.AreEqual(0, WorldTasks.Credit(save, _tasks, _boron.itemId, 5, out _), "nothing counts once complete");
        }

        [Test]
        public void CreditAll_ReportsEveryFinishedStage()
        {
            var save = new SaveData();
            var finished = WorldTasks.CreditAll(save, _tasks, new Dictionary<int, long>
            {
                { _carbon.itemId, 100 }, { _lithium.itemId, 50 }, { _boron.itemId, 20 },
            });
            CollectionAssert.AreEqual(new[] { "s1", "s2" }, finished.Select(s => s.id).ToArray());
        }

        [Test]
        public void Progress_SurvivesAPrestigeReset()
        {
            var save = new SaveData { worldTaskStage = 1 };
            WorldTasks.Credit(save, _tasks, _lithium.itemId, 30, out _);
            save.currentRun = new CurrentRunData();      // prestige rebuilds the run, not the persistent part
            Assert.AreEqual(1, save.worldTaskStage);
            Assert.AreEqual(30, WorldTasks.Delivered(save, _lithium.itemId));
        }

        [Test]
        public void Migrate_StartsFromPrestigeCount_ShortOfTheLastStage()
        {
            var save = new SaveData { prestigeCount = 1 };
            WorldTasks.Migrate(save, _tasks, isNewGame: false);
            Assert.AreEqual(1, save.worldTaskStage);

            var veteran = new SaveData { prestigeCount = 9 };
            WorldTasks.Migrate(veteran, _tasks, isNewGame: false);
            Assert.AreEqual(1, veteran.worldTaskStage, "the last stage still has to be delivered");
        }

        [Test]
        public void Migrate_UnlockedWorld_IsComplete_AndRunsOnce()
        {
            var save = new SaveData { unlockedWorlds = new List<string> { "world_chemistry" } };
            WorldTasks.Migrate(save, _tasks, isNewGame: false);
            Assert.IsTrue(WorldTasks.IsComplete(save, _tasks));

            var fresh = new SaveData { prestigeCount = 3 };
            WorldTasks.Migrate(fresh, _tasks, isNewGame: true);
            Assert.AreEqual(0, fresh.worldTaskStage, "new games start at stage 0");
            fresh.prestigeCount = 5;
            WorldTasks.Migrate(fresh, _tasks, isNewGame: false);
            Assert.AreEqual(0, fresh.worldTaskStage, "migration runs once");
        }

        [Test]
        public void UnlockByTasks_WorldNeedsEveryStage()
        {
            var world = Make<WorldSO>();
            world.id = "world_chemistry";
            world.unlockByTasks = true;
            var save = new SaveData { worldTaskStage = 1 };
            Assert.IsFalse(WorldService.PrereqsMet(save, world, _tasks));
            save.worldTaskStage = 2;
            Assert.IsTrue(WorldService.PrereqsMet(save, world, _tasks));
            Assert.IsFalse(WorldService.PrereqsMet(save, world, null), "no task data means locked");
        }

        [Test]
        public void SaveData_RoundTripsTaskFields_AndOldSavesReadZero()
        {
            var save = new SaveData { worldTaskStage = 2, worldTaskVersion = 1 };
            WorldTasks.Credit(save, _tasks, _carbon.itemId, 0, out _);
            save.worldTaskProgressKeys.Add("6"); save.worldTaskProgressValues.Add(12);
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            Assert.AreEqual(2, back.worldTaskStage);
            Assert.AreEqual(12, WorldTasks.Delivered(back, 6));
            var old = JsonUtility.FromJson<SaveData>("{\"prestigeCount\":2}");
            Assert.AreEqual(0, old.worldTaskStage);
            Assert.AreEqual(0, old.worldTaskVersion);
        }

        [Test]
        public void TaskStageTitle_NamesTheStageBeingWorkedOn()
        {
            Assert.AreEqual("Stage 1/5: Ignition Ash", WorldsSubController.TaskStageTitle(0, 5, "Ignition Ash"));
            Assert.AreEqual("Stage 5/5: Silicon", WorldsSubController.TaskStageTitle(4, 5, "Silicon"));
            Assert.AreEqual("Stage 5/5: Silicon", WorldsSubController.TaskStageTitle(5, 5, "Silicon"), "never past the last stage");
        }

        [Test]
        public void GeneratedData_ChemistryTasks_FiveStages_LightElementsOnly()
        {
            var data = Resources.Load<WorldTaskSO>("WorldTasks");
            Assert.IsNotNull(data, "run Import Game Data");
            Assert.AreEqual("world_chemistry", data.worldId);
            Assert.AreEqual(5, data.stages.Length);
            foreach (var stage in data.stages)
                for (int i = 0; i < stage.items.Length; i++)
                {
                    Assert.IsNotNull(stage.items[i], stage.id);
                    Assert.AreEqual(ItemCategory.Element, stage.items[i].category, $"{stage.id}: {stage.items[i].id}");
                    Assert.Greater(stage.quantities[i], 0, stage.id);
                }
        }
    }
}
