using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Taking a cloud save on another device (docs/agents/save-system.md, "Cross-device saves"):
    ///  - the running game is emptied before the scene reloads from the cloud save (RunStateClearer). On
    ///    2026-11-21 an Android save loaded on an iPad came up with both devices' buildings on one map,
    ///    because the default ECS world survives the reload and only prestige cleared it;
    ///  - recipe knowledge travels with the save (RecipeKnowledgeService.SyncWithSave);
    ///  - the startup choice is waited on while the loading screen can show it (ECSLoadBridge).
    /// </summary>
    [TestFixture]
    public class CrossDeviceSwapTests
    {
        World _world;
        EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("CrossDeviceSwapTest");
            _em    = _world.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (_world.IsCreated) _world.Dispose();
        }

        // ── RunStateClearer ──────────────────────────────────────────────────

        [Test]
        public void ClearLiveRunEntities_RemovesThisDevicesBuildingsAndBelts_KeepsAnEmptiedDemon()
        {
            var building = _em.CreateEntity(typeof(BuildingData), typeof(GridPosition));
            var other    = _em.CreateEntity(typeof(BuildingData), typeof(GridPosition));
            var belt     = _em.CreateEntity(typeof(ConveyorSegmentData), typeof(GridPosition));
            ConveyorItems.Add(_em, belt, 3, 0.5f);
            var demon = _em.CreateEntity(typeof(BuildingData), typeof(EntropySinkTag));
            _em.AddBuffer<BuildingInputSlot>(demon).Add(new BuildingInputSlot { ItemID = 3, Quantity = 7 });
            _em.AddBuffer<BuildingOutputSlot>(demon).Add(new BuildingOutputSlot { ItemID = 3, Quantity = 2 });

            int cleared = RunStateClearer.ClearLiveRunEntities(_em);

            Assert.AreEqual(2, cleared, "both run buildings counted (not the Demon)");
            Assert.IsFalse(_em.Exists(building));
            Assert.IsFalse(_em.Exists(other));
            Assert.IsFalse(_em.Exists(belt), "belts must not carry over under the cloud grid");
            using (var items = _em.CreateEntityQuery(typeof(ConveyorItem)))
                Assert.AreEqual(0, items.CalculateEntityCount(), "no belt item survives");
            Assert.IsTrue(_em.Exists(demon), "the Demon is a baked fixture and stays");
            Assert.AreEqual(0, _em.GetBuffer<BuildingInputSlot>(demon).Length);
            Assert.AreEqual(0, _em.GetBuffer<BuildingOutputSlot>(demon).Length);
        }

        [Test]
        public void ClearLiveRunEntities_EmptyWorld_IsANoOp()
        {
            Assert.AreEqual(0, RunStateClearer.ClearLiveRunEntities(_em));
        }

        // ── Recipe knowledge roams with the save ─────────────────────────────

        static RecipeKnowledgeSave File(params (string id, bool known)[] entries)
        {
            var data = new RecipeKnowledgeSave();
            foreach (var (id, known) in entries)
                data.entries.Add(new RecipeKnowledgeEntry { id = id, previous_research = known });
            return data;
        }

        static bool Known(RecipeKnowledgeSave data, string id) =>
            data.entries.Exists(e => e.id == id && e.previous_research);

        [Test]
        public void OlderSave_IsBackFilledFromTheDeviceFile()
        {
            var data = File(("a", true), ("b", false), ("c", true));
            var save = new SaveData();   // recipeKnowledgeVersion 0: written by an older build

            bool fileChanged = RecipeKnowledgeService.SyncWithSave(data, save);

            Assert.IsFalse(fileChanged);
            CollectionAssert.AreEquivalent(new[] { "a", "c" }, save.unlockedRecipes);
            Assert.AreEqual(1, save.recipeKnowledgeVersion);
        }

        [Test]
        public void SameProfile_NormalLaunch_TakesTheUnion()
        {
            // The file learned "b" but the app died before save.json was written; the save knows "d"
            // that the file lost. Neither may be dropped.
            var data = File(("a", true), ("b", true), ("c", false));
            var save = new SaveData { unlockedRecipes = new List<string> { "a", "d" }, recipeKnowledgeVersion = 1 };

            bool fileChanged = RecipeKnowledgeService.SyncWithSave(data, save);

            Assert.IsTrue(fileChanged, "d was added to the file");
            Assert.IsTrue(Known(data, "a") && Known(data, "b") && Known(data, "d"));
            Assert.IsFalse(Known(data, "c"));
            CollectionAssert.AreEquivalent(new[] { "a", "b", "d" }, save.unlockedRecipes);
        }

        [Test]
        public void CloudSaveTaken_ItsKnowledgeReplacesTheDevicesOwn()
        {
            var data  = File(("a", true), ("b", true), ("c", false));
            var cloud = new SaveData { unlockedRecipes = new List<string> { "a", "c" }, recipeKnowledgeVersion = 1 };

            bool fileChanged = RecipeKnowledgeService.SyncWithSave(data, cloud, saveWins: true);

            Assert.IsTrue(fileChanged);
            Assert.IsTrue(Known(data, "a"));
            Assert.IsFalse(Known(data, "b"), "the device's own knowledge does not leak into the adopted profile");
            Assert.IsTrue(Known(data, "c"));
            CollectionAssert.AreEquivalent(new[] { "a", "c" }, cloud.unlockedRecipes);
        }

        [Test]
        public void LegacySaveJson_WithoutTheVersion_LoadsAsNeverWritten()
        {
            var save = UnityEngine.JsonUtility.FromJson<SaveData>("{\"prestigeCount\":3,\"unlockedRecipes\":[]}");
            Assert.AreEqual(0, save.recipeKnowledgeVersion);
            Assert.AreEqual(3, save.prestigeCount);
        }

        [Test]
        public void RecipeKnowledge_RoundTripsThroughTheSaveJson()
        {
            var save = new SaveData { unlockedRecipes = new List<string> { "a", "b" }, recipeKnowledgeVersion = 1 };
            var back = UnityEngine.JsonUtility.FromJson<SaveData>(UnityEngine.JsonUtility.ToJson(save));
            CollectionAssert.AreEqual(new[] { "a", "b" }, back.unlockedRecipes);
            Assert.AreEqual(1, back.recipeKnowledgeVersion);
        }

        // ── Startup choice waits on the loading screen ───────────────────────

        [Test]
        public void ChoiceOnTheLoadingScreen_IsWaitedOn_PastTheCloudTimeout()
        {
            Assert.IsTrue(ECSLoadBridge.ShouldKeepWaitingForCloud(true, true, elapsed: 120f, timeout: 5f));
        }

        [Test]
        public void ChoiceThatNeedsTheHud_IsNotWaitedOn()
        {
            Assert.IsFalse(ECSLoadBridge.ShouldKeepWaitingForCloud(true, false, elapsed: 0f, timeout: 5f));
        }

        [Test]
        public void NoChoice_WaitsUpToTheCloudTimeout()
        {
            Assert.IsTrue(ECSLoadBridge.ShouldKeepWaitingForCloud(false, true, elapsed: 4f, timeout: 5f));
            Assert.IsFalse(ECSLoadBridge.ShouldKeepWaitingForCloud(false, true, elapsed: 5f, timeout: 5f));
        }

        // ── Log summary ──────────────────────────────────────────────────────

        [Test]
        public void GridSummary_CountsEverySite()
        {
            var save = new SaveData();
            var g0 = new GridSaveData();
            g0.buildings.Add(new BuildingSaveData());
            g0.conveyors.Add(new ConveyorSaveData());
            g0.conveyors.Add(new ConveyorSaveData());
            var g1 = new GridSaveData();
            g1.buildings.Add(new BuildingSaveData());
            save.currentRun.grids = new List<GridSaveData> { g0, g1 };

            Assert.AreEqual("sites 2, buildings 2, belts 2", SaveManager.GridSummary(save));
            Assert.AreEqual("none", SaveManager.GridSummary(null));
        }
    }
}
