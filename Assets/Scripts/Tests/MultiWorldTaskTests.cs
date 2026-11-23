using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Item Timeline phase 3 (2026-11-23): delivery tasks for more than one world. The Chemistry gate keeps its
    /// original save fields; every other world's tasks (Metallurgy, delivered in Biology) live in
    /// <see cref="SaveData.worldTaskSets"/>. The set in focus is the unfinished one delivered in the live world.
    /// </summary>
    [TestFixture]
    public class MultiWorldTaskTests
    {
        WorldTaskSO _chem, _metal;
        ItemSO _carbon, _protein;
        WorldTaskSO _savedData;
        WorldTaskSO[] _savedAll;

        static ItemSO Item(string id, int num)
        {
            var i = ScriptableObject.CreateInstance<ItemSO>();
            i.id = id; i.itemId = num;
            return i;
        }

        static WorldTaskSO Tasks(string world, string deliverIn, ItemSO item, int qty, int stages)
        {
            var t = ScriptableObject.CreateInstance<WorldTaskSO>();
            t.worldId = world; t.deliverIn = deliverIn;
            t.stages = Enumerable.Range(0, stages).Select(i => new WorldTaskStage
                { id = $"{world}_{i}", displayName = $"S{i}", items = new[] { item }, quantities = new[] { qty } }).ToArray();
            return t;
        }

        [SetUp]
        public void SetUp()
        {
            _savedData = WorldTasks.Data;
            _savedAll  = WorldTasks.All;
            _carbon  = Item("carbon", 9001);
            _protein = Item("protein", 9002);
            _chem  = Tasks(WorldGate.Chemistry, WorldGate.Physics, _carbon, 10, 2);
            _metal = Tasks(WorldGate.Metallurgy, WorldGate.Biology, _protein, 5, 2);
            WorldTasks.Data = _chem;
            WorldTasks.All  = new[] { _chem, _metal };
        }

        [TearDown]
        public void TearDown()
        {
            WorldTasks.Data = _savedData;
            WorldTasks.All  = _savedAll;
            foreach (var o in new Object[] { _chem, _metal, _carbon, _protein }) Object.DestroyImmediate(o);
        }

        [Test]
        public void EachWorldsTasks_KeepTheirOwnProgress()
        {
            var save = new SaveData();
            WorldTasks.Credit(save, _metal, _protein.itemId, 5, out var done);
            Assert.IsNotNull(done, "Metallurgy's first stage finished");
            Assert.AreEqual(1, WorldTasks.CompletedStages(save, _metal));
            Assert.AreEqual(0, WorldTasks.CompletedStages(save, _chem), "the Chemistry gate is untouched");
            Assert.AreEqual(0, save.worldTaskStage, "the gate's own fields are untouched");
            Assert.AreEqual(1, save.worldTaskSets.Single(e => e.worldId == WorldGate.Metallurgy).stage);

            WorldTasks.Credit(save, _chem, _carbon.itemId, 4, out _);
            Assert.AreEqual(4, WorldTasks.Delivered(save, _chem, _carbon.itemId));
            Assert.AreEqual(0, WorldTasks.Delivered(save, _metal, _carbon.itemId));
        }

        [Test]
        public void Focus_IsTheUnfinishedSetDeliveredInTheLiveWorld()
        {
            var save = new SaveData();
            Assert.AreSame(_chem, WorldTasks.Focus(save, WorldGate.Physics));
            Assert.AreSame(_metal, WorldTasks.Focus(save, WorldGate.Biology));
            Assert.AreSame(_chem, WorldTasks.Focus(save, WorldGate.Chemistry), "nothing delivered there: the gate");
            WorldTasks.SetStage(save, _metal, 2);
            Assert.IsTrue(WorldTasks.IsComplete(save, _metal));
            Assert.AreSame(_chem, WorldTasks.Focus(save, WorldGate.Biology), "finished sets leave focus");
        }

        [Test]
        public void For_FindsEachWorldsTasks()
        {
            Assert.AreSame(_metal, WorldTasks.For(WorldGate.Metallurgy));
            Assert.AreSame(_chem, WorldTasks.For(WorldGate.Chemistry));
            Assert.IsNull(WorldTasks.For("world_engineering"));
        }

        [Test]
        public void MetallurgyWorld_UnlocksFromTasksDeliveredInBiology()
        {
            WorldTasks.Data = _savedData;
            WorldTasks.All  = null;      // the imported data
            var tasks = WorldTasks.For(WorldGate.Metallurgy);
            Assert.IsNotNull(tasks, "Resources/WorldTaskSets has Metallurgy's tasks: run Import Game Data");
            Assert.AreEqual(WorldGate.Biology, tasks.deliverIn);
            Assert.GreaterOrEqual(tasks.stages.Length, 3);
            var world = Resources.Load<WorldDatabaseSO>("WorldDatabase").allWorlds.Single(w => w != null && w.id == WorldGate.Metallurgy);
            Assert.IsTrue(world.unlockByTasks);
            Assert.IsNotNull(world.tutorialFlow, "Metallurgy has its own tutorial");
            Assert.IsFalse(world.streamTransport, "Metallurgy uses belts and the Demon, not streams");
            var save = new SaveData();
            Assert.IsFalse(WorldService.PrereqsMet(save, world));
            WorldTasks.SetStage(save, tasks, tasks.stages.Length);
            Assert.IsTrue(WorldService.PrereqsMet(save, world));
        }
    }
}
