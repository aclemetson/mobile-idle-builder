using NUnit.Framework;
using Unity.Core;
using Unity.Entities;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// The creation buffer (CraftStagingSlot, #268): ProductionSystem gathers a craft's inputs there up to the
    /// recipe quantities, so a recipe needing more than the input's per-item cap still crafts and the
    /// input queues the next craft meanwhile.
    /// </summary>
    [TestFixture]
    public class CraftStagingTests
    {
        const int Proton = 4, Electron = 3, Neutron = 11, Uranium = 90;
        const int Cap = 30;

        World _world;
        EntityManager _em;
        Entity _player, _building;

        [SetUp]
        public void SetUp()
        {
            _world = new World("CraftStagingTest");
            _em    = _world.EntityManager;
            var sim = _world.CreateSystemManaged<SimulationSystemGroup>();
            sim.AddSystemToUpdateList(_world.CreateSystem<ProductionSystem>());
            sim.SortSystems();

            _player = _em.CreateEntity();
            _em.AddComponent<PlayerInventoryTag>(_player);
            _em.AddBuffer<InventorySlot>(_player);

            _building = _em.CreateEntity();
            _em.AddComponentData(_building, new BuildingData { IsActive = true, ProductionSpeed = 1f });
            _em.AddBuffer<RecipeInputSlot>(_building);
            _em.AddBuffer<RecipeOutputSlot>(_building).Add(new RecipeOutputSlot { ItemID = Uranium, Quantity = 1 });
            _em.AddBuffer<BuildingInputSlot>(_building);
            _em.AddBuffer<BuildingOutputSlot>(_building);
            _em.AddBuffer<CraftStagingSlot>(_building);
            _em.AddComponentData(_building, new BuildingInventoryConfig { OutputCapacity = 20, InputCapacity = Cap });
        }

        [TearDown]
        public void TearDown() { if (_world.IsCreated) _world.Dispose(); }

        void SetRecipe(float craftTime, params (int id, int qty)[] inputs)
        {
            _em.AddComponentData(_building, new RecipeProcessData { RecipeID = 1, CraftTime = craftTime });
            var buf = _em.GetBuffer<RecipeInputSlot>(_building);
            buf.Clear();
            foreach (var (id, qty) in inputs) buf.Add(new RecipeInputSlot { ItemID = id, Quantity = qty });
        }

        void Tick(float dt) { _world.SetTime(new TimeData(0, dt)); _world.Update(); }
        void Feed(int id, int qty) => SlotBufferUtils.AddToInputBuffer(_em.GetBuffer<BuildingInputSlot>(_building), id, qty);
        int InInput(int id) => SlotBufferUtils.CountInInputBuffer(_em.GetBuffer<BuildingInputSlot>(_building), id);
        int Staged(int id)  => CraftStaging.Count(_em.GetBuffer<CraftStagingSlot>(_building), id);
        RecipeProcessData Process => _em.GetComponentData<RecipeProcessData>(_building);
        int Made => SlotBufferUtils.CountInOutputBuffer(_em.GetBuffer<BuildingOutputSlot>(_building), Uranium);

        [Test]
        public void RecipeBiggerThanTheInputCap_GathersInTheCreationBuffer_AndCrafts()
        {
            SetRecipe(craftTime: 100f, (Neutron, 146));
            for (int refill = 0; refill < 4; refill++)
            {
                Feed(Neutron, Cap);   // a belt topping the input up to its cap
                Tick(0.01f);
                Assert.IsFalse(Process.IsCrafting, $"not ready after {Cap * (refill + 1)} of 146");
            }
            Assert.AreEqual(120, Staged(Neutron));
            Assert.AreEqual(0, InInput(Neutron), "the creation buffer drains the input so the belt can refill it");

            Feed(Neutron, Cap);
            Tick(0.01f);

            Assert.AreEqual(146, Staged(Neutron), "the creation buffer holds exactly one craft");
            Assert.AreEqual(4, InInput(Neutron), "the rest waits in the input for the next craft");
            Assert.IsTrue(Process.IsCrafting);
        }

        [Test]
        public void CreationBuffer_NeverTakesMoreThanTheRecipeQuantity()
        {
            SetRecipe(craftTime: 100f, (Proton, 2));
            Feed(Proton, 25);

            Tick(0.01f);

            Assert.AreEqual(2, Staged(Proton));
            Assert.AreEqual(23, InInput(Proton));
        }

        [Test]
        public void WhileCrafting_TheInputKeepsQueuingForTheNextCraft()
        {
            SetRecipe(craftTime: 100f, (Proton, 2));
            Feed(Proton, 2);
            Tick(0.01f);
            Assert.IsTrue(Process.IsCrafting);

            Feed(Proton, 10);
            Tick(0.01f);

            Assert.AreEqual(2, Staged(Proton), "the running craft's components stay put");
            Assert.AreEqual(10, InInput(Proton), "new arrivals queue in the input");
        }

        [Test]
        public void FinishedCraft_ConsumesTheCreationBuffer_ThenItRefillsFromTheInput()
        {
            SetRecipe(craftTime: 1f, (Proton, 2));
            Feed(Proton, 5);
            Tick(0.01f);        // stage 2, input 3
            Tick(1f);           // craft completes

            Assert.AreEqual(1, Made);
            Assert.AreEqual(0, Staged(Proton), "the finished craft used its components");
            Assert.AreEqual(3, InInput(Proton));

            Tick(0.01f);
            Assert.AreEqual(2, Staged(Proton), "the next craft gathers from the queue");
            Assert.AreEqual(1, InInput(Proton));
            Assert.IsTrue(Process.IsCrafting);
        }

        [Test]
        public void UraniumLikeRecipe_WaitsForEveryInput()
        {
            SetRecipe(craftTime: 100f, (Proton, 92), (Neutron, 146), (Electron, 92));
            for (int i = 0; i < 5; i++) { Feed(Proton, Cap); Feed(Neutron, Cap); Tick(0.01f); }

            Assert.AreEqual(92, Staged(Proton));
            Assert.AreEqual(146, Staged(Neutron));
            Assert.IsFalse(Process.IsCrafting, "no electrons yet");

            for (int i = 0; i < 4; i++) { Feed(Electron, Cap); Tick(0.01f); }

            Assert.AreEqual(92, Staged(Electron));
            Assert.IsTrue(Process.IsCrafting);
        }

        [Test]
        public void BothBuffersEmpty_StillCraftsFromTheGlobalInventory()
        {
            SetRecipe(craftTime: 1f, (Proton, 2));
            var inv = _em.GetBuffer<InventorySlot>(_player);
            SlotBufferUtils.AddToInventory(ref inv, Proton, 2);

            Tick(1f);

            Assert.AreEqual(1, Made);
            Assert.AreEqual(0, SlotBufferUtils.CountInInventory(_em.GetBuffer<InventorySlot>(_player), Proton));
        }

        [Test]
        public void ReturnToInput_EmptiesTheCreationBufferIntoTheInput()
        {
            SetRecipe(craftTime: 100f, (Proton, 2), (Neutron, 146));
            Feed(Proton, 2);
            Feed(Neutron, 30);
            Tick(0.01f);

            CraftStaging.ReturnToInput(_em.GetBuffer<CraftStagingSlot>(_building), _em.GetBuffer<BuildingInputSlot>(_building));

            Assert.AreEqual(0, _em.GetBuffer<CraftStagingSlot>(_building).Length);
            Assert.AreEqual(2, InInput(Proton));
            Assert.AreEqual(30, InInput(Neutron));
        }
    }
}
