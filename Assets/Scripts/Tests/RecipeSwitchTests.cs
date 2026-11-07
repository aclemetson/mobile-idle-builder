using NUnit.Framework;
using Unity.Entities;

namespace MobileIdleBuilder.Tests
{
    /// <summary>#341: switching recipe moves outputs the new recipe doesn't make into the inventory.</summary>
    [TestFixture]
    public class RecipeSwitchTests
    {
        private World         _world;
        private EntityManager _em;
        private Entity        _building;
        private Entity        _inventory;

        [SetUp]
        public void SetUp()
        {
            _world     = new World("RecipeSwitchTest");
            _em        = _world.EntityManager;
            _building  = _em.CreateEntity();
            _em.AddBuffer<BuildingOutputSlot>(_building);
            _em.AddBuffer<RecipeOutputSlot>(_building);
            _inventory = _em.CreateEntity();
            _em.AddBuffer<InventorySlot>(_inventory);
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private int Run()
        {
            var inv = _em.GetBuffer<InventorySlot>(_inventory);
            return RecipeSwitch.MoveUnproducedOutputsToInventory(
                _em.GetBuffer<BuildingOutputSlot>(_building), _em.GetBuffer<RecipeOutputSlot>(_building), ref inv);
        }

        [Test]
        public void UnproducedOutput_MovesToInventory_AndLeavesTheBuffer()
        {
            _em.GetBuffer<RecipeOutputSlot>(_building).Add(new RecipeOutputSlot { ItemID = 2, Quantity = 1 });
            _em.GetBuffer<BuildingOutputSlot>(_building).Add(new BuildingOutputSlot { ItemID = 1, Quantity = 3 });

            Assert.AreEqual(3, Run());

            Assert.AreEqual(0, _em.GetBuffer<BuildingOutputSlot>(_building).Length);
            var inv = _em.GetBuffer<InventorySlot>(_inventory);
            Assert.AreEqual(1, inv.Length);
            Assert.AreEqual(1, inv[0].ItemID);
            Assert.AreEqual(3, inv[0].Quantity);
        }

        [Test]
        public void ProducedOutput_StaysInTheBuffer_InventoryUnchanged()
        {
            _em.GetBuffer<RecipeOutputSlot>(_building).Add(new RecipeOutputSlot { ItemID = 1, Quantity = 1 });
            _em.GetBuffer<BuildingOutputSlot>(_building).Add(new BuildingOutputSlot { ItemID = 1, Quantity = 3 });

            Assert.AreEqual(0, Run());

            var outBuf = _em.GetBuffer<BuildingOutputSlot>(_building);
            Assert.AreEqual(1, outBuf.Length);
            Assert.AreEqual(3, outBuf[0].Quantity);
            Assert.AreEqual(0, _em.GetBuffer<InventorySlot>(_inventory).Length);
        }

        [Test]
        public void MixedBuffer_SplitsCorrectly_AndReturnsTheMovedCount()
        {
            _em.GetBuffer<RecipeOutputSlot>(_building).Add(new RecipeOutputSlot { ItemID = 2, Quantity = 1 });
            var outBuf = _em.GetBuffer<BuildingOutputSlot>(_building);
            outBuf.Add(new BuildingOutputSlot { ItemID = 1, Quantity = 2 });   // moves
            outBuf.Add(new BuildingOutputSlot { ItemID = 2, Quantity = 5 });   // stays
            outBuf.Add(new BuildingOutputSlot { ItemID = 3, Quantity = 4 });   // moves

            Assert.AreEqual(6, Run());

            outBuf = _em.GetBuffer<BuildingOutputSlot>(_building);
            Assert.AreEqual(1, outBuf.Length);
            Assert.AreEqual(2, outBuf[0].ItemID);
            Assert.AreEqual(5, outBuf[0].Quantity);
            var inv = _em.GetBuffer<InventorySlot>(_inventory);
            Assert.AreEqual(2, inv.Length);
            Assert.AreEqual(2, SlotBufferUtils.CountInInventory(inv, 1));
            Assert.AreEqual(4, SlotBufferUtils.CountInInventory(inv, 3));
        }

        [Test]
        public void MovedItem_AddsToTheExistingInventorySlot()
        {
            _em.GetBuffer<InventorySlot>(_inventory).Add(new InventorySlot { ItemID = 1, Quantity = 10 });
            _em.GetBuffer<BuildingOutputSlot>(_building).Add(new BuildingOutputSlot { ItemID = 1, Quantity = 3 });

            Assert.AreEqual(3, Run());

            var inv = _em.GetBuffer<InventorySlot>(_inventory);
            Assert.AreEqual(1, inv.Length);
            Assert.AreEqual(13, inv[0].Quantity);
        }
    }
}
